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
from .core import Session, parse_endpoint, player_world_readiness
from .runtime import BuddyReconciler, Tracker, startup_enabled
from .fileio import read_request, write_json

__version__ = "0.2.6"
DATA = MODS_DIR.parent / "BL4-LAN-Data"
try:
    DATA.mkdir(exist_ok=True)
except OSError:
    pass  # Local evidence storage is optional; native controls must still load.
INSTANCE = uuid.uuid4().hex
PID = os.getpid()
FAILURE_HOOKS = ("/Script/Engine.GameInstance:HandleTravelError", "/Script/Engine.GameInstance:HandleNetworkError")
POLL_HOOKS = ("/Script/Engine.CameraModifier:BlueprintModifyCamera", "/Script/GbxUIUMG.GbxUIUMGTickWidget:BP_TickWidget",
              "/Script/Engine.PlayerController:ClientRestart")
LIFECYCLE_HOOKS = tuple("/Script/Engine.PlayerController:" + name for name in (
    "ClientTravel", "ClientTravelInternal", "ClientWasKicked", "ClientReturnToMainMenuWithTextReason")) + tuple(
    "/Script/OakGame.OakPlayerController:" + name for name in (
        "ClientGameHostDisconnecting", "ClientEntitlementDisconnect", "ClientForceCharacterSelectionAfterCountdown"))
registered_lifecycle_hooks = set()
tracker = Tracker()
buddy_reconciler = BuddyReconciler()
buddy_context = None
buddy_next_scan = 0.0
buddy_signature_valid = None
buddy_reported_errors = set()
session = None
request_id = None
last_poll = 0.0
last_write = 0.0
last_event = None
last_announcement = None
busy = False
attempt_id = None
action_source = None
EVENT_LOG_LIMIT = 4 * 1024 * 1024

def append_event(payload):
    """Keep two bounded local logs; locked/unwritable logs may drop evidence."""
    try:
        line = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")
        if len(line) > EVENT_LOG_LIMIT:
            return
        path = DATA / "events.log"
        try:
            size = path.stat().st_size
        except FileNotFoundError:
            size = 0
        if size + len(line) > EVENT_LOG_LIMIT:
            if size > EVENT_LOG_LIMIT:
                # Upgrade an unbounded legacy log using only its recent tail.
                with path.open("r+b") as log:
                    log.seek(-EVENT_LOG_LIMIT, os.SEEK_END)
                    tail = log.read(EVENT_LOG_LIMIT).partition(b"\n")[2]
                    log.seek(0)
                    log.write(tail)
                    log.truncate()
            os.replace(path, DATA / "events.previous.log")
        with path.open("ab") as log:
            log.write(line)
    except (OSError, TypeError, ValueError):
        pass  # Evidence collection must never change a native travel result.

def diagnostic(event, **details):
    """Local report evidence, optionally shared by the player; never commands."""
    payload = dict(event=event, pid=PID, instance=INSTANCE, timestamp=time.time(),
                   version=__version__, request_id=request_id, attempt_id=attempt_id,
                   action_source=action_source, **details)
    append_event(payload)

def report(data, force=False):
    global last_write, last_event, last_announcement
    now = time.monotonic()
    event = tuple(data.get(key) for key in ("status", "message", "player_count", "level", "pawn_present",
                  "hud_present", "player_state_present", "native_driver", "server_connection_present", "remote_connection_count",
                  "pc_present", "snapshot_available", "player_world_ready", "camera_target_class")) + (request_id,)
    if not force and event == last_event and now - last_write < 1.0:
        return
    payload = dict(data, pid=PID, instance=INSTANCE, timestamp=time.time(), version=__version__, request_id=request_id,
                   attempt_id=attempt_id, action_source=action_source)
    for path in (DATA / f"status-{PID}.json", DATA / "status.json"):
        try:
            write_json(path, payload)
        except (OSError, TypeError, ValueError):
            pass  # An unwritable report must not prevent the native travel.
    if event != last_event:
        announcement = (data.get("status"), data.get("message"))
        if announcement != last_announcement:
            try:
                print("[BL4 LAN] " + payload.get("message", payload["status"]))
            except (OSError, ValueError):
                pass
            last_announcement = announcement
        append_event(dict(payload, event="state"))
    last_event = event
    last_write = now

