"""BL4 LAN by skykindness — native LAN/VPN session controls."""
import json
import os
import time
import traceback
import uuid
import unrealsdk
from mods_base import ENGINE, MODS_DIR, ButtonOption, CoopSupport, Game, build_mod, get_pc, keybind
from unrealsdk.commands import add_command, remove_command
from unrealsdk.hooks import Type, add_hook, remove_hook
from .core import Session, parse_endpoint
from .runtime import Tracker, startup_enabled
from .fileio import read_request, write_json

__version__ = "0.2.0"
DATA = MODS_DIR.parent / "BL4-LAN-Data"
DATA.mkdir(exist_ok=True)
INSTANCE = uuid.uuid4().hex
PID = os.getpid()
FAILURE_HOOKS = ("/Script/Engine.GameInstance:HandleTravelError", "/Script/Engine.GameInstance:HandleNetworkError")
POLL_HOOKS = ("/Script/Engine.CameraModifier:BlueprintModifyCamera", "/Script/GbxUIUMG.GbxUIUMGTickWidget:BP_TickWidget",
              "/Script/Engine.PlayerController:ClientRestart")
tracker = Tracker()
session = None
request_id = None
last_poll = 0.0
last_write = 0.0
last_event = None
busy = False

def report(data, force=False):
    global last_write, last_event
    now = time.monotonic()
    event = (data.get("status"), data.get("message"), data.get("player_count"), request_id)
    if not force and event == last_event and now - last_write < 1.0:
        return
    payload = dict(data, pid=PID, instance=INSTANCE, timestamp=time.time(), version=__version__, request_id=request_id)
    write_json(DATA / f"status-{PID}.json", payload)
    write_json(DATA / "status.json", payload)
    if event != last_event:
        print("[BL4 LAN] " + payload.get("message", payload["status"]))
        with (DATA / "events.log").open("a", encoding="utf-8") as log:
            log.write(json.dumps(payload, ensure_ascii=False) + "\n")
    last_event = event
    last_write = now

def record_exception():
    (DATA / "last-error.txt").write_text(traceback.format_exc(), encoding="utf-8")

def map_package(pc):
    obj = pc
    while obj is not None:
        if obj.Class.Name == "Package" and str(obj.Name).startswith("/"):
            return str(obj.Name)
        obj = obj.Outer
    raise RuntimeError("Не удалось определить текущую локацию. Загрузите персонажа в мир.")

def inspect_state():
    pc = get_pc()
    empty = dict(level="", pawn_present=False, native_driver=None, passthrough=False,
                 server_connection_present=False, remote_connection_count=0, player_count=0)
    if pc is None or session is None:
        return empty
    world = pc.Outer
    world_cls = unrealsdk.find_class("World")
    while world is not None and not world.Class._inherits(world_cls):
        world = world.Outer
    game_state = session.gameplay.GetGameState(pc)
    driver = world.NetDriver if world else None
    return dict(level=session.gameplay.GetCurrentLevelName(pc, True), pawn_present=pc.Pawn is not None,
                native_driver=str(driver) if driver else None,
                passthrough=bool(getattr(driver,"bIsPassthrough",False)) if driver else False,
                server_connection_present=driver.ServerConnection is not None if driver else False,
                remote_connection_count=len(driver.ClientConnections) if driver else 0,
                player_count=len(game_state.PlayerArray) if game_state else 0)

def current_state():
    if not mod.is_enabled:
        return dict(tracker.current)
    return tracker.observe(inspect_state(), now=time.monotonic())

def run(action, address=None):
    try:
        if action == "status":
            report(current_state(), True)
            return
        if action not in ("host", "join"):
            raise ValueError("Доступны: bl4lan host, bl4lan join IPv4[:порт], bl4lan status.")
        if not mod.is_enabled:
            mod.enable()
        if session is None:
            raise RuntimeError("Не удалось включить сеть игры. Посмотрите сообщение в лаунчере.")
        if tracker.pending:
            state = current_state()
            if tracker.pending:
                report(state, True)
                return
        if action == "join":
            if address is None:
                try:
                    config = json.loads((DATA / "connection.json").read_text(encoding="utf-8-sig"))
                    address = config.get("address", "")
                except FileNotFoundError:
                    address = ""
            if not address:
                raise ValueError("Адрес друга не указан. В лаунчере выберите «Подключиться» и введите его LAN/VPN-адрес. Если вы хост, используйте «Создать хост» или F6.")
            address = parse_endpoint(address)
        elif tracker.role == "host":
            state = current_state()
            if state.get("status") in ("hosting", "connected"):
                report(state, True)
                return
        report(tracker.start(action, address or "", now=time.monotonic()), True)
        if action == "host":
            session.host()
        else:
            session.join(address)
    except Exception as error:
        report(tracker.fail(str(error), "request"), True)
        record_exception()

