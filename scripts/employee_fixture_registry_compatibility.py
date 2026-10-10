"""Original-wire compatibility custody. Root trust remains absent; no enrollment.

This successor does not alter immutable registry-v4 or original fixtures.
Plans are source-only until their exact transformation is independently signed.
"""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat

ROOT_PUBLIC_KEY_SHA256 = None
DOMAIN = b'employee-original-fixture-transform/v1\0'
HEX = re.compile(r'[0-9a-f]{64}\Z')
NONCE = re.compile(r'[0-9a-f]{32}\Z')
ID = HEX
PG_FLAGS = ['-c','fsync=off','-c','full_page_writes=off','-c','synchronous_commit=off']
IMAGES = {'postgres:18-alpine','redis:7.4-alpine','redis:8-alpine','testcontainers/ryuk:0.14.0'}
MAX_RAW = 65536

def need(value,message):
    if not value:raise ValueError(message)

def sha(raw):return hashlib.sha256(raw).hexdigest()
def encode(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=True,allow_nan=False).encode()
def parse(raw):
    need(type(raw) is bytes and 0<len(raw)<=MAX_RAW,'bounded exact JSON bytes required')
    depth=0;quoted=False;escaped=False
    for byte in raw:
        if quoted:
            if escaped:escaped=False
            elif byte==92:escaped=True
            elif byte==34:quoted=False
        elif byte==34:quoted=True
        elif byte in (91,123):depth+=1;need(depth<=16,'JSON depth bound')
        elif byte in (93,125):depth-=1
    def pairs(items):
        out={}
        for key,value in items:need(key not in out,'duplicate JSON key');out[key]=value
        return out
    return json.loads(raw,object_pairs_hook=pairs,parse_constant=lambda _:need(False,'nonfinite JSON'))
def utc(text):
    need(type(text) is str and text.endswith('Z'),'UTC expiry required')
    value=dt.datetime.fromisoformat(text[:-1]+'+00:00');need(value.utcoffset()==dt.timedelta(0),'UTC required');return value

def generation(value):
    need(type(value) is dict and set(value)=={'pid','birth','bootId','unit','invocationId','cgroup','expiresUtc'},'closed SDK generation')
    need(type(value['pid']) is int and 0<value['pid']<2**31,'positive typed SDK PID')
    need(type(value['birth']) is str and re.fullmatch(r'[1-9][0-9]{0,23}',value['birth']),'lexical SDK birth')
    need(type(value['bootId']) is str and re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}',value['bootId']),'boot UUID')
    need(type(value['invocationId']) is str and NONCE.fullmatch(value['invocationId']),'manager invocation')
    need(type(value['unit']) is str and re.fullmatch(r'[a-z0-9][a-z0-9_.-]{0,120}\.service',value['unit']),'exact unit')
    need(type(value['cgroup']) is str and value['cgroup'].startswith('/') and len(value['cgroup'])<=256 and all(re.fullmatch(r'[A-Za-z0-9_.:-]+',part) and part not in {'.','..'} for part in value['cgroup'][1:].split('/')),'exact cgroup')
    utc(value['expiresUtc'])

def spans(raw):
    """Retain original top-level JSON value byte slices, including literal Env."""
    item=parse(raw);need(type(item) is dict,'object request required')
    text=raw.decode('utf-8');decoder=json.JSONDecoder();i=0;out={}
    def white(pos):
        while pos<len(text) and text[pos].isspace():pos+=1
        return pos
    i=white(0);need(text[i]=='{','object required');i=white(i+1)
    while text[i]!='}':
        key,end=decoder.raw_decode(text,i);i=white(end);need(text[i]==':','colon required');start=white(i+1)
        _,end=decoder.raw_decode(text,start)
        out[key]=(len(text[:start].encode()),len(text[:end].encode()))
        i=white(end)
        if text[i]==',':i=white(i+1)
        else:need(text[i]=='}','object delimiter')
    return out

