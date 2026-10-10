"""Bounded original Testcontainers wire adapter SOURCE; production stays closed.

Original requests are never silently changed. Only an exact reviewed transform
may replace caps/labels or a Ryuk mount. Synthetic controls are not admission.
"""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import select
import socket
import stat
import struct
import time
from urllib.parse import urlsplit,parse_qsl,unquote
import employee_fixture_registry_compatibility as custody

ROOT_PUBLIC_KEY_SHA256=None
BACKEND_QUALIFICATION_SHA256=None
UPSTREAM_COMMIT='d66a9f12a63d1082580270a310a77463ce01272c'
MAX_HEADER=8192
MAX_BODY=65536
MAX_OUTPUT=1048576
ROUTES={'info','version','ping','create','start','stop','inspect','wait','exec-create','exec-start','exec-inspect','logs','delete','image-inspect','image-pull','image-list','container-list','events','attach'}

def need(value,message):custody.need(value,message)
def digest(raw):return hashlib.sha256(raw).hexdigest()

def route(method,target):
    need(type(method) is str and type(target) is str and len(target)<=4096 and target.startswith('/') and not any(c in target for c in '\r\n\x00'),'bounded literal target')
    u=urlsplit(target);need(not u.scheme and not u.netloc and not u.fragment,'origin target only')
    path=u.path
    if path.startswith('/v'):
        match=re.match(r'/v1\.44(?=/)',path);need(match is not None,'pinned API 1.44 only');path=path[match.end():]
    rows=parse_qsl(u.query,keep_blank_values=True,strict_parsing=True,max_num_fields=16) if u.query else []
    q={}
    for key,value in rows:need(key not in q and len(value)<=3072,'duplicate/bounded query');q[key]=value
    result=None;ident=None
    if path in {'/info','/version','/_ping'} and method in {'GET','HEAD'}:result={'/info':'info','/version':'version','/_ping':'ping'}[path];allowed=set()
    elif path=='/containers/create' and method=='POST':result='create';allowed={'name','platform'};need(type(q.get('name')) is str and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,127}',q['name']),'original dynamic container name')
    elif path=='/containers/json' and method=='GET':result='container-list';allowed={'all','filters','limit','size'}
    elif path=='/images/json' and method=='GET':result='image-list';allowed={'all','filters'}
    elif path=='/images/create' and method=='POST':result='image-pull';allowed={'fromImage','tag','platform'};need(bool(q.get('fromImage')),'pinned image required')
    elif path=='/events' and method=='GET':result='events';allowed={'since','until','filters'}
    else:
        match=re.fullmatch(r'/containers/([0-9a-f]{64})(?:/(json|start|stop|wait|exec|logs|attach))?',path)
        if match:
            ident,op=match.groups()
            mapping={(None,'DELETE'):('delete',{'force','v','link'}),('json','GET'):('inspect',set()),('start','POST'):('start',{'detachKeys'}),('stop','POST'):('stop',{'t','signal'}),('wait','POST'):('wait',{'condition'}),('exec','POST'):('exec-create',set()),('logs','GET'):('logs',{'stdout','stderr','since','until','timestamps','follow','tail'}),('attach','POST'):('attach',{'stdout','stderr','stream','logs','stdin','detachKeys'})}
            need((op,method) in mapping,'container operation refused');result,allowed=mapping[(op,method)]
        else:
            match=re.fullmatch(r'/exec/([0-9a-f]{64})/(start|json)',path)
            if match:
                ident,op=match.groups();need((op,method) in {('start','POST'),('json','GET')},'exec method');result='exec-start' if op=='start' else 'exec-inspect';allowed=set()
            elif path.startswith('/images/') and path.endswith('/json') and method=='GET':
                ident=unquote(path[8:-5]);result='image-inspect';allowed=set();need(ident in custody.IMAGES or re.fullmatch(r'sha256:[0-9a-f]{64}',ident),'image identity only')
    need(result is not None and result in ROUTES and set(q)<=allowed,'unqualified route/query refused')
    if 'platform' in q:need(q['platform'] in {'linux/amd64'},'fixed reviewed platform')
    if 'condition' in q:need(q['condition'] in {'not-running','next-exit','removed'},'wait condition')
    for key in set(q)&{'all','size','stdout','stderr','timestamps','follow','stream','logs','stdin','force','v','link'}:need(q[key] in {'0','1','true','false'},'canonical boolean query')
    if 'signal' in q:need(q['signal'] in {'SIGTERM','15'},'only graceful stop signal')
    return result,ident,q

class Deadline:
    def __init__(self,seconds,monotonic=time.monotonic,guard=lambda:True):
        need(type(seconds) in {int,float} and 0<seconds<=120,'finite operation wall');self.clock=monotonic;self.guard=guard;self.end=self.clock()+seconds
    def remaining(self):
        need(self.guard() is True,'controller death/lease guardian refusal')
        value=self.end-self.clock()
        if value<=0:raise TimeoutError('aggregate wire deadline')
        return value

class Reader:
    def __init__(self,conn,deadline,limit=MAX_OUTPUT):self.conn=conn;self.deadline=deadline;self.buffer=bytearray();self.total=0;self.limit=limit
    def more(self):
        while True:
            self.conn.settimeout(min(0.25,self.deadline.remaining()))
            try:raw=self.conn.recv(4096);break
            except socket.timeout:self.deadline.remaining()
        need(type(raw) is bytes and len(raw)<=4096,'literal bounded socket read')
        self.total+=len(raw);need(self.total<=self.limit+MAX_HEADER+65536,'aggregate wire allocation bound');self.buffer.extend(raw);return bool(raw)
    def exact(self,size):
        need(type(size) is int and 0<=size<=self.limit,'frame bound')
        while len(self.buffer)<size:need(self.more(),'truncated wire frame')
        raw=bytes(self.buffer[:size]);del self.buffer[:size];return raw
    def line(self,limit):
        while True:
            pos=self.buffer.find(b'\r\n')
            if pos>=0:need(pos<=limit,'wire line bound');raw=bytes(self.buffer[:pos]);del self.buffer[:pos+2];return raw
            need(len(self.buffer)<=limit and self.more(),'wire line EOF/bound')