def console(line, length):
    parts = line[length:].strip().split()
    if len(parts) > 2:
        report(tracker.fail("Слишком много аргументов. Укажите один адрес IPv4[:порт].", "address"), True)
        return
    run(parts[0] if parts else "status", parts[1] if len(parts) == 2 else None)

def native_failure(obj, args, ret, func):
    if session is None or not session.active:
        return
    session.restore()
    detail = str(args)
    if "Timeout" in detail:
        message = "Хост не ответил вовремя. Проверьте адрес, LAN/VPN и готовность хоста; повторите подключение."
    elif "ConnectionLost" in detail:
        message = "Соединение с другом потеряно. Можно подключиться ещё раз."
    elif "Listen" in detail:
        message = "Не удалось создать хост. Закройте другую запущенную копию игры и повторите."
    else:
        message = "Игра отклонила подключение или загрузку локации. Проверьте одинаковую сборку игры и готовность хоста."
    report(dict(tracker.fail(message,"native"), error_detail=detail), True)
    (DATA / "last-error.txt").write_text(detail, encoding="utf-8")

def on_enable():
    global session
    try:
        if session is None:
            session = Session(ENGINE, get_pc, unrealsdk.find_class("GameplayStatics").ClassDefaultObject,
                              unrealsdk.find_class("ShiftNetDriver").ClassDefaultObject, map_package)
        add_command("bl4lan", console)
        for path in FAILURE_HOOKS:
            add_hook(path, Type.POST, "bl4_lan_failure", native_failure)
        tracker.error = None
        report(tracker._state("ready", "BL4 LAN включён. Создайте хост или подключитесь через лаунчер."), True)
    except Exception as error:
        report(tracker.fail("Не удалось загрузить BL4 LAN: " + str(error),"startup"), True)
        record_exception()

def on_disable():
    remove_command("bl4lan")
    for path in FAILURE_HOOKS:
        remove_hook(path, Type.POST, "bl4_lan_failure")
    if session:
        session.restore()
    report(tracker.disable(), True)

def poll(obj, args, ret, func):
    global last_poll, request_id, busy
    now = time.monotonic()
    if busy or now - last_poll < .25:
        return
    last_poll = now
    busy = True
    try:
        request = read_request(DATA / f"request-{PID}.json", INSTANCE, time.time())
        if request and request["id"] != request_id:
            request_id = request["id"]
            run(request["action"], request.get("address"))
        try:
            report(current_state())
        except (AttributeError, ValueError, IndexError):
            report(dict(tracker.current))
    except Exception:
        record_exception()
    finally:
        busy = False

host_key = keybind("Создать LAN-хост", "F6", lambda: run("host"))
join_key = keybind("Войти к другу", "F7", lambda: run("join"))
state_key = keybind("Проверить состояние LAN", "F8", lambda: run("status"))
settings_file = MODS_DIR / "settings" / "bl4_lan.json"
try:
    enable_on_start = startup_enabled(settings_file)
except (OSError, ValueError):
    enable_on_start = False
    record_exception()
mod = build_mod(name="BL4 LAN", author="skykindness", version=__version__,
                description="LAN/VPN кооператив. Управление и сообщения — в лаунчере BL4 LAN. F6: хост из игровой локации. F7: вход по сохранённому адресу. F8: состояние. by skykindness",
                supported_games=Game.BL4, coop_support=CoopSupport.RequiresAllPlayers, auto_enable=True,
                settings_file=settings_file, keybinds=[host_key,join_key,state_key],
                options=[ButtonOption("Создать хост (перезагрузить локацию)",on_press=lambda _:run("host")),
                         ButtonOption("Подключиться по адресу лаунчера",on_press=lambda _:run("join")),
                         ButtonOption("Показать состояние",on_press=lambda _:run("status"))],
                on_enable=on_enable,on_disable=on_disable)
if enable_on_start:
    mod.enable()
else:
    report(tracker.disable(), True)
for path in POLL_HOOKS:
    add_hook(path, Type.POST, "bl4_lan_launcher", poll)

