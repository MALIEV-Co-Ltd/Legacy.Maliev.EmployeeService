"""Receipt syscall controls and actual Linux files; no service/provider launch."""
import hashlib
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

import employee_owned_receipts as m


class FakeOs(types.SimpleNamespace):
    """Deterministic descriptor-only model, without filesystem syscalls."""
    def __init__(self):
        super().__init__(name='posix', O_RDONLY=0, O_WRONLY=1, O_CREAT=64,
                         O_EXCL=128, O_NOFOLLOW=256, O_NONBLOCK=512,
                         O_CLOEXEC=1024, O_DIRECTORY=2048)
        self.closed = []
        self.syncs = []
        self.opens = []
        self.payload = bytearray()
        self.offset = 0
        self.directory = types.SimpleNamespace(st_mode=stat.S_IFDIR | 0o700,
            st_uid=1000, st_gid=1000, st_dev=7, st_ino=9, st_nlink=1,
            st_size=0, st_mtime_ns=1, st_ctime_ns=1)
        self.file = types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600,
            st_uid=1000, st_gid=1000, st_dev=7, st_ino=10, st_nlink=1,
            st_size=0, st_mtime_ns=1, st_ctime_ns=1)
        self.close_failure = False
        self.duplicates = []

    def geteuid(self): return 1000
    def dup(self, fd):
        if fd != 4: raise OSError('unexpected caller descriptor')
        self.duplicates.append(fd)
        return 5
    def get_inheritable(self, fd): return False
    def set_inheritable(self, fd, value):
        if fd != 5 or value: raise OSError('unexpected inheritable descriptor')
    def fstat(self, fd):
        if fd in (4, 5): return types.SimpleNamespace(**vars(self.directory))
        if fd == 6:
            self.file.st_size = len(self.payload)
            return types.SimpleNamespace(**vars(self.file))
        raise OSError('unknown descriptor')
    def open(self, name, flags, mode=0o777, *, dir_fd=None):
        if dir_fd != 5: raise OSError('unanchored open')
        self.opens.append((name, flags, mode, dir_fd))
        self.offset = 0
        return 6
    def write(self, fd, raw):
        if fd != 6: raise OSError('foreign write')
        self.payload.extend(raw)
        return len(raw)
    def read(self, fd, count):
        if fd != 6: raise OSError('foreign read')
        raw = bytes(self.payload[self.offset:self.offset + count])
        self.offset += len(raw)
        return raw
    def fsync(self, fd): self.syncs.append(fd)
    def close(self, fd):
        self.closed.append(fd)
        if self.close_failure: raise OSError('injected close failure')


class PortableSyscalls(unittest.TestCase):
    def setUp(self):
        self.enterContext(patch.object(m.sys, 'platform', 'linux'))

    def test_nonlinux_refuses_before_duplication(self):
        fake = FakeOs()
        with patch.object(m.sys, 'platform', 'win32'), patch.object(m, 'os', fake):
            with self.assertRaises(ValueError):
                m.OwnedReceipts(4)
            self.assertEqual(fake.duplicates, [])

    def test_descriptor_write_is_relative_exclusive_and_durable(self):
        fake = FakeOs()
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            try:
                digest = store.write_json('proof.json', {'b': 2, 'a': 1})
                self.assertEqual(digest, hashlib.sha256(fake.payload).hexdigest())
                self.assertEqual(fake.syncs, [6, 5])
                name, flags, mode, parent = fake.opens[0]
                self.assertEqual((name, mode, parent), ('proof.json', 0o600, 5))
                self.assertTrue(flags & fake.O_EXCL)
                self.assertTrue(flags & fake.O_NOFOLLOW)
            finally:
                store.close()
            self.assertNotIn(4, fake.closed)

    def test_descriptor_identity_drift_blocks_open(self):
        fake = FakeOs()
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            try:
                fake.directory.st_ino += 1
                with self.assertRaises((OSError, ValueError)):
                    store.write_json('proof.json', {'a': 1})
                self.assertEqual(fake.opens, [])
            finally:
                store.close()

    def test_foreign_owner_and_public_directory_refuse(self):
        for attribute, value in (('st_uid', 1001), ('st_mode', stat.S_IFDIR | 0o755)):
            fake = FakeOs()
            setattr(fake.directory, attribute, value)
            with self.subTest(attribute=attribute), patch.object(m, 'os', fake):
                with self.assertRaises((OSError, ValueError)):
                    m.OwnedReceipts(4)
                self.assertEqual(fake.opens, [])
                self.assertNotIn(4, fake.closed)

    def test_close_failure_never_closes_caller_descriptor(self):
        fake = FakeOs()
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            fake.close_failure = True
            with self.assertRaises(OSError):
                store.close()
            self.assertNotIn(4, fake.closed)

    def test_read_metadata_change_refuses_and_closes_file(self):
        fake = FakeOs()
        fake.payload.extend(b'{"a":1}')
        original_read = fake.read
        def changed_read(fd, count):
            raw = original_read(fd, count)
            fake.file.st_mtime_ns += 1
            return raw
        fake.read = changed_read
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            try:
                with self.assertRaises((OSError, ValueError)):
                    store.read('proof.json')
                self.assertIn(6, fake.closed)
            finally:
                store.close()

    def test_file_fsync_failure_closes_file_without_claiming_success(self):
        fake = FakeOs()
        def fail_sync(fd):
            fake.syncs.append(fd)
            raise OSError('injected durability failure')
        fake.fsync = fail_sync
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            try:
                with self.assertRaises(OSError):
                    store.write_json('proof.json', {'a': 1})
                self.assertIn(6, fake.closed)
            finally:
                store.close()

    def test_postwrite_link_change_refuses_without_directory_acknowledgment(self):
        fake = FakeOs()
        def changed_sync(fd):
            fake.syncs.append(fd)
            if fd == 6:
                fake.file.st_nlink = 2
        fake.fsync = changed_sync
        with patch.object(m, 'os', fake):
            store = m.OwnedReceipts(4)
            try:
                with self.assertRaises((OSError, ValueError)):
                    store.write_json('proof.json', {'a': 1})
                self.assertEqual(fake.syncs, [6])
                self.assertIn(6, fake.closed)
            finally:
                store.close()


