"""Pure fake-socket controls, synthetic observations; no runtime admission."""
import datetime as dt
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import types
import employee_fixture_compatibility as r
import employee_fixture_registry_compatibility as c
from test_employee_fixture_registry_compatibility import vector,NOW,SDK,SESSION
CID='c'*64
EID='e'*64
SOCKET={'device':1,'inode':2,'uid':1000,'gid':1000,'mode':0o600}
PEER={'pid':123,'uid':1000,'gid':1000,'birth':'456','bootId':SESSION,'cgroup':'/owned/sdk'}
class Conn:
    def __init__(self,raw):self.raw=raw;self.sent=b'';self.closed=False;self.reads=0;self.fail_send=False
    def settimeout(self,value):assert 0<value<=120
    def connect(self,path):self.path=path
    def recv(self,count):self.reads+=1;raw,self.raw=self.raw[:count],self.raw[count:];return raw
    def send(self,raw):
        if self.fail_send:return 0
        self.sent+=bytes(raw);return len(raw)
    def close(self):self.closed=True
class Wire:
    mode='synthetic-source-control'
    def __init__(self,rows):self.rows=list(rows);self.calls=[]
    def request(self,*args,**kwargs):
        self.calls.append((args,kwargs));row=self.rows.pop(0)
        if isinstance(row,Exception):raise row
        return row

def reply(status,value):return status,{},c.encode(value)
def info():return reply(200,{'ID':'synthetic-daemon'})
def own_row():
    raw,plan,effective,config,host=vector()
    return {'Id':CID,'Created':NOW.isoformat().replace('+00:00','Z'),'Image':'sha256:'+'b'*64,'Name':'/'+c.parse(plan)['name'],'Config':config,'HostConfig':host,'Mounts':[],'ExecIDs':None,'State':{'Running':False}}
def observer(retired=False):return {'sdkGeneration':SDK,'freePhysicalKiB':4194304,'observedUtc':NOW.isoformat().replace('+00:00','Z'),'sdkActive':not retired,'sdkRetired':retired,'clientsEmpty':retired,'expiresUtc':SDK['expiresUtc']}
class RelaySystem:
    def __init__(self,wire):self.files={};self.conn=Conn(wire);self.closedfd=False;self.alive=True;self.actual=dict(PEER)
    def identity(self,path):return dict(self.files[path])
    def private_base(self,path,uid):pass
    def validate_owner_pipe(self,fd):pass
    def owner_alive(self,fd):return self.alive
    def mkdir(self,path):self.files[path]={**SOCKET,'inode':5,'mode':0o700,'kind':'directory'}
    def chmod(self,path,mode):self.files[path]['mode']=mode
    def socket(self):self.listener=RelayListener(self);return self.listener
    def exists(self,path):return path in self.files
    def unlink(self,path):del self.files[path]
    def rmdir(self,path):assert not any(key.startswith(path+'/') for key in self.files);del self.files[path]
    def peer(self,conn):return dict(self.actual)
    def retain_peer(self,pid):return 10
    def peer_alive(self,fd):return True
    def close_peer(self,fd):self.closedfd=True
    def sdk_descendant(self,actual,sdk):return actual['birth']==sdk['birth']
class RelayListener:
    def __init__(self,system):self.system=system;self.closed=False
    def bind(self,path):self.system.files[path]={**SOCKET,'kind':'socket'}
    def listen(self,backlog):assert backlog==1
    def settimeout(self,value):assert 0<value<=2
    def accept(self):return self.system.conn,None
    def close(self):self.closed=True
class RelayBackend:
    def __init__(self):raw,plan,_,_,_=vector();self.plans={c.sha(raw):plan};self.owned={};self.calls=[]
    def dispatch(self,*args,**kwargs):self.calls.append((args,kwargs));return 200,{b'content-type':b'application/json'},b'{}'
def sdk_stage(module):
    return {'schemaVersion':1,'sourceOnly':True,'sourceHead':'5ace8e2b7b1de498100c2e3cd7d2327451578ea7','codeSha256':'f'*64,'phaseContractSha256':module.PHASE_CONTRACT_SHA256,'sourcePolicySha256':module.SOURCE_POLICY_SHA256,'sdkExecutableSha256':'e'*64,'runId':'1234','attempt':1,'bootId':SESSION,'startsUtc':(NOW-dt.timedelta(seconds=1)).isoformat().replace('+00:00','Z'),'expiresUtc':(NOW+dt.timedelta(seconds=600)).isoformat().replace('+00:00','Z'),'phaseSeconds':60,'phaseId':'baseline-focused','argv':module.FIXED_SDK_PHASES['baseline-focused'],'workingDirectory':'/private/frozen/baseline','rootGrantSha256':None,'backendQualificationSha256':None}
