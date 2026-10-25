"""Connection lifecycle, independent of game bindings."""
import json
from pathlib import Path

def startup_enabled(settings_file: Path) -> bool:
    try:
        data = json.loads(settings_file.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        return True
    if not isinstance(data, dict) or not isinstance(data.get("enabled", True), bool):
        raise ValueError("Повреждены настройки включения BL4 LAN. Переустановите мод через лаунчер.")
    return data.get("enabled", True)

class Tracker:
    def __init__(self, timeout=45):
        self.timeout = timeout
        self.role = None
        self.target = ""
        self.started = None
        self.pending = False
        self.error = None
        self.was_connected = False
        self.missing_since = None
        self.current = self._state("ready", "Мод готов. Создайте хост или подключитесь через лаунчер.")

    def _state(self, status, message, **extra):
        self.current = dict(status=status, message=message, role=self.role,
                            target=self.target, network_connected=status == "connected", **extra)
        return dict(self.current)

    def start(self, action, target="", *, now):
        self.role = "host" if action == "host" else "guest"
        self.target = target
        self.started = now
        self.pending = True
        self.error = None
        self.was_connected = False
        self.missing_since = None
        return self._state("starting_host" if action == "host" else "connecting",
                           "Создаём хост. Дождитесь перезагрузки локации." if action == "host" else f"Подключаемся к {target}. Ожидаем ответ хоста…")

    def fail(self, message, code="native"):
        self.pending = False
        self.error = dict(status="error", message=message, code=code)
        return self._state(**self.error)

    def disable(self):
        self.role = None
        self.pending = False
        self.error = None
        self.was_connected = False
        self.missing_since = None
        return self._state("disabled", "Мод отключён. Кнопка создания хоста или входа включит его.")

    def observe(self, snapshot, *, now):
        s = dict(snapshot)
        players = s.get("player_count", 0)
        pawn = s.get("pawn_present", False)
        guest_ok = self.role == "guest" and s.get("server_connection_present") and pawn and players >= 2
        host_ok = self.role == "host" and s.get("native_driver") and s.get("passthrough") and not s.get("server_connection_present") and pawn
        # Native errors are terminal for this attempt. Status reads must not erase them.
        if self.error:
            return dict(self.current, **s)
        if guest_ok or (host_ok and s.get("remote_connection_count", 0) >= 1 and players >= 2):
            self.pending = False
            self.was_connected = True
            self.missing_since = None
            result = self._state("connected", f"{'Друг подключился' if self.role == 'host' else 'Подключено к хосту'}. Игроков: {players}.")
        elif host_ok:
            self.pending = False
            self.missing_since = None
            result = self._state("hosting", "Хост создан. Ожидаем друга." if not self.was_connected else "Друг отключился. Хост ожидает подключения.")
        elif self.role == "host" and not self.pending:
            if self.missing_since is None:
                self.missing_since = now
            if now - self.missing_since < 8:
                result = self._state("loading", "Хост временно недоступен. Ожидаем загрузку игрового мира.")
            else:
                self.was_connected = False
                result = self._state("ready", "Хост больше не активен. Загрузите персонажа в мир и создайте хост заново.")
        elif self.was_connected:
            if self.missing_since is None:
                self.missing_since = now
            if s.get("server_connection_present") or now - self.missing_since < self.timeout:
                result = self._state("loading", "Ожидаем загрузку локации и персонажа. Проверяем состояние соединения…")
            else:
                # A missing snapshot alone is not a terminal native network error.
                result = self._state("disconnected", "Соединение с хостом сейчас не подтверждено. Можно повторить вход.", code="disconnected")
        elif self.pending:
            if now - self.started >= self.timeout:
                result = self.fail("Игра не подтвердила соединение за 45 секунд. Проверьте адрес, общую LAN/VPN и готовность хоста; затем повторите вход.", "timeout")
            else:
                result = dict(self.current, elapsed_seconds=int(now - self.started))
        else:
            result = dict(self.current)
        return dict(result, **s)