def read_response(conn,deadline,*,method='GET',stream=False):
    reader=Reader(conn,deadline);first=reader.line(256)
    match=re.fullmatch(rb'HTTP/1\.[01] ([1-5][0-9]{2}) [\x20-\x7e]{0,160}',first);need(match,'strict daemon response status');status=int(match[1]);headers={};size=len(first)+2
    while True:
        line=reader.line(MAX_HEADER);size+=len(line)+2;need(size<=MAX_HEADER,'response headers bound')
        if not line:break
        need(b':' in line and not line.startswith((b' ',b'\t')),'folded response refused');key,value=line.split(b':',1);key=key.lower();value=value.strip()
        need(re.fullmatch(rb'[a-z0-9-]{1,64}',key) and key not in headers and re.fullmatch(rb'[\x20-\x7e]*',value),'duplicate/control response header');headers[key]=value
    need(not (b'content-length' in headers and b'transfer-encoding' in headers),'ambiguous response framing')
    body=bytearray()
    if method=='HEAD' or status in {204,304}:need(not reader.buffer,'surplus no-body response')
    elif status==101:
        need(stream and headers.get(b'upgrade')==b'tcp' and headers.get(b'connection',b'').lower()==b'upgrade','reviewed Docker hijack only')
        while True:
            body.extend(reader.buffer);reader.buffer.clear();need(len(body)<=MAX_OUTPUT,'stream output bound')
            if not reader.more():break
    elif b'transfer-encoding' in headers:
        need(headers[b'transfer-encoding'].lower()==b'chunked','unknown transfer coding')
        while True:
            line=reader.line(32);need(re.fullmatch(rb'[0-9a-fA-F]{1,8}',line),'strict chunk size; extensions refused');count=int(line,16)
            if count==0:need(reader.line(0)==b'','trailers refused');break
            need(len(body)+count<=MAX_OUTPUT,'chunk aggregate bound');body.extend(reader.exact(count));need(reader.exact(2)==b'\r\n','chunk terminator')
        need(not reader.buffer,'chunk surplus response')
    elif b'content-length' in headers:
        length=headers[b'content-length'];need(re.fullmatch(rb'0|[1-9][0-9]{0,7}',length),'canonical response length');body.extend(reader.exact(int(length)));need(not reader.buffer,'surplus response')
    else:
        need(stream or headers.get(b'connection',b'').lower()==b'close','unframed persistent response refused')
        while True:
            body.extend(reader.buffer);reader.buffer.clear();need(len(body)<=MAX_OUTPUT,'EOF output bound')
            if not reader.more():break
    deadline.remaining();return status,headers,bytes(body)

def multiplex(raw):
    """Verify Docker stdout/stderr frame structure without decoding or rewriting."""
    need(type(raw) is bytes and len(raw)<=MAX_OUTPUT,'bounded multiplex bytes');i=0
    while i<len(raw):
        need(i+8<=len(raw) and raw[i] in {1,2} and raw[i+1:i+4]==b'\0\0\0','multiplex frame header')
        size=struct.unpack('>I',raw[i+4:i+8])[0];i+=8;need(i+size<=len(raw),'multiplex frame size');i+=size
    return raw

def anonymous_registry_header(value):
    """Validate public-registry-only AuthConfig and retain original wire bytes."""
    import base64
    need(type(value) is bytes and 0<len(value)<=4096 and not any(c in value for c in b'\r\n'),'bounded exact registry auth header')
    raw=base64.b64decode(value,validate=True);item=custody.parse(raw)
    need(type(item) is dict and set(item)<={'serveraddress','username','password','email','auth','identitytoken','registrytoken'},'original anonymous AuthConfig fields only')
    need(all(item[key] in (None,'') for key in item if key!='serveraddress'),'registry credentials prohibited; never persisted')
    need(item.get('serveraddress') in (None,'','docker.io','index.docker.io','registry-1.docker.io','https://index.docker.io/v1/'),'approved public Docker registry only')
    return value

class UnixForwarder:
    """Concrete per-operation socket forwarding; no thread/blocking callback."""
    def __init__(self,path,expected_socket,expected_daemon,*,mode='production',socket_factory=None,identity=None,peer_generation=None,monotonic=time.monotonic,owner_fd=None,owner_alive=None):
        self.mode=mode;self.path=path;self.expected_socket=expected_socket;self.expected_daemon=expected_daemon;self.monotonic=monotonic
        if mode=='production':
            if not custody.HEX.fullmatch(ROOT_PUBLIC_KEY_SHA256 or '') or not custody.HEX.fullmatch(BACKEND_QUALIFICATION_SHA256 or ''):raise PermissionError('Root/backend absent before Unix socket creation')
            need(socket_factory is None and identity is None and peer_generation is None and owner_alive is None,'production syscall injection refused')
            need(type(owner_fd) is int and owner_fd>=0 and stat.S_ISFIFO(os.fstat(owner_fd).st_mode),'retained qualified controller pipe')
            self.owner_alive=lambda:not select.select([owner_fd],[],[],0)[0]
            self.socket_factory=lambda:socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);self.identity=self._identity;self.peer_generation=self._generation
        else:
            need(mode=='synthetic-source-control' and all(callable(x) for x in (socket_factory,identity,peer_generation)),'explicit fake sockets/observations required')
            self.socket_factory=socket_factory;self.identity=identity;self.peer_generation=peer_generation;self.owner_alive=owner_alive or (lambda:True)
        need(type(path) is str and path.startswith('/') and len(path.encode())<=100 and '..' not in Path(path).parts,'private Unix endpoint only')
    @staticmethod
    def _identity(path):
        item=os.lstat(path);need(stat.S_ISSOCK(item.st_mode),'real Unix socket required');return {'device':item.st_dev,'inode':item.st_ino,'uid':item.st_uid,'gid':item.st_gid,'mode':stat.S_IMODE(item.st_mode)}
    @staticmethod
    def _generation(conn):
        raw=conn.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12);need(len(raw)==12,'exact Linux credential layout');pid,uid,gid=struct.unpack('3i',raw)
        def read(path,cap):
            with open(path,'rb') as f:raw=f.read(cap+1)
            need(len(raw)<=cap,'kernel observation bound');return raw
        first=read('/proc/'+str(pid)+'/stat',8192).rsplit(b')',1)[1].split()[19].decode()
        fd=os.pidfd_open(pid)
        try:
            boot=read('/proc/sys/kernel/random/boot_id',64).strip().decode();groups=read('/proc/'+str(pid)+'/cgroup',8192).decode().splitlines();need(len(groups)==1 and groups[0].startswith('0::/'),'unified exact daemon cgroup')
            second=read('/proc/'+str(pid)+'/stat',8192).rsplit(b')',1)[1].split()[19].decode();need(first==second and not select.select([fd],[],[],0)[0],'live stable daemon PID')
            return {'pid':pid,'uid':uid,'gid':gid,'birth':first,'bootId':boot,'cgroup':groups[0][3:]}
        finally:os.close(fd)
    def request(self,method,target,body=b'',*,seconds=2,stream=False,extra_headers=None):
        need(type(body) is bytes and len(body)<=MAX_BODY,'bounded exact request bytes');route(method,target)
        need(extra_headers is None or type(extra_headers) is dict and set(extra_headers)<={b'x-registry-auth'},'unreviewed request headers refused')
        if extra_headers:need(route(method,target)[0]=='image-pull','registry header only public image pull');anonymous_registry_header(extra_headers[b'x-registry-auth'])
        deadline=Deadline(seconds,self.monotonic,self.owner_alive);conn=None
        need(custody.encode(self.identity(self.path))==custody.encode(self.expected_socket),'owned daemon socket generation differs')
        try:
            conn=self.socket_factory();conn.settimeout(min(.25,deadline.remaining()));conn.connect(self.path)
            need(custody.encode(self.peer_generation(conn))==custody.encode(self.expected_daemon),'exact authenticated daemon peer differs')
            raw=(method+' '+target+' HTTP/1.1\r\nHost: docker\r\nContent-Type: application/json\r\nContent-Length: '+str(len(body))+'\r\nConnection: '+('Upgrade\r\nUpgrade: tcp' if stream else 'close')+'\r\n\r\n').encode('ascii')+body
            if extra_headers:
                head,content=raw.split(b'\r\n\r\n',1);raw=head+b'\r\nX-Registry-Auth: '+extra_headers[b'x-registry-auth']+b'\r\n\r\n'+content
            view=memoryview(raw)
            while view:
                conn.settimeout(min(.25,deadline.remaining()));count=conn.send(view);need(type(count) is int and 0<count<=len(view),'ambiguous send ACK');view=view[count:]
            result=read_response(conn,deadline,method=method,stream=stream)
            need(custody.encode(self.identity(self.path))==custody.encode(self.expected_socket) and custody.encode(self.peer_generation(conn))==custody.encode(self.expected_daemon),'daemon custody drift after wire')
            return result
        finally:
            if conn is not None:conn.close()