def caps(host,ryuk_path=None):
    required={'Memory','MemorySwap','NanoCpus','PidsLimit','Privileged','CapAdd','CapDrop','SecurityOpt','NetworkMode','PortBindings','Tmpfs','Binds','Mounts','PublishAllPorts','PidMode','IpcMode'}
    need(type(host) is dict and required<=set(host) and set(host)<=required|{'AutoRemove','ExtraHosts'},'complete explicit cap overlay')
    need(type(host['Memory']) is int and 0<host['Memory']<=1024**3 and type(host['MemorySwap']) is int and host['MemorySwap']==host['Memory'],'memory <=1GiB/no swap')
    need(type(host['NanoCpus']) is int and 0<host['NanoCpus']<=10**9 and type(host['PidsLimit']) is int and 0<host['PidsLimit']<=256,'CPU/PID caps')
    need(host['Privileged'] is False and host['CapAdd'] in (None,[]) and host['CapDrop']==['ALL'] and host['SecurityOpt']==['no-new-privileges'],'no elevation')
    need(host['NetworkMode']=='bridge' and host['PidMode'] in ('',None) and host['IpcMode'] in ('private',None) and host['PublishAllPorts'] is False,'isolated modes')
    need(type(host['PortBindings']) is dict and len(host['PortBindings'])<=2,'bounded ports')
    for port,rows in host['PortBindings'].items():
        need(port in {'5432/tcp','6379/tcp','8080/tcp'} and type(rows) is list and len(rows)==1,'fixture port only')
        need(type(rows[0]) is dict and set(rows[0])=={'HostIp','HostPort'} and rows[0]['HostIp']=='127.0.0.1' and type(rows[0]['HostPort']) is str and re.fullmatch(r'0|[1-9][0-9]{0,4}',rows[0]['HostPort']) and int(rows[0]['HostPort'])<=65535,'loopback only')
    need(host.get('ExtraHosts') in (None,[]) and host.get('AutoRemove') in (None,False,True),'reviewed host defaults only')
    need(host['Binds'] in (None,[]),'bind mounts refused')
    mounts=host['Mounts']
    if ryuk_path is None:need(mounts in (None,[]),'no provider mounts')
    else:
        need(type(ryuk_path) is str and ryuk_path.startswith('/') and '..' not in Path(ryuk_path).parts,'private reviewed Ryuk endpoint')
        need(mounts==[{'Type':'bind','Source':ryuk_path,'Target':'/var/run/docker.sock','ReadOnly':False}],'only exact restricted relay mount')
    need(type(host['Tmpfs']) is dict and len(host['Tmpfs'])<=1,'tmpfs bound')
    for target,options in host['Tmpfs'].items():need(target in {'/var/lib/postgresql','/data'} and type(options) is str and re.fullmatch(r'rw,noexec,nosuid,nodev,size=[1-9][0-9]{0,9}',options) and int(options.split('=')[-1])<=host['Memory'],'bounded tmpfs')

