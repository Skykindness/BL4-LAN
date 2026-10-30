"""Missing native lifecycle edges; policy stores scalar identities only."""

class PartyCoordinator:
    def __init__(self):
        self._context=None
        self._collector=None
        self._registration_attempted=set()
        self._registration_failed=set()
        self._queue_attempted=set()
        self._settling={}

    def _settled(self,phase,key,now):
        token=(phase,key)
        if token not in self._settling:
            self._settling[token]=now
            return False
        return now-self._settling[token]>=1.0

    def reconcile(self,context,collector_key,members,read_sources,register,enqueue,now):
        result=dict(registration_dispatched=0,queue_dispatched=0,errors=[])
        if not context or members is None or not collector_key:
            self._settling.clear()
            return result
        context=tuple(context)
        if context!=self._context:
            self.__init__()
            self._context=context
        if collector_key!=self._collector:
            self._collector=collector_key
            self._queue_attempted.clear()
            self._settling={k:v for k,v in self._settling.items() if k[0]=='register'}
        rows={}
        for member in members:
            key=member['key']
            if not isinstance(key,str) or not key:
                raise ValueError('Missing scalar native member identity')
            if key not in rows:rows[key]=member
        self._settling={k:v for k,v in self._settling.items() if k[1] in rows and rows[k[1]].get('ready') is True}
        sources=read_sources()
        if sources.get('collector_ready') is not True:
            self._settling.clear()
            return result
        for key,member in rows.items():
            if member.get('ready') is not True or key in sources['registered'] or key in self._registration_attempted:
                self._settling.pop(('register',key),None)
                continue
            if not self._settled('register',key,now):continue
            # A throwing callback can already have modified native state.
            self._registration_attempted.add(key)
            self._settling.pop(('register',key),None)
            try:register(member)
            except Exception as error:
                self._registration_failed.add(key)
                result['errors'].append(dict(phase='register',key=key,error=f'{type(error).__name__}: {error}'))
            else:result['registration_dispatched']+=1
            sources=read_sources()
            if sources.get('collector_ready') is not True:return result
        # Registration can synchronously broadcast OnPlayerConnected and fill
        # this queue. Always use post-callback sources for the second stage.
        sources=read_sources()
        for key,member in rows.items():
            if (member.get('ready') is not True or member.get('local') is not False
                    or key not in sources['registered'] or key in sources['pending']
                    or key in self._registration_failed or key in self._queue_attempted):
                self._settling.pop(('queue',key),None)
                continue
            if sources.get('collector_ready') is not True:return result
            if not self._settled('queue',key,now):continue
            self._queue_attempted.add(key)
            self._settling.pop(('queue',key),None)
            try:enqueue(member)
            except Exception as error:
                result['errors'].append(dict(phase='queue',key=key,error=f'{type(error).__name__}: {error}'))
            else:result['queue_dispatched']+=1
            sources=read_sources()
        return result
