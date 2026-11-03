"""Bounded operation breadcrumbs; no Unreal imports or native actions."""
from contextlib import contextmanager
import json
import math
import os
from pathlib import Path
import re
import time


class OpTrace:
    OPERATIONS=frozenset(('party_bridge_initialize','party_audit_initialize',
        'party_bridge_sync','party_audit_sync','player_state_registration',
        'buddy_collector_enqueue','overhead_name_refresh','native_name_probe',
        'native_travel','native_state_observation','buddy_collector_scan','native_callback_signatures'))
    MAX_EDGES=64
    MAX_DEPTH=8
    MAX_BYTES=2048

    def __init__(self,root,pid,instance,*,clock=time.time,max_edges=64):
        self._clock=clock;self._attempt=None;self._stack=[];self._edges=0;self._sequence=0
        self._generation=0;self._valid=False;self._path=None;self._limit=0
        self.pid=pid;self.instance=instance;self.error=None
        try:
            self._valid=(isinstance(pid,int) and not isinstance(pid,bool) and 0<pid<=0x7fffffff
                and self._opaque(instance) and isinstance(max_edges,int) and not isinstance(max_edges,bool)
                and 2<=max_edges<=self.MAX_EDGES and callable(clock))
            if self._valid:
                self._path=Path(root)/('native-operation-'+str(pid)+'.json');self._limit=max_edges
        except Exception:self._valid=False

    @staticmethod
    def _opaque(value):
        return isinstance(value,str) and re.fullmatch(r'[A-Za-z0-9_-]{1,64}',value) is not None

    def arm(self,attempt_id):
        """Bind a new attempt without I/O; same-attempt polls cannot reset budgets."""
        try:
            if not self._valid or not self._opaque(attempt_id):
                self._attempt=None;self._generation+=1;self._stack.clear();return False
            if attempt_id!=self._attempt:
                self._attempt=attempt_id;self._generation+=1;self._stack.clear()
                self._edges=0;self._sequence=0;self.error=None
            return True
        except Exception:return False

    def _now(self):
        value=float(self._clock())
        if not math.isfinite(value) or value<0:raise ValueError('invalid_clock')
        return value

    def _write(self,record,phase,completed=None):
        temporary=None
        try:
            row=dict(schema=1,pid=self.pid,instance=self.instance,attempt_id=self._attempt,
                timestamp=self._now(),phase=phase,operation=record['operation'],
                sequence=record['sequence'],depth=len(self._stack),
                pending=[dict(operation=r['operation'],sequence=r['sequence'],started_at=r['started_at'])
                         for _,_,r in self._stack])
            if completed is not None:row['completed']=completed
            body=json.dumps(row,ensure_ascii=True,allow_nan=False,separators=(',',':')).encode('utf-8')
            if len(body)>self.MAX_BYTES:raise ValueError('trace_size')
            self._path.parent.mkdir(parents=True,exist_ok=True)
            temporary=self._path.with_suffix('.tmp')
            with temporary.open('wb') as stream:
                stream.write(body);stream.flush();os.fsync(stream.fileno())
            os.replace(temporary,self._path);self.error=None
        except Exception:self.error='write_unavailable'
        finally:
            if temporary is not None:
                try:temporary.unlink(missing_ok=True)
                except Exception:pass

    def begin(self,operation):
        """Persist a pending edge before the caller enters a risky operation."""
        try:
            if (not self._valid or self._attempt is None or operation not in self.OPERATIONS
                or len(self._stack)>=self.MAX_DEPTH or self._edges+len(self._stack)+2>self._limit):return None
            started=self._now();self._sequence+=1
            token=object();record=dict(operation=operation,sequence=self._sequence,started_at=started)
            self._stack.append((token,self._generation,record));self._edges+=1
            self._write(record,'started');return token
        except Exception:return None

    def end(self,token,outcome='returned'):
        """Complete only the current own edge. An outer pending edge stays visible."""
        try:
            if (not self._stack or token is not self._stack[-1][0]
                or self._stack[-1][1]!=self._generation or outcome not in ('returned','raised')):return False
            _,_,record=self._stack.pop();self._edges+=1
            completed=dict(operation=record['operation'],outcome=outcome)
            self._write(self._stack[-1][2] if self._stack else record,
                        'started' if self._stack else outcome,completed)
            return True
        except Exception:return False

    @contextmanager
    def operation(self,name):
        """Tracing failures never replace or suppress the operation's outcome."""
        token=self.begin(name)
        try:yield
        except BaseException:
            self.end(token,'raised');raise
        else:self.end(token)
