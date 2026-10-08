"""Real Linux receipt integration; manager/native observations remain synthetic."""
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import employee_linux_custody as custody

def OwnedReceipts(fd):return custody.receipts_class()(fd)


class RefusalControls(unittest.TestCase):
    def test_changed_receipt_source_refused_before_compile_or_cache(self):
        with patch.object(custody,'_receipts_module',None),patch.object(custody,'read',return_value=b'raise AssertionError("unreviewed execution")'):
            with self.assertRaisesRegex(ValueError,'descriptor receipt source differs'):
                custody.receipts_class()
            self.assertIsNone(custody._receipts_module)

    def test_isolated_payload_module_starts_without_sibling_search_path(self):
        import subprocess
        result=subprocess.run([sys.executable,'-I',str(Path(custody.__file__).resolve()),'--help'],capture_output=True,timeout=5,check=False)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn(b'probe-payload',result.stdout)

    def test_path_only_qualifier_refused_before_source_or_manager_access(self):
        args=types.SimpleNamespace(evidence='/private/evidence')
        gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None)
        with patch.object(custody.sys,'platform','linux'),patch.object(custody,'os',gate),patch.object(custody,'read') as read,patch.object(custody,'backend_class') as backend:
            with self.assertRaisesRegex(ValueError,'retained evidence descriptor'):
                custody.qualify_no_sdk(args)
        read.assert_not_called();backend.assert_not_called()

    def test_boolean_descriptor_is_not_admission(self):
        gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None)
        with patch.object(custody.sys,'platform','linux'),patch.object(custody,'os',gate),patch.object(custody,'receipts_class') as store:
            with self.assertRaisesRegex(ValueError,'retained evidence descriptor'):
                custody.qualify_no_sdk(types.SimpleNamespace(evidence_fd=True))
        store.assert_not_called()

    def test_cli_path_only_never_enters_backend(self):
        gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None)
        with patch.object(custody.sys,'platform','linux'),patch.object(custody,'os',gate),patch.object(custody,'backend_class') as backend:
            with self.assertRaisesRegex(ValueError,'retained evidence descriptor'):
                custody.main(['--mode','qualify-no-sdk','--evidence','/private/evidence'])
        backend.assert_not_called()


@unittest.skipUnless(sys.platform=='linux','real Linux descriptor controls')
class FilesystemControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.parent=Path(self.temp.name)
        self.directory=self.parent/'owned';self.directory.mkdir(mode=0o700)
        self.fd=os.open(self.directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        self.addCleanup(os.close,self.fd)

    def backend(self,store,watcher=False):
        class Base:
            def __init__(self,evidence,*_):self.evidence=Path(evidence)
            def _write(self,*_):raise AssertionError('path writer must not run')
        with patch.object(custody,'load_adapter',return_value=types.SimpleNamespace(RealLinuxBackend=Base)):
            backend=custody.backend_class(b'sealed')
        return backend(self.directory,'0'*64,watcher_side=watcher,receipt_store=store)

    def test_helper_receipt_survives_parent_replacement(self):
        with OwnedReceipts(self.fd) as store:
            backend=self.backend(store)
            moved=self.parent/'retained';self.directory.rename(moved)
            self.directory.mkdir(mode=0o700)
            backend._write('helper-1.json',{'cleanupVerified':True})
            self.assertEqual({'cleanupVerified':True},custody.parse(store.read('helper-controller-1.json')))
            self.assertFalse((self.directory/'helper-controller-1.json').exists())
            self.assertTrue((moved/'helper-controller-1.json').is_file())

    def test_actor_receipts_remain_exclusive_and_separate(self):
        with OwnedReceipts(self.fd) as store:
            self.backend(store)._write('helper-1.json',{'actor':'controller'})
            watcher=self.backend(store,True)
            watcher._write('helper-1.json',{'actor':'watcher'})
            self.assertEqual('watcher',custody.parse(store.read('helper-watcher-1.json'))['actor'])
            with self.assertRaises(FileExistsError):watcher._write('helper-1.json',{})

    def test_helper_refuses_link_without_touching_foreign_target(self):
        foreign=self.parent/'foreign';foreign.write_bytes(b'unchanged')
        (self.directory/'helper-controller-1.json').symlink_to(foreign)
        with OwnedReceipts(self.fd) as store:
            with self.assertRaises(FileExistsError):self.backend(store)._write('helper-1.json',{})
        self.assertEqual(b'unchanged',foreign.read_bytes())

    def test_qualifier_closes_duplicate_on_failure_preserves_caller(self):
        held=[]
        def observed(args,store):
            held.append(store);store.write_json('qualification-intent.json',{'runtimeQualified':False})
            raise ValueError('synthetic manager refusal')
        gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None)
        with patch.object(custody,'os',gate),patch.object(custody,'_qualify_no_sdk',side_effect=observed):
            with self.assertRaisesRegex(ValueError,'synthetic manager refusal'):
                custody.qualify_no_sdk(types.SimpleNamespace(evidence_fd=self.fd))
        os.fstat(self.fd)
        with self.assertRaises(ValueError):held[0].read('qualification-intent.json')

    def test_qualifier_closes_duplicate_on_success_preserves_caller(self):
        held=[]
        def observed(args,store):held.append(store);return store.write_json('qualification-result.json',{'runtimeQualified':False})
        gate=types.SimpleNamespace(geteuid=lambda:0,pidfd_open=lambda *_:None)
        with patch.object(custody,'os',gate),patch.object(custody,'_qualify_no_sdk',side_effect=observed):
            actual=custody.qualify_no_sdk(types.SimpleNamespace(evidence_fd=self.fd))
        os.fstat(self.fd)
        self.assertEqual(custody.sha((self.directory/'qualification-result.json').read_bytes()),actual)
        with self.assertRaises(ValueError):held[0].read('qualification-result.json')

    def test_changed_private_mode_refuses_helper_receipt(self):
        with OwnedReceipts(self.fd) as store:
            backend=self.backend(store);self.directory.chmod(0o755)
            with self.assertRaises(ValueError):backend._write('helper-1.json',{})
            self.directory.chmod(0o700)
        self.assertFalse((self.directory/'helper-controller-1.json').exists())


if __name__=='__main__':unittest.main()