def record_exception():
    try:
        (DATA / "last-error.txt").write_text(traceback.format_exc()[-16384:], encoding="utf-8")
    except OSError:
        pass

def evidence_text(value, limit=512):
    """UObject/struct formatting can fail while native travel destroys objects."""
    try:
        return str(value)[:limit]
    except Exception:
        return "<unavailable>"

def optional_value(obj, name, convert=None):
    try:
        value = getattr(obj, name)
        if value is None:
            return None
        return convert(value) if convert else value
    except Exception:
        return None

def optional_present(obj, name, call=False):
    """Distinguish an observed null object from an unavailable native field."""
    try:
        value = getattr(obj, name)
        return (value() if call else value) is not None
    except Exception:
        return None

def buddy_diagnostic(event, **details):
    """Optional evidence must not alter a completed native UI registration."""
    try:
        diagnostic(event, **details)
    except Exception:
        pass


def buddy_error_once(stage, error, peer=None):
    key = (buddy_context, stage, peer)
    if key in buddy_reported_errors:
        return
    buddy_reported_errors.add(key)
    buddy_diagnostic("buddy_ui_error", stage=stage, peer=peer,
                     context=buddy_context, error=evidence_text(error, 512))


def buddy_path(obj):
    """Require a native identity path; formatted object text is not an ID."""
    path = obj._path_name()
    if not isinstance(path, str) or not path.startswith("/"):
        raise ValueError("Native buddy identity path is unavailable")
    return path


def validate_buddy_signature():
    function = unrealsdk.find_object("Function", "/Script/OakGame.OakUIDataCollector_BuddyInfo:AddBuddy")
    params = list(function._properties())
    expected = (("Character", "/Script/OakGame.OakCharacter"),
                ("PlayerState", "/Script/Engine.PlayerState"))
    if len(params) != len(expected) or any(
        str(prop.Name) != name or buddy_path(prop.PropertyClass) != class_path
        for prop, (name, class_path) in zip(params, expected)
    ):
        raise ValueError("Unsupported native AddBuddy signature")


def sync_buddy_ui(observed, now=None):
    """Settle valid remote guests, then initialize their existing native HUD.

    Run only on the game-thread poll. The policy retains string keys, never
    native objects. A failed native call is terminal for that player state in
    this controller/world/collector context because it may have partly added UI.
    """
    global buddy_context, buddy_next_scan, buddy_signature_valid
    now = time.monotonic() if now is None else now
    try:
        if not (mod.is_enabled and tracker.role == "guest"
                and observed.get("snapshot_available") is True
                and observed.get("server_connection_present") is True
                and observed.get("pawn_present") is True
                and observed.get("player_count", 0) >= 2):
            buddy_reconciler.reconcile(None, None, None, now)
            return
        pc = get_pc()
        if pc is None or pc.Pawn is None or pc.PlayerState is None:
            buddy_reconciler.reconcile(None, None, None, now)
            return
        if now < buddy_next_scan:
            return
        buddy_next_scan = now + 1.0
        game_state = session.gameplay.GetGameState(pc)
        if game_state is None or len(game_state.PlayerArray) < 2:
            buddy_reconciler.reconcile(None, None, None, now)
            return
        local_player = pc.Player
        if local_player is None:
            buddy_reconciler.reconcile(None, None, None, now)
            return
        cls = unrealsdk.find_class("OakUIDataCollector_BuddyInfo")
        collectors = [obj for obj in unrealsdk.find_all(cls, False)
                      if not str(obj.Name).startswith("Default__") and obj.Outer == local_player]
        if len(collectors) != 1:
            buddy_reconciler.reconcile(None, None, None, now)
            return
        collector = collectors[0]
        context = (buddy_path(pc), buddy_path(game_state), buddy_path(collector))
        if context != buddy_context:
            buddy_context = context
            buddy_signature_valid = None
            buddy_reported_errors.clear()
        peers = []
        for ps in list(game_state.PlayerArray)[:16]:
            if ps is None or ps == pc.PlayerState:
                continue
            key = buddy_path(ps)
            pawn = optional_value(ps, "GetPawn")
            try:
                pawn = pawn() if pawn else None
                index = ps.BuddyIndex
                ready = (pawn is not None and pawn != pc.Pawn and pawn.PlayerState == ps
                         and isinstance(index, int) and not isinstance(index, bool) and 0 <= index <= 3)
            except Exception:
                pawn, ready = None, False
            # These current-tick rows/closure are discarded after reconciliation.
            peers.append(dict(key=key, ready=ready, pawn=pawn, player_state=ps,
                              player_id=optional_value(ps, "PlayerId", int)))
        if any(peer["ready"] for peer in peers) and buddy_signature_valid is None:
            try:
                validate_buddy_signature()
            except Exception as error:
                buddy_signature_valid = False
                buddy_error_once("signature", error)
            else:
                buddy_signature_valid = True
        if buddy_signature_valid is not True:
            buddy_reconciler.reconcile(context, None, None, now)
            return

        def add_buddy(peer):
            collector.AddBuddy(peer["pawn"], peer["player_state"])
            buddy_diagnostic("buddy_ui_registered", context=context, peer=peer["key"],
                             player_id=peer["player_id"])

        result = buddy_reconciler.reconcile(context, peers, add_buddy, now)
        for error in result["errors"]:
            buddy_error_once("add", error["error"], error["key"])
    except Exception as error:
        # Optional SDK APIs and stale travel objects cannot break the session.
        buddy_reconciler.reconcile(None, None, None, now)
        buddy_error_once("scan", error)