class OriginalFixtureBackend:
    """Executable source dispatcher. Production constructor remains disabled."""
    def __init__(self,forwarder,journal,budget,plans,images,sdk_generation,observer,*,mode='production',clock=lambda:dt.datetime.now(dt.timezone.utc),daemon_id=None):
        if mode=='production':
            if not custody.HEX.fullmatch(ROOT_PUBLIC_KEY_SHA256 or '') or not custody.HEX.fullmatch(BACKEND_QUALIFICATION_SHA256 or ''):raise PermissionError('no independently enrolled Root/backend admission')
            need(all(type(plan) is custody.SignedPlan for plan in plans.values()),'exact authenticated plans')
        else:need(mode=='synthetic-source-control' and forwarder.mode=='synthetic-source-control','synthetic dispatch cannot admit actual forwarding')
        need(type(daemon_id) is str and 0<len(daemon_id)<=128,'independently reviewed daemon ID required');self.daemon_id=daemon_id
        custody.generation(sdk_generation);self.sdk=sdk_generation;self.forwarder=forwarder;self.journal=journal;self.budget=budget;self.plans=plans;self.images=images;self.observer=observer;self.mode=mode;self.clock=clock;self.owned={};self.retired={};self.execs={};self.quarantined=set();self.operations=set();self.current_deadline=None
    def _wire(self,method,target,body=b'',*,seconds=2,stream=False,extra_headers=None):
        need(self.current_deadline is not None,'one finite aggregate operation required')
        return self.forwarder.request(method,target,body,seconds=min(seconds,self.current_deadline.remaining()),stream=stream,extra_headers=extra_headers)
    def _observe(self,cleanup=False):
        value=self.observer();need(type(value) is dict and set(value)=={'sdkGeneration','freePhysicalKiB','observedUtc','sdkActive','sdkRetired','clientsEmpty','expiresUtc'},'closed actual admission observation')
        now=self.clock();need(custody.encode(value['sdkGeneration'])==custody.encode(self.sdk) and 0<=(now-custody.utc(value['observedUtc'])).total_seconds()<=2,'fresh type-exact SDK observation')
        if cleanup:need(value['sdkRetired'] is True and value['clientsEmpty'] is True,'SDK-first actual retirement/client absence')
        else:need(value['sdkActive'] is True and type(value['freePhysicalKiB']) is int and value['freePhysicalKiB']>=4194304 and now<custody.utc(value['expiresUtc']) and now<custody.utc(self.sdk['expiresUtc']),'fresh 4GiB/full phase/current authority')
        info_status,_,info_raw=self._wire('GET','/v1.44/info');need(info_status==200 and custody.parse(info_raw).get('ID')==self.daemon_id,'actual healthy exact daemon ID')
        return value
    def _owned(self,cid):
        need(cid in self.owned and cid not in self.quarantined,'exact retained allocation required; no inventory/name adoption')
        status,_,raw=self._wire('GET','/v1.44/containers/'+cid+'/json');need(status==200,'actual owned inspect required');row=custody.parse(raw);expected=self.owned[cid]
        need(type(row) is dict and row.get('Id')==cid and row.get('Created')==expected['rawCreated'] and row.get('Image')==expected['imageId'] and row.get('Name')=='/'+expected['name'] and row.get('Config',{}).get('Labels')==expected['labels'],'fullID/rawCreated/image/name/labels differ')
        for key in ('Memory','MemorySwap','NanoCpus','PidsLimit','Privileged','CapDrop','SecurityOpt','NetworkMode','Binds','Mounts','Tmpfs'):
            need(row.get('HostConfig',{}).get(key)==expected['hostConfig'].get(key),'actual effective containment differs')
        need(digest(custody.encode(row.get('Config')))==expected['inspectConfigSha256'] and digest(custody.encode(row.get('HostConfig')))==expected['inspectHostConfigSha256'],'reviewed full Config/HostConfig projection differs')
        mounts=row.get('Mounts');need(type(mounts) is list and len(mounts)<=1,'bounded actual mounts; no persistent volumes')
        for mount in mounts:
            need(type(mount) is dict and (mount.get('Type')=='tmpfs' and mount.get('Destination') in {'/data','/var/lib/postgresql'} or mount.get('Type')=='bind' and expected['ryukSocketPath'] is not None and mount.get('Source')==expected['ryukSocketPath'] and mount.get('Destination')=='/var/run/docker.sock'),'foreign/persistent actual mount refused')
        execs=row.get('ExecIDs');need(execs is None or type(execs) is list and all(x in self.execs and self.execs[x]==cid for x in execs),'unknown exec custody')
        return row
    def _intent(self,op,action,cid,body):
        need(type(op) is str and custody.NONCE.fullmatch(op) and op not in self.operations,'durable operation replay refuses');self.operations.add(op)
        return self.journal.write(op,'intent',{'action':action,'containerId':cid,'rawRequestSha256':digest(body),'sdkGeneration':self.sdk,'sourceOnly':True,'executionGranted':False})
    def _pre_mutation(self,observation,expires=None):
        now=self.clock();need(0<=(now-custody.utc(observation['observedUtc'])).total_seconds()<=2 and now<custody.utc(observation['expiresUtc']) and now<custody.utc(self.sdk['expiresUtc']),'fresh admission and exact SDK lease immediately before forward')
        if expires is not None:need(now<custody.utc(expires),'fixture transformation lease immediately before forward')
    def _absence(self,cid):
        status,headers,raw=self._wire('GET','/v1.44/containers/'+cid+'/json');need(status==404,'actual same-daemon full ID404')
        healthy_status,_,healthy_raw=self._wire('GET','/v1.44/info');need(healthy_status==200 and custody.parse(healthy_raw).get('ID')==self.daemon_id,'healthy exact daemon after deletion')
        inventory_status,_,inventory_raw=self._wire('GET','/v1.44/containers/json?all=true');inventory=custody.parse(inventory_raw);need(inventory_status==200 and type(inventory) is list and len(inventory)<=4096 and all(type(row) is dict and type(row.get('Id')) is str and custody.ID.fullmatch(row['Id']) for row in inventory) and len({row['Id'] for row in inventory})==len(inventory) and cid not in {row['Id'] for row in inventory},'healthy full-ID inventory actual absence')
        return status,headers,raw
    def dispatch(self,op,method,target,body=b'',*,cleanup_origin=None,extra_headers=None):
        need(self.current_deadline is None,'serialized backend connection required')
        self.current_deadline=Deadline(30)
        try:return self._dispatch(op,method,target,body,cleanup_origin,extra_headers)
        finally:self.current_deadline=None
    def _dispatch(self,op,method,target,body=b'',cleanup_origin=None,extra_headers=None):
        action,cid,q=route(method,target);need(type(body) is bytes and len(body)<=MAX_BODY,'bounded original body')
        cleanup=action in {'stop','delete'}
        if cleanup:need(cleanup_origin in {'foreground-sdk','retired-ryuk'},'explicit verified caller cleanup phase required')
        obs=self._observe(cleanup and cleanup_origin=='retired-ryuk')
        if action=='inspect' and cid in self.retired:
            need(cid not in self.owned and cid not in self.quarantined,'completed exact retained retirement only')
            return self._absence(cid)
        mutation=action in {'create','start','stop','delete','exec-create','exec-start','image-pull'};sent=False;allocated=None
        try:
            if action=='create':
                plan_object=self.plans.get(digest(body));need(plan_object is not None,'exact signed transformation required; no spontaneous raw SHA enrollment')
                plan_raw=plan_object.verify() if type(plan_object) is custody.SignedPlan else plan_object
                effective,receipt=custody.transform(body,plan_raw);plan=custody.parse(plan_raw);need(plan['daemonId']==self.daemon_id and plan['name']==q['name'] and custody.encode(plan['sdkGeneration'])==custody.encode(self.sdk) and self.clock()<custody.utc(plan['expiresUtc']),'plan identity/SDK/expiry')
                nonce=op;self.budget.reserve(nonce,plan['hostConfig']['Memory'],plan['hostConfig']['NanoCpus'],obs['freePhysicalKiB']);allocated=nonce
                self._intent(op,action,None,body);self._pre_mutation(obs,plan['expiresUtc']);sent=True;status,headers,raw=self._wire(method,target,effective)
                need(status==201,'create actual201 ACK');created=custody.parse(raw);need(type(created) is dict and type(created.get('Id')) is str and custody.ID.fullmatch(created['Id']) and created['Id'] not in self.owned,'fresh full ID ACK');cid=created['Id']
                self.owned[cid]={'nonce':nonce,'rawCreated':None,'imageId':plan['imageId'],'name':q['name'],'labels':custody.parse(effective)['Labels'],'hostConfig':plan['hostConfig'],'sessionId':plan['sessionId'],'image':custody.parse(body)['Image'],'ryukSocketPath':plan['ryukSocketPath'],'inspectConfigSha256':plan['inspectConfigSha256'],'inspectHostConfigSha256':plan['inspectHostConfigSha256'],'expiresUtc':plan['expiresUtc']}
                created_after=self.clock();inspect_status,_,inspect_raw=self._wire('GET','/v1.44/containers/'+cid+'/json');need(inspect_status==200,'postcreate inspect');row=custody.parse(inspect_raw);need(type(row.get('Created')) is str,'raw lexical created');need(0<=(self.clock()-custody.utc(row['Created'])).total_seconds()<=30 and custody.utc(row['Created'])<=created_after,'actual create timestamp window');self.owned[cid]['rawCreated']=row['Created'];self._owned(cid)
                self.journal.write(op,'response',{'containerId':cid,'rawCreated':row['Created'],'imageId':plan['imageId'],'name':q['name'],'planSha256':receipt['planSha256'],'originalSha256':receipt['originalSha256'],'effectiveSha256':receipt['effectiveSha256'],'responseSha256':digest(raw),'sourceOnly':True,'executionGranted':False});return status,headers,raw
            if action.startswith('exec-') and action!='exec-create':need(cid in self.execs,'exact retained exec ID only');parent=self.execs[cid];self._owned(parent)
            elif cid and action not in {'image-inspect'}:self._owned(cid)
            if action in {'container-list','events'}:
                filters=custody.parse(q.get('filters','{}').encode());need(type(filters) is dict and set(filters)<={'label','container','event','type'} and type(filters.get('label')) is list and filters['label'],'explicit session-scoped filter')
                allowed={'org.testcontainers.resource-reaper-session='+row['sessionId'] for row in self.owned.values()};need(set(filters['label'])<=allowed,'foreign/global filter refused')
                need(filters.get('type') in (None,['container']) and all(type(value) is list and all(type(x) is str for x in value) for value in filters.values()),'typed container-only filter')
                if 'container' in filters:need(type(filters['container']) is list and set(filters['container'])<=set(self.owned),'only exact owned event IDs')
            if action in {'image-inspect','image-pull','image-list'}:
                ref=cid if action=='image-inspect' else ((q.get('fromImage')+':'+q['tag']) if 'tag' in q and ':' not in q.get('fromImage','') else q.get('fromImage')) if action=='image-pull' else None
                if action=='image-list':
                    filters=custody.parse(q.get('filters','{}').encode());need(type(filters) is dict and set(filters)=={'reference'} and type(filters['reference']) is list and set(filters['reference'])<=set(self.images),'pinned image-list references')
                else:
                    need('tag' not in q or ':' not in q.get('fromImage',''),'unambiguous image tag query')
                    need(ref in self.images and re.fullmatch(r'sha256:[0-9a-f]{64}',self.images[ref]),'immutable approved image only')
            if action=='attach':need(q.get('stdin') in (None,'0','false') and q.get('logs') in (None,'0','false'),'noninteractive original output consumer only')
            if action=='delete':need(q.get('link') in (None,'0','false'),'link mutation refused')
            if action=='exec-create':
                command=custody.parse(body);need(type(command) is dict and set(command)<={'Cmd','AttachStdout','AttachStderr','AttachStdin','TTY','ConsoleSize','Privileged','User','WorkingDir','Env','DetachKeys'} and command.get('AttachStdout') is True and command.get('AttachStderr') is True and command.get('AttachStdin') in (None,False) and command.get('TTY') in (None,False) and command.get('Privileged') in (None,False) and command.get('Env') in (None,[]) and command.get('User') in (None,'') and command.get('WorkingDir') in (None,'') and command.get('ConsoleSize') is None,'no elevated/interactive exec')
                need(digest(custody.encode(command.get('Cmd'))) in self.images.get('approvedExecCommandSha256',[]),'independently pinned original wait commands')
            if action=='exec-start':
                command=custody.parse(body);need(type(command) is dict and set(command)<={'Detach','TTY','ConsoleSize'} and command.get('Detach') in (None,False) and command.get('TTY') in (None,False) and command.get('ConsoleSize') is None,'noninteractive bounded exec stream')
            if mutation:self._intent(op,action,cid,body)
            if mutation and not cleanup:
                allocation=self.owned.get(self.execs.get(cid,cid))
                self._pre_mutation(obs,allocation['expiresUtc'] if allocation is not None else None)
            sent=True;status,headers,raw=self._wire(method,target,body,seconds=15 if action in {'wait','exec-start','image-pull','events','attach'} else 2,stream=action in {'exec-start','logs','attach','events','image-pull'},extra_headers=extra_headers)
            if action in {'exec-start','logs','attach'} and status in {101,200}:multiplex(raw)
            if action=='exec-create':
                result=custody.parse(raw);need(status==201 and type(result.get('Id')) is str and custody.ID.fullmatch(result['Id']) and result['Id'] not in self.execs,'full unique exec ID ACK');self.execs[result['Id']]=cid
            if action=='exec-inspect':
                result=custody.parse(raw);need(status==200 and result.get('ID')==cid and result.get('ContainerID')==self.execs[cid] and type(result.get('Running')) is bool and type(result.get('ExitCode')) is int,'actual exact exec result')
            if action in {'start','stop'}:need(status in {204,304} and raw==b'','actual lifecycle ACK');after=self._owned(cid);need(after.get('State',{}).get('Running') is (action=='start'),'post lifecycle state')
            if action=='delete':
                need(status==204 and raw==b'','delete ACK');self._absence(cid);self.budget.retire(self.owned[cid]['nonce'],True);self.retired[cid]=self.owned.pop(cid)
            if action=='image-pull':
                need(status==200,'image pull actual HTTP200')
                for line in raw.splitlines():
                    if line:need(not custody.parse(line).get('error'),'actual image pull failure')
                pinned_status,_,pinned_raw=self._wire('GET','/v1.44/images/'+ref+'/json');need(pinned_status==200 and custody.parse(pinned_raw).get('Id')==self.images[ref],'pulled actual image ID pin')
            if action=='container-list' and status==200:
                rows=custody.parse(raw);need(type(rows) is list and len(rows)<=8 and all(type(row) is dict and row.get('Id') in self.owned for row in rows),'only exact owned container list')
                for row in rows:self._owned(row['Id'])
            if action=='events' and status==200:
                for line in raw.splitlines():
                    if not line:continue
                    event=custody.parse(line);need(type(event) is dict and event.get('Type')=='container' and event.get('Actor',{}).get('ID') in self.owned,'foreign/global event refused')
            if action=='image-list' and status==200:
                rows=custody.parse(raw);need(type(rows) is list and len(rows)<=4 and all(type(row) is dict and row.get('Id') in {value for value in self.images.values() if type(value) is str} for row in rows),'only pinned image list')
            if action=='image-inspect' and status==200:need(custody.parse(raw).get('Id')==self.images[cid],'actual image pin differs')
            if mutation:self.journal.write(op,'response',{'action':action,'containerId':cid,'httpStatus':status,'responseSha256':digest(raw),'sourceOnly':True,'executionGranted':False})
            return status,headers,raw
        except BaseException as error:
            if sent and mutation:
                if cid:self.quarantined.add(cid)
                self.journal.write(op,'quarantine',{'action':action,'containerId':cid,'failureType':type(error).__name__,'budgetRetained':allocated is not None,'sourceOnly':True,'executionGranted':False})
            elif allocated is not None:self.budget.retire(allocated,True) # no forward occurred
            raise

