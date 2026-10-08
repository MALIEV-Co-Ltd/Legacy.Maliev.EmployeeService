"""Caller/schema controls only; no real manager, SDK or policy signer."""
import datetime as dt
import hashlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import employee_no_sdk_controller as m


class Controls(unittest.TestCase):
    def setUp(self):
        self.scripts=Path(__file__).resolve().parent
        self.custody=m.load_no_sdk_custody(self.scripts)
        self.now=dt.datetime(2026,10,9,tzinfo=dt.timezone.utc)
        self.policy={'schemaVersion':1,'purpose':'employee-disposable-no-sdk','sourceHead':'1'*40,'runId':'123','attempt':1,'bootId':'11111111-2222-3333-4444-555555555555','sourceSha256':m.CUSTODY_SOURCE_SHA256,'startsUtc':'2026-10-08T23:59:00Z','expiresUtc':'2026-10-09T00:20:00Z','phaseSeconds':3,'approvedNonNativeExecutableSha256':['3'*64]}
        self.env={'GITHUB_ACTIONS':'true','GITHUB_REPOSITORY':'MALIEV-Co-Ltd/Legacy.Maliev.EmployeeService','GITHUB_SHA':'1'*40,'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1'}
        self.gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None,environ=self.env)

    def call(self,policy=None,fd=9,pin=None):
        raw=self.custody.encode(self.policy if policy is None else policy)
        pin=hashlib.sha256(raw).hexdigest() if pin is None else pin
        observer=types.SimpleNamespace(boot=lambda:self.policy['bootId'])
        with patch.object(m.sys,'platform','linux'),patch.object(m,'os',self.gate),patch.object(m,'read_regular',return_value=raw),patch.object(m,'load_no_sdk_custody',return_value=self.custody),patch.object(self.custody,'read',return_value=raw),patch.object(self.custody,'now',return_value=self.now),patch.object(self.custody,'ProcObserver',return_value=observer):
            return m.qualify_no_sdk(self.scripts,self.scripts/'policy.json',pin,self.scripts/'evidence',fd)

    def test_exact_allocation_delegates_original_descriptor(self):
        with patch.object(self.custody,'qualify_no_sdk',return_value='observed') as delegate:
            self.assertEqual('observed',self.call())
        args=delegate.call_args.args[0]
        self.assertEqual(9,args.evidence_fd)
        self.assertEqual(str(self.scripts/'evidence'),args.evidence)
        self.assertEqual(str(self.scripts/'employee_linux_sdk_owner.py'),args.adapter)

    def test_manager_cleanup_failure_propagates(self):
        with patch.object(self.custody,'qualify_no_sdk',side_effect=ValueError('cleanup unverified')):
            with self.assertRaisesRegex(ValueError,'cleanup unverified'):self.call()

    def test_missing_descriptor_refused_before_loading_source(self):
        with patch.object(m.sys,'platform','linux'),patch.object(m,'os',self.gate),patch.object(m,'load_no_sdk_custody') as loader:
            with self.assertRaisesRegex(ValueError,'descriptor'):m.qualify_no_sdk(self.scripts,Path('/policy'),'0'*64,Path('/evidence'),None)
        loader.assert_not_called()

    def test_boolean_descriptor_refused(self):
        with self.assertRaisesRegex(ValueError,'descriptor'):self.call(fd=True)

    def test_non_linux_refused_before_policy_read(self):
        with patch.object(m.sys,'platform','win32'),patch.object(m,'load_no_sdk_custody') as loader:
            with self.assertRaisesRegex(ValueError,'Linux'):m.qualify_no_sdk(self.scripts,Path('/policy'),'0'*64,Path('/evidence'),9)
        loader.assert_not_called()

    def test_non_root_refused(self):
        self.gate.geteuid=lambda:1001
        with self.assertRaisesRegex(ValueError,'root'):self.call()

    def test_relative_evidence_label_refused(self):
        with patch.object(m.sys,'platform','linux'),patch.object(m,'os',self.gate):
            with self.assertRaisesRegex(ValueError,'Absolute evidence'):m.qualify_no_sdk(self.scripts,self.scripts/'policy.json','0'*64,Path('relative'),9)

    @unittest.skipUnless(sys.platform=='linux','real Linux descriptor handoff')
    def test_actual_descriptor_delegates_and_duplicate_closes(self):
        self.custody.receipts_class()  # Actual sealed dependency, before policy stub.
        held=[]
        def manager_model(args,store):
            held.append(store);return store.write_json('qualification-result.json',{'runtimeQualified':False})
        with tempfile.TemporaryDirectory() as directory:
            fd=os.open(directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
            try:
                with patch.object(self.custody,'os',self.gate),patch.object(self.custody,'_qualify_no_sdk',side_effect=manager_model):
                    self.assertEqual(64,len(self.call(fd=fd)))
                os.fstat(fd)
                with self.assertRaises(ValueError):held[0].read('qualification-result.json')
                self.assertTrue((Path(directory)/'qualification-result.json').is_file())
            finally:os.close(fd)

    def test_wrong_policy_bytes_refused(self):
        with patch.object(self.custody,'qualify_no_sdk') as delegate:
            with self.assertRaisesRegex(ValueError,'policy bytes'):self.call(pin='0'*64)
        delegate.assert_not_called()

    def test_missing_independent_pin_refused(self):
        with self.assertRaisesRegex(ValueError,'policy pin'):self.call(pin='')

    def test_wrong_allocation_fields_refused(self):
        for field,value in [('GITHUB_ACTIONS','false'),('GITHUB_REPOSITORY','foreign/repo'),('GITHUB_SHA','2'*40),('GITHUB_RUN_ID','124'),('GITHUB_RUN_ATTEMPT','2'),('GITHUB_RUN_ATTEMPT','01')]:
            with self.subTest(field=field,value=value),patch.dict(self.env,{field:value}),patch.object(self.custody,'qualify_no_sdk') as delegate:
                with self.assertRaisesRegex(ValueError,'live hosted allocation'):self.call()
                delegate.assert_not_called()

    def test_wrong_boot_refused(self):
        policy=self.policy|{'bootId':'22222222-2222-3333-4444-555555555555'}
        with self.assertRaisesRegex(ValueError,'boot'):self.call(policy)

    def test_wrong_source_binding_refused(self):
        with self.assertRaisesRegex(ValueError,'source'):self.call(self.policy|{'sourceSha256':'2'*64})

    def test_expired_policy_refused(self):
        with self.assertRaises(ValueError):self.call(self.policy|{'expiresUtc':'2026-10-08T23:59:59Z'})

    def test_sdk_policy_substitution_refused(self):
        with self.assertRaisesRegex(ValueError,'SDK authority'):self.call(self.policy|{'purpose':'employee-sdk'})

    def test_actual_sealed_custody_loader(self):
        self.assertEqual(m.CUSTODY_SOURCE_SHA256,hashlib.sha256((self.scripts/'employee_linux_custody.py').read_bytes()).hexdigest())
        self.assertTrue(callable(m.load_no_sdk_custody(self.scripts).qualify_no_sdk))

    def test_tampered_source_never_compiles(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory)/'employee_linux_custody.py').write_bytes(b'raise AssertionError("unreviewed execution")')
            with self.assertRaisesRegex(ValueError,'source bytes differ'):m.load_no_sdk_custody(Path(directory))

    def test_isolated_cli_starts(self):
        result=subprocess.run([sys.executable,'-I',str(self.scripts/'employee_no_sdk_controller.py'),'--help'],capture_output=True,timeout=5,check=False)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn(b'--evidence-fd',result.stdout)


if __name__=='__main__':unittest.main()