def native_details(pc, game_state, driver):
    """Read a small allowlist on the existing game-thread poll, never mutate it."""
    connection = optional_value(driver, "ServerConnection")
    clients = optional_value(driver, "ClientConnections")
    players = optional_value(game_state, "PlayerArray")
    player_ids, player_groups, client_states = [], [], []
    try:
        for i in range(min(len(players), 16)):
            player_id = optional_value(players[i], "PlayerId", int)
            if player_id is not None:
                player_ids.append(player_id)
            player_groups.append(dict(player_id=player_id,
                                      player_group_state_present=optional_present(players[i], "PlayerGroupState")))
    except Exception:
        pass
    try:
        for i in range(min(len(clients), 16)):
            client_states.append(optional_value(clients[i], "State", evidence_text))
    except Exception:
        pass
    instance = optional_value(ENGINE, "GameInstance")
    online = optional_value(instance, "OnlineSession")
    shaders = {}
    try:
        library = unrealsdk.find_class("KismetSystemLibrary").ClassDefaultObject
    except Exception:
        library = None
    for name in ("r.ShaderPipelineCache.Enabled", "r.PSOPrecaching"):
        try:
            shaders[name] = int(library.GetConsoleVariableIntValue(name))
        except Exception:
            shaders[name] = None
    try:
        target = pc.GetViewTarget()
        target_class = evidence_text(target.Class.Name) if target else None
        target_is_pawn = target is not None and target == pc.Pawn
    except Exception:
        target_class = target_is_pawn = None
    return dict(travel_status=optional_value(pc, "TravelStatus", evidence_text),
                camera_target_class=target_class, camera_target_is_pawn=target_is_pawn,
                realtime_gameplay=optional_value(optional_value(pc,"PlayerState"),"bIsInRealtimeGameplay",bool),
                player_ids=player_ids if players is not None else None,
                player_group_present=optional_present(pc, "GetPlayerGroup", call=True),
                player_group_state_present=optional_present(pc, "GetPlayerGroupState", call=True),
                hud_icon_total_count=optional_value(pc, "HUDIconTotalCount", int),
                player_group_states=player_groups if players is not None else None,
                server_connection_state=optional_value(connection, "State", evidence_text),
                client_connection_states=client_states if clients is not None else None,
                handling_disconnect=optional_value(online, "bHandlingDisconnect", bool),
                shader_console_values=shaders)

def native_reason(args):
    result = {}
    for name in ("FailureType", "ErrorString", "Reason", "reason", "ReturnReason", "TextReason",
                 "MissingEntitlement", "DLCIdent"):
        value = optional_value(args, name, evidence_text)
        if value is not None:
            result[name] = value
    return result

def map_package(pc):
    obj = pc
    while obj is not None:
        if obj.Class.Name == "Package" and str(obj.Name).startswith("/"):
            return str(obj.Name)
        obj = obj.Outer
    raise RuntimeError("Не удалось определить текущую локацию. Загрузите персонажа в мир.")