class RyukFilters:
    """Finite Ryuk cleanup-filter/ACK source; never broadens owned generations."""
    def __init__(self,session_id,journal):self.session=session_id;self.journal=journal;self.registered=False
    def register(self,op,line):
        need(type(line) is bytes and 0<len(line)<=4096 and line.endswith(b'\n') and b'\r' not in line and line.count(b'\n')==1,'one bounded Ryuk filter line')
        pairs=parse_qsl(line[:-1].decode('ascii'),keep_blank_values=True,strict_parsing=True,max_num_fields=8)
        need(pairs==[('label','org.testcontainers.resource-reaper-session='+self.session)],'exact original Ryuk session filter; no global cleanup')
        need(not self.registered,'filter replay refused');self.journal.write(op,'filter',{'sessionId':self.session,'filterSha256':digest(line),'sourceOnly':True,'executionGranted':False});self.registered=True;return b'ACK\n'
    def cleanup(self,backend,operations):
        need(self.registered and type(backend) is OriginalFixtureBackend,'registered exact backend')
        need(backend.current_deadline is None,'serialized finite retirement entry')
        backend.current_deadline=Deadline(30)
        try:
            backend._observe(True);rows=[cid for cid,row in backend.owned.items() if row['sessionId']==self.session and row['image']!='testcontainers/ryuk:0.14.0'];need(len(rows)<=8 and type(operations) is list and len(operations)==len(rows),'finite exact owned retirement operations')
            results=[];failures=[];self.retirement_failures=[]
            for cid,op in zip(rows,operations):
                try:results.append(backend._dispatch(op,'DELETE','/v1.44/containers/'+cid+'?force=true&v=true',cleanup_origin='retired-ryuk'))
                except BaseException as error:failures.append(error);self.retirement_failures.append({'containerId':cid,'operation':op,'failureType':type(error).__name__})
            if failures:raise BaseExceptionGroup('owned fixture retirement failures; remaining exact generations attempted',failures)
            return results
        finally:backend.current_deadline=None

