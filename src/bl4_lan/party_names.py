"""Bounded recovery of an existing empty native overhead-name row."""
import hashlib
import math
import re
from .party_memory import require
from .party_probe import BudgetReader,name_summary


class NameBridge:
    MAX_CONTEXTS=8
    MAX_ATTEMPTS=32
    CALLBACK='/Script/OakGame.OakUIDataCollector_OverheadNP:OnNoSignalStateChanged'

    def __init__(self,sdk,audit_bridge,diagnostic=lambda *a,**k:None,trace=None):
        self.sdk=sdk;self.audit=audit_bridge;self.diagnostic=diagnostic;self.trace=trace
        self._contexts={};self._attempted=set();self._errors=set();self._next_scan=0.;self._busy=False

    def _emit(self,event,**payload):
        try:self.diagnostic(event,**payload)
        except Exception:pass

    def _error(self,phase,error,tag=None):
        token=(phase,type(error).__name__)
        if token not in self._errors and len(self._errors)<8:
            self._errors.add(token)
            self._emit('overhead_name_refresh_unavailable',phase=phase,exception_type=type(error).__name__,context_tag=tag)

    @staticmethod
    def _one(rows,player_id):
        matches=[row for row in rows if isinstance(row,dict) and row.get('player_id')==player_id]
        return matches[0] if len(matches)==1 else None

    def _source(self,probe,prepared,member):
        name=member.get('replicated_name')
        if not isinstance(name,str) or not 0<len(name)<=128:return None
        expected=name_summary(name)
        names=probe.player_names(prepared['singletons']._get_address(),[member])
        world=probe.world_rows(prepared['world']._get_address(),prepared['gi']._get_address(),[member])
        if names.get('status')!='ok' or world.get('status')!='ok':return None
        source=self._one(names.get('players',[]),member['player_id'])
        row=self._one(world.get('players',[]),member['player_id'])
        if not (source and source.get('lookup')=='matched' and source.get('matches_replicated') is True
                and source.get('name')==expected and row and row.get('row_status')=='present'
                and row.get('active_value')==1):return None
        if row.get('name')==name_summary(''):return expected,False
        summary=self._safe_summary(row.get('name'))
        return (expected,True) if summary.get('status')=='present' else None

    @staticmethod
    def _safe_summary(value):
        if isinstance(value,dict):
            status=value.get('status');count=value.get('characters');digest=value.get('digest')
            if status=='empty' and count==0 and digest is None:return name_summary('')
            if (status=='present' and isinstance(count,int) and not isinstance(count,bool) and 0<count<=128
                    and isinstance(digest,str) and re.fullmatch('[0-9a-f]{16}',digest)):
                return dict(status=status,characters=count,digest=digest)
        return dict(status='unavailable')

    def _collector(self,prepared,obj,pawn):
        require(obj.Outer==pawn and self.audit._typed(obj,'OakUIDataCollector_OverheadNP'),'collector_owner_changed')
        address=obj._get_address()
        return address,prepared['identities'].context_identity(address),obj.Class

    @staticmethod
    def _flags(probe,address):
        require(tuple(probe.number(address+offset,'B') for offset in (0x40,0x48,0xd0,0xd1))==(1,1,1,0),
                'collector_not_ready')

    def _validate(self,read,context,prepared,probe,member,collector,token):
        current,now=self.audit._prepare(read)
        require(current==context and now['pc']==prepared['pc'],'name_context_changed')
        peer=self._one(now['members'],member['player_id'])
        require(peer is not None and peer['replicated_name']==member['replicated_name']
                and peer['player_state']==member['player_state'] and peer['pawn']==member['pawn'],
                'name_member_changed')
        require(int(peer['pawn'].Role)==3 or int(peer['pawn'].RemoteRole)==3,'name_creator_role_changed')
        pawn=peer['pawn']._get_address();override=probe.number(pawn+0x7fe8,'B')
        if override!=1:
            definition=probe.number(pawn+0x3f70,'Q')
            require(override!=0 and definition and probe.number(definition+0x1088,'B')==1,
                    'name_enable_policy_changed')
        require(self._collector(now,collector,member['pawn'])==token,'name_collector_changed')
        self._flags(probe,token[0])

    def _signature(self):
        fn=self.sdk.find_object('Function',self.CALLBACK)
        require(fn is not None and int(fn.FunctionFlags)&0x400 and not int(fn.FunctionFlags)&0x40,
                'unsupported_name_callback_flags')
        require(list(fn._properties())==[],'unsupported_name_callback_parameters')

    def _invoke(self,callback,validate):
        token=None
        try:
            if self.trace is not None:token=self.trace.begin('overhead_name_refresh')
        except Exception:pass
        called=False;error=None
        try:
            validate();called=True;callback()
        except Exception as failure:error=failure
        try:
            if self.trace is not None:self.trace.end(token,'raised' if error is not None else 'returned')
        except Exception:pass
        return called,error

    def sync(self,observed,*,enabled,role,now):
        if self._busy:return
        self._busy=True;tag=None
        try:
            count=observed.get('player_count',0)
            eligible=(enabled and role in ('host','guest') and observed.get('snapshot_available') is True
                and observed.get('player_world_ready') is True and observed.get('passthrough') is True
                and observed.get('pawn_present') is True and isinstance(count,int) and not isinstance(count,bool)
                and 2<=count<=4 and (observed.get('server_connection_present') is True if role=='guest'
                    else observed.get('remote_connection_count',0)>0))
            if not eligible or not isinstance(now,(int,float)) or not math.isfinite(now) or now<self._next_scan:return
            self._next_scan=now+2.
            if self.audit.memory is None:self.audit.memory=self.audit.memory_factory()
            memory=self.audit.memory
            if not memory.ready:return
            read=BudgetReader(memory.read,max_calls=1024,max_bytes=65536)
            context,prepared=self.audit._prepare(read);key=(role,)+context
            tag=hashlib.sha256(repr(key).encode('utf-8')).hexdigest()[:16]
            if key not in self._contexts:
                if len(self._contexts)>=self.MAX_CONTEXTS:return
                self._contexts[key]=dict(done=set(),tracked={},complete=False)
            state=self._contexts[key]
            if state['complete']:
                for member_key,(collector,pawn,token) in tuple(state['tracked'].items()):
                    try:require(self._collector(prepared,collector,pawn)==token,'name_collector_changed')
                    except Exception:
                        state['done'].discard(member_key);state['tracked'].pop(member_key,None);state['complete']=False
                if state['complete']:self._next_scan=now+5.;return
            peers=[m for m in prepared['members'] if not m['local']]
            require(1<=len(peers)<=3,'invalid_remote_roster')
            collectors=self.audit._scan('OakUIDataCollector_OverheadNP')
            probe=self.audit.probe_factory(read,memory.base)
            for member in peers:
                member_key=(member['weak'],member['actor_weak'])
                if member_key in state['done'] or member['pawn'] is None:continue
                try:
                    matches=[o for o in collectors if o.Outer==member['pawn']]
                    if len(matches)!=1:continue
                    collector=matches[0];token=self._collector(prepared,collector,member['pawn'])
                    attempt=(context[0],member['weak'],member['actor_weak'],token[:2])
                    def done():
                        state['done'].add(member_key);state['tracked'][member_key]=(collector,member['pawn'],token)
                    if attempt in self._attempted:done();continue
                    self._flags(probe,token[0])
                    source=self._source(probe,prepared,member)
                    if source is None:continue
                    if source[1]:done();continue
                    validate=lambda:self._validate(read,context,prepared,probe,member,collector,token)
                    validate()
                    refreshed_source=self._source(probe,prepared,member)
                    if refreshed_source is None or refreshed_source!=source:continue
                    self._signature();validate()
                    callback=collector.OnNoSignalStateChanged
                    require(callable(callback),'name_callback_unavailable')
                    if len(self._attempted)>=self.MAX_ATTEMPTS:continue
                    self._attempted.add(attempt);done()
                    self._emit('overhead_name_refresh',phase='started',context_tag=tag,
                               player_id=member['player_id'],source_name=source[0],row_before=name_summary(''))
                    called,error=self._invoke(callback,validate)
                    if error is not None:
                        self._emit('overhead_name_refresh',phase='raised' if called else 'skipped',
                            context_tag=tag,player_id=member['player_id'],exception_type=type(error).__name__)
                        continue
                    after=dict(status='unavailable');measurement_error=None
                    try:
                        validate()
                        rows=probe.world_rows(prepared['world']._get_address(),prepared['gi']._get_address(),[member])
                        row=self._one(rows.get('players',[]),member['player_id']) if rows.get('status')=='ok' else None
                        if row and row.get('row_status')=='present':after=self._safe_summary(row.get('name'))
                        validate()
                    except Exception as failure:after=dict(status='unavailable');measurement_error=type(failure).__name__
                    self._emit('overhead_name_refresh',phase='returned',context_tag=tag,player_id=member['player_id'],
                        row_after=after,matches_source=(after==source[0]) if after['status']!='unavailable' else None,
                        measurement_exception_type=measurement_error)
                except Exception as error:self._error('member',error,tag)
            if all((m['weak'],m['actor_weak']) in state['done'] for m in peers):
                state['complete']=True;self._next_scan=now+5.
        except Exception as error:self._error('context',error,tag)
        finally:self._busy=False