def inspect_state():
    # Nulls represent unavailable evidence, not observed destruction/disconnection.
    empty = dict(pc_present=None, snapshot_available=False, level=None, pawn_present=None,
                 player_world_ready=False, player_world_hint=player_world_readiness(None)[1],
                 native_driver=None, passthrough=None, server_connection_present=None,
                 remote_connection_count=None, player_count=None)
    try:
        pc = get_pc()
        empty["pc_present"] = pc is not None
        if pc is None or session is None:
            return empty
        world = pc.Outer
        world_cls = unrealsdk.find_class("World")
        for _ in range(32):
            if world is None or world.Class._inherits(world_cls):
                break
            world = world.Outer
        else:
            return dict(empty, snapshot_error="WorldOuterLimit")
        if world is None:
            return empty
        game_state = session.gameplay.GetGameState(pc)
        driver = world.NetDriver
        observed = dict(pc_present=True, snapshot_available=True,
                level=evidence_text(session.gameplay.GetCurrentLevelName(pc, True)), pawn_present=pc.Pawn is not None,
                controller_class=evidence_text(getattr(getattr(pc,"Class",None),"Name","")),
                player_state_present=getattr(pc,"PlayerState",None) is not None,
                hud_present=getattr(pc,"MyHUD",None) is not None,
                native_driver=evidence_text(driver) if driver else None,
                passthrough=bool(getattr(driver,"bIsPassthrough",False)) if driver else False,
                server_connection_present=driver.ServerConnection is not None if driver else False,
                remote_connection_count=len(driver.ClientConnections) if driver else 0,
                player_count=len(game_state.PlayerArray) if game_state else 0)
        observed["player_world_ready"], observed["player_world_hint"] = player_world_readiness(pc)
    except Exception as error:
        return dict(empty, snapshot_error=type(error).__name__[:80])
    # Optional reflection differences must not erase the core world observation.
    try:
        observed.update(native_details(pc, game_state, driver))
    except Exception:
        pass
    return observed

def current_state():
    if not mod.is_enabled:
        return dict(tracker.current)
    return tracker.observe(inspect_state(), now=time.monotonic())

def run(action, address=None, source="console"):
    global attempt_id, action_source
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
                diagnostic("action_ignored", reason="attempt_pending", action=action, source=source)
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
            state = current_state()
            # Repeating join after success used to OpenLevel again and tear down
            # the live session. Loading/respawning must not trigger another travel.
            if state.get("status") in ("connected", "loading") or (state.get("server_connection_present") and state.get("pawn_present") and state.get("player_count",0) >= 2):
                report(state, True)
                diagnostic("action_ignored", reason="session_active", action=action, source=source)
                return
        elif tracker.role == "host":
            state = current_state()
            if state.get("status") in ("hosting", "connected"):
                report(state, True)
                return
        ready, hint = player_world_readiness(get_pc())
        if not ready:
            raise ValueError(hint)
        attempt_id = uuid.uuid4().hex
        action_source = source
        report(tracker.start(action, address or "", now=time.monotonic()), True)
        if action == "host":
            session.host()
        else:
            session.join(address)
        diagnostic("travel_dispatched", action=action, target=address or "current_map")
    except Exception as error:
        tracker.fail(str(error), "request")
        if action in ("host", "join"):
            # Validation may fail before a native attempt exists. Keep evidence
            # identity across status polls without starting or changing travel.
            tracker.current.update(requested_action=action, validation_attempt_id=uuid.uuid4().hex)
        report(dict(tracker.current), True)
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
    detail = evidence_text(args, 4096)
    try:
        observed = inspect_state()
    except Exception:
        observed = None
    diagnostic("native_failure", detail=detail, function=evidence_text(func),
               native_reason=native_reason(args), observed=observed)
    if "Timeout" in detail:
        message = "Хост не ответил вовремя. Проверьте адрес, LAN/VPN и готовность хоста; повторите подключение."
    elif "ConnectionLost" in detail:
        message = "Соединение с другом потеряно. Можно подключиться ещё раз."
    elif "Listen" in detail:
        message = "Не удалось создать хост. Закройте другую запущенную копию игры и повторите."
    else:
        message = "Игра отклонила подключение или загрузку локации. Проверьте одинаковую сборку игры и готовность хоста."
    report(dict(tracker.fail(message,"native"), error_detail=detail), True)
    try:
        (DATA / "last-error.txt").write_text(detail, encoding="utf-8")
    except OSError:
        pass

