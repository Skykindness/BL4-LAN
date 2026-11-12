"""Restore missing discovery bindings after a LAN host loads World_P."""

import ctypes
import struct

from .party_memory import ProcessMemory


BIND = 0x5744462
BIND_FINGERPRINT = bytes.fromhex(
    "56574883ec48488b05d134fc064831e048894424404885d27459488b9288040000"
    "4885d2744d4889ce488d7c24304889"
)
SAFEHOUSES = tuple(
    f"ZoneActivity_{region}_Safehouse{number}"
    for region, numbers in (
        ("City", (1, 2, 3)), ("Grasslands", ("Main", 2, 3, 4)),
        ("Mountains", (1, 2, 3, 4)), ("Shatteredlands", (1, 2, 3)),
    )
    for number in numbers
)


class DiscoveryBindings:
    def __init__(self, sdk, memory):
        self.memory = memory
        self.base = memory.base
        self.read = memory.read
        self.number = memory.layout.number
        if self.read(self.base + BIND, len(BIND_FINGERPRINT)) != BIND_FINGERPRINT:
            raise ValueError("Safehouse binding fingerprint mismatch")
        self.names = {}
        for name in (*SAFEHOUSES, "available", "accepted", "active"):
            token = sdk.make_struct("/Script/CoreUObject.SToken", Name=name)
            self.names[name] = self.read(token._get_address() + 4, 8)
        if len(set(self.names.values())) != len(self.names):
            raise ValueError("Ambiguous native mission tokens")
        self.bind = ctypes.WINFUNCTYPE(None, ctypes.c_void_p, ctypes.c_void_p)(self.base + BIND)

    def pointer(self, address):
        return self.number(address, "Q")

    def records(self, world):
        instance = self.pointer(world + 0x1F0)
        if self.pointer(instance) != self.base + 0xB75A530:
            raise ValueError("Unexpected safehouse game instance")
        shard = self.pointer(self.pointer(instance + 0x518) + 0x38)
        if self.pointer(shard) != self.base + 0xB7E3D30:
            raise ValueError("Unexpected safehouse mission shard")
        interface = self.pointer(self.pointer(shard + 0x180) + 0xE0)
        if self.pointer(interface) != self.base + 0xB97D710:
            raise ValueError("Unexpected safehouse mission interface")
        array = interface + 0x60
        data, count, capacity = struct.unpack("<Qii", self.read(array, 16))
        bits, bit_capacity = struct.unpack("<ii", self.read(array + 0x28, 8))
        if not (0 <= count <= capacity <= 2000 and bits == count <= bit_capacity <= 4000 and (data or not count)):
            raise ValueError("Unexpected safehouse mission registry bounds")
        mask = self.read(self.pointer(array + 0x20) or array + 0x10, ((count + 31) // 32) * 4) if count else b""
        wanted = {self.names[name] for name in SAFEHOUSES}
        rows = {}
        for offset in range(0, count, 128):
            block = self.read(data + offset * 32, min(128, count - offset) * 32)
            for i in range(len(block) // 32):
                index = offset + i
                if not mask[index // 8] & (1 << (index % 8)):
                    continue
                entry = block[i * 32:(i + 1) * 32]
                if entry[4:12] not in wanted:
                    continue
                mission = struct.unpack_from("<Q", entry, 16)[0]
                table = self.pointer(mission)
                if (table != self.base + 0xB97B5B0 or self.pointer(table + 0xF8) != self.base + 0xD82910
                        or self.pointer(table + 0x110) != self.base + 0x41A66BE):
                    raise ValueError("Unexpected safehouse mission record")
                rows.setdefault(entry[4:12], []).append((entry[:12], self.pointer(mission + 0xA8)))
        return dict(values[0] for values in rows.values() if len(values) == 1 and values[0][1])

    def status(self, storage):
        value = self.pointer(self.pointer(storage + 8) + 0x20)
        if not value:
            return None
        fact = self.read(value + 0x38, 12)
        return fact[4:12] if fact[0] == 3 else None

    def owners_world(self, context, references):
        if not context or not references or self.number(references + 8, "i") <= 0:
            return 0
        index, serial = struct.unpack("<ii", self.read(context + 0x18, 8))
        count = self.number(self.base + 0xC9647DC, "i")
        if not 0 <= index < count <= 16_000_000 or serial <= 0:
            return 0
        chunks = self.pointer(self.base + 0xC9647C8)
        chunk = self.pointer(chunks + (index >> 16) * 8)
        if not chunk:
            return 0
        item = chunk + (index & 0xFFFF) * 24
        obj, flags, _, live_serial, _ = struct.unpack("<Q4i", self.read(item, 24))
        if not obj or live_serial != serial or flags & 0x10200000:
            return 0
        outer = self.pointer(obj + 0x30)
        return self.pointer(outer + 0x360) if outer else 0

    def registrations(self, world, tokens):
        address = self.base + 0xCB7DF38
        header = self.read(address, 24)
        data, count, capacity, _, broadcasting = struct.unpack("<Q4i", header)
        if broadcasting or not 0 <= count <= capacity <= 32768 or (count and not data):
            raise ValueError("Safehouse callback registry is busy or invalid")
        rows = {token: {} for token in tokens}
        for offset in range(0, count, 256):
            block = self.read(data + offset * 16, min(count - offset, 256) * 16)
            for callback, words, _ in struct.iter_unpack("<Q2I", block):
                if not callback or words != 3:
                    continue
                table, giver, control, function = struct.unpack("<Q16x3Q", self.read(callback, 48))
                if (table != self.base + 0xB97D2C0 or function != self.base + BIND
                        or not giver or not control or self.number(control + 8, "i") <= 0):
                    continue
                row = self.read(giver, 0x110)
                token = row[0x18:0x24]
                if (token not in rows or struct.unpack_from("<3Q", row) != (self.base + 0xB6AEE80, giver, control)
                        or struct.unpack_from("<i", row, 0x28)[0] != 1):
                    continue
                context, references, target, source, source_ref = struct.unpack_from("<5Q", row, 0xE8)
                if self.owners_world(context, references) == world:
                    rows[token][giver] = (target, source, source_ref)
        if self.read(address, 24) != header:
            raise ValueError("Safehouse callback registry changed")
        return rows

    def pending(self, world):
        records = {token: storage for token, storage in self.records(world).items()
                   if self.status(storage) == self.names["available"]}
        registrations = self.registrations(world, records)
        return [(token, records[token], giver, target)
                for token, rows in registrations.items() if len(rows) == 1
                for giver, (target, source, refs) in rows.items() if target and not source and not refs]

    def repair(self, scope, plan, current_scope, result):
        world, _, game_state, source = scope
        for token, storage, giver, target in plan:
            if (current_scope() != scope or self.records(world).get(token) != storage
                    or self.status(storage) != self.names["available"]
                    or self.registrations(world, (token,))[token] != {giver: (target, 0, 0)}):
                raise ValueError("Safehouse registration changed before binding")
            self.bind(giver, game_state)
            if current_scope() != scope or self.records(world).get(token) != storage:
                raise ValueError("Host world changed during safehouse binding")
            rows = self.registrations(world, (token,))[token]
            bound_target, bound_source, refs = rows.get(giver, (0, 0, 0))
            if len(rows) == 1 and bound_target == target and bound_source == source and refs and self.number(refs + 8, "i") > 0:
                result["bound"] += 1
            elif not rows and self.status(storage) in (self.names["accepted"], self.names["active"]):
                result["started"] += 1
            else:
                raise ValueError("Native safehouse binding was not verified")


class SafehouseRecovery:
    def __init__(self, sdk, get_pc, get_world, get_game_state, diagnostic):
        self.sdk = sdk
        self.get_pc = get_pc
        self.get_world = get_world
        self.get_game_state = get_game_state
        self.diagnostic = diagnostic
        self.memory = None
        self.bindings = None
        self.cancel()

    def cancel(self):
        self.initial = None

    def arm(self, now):
        world, pc = self.get_world(), self.get_pc()
        if world is None or pc is None:
            return
        if self.memory is None:
            self.memory = ProcessMemory()
        self.initial = (world._get_address(), pc._get_address())
        self.deadline = now + 60
        self.next_scan = now

    def scope(self):
        native = self.bindings
        world, pc = self.get_world(), self.get_pc()
        if world is None or pc is None or not pc.HasAuthority() or world._path_name().rsplit(".", 1)[-1] != "World_P":
            return None
        w, p = world._get_address(), pc._get_address()
        driver = world.NetDriver
        state = self.get_game_state(pc)
        if (not pc._path_name().startswith(world._path_name() + ":") or driver is None
                or driver.ServerConnection is not None or not driver.bIsPassthrough or state is None or pc.PlayerState is None):
            return None
        gs = state._get_address()
        if (native.pointer(native.base + 0xCB10918) != w or native.pointer(w + 0x178) != gs
                or native.pointer(gs + 0x488) != pc.PlayerState._get_address()
                or native.pointer(p + 0xE60) != native.base + 0xB9938A0):
            return None
        source, refs = struct.unpack("<2Q", native.read(p + 0x2200, 16))
        if not source or not refs or native.number(refs + 8, "i") <= 0:
            return None
        return w, p, gs, source

    def sync(self, observed, now):
        if self.initial is None:
            return
        if observed.get("role") != "host" or observed.get("status") == "error":
            self.cancel()
            return
        if self.memory.error is not None:
            self.cancel()
            self.diagnostic("safehouse_binding_unavailable", error=self.memory.error)
            return
        if now >= self.deadline:
            self.cancel()
            return
        if (now < self.next_scan or not self.memory.ready or observed.get("status") not in ("hosting", "connected")
                or observed.get("player_world_ready") is not True):
            return
        self.next_scan = now + 1
        if self.bindings is None:
            try:
                self.bindings = DiscoveryBindings(self.sdk, self.memory)
            except (ValueError, RuntimeError, AttributeError, OSError) as error:
                self.cancel()
                self.diagnostic("safehouse_binding_unavailable", error=str(error))
                return
        try:
            scope = self.scope()
            if scope is None or scope[:2] == self.initial:
                return
            plan = self.bindings.pending(scope[0])
            if not plan:
                return
        except (ValueError, RuntimeError, AttributeError, OSError) as error:
            if now + 1 >= self.deadline:
                self.diagnostic("safehouse_binding_unavailable", error=str(error))
            return

        self.cancel()
        result = dict(planned=len(plan), bound=0, started=0)
        try:
            self.bindings.repair(scope, plan, self.scope, result)
        except (ValueError, RuntimeError, AttributeError, OSError) as error:
            result["error"] = str(error)
        self.diagnostic("safehouse_bindings", **result)
