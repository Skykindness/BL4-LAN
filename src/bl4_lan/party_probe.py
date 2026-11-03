"""Bounded read-only sources for the pinned party audit; no native game calls."""
import hashlib
import struct
from .party_memory import NativeLayout,require


def name_summary(text):
    """Compare native/replicated names without putting a nickname in the archive."""
    require(isinstance(text,str),'name_type')
    return dict(status='present' if text else 'empty',characters=len(text),
                digest=hashlib.sha256(text.encode('utf-8')).hexdigest()[:16] if text else None)


class BudgetReader:
    def __init__(self,read,max_calls=512,max_bytes=65536):
        self.source=read;self.max_calls=max_calls;self.max_bytes=max_bytes
        self.calls=0;self.bytes=0

    def __call__(self,address,length):
        require(isinstance(address,int) and address>0 and isinstance(length,int)
                and 0<length<=4096,'read_bounds')
        require(self.calls<self.max_calls and self.bytes+length<=self.max_bytes,'read_budget')
        self.calls+=1;self.bytes+=length
        data=self.source(address,length)
        require(len(data)==length,'short_read')
        return data


class AuditIdentity(NativeLayout):
    def context_identity(self,address):
        """Validate strong-owned context objects without allocating a weak serial.

        Zero is legitimate for a strong-only object. Native PS/pawn links still
        use the inherited positive-serial weak_identity contract.
        """
        index=self.number(address+0xc,'i')
        require(0<=index<self.number(self.base+0xc9647dc,'i'),'context_index')
        chunks=self.number(self.base+0xc9647c8,'Q')
        chunk=self.number(chunks+(index>>16)*8,'Q');item=chunk+(index&65535)*24
        require(self.number(item,'Q')==address,'context_recycled')
        require(not self.number(item+8,'I')&0x10200000,'context_destroying')
        serial=self.number(item+16,'i');require(serial>=0,'context_serial')
        return index,serial