def lifecycle_event(obj, args, ret, func):
    """Observe native transitions; never call, suppress or replace a game action."""
    if session is None or not session.active:
        return
    try:
        observed = inspect_state()
    except Exception:
        observed = None
    diagnostic("native_lifecycle", function=evidence_text(func), detail=evidence_text(args, 4096),
               native_reason=native_reason(args), observed=observed)

def on_enable():
    global session
    try:
        if session is None:
            session = Session(ENGINE, get_pc, unrealsdk.find_class("GameplayStatics").ClassDefaultObject,
                              unrealsdk.find_class("ShiftNetDriver").ClassDefaultObject, map_package)
        add_command("bl4lan", console)
        for path in FAILURE_HOOKS:
            add_hook(path, Type.POST, "bl4_lan_failure", native_failure)
        for path in LIFECYCLE_HOOKS:
            try:
                if unrealsdk.find_object("Function", path) is None:
                    continue
                add_hook(path, Type.POST, "bl4_lan_lifecycle", lifecycle_event)
                registered_lifecycle_hooks.add(path)
            except (ValueError, AttributeError, RuntimeError):
                # Reflected functions differ by build. Optional observations
                # must never prevent the native LAN adapter from enabling.
                continue
        diagnostic("lifecycle_observers", functions=sorted(registered_lifecycle_hooks))
        tracker.error = None
        report(tracker._state("ready", "BL4 LAN включён. Создайте хост или подключитесь через лаунчер."), True)
    except Exception as error:
        report(tracker.fail("Не удалось загрузить BL4 LAN: " + str(error),"startup"), True)
        record_exception()

def on_disable():
    global buddy_context, buddy_next_scan, buddy_signature_valid
    buddy_reconciler.reset()
    buddy_context = None
    buddy_next_scan = 0.0
    buddy_signature_valid = None
    buddy_reported_errors.clear()
    remove_command("bl4lan")
    for path in FAILURE_HOOKS:
        remove_hook(path, Type.POST, "bl4_lan_failure")
    for path in registered_lifecycle_hooks:
        remove_hook(path, Type.POST, "bl4_lan_lifecycle")
    registered_lifecycle_hooks.clear()
    if session:
        session.restore()
    report(tracker.disable(), True)

def poll(obj, args, ret, func):
    global last_poll, request_id, busy
    if not busy and getattr(func, "Name", "") == "ClientRestart":
        try:
            diagnostic("client_restart", observed=inspect_state())
        except (AttributeError, ValueError, IndexError):
            diagnostic("client_restart", snapshot_unavailable=True)
    now = time.monotonic()
    if busy or now - last_poll < .25:
        return
    last_poll = now
    busy = True
    try:
        request = read_request(DATA / f"request-{PID}.json", INSTANCE, time.time())
        if request and request["id"] != request_id:
            request_id = request["id"]
            run(request["action"], request.get("address"), source="launcher")
        try:
            observed = current_state()
            try:
                sync_buddy_ui(observed, now=now)
            except Exception as error:
                buddy_error_once("poll", error)
            report(observed)
        except (AttributeError, ValueError, IndexError):
            report(dict(tracker.current))
    except Exception:
        record_exception()
    finally:
        busy = False

host_key = keybind("Создать LAN-хост", "F6", lambda: run("host", source="F6"))
join_key = keybind("Войти к другу", "F7", lambda: run("join", source="F7"))
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
                options=[ButtonOption("Создать хост (перезагрузить локацию)",on_press=lambda _:run("host",source="mod_menu")),
                         ButtonOption("Подключиться по адресу лаунчера",on_press=lambda _:run("join",source="mod_menu")),
                         ButtonOption("Показать состояние",on_press=lambda _:run("status"))],
                on_enable=on_enable,on_disable=on_disable)
if enable_on_start:
    mod.enable()
else:
    report(tracker.disable(), True)
for path in POLL_HOOKS:
    add_hook(path, Type.POST, "bl4_lan_launcher", poll)