class LinuxFilesystem(unittest.TestCase):
    @unittest.skipUnless(sys.platform == 'linux' and hasattr(os, 'O_NOFOLLOW'), 'actual Linux descriptor semantics required')
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.directory = self.root / 'owned'
        self.directory.mkdir(mode=0o700)
        self.fd = os.open(self.directory, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, self.fd)
        self.store = m.OwnedReceipts(self.fd)
        self.addCleanup(self.store.close)

    def test_canonical_roundtrip_and_duplicate_refusal(self):
        digest = self.store.write_json('receipt.json', {'z': 2, 'a': 1})
        raw = self.store.read('receipt.json')
        self.assertEqual(json.loads(raw), {'a': 1, 'z': 2})
        self.assertEqual(digest, hashlib.sha256(raw).hexdigest())
        self.assertEqual(stat.S_IMODE((self.directory / 'receipt.json').stat().st_mode), 0o600)
        with self.assertRaises((OSError, ValueError)):
            self.store.write_json('receipt.json', {'a': 2})
        self.assertEqual(self.store.read('receipt.json'), raw)

    def test_directory_rename_and_foreign_replacement(self):
        moved = self.root / 'retained'
        self.directory.rename(moved)
        self.directory.mkdir(mode=0o700)
        self.store.write_json('receipt.json', {'retained': True})
        self.assertTrue((moved / 'receipt.json').exists())
        self.assertEqual(list(self.directory.iterdir()), [])

    def test_parent_rename_and_symlink_replacement(self):
        parent = self.root / 'parent'
        parent.mkdir(mode=0o700)
        child = parent / 'child'
        child.mkdir(mode=0o700)
        fd = os.open(child, os.O_RDONLY | os.O_DIRECTORY)
        try:
            with m.OwnedReceipts(fd) as store:
                moved = self.root / 'moved-parent'
                parent.rename(moved)
                foreign = self.root / 'foreign'
                foreign.mkdir(mode=0o700)
                parent.symlink_to(foreign, target_is_directory=True)
                store.write_json('proof.json', {'original': True})
                self.assertTrue((moved / 'child' / 'proof.json').exists())
                self.assertEqual(list(foreign.iterdir()), [])
        finally:
            os.close(fd)

    def test_symlink_receipt_refuses(self):
        outside = self.root / 'outside'
        outside.write_bytes(b'private')
        outside.chmod(0o600)
        (self.directory / 'receipt.json').symlink_to(outside)
        with self.assertRaises((OSError, ValueError)):
            self.store.read('receipt.json')
        self.assertEqual(outside.read_bytes(), b'private')

    def test_hardlink_receipt_refuses(self):
        self.store.write_json('receipt.json', {'a': 1})
        os.link(self.directory / 'receipt.json', self.root / 'second-link')
        with self.assertRaises((OSError, ValueError)):
            self.store.read('receipt.json')

    def test_fifo_refuses_without_blocking(self):
        os.mkfifo(self.directory / 'receipt.json', mode=0o600)
        with self.assertRaises((OSError, ValueError)):
            self.store.read('receipt.json')

    def test_read_bound_and_private_mode(self):
        self.store.write_json('receipt.json', {'long': 'a' * 100})
        with self.assertRaises((OSError, ValueError)):
            self.store.read('receipt.json', max_bytes=10)
        (self.directory / 'receipt.json').chmod(0o644)
        with self.assertRaises((OSError, ValueError)):
            self.store.read('receipt.json')

    def test_invalid_names_and_nonfinite_json(self):
        for name in ('../escape.json', '/escape.json', 'a/b.json'):
            with self.subTest(name=name), self.assertRaises((OSError, ValueError)):
                self.store.write_json(name, {'a': 1})
        with self.assertRaises((OSError, ValueError)):
            self.store.write_json('nan.json', {'a': float('nan')})
        self.assertFalse((self.directory / 'nan.json').exists())

    def test_oversized_write_has_no_receipt(self):
        with self.assertRaises((OSError, ValueError)):
            self.store.write_json('large.json', {'data': 'x' * 65536})
        self.assertFalse((self.directory / 'large.json').exists())

    def test_close_preserves_caller_descriptor(self):
        duplicate = self.store._fd
        self.store.close()
        os.fstat(self.fd)
        with self.assertRaises(OSError):
            os.fstat(duplicate)
        self.store.close()


if __name__ == '__main__':
    unittest.main()