def transform(original,plan_raw):
    """Pure exact signed-plan projection; this function never grants execution."""
    item=parse(original);plan=parse(plan_raw)
    fields={'schemaVersion','originalSha256','effectiveSha256','name','sessionId','staticLabels','custodyLabels','hostConfig','ryukSocketPath','imageId','sdkGeneration','expiresUtc','inspectConfigSha256','inspectHostConfigSha256','daemonId'}
    need(type(plan) is dict and set(plan)==fields and type(plan['schemaVersion']) is int and plan['schemaVersion']==1,'closed transformation plan')
    need(plan['originalSha256']==sha(original) and all(type(plan[k]) is str and HEX.fullmatch(plan[k]) for k in ('originalSha256','effectiveSha256')),'raw request seals')
    need(type(plan['imageId']) is str and re.fullmatch(r'sha256:[0-9a-f]{64}',plan['imageId']),'immutable image ID')
    need(all(type(plan[k]) is str and HEX.fullmatch(plan[k]) for k in ('inspectConfigSha256','inspectHostConfigSha256')),'reviewed image-default/engine-expanded readback projections')
    need(type(plan['daemonId']) is str and 0<len(plan['daemonId'])<=128,'exact reviewed daemon ID')
    generation(plan['sdkGeneration']);utc(plan['expiresUtc'])
    need(type(item) is dict and {'Image','Labels','HostConfig'}<=set(item) and set(item)<={'Image','Platform','Name','Hostname','WorkingDir','Entrypoint','Cmd','Env','Labels','ExposedPorts','HostConfig','NetworkingConfig'},'original fixture fields')
    need(item['Image'] in IMAGES and item.get('Name') in (None,plan['name']) and type(plan['name']) is str and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,127}',plan['name']),'original image/name')
    need(type(plan['sessionId']) is str and re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}',plan['sessionId']),'exact session')
    labels=item['Labels'];need(type(labels) is dict and type(plan['staticLabels']) is dict,'labels required')
    dynamic={'org.testcontainers.resource-reaper-session':'00000000-0000-0000-0000-000000000000' if item['Image']=='testcontainers/ryuk:0.14.0' else plan['sessionId']}
    need(labels==dict(plan['staticLabels'],**dynamic),'exact original static/resource-reaper session labels')
    need({'org.testcontainers','org.testcontainers.lang','org.testcontainers.version','org.testcontainers.session-id'}<=set(plan['staticLabels']) and all(type(k) is str and type(v) is str and 0<len(v)<=160 for k,v in plan['staticLabels'].items()),'observed original static labels')
    need(type(plan['custodyLabels']) is dict and set(plan['custodyLabels'])=={'employee.owner','employee.run','employee.disposable','employee.expiry'} and plan['custodyLabels']['employee.disposable']=='true' and plan['custodyLabels']['employee.expiry']==plan['expiresUtc'] and not(set(labels)&set(plan['custodyLabels'])),'explicit additive custody labels')
    need(plan['staticLabels']['org.testcontainers.session-id']==plan['sessionId'],'original static SDK session preserved')
    env=item.get('Env');need(env is None or type(env) is list,'exact Env array')
    seen=set()
    for value in env or []:
        need(type(value) is str and '=' in value and not any(c in value for c in '\x00\r\n'),'literal Env entry')
        key,_=value.split('=',1);need(key not in seen,'duplicate Env');seen.add(key)
    if item['Image']=='postgres:18-alpine':need(item.get('Cmd')==PG_FLAGS and seen=={'POSTGRES_DB','POSTGRES_USER','POSTGRES_PASSWORD'},'original PG flags/environment')
    elif item['Image'].startswith('redis:'):need(item.get('Cmd') in (None,[]) and not seen,'original Redis defaults')
    else:need(item.get('Cmd') in (None,[]) and plan['ryukSocketPath'] is not None,'Ryuk requires explicit restricted socket plan')
    need(item.get('Entrypoint') in (None,[]),'entrypoint override refused')
    if item['Image']!='testcontainers/ryuk:0.14.0':need(plan['ryukSocketPath'] is None,'non-Ryuk relay mount refused')
    caps(plan['hostConfig'],plan['ryukSocketPath'])
    slices=spans(original); replacements={'HostConfig':encode(plan['hostConfig']),'Labels':encode(dict(labels,**plan['custodyLabels']))}
    effective=original
    for key in sorted(replacements,key=lambda key:slices[key][0],reverse=True):
        start,end=slices[key];effective=effective[:start]+replacements[key]+effective[end:]
    need(sha(effective)==plan['effectiveSha256'],'effective wire SHA differs; no auto-approval')
    new=parse(effective);need(new.get('Cmd')==item.get('Cmd') and new.get('Env')==env,'original behavior changed')
    original_env=slices.get('Env');new_env=spans(effective).get('Env')
    if original_env is None:need(new_env is None,'omitted original Env introduced');env_literal=b''
    else:
        env_literal=original[original_env[0]:original_env[1]]
        need(new_env is not None and env_literal==effective[new_env[0]:new_env[1]],'literal Env bytes changed')
    return effective,{'sourceOnly':True,'executionGranted':False,'originalSha256':sha(original),'effectiveSha256':sha(effective),'planSha256':sha(plan_raw),'envSha256':sha(env_literal)}