class PrivateRelay:
    """Actual finite Unix listener source with an inherited owner-liveness pipe.

    The owner pipe, SDK and Ryuk peer attestations require independent backend
    qualification. Default gates refuse before any filesystem/socket mutation.
    """
    def __init__(self,base,nonce,backend,sdk_peer,ryuk_peers,owner_fd,expires_utc,*,mode='production',system=None,monotonic=time.monotonic):
        if mode=='production':
            if not custody.HEX.fullmatch(ROOT_PUBLIC_KEY_SHA256 or '') or not custody.HEX.fullmatch(BACKEND_QUALIFICATION_SHA256 or ''):raise PermissionError('Root/backend absent before relay bind')
            need(system is None and type(backend) is OriginalFixtureBackend and backend.mode=='production','qualified concrete backend only')
            self.system=LinuxRelaySystem()
        else:need(mode=='synthetic-source-control' and system is not None,'explicit fake relay interfaces');self.system=system
        need(type(nonce) is str and custody.NONCE.fullmatch(nonce) and type(base) is str and base.startswith('/') and '..' not in Path(base).parts,'owned private path')
        need(type(owner_fd) is int and owner_fd>=0,'inherited controller liveness FD')
        self.mode=mode;self.backend=backend;self.sdk_peer=sdk_peer;self.ryuk_peers=ryuk_peers;self.owner_fd=owner_fd;self.directory=base+'/employee-fixture-'+nonce;self.path=self.directory+'/docker.sock';need(len(self.path.encode())<=100,'Unix endpoint bound')
        self.expires=custody.utc(expires_utc);self.clock=monotonic;self.nonce=nonce;self.listener=None;self.socket_id=None;self.directory_id=None;self.cleanup_verified=False
    def open(self):
        need(self.listener is None,'endpoint replay refused');now=dt.datetime.now(dt.timezone.utc);need(0<(self.expires-now).total_seconds()<=120,'finite relay lease');self.system.private_base(str(Path(self.directory).parent),self.sdk_peer['uid']);self.system.validate_owner_pipe(self.owner_fd)
        try:
            self.system.mkdir(self.directory);self.directory_id=self.system.identity(self.directory);need(self.directory_id['kind']=='directory' and self.directory_id['mode']==0o700,'private directory')
            self.listener=self.system.socket();self.listener.bind(self.path);self.socket_id=self.system.identity(self.path);need(self.socket_id['kind']=='socket' and self.socket_id['uid']==self.sdk_peer['uid'],'owned bound socket')
            self.system.chmod(self.path,0o600);after=self.system.identity(self.path);need(all(after[key]==self.socket_id[key] for key in ('device','inode','uid','gid','kind')),'socket changed during permissions');self.socket_id=after;self.listener.listen(1)
            return {'DOCKER_HOST':'unix://'+self.path}
        except BaseException:self.close();raise
    def _controller(self):need(self.system.owner_alive(self.owner_fd),'controller death; stop relay before daemon forward')
    def _peer(self,conn):
        actual=self.system.peer(conn);need(type(actual) is dict and set(actual)=={'pid','uid','gid','birth','bootId','cgroup'} and type(actual['pid']) is int and actual['pid']>0,'actual typed peer generation')
        if actual['bootId']==self.sdk_peer['bootId'] and actual['uid']==self.sdk_peer['uid'] and actual['gid']==self.sdk_peer['gid'] and actual['cgroup']==self.sdk_peer['cgroup']:
            need(self.system.sdk_descendant(actual,self.sdk_peer),'actual live SDK/testhost ancestry and pidfd custody');return actual
        need(any(custody.encode(actual)==custody.encode(row) for row in self.ryuk_peers.values()),'foreign peer outside exact SDK/Ryuk generations');return actual
    def serve_one(self,op):
        self._controller();need(self.system.identity(self.path)==self.socket_id,'relay socket changed');deadline=Deadline(min(30,(self.expires-dt.datetime.now(dt.timezone.utc)).total_seconds()),self.clock);self.listener.settimeout(min(2,deadline.remaining()));conn,_=self.listener.accept();fd=None
        try:
            actual=self._peer(conn);fd=self.system.retain_peer(actual['pid']);reader=Reader(conn,deadline,MAX_BODY);first=reader.line(4352);match=re.fullmatch(rb'([A-Z]{1,8}) ([\x21-\x7e]{1,4096}) HTTP/1\.1',first);need(match,'strict private request line');method,target=match[1].decode(),match[2].decode();action,_,_=route(method,target);headers={};count=len(first)+2
            while True:
                line=reader.line(MAX_HEADER);count+=len(line)+2;need(count<=MAX_HEADER,'private header bound')
                if not line:break
                need(b':' in line and not line.startswith((b' ',b'\t')),'folded header');key,value=line.split(b':',1);key=key.lower();value=value.strip();need(re.fullmatch(rb'[a-z0-9-]{1,64}',key) and key not in headers and re.fullmatch(rb'[\x20-\x7e]*',value),'duplicate/control header');need(key in {b'host',b'content-length',b'content-type',b'connection',b'upgrade',b'user-agent',b'x-registry-auth',b'x-tc-sid'},'unknown client header');headers[key]=value
            need(headers.get(b'host')==(Path(self.path).name+':80').encode('ascii'),'reviewed Docker host');length=headers.get(b'content-length',b'0');need(re.fullmatch(rb'0|[1-9][0-9]{0,5}',length) and int(length)<=MAX_BODY,'request body bound');body=reader.exact(int(length));need(not reader.buffer,'pipeline/surplus refused')
            sid=headers.get(b'x-tc-sid');allowed_sids={row['sessionId'] for row in self.backend.owned.values()}|{custody.parse(plan.raw if type(plan) is custody.SignedPlan else plan)['sessionId'] for plan in self.backend.plans.values()}|{'00000000-0000-0000-0000-000000000000'}
            need(sid is not None and sid.decode('ascii') in allowed_sids,'exact original x-tc-sid scope')
            if b'upgrade' in headers:need(action in {'exec-start','attach'} and headers[b'upgrade']==b'tcp' and headers.get(b'connection',b'').lower()==b'upgrade','original Docker hijack only')
            if b'x-registry-auth' in headers:need(action=='image-pull','auth header only pinned pull');anonymous_registry_header(headers[b'x-registry-auth'])
            need(self.system.peer(conn)==actual and self.system.peer_alive(fd),'peer drift/exit before mutation');self._controller()
            status,returned,raw=self.backend.dispatch(op,method,target,body,cleanup_origin='foreground-sdk' if actual['cgroup']==self.sdk_peer['cgroup'] else 'retired-ryuk',extra_headers={b'x-registry-auth':headers[b'x-registry-auth']} if b'x-registry-auth' in headers else None);deadline.remaining();self._controller();need(len(raw)<=MAX_OUTPUT,'bounded relay output')
            upgrade=status==101;prefix=('HTTP/1.1 '+str(status)+' Docker\r\n').encode()
            if upgrade:prefix+=b'Connection: Upgrade\r\nUpgrade: tcp\r\n'
            else:prefix+=b'Connection: close\r\nContent-Length: '+str(len(raw)).encode()+b'\r\n'
            for key in (b'content-type',b'api-version',b'docker-experimental',b'ostype',b'server'):
                if key in returned:need(re.fullmatch(rb'[\x20-\x7e]{0,160}',returned[key]),'safe response metadata');prefix+=key+b': '+returned[key]+b'\r\n'
            pending=memoryview(prefix+b'\r\n'+raw)
            while pending:self._controller();conn.settimeout(min(2,deadline.remaining()));sent=conn.send(pending);need(type(sent) is int and 0<sent<=len(pending),'relay lost ACK');pending=pending[sent:]
            return {'sourceOnly':True,'runtimeQualified':False,'nativeExecutionGranted':False,'action':action,'peerPid':actual['pid'],'responseSha256':digest(raw)}
        finally:
            try:
                if fd is not None:self.system.close_peer(fd)
            finally:conn.close()
    def close(self):
        if self.listener is not None:self.listener.close();self.listener=None
        if self.socket_id is not None and self.system.exists(self.path):need(self.system.identity(self.path)==self.socket_id,'changed socket preserved');self.system.unlink(self.path)
        if self.directory_id is not None and self.system.exists(self.directory):need(self.system.identity(self.directory)==self.directory_id,'changed directory preserved');self.system.rmdir(self.directory)
        need(not self.system.exists(self.path) and not self.system.exists(self.directory),'owned endpoint absence required');self.cleanup_verified=True
    def run(self,operation_nonces):
        try:
            self.open()
            for op in operation_nonces:self._controller();self.serve_one(op)
        finally:self.close()

