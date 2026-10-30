"""Guest-only bridge into native party registration and deferred UI consumers."""
from .party import PartyCoordinator
from .party_memory import ProcessMemory,require

def path(obj):
    value=obj._path_name()
    require(isinstance(value,str) and value.startswith('/'),'Native object identity is unavailable')
    return value

def world_of(obj):
    for _ in range(8):
        require(obj is not None,'Current world is unavailable')
        if path(obj.Class)=='/Script/Engine.World':return obj
        obj=obj.Outer
    raise ValueError('Unsupported world ownership chain')

class PartyBridge:
    def __init__(self,sdk,get_pc,get_game_state,memory_factory=ProcessMemory,diagnostic=lambda *a,**k:None):
        self.sdk=sdk;self.get_pc=get_pc;self.get_game_state=get_game_state
        self.memory_factory=memory_factory;self.diagnostic=diagnostic
        self.memory=None;self.coordinator=PartyCoordinator()
        self._next_scan=0.;self._signature=None;self._reported=set();self._active=False

    def _log(self,event,**details):
        try:self.diagnostic(event,**details)
        except Exception:pass

    def deactivate(self):
        self._active=False
        self.coordinator.reconcile(None,None,None,None,None,None,0)

    def _signatures(self):
        register=self.sdk.find_object('Function','/Script/Engine.PlayerState:OnRep_UniqueId')
        enqueue=self.sdk.find_object('Function','/Script/OakGame.OakUIDataCollector_BuddyInfo:OnPlayerConnected')
        require(register is not None and enqueue is not None,'Native party callbacks are unavailable')
        for fn in (register,enqueue):
            require(int(fn.FunctionFlags)&0x400 and not int(fn.FunctionFlags)&0x40,'Unsupported native party callback flags')
        require(list(register._properties())==[],'Unsupported native PlayerState registration signature')
        params=list(enqueue._properties())
        require(len(params)==1 and str(params[0].Name)=='ConnectedPlayer'
                and path(params[0].PropertyClass)=='/Script/Engine.PlayerState','Unsupported native connected signature')

    def sync(self,observed,*,enabled,guest,now):
        self._active=bool(enabled and guest and observed.get('snapshot_available') is True
                          and observed.get('server_connection_present') is True
                          and observed.get('passthrough') is True and observed.get('pawn_present') is True
                          and 2 <= observed.get('player_count',0) <= 4)
        if not self._active:
            self.deactivate();return
        if now<self._next_scan:return
        self._next_scan=now+1.
        context=None
        try:
            pc=self.get_pc()
            require(pc is not None and pc.Pawn is not None and pc.PlayerState is not None and pc.Player is not None,'Local player is unavailable')
            gs=self.get_game_state(pc)
            require(gs is not None,'Replicated world roster is unavailable')
            local_player=pc.Player;local_ps=pc.PlayerState;world=world_of(gs)
            cls=self.sdk.find_class('OakUIDataCollector_BuddyInfo')
            collectors=[o for o in self.sdk.find_all(cls,False) if not str(o.Name).startswith('Default__') and o.Outer==local_player]
            require(len(collectors)==1,'Owned native party collector is unavailable')
            collector=collectors[0]
            if self.memory is None:self.memory=self.memory_factory()
            if not self.memory.ready:
                if self.memory.error:raise ValueError(self.memory.error)
                self.coordinator.reconcile(None,None,None,None,None,None,now)
                return
            def identity(obj):
                index,serial=self.memory.layout.weak_identity(obj._get_address())
                return f'{path(obj)}#{index}:{serial}'
            context=tuple(identity(obj) for obj in (world,pc,gs,local_ps))
            collector_key=identity(collector)
            if self._signature is None:
                try:self._signatures()
                except Exception:
                    self._signature=False;raise
                self._signature=True
            if not self._signature:return
            roster=list(gs.PlayerArray)
            require(2 <= len(roster) <= 4 and sum(ps==local_ps for ps in roster)==1,'Invalid native party roster')
            members=[];ids=set()
            for ps in roster:
                player_id=ps.PlayerId
                require(isinstance(player_id,int) and not isinstance(player_id,bool) and 0 <= player_id <= 0x7ffffffe
                        and player_id not in ids,'Native player IDs are not settled')
                ids.add(player_id)
                weak_identity=self.memory.layout.weak_identity(ps._get_address())
                require(world_of(ps)==world,'Native party member belongs to another world')
                pawn=ps.GetPawn();index=ps.BuddyIndex
                require(pawn is None or world_of(pawn)==world,'Native party pawn belongs to another world')
                ready=(pawn is not None and pawn.PlayerState==ps and isinstance(index,int)
                       and not isinstance(index,bool) and 0 <= index <= 3)
                key=f'{path(ps)}#{player_id}#{weak_identity[0]}:{weak_identity[1]}'
                members.append(dict(key=key,ready=ready,local=ps==local_ps,player_state=ps,
                                    player_id=player_id,weak_identity=weak_identity,pawn=pawn))

            def validate():
                require(self._active and self.get_pc()==pc and self.get_game_state(pc)==gs
                        and pc.Player==local_player and pc.PlayerState==local_ps and world_of(gs)==world
                        and collector.Outer==local_player
                        and tuple(identity(obj) for obj in (world,pc,gs,local_ps))==context
                        and identity(collector)==collector_key
                        and world_of(pc)==world,'Native party context changed')
                current=list(gs.PlayerArray)
                require(len(current)==len(roster) and all(m['player_state'] in current for m in members),'Native party roster changed')
                for member in members:
                    ps=member['player_state']
                    require(ps.PlayerId==member['player_id'] and self.memory.layout.weak_identity(ps._get_address())==member['weak_identity'],
                            'Native party member changed')
                    pawn=ps.GetPawn()
                    require(world_of(ps)==world and pawn==member['pawn']
                            and (pawn is None or (world_of(pawn)==world and pawn.PlayerState==ps)),
                            'Native party member world or pawn changed')

            def sources():
                validate()
                registered=self.memory.layout.registry(world._get_address())
                state=self.memory.layout.buddy(collector._get_address())
                validate()
                return dict(registered={m['key'] for m in members if m['weak_identity'] in registered},
                            pending={m['key'] for m in members if m['weak_identity'] in state['pending']},
                            collector_ready=state['ready'])

            def register(member):
                if member['key'] in sources()['registered']:return
                member['player_state'].OnRep_UniqueId()
                self._log('native_party_registration_dispatched',context=context,player_id=member['player_id'])

            def enqueue(member):
                current=sources()
                if member['key'] in current['pending'] or member['key'] not in current['registered']:return
                collector.OnPlayerConnected(member['player_state'])
                self._log('native_party_queue_seeded',context=context,player_id=member['player_id'])

            result=self.coordinator.reconcile(context,collector_key,members,sources,register,enqueue,now)
            for error in result['errors']:
                token=(context,error['phase'],error['key'])
                if token not in self._reported:
                    self._reported.add(token);self._log('native_party_error',context=context,**error)
        except Exception as error:
            self.coordinator.reconcile(None,None,None,None,None,None,now)
            token=(context,type(error).__name__,str(error)[:160])
            if token not in self._reported:
                self._reported.add(token);self._log('native_party_unavailable',context=context,error=str(error)[:300])
