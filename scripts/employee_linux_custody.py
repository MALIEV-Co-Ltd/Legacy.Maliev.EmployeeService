"""Linux finite independent custody. No SDK/provider authority is created here.

The executable no-SDK route qualifies actual disposable systemd behavior only.
SDKOwner still requires separately authenticated Root and backend receipts.
"""
import argparse
import base64
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import select
import stat
import sys
import time
import types

ADAPTER_SHA = '8618a2ca9bd01f02e108303b78cfafd9a8b68d6a987026381e24bb7ea5c77d69'
AUTHORITY_SHA = '1d19033025cf0a7a7cee56259f34fe91519356a47a04b2b8ef35e1668bbacc25'
TRUSTED_ROOT_PUBLIC_KEY_SHA256 = None
CUSTODY_DOMAIN = b'employee-linux-custody-admission/v1\x00'
HEX = re.compile(r'[0-9a-f]{64}\Z')
BOOT = re.compile(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z')
FLOOR = 4194304
MAX_HELPER_SECONDS = 90


def need(ok, message):
    if not ok: raise ValueError(message)


def encode(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def now():
    return dt.datetime.now(dt.timezone.utc)


def stamp():
    return now().isoformat().replace('+00:00', 'Z')


def utc(value):
    need(type(value) is str and value.endswith('Z'), 'explicit UTC required')
    return dt.datetime.fromisoformat(value[:-1]+'+00:00')


def read(path, limit=65536):
    path = Path(path)
    for part in (path, *path.parents): need(not part.is_symlink(), 'linked evidence/source path')
    with path.open('rb') as stream: raw = stream.read(limit+1)
    need(len(raw) <= limit, 'read bound exceeded')
    return raw


def parse(raw):
    need(type(raw) is bytes and 0 < len(raw) <= 65536, 'bounded JSON required')
    def pairs(items):
        row = {}
        for key, value in items:
            need(key not in row, 'duplicate JSON key'); row[key] = value
        return row
    value = json.loads(raw, object_pairs_hook=pairs, parse_constant=lambda _: (_ for _ in ()).throw(ValueError('nonfinite JSON')))
    need(encode(value) == raw, 'canonical original JSON required')
    return value


def closed(value, keys):
    need(type(value) is dict and set(value) == set(keys), 'closed schema required')


def atomic(directory, name, value):
    directory = Path(directory)
    for part in (directory, *directory.parents): need(not part.is_symlink(), 'linked evidence directory')
    info = directory.stat()
    need(stat.S_ISDIR(info.st_mode), 'existing evidence directory required')
    if os.name == 'posix': need(info.st_uid == os.geteuid() and stat.S_IMODE(info.st_mode) & 0o077 == 0, 'private exact-user directory required')
    need(re.fullmatch(r'[a-z0-9-]+\.json', name), 'fixed receipt filename required')
    raw = encode(value); need(len(raw) <= 65536, 'receipt bound exceeded')
    fd = os.open(directory/name, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream: stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    if os.name == 'posix':
        parent = os.open(directory, os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            need((os.fstat(parent).st_dev, os.fstat(parent).st_ino) == (info.st_dev, info.st_ino), 'directory generation changed')
            os.fsync(parent)
        finally: os.close(parent)
    return sha(raw)


def load_adapter(raw):
    need(type(raw) is bytes and sha(raw) == ADAPTER_SHA, 'sealed SDK adapter differs')
    module = types.ModuleType('sealed_employee_sdk_adapter')
    exec(compile(raw, 'sealed_employee_sdk_adapter', 'exec'), module.__dict__)
    return module


def typed_property(raw, signature):
    # busctl is not canonical JSON; duplicate and size guards still apply.
    need(type(raw) is bytes and len(raw) <= 65536, 'typed property bound')
    def pairs(items):
        row = {}
        for key, value in items:
            need(key not in row, 'duplicate bus property'); row[key] = value
        return row
    result = json.loads(raw, object_pairs_hook=pairs)
    closed(result, {'type','data'})
    need(result['type'] == signature, 'typed manager signature differs')
    data=result['data']
    if signature.startswith('a'):
        need(type(data) is list,'typed manager array required')
        # busctl releases serialize arrays directly or with one top-level value.
        value=data[0] if len(data)==1 and type(data[0]) is list and (signature in {'ay','as'} or not data[0] or type(data[0][0]) is list) else data
    else:
        value=data[0] if type(data) is list and len(data)==1 else data
    if signature in {'s','o'}: need(type(value) is str, 'typed manager string required')
    elif signature in {'u','t','i'}: need(type(value) is int and (signature == 'i' or value >= 0), 'typed manager integer required')
    elif signature == 'b': need(type(value) is bool, 'typed manager boolean required')
    elif signature == 'ay':
        need(type(value) is list and len(value) == 16 and all(type(v) is int and 0 <= v <= 255 for v in value), 'typed invocation bytes required')
        value = bytes(value).hex()
    elif signature.startswith('a'): need(type(value) is list, 'typed manager array required')
    else: raise ValueError('unsupported property type')
    return value


class ProcObserver:
    def __init__(self, proc='/proc', groups='/sys/fs/cgroup'):
        self.proc = Path(proc); self.groups = Path(groups)
        self.hash_bytes = 0

    def boot(self):
        value = read(self.proc/'sys/kernel/random/boot_id', 64).decode().strip()
        need(BOOT.fullmatch(value), 'actual boot identity required'); return value

    def birth(self, pid):
        need(type(pid) is int and 0 < pid < 2**31, 'typed actual PID required')
        raw = read(self.proc/str(pid)/'stat', 8192).decode()
        prefix, tail = raw.rsplit(')', 1)
        need(prefix.split(' ',1)[0] == str(pid), 'stat PID differs')
        fields = tail.split(); need(len(fields) > 19 and fields[19].isdecimal(), 'actual birth missing')
        return fields[19], int(fields[6])

    def process(self, pid):
        birth, flags = self.birth(pid)
        link = self.proc/str(pid)/'exe'
        try: fd = os.open(link, os.O_RDONLY)
        except FileNotFoundError:
            need(flags & 0x00200000 != 0, 'unknown executable/exit race')
            need(self.birth(pid)[0] == birth, 'kernel PID changed')
            return {'pid':pid,'birthTicks':birth,'kernelThread':True}
        try:
            before = os.fstat(fd)
            need(stat.S_ISREG(before.st_mode) and 0 < before.st_size <= 128*1024**2, 'regular bounded executable required')
            executable = os.readlink(link)
            need(executable.startswith('/') and not executable.endswith(' (deleted)'), 'unqualified deleted executable')
            digest = hashlib.sha256()
            while True:
                block = os.read(fd, 65536)
                if not block: break
                self.hash_bytes += len(block); need(self.hash_bytes <= 512*1024**2, 'aggregate executable census byte budget')
                digest.update(block)
            after = os.fstat(fd)
            need((before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns) == (after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns) and self.birth(pid)[0] == birth and os.readlink(link) == executable, 'executable/PID generation changed')
            return {'pid':pid,'birthTicks':birth,'kernelThread':False,'executable':executable,'device':before.st_dev,'inode':before.st_ino,'sha256':digest.hexdigest()}
        finally: os.close(fd)

    def census(self):
        self.hash_bytes = 0
        def snapshot():
            pids = sorted(int(p.name) for p in self.proc.iterdir() if p.name.isdecimal())
            need(0 < len(pids) <= 4096, 'complete process census bound')
            return [self.process(pid) for pid in pids]
        first = snapshot(); second = snapshot()
        need(first == second, 'complete process/executable census changed')
        return first

    def memory(self):
        matches = re.findall(rb'^MemFree:\s+([0-9]+) kB$', read(self.proc/'meminfo'), re.M)
        need(len(matches) == 1, 'physical MemFree required'); return int(matches[0])

    def cgroup(self, name, retained=None):
        need(type(name) is str and re.fullmatch(r'/system\.slice/[a-z0-9_.-]+\.service', name), 'exact private service group required')
        root = self.groups/name.lstrip('/')
        if not root.exists():
            # exists is not an absence oracle; explicit stat distinguishes errors.
            try: root.stat()
            except FileNotFoundError: return None
            raise ValueError('cgroup presence uncertainty')
        def snapshot():
            dirs=[]; members=[]; queue=[root]
            while queue:
                path=queue.pop(); info=path.stat()
                need(stat.S_ISDIR(info.st_mode) and not path.is_symlink() and len(dirs)<256,'cgroup directory generation required')
                dirs.append((str(path),info.st_dev,info.st_ino))
                values=read(path/'cgroup.procs',8192).decode().split()
                need(all(v.isdecimal() and 0<int(v)<2**31 for v in values),'typed cgroup PIDs required')
                members += [self.process(int(v)) for v in values]
                need(len(members)<=128,'whole cgroup task budget')
                with os.scandir(path) as entries:
                    for entry in entries:
                        metadata=entry.stat(follow_symlinks=False)
                        need(not stat.S_ISLNK(metadata.st_mode),'cgroup links refused')
                        if stat.S_ISDIR(metadata.st_mode): queue.append(Path(entry.path))
            return sorted(dirs),sorted(members,key=lambda r:r['pid'])
        first=snapshot(); second=snapshot();need(first==second,'unstable whole cgroup census')
        if retained is not None: need(first[0][0][1:] == (retained['device'],retained['inode']),'cgroup replaced')
        return {'device':root.stat().st_dev,'inode':root.stat().st_ino,'members':first[1]}


def classify(census, approved_non_native, owned):
    need(type(approved_non_native) is list and len(approved_non_native)<=256 and all(type(v) is str and HEX.fullmatch(v) for v in approved_non_native),'independently reviewed executable allowlist required')
    competing=[]
    for row in census:
        if row['kernelThread']: continue
        if row in owned: continue
        if row['sha256'] not in approved_non_native: competing.append(row)
    return competing


def qualification_policy(raw, expected, observed_boot, source_sha, instant):
    need(type(expected) is str and HEX.fullmatch(expected) and sha(raw)==expected,'original reviewed finite qualification input required')
    value=parse(raw)
    closed(value,{'schemaVersion','purpose','sourceHead','runId','attempt','bootId','sourceSha256','startsUtc','expiresUtc','phaseSeconds','approvedNonNativeExecutableSha256'})
    need(type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['purpose']=='employee-disposable-no-sdk','SDK authority cannot be substituted')
    need(type(value['sourceHead']) is str and re.fullmatch(r'[0-9a-f]{40}',value['sourceHead']) and type(value['runId']) is str and re.fullmatch(r'[1-9][0-9]{0,19}',value['runId']) and type(value['attempt']) is int and 0<value['attempt']<=1000,'exact hosted allocation required')
    need(value['bootId']==observed_boot and value['sourceSha256']==source_sha,'actual boot/source differs')
    start,end=utc(value['startsUtc']),utc(value['expiresUtc'])
    need(start<=instant<end and 0<(end-start).total_seconds()<=3600,'finite original allocation required')
    need(type(value['phaseSeconds']) is int and 2<=value['phaseSeconds']<=30 and (end-instant).total_seconds()>=value['phaseSeconds']+155,'original phase/start/five-stop/cleanup reserve required')
    classify([],value['approvedNonNativeExecutableSha256'],[])
    return value


def verify_sdk_authority(envelope_file,stage,source_sha):
    # This is not caller enrollment: production pin remains absent. All original
    # signed input bytes are read privately and never copied into resource ledgers.
    need(type(TRUSTED_ROOT_PUBLIC_KEY_SHA256) is str and HEX.fullmatch(TRUSTED_ROOT_PUBLIC_KEY_SHA256),'independently enrolled original Root trust absent')
    envelope=parse(read(envelope_file))
    closed(envelope,{'publicKeyHex','authoritySourcePath','policyBase64','policySignatureHex','grantBase64','grantSignatureHex','backendBase64','backendSignatureHex','custodyBase64','custodySignatureHex'})
    source=read(envelope['authoritySourcePath']);need(sha(source)==AUTHORITY_SHA,'original crypto source differs')
    auth=types.ModuleType('sealed_custody_authority');exec(compile(source,'sealed_custody_authority','exec'),auth.__dict__)
    public=bytes.fromhex(envelope['publicKeyHex'])
    verifier=auth.DetachedVerifier(public,TRUSTED_ROOT_PUBLIC_KEY_SHA256)
    def original(name):return base64.b64decode(envelope[name+'Base64'],validate=True)
    def signature(name):return bytes.fromhex(envelope[name+'SignatureHex'])
    policy_raw=original('policy')
    need(verifier.sdk_policy_callback(signature('policy'),stage,now)(policy_raw,TRUSTED_ROOT_PUBLIC_KEY_SHA256) is True,'original exact SDK policy refused')
    policy=auth.parse(policy_raw)
    grant_raw=original('grant');grant=verifier.authenticate('stage-grant',grant_raw,signature('grant'))
    closed(grant,{'stageSha256','runId','attempt','bootId','expiresUtc'})
    unpinned=dict(stage,rootGrantSha256=None)
    need(grant['stageSha256']==sha(encode(unpinned)) and all(type(grant[k]) is type(stage[k]) and grant[k]==stage[k] for k in ('runId','attempt','bootId','expiresUtc')) and sha(grant_raw)==stage['rootGrantSha256'],'original stage grant differs')
    backend_raw=original('backend');verifier.authenticate('backend-qualification',backend_raw,signature('backend'))
    need(sha(backend_raw)==stage['backendQualificationSha256']==policy['backendReceiptSha256'] and policy['backendModuleSha256']==ADAPTER_SHA,'original backend qualification differs')
    custody_raw=original('custody');custody=auth.parse(custody_raw)
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    Ed25519PublicKey.from_public_bytes(public).verify(signature('custody'),CUSTODY_DOMAIN+custody_raw)
    closed(custody,{'stageSha256','custodySourceSha256','approvedNonNativeExecutableSha256','reviewedRuntimeQualified'})
    need(custody['stageSha256']==sha(encode(stage)) and custody['custodySourceSha256']==source_sha and custody['reviewedRuntimeQualified'] is True,'independently reviewed concrete custody absent')
    need(stage['phaseId'] not in {'baseline-focused','candidate-focused','candidate-suite'},'provider phases require separately qualified transport')
    # Candidate baseline review is still consumed by SDK execute_qualification;
    # this direct watchdog cannot bypass it by accepting a review SHA alone.
    need(stage['phaseId'].startswith('baseline-'),'direct watchdog candidate outcome bridge not admitted')
    classify([],custody['approvedNonNativeExecutableSha256'],[])
    return custody['approvedNonNativeExecutableSha256']


def backend_class(adapter_raw):
    adapter=load_adapter(adapter_raw)
    class QualifiedLinuxBackend(adapter.RealLinuxBackend):
        sdk_adapter = adapter
        def __init__(self,evidence,executable_sha256,allowlist=None,adapter_path=None,source_path=None,authority_file=None,watcher_side=False):
            super().__init__(evidence,executable_sha256)
            self.observer=ProcObserver();self.approved=allowlist
            self.adapter_path=adapter_path;self.source_path=source_path or str(Path(__file__).resolve())
            self.authority_file=authority_file
            # The two independent actors share the original aggregate cap by
            # fixed partitions; no cross-process mutable budget is inferred.
            self.helper_remaining=MAX_HELPER_SECONDS/2
            self.helper_cleanup_reserve=15
            self.cleaning=False
            self.watcher=None;self.intent_token=None
            need(type(watcher_side) is bool,'explicit custody role required')
            self.watcher_side=watcher_side

        def _write(self,name,value):
            if re.fullmatch(r'helper-[1-9][0-9]*\.json',name):
                actor='watcher' if self.watcher_side else 'controller'
                name='helper-'+actor+'-'+name.removeprefix('helper-')
            return super()._write(name,value)

        def _command(self,argv,timeout=3):
            available=self.helper_remaining if self.cleaning else self.helper_remaining-self.helper_cleanup_reserve
            need(available>0,'aggregate manager helper budget exhausted; cleanup reserve retained')
            started=time.monotonic()
            try: return super()._command(argv,min(timeout,available))
            finally: self.helper_remaining-=time.monotonic()-started

        def preflight(self):
            need(type(self.approved) is list,'independently reviewed complete executable inventory absent')
            need(self.stage is not None and verify_sdk_authority(self.authority_file,self.stage,sha(read(self.source_path)))==self.approved,'authenticated original inventory differs')
            census=self.observer.census(); competing=classify(census,self.approved,[])
            resolved=Path('/usr/bin/dotnet').resolve(strict=True)
            need(sha(read(resolved,128*1024**2))==self.expected_binary,'actual SDK executable bytes differ')
            free=self.observer.memory()
            return {'bootId':self.observer.boot(),'observedUtc':stamp(),'memFreeKiB':free,'competingProcesses':competing,'candidateExecutableCensusQualified':True}

        def register_intent(self,plan):
            need(self.stage is not None and self.root_access,'actual independently admitted stage required')
            need(self.adapter_path is not None and sha(read(self.adapter_path))==ADAPTER_SHA,'custodian adapter source required')
            self.plan=plan
            controller=self.observer.process(os.getpid())
            source_sha=sha(read(self.source_path))
            admitted=verify_sdk_authority(self.authority_file,self.stage,source_sha)
            need(admitted==self.approved,'independent complete executable classification differs')
            need(not self.stage['workingDirectory'].startswith(('/tmp/','/var/tmp/')),'PrivateTmp hides source projection')
            intent={'plan':plan,'stage':self.stage,'controller':controller,'bootId':self.observer.boot(),'sourceSha256':source_sha,'adapterSha256':ADAPTER_SHA,'createdUtc':stamp(),'approvedNonNativeExecutableSha256':self.approved}
            token=self._write('custody-intent.json',intent)
            self.intent_token=token
            watcher_unit=plan['unit'].removesuffix('.service')+'-custody.service'
            argv=watcher_argv(watcher_unit,self.source_path,self.adapter_path,self.evidence,token,source_sha,self.stage['phaseSeconds']+155)+['--authority',str(self.authority_file)]
            self._command(argv,10)
            wait_file(self.evidence/'custody-ready.json',10)
            ready=parse(read(self.evidence/'custody-ready.json'))
            need(ready['intentSha256']==token and ready['sourceSha256']==source_sha,'independent custody readiness differs')
            unit=ready['unit']
            need(unit==watcher_unit and self._property(unit,'org.freedesktop.systemd1.Unit','InvocationID')[0]==ready['invocationId'],'actual watcher invocation differs')
            row=self.observer.process(ready['pid'])
            need(row['birthTicks']==ready['birthTicks'] and self.observer.boot()==ready['bootId'],'actual watcher process generation differs')
            group=self.observer.cgroup('/system.slice/'+watcher_unit)
            need(group is not None and row in group['members'],'actual watcher cgroup custody missing')
            self.watcher=ready
            return token

        def start_transient(self,plan,token):
            need(plan==self.plan and token==self.intent_token and self.watcher is not None,'independent retained custody missing')
            self._write('custody-start.json',{'intentSha256':token,'requestedUtc':stamp()})
            wait_file(self.evidence/'custody-generation.json',10)
            row=parse(read(self.evidence/'custody-generation.json'))
            need(row['intentSha256']==token,'custody generation differs')

        def persist_generation(self,generation,token):
            row=parse(read(self.evidence/'custody-generation.json'))
            need(row['intentSha256']==token and row['generation']==generation,'independent actual generation differs')
            return self._write('sdk-generation.json',{'generation':generation,'intentSha256':token})

        def _property(self,unit,interface,name):
            signatures={'InvocationID':'ay','ControlGroup':'s','MainPID':'u','ActiveState':'s','SubState':'s','ExecMainStatus':'i','Type':'s','NotifyAccess':'s','WorkingDirectory':'s','RuntimeMaxUSec':'t','TimeoutStartUSec':'t','TimeoutStopUSec':'t','MemoryMax':'t','MemorySwapMax':'t','CPUQuotaPerSecUSec':'t','TasksMax':'t','KillMode':'s','SendSIGKILL':'b','NoNewPrivileges':'b','PrivateTmp':'b','RemainAfterExit':'b','ExecStart':'a(sasbttttuii)','ExecStop':'a(sasbttttuii)','Environment':'as'}
            need(name in signatures,'unknown manager property')
            path='/org/freedesktop/systemd1/unit/'+''.join(c if c.isalnum() else '_'+format(ord(c),'02x') for c in unit)
            raw=self._command(['/usr/bin/busctl','--system','--json=short','--timeout=2s','get-property','org.freedesktop.systemd1',path,interface,name])
            return [typed_property(raw,signatures[name])]

        def stop_exact(self,generation,deadlineSeconds):
            need(deadlineSeconds==120,'original cleanup grace required')
            self.cleaning=True
            super().stop_exact(generation,deadlineSeconds)

        def persist_cleanup(self,receipt):
            # The independent watcher produces custody-terminal only after
            # SDK cleanup returns. Its immutable receipt has a separate name
            # from the controller's, so either settlement order is preserved.
            if self.watcher_side:return self._write('custody-sdk-cleanup.json',receipt)
            result=super().persist_cleanup(receipt)
            self._write('custody-release.json',{'intentSha256':self.intent_token,'cleanupSha256':result,'observedUtc':stamp()})
            wait_file(self.evidence/'custody-terminal.json',10)
            need(self.watcher is not None,'retained independent watcher required')
            unit=self.watcher['unit']
            need(self._property(unit,'org.freedesktop.systemd1.Unit','InvocationID')[0]==self.watcher['invocationId'],'watcher generation changed before release')
            self._command(['/usr/bin/systemctl','stop','--no-block',unit],3)
            end=time.monotonic()+10
            while time.monotonic()<end:
                need(self._property(unit,'org.freedesktop.systemd1.Unit','InvocationID')[0]==self.watcher['invocationId'],'watcher invocation changed during cleanup')
                state=self._property(unit,'org.freedesktop.systemd1.Unit','ActiveState')[0]
                pid=self._property(unit,'org.freedesktop.systemd1.Service','MainPID')[0]
                group=self.observer.cgroup('/system.slice/'+unit)
                if state in {'inactive','failed'} and type(pid) is int and pid==0 and (group is None or group['members']==[]):break
                time.sleep(.05)
            else:raise ValueError('independent watcher cleanup unverified')
            self._write('custody-watcher-cleanup.json',{'unit':unit,'invocationId':self.watcher['invocationId'],'remainingMembers':[],'observedUtc':stamp()})
            return result
    return QualifiedLinuxBackend


def wait_file(path,seconds):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        try: path.stat();return
        except FileNotFoundError: time.sleep(.05)
    raise ValueError('finite custody witness deadline exceeded')


def watcher_argv(unit,source,adapter,evidence,token,source_sha,runtime):
    need(type(runtime) is int and 0<runtime<=755 and re.fullmatch(r'employee-sdk-[a-z0-9-]+-custody\.service',unit),'finite exact custody unit required')
    argv=['/usr/bin/systemd-run','--unit='+unit,'--quiet']
    props={'Type':'exec','RuntimeMaxSec':str(runtime),'TimeoutStartSec':'10','TimeoutStopSec':'5','MemoryMax':str(256*1024**2),'MemorySwapMax':'0','CPUQuota':'25%','TasksMax':'16','KillMode':'control-group','NoNewPrivileges':'yes','PrivateTmp':'no','RemainAfterExit':'yes'}
    for key,value in props.items():argv.append('--property='+key+'='+value)
    return argv+['--','/usr/bin/python3','-I',str(source),'--mode','watch','--evidence',str(evidence),'--adapter',str(adapter),'--intent-sha',token,'--source-sha',source_sha]


def watch(evidence,adapter_path,intent_sha,source_sha,authority_file):
    need(sys.platform=='linux' and os.geteuid()==0,'Linux original root custody required')
    need(sha(read(Path(__file__)))==source_sha,'custodian source changed')
    original=read(Path(evidence)/'custody-intent.json');need(sha(original)==intent_sha,'original custody intent differs')
    intent=parse(original);adapter=load_adapter(read(adapter_path));stage=intent['stage']
    approved=verify_sdk_authority(authority_file,stage,source_sha)
    need(approved==intent['approvedNonNativeExecutableSha256'],'original inventory classification differs')
    plan=adapter.unit_plan(stage,now());need(plan==intent['plan'],'original fixed phase plan differs')
    observer=ProcObserver();need(observer.boot()==intent['bootId'],'custody boot differs')
    controller=intent['controller'];need(observer.process(controller['pid'])==controller,'controller generation differs')
    controller_fd=os.pidfd_open(controller['pid']);sdk_fd=None;generation=None
    need(observer.process(controller['pid'])==controller,'controller changed after pidfd retention')
    # Use the corrected typed property decoder and aggregate helper budget.
    corrected=backend_class(read(adapter_path))(evidence,stage['sdkExecutableSha256'],intent['approvedNonNativeExecutableSha256'],authority_file=authority_file,watcher_side=True)
    corrected.stage=stage;corrected.plan=plan
    owner=adapter.SDKOwner(corrected,stage,lambda *_:False,lambda *_:False);owner.plan=plan
    try:
        watcher_unit=plan['unit'].removesuffix('.service')+'-custody.service'
        invocation=corrected._property(watcher_unit,'org.freedesktop.systemd1.Unit','InvocationID')[0]
        expected_caps={'Type':'exec','RuntimeMaxUSec':(stage['phaseSeconds']+155)*1000000,'MemoryMax':256*1024**2,'MemorySwapMax':0,'CPUQuotaPerSecUSec':250000,'TasksMax':16,'KillMode':'control-group','NoNewPrivileges':True,'PrivateTmp':False,'RemainAfterExit':True}
        for field,expected in expected_caps.items():need(corrected._property(watcher_unit,'org.freedesktop.systemd1.Service',field)[0]==expected,'actual independent watcher caps differ')
        group=observer.cgroup('/system.slice/'+watcher_unit)
        need(group is not None and any(row['pid']==os.getpid() for row in group['members']),'independent watcher not in actual owned unit')
        atomic(evidence,'custody-ready.json',{'intentSha256':intent_sha,'sourceSha256':source_sha,'unit':watcher_unit,'invocationId':invocation,'pid':os.getpid(),'birthTicks':observer.birth(os.getpid())[0],'bootId':observer.boot(),'observedUtc':stamp()})
        wait_file(Path(evidence)/'custody-start.json',10)
        request=parse(read(Path(evidence)/'custody-start.json'));need(request['intentSha256']==intent_sha,'start request changed')
        need(not select.select([controller_fd],[],[],0)[0],'controller already exited')
        fresh=corrected.preflight()
        need(fresh['bootId']==stage['bootId'] and fresh['memFreeKiB']>=FLOOR and fresh['competingProcesses']==[],'fresh start admission failed')
        # Independent custodian performs the actual start, so lost controller
        # ACK never loses the object that owns acquisition and settlement.
        # Bypass only the controller's start-request handshake; all real
        # manager helpers retain this watcher's receipt namespace and budget.
        corrected.sdk_adapter.RealLinuxBackend.start_transient(corrected,plan,intent_sha)
        item=corrected.observe(plan['unit'])
        owner.generation=owner._validate(item,running=item['active']);generation=owner.generation
        if item['active']:
            sdk_fd=os.pidfd_open(item['pid'])
            actual_sdk=observer.process(item['pid'])
            need(actual_sdk['sha256']==stage['sdkExecutableSha256'] and actual_sdk['birthTicks']==generation['birthTicks'],'actual SDK generation binary differs')
        atomic(evidence,'custody-generation.json',{'intentSha256':intent_sha,'generation':generation,'observedUtc':stamp()})
        end=time.monotonic()+stage['phaseSeconds']
        while time.monotonic()<end and now()<utc(stage['expiresUtc']):
            if select.select([controller_fd],[],[],.1)[0]:break
            if (Path(evidence)/'custody-release.json').is_file():break
            item=corrected.observe(plan['unit']);owner._same_generation_or_terminal(item)
            if not item['active']:break
    finally:
        try:
            if generation is not None:
                corrected.cleaning=True
                receipt=owner.cleanup(now())
                atomic(evidence,'custody-terminal.json',{'intentSha256':intent_sha,'generation':generation,'cleanup':receipt,'observedUtc':stamp(),'runtimeQualified':False})
            else:
                atomic(evidence,'custody-uncertain.json',{'intentSha256':intent_sha,'failureType':'UncertainStart','runtimeQualified':False})
        finally:
            if sdk_fd is not None:os.close(sdk_fd)
            os.close(controller_fd)


def qualify_no_sdk(args):
    """Actual Linux disposable cap/typed-observation/expiry route; never dotnet.

    This does not itself enroll the SDK/provider backend. Controller-loss and
    ambiguous SDK start must still obtain distinct actual witnesses.
    """
    need(sys.platform=='linux' and os.geteuid()==0 and hasattr(os,'pidfd_open'),'Linux root pidfd/systemd qualification host required')
    source_sha=sha(read(Path(__file__)))
    observer=ProcObserver()
    policy=qualification_policy(read(args.policy),args.policy_sha,observer.boot(),source_sha,now())
    evidence=Path(args.evidence)
    need(evidence.is_absolute() and evidence.is_dir(),'precreated private evidence required')
    adapter=load_adapter(read(args.adapter))
    Backend=backend_class(read(args.adapter))
    backend=Backend(evidence,'0'*64,policy['approvedNonNativeExecutableSha256'])
    # NoSDK census does not call the SDK binary admission method.
    census=observer.census()
    need(observer.memory()>=FLOOR and classify(census,policy['approvedNonNativeExecutableSha256'],[])==[],'fresh physical floor/executable census refused')
    unit='employee-custody-probe-'+policy['runId']+'-'+str(policy['attempt'])+'.service'
    seconds=policy['phaseSeconds']
    properties={'Type':'exec','NotifyAccess':'none','RuntimeMaxUSec':seconds*1000000,'TimeoutStartUSec':10000000,'TimeoutStopUSec':5000000,'MemoryMax':3*1024**3,'MemorySwapMax':0,'CPUQuotaPerSecUSec':250000,'TasksMax':128,'KillMode':'control-group','SendSIGKILL':True,'NoNewPrivileges':True,'PrivateTmp':True,'RemainAfterExit':True,'Environment':[]}
    literal={'Type':'exec','NotifyAccess':'none','RuntimeMaxSec':str(seconds),'TimeoutStartSec':'10','TimeoutStopSec':'5','MemoryMax':str(3*1024**3),'MemorySwapMax':'0','CPUQuota':'25%','TasksMax':'128','KillMode':'control-group','SendSIGKILL':'yes','NoNewPrivileges':'yes','PrivateTmp':'yes','RemainAfterExit':'yes'}
    command=['/usr/bin/python3','-I',str(Path(__file__).resolve()),'--mode','probe-payload']
    argv=['/usr/bin/systemd-run','--quiet','--unit='+unit]+['--property='+k+'='+v for k,v in literal.items()]+['--',*command]
    intent={'sourceOnly':True,'nativeSDKExecutionGranted':False,'policySha256':args.policy_sha,'sourceSha256':source_sha,'unit':unit,'command':command,'properties':properties,'bootId':observer.boot(),'observedUtc':stamp()}
    token=atomic(evidence,'qualification-intent.json',intent)
    generation=None;pidfd=None
    try:
        backend._command(argv,10)
        def prop(interface,name,signature):
            path='/org/freedesktop/systemd1/unit/'+''.join(c if c.isalnum() else '_'+format(ord(c),'02x') for c in unit)
            return typed_property(backend._command(['/usr/bin/busctl','--system','--json=short','--timeout=2s','get-property','org.freedesktop.systemd1',path,interface,name]),signature)
        service='org.freedesktop.systemd1.Service';manager='org.freedesktop.systemd1.Unit'
        invocation=prop(manager,'InvocationID','ay')
        group=prop(service,'ControlGroup','s');pid=prop(service,'MainPID','u')
        need(group=='/system.slice/'+unit and pid>0,'actual probe group/PID required')
        kernel=observer.cgroup(group);process=observer.process(pid)
        need(kernel is not None and process in kernel['members'],'actual complete probe membership required')
        generation={'unit':unit,'invocationId':invocation,'bootId':observer.boot(),'cgroup':group,'device':kernel['device'],'inode':kernel['inode'],'process':process}
        atomic(evidence,'qualification-generation.json',{'generation':generation,'intentSha256':token,'observedUtc':stamp(),'containmentValidated':False})
        pidfd=os.pidfd_open(pid)
        for name,expected in properties.items():
            sig='b' if type(expected) is bool else 't' if type(expected) is int else 'as' if name=='Environment' else 's'
            need(prop(service,name,sig)==expected,'actual manager containment differs')
        actual=prop(service,'ExecStart','a(sasbttttuii)')
        need(len(actual)==1 and type(actual[0]) is list and actual[0][:3]==[command[0],command,False],'literal actual probe argv differs')
        atomic(evidence,'qualification-containment.json',{'generation':generation,'intentSha256':token,'observedUtc':stamp(),'containmentValidated':True})
        deadline=time.monotonic()+seconds+35
        while time.monotonic()<deadline:
            need(prop(manager,'InvocationID','ay')==invocation and observer.boot()==generation['bootId'],'manager invocation changed')
            active=prop(manager,'ActiveState','s');main=prop(service,'MainPID','u')
            if active in {'inactive','failed'} and main==0:break
            time.sleep(.1)
        else:raise ValueError('actual finite expiry failed')
        # Terminal absence is accepted only with the retained same invocation.
        kernel=observer.cgroup(group,generation)
        need(kernel is None or kernel['members']==[],'probe descendants remain')
        need(select.select([pidfd],[],[],0)[0],'retained probe PID has not exited')
        atomic(evidence,'qualification-result.json',{'generation':generation,'actualFiniteExpiryPassed':True,'actualTypedPropertiesPassed':True,'remainingMembers':[],'managerState':active,'execMainStatus':prop(service,'ExecMainStatus','i'),'observedUtc':stamp(),'runtimeQualified':False,'sdkProviderQualified':False})
    finally:
        try:
            backend.cleaning=True
            if generation is not None:
                need(prop(manager,'InvocationID','ay')==generation['invocationId'],'changed invocation preserved')
                kernel=observer.cgroup(generation['cgroup'],generation)
                if kernel is not None:
                    need(all(row==process or row['pid']!=process['pid'] for row in kernel['members']),'probe PID generation changed')
                backend._command(['/usr/bin/systemctl','stop','--no-block',unit],3)
                end=time.monotonic()+120
                while time.monotonic()<end:
                    need(prop(manager,'InvocationID','ay')==generation['invocationId'],'cleanup invocation changed')
                    kernel=observer.cgroup(generation['cgroup'],generation)
                    if prop(manager,'ActiveState','s') in {'inactive','failed'} and prop(service,'MainPID','u')==0 and (kernel is None or kernel['members']==[]):break
                    time.sleep(.1)
                else:raise ValueError('same-generation qualification cleanup deadline failed')
                atomic(evidence,'qualification-cleanup.json',{'intentSha256':token,'generation':generation,'remainingMembers':[],'actualTerminal':True,'observedUtc':stamp()})
            else:atomic(evidence,'qualification-uncertain.json',{'intentSha256':token,'failureType':'UncertainAcquisition','runtimeQualified':False})
        finally:
            if pidfd is not None:os.close(pidfd)


def probe_payload():
    # Fixed harmless payload. systemd RuntimeMax, rather than this sleep, expires it.
    time.sleep(60)


def main(argv=None):
    p=argparse.ArgumentParser();p.add_argument('--mode',choices=['watch','qualify-no-sdk','probe-payload'],required=True)
    p.add_argument('--evidence');p.add_argument('--adapter')
    p.add_argument('--intent-sha');p.add_argument('--source-sha');p.add_argument('--policy');p.add_argument('--policy-sha');p.add_argument('--authority')
    args=p.parse_args(argv)
    if args.mode=='probe-payload':return probe_payload()
    if args.mode=='watch':return watch(args.evidence,args.adapter,args.intent_sha,args.source_sha,args.authority)
    return qualify_no_sdk(args)


if __name__=='__main__':main()