class LinuxRelaySystem:
    def __init__(self):
        need(os.name=='posix' and hasattr(socket,'SO_PEERCRED') and hasattr(os,'pidfd_open'),'qualified Linux syscall layout')
        import resource
        memory=resource.getrlimit(resource.RLIMIT_AS);cpu=resource.getrlimit(resource.RLIMIT_CPU)
        need(0<memory[0]<=268435456 and 0<cpu[0]<=90,'dedicated finite helper memory/CPU required')
    def identity(self,path):
        x=os.lstat(path);return {'device':x.st_dev,'inode':x.st_ino,'uid':x.st_uid,'gid':x.st_gid,'mode':stat.S_IMODE(x.st_mode),'kind':'socket' if stat.S_ISSOCK(x.st_mode) else 'directory' if stat.S_ISDIR(x.st_mode) else 'other'}
    def private_base(self,path,uid):
        for p in (Path(path),*Path(path).parents):need(not p.is_symlink(),'linked base')
        x=self.identity(path);need(x['kind']=='directory' and x['uid']==uid and x['mode']&0o077==0,'owned private base')
    def validate_owner_pipe(self,fd):need(stat.S_ISFIFO(os.fstat(fd).st_mode),'retained owner pipe only')
    def owner_alive(self,fd):return not select.select([fd],[],[],0)[0]
    def mkdir(self,path):os.mkdir(path,0o700)
    def chmod(self,path,mode):os.chmod(path,mode,follow_symlinks=False)
    def socket(self):return socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
    def exists(self,path):return os.path.lexists(path)
    def unlink(self,path):os.unlink(path)
    def rmdir(self,path):os.rmdir(path)
    def peer(self,conn):return UnixForwarder._generation(conn)
    def retain_peer(self,pid):return os.pidfd_open(pid)
    def peer_alive(self,fd):return not select.select([fd],[],[],0)[0]
    def close_peer(self,fd):os.close(fd)
    def sdk_descendant(self,actual,sdk):
        # An exact SDK root birth/boot must remain live; same cgroup alone is insufficient.
        pid=actual['pid'];visited=set()
        for _ in range(64):
            need(pid not in visited and pid>0,'bounded actual ancestry');visited.add(pid)
            with open('/proc/'+str(pid)+'/stat','rb') as f:raw=f.read(8193)
            need(len(raw)<=8192,'bounded ancestry stat');fields=raw.rsplit(b')',1)[1].split()
            if pid==sdk['pid']:return fields[19].decode()==sdk['birth']
            pid=int(fields[1])
        return False

