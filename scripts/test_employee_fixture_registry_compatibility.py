"""Synthetic original request controls; no Root key or runtime enrollment."""
import datetime as dt
import json
import os
from pathlib import Path
import tempfile
import unittest
import employee_fixture_registry_compatibility as r
NOW=dt.datetime.now(dt.timezone.utc)
SESSION='11111111-1111-1111-1111-111111111111'
SDK={'pid':123,'birth':'456','bootId':SESSION,'unit':'owned.service','invocationId':'a'*32,'cgroup':'/owned/sdk','expiresUtc':(NOW+dt.timedelta(seconds=90)).isoformat().replace('+00:00','Z')}
def vector(image='postgres:18-alpine',nonce='a'*32):
    expiry=(NOW+dt.timedelta(seconds=60)).isoformat().replace('+00:00','Z')
    static={'org.testcontainers':'true','org.testcontainers.lang':'dotnet','org.testcontainers.version':'synthetic-not-an-actual-assembly-version','org.testcontainers.session-id':SESSION}
    labels={**static,'org.testcontainers.resource-reaper-session':'00000000-0000-0000-0000-000000000000' if image.startswith('testcontainers') else SESSION}
    env=['POSTGRES_DB=synthetic','POSTGRES_USER=synthetic','POSTGRES_PASSWORD=synthetic'] if image.startswith('postgres') else []
    original={'Image':image,'Name':'original-'+nonce,'Cmd':r.PG_FLAGS if image.startswith('postgres') else [],'Entrypoint':None,'Env':env,'Labels':labels,'HostConfig':{'AutoRemove':image.startswith('testcontainers'),'Privileged':image.startswith('testcontainers'),'PortBindings':{'5432/tcp':[{'HostPort':'0'}]}},'NetworkingConfig':{'EndpointsConfig':{}}}
    port='5432/tcp' if image.startswith('postgres') else '8080/tcp' if image.startswith('testcontainers') else '6379/tcp'
    host={'Memory':268435456,'MemorySwap':268435456,'NanoCpus':250000000,'PidsLimit':128,'Privileged':False,'CapAdd':None,'CapDrop':['ALL'],'SecurityOpt':['no-new-privileges'],'NetworkMode':'bridge','PortBindings':{port:[{'HostIp':'127.0.0.1','HostPort':'0'}]},'Tmpfs':{'/var/lib/postgresql' if image.startswith('postgres') else '/data':'rw,noexec,nosuid,nodev,size=134217728'} if not image.startswith('testcontainers') else {},'Binds':[],'Mounts':[],'PublishAllPorts':False,'PidMode':'','IpcMode':'private','AutoRemove':image.startswith('testcontainers'),'ExtraHosts':[]}
    relay='/private/employee-fixture-'+nonce+'/docker.sock' if image.startswith('testcontainers') else None
    if relay:host['Mounts']=[{'Type':'bind','Source':relay,'Target':'/var/run/docker.sock','ReadOnly':False}]
    custody={'employee.owner':'synthetic-owner','employee.run':nonce,'employee.disposable':'true','employee.expiry':expiry}
    original_raw=json.dumps(original,indent=1,ensure_ascii=False).encode()
    effective=original_raw;sp=r.spans(original_raw)
    for key,value in sorted({'HostConfig':host,'Labels':{**labels,**custody}}.items(),key=lambda row:sp[row[0]][0],reverse=True):
        start,end=sp[key];effective=effective[:start]+r.encode(value)+effective[end:]
    config={'Image':image,'Cmd':original['Cmd'],'Env':env,'Labels':{**labels,**custody}}
    plan={'schemaVersion':1,'originalSha256':r.sha(original_raw),'effectiveSha256':r.sha(effective),'name':original['Name'],'sessionId':SESSION,'staticLabels':static,'custodyLabels':custody,'hostConfig':host,'ryukSocketPath':relay,'imageId':'sha256:'+'b'*64,'sdkGeneration':SDK,'expiresUtc':expiry,'inspectConfigSha256':r.sha(r.encode(config)),'inspectHostConfigSha256':r.sha(r.encode(host)),'daemonId':'synthetic-daemon'}
    return original_raw,r.encode(plan),effective,config,host