class ProbeLayout:
    def __init__(self,read,base):
        self.read=read if isinstance(read,BudgetReader) else BudgetReader(read)
        self.base=base

    def number(self,address,fmt):
        return struct.unpack('<'+fmt,self.read(address,struct.calcsize('<'+fmt)))[0]

    def text(self,address):
        data,count,capacity=struct.unpack('<Qii',self.read(address,16))
        require(0<=count<=capacity<=4096 and count<=129,'string_bounds')
        if not count:return ''
        require(data,'string_storage')
        raw=self.read(data,count*2)
        require(raw[-2:]==b'\0\0','string_terminator')
        return raw[:-2].decode('utf-16-le')

    def summary(self,address):
        return name_summary(self.text(address))

    @staticmethod
    def pointer_hash(value):
        value=(value>>4)&0xffffffff
        value=((value^(value>>16))*0x85ebca6b)&0xffffffff
        value=((value^(value>>13))*0xc2b2ae35)&0xffffffff
        return value^(value>>16)

    @staticmethod
    def name_key_hash(key):
        lo=key&0xffff;hi=(key>>16)&0xffff
        return ((key>>32)+((lo<<16)|lo)+((hi<<19)|hi)+(lo>>4))&0xffffffff

    def hash_lookup(self,*,data,count,free,buckets,size,stride,key,fmt,chain,hashed):
        require(0<=free<=count<=256,'map_bounds')
        if count==free:return None
        require(data and 0<size<=512 and size&(size-1)==0,'hash_bounds')
        index=self.number(buckets+(hashed&(size-1))*4,'i');seen=set()
        while index!=-1:
            require(0<=index<count and index not in seen and len(seen)<64,'hash_chain')
            seen.add(index);entry=data+index*stride
            if self.number(entry,fmt)==key:return entry
            index=self.number(entry+chain,'i')
        return None

    def player_names(self,singletons,members):
        manager=self.number(singletons+0x68,'Q')
        if not manager:return dict(status='absent',players=[])
        data,count=struct.unpack('<Qi',self.read(manager+8,12))
        require(0<=count<=32 and (not count or data),'manager_bounds')
        pointers=struct.unpack('<'+'Q'*count,self.read(data,count*8)) if count else ()
        records=[]
        for pointer in pointers:
            if not pointer:continue
            header=self.read(pointer,32)
            if struct.unpack_from('<h',header,4)[0]<0:
                records.append((pointer,header,struct.unpack_from('<ii',header,0x18)))
        rows=[]
        for member in members:
            row=dict(player_id=member['player_id'],lookup='missing',name=dict(status='unavailable'))
            matches=[r for r in records if r[2]==tuple(member['weak'])]
            if len(matches)>1:row['lookup']='ambiguous'
            elif matches:
                row['lookup']='matched'
                try:
                    pointer,header,_=matches[0]
                    handle=struct.unpack_from('<h',header,2)[0]
                    generation=struct.unpack_from('<H',header,8)[0]
                    require(0<=handle<count and pointers[handle],'handle_index')
                    current=pointers[handle];seen=set()
                    require(self.number(current+8,'I')==generation,'handle_generation')
                    while self.number(current+4,'h')>=0:
                        require(current not in seen and len(seen)<16,'alias_chain');seen.add(current)
                        alias=self.number(current+2,'h')
                        require(0<=alias<count and pointers[alias],'alias_index');current=pointers[alias]
                    require(current==pointer,'canonical_identity')
                    row.update(self._record_name(current))
                    require(self.read(pointer,32)==header,'record_changed')
                    replicated=member.get('replicated_name')
                    row['matches_replicated']=(row['name']==name_summary(replicated)) if isinstance(replicated,str) else None
                except (ValueError,KeyError,UnicodeError,struct.error,OSError):
                    row['name']=dict(status='unavailable');row['name_reason']='invalid_or_changed_source'
                    row['matches_replicated']=None
            rows.append(row)
        require(self.number(singletons+0x68,'Q')==manager
                and struct.unpack('<Qi',self.read(manager+8,12))==(data,count),'manager_changed')
        require(not count or struct.unpack('<'+'Q'*count,self.read(data,count*8))==pointers,'manager_records_changed')
        require(all(self.read(pointer,32)==header for pointer,header,_ in records),'manager_member_changed')
        return dict(status='ok',records=count,players=rows,source_kind='raw_context_name',display_processing_observed=False)

    def _record_name(self,record):
        key=self.number(record+0x110,'Q')
        data,count=struct.unpack('<Qi',self.read(record+0xc0,12));free=self.number(record+0xf4,'i')
        flag=self.number(record+0x11c,'B')
        require(0<=free<=count<=128,'name_map_bounds')
        reason='empty_map';text=''
        entry=None;string_header=None
        if count!=free:
            size=self.number(record+0x108,'i')
            buckets=self.number(record+0x100,'Q') or record+0xf8
            entry=self.hash_lookup(data=data,count=count,free=free,buckets=buckets,size=size,stride=32,
                key=key,fmt='Q',chain=0x18,hashed=self.name_key_hash(key))
            reason='missing_key'
            if entry:
                string_header=self.read(entry+8,16);text=self.text(entry+8);reason='map_entry'
                if flag==1:text=' (Bot)';reason='bot_literal'
        require(struct.unpack('<Qi',self.read(record+0xc0,12))==(data,count)
                and self.number(record+0xf4,'i')==free and self.number(record+0x110,'Q')==key
                and self.number(record+0x11c,'B')==flag,'name_map_changed')
        if count!=free:
            require(self.number(record+0x108,'i')==size
                    and (self.number(record+0x100,'Q') or record+0xf8)==buckets,'name_buckets_changed')
            require(self.hash_lookup(data=data,count=count,free=free,buckets=buckets,size=size,stride=32,
                        key=key,fmt='Q',chain=0x18,hashed=self.name_key_hash(key))==entry,'name_entry_changed')
            if entry is not None:
                require(self.number(entry,'Q')==key and self.read(entry+8,16)==string_header,'name_slot_changed')
        return dict(name=name_summary(text),name_reason=reason,context_present=bool(key),alias_hops=0)

    def world_rows(self,world,game_instance,members):
        require(self.number(world+0x1f0,'Q')==game_instance,'world_instance_changed')
        cls=self.number(self.base+0xcbb3c08,'Q')
        if not cls:return dict(status='unavailable',reason='subsystem_class_not_cached')
        collection=game_instance+0x100
        slots=self.number(collection+8,'Q');allocated=self.number(collection+0x10,'i')
        free=self.number(collection+0x3c,'i')
        require(0<=free<=allocated<=256,'subsystem_bounds')
        if allocated==free:return dict(status='unavailable',reason='exact_subsystem_not_found')
        bucket_count=self.number(collection+0x50,'i')
        buckets=self.number(collection+0x48,'Q') or collection+0x40
        entry=self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,size=bucket_count,
            stride=24,key=cls,fmt='Q',chain=0x10,hashed=self.pointer_hash(cls))
        if entry is None:return dict(status='unavailable',reason='exact_subsystem_not_found')
        subsystem=self.number(entry+8,'Q');require(subsystem,'missing_subsystem')
        wrapper=self.number(subsystem+0x30,'Q');require(wrapper,'missing_wrapper')
        container=self.number(wrapper,'Q');require(container,'missing_world_container')
        header=self.read(container,16);array,count,capacity=struct.unpack('<Qii',header)
        require(0<=count<=capacity<=2048 and count<=256 and (array or not count),'world_array_bounds')
        version=self.number(container+0x40,'I')
        selected={m['actor_index']:[] for m in members if m.get('actor_index') is not None}
        for i in range(count):
            row=array+i*0x120;actor=self.number(row+0x38,'Q')
            if actor not in selected:continue
            selected[actor].append(dict(name=self.summary(row+0x20),
                name_version=self.number(row+0x30,'I'),active_value=self.number(row+0xd0,'B'),
                active_version=self.number(row+0xd4,'I'),player_id_input=self.number(row+0xa8,'I'),
                down_value=self.number(row+0x50,'B'),revive_value=self.number(row+0x58,'B'),
                converter_exception_value=self.number(row+0xe0,'B')))
        require(self.number(world+0x1f0,'Q')==game_instance and self.number(self.base+0xcbb3c08,'Q')==cls
                and self.number(collection+8,'Q')==slots and self.number(collection+0x10,'i')==allocated
                and self.number(collection+0x3c,'i')==free and self.number(collection+0x50,'i')==bucket_count
                and (self.number(collection+0x48,'Q') or collection+0x40)==buckets,'subsystem_root_changed')
        require(self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,size=bucket_count,
                    stride=24,key=cls,fmt='Q',chain=0x10,hashed=self.pointer_hash(cls))==entry,'subsystem_entry_changed')
        require(self.read(container,16)==header and self.number(container+0x40,'I')==version
                and self.number(entry,'Q')==cls and self.number(entry+8,'Q')==subsystem
                and self.number(subsystem+0x30,'Q')==wrapper and self.number(wrapper,'Q')==container,
                'world_source_changed')
        players=[]
        for member in members:
            matches=selected.get(member.get('actor_index'),[])
            row=dict(player_id=member['player_id'],row_status='present' if len(matches)==1 else 'absent' if not matches else 'ambiguous')
            if len(matches)==1:row.update(matches[0])
            players.append(row)
        return dict(status='ok',total_rows=count,version=version,players=players)

    def matchmaking(self,key):
        """Read the owned collector's source; it is not an active widget probe."""
        origin=dict(context_origin='owned_matchmaking_collector',rendered_widget_context_observed=False)
        try:
            require(isinstance(key,int) and not isinstance(key,bool) and 0<key<=0xffffffff,'local_key_bounds')
            root_offsets=((0xcbd5628,'Q'),(0xcbd5630,'i'),(0xcbd565c,'i'),
                          (0xcbd5668,'Q'),(0xcbd5670,'i'))
            root=tuple(self.number(self.base+offset,fmt) for offset,fmt in root_offsets)
            slots,allocated,free,external,bucket_count=root
            buckets=external or self.base+0xcbd5660
            entry=self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,
                size=bucket_count,stride=32,key=key,fmt='I',chain=0x18,hashed=key)
            if entry is None:return dict(origin,status='unavailable',reason='local_repository_not_found')
            repo=self.number(entry+8,'Q')
            require(repo,'missing_local_repo')
            source=self.number(repo+0x480,'Q')
            if not source:return dict(origin,status='unavailable',reason='matchmaking_source_not_found')
            header=self.read(source,0xc0)
            active=header[8];state=struct.unpack_from('<i',header,0x20)[0]
            remaining=struct.unpack_from('<i',header,0x58)[0]
            array,count,capacity=struct.unpack_from('<Qii',header,0x78)
            version=struct.unpack_from('<I',header,0xb8)[0]
            require(0<=count<=capacity<=256 and count<=64 and (array or not count),'activity_array_bounds')
            text=self.summary(source+0x40)
            require(tuple(self.number(self.base+offset,fmt) for offset,fmt in root_offsets)==root,
                    'matchmaking_registry_changed')
            require(self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,
                        size=bucket_count,stride=32,key=key,fmt='I',chain=0x18,hashed=key)==entry,
                    'matchmaking_entry_changed')
            require(self.number(entry,'I')==key and self.number(entry+8,'Q')==repo
                    and self.number(repo+0x480,'Q')==source and self.read(source,0xc0)==header,
                    'matchmaking_source_changed')
            return dict(origin,status='ok',state_numeric=state,active_value=active,
                        remaining_seconds=remaining,activity_count=count,activity_text=text,version=version)
        except (KeyError,UnicodeError,struct.error,OSError):
            return dict(origin,status='unavailable',reason='invalid_or_changed_source')
        except ValueError as error:
            reason=str(error)
            known={'local_key_bounds','map_bounds','hash_bounds','hash_chain','missing_local_repo',
                   'activity_array_bounds','string_bounds','string_storage','string_terminator',
                   'matchmaking_registry_changed','matchmaking_entry_changed','matchmaking_source_changed',
                   'read_bounds','read_budget','short_read'}
            return dict(origin,status='unavailable',reason=reason if reason in known else 'invalid_or_changed_source')

    def lobby(self,key):
        require(isinstance(key,int) and 0<=key<=0xffffffff,'local_key_bounds')
        slots=self.number(self.base+0xcbd5628,'Q');allocated=self.number(self.base+0xcbd5630,'i')
        free=self.number(self.base+0xcbd565c,'i');bucket_count=self.number(self.base+0xcbd5670,'i')
        buckets=self.number(self.base+0xcbd5668,'Q') or self.base+0xcbd5660
        entry=self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,size=bucket_count,
            stride=32,key=key,fmt='I',chain=0x18,hashed=key)
        if entry is None:return dict(status='unavailable',reason='local_repository_not_found')
        repo=self.number(entry+8,'Q');require(repo,'missing_local_repo')
        source=self.number(repo+0x110,'Q');require(source,'missing_lobby_source')
        version=self.number(source+0xf8,'I');header=self.read(source+0x18,16)
        array,count,capacity=struct.unpack('<Qii',header)
        require(0<=count<=capacity<=256 and count<=4 and (array or not count),'lobby_array_bounds')
        rows=[]
        for i in range(count):
            row=array+i*0x198
            rows.append(dict(identifier=self.summary(row+0x10),name=self.summary(row+0x28),
                character_name_words=list(struct.unpack('<II',self.read(row+0x88,8))),
                leader_value=self.number(row+0xd8,'B'),local_value=self.number(row+0xf8,'B'),
                local_number=self.number(row+0x100,'i'),ready_value=self.number(row+0x160,'B')))
        counts={f'input_{offset:x}':self.number(source+offset,'i') for offset in (0xb8,0xc0,0xc8,0xd0)}
        require(self.number(self.base+0xcbd5628,'Q')==slots and self.number(self.base+0xcbd5630,'i')==allocated
                and self.number(self.base+0xcbd565c,'i')==free and self.number(self.base+0xcbd5670,'i')==bucket_count
                and (self.number(self.base+0xcbd5668,'Q') or self.base+0xcbd5660)==buckets,'lobby_registry_changed')
        require(self.hash_lookup(data=slots,count=allocated,free=free,buckets=buckets,size=bucket_count,
                    stride=32,key=key,fmt='I',chain=0x18,hashed=key)==entry,'lobby_registry_entry_changed')
        require(self.number(source+0xf8,'I')==version and self.read(source+0x18,16)==header
                and self.number(repo+0x110,'Q')==source and self.number(entry,'I')==key
                and self.number(entry+8,'Q')==repo,'lobby_source_changed')
        return dict(status='ok',context_origin='owned_buddy_collector',rendered_widget_context_observed=False,
                    row_count=count,version=version,rows=rows,counts=counts)

