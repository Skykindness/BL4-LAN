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


class BuddyReconciler:
    """Settle remote peers, then attempt native registration once per context.

    Context and peer keys are strings supplied by the adapter. Only those keys
    and readiness timestamps are retained; native objects stay in caller rows.
    A callback failure may have partially registered a buddy, so it is terminal
    for that key until a real context change or explicit reset.
    """

    def __init__(self):
        self.reset()

    def reset(self):
        self._context = None
        self._attempted = set()
        self._ready_since = {}

    def reconcile(self, context, peers, add_callback, now):
        """Call add_callback(current_peer_row) after two ready polls and .5s.

        None/empty context and None peers mean an unavailable snapshot. They
        interrupt readiness settling but preserve prior attempt history. Empty
        peer lists also preserve history. Results contain per-call attempted,
        added and errors, plus observed/pending counts and attempted_total.
        """
        result = dict(observed=0, pending=0, attempted=0, added=0,
                      attempted_total=len(self._attempted), errors=[])
        if not context:
            self._ready_since.clear()
            return result
        context = tuple(context)
        if context != self._context:
            self.reset()
            self._context = context
        if peers is None:
            self._ready_since.clear()
            result["attempted_total"] = len(self._attempted)
            return result
        seen = set()
        for peer in peers:
            key = peer["key"]
            if key in seen:
                continue
            seen.add(key)
            if not peer.get("ready", False):
                self._ready_since.pop(key, None)
                continue
            if key in self._attempted:
                continue
            if key not in self._ready_since:
                self._ready_since[key] = now
                continue
            if now - self._ready_since[key] < .5:
                continue
            # Mark BEFORE entering native code, including reentrant callbacks.
            self._attempted.add(key)
            self._ready_since.pop(key, None)
            result["attempted"] += 1
            try:
                add_callback(peer)
            except Exception as error:
                result["errors"].append(dict(key=key,
                    error=f"{type(error).__name__}: {error}"))
            else:
                result["added"] += 1
            if self._context != context:
                break
        for key in self._ready_since.keys() - seen:
            del self._ready_since[key]
        result.update(observed=len(seen), pending=len(self._ready_since),
                      attempted_total=len(self._attempted))
        return result
