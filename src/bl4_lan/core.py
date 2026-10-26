"""Narrow native travel adapter. No SDK dependency in this module."""
from ipaddress import IPv4Address

_MISSING = object()
PREPARE_CHARACTER = "Сначала нажмите «Продолжить» в игре и загрузите персонажа в свой игровой мир. Затем создайте хост или подключитесь к другу."
WAIT_CHARACTER = "Персонаж ещё загружается. Дождитесь игрового мира и интерфейса, затем повторите действие."

def player_world_readiness(pc):
    """Frontend controllers are valid UObjects but cannot safely enter LAN."""
    if pc is None:
        return False, PREPARE_CHARACTER
    try:
        if pc.Pawn is None:
            return False, PREPARE_CHARACTER
        state = getattr(pc, "PlayerState", _MISSING)
        if state is None:
            return False, WAIT_CHARACTER
        if state is not _MISSING:
            guid = getattr(state, "ActiveCharGuid", _MISSING)
            if guid is not _MISSING and not any(int(getattr(guid, key)) for key in ("A", "B", "C", "D")):
                return False, WAIT_CHARACTER
            realtime = getattr(state, "bIsInRealtimeGameplay", _MISSING)
            if realtime is not _MISSING and not bool(realtime):
                return False, WAIT_CHARACTER
        get_target = getattr(pc, "GetViewTarget", None)
        if callable(get_target):
            target = get_target()
            target_class = str(getattr(getattr(target, "Class", None), "Name", ""))
            if "mainmenustation" in target_class.lower():
                return False, PREPARE_CHARACTER
    except (AttributeError, TypeError, ValueError, RuntimeError):
        return False, WAIT_CHARACTER
    return True, ""

def parse_endpoint(value: str) -> str:
    if not isinstance(value, str):
        raise ValueError("Укажите IPv4-адрес хоста.")
    address, separator, raw_port = value.strip().partition(":")
    try:
        ip = IPv4Address(address)
        port = int(raw_port) if separator else 7777
    except (ValueError, TypeError) as error:
        raise ValueError("Адрес должен иметь вид 192.168.1.5 или 192.168.1.5:7777.") from error
    if ip.is_unspecified or ip.is_multicast or ip == IPv4Address("255.255.255.255") or not 1024 <= port <= 65535:
        raise ValueError("Укажите адрес одного компьютера и порт от 1024 до 65535.")
    return f"{ip}:{port}"

class Session:
    def __init__(self, engine, get_pc, gameplay, transport, resolve_map=None):
        self.engine = engine
        self.get_pc = get_pc
        self.gameplay = gameplay
        self.transport = transport
        self.resolve_map = resolve_map
        self.original_passthrough = None
        self.active = False

    def _use_lan(self):
        if not self.active:
            self.original_passthrough = bool(self.transport.bIsPassthrough)
        self.transport.bIsPassthrough = True
        self.active = True

    def restore(self):
        if self.active:
            self.transport.bIsPassthrough = self.original_passthrough
        self.active = False
        self.original_passthrough = None

    def _travel(self, pc, level, options):
        self._use_lan()
        try:
            self.gameplay.OpenLevel(pc, level, True, options)
        except Exception:
            self.restore()
            raise
        return {"status": "travel_requested", "connection_verified": False, "target": level}

    def host(self):
        pc = self.get_pc()
        ready, hint = player_world_readiness(pc)
        if not ready:
            raise ValueError(hint)
        level = self.resolve_map(pc) if self.resolve_map else self.gameplay.GetCurrentLevelName(pc, True)
        if not level or any(x in level.lower() for x in ("frontend", "mainmenu")):
            raise ValueError("Создать хост можно после загрузки игровой локации.")
        return self._travel(pc, level, "listen?bIsLanMatch=1")

    def join(self, address):
        endpoint = parse_endpoint(address)
        pc = self.get_pc()
        ready, hint = player_world_readiness(pc)
        if not ready:
            raise ValueError(hint)
        return self._travel(pc, endpoint, "")
