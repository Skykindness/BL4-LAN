"""Automatic bounded party evidence; never restores, registers or refreshes UI."""
import hashlib
import json
import re
from contextlib import nullcontext
from .party_adapter import world_of
from .party_memory import ProcessMemory,require
from .party_probe import AuditIdentity,BudgetReader,ProbeLayout,name_summary


class AuditSchedule:
    DELAYS=(2.,10.,30.)
    def __init__(self):self.history={}

    def offer(self,context,now):
        if context not in self.history:
            if len(self.history)>=8:return None
            self.history[context]=[now,0]
        start,stage=self.history[context]
        if stage>=len(self.DELAYS) or now-start<self.DELAYS[stage]:return None
        self.history[context][1]+=1
        return stage


def reason(error):
    text=str(error)
    return text if re.fullmatch('[a-z_]{1,64}',text) else type(error).__name__


class AuditBridge:
    def __init__(self,sdk,get_pc,get_game_state,get_game_instance,*,memory_factory=ProcessMemory,
                 probe_factory=ProbeLayout,identity_factory=AuditIdentity,diagnostic=lambda *a,**k:None,trace=None):
        self.sdk=sdk;self.get_pc=get_pc;self.get_gs=get_game_state;self.get_gi=get_game_instance
        self.memory_factory=memory_factory;self.probe_factory=probe_factory;self.identity_factory=identity_factory
        self.diagnostic=diagnostic;self.memory=None;self.schedule=AuditSchedule()
        self.trace=trace
        self._next_scan=0.;self._errors=set();self._limited=False

    def _emit(self,event,audit):
        try:self.diagnostic(event,audit=audit)
        except Exception:pass

    def _error(self,phase,error):
        value=reason(error);key=(phase,value)
        if key not in self._errors and len(self._errors)<8:
            self._errors.add(key)
            self._emit('party_audit_unavailable',dict(schema=1,status='unavailable',phase=phase,reason=value))

    def _typed(self,obj,kind):
        return obj is not None and obj.Class._inherits(self.sdk.find_class(kind))

    def _prepare(self,read):
        pc=self.get_pc();require(pc is not None and pc.Pawn is not None and pc.Player is not None,'local_player_unavailable')
        gs=self.get_gs(pc);require(gs is not None,'world_roster_unavailable')
        world=world_of(gs);require(world_of(pc)==world,'controller_world_changed')
        gi=self.get_gi();require(self._typed(gi,'GbxGameInstance'),'game_instance_unavailable')
        singletons=gi.GameSingletons;require(self._typed(singletons,'GbxGameSingletons'),'singletons_unavailable')
        identities=self.identity_factory(read,self.memory.base);identity=identities.weak_identity
        require(struct_world_instance(read,world._get_address())==gi._get_address(),'world_instance_changed')
        def ident(obj):return identity(obj._get_address()) if obj is not None else None
        roster=list(gs.PlayerArray)
        require(2<=len(roster)<=4 and sum(ps==pc.PlayerState for ps in roster)==1,'invalid_party_roster')
        members=[];ids=set()
        for ps in roster:
            require(self._typed(ps,'OakPlayerState') and world_of(ps)==world,'foreign_player_state')
            weak=ident(ps);player_id=ps.PlayerId
            require(isinstance(player_id,int) and not isinstance(player_id,bool) and 0<=player_id<=0x7ffffffe
                    and player_id not in ids,'invalid_player_id');ids.add(player_id)
            pawn=ps.GetPawn();actor_weak=ident(pawn)
            require(pawn is None or self._typed(pawn,'OakCharacter') and world_of(pawn)==world
                    and pawn.PlayerState==ps,'foreign_or_changed_pawn')
            if ps==pc.PlayerState:require(pawn==pc.Pawn,'local_pawn_changed')
            try:
                nickname=ps.PlayerNamePrivate
                if not isinstance(nickname,str) or len(nickname)>128:nickname=None
            except Exception:nickname=None
            members.append(dict(player_state=ps,pawn=pawn,player_id=player_id,weak=weak,
                actor_weak=actor_weak,actor_index=actor_weak[0] if actor_weak else None,
                local=ps==pc.PlayerState,replicated_name=nickname))
        members.sort(key=lambda m:m['player_id'])
        key=tuple(identities.context_identity(o._get_address()) for o in (world,pc,gs,pc.Player,gi,singletons)) + tuple(
            (m['player_id'],m['weak'],m['actor_weak'],m['local']) for m in members)
        return key,dict(pc=pc,gs=gs,world=world,gi=gi,singletons=singletons,members=members,identities=identities)

    def _scan(self,name):
        cls=self.sdk.find_class(name);require(cls is not None,'collector_class_unavailable');objects=[]
        for i,obj in enumerate(self.sdk.find_all(cls,False)):
            require(i<1024,'collector_scan_limit')
            if not str(obj.Name).startswith('Default__') and self._typed(obj,name):objects.append(obj)
        return objects

    @staticmethod
    def _section(callback):
        try:return callback()
        except Exception as error:return dict(status='unavailable',reason=reason(error))

    @staticmethod
    def _difficulty_index(value,key='index'):
        require(isinstance(value,int) and not isinstance(value,bool) and 0<=value<=255,
                'invalid_difficulty_index')
        label={0:'easy',1:'normal',2:'hard'}.get(value)
        return dict(status='ok',**{key:value},base_label=label,
                    interpretation='known_base_index' if label is not None else 'mode_derived_or_unknown')

    @staticmethod
    def _authority_role(obj):
        try:return int(obj.Role)==3
        except Exception:return None

    def _difficulty(self,prepared,probe,role):
        """Observed values only; requested choice is not a replicated field."""
        world=self._section(lambda:self._difficulty_index(prepared['gs'].WorldPlayerDifficulty,'observed_index'))
        world.update(source=role+'_game_state_observation',
                     authority_observed=self._authority_role(prepared['gs']))
        players=[]
        for member in prepared['members']:
            ps=member['player_state'];authority=self._authority_role(ps)
            def validate(requested=False):
                require(self._typed(ps,'OakPlayerState') and world_of(ps)==prepared['world']
                        and prepared['identities'].weak_identity(ps._get_address())==member['weak'],
                        'difficulty_player_changed')
                if member['local']:
                    require(prepared['pc']==self.get_pc() and prepared['pc'].PlayerState==ps,
                            'difficulty_local_player_changed')
                if requested:
                    require(self.memory is not None and self.memory.ready,'unsupported_executable')
                    require(member['local'] or role=='host' and self._authority_role(ps) is True,
                            'difficulty_requested_not_owned')
            def effective():
                validate();result=self._difficulty_index(ps.PlayerDifficulty);validate()
                result.update(source='authority_player_state_observation' if role=='host' and authority is True
                              else 'replicated_player_state_observation',authority_observed=authority)
                return result
            def requested():
                validate(True);result=self._difficulty_index(probe.number(ps._get_address()+0x710,'B'));validate(True)
                result.update(source='local_player_state_requested' if member['local']
                              else 'authority_player_state_requested',replicated=False)
                return result
            row=dict(player_id=member['player_id'],local=member['local'],effective=self._section(effective))
            if member['local'] or role=='host' and authority is True:
                row['requested']=self._section(requested)
            else:
                row['requested']=dict(status='not_read',reason='remote_choice_not_replicated' if role=='guest'
                                      else 'authority_not_observed')
            players.append(row)
        return dict(status='captured',world=world,players=players)

    def _snapshot(self,prepared,probe,stage,role,tag):
        overheads=self._section(lambda:dict(status='ok',objects=self._scan('OakUIDataCollector_OverheadNP')))
        buddies=self._section(lambda:dict(status='ok',objects=self._scan('OakUIDataCollector_BuddyInfo')))
        matchmaking=self._section(lambda:dict(status='ok',objects=self._scan('OakUIDataCollector_Matchmaking')))
        members=[]
        def collector_read(obj,owner,kind,callback):
            address=obj._get_address();identity=prepared['identities'].context_identity(address)
            require(obj.Outer==owner and self._typed(obj,kind),'collector_owner_changed')
            result=callback(address)
            require(obj._get_address()==address and obj.Outer==owner and self._typed(obj,kind)
                    and prepared['identities'].context_identity(address)==identity,'collector_instance_changed')
            return result
        for member in prepared['members']:
            pawn=member['pawn'];row=dict(player_id=member['player_id'],local=member['local'],
                player_instance=list(member['weak']),pawn_instance=list(member['actor_weak']) if pawn else None,
                replicated_name=name_summary(member['replicated_name']) if member['replicated_name'] is not None else dict(status='unavailable'))
            if pawn is not None:
                def flags():
                    address=pawn._get_address();definition=probe.number(address+0x3f70,'Q')
                    return dict(status='ok',role_value=int(pawn.Role),remote_role_value=int(pawn.RemoteRole),
                        override_value=probe.number(address+0x7fe8,'B'),definition_present=bool(definition),
                        definition_flag_byte=probe.number(definition+0x1088,'B') if definition else None)
                row['actor_flags']=self._section(flags)
            else:row['actor_flags']=dict(status='absent')
            if overheads['status']!='ok':row['overhead_collector']=dict(status='unavailable',reason=overheads.get('reason'))
            else:
                matches=[o for o in overheads['objects'] if o.Outer==pawn and pawn is not None]
                if len(matches)!=1:row['overhead_collector']=dict(status='absent' if not matches else 'ambiguous',count=len(matches))
                else:
                    row['overhead_collector']=self._section(lambda:collector_read(matches[0],pawn,
                        'OakUIDataCollector_OverheadNP',lambda address:dict(status='ok',
                            ready_value=probe.number(address+0x40,'B'),enabled_value=probe.number(address+0x48,'B'),
                            player_association_value=probe.number(address+0xd0,'B'),dirty_value=probe.number(address+0xd1,'B'))))
            members.append(row)
        result=dict(schema=1,status='captured',stage=stage,role=role,context_tag=tag,members=members,
            names=self._section(lambda:probe.player_names(prepared['singletons']._get_address(),prepared['members'])),
            world_rows=self._section(lambda:probe.world_rows(prepared['world']._get_address(),prepared['gi']._get_address(),prepared['members'])),
            difficulty=self._section(lambda:self._difficulty(prepared,probe,role)),
            rendered_overhead_widget=dict(status='not_instrumented',reason='private_widget_not_safely_located'))
        if buddies['status']!='ok':result['lobby']=dict(status='unavailable',reason=buddies.get('reason'))
        else:
            matches=[o for o in buddies['objects'] if o.Outer==prepared['pc'].Player]
            if len(matches)!=1:result['lobby']=dict(status='absent' if not matches else 'ambiguous',count=len(matches))
            else:
                def lobby(address):
                    key=probe.number(address+0x54,'I');value=probe.lobby(key)
                    require(probe.number(address+0x54,'I')==key,'collector_key_changed')
                    return value
                result['lobby']=self._section(lambda:collector_read(matches[0],prepared['pc'].Player,
                    'OakUIDataCollector_BuddyInfo',lobby))
        if matchmaking['status']!='ok':result['matchmaking']=dict(status='unavailable',reason=matchmaking.get('reason'))
        else:
            matches=[o for o in matchmaking['objects'] if o.Outer==prepared['pc'].Player]
            if len(matches)!=1:result['matchmaking']=dict(status='absent' if not matches else 'ambiguous',count=len(matches))
            else:
                def matchmaking_source(address):
                    key=probe.number(address+0x54,'I');value=probe.matchmaking(key)
                    require(probe.number(address+0x54,'I')==key,'collector_key_changed')
                    return value
                result['matchmaking']=self._section(lambda:collector_read(matches[0],prepared['pc'].Player,
                    'OakUIDataCollector_Matchmaking',matchmaking_source))
        return result

    def sync(self,observed,*,enabled,role,now):
        eligible=(enabled and role in ('host','guest') and observed.get('snapshot_available') is True
            and observed.get('player_world_ready') is True and observed.get('passthrough') is True
            and observed.get('pawn_present') is True and 2<=observed.get('player_count',0)<=4
            and (observed.get('server_connection_present') is True if role=='guest' else observed.get('remote_connection_count',0)>0))
        if not eligible or now<self._next_scan:return
        self._next_scan=now+1.
        try:
            if self.memory is None:self.memory=self.memory_factory()
            if not self.memory.ready:
                if self.memory.error:self._error('build',ValueError('unsupported_executable'))
                return
            read=BudgetReader(self.memory.read,max_calls=1024,max_bytes=65536)
            context,prepared=self._prepare(read);context=(role,)+context
            if context not in self.schedule.history and len(self.schedule.history)>=8:
                if not self._limited:
                    self._limited=True;self._emit('party_audit_limit',dict(schema=1,status='limited',reason='context_budget'))
                return
            stage=self.schedule.offer(context,now)
            if stage is None:
                if self.schedule.history[context][1]>=3:self._next_scan=now+5.
                return
            tag=hashlib.sha256(repr(context).encode('utf-8')).hexdigest()[:16]
            try:
                with self.trace.operation('native_name_probe') if self.trace is not None else nullcontext():
                    result=self._snapshot(prepared,self.probe_factory(read,self.memory.base),stage,role,tag)
                current,_=self._prepare(read);require((role,)+current==context,'context_changed')
                result['read_budget']=dict(calls=read.calls,bytes=read.bytes,max_calls=read.max_calls,max_bytes=read.max_bytes)
                require(len(json.dumps(result,ensure_ascii=True).encode('utf-8'))<=16384,'output_budget')
            except Exception as error:
                result=dict(schema=1,status='unavailable',stage=stage,role=role,context_tag=tag,reason=reason(error))
            self._emit('party_audit',result)
        except Exception as error:self._error('context',error)


def struct_world_instance(read,world):
    import struct
    return struct.unpack('<Q',read(world+0x1f0,8))[0]