class Tests(unittest.TestCase):
    def test_literal_env_and_flags_preserved(self):
        raw,plan,expected,_,_=vector();actual,receipt=r.transform(raw,plan);self.assertEqual(actual,expected);self.assertEqual(r.parse(actual)['Cmd'],r.PG_FLAGS);a=r.spans(raw)['Env'];b=r.spans(actual)['Env'];self.assertEqual(raw[a[0]:a[1]],actual[b[0]:b[1]]);self.assertFalse(receipt['executionGranted'])
    def test_redis74_original(self):raw,plan,expected,_,_=vector('redis:7.4-alpine');self.assertEqual(r.transform(raw,plan)[0],expected)
    def test_redis8_original(self):raw,plan,expected,_,_=vector('redis:8-alpine');self.assertEqual(r.transform(raw,plan)[0],expected)
    def test_explicit_ryuk_private_mount_reduced_privilege(self):
        raw,plan,expected,_,_=vector('testcontainers/ryuk:0.14.0');self.assertTrue(r.parse(raw)['HostConfig']['Privileged']);item=r.parse(r.transform(raw,plan)[0]);self.assertFalse(item['HostConfig']['Privileged']);self.assertEqual(item['HostConfig']['Mounts'][0]['Source'],r.parse(plan)['ryukSocketPath'])
    def bad(self,key,value):
        raw,plan,_,_,_=vector();item=r.parse(plan);item[key]=value
        with self.assertRaises(ValueError):r.transform(raw,r.encode(item))
    def test_original_sha_drift(self):self.bad('originalSha256','0'*64)
    def test_effective_sha_drift(self):self.bad('effectiveSha256','0'*64)
    def test_unknown_plan_field(self):
        raw,plan,_,_,_=vector();item=r.parse(plan);item['automaticGrant']=True
        with self.assertRaises(ValueError):r.transform(raw,r.encode(item))
    def test_foreign_session(self):self.bad('sessionId','22222222-2222-2222-2222-222222222222')
    def test_static_labels_mutation(self):self.bad('staticLabels',{'org.testcontainers':'false'})
    def test_custody_not_additive(self):self.bad('custodyLabels',{'org.testcontainers':'override'})
    def test_missing_image_pin(self):self.bad('imageId','postgres:18-alpine')
    def test_bool_sdk_pid(self):self.bad('sdkGeneration',{**SDK,'pid':True})
    def test_unknown_sdk_cgroup(self):self.bad('sdkGeneration',{**SDK,'cgroup':'/owned/../sdk'})
    def test_cap_floor_not_guessed(self):
        raw,plan,_,_,_=vector();item=r.parse(plan);host=item['hostConfig'];host['Memory']=2*1024**3
        with self.assertRaises(ValueError):r.transform(raw,r.encode(item))
    def test_swap_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['MemorySwap']=-1
        with self.assertRaises(ValueError):r.caps(host)
    def test_cpu_bool_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['NanoCpus']=True
        with self.assertRaises(ValueError):r.caps(host)
    def test_global_port_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['PortBindings']['5432/tcp'][0]['HostIp']='0.0.0.0'
        with self.assertRaises(ValueError):r.caps(host)
    def test_privileged_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['Privileged']=True
        with self.assertRaises(ValueError):r.caps(host)
    def test_broad_socket_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['Binds']=['/var/run/docker.sock:/var/run/docker.sock']
        with self.assertRaises(ValueError):r.caps(host)
    def test_unknown_host_field_refused(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['Devices']=['foreign']
        with self.assertRaises(ValueError):r.caps(host)
    def test_tmpfs_size_bound(self):
        _,plan,_,_,_=vector();host=r.parse(plan)['hostConfig'];host['Tmpfs']['/var/lib/postgresql']='rw,noexec,nosuid,nodev,size=9999999999'
        with self.assertRaises(ValueError):r.caps(host)
    def test_root_absent_before_crypto(self):
        with self.assertRaises(PermissionError):r.SignedPlan(None,None,None)
    def test_duplicate_json_refused(self):
        with self.assertRaises(ValueError):r.parse(b'{"a":1,"a":2}')
    def test_json_depth_bound(self):
        with self.assertRaises(ValueError):r.parse(b'['*17+b'0'+b']'*17)
    def test_literal_unknown_env_refused(self):
        raw,plan,_,_,_=vector();item=r.parse(raw);item['Env'].append('FOREIGN=synthetic');bad=r.encode(item);parsed=r.parse(plan);parsed['originalSha256']=r.sha(bad)
        with self.assertRaises(ValueError):r.transform(bad,r.encode(parsed))
    def test_active_sum_cap(self):
        b=r.ActiveBudget(512*1024**2,10**9);b.reserve('a'*32,512*1024**2,250000000,4194304)
        with self.assertRaises(ValueError):b.reserve('b'*32,1,1,4194304)
        self.assertEqual(len(b.active),1)
    def test_fresh_floor_refusal(self):
        with self.assertRaises(ValueError):r.ActiveBudget(512*1024**2,10**9).reserve('a'*32,1,1,4194303)
    def test_quarantine_budget_retained(self):
        b=r.ActiveBudget(512*1024**2,10**9);b.reserve('a'*32,1,1,4194304)
        with self.assertRaises(ValueError):b.retire('a'*32,False)
        self.assertEqual(len(b.active),1);b.retire('a'*32,True);self.assertFalse(b.active)
    def test_durable_intent_no_adoption_or_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            j=r.Journal(Path(tmp).resolve());j.write('a'*32,'intent',{'rawRequestSha256':'b'*64,'sourceOnly':True})
            with self.assertRaises(FileExistsError):j.write('a'*32,'intent',{'rawRequestSha256':'b'*64})
            self.assertEqual(len(j.retained),1)
    def test_no_secret_receipt(self):
        with tempfile.TemporaryDirectory() as tmp:
            j=r.Journal(Path(tmp).resolve())
            with self.assertRaises(ValueError):j.write('a'*32,'intent',{'Env':['secret=synthetic']})
            self.assertEqual(list(Path(tmp).iterdir()),[])
if __name__=='__main__':unittest.main()