class Tests(unittest.TestCase):
    def backend(self,rows,retired=False):
        tmp=tempfile.TemporaryDirectory();self.addCleanup(tmp.cleanup);wire=Wire(rows);raw,plan,_,_,_=vector();backend=r.OriginalFixtureBackend(wire,c.Journal(Path(tmp.name).resolve()),c.ActiveBudget(1024**3,10**9),{c.sha(raw):plan},{'postgres:18-alpine':'sha256:'+'b'*64,'approvedExecCommandSha256':[c.sha(c.encode(['/bin/sh','-c','synthetic-wait']))]},SDK,lambda:observer(retired),mode='synthetic-source-control',clock=lambda:NOW,daemon_id='synthetic-daemon');return backend,wire
    def adopted_synthetic(self,b):
        raw,plan,eff,config,host=vector();p=c.parse(plan);b.owned[CID]={'nonce':'a'*32,'rawCreated':own_row()['Created'],'imageId':p['imageId'],'name':p['name'],'labels':config['Labels'],'hostConfig':host,'sessionId':SESSION,'image':'postgres:18-alpine','ryukSocketPath':None,'inspectConfigSha256':p['inspectConfigSha256'],'inspectHostConfigSha256':p['inspectHostConfigSha256'],'expiresUtc':p['expiresUtc']};b.budget.reserve('a'*32,host['Memory'],host['NanoCpus'],4194304)
    def test_production_forwarder_before_socket(self):
        with self.assertRaises(PermissionError):r.UnixForwarder('/private/socket',SOCKET,PEER)
    def test_production_backend_before_observer(self):
        with self.assertRaises(PermissionError):r.OriginalFixtureBackend(None,None,None,{}, {},{},lambda:(_ for _ in ()).throw(AssertionError('no observer')))
    def test_production_relay_before_bind(self):
        with self.assertRaises(PermissionError):r.PrivateRelay('/private','a'*32,None,{}, {},None,'2026-10-08T10:00:00Z')
    def test_route_inventory(self):
        rows=[('POST','/containers/create?name=original','create'),('POST','/containers/'+CID+'/start','start'),('POST','/containers/'+CID+'/stop?t=10','stop'),('GET','/containers/'+CID+'/json','inspect'),('POST','/containers/'+CID+'/wait','wait'),('POST','/containers/'+CID+'/exec','exec-create'),('POST','/exec/'+EID+'/start','exec-start'),('GET','/exec/'+EID+'/json','exec-inspect'),('GET','/containers/'+CID+'/logs?stdout=1&stderr=1','logs'),('DELETE','/containers/'+CID+'?force=true&v=true','delete'),('GET','/images/postgres:18-alpine/json','image-inspect'),('POST','/images/create?fromImage=postgres:18-alpine','image-pull'),('GET','/events?filters=%7B%7D','events'),('POST','/containers/'+CID+'/attach?stdout=1&stderr=1&stream=1','attach')]
        for method,target,action in rows:
            with self.subTest(action=action):self.assertEqual(r.route(method,'/v1.44'+target)[0],action)
    def test_foreign_route_refused(self):
        for path in ['/v1.47/info','/v1.44/networks/create','/v1.44/volumes/create','/v1.44/build','/v1.44/containers/short/json','/v1.44/containers/'+CID+'/kill']:
            with self.subTest(path=path),self.assertRaises(ValueError):r.route('POST',path)
    def test_duplicate_query(self):
        with self.assertRaises(ValueError):r.route('POST','/v1.44/containers/create?name=a&name=b')
    def test_multiplex_preserved(self):
        raw=b'\x01\0\0\0'+struct.pack('>I',3)+b'out'+b'\x02\0\0\0'+struct.pack('>I',3)+b'err';self.assertEqual(r.multiplex(raw),raw)
    def test_multiplex_truncated(self):
        with self.assertRaises(ValueError):r.multiplex(b'\x01\0\0\0'+struct.pack('>I',9)+b'x')
    def test_chunked_preserves_bytes(self):
        conn=Conn(b'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n2\r\nde\r\n0\r\n\r\n');status,_,body=r.read_response(conn,r.Deadline(2));self.assertEqual((status,body),(200,b'abcde'))
    def test_hijack_preserves_multiplex(self):
        body=b'\x01\0\0\0'+struct.pack('>I',1)+b'x';conn=Conn(b'HTTP/1.1 101 UPGRADED\r\nConnection: Upgrade\r\nUpgrade: tcp\r\n\r\n'+body);self.assertEqual(r.read_response(conn,r.Deadline(2),stream=True)[2],body)
    def test_duplicate_response_fails(self):
        with self.assertRaises(ValueError):r.read_response(Conn(b'HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Length: 0\r\n\r\n'),r.Deadline(2))
    def test_ambiguous_response_fails(self):
        with self.assertRaises(ValueError):r.read_response(Conn(b'HTTP/1.1 200 OK\r\nContent-Length: 0\r\nTransfer-Encoding: chunked\r\n\r\n'),r.Deadline(2))
    def test_chunk_extensions_refused(self):
        with self.assertRaises(ValueError):r.read_response(Conn(b'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n1;x=y\r\na\r\n0\r\n\r\n'),r.Deadline(2))
    def test_truncated_response(self):
        with self.assertRaises(ValueError):r.read_response(Conn(b'HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nx'),r.Deadline(2))
    def test_output_bound(self):
        with self.assertRaises(ValueError):r.read_response(Conn(b'HTTP/1.1 200 OK\r\nContent-Length: 1048577\r\n\r\n'),r.Deadline(2))
    def test_controller_deadline_guard(self):
        with self.assertRaises(ValueError):r.Deadline(2,guard=lambda:False).remaining()
    def test_socket_generation_before_factory(self):
        calls=[];f=r.UnixForwarder('/private/socket',SOCKET,PEER,mode='synthetic-source-control',socket_factory=lambda:calls.append(1),identity=lambda path:{**SOCKET,'inode':99},peer_generation=lambda conn:PEER)
        with self.assertRaises(ValueError):f.request('GET','/v1.44/info')
        self.assertEqual(calls,[])
    def test_foreign_daemon_before_send(self):
        conn=Conn(b'');f=r.UnixForwarder('/private/socket',SOCKET,PEER,mode='synthetic-source-control',socket_factory=lambda:conn,identity=lambda path:SOCKET,peer_generation=lambda conn:{**PEER,'birth':'999'})
        with self.assertRaises(ValueError):f.request('GET','/v1.44/info')
        self.assertEqual(conn.sent,b'');self.assertTrue(conn.closed)
    def test_wire_body_preserved_and_closed(self):
        conn=Conn(b'HTTP/1.1 201 Created\r\nContent-Length: 2\r\n\r\n{}');f=r.UnixForwarder('/private/socket',SOCKET,PEER,mode='synthetic-source-control',socket_factory=lambda:conn,identity=lambda path:SOCKET,peer_generation=lambda conn:PEER);body=b' {"Env": ["SYNTHETIC=literal"]} ';self.assertEqual(f.request('POST','/v1.44/containers/create?name=original',body)[0],201);self.assertTrue(conn.sent.endswith(body));self.assertTrue(conn.closed)
    def test_wire_lost_ack_closes(self):
        conn=Conn(b'');conn.fail_send=True;f=r.UnixForwarder('/private/socket',SOCKET,PEER,mode='synthetic-source-control',socket_factory=lambda:conn,identity=lambda path:SOCKET,peer_generation=lambda conn:PEER)
        with self.assertRaises(ValueError):f.request('GET','/v1.44/info')
        self.assertTrue(conn.closed)
    def test_create_intent_before_forward_and_binding(self):
        row=own_row();b,w=self.backend([info(),reply(201,{'Id':CID}),reply(200,row),reply(200,row)]);raw,_,expected,_,_=vector();result=b.dispatch('a'*32,'POST','/v1.44/containers/create?name='+c.parse(raw)['Name'],raw);self.assertEqual(result[0],201);self.assertEqual(w.calls[1][0][2],expected);self.assertIn(('a'*32,'intent'),b.journal.retained);self.assertIn(CID,b.owned)
    def test_create_lost_ack_quarantines_budget(self):
        b,w=self.backend([info(),TimeoutError('synthetic lost ACK')]);raw,_,_,_,_=vector()
        with self.assertRaises(TimeoutError):b.dispatch('a'*32,'POST','/v1.44/containers/create?name='+c.parse(raw)['Name'],raw)
        self.assertIn(('a'*32,'quarantine'),b.journal.retained);self.assertEqual(len(b.budget.active),1)
    def test_unreviewed_request_refuses_before_mutation(self):
        b,w=self.backend([info()])
        with self.assertRaises(ValueError):b.dispatch('a'*32,'POST','/v1.44/containers/create?name=unreviewed',b'{}')
        self.assertEqual(len(w.calls),1);self.assertFalse(b.budget.active)
    def test_wrong_daemon_refuses(self):
        b,w=self.backend([reply(200,{'ID':'foreign'})])
        with self.assertRaises(ValueError):b.dispatch('a'*32,'GET','/v1.44/info')
        self.assertEqual(len(w.calls),1)
    def test_stale_observation_refuses(self):
        b,w=self.backend([]);b.observer=lambda:{**observer(),'observedUtc':(NOW-dt.timedelta(seconds=3)).isoformat().replace('+00:00','Z')}
        with self.assertRaises(ValueError):b.dispatch('a'*32,'GET','/v1.44/info')
        self.assertFalse(w.calls)
    def test_original_live_sdk_delete(self):
        b,w=self.backend([info(),reply(200,own_row()),(204,{},b''),(404,{},b'{}'),info(),reply(200,[])]);self.adopted_synthetic(b);out=b.dispatch('d'*32,'DELETE','/v1.44/containers/'+CID+'?force=true&v=true',cleanup_origin='foreground-sdk');self.assertEqual(out[0],204);self.assertNotIn(CID,b.owned);self.assertFalse(b.budget.active)
    def test_completed_owned_retirement_exists_read_actual_absence(self):
        b,w=self.backend([info(),reply(200,own_row()),(204,{},b''),(404,{},b'{}'),info(),reply(200,[]),info(),(404,{},b'{"message":"synthetic absent"}'),info(),reply(200,[])]);self.adopted_synthetic(b);b.dispatch('d'*32,'DELETE','/v1.44/containers/'+CID,cleanup_origin='foreground-sdk');self.assertIn(CID,b.retired);self.assertEqual(b.dispatch('e'*32,'GET','/v1.44/containers/'+CID+'/json')[0],404);self.assertFalse(b.owned)
    def test_unknown_id_is_not_retirement_tombstone(self):
        b,w=self.backend([info()])
        with self.assertRaises(ValueError):b.dispatch('e'*32,'GET','/v1.44/containers/'+CID+'/json')
        self.assertEqual(len(w.calls),1)
    def test_404_alone_not_absence(self):
        b,w=self.backend([info(),reply(200,own_row()),(204,{},b''),(404,{},b'{}'),reply(503,{})]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('d'*32,'DELETE','/v1.44/containers/'+CID+'?force=true&v=true',cleanup_origin='foreground-sdk')
        self.assertIn(CID,b.quarantined);self.assertTrue(b.budget.active)
    def test_owned_fullid_still_in_inventory_refuses(self):
        b,w=self.backend([info(),reply(200,own_row()),(204,{},b''),(404,{},b'{}'),info(),reply(200,[{'Id':CID}])]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('d'*32,'DELETE','/v1.44/containers/'+CID,cleanup_origin='foreground-sdk')
        self.assertTrue(b.budget.active)
    def test_retired_cleanup_requires_actual_retirement(self):
        b,w=self.backend([]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('d'*32,'DELETE','/v1.44/containers/'+CID,cleanup_origin='retired-ryuk')
        self.assertFalse(w.calls)
    def test_raw_created_drift_no_start(self):
        row=own_row();row['Created']='2020-01-01T00:00:00Z';b,w=self.backend([info(),reply(200,row)]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('d'*32,'POST','/v1.44/containers/'+CID+'/start')
        self.assertEqual(len(w.calls),2)
    def test_actual_persistent_mount_refused(self):
        row=own_row();row['Mounts']=[{'Type':'volume','Destination':'/data','Source':'persistent'}];b,w=self.backend([info(),reply(200,row)]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('d'*32,'GET','/v1.44/containers/'+CID+'/json')
    def test_global_list_refused(self):
        b,w=self.backend([info()])
        with self.assertRaises(ValueError):b.dispatch('d'*32,'GET','/v1.44/containers/json?all=true')
        self.assertEqual(len(w.calls),1)
    def test_ryuk_ack_after_durable_filter(self):
        with tempfile.TemporaryDirectory() as tmp:
            j=c.Journal(Path(tmp).resolve());f=r.RyukFilters(SESSION,j);self.assertEqual(f.register('a'*32,('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode()),b'ACK\n');self.assertIn(('a'*32,'filter'),j.retained)
    def test_ryuk_foreign_filter_refuses(self):
        with tempfile.TemporaryDirectory() as tmp:
            f=r.RyukFilters(SESSION,c.Journal(Path(tmp).resolve()))
            with self.assertRaises(ValueError):f.register('a'*32,b'label=foreign\n')
    def test_ryuk_filter_replay_refuses(self):
        with tempfile.TemporaryDirectory() as tmp:
            f=r.RyukFilters(SESSION,c.Journal(Path(tmp).resolve()));line=('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode();f.register('a'*32,line)
            with self.assertRaises(ValueError):f.register('b'*32,line)
    def test_ryuk_concrete_retired_cleanup_finite_and_absent(self):
        b,w=self.backend([info(),info(),reply(200,own_row()),(204,{},b''),(404,{},b'{}'),info(),reply(200,[])],retired=True);self.adopted_synthetic(b);f=r.RyukFilters(SESSION,b.journal);f.register('b'*32,('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode());result=f.cleanup(b,['d'*32]);self.assertEqual(result[0][0],204);self.assertFalse(b.owned);self.assertFalse(b.budget.active);self.assertIsNone(b.current_deadline)
    def test_ryuk_cleanup_failure_releases_finite_admission(self):
        b,w=self.backend([],retired=False);self.adopted_synthetic(b);f=r.RyukFilters(SESSION,b.journal);f.register('b'*32,('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode())
        with self.assertRaises(ValueError):f.cleanup(b,['d'*32])
        self.assertIsNone(b.current_deadline);self.assertFalse(w.calls);self.assertTrue(b.budget.active)
    def test_ryuk_cleanup_refuses_overlapping_dispatch(self):
        b,w=self.backend([],retired=True);f=r.RyukFilters(SESSION,b.journal);f.register('b'*32,('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode());current=r.Deadline(2);b.current_deadline=current
        with self.assertRaises(ValueError):f.cleanup(b,[])
        self.assertIs(b.current_deadline,current);self.assertFalse(w.calls)
    def test_ryuk_cleanup_attempts_remaining_after_lost_ack(self):
        second='d'*64;row=own_row();row['Id']=second;b,w=self.backend([info(),info(),reply(200,own_row()),TimeoutError('synthetic first ACK loss'),info(),reply(200,row),(204,{},b''),(404,{},b'{}'),info(),reply(200,[{'Id':CID}])],retired=True);self.adopted_synthetic(b);b.owned[second]={**b.owned[CID],'nonce':'f'*32};b.budget.reserve('f'*32,1,1,4194304);f=r.RyukFilters(SESSION,b.journal);f.register('b'*32,('label=org.testcontainers.resource-reaper-session='+SESSION+'\n').encode())
        with self.assertRaises(ExceptionGroup) as caught:f.cleanup(b,['c'*32,'d'*32])
        self.assertIsInstance(caught.exception.exceptions[0],TimeoutError);self.assertIn(CID,b.quarantined);self.assertNotIn(second,b.owned);self.assertEqual(f.retirement_failures[0]['containerId'],CID);self.assertIsNone(b.current_deadline)
    def test_start_expiry_after_inspect_refuses_forward(self):
        b,w=self.backend([info(),reply(200,own_row())]);self.adopted_synthetic(b);b.owned[CID]['expiresUtc']=(NOW+dt.timedelta(seconds=1)).isoformat().replace('+00:00','Z');original=w.request
        def advance(*args,**kwargs):
            result=original(*args,**kwargs)
            if len(w.calls)==2:b.clock=lambda:NOW+dt.timedelta(seconds=2)
            return result
        w.request=advance
        with self.assertRaises(ValueError):b.dispatch('d'*32,'POST','/v1.44/containers/'+CID+'/start')
        self.assertEqual(len(w.calls),2);self.assertNotIn(CID,b.quarantined)
    def test_create_expiry_after_info_refuses_forward(self):
        b,w=self.backend([info()]);original=w.request
        def advance(*args,**kwargs):
            result=original(*args,**kwargs);b.clock=lambda:NOW+dt.timedelta(seconds=3);return result
        w.request=advance;raw,_,_,_,_=vector()
        with self.assertRaises(ValueError):b.dispatch('a'*32,'POST','/v1.44/containers/create?name='+c.parse(raw)['Name'],raw)
        self.assertEqual(len(w.calls),1);self.assertFalse(b.budget.active)
    def relay(self,headers=b'',target=b'/v1.44/info'):
        body=b'GET '+target+b' HTTP/1.1\r\nHost: docker.sock:80\r\nUser-Agent: Docker.DotNet tc-dotnet/synthetic\r\nx-tc-sid: '+SESSION.encode()+b'\r\n'+headers+b'\r\n';system=RelaySystem(body);backend=RelayBackend();x=r.PrivateRelay('/private','a'*32,backend,PEER,{},3,(dt.datetime.now(dt.timezone.utc)+dt.timedelta(seconds=90)).isoformat().replace('+00:00','Z'),mode='synthetic-source-control',system=system);return x,system,backend
    def test_pinned_dotnet_host_headers_relay(self):
        x,system,backend=self.relay();x.run(['a'*32]);self.assertEqual(len(backend.calls),1);self.assertTrue(system.conn.closed);self.assertTrue(system.closedfd);self.assertTrue(x.cleanup_verified);self.assertFalse(system.files)
    def test_guessed_old_host_refused(self):
        x,system,backend=self.relay();system.conn.raw=system.conn.raw.replace(b'docker.sock:80',b'docker')
        with self.assertRaises(ValueError):x.run(['a'*32])
        self.assertFalse(backend.calls);self.assertFalse(system.files)
    def test_foreign_tc_sid_refused(self):
        x,system,backend=self.relay();system.conn.raw=system.conn.raw.replace(SESSION.encode(),b'22222222-2222-2222-2222-222222222222')
        with self.assertRaises(ValueError):x.run(['a'*32])
        self.assertFalse(backend.calls)
    def test_foreign_peer_before_request_read(self):
        x,system,backend=self.relay();system.actual['cgroup']='/foreign'
        with self.assertRaises(ValueError):x.run(['a'*32])
        self.assertEqual(system.conn.reads,0);self.assertTrue(system.conn.closed);self.assertFalse(system.files)
    def test_actual_sdk_child_peer(self):
        x,system,backend=self.relay();system.actual['pid']=124;x.run(['a'*32]);self.assertEqual(len(backend.calls),1)
    def test_controller_death_before_accept(self):
        x,system,backend=self.relay();system.alive=False
        with self.assertRaises(ValueError):x.run(['a'*32])
        self.assertEqual(system.conn.reads,0);self.assertFalse(system.files)
    def test_replaced_endpoint_preserved(self):
        x,system,backend=self.relay();x.open();system.files[x.path]['inode']=999
        with self.assertRaises(ValueError):x.close()
        self.assertTrue(system.exists(x.path));self.assertTrue(system.listener.closed);self.assertFalse(x.cleanup_verified)
    def test_tty_exact_serializer_fields(self):
        b,w=self.backend([info(),reply(200,own_row()),reply(201,{'Id':EID})]);self.adopted_synthetic(b);body=c.encode({'Cmd':['/bin/sh','-c','synthetic-wait'],'AttachStdout':True,'AttachStderr':True,'AttachStdin':False,'Privileged':False,'TTY':False});self.assertEqual(b.dispatch('f'*32,'POST','/v1.44/containers/'+CID+'/exec',body)[0],201);self.assertEqual(b.execs[EID],CID)
    def test_guessed_tty_case_refused(self):
        b,w=self.backend([info(),reply(200,own_row())]);self.adopted_synthetic(b)
        with self.assertRaises(ValueError):b.dispatch('f'*32,'POST','/v1.44/containers/'+CID+'/exec',c.encode({'Cmd':['x'],'AttachStdout':True,'AttachStderr':True,'Tty':False}))
        self.assertEqual(len(w.calls),2)
    def test_null_omission_redis_original(self):
        raw,plan,_,_,_=vector('redis:7.4-alpine');item=c.parse(raw);del item['Cmd'];del item['Env'];del item['Entrypoint'];original=c.encode(item);p=c.parse(plan);p['originalSha256']=c.sha(original);sp=c.spans(original);effective=original
        for key,value in sorted({'HostConfig':p['hostConfig'],'Labels':{**item['Labels'],**p['custodyLabels']}}.items(),key=lambda row:sp[row[0]][0],reverse=True):a,b=sp[key];effective=effective[:a]+c.encode(value)+effective[b:]
        p['effectiveSha256']=c.sha(effective);actual,_=c.transform(original,c.encode(p));self.assertNotIn('Cmd',c.parse(actual));self.assertNotIn('Env',c.parse(actual))
    def test_zero_reaper_static_session_preserved(self):
        raw,plan,_,_,_=vector('testcontainers/ryuk:0.14.0');actual=c.parse(c.transform(raw,plan)[0]);self.assertEqual(actual['Labels']['org.testcontainers.resource-reaper-session'],'00000000-0000-0000-0000-000000000000');self.assertEqual(actual['Labels']['org.testcontainers.session-id'],SESSION)
    def test_anonymous_public_registry_header(self):
        import base64
        raw=base64.b64encode(b'{"serveraddress":"https://index.docker.io/v1/"}');self.assertEqual(r.anonymous_registry_header(raw),raw)
    def test_registry_credentials_refused(self):
        import base64
        with self.assertRaises(ValueError):r.anonymous_registry_header(base64.b64encode(b'{"username":"synthetic","password":"synthetic"}'))
    def test_exact_sdk_successor_patch_no_old_mutation(self):
        path=Path(__file__).with_name('employee_linux_sdk_owner.py');original=path.read_bytes();patched=r.sdk_successor_source(original);self.assertEqual(path.read_bytes(),original);self.assertNotEqual(c.sha(patched),c.sha(original));module=types.ModuleType('synthetic_sdk_postimage');exec(compile(patched,'synthetic_sdk_postimage','exec'),module.__dict__);self.assertIsNone(module._SCOPED_RELAY_BINDING);self.assertIsNone(module.TRUSTED_ROOT_PUBLIC_KEY_SHA256)
        stage=sdk_stage(module);unit=module.unit_plan(stage,NOW);self.assertEqual(unit['properties']['Environment'],[]);self.assertEqual(unit['properties']['MemoryMax'],3*1024**3);self.assertEqual(unit['properties']['CPUQuotaPerSecUSec'],250000);self.assertFalse(unit['executionGranted'])
    def test_sdk_successor_cli_binding_defined_before_entry(self):
        import ast
        original=Path(__file__).with_name('employee_linux_sdk_owner.py').read_bytes();tree=ast.parse(r.sdk_successor_source(original));scope={'__name__':'__main__'};guard_seen=False
        for node in tree.body:
            if isinstance(node,ast.If) and ast.unparse(node.test)=="__name__ == '__main__'":
                self.assertIn('_SCOPED_RELAY_BINDING',scope);self.assertIsNone(scope['_SCOPED_RELAY_BINDING']);guard_seen=True;break
            exec(compile(ast.Module(body=[node],type_ignores=[]),'source-cli-entry-control','exec'),scope)
        self.assertTrue(guard_seen)
    def test_exact_sdk_source_consumption_cli_api(self):
        original=Path(__file__).with_name('employee_linux_sdk_owner.py').read_bytes();module=types.ModuleType('source_sdk');exec(compile(original,'source_sdk','exec'),module.__dict__);stage=sdk_stage(module);raw,plan,_,_,_=vector();result=r.prepare_consumption(c.encode(stage),original,raw,plan,'/private/employee-fixture-'+'a'*32+'/docker.sock',NOW);self.assertEqual(result['originalFrozenArgv'],stage['argv']);self.assertEqual(result['existingSdkEnvironment'],[]);self.assertTrue(result['existingSdkProviderEntryBlocked']);self.assertFalse(result['nativeExecutionGranted'])
    def test_qualification_cli_refuses_before_paths(self):
        with self.assertRaises(PermissionError):r.main(['--mode','qualification','--stage','definitely-missing'])
    def test_scoped_sdk_binding_native_default_absent(self):
        with self.assertRaises(PermissionError):r.ScopedSdkBinding(None,None,None)
    def test_default_root_backend_none(self):self.assertIsNone(r.ROOT_PUBLIC_KEY_SHA256);self.assertIsNone(r.BACKEND_QUALIFICATION_SHA256)
if __name__=='__main__':unittest.main()