SDK_SOURCE_SHA256='8618a2ca9bd01f02e108303b78cfafd9a8b68d6a987026381e24bb7ea5c77d69'

def sdk_successor_source(original):
    """Exact executable postimage proposal, leaving sealed original bytes alone.

    The emitted source still has absent Root trust and an absent scoped binding.
    It cannot start providers until both independent gates are supplied/reviewed.
    """
    need(type(original) is bytes and digest(original)==SDK_SOURCE_SHA256,'exact SDK preimage')
    replacements={
        b'"Environment": []}':b'"Environment": _SCOPED_RELAY_BINDING.environment(stage, now) if _SCOPED_RELAY_BINDING is not None else []}',
        b'one(iface, "Environment") == []':b'one(iface, "Environment") == props["Environment"]',
        b'keys(receipt, {"runtimeQualified", "sdkSourceSha256", "fixtureTransportQualified", "fixtureRegistrySha256", "nativeExecutableInventory"})':b'keys(receipt, {"runtimeQualified", "sdkSourceSha256", "fixtureTransportQualified", "fixtureRegistrySha256", "nativeExecutableInventory", "fixtureEnvironmentPolicySha256"})',
        b'need(stage["phaseId"] not in {"baseline-focused", "candidate-focused", "candidate-suite"}, "provider phases blocked: sealed scoped DOCKER_HOST/runtime transport custody not integrated; default daemon forbidden")':b'need(_SCOPED_RELAY_BINDING is not None and _SCOPED_RELAY_BINDING.guard(stage, root_policy_raw, grant_raw, backend_receipt_raw) is True, "independently authenticated scoped relay binding absent; default daemon forbidden")'
    }
    patched=original
    for old,new in replacements.items():need(patched.count(old)==1,'exact SDK source patch preimage differs');patched=patched.replace(old,new)
    main_guard=b'if __name__ == "__main__":'
    need(patched.count(main_guard)==1,'exact SDK main entry preimage')
    patched=patched.replace(main_guard,b'# Reviewed successor injection remains absent until authenticated admission.\n_SCOPED_RELAY_BINDING = None\n\n'+main_guard)
    import ast
    ast.parse(patched,'employee_scoped_sdk_successor')
    return patched

class ScopedSdkBinding:
    """Detached exact environment attestation; no signing or key enrollment."""
    DOMAIN=b'employee-sdk-scoped-relay-binding/v1\0'
    FIELDS={'schemaVersion','stageBindingSha256','sdkSuccessorSha256','fixtureCompatibilitySha256','ownedEndpoint','socketIdentity','startsUtc','expiresUtc'}
    def __init__(self,raw,signature,public_key,*,mode='production',observe_socket=None):
        self.mode=mode
        if mode=='production':
            if not custody.HEX.fullmatch(ROOT_PUBLIC_KEY_SHA256 or '') or not custody.HEX.fullmatch(BACKEND_QUALIFICATION_SHA256 or ''):raise PermissionError('Root/backend absent before SDK environment admission')
            need(observe_socket is None and type(public_key) is bytes and len(public_key)==32 and digest(public_key)==ROOT_PUBLIC_KEY_SHA256 and type(signature) is bytes and len(signature)==64,'independently enrolled Root environment authority')
            from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
            Ed25519PublicKey.from_public_bytes(public_key).verify(signature,self.DOMAIN+raw)
            self.observe_socket=UnixForwarder._identity
        else:need(mode=='synthetic-source-control' and callable(observe_socket),'explicit fake endpoint observation');self.observe_socket=observe_socket
        self.signature=signature;self.public_key=public_key
        self.raw=raw;self.policy=custody.parse(raw);need(type(self.policy) is dict and set(self.policy)==self.FIELDS and type(self.policy['schemaVersion']) is int and self.policy['schemaVersion']==1,'closed scoped environment attestation')
        need(all(type(self.policy[key]) is str and custody.HEX.fullmatch(self.policy[key]) for key in ('stageBindingSha256','sdkSuccessorSha256','fixtureCompatibilitySha256')),'exact source/stage scope seals')
        path=self.policy['ownedEndpoint'];need(type(path) is str and path.startswith('/') and Path(path).name=='docker.sock' and re.fullmatch(r'employee-fixture-[0-9a-f]{32}',Path(path).parent.name) and len(path.encode())<=100 and '..' not in Path(path).parts,'only nonce-owned private relay endpoint')
        need(type(self.policy['socketIdentity']) is dict and set(self.policy['socketIdentity'])=={'device','inode','uid','gid','mode'} and all(type(value) is int and value>=0 for value in self.policy['socketIdentity'].values()) and self.policy['socketIdentity']['inode']>0 and self.policy['socketIdentity']['mode']==0o600,'typed private owned socket generation')
        start,end=custody.utc(self.policy['startsUtc']),custody.utc(self.policy['expiresUtc']);need(0<(end-start).total_seconds()<=120,'finite relay binding lease')
    @staticmethod
    def stage_binding(stage):return digest(custody.encode({key:value for key,value in stage.items() if key not in {'rootGrantSha256','backendQualificationSha256'}}))
    def environment(self,stage,now):
        need(custody.encode(self.policy)==self.raw,'canonical original scoped policy drift')
        need(self.stage_binding(stage)==self.policy['stageBindingSha256'] and custody.utc(self.policy['startsUtc'])<=now<custody.utc(self.policy['expiresUtc']) and now<custody.utc(stage['expiresUtc']),'exact current SDK phase binding')
        need(custody.encode(self.observe_socket(self.policy['ownedEndpoint']))==custody.encode(self.policy['socketIdentity']),'actual owned socket generation changed')
        return ['DOCKER_HOST=unix://'+self.policy['ownedEndpoint']]
    def guard(self,stage,root_policy_raw,grant_raw,backend_receipt_raw):
        if self.mode!='production':raise PermissionError('synthetic environment is not native authority')
        need(type(self.public_key) is bytes and digest(self.public_key)==ROOT_PUBLIC_KEY_SHA256,'original Root key drift')
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        Ed25519PublicKey.from_public_bytes(self.public_key).verify(self.signature,self.DOMAIN+self.raw)
        need(digest(Path(__file__).read_bytes())==self.policy['fixtureCompatibilitySha256'],'actual compatibility source seal differs')
        receipt=custody.parse(backend_receipt_raw);policy=custody.parse(root_policy_raw)
        need(receipt.get('fixtureEnvironmentPolicySha256')==digest(self.raw) and receipt.get('sdkSourceSha256')==self.policy['sdkSuccessorSha256'] and receipt.get('runtimeQualified') is True and receipt.get('fixtureTransportQualified') is True,'independently authenticated backend/environment scope')
        need(policy.get('backendReceiptSha256')==digest(backend_receipt_raw)==stage['backendQualificationSha256'] and policy.get('grantSha256')==digest(grant_raw)==stage['rootGrantSha256'] and policy.get('backendModuleSha256')==self.policy['sdkSuccessorSha256'],'original unchanged Root scope hashes')
        self.environment(stage,dt.datetime.now(dt.timezone.utc));return True