class SignedPlan:
    def __init__(self,public_key,raw,signature):
        if ROOT_PUBLIC_KEY_SHA256 is None:raise PermissionError('Root enrollment absent; no compatibility authority')
        need(type(public_key) is bytes and len(public_key)==32 and sha(public_key)==ROOT_PUBLIC_KEY_SHA256,'independently pinned Root key')
        need(type(signature) is bytes and len(signature)==64,'detached signature')
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        Ed25519PublicKey.from_public_bytes(public_key).verify(signature,DOMAIN+raw)
        parse(raw);self.raw=raw;self.signature=signature;self.public_key=public_key;self.sha256=sha(raw)
    def verify(self):
        if ROOT_PUBLIC_KEY_SHA256 is None:raise PermissionError('Root enrollment absent')
        need(sha(self.public_key)==ROOT_PUBLIC_KEY_SHA256 and sha(self.raw)==self.sha256,'original signed plan bytes/key drift')
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        Ed25519PublicKey.from_public_bytes(self.public_key).verify(self.signature,DOMAIN+self.raw)
        parse(self.raw);return self.raw

class Journal:
    """Exclusive durable nonsensitive intent. No ledger adoption after restart."""
    def __init__(self,directory):
        self.directory=Path(directory);need(self.directory.is_absolute(),'absolute private receipt directory')
        for p in (self.directory,*self.directory.parents):need(not p.is_symlink(),'linked receipt path refused')
        item=self.directory.stat();need(stat.S_ISDIR(item.st_mode),'existing receipt directory required')
        self.identity=(item.st_dev,item.st_ino);self.retained={}
    def write(self,nonce,kind,row):
        need(type(nonce) is str and NONCE.fullmatch(nonce) and kind in {'intent','response','quarantine','retired','filter'},'closed receipt name')
        need((self.directory.stat().st_dev,self.directory.stat().st_ino)==self.identity and not self.directory.is_symlink(),'receipt generation differs')
        need(len(list(self.directory.iterdir()))<512,'finite receipt history')
        raw=encode(row);need(len(raw)<=MAX_RAW and not any(k in raw for k in (b'"Env":',b'"body":',b'"requestBody":',b'"password":')),'nonsensitive bounded receipt')
        path=self.directory/(nonce+'.'+kind+'.json');flags=os.O_WRONLY|os.O_CREAT|os.O_EXCL|getattr(os,'O_NOFOLLOW',0)
        fd=os.open(path,flags,0o600)
        try:
            view=memoryview(raw)
            while view:
                count=os.write(fd,view);need(count>0,'durable receipt short write');view=view[count:]
            os.fsync(fd)
        finally:os.close(fd)
        if os.name=='posix':
            parent=os.open(self.directory,os.O_RDONLY|getattr(os,'O_DIRECTORY',0))
            try:os.fsync(parent)
            finally:os.close(parent)
        self.retained[(nonce,kind)]=sha(raw);return sha(raw)

class ActiveBudget:
    def __init__(self,max_memory,max_cpu,max_active=8):
        need(type(max_memory) is int and 0<max_memory<=2*1024**3 and type(max_cpu) is int and 0<max_cpu<=2*10**9 and type(max_active) is int and 0<max_active<=8,'aggregate run caps')
        self.limit=(max_memory,max_cpu,max_active);self.active={}
    def reserve(self,nonce,memory,cpu,free_kib):
        need(type(free_kib) is int and free_kib>=4194304,'fresh full-phase physical floor')
        need(type(memory) is int and 0<memory<=1024**3 and type(cpu) is int and 0<cpu<=10**9 and nonce not in self.active,'allocation caps/replay')
        need(len(self.active)<self.limit[2] and sum(v[0] for v in self.active.values())+memory<=self.limit[0] and sum(v[1] for v in self.active.values())+cpu<=self.limit[1],'aggregate active allocation cap')
        self.active[nonce]=(memory,cpu)
    def retire(self,nonce,actual_absence):
        need(actual_absence is True and nonce in self.active,'actual owned absence required; quarantine retains budget');del self.active[nonce]
