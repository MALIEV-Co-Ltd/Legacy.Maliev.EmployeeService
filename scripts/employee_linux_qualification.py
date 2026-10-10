"""Finite Linux read-only capability probe source. No SDK/provider grant.

Only fixed busctl GET properties may launch helpers. Optional disposable-unit
qualification is deliberately refused until an independent policy is integrated.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import sys
import types

READONLY_HELPER_SHA = '10696261b8ea4291593993cfd9070ceb3b9843b494f08067a2013330b148b1af'
MAX_RAW = 65536
BOOT = re.compile(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z')
TRUSTED_QUALIFICATION_POLICY_SHA256 = None
COMMANDS = {
    name: ['/usr/bin/busctl', '--system', '--json=short', '--timeout=2s', 'get-property',
           'org.freedesktop.systemd1', '/org/freedesktop/systemd1',
           'org.freedesktop.systemd1.Manager', name]
    for name in ('Version', 'SystemState')
}


def need(value, message):
    if not value: raise ValueError(message)


def bounded_read(path, maximum=MAX_RAW):
    path = Path(path)
    for part in (path, *path.parents): need(not part.is_symlink(), 'linked probe path refused')
    need(path.is_file(), 'regular readable probe source required')
    with path.open('rb') as reader: raw = reader.read(maximum+1)
    need(len(raw) <= maximum, 'probe read bound exceeded')
    return raw


def parse(raw):
    need(type(raw) is bytes and 0 < len(raw) <= MAX_RAW, 'bounded raw JSON required')
    def pairs(items):
        row = {}
        for key, value in items:
            need(key not in row, 'duplicate JSON key')
            row[key] = value
        return row
    return json.loads(raw.decode('utf-8', errors='strict'), object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('nonfinite JSON')))


def manager_property(raw, name):
    need(name in COMMANDS, 'unknown manager property')
    item = parse(raw)
    need(type(item) is dict and set(item) == {'type', 'data'} and item['type'] == 's', 'unknown manager JSON layout')
    # get-property serializes its string variant as a scalar. Method-message
    # fixtures use a singleton array. Both retain the closed D-Bus type 's'.
    data = item['data']
    if type(data) is str:
        value = data
    else:
        need(type(data) is list and len(data) == 1 and type(data[0]) is str,
             'typed scalar or singleton manager string required')
        value = data[0]
    need(0 < len(value) <= 128 and re.fullmatch(r'[\x20-\x7e]+', value), 'bounded manager string required')
    if name == 'SystemState':
        need(value in {'initializing', 'starting', 'running', 'degraded', 'maintenance', 'stopping', 'offline'},
             'unknown systemd state refused')
    return {'value': value, 'rawSha256': hashlib.sha256(raw).hexdigest()}


def proc_birth(raw, expected_pid):
    need(type(expected_pid) is int and 0 < expected_pid < 2**31 and type(raw) is bytes and len(raw) <= 8192,
         'bounded own PID stat required')
    text = raw.decode('ascii', errors='strict')
    prefix, rest = text.rsplit(')', 1)
    need(prefix.split(' ', 1)[0] == str(expected_pid) and ' (' in prefix, 'own PID stat differs')
    fields = rest.split()
    need(len(fields) >= 20 and fields[19].isdecimal() and int(fields[19]) > 0, 'actual process birth required')
    return fields[19]


def mem_free(raw):
    need(type(raw) is bytes and len(raw) <= MAX_RAW, 'bounded memory observation required')
    rows = raw.decode('ascii', errors='strict').splitlines()
    values = [re.fullmatch(r'MemFree:\s+([0-9]+) kB', row) for row in rows if row.startswith('MemFree:')]
    need(len(values) == 1 and values[0] is not None, 'exact physical MemFree required')
    return int(values[0].group(1))


def unified_cgroup(raw):
    need(type(raw) is bytes and len(raw) <= 8192, 'bounded own cgroup required')
    rows = raw.decode('ascii', errors='strict').splitlines()
    need(len(rows) == 1 and rows[0].startswith('0::/'), 'unknown/non-unified cgroup layout')
    path = rows[0][3:]
    need(len(path) <= 512 and (path == '/' or all(re.fullmatch(r'[A-Za-z0-9_.:-]+', part)
         and part not in {'.', '..'} for part in path[1:].split('/'))), 'canonical cgroup path required')
    return path


def sealed_helper(path):
    raw = bounded_read(path, 65536)
    need(hashlib.sha256(raw).hexdigest() == READONLY_HELPER_SHA, 'owned-helper source seal changed')
    module = types.ModuleType('sealed_employee_linux_readonly_helper')
    module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


class ReadOnlyHelpers:
    def __init__(self, evidence, helper_path):
        module = sealed_helper(helper_path)
        self.evidence = Path(evidence)
        # Standalone helper exposes no SDK/backend/start API.
        self.backend = module.FiniteReadOnlyHelpers(evidence)
    def get(self, name):
        need(name in COMMANDS, 'unknown helper command refused')
        result = self.backend._command(list(COMMANDS[name]), timeout=3)
        need(len(result) <= MAX_RAW, 'manager raw response bound')
        # These two fixed public manager properties contain no application data.
        # Keep exact response bytes even when their parser refuses the layout.
        with (self.evidence/('manager-'+name+'.json')).open('xb') as writer:
            writer.write(result)
            writer.flush()
            os.fsync(writer.fileno())
        return result
    def release_evidence(self):
        rows = self.backend.command_receipts
        need(len(rows) <= 2 and all(row['cleanupVerified'] is True and row['remainingMembers'] == []
             and type(row['exitCode']) is int and type(row['pid']) is int and 0 < row['pid'] < 2**31
             and type(row['birthTicks']) is str and row['birthTicks'].isdecimal()
             and row['executable'] == '/usr/bin/busctl' and row['memoryBytes'] == 256*1024**2
             and row['cpuSeconds'] == 10 and row['timeoutSeconds'] == 3
             for row in rows), 'owned manager helpers not terminal or bounded')
        return rows


class LinuxReader:
    def __init__(self):
        need(sys.platform == 'linux' and hasattr(os, 'pidfd_open'), 'Linux pidfd capability absent')
    def local(self):
        pid = os.getpid()
        before = proc_birth(bounded_read('/proc/'+str(pid)+'/stat', 8192), pid)
        boot = bounded_read('/proc/sys/kernel/random/boot_id', 64).decode('ascii').strip()
        need(BOOT.fullmatch(boot), 'actual canonical Linux boot required')
        group = unified_cgroup(bounded_read('/proc/'+str(pid)+'/cgroup', 8192))
        root = Path('/sys/fs/cgroup')
        target = root/group.lstrip('/')
        need(bounded_read(root/'cgroup.controllers', 8192).strip(), 'cgroup v2 controllers required')
        for node in (target, *target.parents): need(not node.is_symlink(), 'linked cgroup refused')
        first = target.stat()
        need(target.is_dir(), 'actual own cgroup required')
        members = bounded_read(target/'cgroup.procs', 8192).decode('ascii').split()
        need(len(members) <= 4096 and all(value.isdecimal() for value in members) and str(pid) in members,
             'own process not observed in cgroup')
        fd = os.pidfd_open(pid)
        try:
            after = proc_birth(bounded_read('/proc/'+str(pid)+'/stat', 8192), pid)
            second = target.stat()
            need(before == after and (first.st_dev, first.st_ino) == (second.st_dev, second.st_ino),
                 'own kernel generation changed')
        finally: os.close(fd)
        return {'bootId': boot, 'pid': pid, 'birthTicks': before, 'cgroup': group,
                'cgroupDevice': first.st_dev, 'cgroupInode': first.st_ino,
                'memFreeKiB': mem_free(bounded_read('/proc/meminfo')),
                'cgroupVersion': 2, 'pidfdObserved': True}


class Probe:
    def __init__(self, reader, helpers, *, synthetic=False):
        need(synthetic is True or (type(reader) is LinuxReader and type(helpers) is ReadOnlyHelpers),
             'actual observation requires concrete read-only collectors')
        self.reader = reader
        self.helpers = helpers
        self.synthetic = synthetic
    def capture(self):
        # Fixed two read-only operations. No mutation routes or daemon socket.
        local = self.reader.local()
        properties = {}
        try:
            for name in COMMANDS: properties[name] = manager_property(self.helpers.get(name), name)
        finally:
            release = self.helpers.release_evidence()
        need(self.synthetic or len(release) == 2, 'two actual fixed helper receipts required')
        return {'schemaVersion': 1, 'sourceOnly': True, 'runtimeQualified': False,
                'nativeExecutionGranted': False, 'providerExecutionGranted': False,
                'actualLinuxObservations': not self.synthetic, 'syntheticControls': self.synthetic,
                'observedUtc': dt.datetime.now(dt.timezone.utc).isoformat().replace('+00:00', 'Z'),
                'local': local, 'managerProperties': properties, 'ownedHelpers': release,
                'probeScope': 'read-only capabilities; no disposable unit lifecycle or SDK admission',
                'unrun': ['installed typed service property layouts', 'exact-owned true unit lifecycle',
                          'SDK/provider lifecycle', 'transport/Ryuk custody', 'native business tests']}


def true_unit_qualification(*args, **kwargs):
    # No installed policy/authenticator or mutating adapter is integrated.
    # A caller-supplied JSON true is not a grant.
    raise PermissionError('independent reviewed disposable-unit qualification policy absent; lifecycle refused')


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument('--mode', choices=('source-check', 'readonly-probe', 'true-unit'), default='source-check')
    parser.add_argument('--evidence', type=Path)
    parser.add_argument('--helper', type=Path, default=Path(__file__).resolve().parent/'employee_linux_readonly_helpers.py')
    args = parser.parse_args(argv)
    if args.mode == 'true-unit': true_unit_qualification()
    if args.mode == 'source-check':
        print(json.dumps({'sourceOnly': True, 'runtimeQualified': False, 'nativeExecutionGranted': False,
                          'trueUnitQualificationEligible': False, 'liveProbeExecuted': False})); return
    need(args.evidence is not None, 'explicit owned evidence required')
    need(args.evidence.is_absolute() and args.evidence.is_dir(), 'existing absolute private evidence directory required')
    for part in (args.evidence, *args.evidence.parents): need(not part.is_symlink(), 'linked evidence refused')
    reader = LinuxReader()  # Refuse unsupported platform before writing evidence.
    owned = args.evidence/('employee-linux-readonly-'+secrets.token_hex(16))
    owned.mkdir(mode=0o700, exist_ok=False)
    marker = owned/'source-probe-owner.json'
    with marker.open('xb') as writer:
        writer.write(json.dumps({'sourceOnly': True, 'purpose': 'finite read-only Linux capability probe',
                                 'nativeExecutionGranted': False}).encode())
    result = Probe(reader, ReadOnlyHelpers(owned, args.helper)).capture()
    result['evidenceDirectory'] = str(owned)
    print(json.dumps(result, sort_keys=True))


if __name__ == '__main__': main()
