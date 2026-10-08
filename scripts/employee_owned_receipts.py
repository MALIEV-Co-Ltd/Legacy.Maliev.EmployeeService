"""Descriptor-only receipt custody. No authority, SDK, provider or process API.

The caller supplies a directory descriptor already acquired by its qualified
owner. Retaining that descriptor prevents later pathname substitution from
redirecting writes. This primitive does not qualify the caller or its backend.
"""
import hashlib
import json
import os
import re
import stat
import sys

MAX_BYTES = 65536
MAX_NODES = 4096
MAX_DEPTH = 16
NAME = re.compile(r'[a-z0-9-]{1,120}\.json\Z')


class ReceiptRefused(ValueError):
    pass


def need(ok, message):
    if not ok:
        raise ReceiptRefused(message)


def receipt_bytes(value):
    """Freeze a bounded JSON tree before opening any output descriptor."""
    nodes = 0
    cost = 0

    def charge(amount):
        nonlocal cost
        cost += amount
        need(cost <= MAX_BYTES, 'receipt byte bound exceeded')

    def copy(item, depth):
        nonlocal nodes
        nodes += 1
        need(nodes <= MAX_NODES and depth <= MAX_DEPTH, 'receipt structural bound exceeded')
        if type(item) in (dict, list):
            need(len(item) <= MAX_NODES, 'receipt collection bound exceeded')
            charge(2 + max(0, len(item) - 1))
            if type(item) is list:
                return [copy(child, depth + 1) for child in item]
            out = {}
            for key, child in item.items():
                need(type(key) is str and len(key) <= 1024, 'bounded string receipt keys required')
                charge(len(json.dumps(key, ensure_ascii=True)) + 1)
                out[key] = copy(child, depth + 1)
            return out
        need(type(item) in (str, int, float, bool, type(None)), 'JSON receipt values required')
        if type(item) is str:
            need(len(item) <= MAX_BYTES, 'receipt string bound exceeded')
        if type(item) is int:
            need(item.bit_length() <= 16384, 'receipt integer bound exceeded')
        charge(len(json.dumps(item, ensure_ascii=True, allow_nan=False)))
        return item

    frozen = copy(value, 0)
    raw = json.dumps(frozen, sort_keys=True, separators=(',', ':'),
                     ensure_ascii=True, allow_nan=False).encode('ascii')
    need(len(raw) <= MAX_BYTES, 'receipt byte bound exceeded')
    return raw


def generation(info):
    return info.st_dev, info.st_ino, info.st_uid, info.st_gid


def file_state(info):
    return (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
            info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


class OwnedReceipts:
    def __init__(self, directory_fd):
        self._fd = None
        need(sys.platform == 'linux', 'Linux descriptor custody required')
        need(type(directory_fd) is int and directory_fd >= 0, 'actual directory descriptor required')
        need(all(hasattr(os, name) for name in
                 ('O_DIRECTORY', 'O_NOFOLLOW', 'O_CLOEXEC', 'O_NONBLOCK')), 'Linux no-follow descriptor flags required')
        fd = os.dup(directory_fd)
        try:
            os.set_inheritable(fd, False)
            info = os.fstat(fd)
            need(stat.S_ISDIR(info.st_mode) and info.st_uid == os.geteuid()
                 and stat.S_IMODE(info.st_mode) == 0o700, 'private exact-owner directory required')
            need(not os.get_inheritable(fd), 'receipt descriptor inheritance refused')
            self.identity = generation(info)
            self._fd = fd
        finally:
            if self._fd is None:
                os.close(fd)

    def _check(self):
        need(self._fd is not None, 'receipt store is closed')
        info = os.fstat(self._fd)
        need(generation(info) == self.identity and stat.S_ISDIR(info.st_mode)
             and info.st_uid == os.geteuid() and stat.S_IMODE(info.st_mode) == 0o700,
             'retained directory ownership or generation changed')
        need(not os.get_inheritable(self._fd), 'receipt descriptor inheritance changed')

    @staticmethod
    def _name(name):
        need(type(name) is str and NAME.fullmatch(name), 'fixed receipt filename required')

    def _file(self, fd):
        info = os.fstat(fd)
        need(stat.S_ISREG(info.st_mode) and info.st_uid == self.identity[2]
             and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600,
             'private single-link regular receipt required')
        return info

    def write_json(self, name, value):
        self._name(name)
        raw = receipt_bytes(value)
        self._check()
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                     os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=self._fd)
        # Failed writes retain the exact partial receipt for recovery. No broad
        # pathname cleanup, retry, success acknowledgment or deletion occurs.
        try:
            before = self._file(fd)
            view = memoryview(raw)
            while view:
                written = os.write(fd, view)
                need(type(written) is int and 0 < written <= len(view), 'short receipt write')
                view = view[written:]
            os.fsync(fd)
            after = self._file(fd)
            need(generation(before) == generation(after) and after.st_size == len(raw),
                 'receipt ownership, generation or length changed during write')
        finally:
            os.close(fd)
        self._check()
        os.fsync(self._fd)
        return hashlib.sha256(raw).hexdigest()

    def read(self, name, max_bytes=MAX_BYTES):
        self._name(name)
        need(type(max_bytes) is int and 0 < max_bytes <= MAX_BYTES, 'finite receipt read bound required')
        self._check()
        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC |
                     os.O_NONBLOCK, dir_fd=self._fd)
        try:
            before = self._file(fd)
            need(0 <= before.st_size <= max_bytes, 'receipt byte bound exceeded')
            chunks = []
            size = 0
            while size <= max_bytes:
                chunk = os.read(fd, max_bytes + 1 - size)
                need(type(chunk) is bytes, 'actual descriptor bytes required')
                if not chunk:
                    break
                chunks.append(chunk)
                size += len(chunk)
            need(size <= max_bytes and size == before.st_size, 'receipt read changed or exceeded bound')
            after = self._file(fd)
            need(file_state(before) == file_state(after), 'receipt changed during read')
            self._check()
            return b''.join(chunks)
        finally:
            os.close(fd)

    def close(self):
        fd, self._fd = self._fd, None
        if fd is not None:
            os.close(fd)

    def __enter__(self):
        self._check()
        return self

    def __exit__(self, exc_type, exc, traceback):
        self.close()
