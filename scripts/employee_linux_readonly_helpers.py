"""Standalone bounded read-only helper source, extracted from reviewed SDK38.
Only two literal busctl GETs; no SDK, systemd-run, systemctl or provider operations.
The original helper source seal is provenance of source, not runtime qualification.
"""
import hashlib
import json
from pathlib import Path

def need(value, message):
    if not value: raise ValueError(message)

READ_ONLY_ARGV = frozenset(tuple(['/usr/bin/busctl', '--system', '--json=short', '--timeout=2s', 'get-property', 'org.freedesktop.systemd1', '/org/freedesktop/systemd1', 'org.freedesktop.systemd1.Manager', name]) for name in ('Version', 'SystemState'))

class FiniteReadOnlyHelpers:
    def __init__(self, evidence):
        self.evidence = Path(evidence)
        need(self.evidence.is_absolute() and self.evidence.is_dir(), 'owned private evidence required')
        self.proc = Path('/proc')
        self.command_receipts = []
        self._serial = 0

    def _write(self, name, value):
        import os
        from pathlib import Path
        need(self.evidence.is_dir() and not self.evidence.is_symlink(), "existing owned private evidence required")
        for part in [self.evidence, *self.evidence.parents]: need(not part.is_symlink(), "linked evidence refused")
        raw = json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()
        need(len(raw) <= 65536, "bounded nonsensitive receipt required")
        target = self.evidence/name
        fd = os.open(target, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
        with os.fdopen(fd, "wb") as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        directory = os.open(self.evidence, os.O_RDONLY|os.O_DIRECTORY)
        try: os.fsync(directory)
        finally: os.close(directory)
        return hashlib.sha256(raw).hexdigest()

    def _group_members(self, group):
        import os
        rows = []
        entries = list(self.proc.iterdir()); need(len(entries) <= 8192, "helper census bound exceeded")
        for entry in entries:
            if not entry.name.isdecimal(): continue
            try:
                fields = (entry/"stat").read_text().rsplit(")", 1)[1].split()
                if int(fields[2]) == group:
                    birth = fields[19]
                    executable = os.readlink(entry/"exe")
                    need(self._birth(int(entry.name)) == birth, "helper member changed during observation")
                    rows.append({"pid": int(entry.name), "birthTicks": birth, "executable": executable})
            except FileNotFoundError: continue
        return sorted(rows, key=lambda row: row["pid"])

    def _command(self, argv, timeout=3):
        """Exact private helper group, pidfds, RLIMIT and all-member absence."""
        import os
        import resource
        import selectors
        import signal
        import subprocess
        import time
        need(type(argv) is list and tuple(argv) in READ_ONLY_ARGV, "unknown or mutating helper command refused")
        if getattr(self, "_cleanup_deadline", None) is not None:
            timeout = min(timeout, self._cleanup_deadline-time.monotonic())
        need(0 < timeout <= 10, "finite helper deadline required")
        def limits():
            resource.setrlimit(resource.RLIMIT_AS, (256*1024**2, 256*1024**2))
            resource.setrlimit(resource.RLIMIT_CPU, (10, 10))
        proc = None; pidfd = None; selector = None
        output = bytearray(); error = bytearray(); known = {}
        row = {"pid": None, "executable": argv[0], "purpose": "owned finite manager helper", "timeoutSeconds": timeout, "memoryBytes": 256*1024**2, "cpuSeconds": 10, "birthTicks": None, "exitCode": None, "cleanupVerified": False, "remainingMembers": []}
        self.command_receipts.append(row)
        failure = None
        try:
            proc = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True, preexec_fn=limits)
            row["pid"] = proc.pid
            pidfd = os.pidfd_open(proc.pid)
            row["birthTicks"] = self._birth(proc.pid)
            selector = selectors.DefaultSelector()
            selector.register(proc.stdout, selectors.EVENT_READ, output)
            selector.register(proc.stderr, selectors.EVENT_READ, error)
            end = time.monotonic()+timeout
            while selector.get_map():
                need(time.monotonic() < end, "manager helper timed out")
                members = self._group_members(proc.pid)
                if proc.poll() is None:
                    need(self._birth(proc.pid) == row["birthTicks"] and os.getpgid(proc.pid) == proc.pid, "helper group generation changed")
                    for member in members: known[member["pid"]] = member
                else:
                    need(all(known.get(member["pid"]) == member for member in members), "unknown postleader helper member preserved")
                for key, _ in selector.select(min(0.1, max(0, end-time.monotonic()))):
                    chunk = os.read(key.fileobj.fileno(), 8192)
                    if not chunk: selector.unregister(key.fileobj)
                    else:
                        key.data.extend(chunk)
                        need(len(output)+len(error) <= 262144, "manager stream bound exceeded")
            proc.wait(timeout=max(0.01, end-time.monotonic()))
            row["exitCode"] = proc.returncode
            need(proc.returncode == 0, "actual manager command failed")
            return bytes(output)
        except BaseException as caught:
            failure = caught
            raise
        finally:
            cleanup_error = None
            try:
                if proc is not None:
                    members = self._group_members(proc.pid)
                    if proc.poll() is None:
                        # pidfd still names the exact retained child even if
                        # initial birth observation failed. Never signal an
                        # inferred group when birth ownership is unknown.
                        if row["birthTicks"] is None:
                            need(pidfd is not None, "child pidfd unavailable; independent helper recovery required")
                            signal.pidfd_send_signal(pidfd, signal.SIGKILL)
                        else:
                            need(self._birth(proc.pid) == row["birthTicks"] and os.getpgid(proc.pid) == proc.pid, "helper generation changed")
                            for member in members: known[member["pid"]] = member
                            signal.pidfd_send_signal(pidfd, signal.SIGTERM)
                        try: proc.wait(timeout=1)
                        except subprocess.TimeoutExpired:
                            signal.pidfd_send_signal(pidfd, signal.SIGKILL); proc.wait(timeout=2)
                    members = self._group_members(proc.pid)
                    for member in members:
                        need(known.get(member["pid"]) == member, "unknown helper member preserved")
                        memberfd = os.pidfd_open(member["pid"])
                        try:
                            need(self._birth(member["pid"]) == member["birthTicks"], "helper member birth changed")
                            signal.pidfd_send_signal(memberfd, signal.SIGTERM)
                            signal.pidfd_send_signal(memberfd, signal.SIGKILL)
                        finally: os.close(memberfd)
                    deadline = time.monotonic()+2
                    while self._group_members(proc.pid) and time.monotonic() < deadline: time.sleep(0.02)
                    first = self._group_members(proc.pid); second = self._group_members(proc.pid)
                    row["remainingMembers"] = second
                    need(first == second == [], "helper group cleanup unverified")
                    row["exitCode"] = proc.returncode
                    row["cleanupVerified"] = proc.returncode is not None
            except BaseException as caught:
                cleanup_error = caught
                row["cleanupVerified"] = False
            finally:
                if selector is not None: selector.close()
                if proc is not None:
                    for stream in (proc.stdout, proc.stderr):
                        if stream is not None: stream.close()
                if pidfd is not None: os.close(pidfd)
                row["failureType"] = type(failure).__name__ if failure else None
                row["cleanupFailureType"] = type(cleanup_error).__name__ if cleanup_error else None
                self._serial += 1
                self._write("helper-"+str(self._serial)+".json", row)
            if cleanup_error is not None: raise cleanup_error

    def _birth(self, pid):
        return (self.proc/str(pid)/"stat").read_text().rsplit(")", 1)[1].split()[19]
