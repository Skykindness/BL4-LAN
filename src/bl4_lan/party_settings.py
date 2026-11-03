"""One initial owned-player difficulty handoff; never advances the join screen."""
import math
import re
import struct
import time
from .party_adapter import world_of
from .party_memory import ProcessMemory, require
from .party_probe import AuditIdentity, BudgetReader


class DifficultyBridge:
    CALLBACK = '/Script/OakGame.OakPlayerState:ServerSetPlayerProfileDifficulty'
    MAX_CONTEXTS = 8
    MAX_ATTEMPTS = 32
    CAPTURE_TTL = 2.5
    STABLE_SECONDS = 1.
    POLL_SECONDS = .5

    def __init__(self, sdk, get_pc, get_game_state, get_game_instance, *,
                 memory_factory=ProcessMemory, identity_factory=AuditIdentity,
                 diagnostic=lambda *a, **k: None, selection_observer_ready=False,
                 clock=time.monotonic):
        self.sdk = sdk
        self.get_pc = get_pc
        self.get_gs = get_game_state
        self.get_gi = get_game_instance
        self.memory_factory = memory_factory
        self.identity_factory = identity_factory
        self.diagnostic = diagnostic
        self.clock = clock
        self.memory = None
        self._verification_started = False
        self._observer_ready = bool(selection_observer_ready)
        self._enabled = False
        self._cache = self._arm = self._settle = None
        self._contexts = set()
        self._attempts = set()
        self._errors = set()
        self._next_capture = self._next_sync = 0.
        self._busy = self._sending = False

    def _emit(self, **values):
        try: self.diagnostic('party_difficulty_sync', **values)
        except Exception: pass

    def _error(self, phase, error):
        key = (phase, type(error).__name__)
        if key not in self._errors and len(self._errors) < 8:
            self._errors.add(key)
            self._emit(phase='unavailable', operation=phase, exception_type=key[1])

    @staticmethod
    def _time(value):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value >= 0

    @staticmethod
    def _base_choice(value):
        require(isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= 2,
                'unsupported_personal_difficulty')
        return value

    @staticmethod
    def _party_count(observed, minimum):
        value = observed.get('player_count')
        return isinstance(value, int) and not isinstance(value, bool) and minimum <= value <= 4

    def start_verification(self):
        """Start the pinned executable check once, including while in the menu."""
        if self._verification_started: return
        self._verification_started = True
        try:
            self.memory = self.memory_factory()
            require(self.memory is not None, 'memory_unavailable')
        except Exception as error: self._error('build', error)

    def _reader(self):
        self.start_verification()
        require(self.memory is not None and self.memory.ready, 'unsupported_executable')
        return BudgetReader(self.memory.read, max_calls=512, max_bytes=8192)

    def _typed(self, obj, name):
        cls = self.sdk.find_class(name)
        return obj is not None and cls is not None and obj.Class._inherits(cls)

    def _capture(self, read):
        """Validate the current local owner around a single bounded byte read."""
        require(self.memory is not None and self.memory.ready, 'unsupported_executable')
        pc = self.get_pc()
        require(self._typed(pc, 'OakPlayerController'), 'local_controller_unavailable')
        ps, pawn, player = pc.PlayerState, pc.Pawn, pc.Player
        require(self._typed(ps, 'OakPlayerState') and self._typed(pawn, 'OakCharacter')
                and self._typed(player, 'LocalPlayer'), 'local_player_unavailable')
        gs, gi = self.get_gs(pc), self.get_gi()
        require(self._typed(gs, 'OakGameState') and self._typed(gi, 'GbxGameInstance'),
                'world_context_unavailable')
        world = world_of(gs)
        require(world is not None, 'world_unavailable')
        driver = world.NetDriver
        connection = driver.ServerConnection if driver is not None else None
        transport = bool(driver.bIsPassthrough) if driver is not None else False
        require(driver is None or self._typed(driver, 'NetDriver'), 'native_driver_unavailable')
        require(connection is None or self._typed(connection, 'NetConnection'), 'server_connection_unavailable')
        identities = self.identity_factory(read, self.memory.base)
        def context():
            require(pc == self.get_pc() and gs == self.get_gs(pc) and gi == self.get_gi()
                    and pc.PlayerState == ps and pc.Pawn == pawn and pc.Player == player
                    and ps.Owner == pc and ps.GetPawn() == pawn and pawn.PlayerState == ps,
                    'local_owner_changed')
            require(world.NetDriver == driver and (driver is None or
                    driver.ServerConnection == connection and bool(driver.bIsPassthrough) == transport),
                    'native_connection_changed')
            require(all(world_of(obj) == world for obj in (pc, ps, pawn, gs)), 'local_world_changed')
            require(struct.unpack('<Q', read(world._get_address() + 0x1f0, 8))[0] == gi._get_address(),
                    'world_instance_changed')
            roster = gs.PlayerArray
            require(1 <= len(roster) <= 4 and sum(member == ps for member in roster) == 1,
                    'local_roster_changed')
            strong = tuple(identities.context_identity(obj._get_address())
                           for obj in (world, pc, gs, player, gi))
            weak = tuple(identities.weak_identity(obj._get_address()) for obj in (ps, pawn))
            network = tuple(identities.context_identity(obj._get_address()) if obj is not None else None
                            for obj in (driver, connection))
            return strong + weak + network
        before = context()
        choice = self._base_choice(struct.unpack('<B', read(ps._get_address() + 0x710, 1))[0])
        effective = self._base_choice(ps.PlayerDifficulty)
        role = int(ps.Role)
        require(before == context(), 'local_instance_changed')
        return dict(key=before, world=before[0], persistent=before[3:5], ps_key=before[5],
                    pc=pc, ps=ps, pawn=pawn, choice=choice, effective=effective, role=role,
                    count=len(gs.PlayerArray), server_connected=connection is not None, passthrough=transport)

    def observe(self, observed, *, enabled, now):
        """Capture a settled standalone choice before any join changes PlayerState."""
        if self._busy: return
        self._enabled = bool(enabled)
        if not enabled:
            self.cancel()
            return
        self.start_verification()
        if not self._time(now): return
        eligible = (observed.get('snapshot_available') is True and observed.get('player_world_ready') is True
                    and observed.get('pawn_present') is True and self._party_count(observed, 1)
                    and observed.get('player_count') == 1
                    and observed.get('server_connection_present') is False
                    and observed.get('remote_connection_count', 0) == 0)
        if not eligible:
            self._cache = None
            return
        if now < self._next_capture: return
        self._next_capture = now + self.POLL_SECONDS
        self._busy = True
        try:
            current = self._capture(self._reader())
            require(current['count'] == 1 and current['role'] == 3
                    and not current['server_connected']
                    and current['choice'] == current['effective'], 'prejoin_choice_not_settled')
            if current['key'] not in self._contexts:
                require(len(self._contexts) < self.MAX_CONTEXTS, 'source_context_budget')
                self._contexts.add(current['key'])
            self._cache = {key: current[key] for key in ('key', 'world', 'persistent', 'ps_key', 'choice')}
            self._cache['timestamp'] = now
        except Exception as error:
            self._cache = None
            self._error('capture', error)
        finally: self._busy = False

    def arm(self, attempt_id, *, now=None):
        """Freeze the already observed choice immediately before the real join."""
        if self._busy: return False
        self._busy = True
        try:
            now = self.clock() if now is None else now
            require(self._enabled and self._observer_ready and self._time(now), 'selection_observer_unavailable')
            require(isinstance(attempt_id, str) and re.fullmatch('[A-Za-z0-9_-]{1,96}', attempt_id),
                    'invalid_attempt_id')
            require(attempt_id not in self._attempts and len(self._attempts) < self.MAX_ATTEMPTS,
                    'attempt_budget_or_duplicate')
            source = self._cache
            require(source is not None and 0 <= now - source['timestamp'] <= self.CAPTURE_TTL,
                    'prejoin_choice_unavailable')
            current = self._capture(self._reader())
            require(current['key'] == source['key'] and current['choice'] == source['choice']
                    and current['effective'] == source['choice'] and current['count'] == 1
                    and current['role'] == 3 and not current['server_connected'], 'prejoin_choice_changed')
            if self._arm is not None and not self._arm['done']: self._finish('skipped', 'new_attempt')
            self._attempts.add(attempt_id)
            self._arm = dict(id=attempt_id, source=dict(source), consumed=False, done=False,
                             cancelled=False, choice=source['choice'])
            self._settle = None
            self._next_sync = 0.
            return True
        except Exception as error:
            self._error('arm', error)
            return False
        finally: self._busy = False

    def _finish(self, phase, why=None, **details):
        active = self._arm
        if active is None or active['done']: return
        active['done'] = True
        self._settle = None
        self._emit(phase=phase, reason=why, source_choice=active['choice'], **details)

    def cancel(self):
        self._cache = None
        if self._arm is not None:
            self._arm['cancelled'] = True
            self._finish('skipped', 'cancelled')
        self._settle = None

    def set_selection_observer_ready(self, ready):
        self._observer_ready = bool(ready)
        if not self._observer_ready: self.cancel()

    def note_native_selection(self, obj):
        """The exact stock setter PRE hook calls this; ignore our own dispatch."""
        if self._sending or self._arm is None or self._arm['done']: return
        try:
            pc = self.get_pc()
            if pc is not None and obj == pc.PlayerState:
                self._arm['cancelled'] = True
                self._finish('skipped', 'stock_selection_observed')
        except Exception:
            # An uncertain observer must not overwrite a possible user action.
            self._arm['cancelled'] = True
            self._finish('skipped', 'selection_observer_uncertain')

    def _signature(self):
        fn = self.sdk.find_object('Function', self.CALLBACK)
        require(fn is not None and fn._path_name() == self.CALLBACK
                and fn.Outer == self.sdk.find_class('OakPlayerState'), 'difficulty_function_unavailable')
        flags = int(fn.FunctionFlags)
        require(flags & 0x002004c0 == 0x002004c0 and not flags & 0x01004000,
                'difficulty_function_flags')
        props = list(fn._properties())
        require(len(props) == 1, 'difficulty_function_parameters')
        prop = props[0]
        require(type(prop).__name__ == 'ZByteProperty' and str(prop.Name) == 'NewDifficulty'
                and int(prop.ArrayDim) == 1 and int(prop.ElementSize) == 1
                and int(prop.Offset_Internal) == 0 and int(prop.PropertyFlags) & 0x80
                and not int(prop.PropertyFlags) & 0x500, 'difficulty_function_parameter_layout')
        return fn

    def sync(self, observed, *, enabled, role, attempt_id, now):
        if self._busy: return
        self._enabled = bool(enabled)
        if not enabled:
            self.cancel()
            return
        active = self._arm
        if (not self._observer_ready or active is None or active['done'] or active['consumed']
                or active['id'] != attempt_id or role != 'guest' or not self._time(now)): return
        eligible = (observed.get('snapshot_available') is True and observed.get('pawn_present') is True
                    and observed.get('server_connection_present') is True
                    and observed.get('passthrough') is True and self._party_count(observed, 2))
        if not eligible:
            self._settle = None
            return
        if now < self._next_sync: return
        self._next_sync = now + self.POLL_SECONDS
        self._busy = True
        called = False
        try:
            read = self._reader()
            current = self._capture(read)
            source = active['source']
            if current['persistent'] != source['persistent']:
                self._finish('skipped', 'local_player_context_changed')
                return
            if (current['ps_key'] == source['ps_key'] or current['world'] == source['world']
                    or current['role'] not in (1, 2) or current['count'] < 2
                    or not current['server_connected'] or not current['passthrough']):
                self._settle = None
                return
            if current['choice'] not in (active['choice'], 1):
                self._finish('skipped', 'postjoin_choice_conflict')
                return
            if self._settle is None or self._settle[0] != current['key']:
                self._settle = (current['key'], now)
                return
            if now - self._settle[1] < self.STABLE_SECONDS: return
            # Consume BEFORE diagnostics or native entry; failures can be partial.
            active['consumed'] = True
            fn = self._signature()
            self._emit(phase='started', source_choice=active['choice'],
                       observed_requested=current['choice'], authority_confirmed=False)
            require(self._arm is active and not active['done'] and not active['cancelled']
                    and self._enabled and self._observer_ready, 'handoff_cancelled')
            latest = self._capture(read)
            require(latest['key'] == current['key'] and latest['role'] in (1, 2)
                    and latest['persistent'] == source['persistent']
                    and latest['server_connected'] and latest['passthrough']
                    and latest['choice'] in (active['choice'], 1), 'handoff_context_changed')
            require(self._signature() == fn, 'difficulty_function_changed')
            callback = latest['ps'].ServerSetPlayerProfileDifficulty
            require(callable(callback), 'difficulty_callback_unavailable')
            self._sending = True
            called = True
            callback(active['choice'])
            self._finish('returned', 'dispatch_returned', authority_confirmed=False)
        except Exception as error:
            if active['consumed']:
                self._finish('raised' if called else 'skipped', 'native_dispatch_uncertain' if called else 'handoff_not_dispatched',
                             exception_type=type(error).__name__, authority_confirmed=False)
            else:
                self._settle = None
                self._error('prepare', error)
        finally:
            self._sending = False
            self._busy = False
