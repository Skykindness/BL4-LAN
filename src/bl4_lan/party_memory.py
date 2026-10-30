"""Bounded read-only sources for the examined Oak2 Win64 executable only.

No native getters, constructors, code patches, writes or remote-process access.
The caller uses this on the engine thread; only executable hashing is threaded.
"""
import ctypes
import hashlib
from pathlib import Path
import struct
import sys
import threading

EXE_SHA256='ba75f7c2874b16efb5536a5c0cc28918a03efd3e55d790d2e4db57de0c3a353a'

def require(condition,message):
    if not condition:raise ValueError(message)

class NativeLayout:
    def __init__(self,read,base):self.read=read;self.base=base

    def number(self,address,fmt):
        return struct.unpack('<'+fmt,self.read(address,struct.calcsize('<'+fmt)))[0]

    def weak_identity(self,address):
        index=self.number(address+0xc,'i')
        require(0 <= index < self.number(self.base+0xc9647dc,'i'),'Stale native object index')
        chunks=self.number(self.base+0xc9647c8,'Q')
        chunk=self.number(chunks+(index>>16)*8,'Q')
        item=chunk+(index&65535)*24
        require(self.number(item,'Q')==address,'Recycled native object index')
        require(not self.number(item+8,'I')&0x10200000,'Native object is being destroyed')
        serial=self.number(item+16,'i')
        require(serial>0,'Native weak identity is not initialized')
        return index,serial

    def registry(self,world):
        cache=self.number(self.base+0xc9a7d10,'Q')
        kind=self.number(self.base+0xcb786a8,'i')
        require(cache and 0 < kind < 8192,'Native registry is not initialized')
        end=self.number(cache,'Q')+kind*0x78
        count,free=self.number(end-0x48,'i'),self.number(end-0x1c,'i')
        require(0 <= free <= count <= 256,'Unsupported native context table')
        require(count>free,'Current world registry is unavailable')
        h=(world>>4)&0xffffffff
        h=((h^(h>>16))*0x85ebca6b)&0xffffffff
        h=((h^(h>>13))*0xc2b2ae35)&0xffffffff
        h^=h>>16
        size=self.number(end-8,'i')
        require(0 < size <= 512 and size&(size-1)==0,'Unsupported world hash table')
        heads=self.number(end-0x10,'Q') or end-0x18
        index=self.number(heads+((size-1)&h)*4,'i')
        data=self.number(end-0x50,'Q')
        seen=set();manager=0
        while index!=-1:
            require(0 <= index < count and index not in seen and len(seen)<128,'Invalid world hash chain')
            seen.add(index);entry=data+index*32
            if self.number(entry,'Q')==world:
                manager=self.number(entry+8,'Q');break
            index=self.number(entry+0x18,'i')
        require(manager,'Current world registry is unavailable')
        source=manager+0xd8
        maximum,free=self.number(source+8,'i'),self.number(source+0x34,'i')
        require(0 <= free <= maximum <= 128,'Unsupported player registry size')
        if maximum==free:return set()
        size=self.number(source+0x48,'i')
        require(0 < size <= 512 and size&(size-1)==0,'Unsupported player hash table')
        heads=self.number(source+0x40,'Q') or source+0x38
        data=self.number(source,'Q')
        seen=set();members=set()
        for bucket in range(size):
            index=self.number(heads+bucket*4,'i')
            while index!=-1:
                require(0 <= index < maximum and index not in seen,'Invalid player hash chain')
                seen.add(index);entry=data+index*16
                members.add((self.number(entry,'i'),self.number(entry+4,'i')))
                index=self.number(entry+8,'i')
        require(len(seen)==maximum-free,'Native player registry changed during read')
        return members

    def buddy(self,address):
        ready=self.number(address+0x40,'B')
        require(ready in (0,1),'Unsupported collector initialization state')
        if not ready:return dict(ready=False,pending=set())
        data=self.number(address+0x78,'Q')
        count,capacity=self.number(address+0x80,'i'),self.number(address+0x84,'i')
        require(0 <= count <= capacity <= 128,'Unsupported native pending queue')
        require(not count or data,'Missing native pending storage')
        return dict(ready=True,pending={struct.unpack('<ii',self.read(data+i*8,8)) for i in range(count)})

class ProcessMemory:
    def __init__(self):
        require(sys.platform=='win32' and ctypes.sizeof(ctypes.c_void_p)==8,'Native party requires Win64')
        class MBI(ctypes.Structure):
            _fields_=[('BaseAddress',ctypes.c_void_p),('AllocationBase',ctypes.c_void_p),
                      ('AllocationProtect',ctypes.c_uint32),('Reserved',ctypes.c_uint32),
                      ('RegionSize',ctypes.c_size_t),('State',ctypes.c_uint32),
                      ('Protect',ctypes.c_uint32),('Type',ctypes.c_uint32)]
        self._MBI=MBI
        self._kernel=ctypes.WinDLL('kernel32',use_last_error=True)
        self._kernel.GetModuleHandleW.argtypes=[ctypes.c_wchar_p]
        self._kernel.GetModuleHandleW.restype=ctypes.c_void_p
        self._kernel.GetModuleFileNameW.argtypes=[ctypes.c_void_p,ctypes.c_wchar_p,ctypes.c_uint32]
        self._kernel.GetModuleFileNameW.restype=ctypes.c_uint32
        self._kernel.VirtualQuery.argtypes=[ctypes.c_void_p,ctypes.POINTER(MBI),ctypes.c_size_t]
        self._kernel.VirtualQuery.restype=ctypes.c_size_t
        self.base=self._kernel.GetModuleHandleW(None)
        name=ctypes.create_unicode_buffer(32768)
        length=self._kernel.GetModuleFileNameW(None,name,len(name))
        require(0 < length < len(name),'Executable path is unavailable')
        self._verified=threading.Event();self.error=None
        threading.Thread(target=self._verify,args=(Path(name.value),),daemon=True,name='BL4-party-build-check').start()
        self.layout=NativeLayout(self.read,self.base)

    def _verify(self,path):
        try:
            with path.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
            require(digest==EXE_SHA256,'This native party layout does not support the running executable')
        except Exception as error:self.error=str(error)
        finally:self._verified.set()

    @property
    def ready(self):return self._verified.is_set() and self.error is None

    def read(self,address,length):
        require(self.ready,'Native build verification is incomplete or unsupported')
        require(address and 0 < length <= 4096,'Invalid bounded native read')
        info=self._MBI()
        require(self._kernel.VirtualQuery(address,ctypes.byref(info),ctypes.sizeof(info)),'Native region is unavailable')
        require(info.State==0x1000 and not info.Protect&(0x100|1)
                and info.Protect&0xff in (2,4,8,0x20,0x40,0x80),'Native region is not readable')
        require(info.BaseAddress <= address and address+length <= info.BaseAddress+info.RegionSize,'Native read crosses region boundary')
        return ctypes.string_at(address,length)