def prepare_consumption(stage_raw,sdk_source_raw,request_raw,plan_raw,relay_path,now):
    """Tie exact frozen SDK phase + original request + compatibility route.

    This pure source receipt explicitly reports the existing SDK Environment=[]
    and provider-phase block; it neither changes those guards nor calls acquire.
    """
    import types
    need(type(sdk_source_raw) is bytes and len(sdk_source_raw)<=65536 and digest(sdk_source_raw)==SDK_SOURCE_SHA256,'exact reviewed SDK owner source')
    module=types.ModuleType('sealed_original_fixture_sdk');exec(compile(sdk_source_raw,'sealed_original_fixture_sdk','exec'),module.__dict__)
    stage=custody.parse(stage_raw);unit=module.unit_plan(stage,now)
    need(stage['phaseId'] in {'baseline-focused','candidate-focused','candidate-suite'},'exact original frozen provider test phase only')
    effective,transformation=custody.transform(request_raw,plan_raw);plan=custody.parse(plan_raw)
    need(stage['bootId']==plan['sdkGeneration']['bootId'] and custody.utc(plan['expiresUtc'])<=custody.utc(stage['expiresUtc']),'original stage/fixture same host and bounded lease')
    need(type(relay_path) is str and relay_path.startswith('/') and len(relay_path.encode())<=100 and '..' not in Path(relay_path).parts,'exact private reviewed endpoint path')
    return {'sourceOnly':True,'nativeExecutionGranted':False,'runtimeQualified':False,'stageSha256':digest(stage_raw),'sdkSourceSha256':SDK_SOURCE_SHA256,'sdkUnit':unit['unit'],'originalFrozenArgv':unit['properties']['ExecStart'][0]['argv'],'workingDirectory':unit['properties']['WorkingDirectory'],'fixtureTransformation':transformation,'effectiveRequestSha256':digest(effective),'requiredEnvironmentSuccessor':['DOCKER_HOST=unix://'+relay_path],'existingSdkEnvironment':unit['properties']['Environment'],'existingSdkProviderEntryBlocked':True,'reviewableSdkSuccessorSha256':digest(sdk_successor_source(sdk_source_raw)),'sdkSuccessorStillRequiresAuthenticatedScopedBinding':True,'sourceConnection':{'registry':'employee_fixture_registry_compatibility.transform + SignedPlan + Journal + ActiveBudget','daemon':'employee_fixture_compatibility.UnixForwarder.request','dispatcher':'employee_fixture_compatibility.OriginalFixtureBackend.dispatch','relay':'employee_fixture_compatibility.PrivateRelay.run','retirement':'employee_fixture_compatibility.RyukFilters.cleanup'},'requiredIndependentReview':['SDK environment/property/observer successor','detached stage+fixture+backend authority binding','actual daemon and SDK/Ryuk peer qualification','immutable image/default expansion and exact original exec command pins','controller owner pipe and concurrency/hard-loss lifecycle qualification']}

def main(argv=None):
    import argparse
    parser=argparse.ArgumentParser(description='Source-only frozen Employee fixture consumption preparation')
    parser.add_argument('--mode',choices=['source-check','prepare-consumption','qualification'],default='source-check')
    for field in ('stage','sdk-source','request','plan','relay-path','evidence'):parser.add_argument('--'+field)
    args=parser.parse_args(argv)
    if args.mode=='qualification':raise PermissionError('Root/backend absent; existing SDK Environment=[] and provider-entry guard remain unchanged; qualification refuses before reading paths or creating resources')
    if args.mode=='source-check':
        result={'sourceOnly':True,'nativeExecutionGranted':False,'runtimeQualified':False,'RootEnrolled':False,'upstreamCommit':UPSTREAM_COMMIT,'productionRefusesBeforeSocket':True,'routes':sorted(ROUTES)}
    else:
        need(all(getattr(args,key) for key in ('stage','sdk_source','request','plan','relay_path','evidence')),'explicit sealed source-route inputs')
        def bounded(path,limit):
            with open(path,'rb') as reader:raw=reader.read(limit+1)
            need(len(raw)<=limit,'bounded source route input');return raw
        result=prepare_consumption(bounded(args.stage,16384),bounded(args.sdk_source,65536),bounded(args.request,65536),bounded(args.plan,65536),args.relay_path,dt.datetime.now(dt.timezone.utc))
        evidence=Path(args.evidence);need(evidence.is_absolute() and evidence.parent.is_dir(),'existing owned evidence parent required')
        for node in (evidence,*evidence.parents):need(not node.is_symlink(),'linked evidence path refused')
        fd=os.open(evidence,os.O_WRONLY|os.O_CREAT|os.O_EXCL|getattr(os,'O_NOFOLLOW',0),0o600)
        try:
            raw=custody.encode(result);need(os.write(fd,raw)==len(raw),'complete source receipt');os.fsync(fd)
        finally:os.close(fd)
    print(json.dumps(result,sort_keys=True));return 0

if __name__=='__main__':main()
