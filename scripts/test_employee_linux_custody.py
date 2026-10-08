"""Pure source controls only; no Linux commands, SDK, daemon or provider."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch
import employee_linux_custody as m

NOW=dt.datetime(2026,10,8,10,tzinfo=dt.timezone.utc)
BOOT='11111111-2222-3333-4444-555555555555'


def prop(kind,data):return json.dumps({'type':kind,'data':data}).encode()


class Controls(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.folder=Path(self.tmp.name)
        self.policy={'schemaVersion':1,'purpose':'employee-disposable-no-sdk','sourceHead':'1'*40,'runId':'123','attempt':1,'bootId':BOOT,'sourceSha256':'2'*64,'startsUtc':'2026-10-08T09:59:00Z','expiresUtc':'2026-10-08T10:20:00Z','phaseSeconds':3,'approvedNonNativeExecutableSha256':['3'*64]}

    def policy_valid(self,p=None,**kw):
        raw=m.encode(self.policy if p is None else p)
        return m.qualification_policy(raw,kw.get('pin',m.sha(raw)),kw.get('boot',BOOT),kw.get('source','2'*64),kw.get('now',NOW))

    def test_original_no_sdk_policy_is_not_sdk_grant(self):
        self.assertEqual('employee-disposable-no-sdk',self.policy_valid()['purpose'])

    def test_missing_pin(self):
        with self.assertRaises(ValueError):self.policy_valid(pin=None)

    def test_changed_pin(self):
        with self.assertRaises(ValueError):self.policy_valid(pin='0'*64)

    def test_sdk_purpose_refused(self):
        p=dict(self.policy,purpose='employee-sdk')
        with self.assertRaises(ValueError):self.policy_valid(p)

    def test_bool_attempt_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(dict(self.policy,attempt=True))

    def test_bool_phase_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(dict(self.policy,phaseSeconds=True))

    def test_unknown_policy_field(self):
        with self.assertRaises(ValueError):self.policy_valid(dict(self.policy,rootGranted=True))

    def test_old_boot_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(boot='0'*36)

    def test_source_drift_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(source='0'*64)

    def test_expiry_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(now=NOW+dt.timedelta(minutes=20))

    def test_cleanup_reserve_refused(self):
        with self.assertRaises(ValueError):self.policy_valid(dict(self.policy,expiresUtc='2026-10-08T10:01:00Z'))

    def test_actual_scalar_string(self):
        self.assertEqual('running',m.typed_property(prop('s','running'),'s'))

    def test_singleton_message_string(self):
        self.assertEqual('running',m.typed_property(prop('s',['running']),'s'))

    def test_actual_scalar_integer(self):
        self.assertEqual(128,m.typed_property(prop('t',128),'t'))

    def test_integer_bool_refused(self):
        with self.assertRaises(ValueError):m.typed_property(prop('u',True),'u')

    def test_typed_false_boolean(self):
        self.assertIs(False,m.typed_property(prop('b',False),'b'))

    def test_direct_invocation_array(self):
        self.assertEqual('01'*16,m.typed_property(prop('ay',[1]*16),'ay'))

    def test_wrapped_invocation_array(self):
        self.assertEqual('01'*16,m.typed_property(prop('ay',[[1]*16]),'ay'))

    def test_boolean_invocation_byte_refused(self):
        with self.assertRaises(ValueError):m.typed_property(prop('ay',[True]*16),'ay')

    def test_wrong_signature(self):
        with self.assertRaises(ValueError):m.typed_property(prop('s','128'),'t')

    def test_duplicate_property(self):
        with self.assertRaises(ValueError):m.typed_property(b'{"type":"s","type":"s","data":"x"}','s')

    def test_unknown_property_field(self):
        with self.assertRaises(ValueError):m.typed_property(b'{"type":"s","data":"x","extra":0}','s')

    def test_direct_and_wrapped_exec_array(self):
        row=['/usr/bin/dotnet',['/usr/bin/dotnet','restore'],False,0,0,0,0,1,0,0]
        self.assertEqual([row],m.typed_property(prop('a(sasbttttuii)',[row]),'a(sasbttttuii)'))
        self.assertEqual([row],m.typed_property(prop('a(sasbttttuii)',[[row]]),'a(sasbttttuii)'))

    def test_empty_environment_direct_and_wrapped(self):
        self.assertEqual([],m.typed_property(prop('as',[]),'as'))
        self.assertEqual([],m.typed_property(prop('as',[[]]),'as'))

    def test_census_unknown_executable_is_competing(self):
        row={'pid':1,'birthTicks':'2','kernelThread':False,'sha256':'4'*64}
        self.assertEqual([row],m.classify([row],['3'*64],[]))

    def test_census_approved_hash_only(self):
        row={'pid':1,'birthTicks':'2','kernelThread':False,'sha256':'3'*64}
        self.assertEqual([],m.classify([row],['3'*64],[]))

    def test_census_owned_exact_row_not_pid_only(self):
        row={'pid':1,'birthTicks':'2','kernelThread':False,'sha256':'4'*64}
        self.assertEqual([row],m.classify([row],[],[dict(row,birthTicks='1')]))

    def test_census_missing_allowlist(self):
        with self.assertRaises(ValueError):m.classify([],None,[])

    def test_complete_census_unstable_refused(self):
        (self.folder/'1').mkdir()
        observer=m.ProcObserver(self.folder)
        with patch.object(observer,'process',side_effect=[{'pid':1,'birthTicks':'1'},{'pid':1,'birthTicks':'2'}]):
            with self.assertRaisesRegex(ValueError,'changed'):observer.census()

    def test_complete_census_disappearing_process_refused(self):
        (self.folder/'1').mkdir()
        observer=m.ProcObserver(self.folder)
        with patch.object(observer,'process',side_effect=FileNotFoundError):
            with self.assertRaises(FileNotFoundError):observer.census()

    def test_memfree_excludes_available(self):
        (self.folder/'meminfo').write_bytes(b'MemFree: 400 kB\nMemAvailable: 8000000 kB\n')
        self.assertEqual(400,m.ProcObserver(self.folder).memory())

    def test_memory_duplicate_refused(self):
        (self.folder/'meminfo').write_bytes(b'MemFree: 400 kB\nMemFree: 500 kB\n')
        with self.assertRaises(ValueError):m.ProcObserver(self.folder).memory()

    def test_birth_bool_pid_refused(self):
        with self.assertRaises(ValueError):m.ProcObserver(self.folder).birth(True)

    def test_cgroup_unknown_layout_refused(self):
        with self.assertRaises(ValueError):m.ProcObserver(groups=self.folder).cgroup('/foreign')

    def test_cgroup_absent_is_not_cleanup_proof(self):
        self.assertIsNone(m.ProcObserver(groups=self.folder).cgroup('/system.slice/owned.service'))

    def test_private_watchdog_fixed_caps_direct_argv(self):
        argv=m.watcher_argv('employee-sdk-123-1-baseline-restore-custody.service','/owned/source.py','/owned/adapter.py','/owned/evidence','1'*64,'2'*64,185)
        self.assertIn('--property=MemoryMax=268435456',argv)
        self.assertIn('--property=MemorySwapMax=0',argv)
        self.assertIn('--property=CPUQuota=25%',argv)
        self.assertIn('--property=KillMode=control-group',argv)
        self.assertNotIn('/bin/sh',argv)

    def test_unbounded_watchdog_refused(self):
        with self.assertRaises(ValueError):m.watcher_argv('employee-sdk-123-custody.service','x','x','x','1'*64,'2'*64,756)

    def test_canonical_receipt_exclusive_partial_no_overwrite(self):
        m.atomic(self.folder,'fixed.json',{'sourceOnly':True})
        with self.assertRaises(FileExistsError):m.atomic(self.folder,'fixed.json',{'sourceOnly':False})

    def test_duplicate_original_json(self):
        with self.assertRaises(ValueError):m.parse(b'{"x":1,"x":2}')

    def test_noncanonical_original_json(self):
        with self.assertRaises(ValueError):m.parse(b'{ "x":1}')

    def test_sealed_adapter_changed_bytes_refused(self):
        with self.assertRaises(ValueError):m.load_adapter(b'raise RuntimeError("must not execute")')

    def test_qualification_windows_refuses_before_backend(self):
        args=type('Args',(),{})()
        with patch.object(m.sys,'platform','win32'):
            with self.assertRaisesRegex(ValueError,'Linux'):m.qualify_no_sdk(args)

    def test_original_root_enrollment_missing_before_authority_read(self):
        with self.assertRaisesRegex(ValueError,'Root trust absent'):m.verify_sdk_authority(None,{},'0'*64)

    def test_watcher_cleanup_persists_without_waiting_on_own_terminal(self):
        class Base:
            def __init__(self,*args):pass
            def persist_cleanup(self,receipt):return 'retained-cleanup-sha'
        with patch.object(m,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            Backend=m.backend_class(b'sealed')
        backend=Backend(self.folder,'0'*64,watcher_side=True)
        with patch.object(m,'wait_file',side_effect=AssertionError('self wait')),patch.object(backend,'_write',return_value='retained-cleanup-sha',create=True) as write:
            self.assertEqual('retained-cleanup-sha',backend.persist_cleanup({'remainingMembers':[]}))
        write.assert_called_once_with('custody-sdk-cleanup.json',{'remainingMembers':[]})

    def test_independent_and_controller_cleanup_have_distinct_immutable_receipts(self):
        class Base:
            def __init__(self,evidence,*args):self.evidence=evidence
            def _write(self,name,value):return m.atomic(self.evidence,name,value)
            def persist_cleanup(self,receipt):return self._write('sdk-cleanup.json',receipt)
        with patch.object(m,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            Backend=m.backend_class(b'sealed')
        watcher=Backend(self.folder,'0'*64,watcher_side=True)
        controller=Backend(self.folder,'0'*64)
        controller.intent_token='1'*64
        controller.watcher={'unit':'employee-sdk-test-custody.service','invocationId':'2'*32}
        watcher.persist_cleanup({'remainingMembers':[],'actor':'watcher'})
        def property_value(unit,interface,name):return [{'InvocationID':'2'*32,'ActiveState':'inactive','MainPID':0}[name]]
        with patch.object(m,'wait_file'),patch.object(controller,'_property',side_effect=property_value),patch.object(controller,'_command'),patch.object(controller.observer,'cgroup',return_value=None):
            controller.persist_cleanup({'remainingMembers':[],'actor':'controller'})
        self.assertEqual('watcher',m.parse(m.read(self.folder/'custody-sdk-cleanup.json'))['actor'])
        self.assertEqual('controller',m.parse(m.read(self.folder/'sdk-cleanup.json'))['actor'])

    def test_two_actor_helpers_preserve_distinct_exclusive_serial_receipts(self):
        class Base:
            def __init__(self,evidence,*args):self.evidence=evidence;self._serial=0
            def _write(self,name,value):return m.atomic(self.evidence,name,value)
            def _command(self,argv,timeout=3):
                self._serial+=1
                self._write('helper-'+str(self._serial)+'.json',{'argv0':argv[0],'cleanupVerified':True})
                return b'observed'
        with patch.object(m,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            Backend=m.backend_class(b'sealed')
        controller=Backend(self.folder,'0'*64)
        watcher=Backend(self.folder,'0'*64,watcher_side=True)
        self.assertEqual(b'observed',controller._command(['/usr/bin/systemd-run']))
        self.assertEqual(b'observed',watcher._command(['/usr/bin/busctl']))
        self.assertTrue((self.folder/'helper-controller-1.json').is_file())
        self.assertTrue((self.folder/'helper-watcher-1.json').is_file())
        self.assertFalse((self.folder/'helper-1.json').exists())
        with self.assertRaises(FileExistsError):watcher._write('helper-1.json',{'changed':True})

    def test_two_actor_helper_budgets_conserve_aggregate_and_cleanup_reserve(self):
        class Base:
            def __init__(self,*args):pass
            def _command(self,*args):return b'observed'
        with patch.object(m,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            Backend=m.backend_class(b'sealed')
        controller=Backend(self.folder,'0'*64)
        watcher=Backend(self.folder,'0'*64,watcher_side=True)
        self.assertEqual(m.MAX_HELPER_SECONDS,controller.helper_remaining+watcher.helper_remaining)
        self.assertEqual(30,controller.helper_cleanup_reserve+watcher.helper_cleanup_reserve)
        for actor in [controller,watcher]:
            actor.helper_remaining=actor.helper_cleanup_reserve
            with self.assertRaisesRegex(ValueError,'cleanup reserve'):actor._command(['/usr/bin/busctl'])
            actor.cleaning=True
            self.assertEqual(b'observed',actor._command(['/usr/bin/systemctl']))

    def test_controller_cleanup_retains_independent_terminal_handshake(self):
        class Base:
            def __init__(self,*args):pass
            def persist_cleanup(self,receipt):return 'retained-cleanup-sha'
        with patch.object(m,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            Backend=m.backend_class(b'sealed')
        backend=Backend(self.folder,'0'*64)
        backend.evidence=self.folder;backend.intent_token='1'*64
        backend.watcher={'unit':'employee-sdk-test-custody.service','invocationId':'2'*32}
        def property_value(unit,interface,name):
            return [{'InvocationID':'2'*32,'ActiveState':'inactive','MainPID':0}[name]]
        with patch.object(m,'wait_file') as wait,patch.object(backend,'_write',return_value='sha',create=True),patch.object(backend,'_property',side_effect=property_value),patch.object(backend,'_command'),patch.object(backend.observer,'cgroup',return_value=None):
            self.assertEqual('retained-cleanup-sha',backend.persist_cleanup({}))
        wait.assert_called_once_with(self.folder/'custody-terminal.json',10)

    def test_probe_cap_refusal_settles_already_retained_exact_generation(self):
        calls=[];stopped=False
        process={'pid':123,'birthTicks':'456'}
        unit='employee-custody-probe-123-1.service'
        group='/system.slice/'+unit
        class Backend:
            def __init__(self,*args):pass
            def _command(self,argv,timeout=3):
                nonlocal stopped
                calls.append(argv)
                if argv[0]=='/usr/bin/systemctl':stopped=True;return b''
                if argv[0]=='/usr/bin/systemd-run':return b''
                name=argv[-1]
                values={'InvocationID':('ay',[1]*16),'ControlGroup':('s',group),'MainPID':('u',0 if stopped else 123),'Type':('s','simple'),'ActiveState':('s','inactive' if stopped else 'active')}
                return prop(*values[name])
        observer=unittest.mock.Mock()
        observer.boot.return_value=BOOT;observer.census.return_value=[];observer.memory.return_value=m.FLOOR
        observer.process.return_value=process
        observer.cgroup.side_effect=lambda *args:None if stopped else {'device':1,'inode':2,'members':[process]}
        args=types.SimpleNamespace(policy='policy',policy_sha='1'*64,evidence=str(self.folder),adapter='adapter')
        with patch.object(m.sys,'platform','linux'),patch.object(m.os,'geteuid',return_value=0,create=True),patch.object(m.os,'pidfd_open',return_value=77,create=True),patch.object(m.os,'close') as close,patch.object(m,'read',return_value=b'sealed'),patch.object(m,'qualification_policy',return_value=self.policy),patch.object(m,'ProcObserver',return_value=observer),patch.object(m,'load_adapter'),patch.object(m,'backend_class',return_value=Backend):
            with self.assertRaisesRegex(ValueError,'containment differs'):m.qualify_no_sdk(args)
        self.assertIn(['/usr/bin/systemctl','stop','--no-block',unit],calls)
        self.assertTrue((self.folder/'qualification-generation.json').is_file())
        self.assertTrue((self.folder/'qualification-cleanup.json').is_file())
        self.assertFalse((self.folder/'qualification-uncertain.json').exists())
        close.assert_called_once_with(77)


if __name__=='__main__':unittest.main()
