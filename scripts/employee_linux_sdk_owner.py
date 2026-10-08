"""Source-only Employee SDK custody adapter, injected backend; no subprocess/RPC.

Default absent authority/backend pins refuse acquisition. No grant is created.
Systemd/observer/receipt authenticity requires independent runtime qualification.
"""
import datetime as dt
import hashlib
import json
import re

FLOOR_KIB = 4194304
RESERVE = 120
START = 10
STOP = 5
STOP_PHASES = 5
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
BOOT = re.compile(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z")


PHASE_CONTRACT_SHA256 = "a0100ee0a6c54f9f561e0c57e06b1aa49f5f9ed32d45b381347cea642ee0745c"
SOURCE_POLICY_SHA256 = "88507d27eeee6b7b4095bb556a5c7d1fc2d1b859786d0d5ba891c5bada0be895"
FIXED_SDK_PHASES = {'baseline-restore': ['/usr/bin/dotnet', 'restore', 'Legacy.Maliev.EmployeeService.slnx'], 'baseline-build': ['/usr/bin/dotnet', 'build', 'Legacy.Maliev.EmployeeService.slnx', '--configuration', 'Release', '--no-restore', '-warnaserror', '-nr:false', '-p:UseSharedCompilation=false'], 'baseline-focused': ['/usr/bin/dotnet', 'test', 'Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~EmployeeSignatureOwnerBoundaryHttpTests', '--logger', 'trx', '--results-directory', 'results/baseline-focused'], 'candidate-restore': ['/usr/bin/dotnet', 'restore', 'Legacy.Maliev.EmployeeService.slnx'], 'candidate-build': ['/usr/bin/dotnet', 'build', 'Legacy.Maliev.EmployeeService.slnx', '--configuration', 'Release', '--no-restore', '-warnaserror', '-nr:false', '-p:UseSharedCompilation=false'], 'candidate-focused': ['/usr/bin/dotnet', 'test', 'Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~EmployeeSignatureOwnerBoundaryHttpTests|FullyQualifiedName~SignatureReassignment_RejectsDifferentOwnerWithoutInventedForeignKey|FullyQualifiedName~Signature_CreateQueryNamedLocationAndSameOwnerUpdate_PreserveAuthorizedMetadataWithoutCloudIo', '--logger', 'trx', '--results-directory', 'results/candidate-focused'], 'candidate-suite': ['/usr/bin/dotnet', 'test', 'Legacy.Maliev.EmployeeService.slnx', '--configuration', 'Release', '--no-build', '--no-restore', '--logger', 'trx', '--collect', 'XPlat Code Coverage', '--results-directory', 'results/candidate-suite'], 'candidate-format': ['/usr/bin/dotnet', 'format', 'Legacy.Maliev.EmployeeService.slnx', '--verify-no-changes', '--no-restore'], 'candidate-audit': ['/usr/bin/dotnet', 'list', 'Legacy.Maliev.EmployeeService.slnx', 'package', '--vulnerable', '--include-transitive', '--no-restore']}


def need(value, message):
    if not value: raise ValueError(message)


def keys(value, expected):
    need(type(value) is dict and set(value) == set(expected), "closed schema required")


def seal(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def utc(value):
    need(type(value) is str and value.endswith("Z"), "explicit UTC required")
    return dt.datetime.fromisoformat(value[:-1]+"+00:00")


def strict_json(raw):
    need(type(raw) is bytes and 0 < len(raw) <= 16384, "bounded original authority required")
    def pairs(items):
        row = {}
        for key, value in items:
            need(key not in row, "duplicate authority field")
            row[key] = value
        return row
    return json.loads(raw, object_pairs_hook=pairs, parse_constant=lambda _: (_ for _ in ()).throw(ValueError("nonfinite authority")))


def unit_plan(stage, now):
    keys(stage, {"schemaVersion", "sourceOnly", "sourceHead", "codeSha256", "phaseContractSha256", "sourcePolicySha256", "sdkExecutableSha256", "runId", "attempt", "bootId", "startsUtc", "expiresUtc", "phaseSeconds", "phaseId", "argv", "workingDirectory", "rootGrantSha256", "backendQualificationSha256"})
    need(type(stage["schemaVersion"]) is int and stage["schemaVersion"] == 1 and stage["sourceOnly"] is True, "source-only fixed stage required")
    need(type(stage["sourceHead"]) is str and re.fullmatch(r"[0-9a-f]{40}", stage["sourceHead"]) and HEX64.fullmatch(stage["codeSha256"] or ""), "source/code seals required")
    need(type(stage["runId"]) is str and re.fullmatch(r"[1-9][0-9]{0,19}", stage["runId"]) and type(stage["attempt"]) is int and 0 < stage["attempt"] <= 1000, "exact hosted run/attempt required")
    need(BOOT.fullmatch(stage["bootId"] or ""), "actual boot identity required")
    need(type(stage["phaseId"]) is str and re.fullmatch(r"(?:baseline|candidate)-[a-z]{1,20}", stage["phaseId"]), "canonical fixed phase identity required")
    need(type(stage["phaseSeconds"]) is int and 0 < stage["phaseSeconds"] <= 600, "finite phase required")
    need(now.tzinfo is not None and now.utcoffset() == dt.timedelta(0), "UTC admission clock required")
    start, end = utc(stage["startsUtc"]), utc(stage["expiresUtc"])
    need(start <= now < end and 0 < (end-start).total_seconds() <= 3600, "finite current allocation required")
    need((end-now).total_seconds() >= stage["phaseSeconds"] + START + STOP_PHASES*STOP + RESERVE, "all manager stop phases plus cleanup reserve required")
    need(stage["phaseContractSha256"] == PHASE_CONTRACT_SHA256 and stage["sourcePolicySha256"] == SOURCE_POLICY_SHA256 and HEX64.fullmatch(stage["sdkExecutableSha256"] or ""), "exact protected phase/policy and actual SDK executable seals required")
    need(stage["phaseId"] in FIXED_SDK_PHASES and stage["argv"] == FIXED_SDK_PHASES[stage["phaseId"]], "fixed protected dotnet phase argv required; nonSDK phases separate")
    argv = stage["argv"]
    need(type(argv) is list and 1 <= len(argv) <= 64 and all(type(value) is str and 0 < len(value) <= 1024 and "\x00" not in value and "\n" not in value for value in argv), "bounded literal argv required")
    need(argv[0] == "/usr/bin/dotnet" and not any(value in {"/bin/sh", "/bin/bash", "sh", "bash", "-c"} for value in argv), "direct SDK executable only; shell refused")
    cwd = stage["workingDirectory"]
    need(type(cwd) is str and cwd.startswith("/") and len(cwd) <= 256 and all(re.fullmatch(r"[A-Za-z0-9_.-]+", part) and part not in {".", ".."} for part in cwd[1:].split("/")), "fixed private absolute working directory required")
    name = "employee-sdk-"+stage["runId"]+"-"+str(stage["attempt"])+"-"+stage["phaseId"]+".service"
    props = {"Type": "exec", "NotifyAccess": "none", "ExecStart": [{"path": argv[0], "argv": argv, "ignoreFailure": False}], "ExecStop": [], "WorkingDirectory": cwd, "RuntimeMaxUSec": stage["phaseSeconds"]*1000000, "TimeoutStartUSec": START*1000000, "TimeoutStopUSec": STOP*1000000, "MemoryMax": 3*1024**3, "MemorySwapMax": 0, "CPUQuotaPerSecUSec": 250000, "TasksMax": 128, "KillMode": "control-group", "SendSIGKILL": True, "NoNewPrivileges": True, "PrivateTmp": True, "RemainAfterExit": True, "Environment": []}
    return {"unit": name, "properties": props, "stageSha256": seal(stage), "sourceOnly": True, "executionGranted": False}


class SDKOwner:
    """Backend owns durable custody and actual systemd effects; never auto-adopt."""
    def __init__(self, backend, stage, verify_root, verify_backend, clock=None):
        self.backend = backend
        self.clock = clock or (lambda: dt.datetime.now(dt.timezone.utc))
        self.stage = stage
        self.verify_root = verify_root
        self.verify_backend = verify_backend
        self.generation = None
        self.plan = None

    def _admit(self, raw, now):
        plan = unit_plan(self.stage, now)
        for field in ("rootGrantSha256", "backendQualificationSha256"):
            need(HEX64.fullmatch(self.stage[field] or ""), "independent original authority/backend pin absent")
        need(hashlib.sha256(raw).hexdigest() == self.stage["rootGrantSha256"] and self.verify_root(raw, self.stage["rootGrantSha256"]) is True, "trusted original Root verification failed")
        authority = strict_json(raw)
        keys(authority, {"stageSha256", "runId", "attempt", "bootId", "expiresUtc"})
        # Root bytes bind the stage without its own grant hash to avoid a hash
        # self-reference; backend/source/argv/expiry remain covered.
        unpinned = dict(self.stage, rootGrantSha256=None)
        need(authority["stageSha256"] == seal(unpinned) and all(authority[key] == self.stage[key] for key in ("runId", "attempt", "bootId", "expiresUtc")), "original authority scope differs")
        need(self.verify_backend(self.stage["backendQualificationSha256"]) is True, "independent runtime backend unqualified")
        if isinstance(self.backend, RealLinuxBackend): self.backend.stage = self.stage
        observation = self.backend.preflight()
        keys(observation, {"bootId", "observedUtc", "memFreeKiB", "competingProcesses", "candidateExecutableCensusQualified"})
        need(observation["bootId"] == self.stage["bootId"] and 0 <= (now-utc(observation["observedUtc"])).total_seconds() <= 2, "fresh exact host observation required")
        need(type(observation["memFreeKiB"]) is int and observation["memFreeKiB"] >= FLOOR_KIB, "fresh physical MemFree floor required")
        need(observation["competingProcesses"] == [] and observation["candidateExecutableCensusQualified"] is True, "competing/unknown executable census refused")
        return plan

    def acquire(self, raw, now):
        need(self.generation is None, "SDK acquisition replay refused")
        plan = self._admit(raw, now)
        # Independent durable custody must own the unit before manager start.
        token = self.backend.register_intent(plan)
        need(type(token) is str and HEX64.fullmatch(token), "durable independent custody intent required")
        self.plan = plan
        need(self._admit(raw, self.clock()) == plan, "admission changed after durable intent")
        self.backend.start_transient(plan, token)
        item = self.backend.observe(plan["unit"])
        self.generation = self._validate(item, running=item["active"])
        self.backend.persist_generation(self.generation, token)
        return {"sourceOnly": True, "executionGrantedByModule": False, "generation": self.generation}

    def _validate(self, item, running):
        keys(item, {"unit", "invocationId", "bootId", "cgroup", "device", "inode", "pid", "birthTicks", "executable", "properties", "active", "members", "expiresUtc", "managerState", "execMainStatus"})
        need(item["unit"] == self.plan["unit"] and item["bootId"] == self.stage["bootId"] and item["properties"] == self.plan["properties"] and item["expiresUtc"] == self.stage["expiresUtc"], "manager identity/properties differ")
        need(type(item["invocationId"]) is str and re.fullmatch(r"[0-9a-f]{32}", item["invocationId"]), "exact invocation required")
        need(type(item["cgroup"]) is str and item["cgroup"] == "/system.slice/"+item["unit"], "exact private SDK cgroup required")
        if running:
            need(all(type(item[key]) is int and item[key] > 0 for key in ("device", "inode", "pid")), "retained kernel generation required")
            need(type(item["birthTicks"]) is str and item["birthTicks"].isdecimal() and item["executable"] == "/usr/bin/dotnet", "exact SDK birth/executable required")
        else:
            need(item["managerState"] in {"inactive", "failed", "exited"} and item["active"] is False and item["members"] == [] and type(item["pid"]) is int and item["pid"] == 0, "actual same-manager terminal witness required")
            need(type(item["execMainStatus"]) is int, "actual terminal exit status required")
        need(type(item["active"]) is bool and type(item["members"]) is list and all(type(pid) is int and 0 < pid < 2**31 for pid in item["members"]) and len(set(item["members"])) == len(item["members"]), "actual complete typed manager/cgroup census required")
        if running: need(item["active"] is True and item["pid"] in item["members"], "SDK not observed running")
        return {key: item[key] for key in ("unit", "invocationId", "bootId", "cgroup", "device", "inode", "pid", "birthTicks", "executable", "expiresUtc")}

    def cleanup(self, now):
        # Cleanup ignores memory/Root expiry, but requires retained generation;
        # uncertain start is settled by independent custody, never inventory.
        need(self.generation is not None and self.plan is not None, "unknown acquisition needs independent custody recovery")
        before = self.backend.observe(self.plan["unit"])
        self._same_generation_or_terminal(before)
        if before["active"] or before["managerState"] == "exited":
            self.backend.stop_exact(self.generation, deadlineSeconds=120)
        after = self.backend.observe(self.plan["unit"])
        self._same_generation_or_terminal(after)
        need(after["active"] is False and after["members"] == [] and after["pid"] == 0 and after["managerState"] in {"inactive", "failed"}, "all-member manager terminal cleanup unverified")
        need(type(after["execMainStatus"]) is int, "actual manager exit status required")
        receipt = {"generation": self.generation, "terminal": True, "remainingMembers": [], "observedUtc": self.clock().isoformat().replace("+00:00", "Z"), "managerResult": after["managerState"], "execMainStatus": after["execMainStatus"], "sourceOnly": True, "runtimeQualificationClaimed": False}
        self.backend.persist_cleanup(receipt)
        return receipt

    def _same_generation_or_terminal(self, item):
        if item["active"]:
            need(self._validate(item, running=True) == self.generation, "SDK generation changed; no foreign stop")
        else:
            self._validate(item, running=False)
            for key in ("unit", "invocationId", "bootId", "cgroup", "expiresUtc"):
                need(item[key] == self.generation[key], "terminal manager generation changed")
            if item["inode"] is not None:
                need((item["device"], item["inode"]) == (self.generation["device"], self.generation["inode"]), "terminal cgroup was replaced")


class RealLinuxBackend:
    """Concrete systemd/proc adapter; never used without SDKOwner's authority gate.

    The injected command method in tests must not be mistaken for qualification.
    Root access is required; no sudo escalation or socket/fixture access exists.
    """
    def __init__(self, evidence, executable_sha256):
        from pathlib import Path
        import os
        self.evidence = Path(evidence)
        need(self.evidence.is_absolute() and HEX64.fullmatch(executable_sha256 or ""), "absolute evidence and qualified SDK binary pin required")
        self.expected_binary = executable_sha256
        self.plan = None
        self.stage = None
        self.command_receipts = []
        self._serial = 0
        self.proc = Path("/proc")
        self.groups = Path("/sys/fs/cgroup")
        self.native_inventory = None
        self.root_access = os.name == "posix" and hasattr(os, "geteuid") and os.geteuid() == 0

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
        need(type(argv) is list and argv[0] in {"/usr/bin/busctl", "/usr/bin/systemd-run", "/usr/bin/systemctl"}, "literal manager executable required")
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

    def _boot(self):
        value = (self.proc/"sys/kernel/random/boot_id").read_text().strip()
        need(BOOT.fullmatch(value), "actual Linux boot required")
        return value

    def preflight(self):
        from pathlib import Path
        import os
        need(self.root_access and self.stage is not None and self.stage["sdkExecutableSha256"] == self.expected_binary, "qualified root-owned Linux manager adapter and stage executable pin required")
        binary = Path("/usr/bin/dotnet").resolve(strict=True)
        need(binary.is_file() and binary.stat().st_size <= 128*1024**2, "bounded actual SDK executable required")
        need(hashlib.sha256(binary.read_bytes()).hexdigest() == self.expected_binary, "actual SDK executable seal differs")
        memory = dict(line.split(":", 1) for line in (self.proc/"meminfo").read_text().splitlines())
        free = int(memory["MemFree"].split()[0])
        candidates = []
        qualified = type(self.native_inventory) is dict and bool(self.native_inventory)
        if qualified:
            need(all(type(path) is str and path.startswith("/") and HEX64.fullmatch(value or "") for path, value in self.native_inventory.items()), "qualified executable inventory required")
        entries = list(self.proc.iterdir()); need(len(entries) <= 8192, "bounded whole-process census required")
        for entry in entries:
            if not entry.name.isdecimal(): continue
            try:
                before = self._birth(int(entry.name))
                executable = os.readlink(entry/"exe")
                after = self._birth(int(entry.name))
                need(before == after, "process census generation changed")
                if qualified and executable in self.native_inventory:
                    need(hashlib.sha256(Path(executable).read_bytes()).hexdigest() == self.native_inventory[executable], "qualified executable bytes changed")
                    candidates.append({"pid": int(entry.name), "birthTicks": before, "executable": executable})
                elif Path(executable).name in {"dotnet", "testhost", "MSBuild", "VBCSCompiler", "csc", "vstest", "datacollector", "postgres", "redis-server"}:
                    candidates.append({"pid": int(entry.name), "birthTicks": before, "executable": executable})
            except FileNotFoundError: continue
        return {"bootId": self._boot(), "observedUtc": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"), "memFreeKiB": free, "competingProcesses": candidates, "candidateExecutableCensusQualified": qualified}

    def _property(self, unit, interface, name):
        # systemd object-path escaping; never evaluate systemctl string argv.
        encoded = "".join(char if char.isalnum() else "_"+format(ord(char), "02x") for char in unit)
        path = "/org/freedesktop/systemd1/unit/"+encoded
        raw = self._command(["/usr/bin/busctl", "--system", "--json=short", "get-property", "org.freedesktop.systemd1", path, interface, name])
        result = strict_json(raw)
        keys(result, {"type", "data"})
        signatures = {"InvocationID": "ay", "ControlGroup": "s", "MainPID": "u", "ActiveState": "s", "SubState": "s", "ExecMainStatus": "i", "Type": "s", "NotifyAccess": "s", "WorkingDirectory": "s", "RuntimeMaxUSec": "t", "TimeoutStartUSec": "t", "TimeoutStopUSec": "t", "MemoryMax": "t", "MemorySwapMax": "t", "CPUQuotaPerSecUSec": "t", "TasksMax": "t", "KillMode": "s", "SendSIGKILL": "b", "NoNewPrivileges": "b", "PrivateTmp": "b", "RemainAfterExit": "b", "ExecStart": "a(sasbttttuii)", "ExecStop": "a(sasbttttuii)", "Environment": "as"}
        need(name in signatures and result["type"] == signatures[name], "actual typed manager signature differs")
        return result["data"]

    def register_intent(self, plan):
        need(self.root_access and self.stage is not None, "qualified stage not installed")
        self.plan = plan
        # This records intent; manager-backed finite unit is the independent
        # process owner after StartTransientUnit. Lost start remains uncertain.
        return self._write("sdk-create-intent.json", {"plan": plan, "bootId": self._boot(), "createdUtc": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")})

    def start_transient(self, plan, token):
        need(self.root_access and HEX64.fullmatch(token or "") and plan == self.plan, "exact retained intent required")
        props = plan["properties"]
        argv = ["/usr/bin/systemd-run", "--unit="+plan["unit"], "--quiet"]
        literal = {"Type": "exec", "NotifyAccess": "none", "RuntimeMaxSec": str(props["RuntimeMaxUSec"]//1000000), "TimeoutStartSec": "10", "TimeoutStopSec": "5", "MemoryMax": str(props["MemoryMax"]), "MemorySwapMax": "0", "CPUQuota": "25%", "TasksMax": "128", "KillMode": "control-group", "SendSIGKILL": "yes", "NoNewPrivileges": "yes", "PrivateTmp": "yes", "RemainAfterExit": "yes", "WorkingDirectory": props["WorkingDirectory"]}
        for key, value in literal.items(): argv += ["--property="+key+"="+value]
        argv += ["--", *props["ExecStart"][0]["argv"]]
        self._command(argv, timeout=10)

    def _cgroup_snapshot(self, target):
        import os
        import stat
        queue = [target]; directories = []; members = []
        while queue:
            path = queue.pop()
            need(len(directories) < 256, "cgroup directory bound exceeded")
            info = path.stat()
            need(stat.S_ISDIR(info.st_mode) and not path.is_symlink(), "actual cgroup directory required")
            directories.append((str(path), info.st_dev, info.st_ino))
            values = (path/"cgroup.procs").read_text().split()
            need(all(value.isdecimal() and int(value) > 0 for value in values), "actual cgroup PID list required")
            members += [int(value) for value in values]
            need(len(members) <= 128, "SDK task census bound exceeded")
            with os.scandir(path) as entries:
                for entry in entries:
                    observed = entry.stat(follow_symlinks=False)
                    need(not stat.S_ISLNK(observed.st_mode), "cgroup links refused")
                    if stat.S_ISDIR(observed.st_mode): queue.append(type(target)(entry.path))
        return sorted(directories), sorted(members)

    def observe(self, unit):
        import os
        from pathlib import Path
        need(self.plan is not None and unit == self.plan["unit"], "no foreign manager observation")
        iface = "org.freedesktop.systemd1.Service"
        manager = "org.freedesktop.systemd1.Unit"
        def one(interface, name):
            data = self._property(unit, interface, name)
            need(type(data) is list and len(data) == 1, "typed scalar manager value required")
            return data[0]
        invocation = one(manager, "InvocationID")
        if type(invocation) is list: invocation = bytes(invocation).hex()
        group = one(iface, "ControlGroup")
        pid = one(iface, "MainPID")
        active_state = one(manager, "ActiveState")
        substate = one(manager, "SubState")
        exec_status = one(iface, "ExecMainStatus")
        props = dict(self.plan["properties"])
        for name in ("Type", "NotifyAccess", "WorkingDirectory", "RuntimeMaxUSec", "TimeoutStartUSec", "TimeoutStopUSec", "MemoryMax", "MemorySwapMax", "CPUQuotaPerSecUSec", "TasksMax", "KillMode", "SendSIGKILL", "NoNewPrivileges", "PrivateTmp", "RemainAfterExit"):
            need(one(iface, name) == props[name], "actual manager containment differs")
        commands = one(iface, "ExecStart")
        need(type(commands) is list and len(commands) == 1 and type(commands[0]) is list and len(commands[0]) == 10 and commands[0][:3] == [props["ExecStart"][0]["path"], props["ExecStart"][0]["argv"], False], "actual typed ExecStart differs")
        need(one(iface, "ExecStop") == [] and one(iface, "Environment") == [], "unexpected stop/environment expansion")
        expected_group = "/system.slice/"+unit
        # Inactive manager may clear ControlGroup; that does not authorize a
        # different path. Retained expected generation is reobserved below.
        need(group in {expected_group, ""}, "unexpected manager cgroup path")
        target = self.groups/expected_group.lstrip("/")
        device = inode = None; members = []
        try:
            first = target.stat()
        except FileNotFoundError:
            need(pid == 0 and (active_state in {"inactive", "failed"} or substate == "exited"), "missing live cgroup refused")
        else:
            # Any descendant disappearance/read error is uncertainty; only an
            # absent ROOT at initial observation with manager terminal permits
            # absence. Never discard an already observed nonempty subtree.
            directories_before, before = self._cgroup_snapshot(target)
            second = target.stat()
            directories_after, after = self._cgroup_snapshot(target)
            need((first.st_dev, first.st_ino) == (second.st_dev, second.st_ino) and directories_before == directories_after and before == after, "unstable whole cgroup census")
            device, inode, members = first.st_dev, first.st_ino, before
        active = pid != 0
        birth = self._birth(pid) if active else None
        executable = os.readlink(self.proc/str(pid)/"exe") if active else None
        if active:
            need(Path(executable).resolve() == Path("/usr/bin/dotnet").resolve() and self._birth(pid) == birth, "SDK executable generation changed")
            executable = "/usr/bin/dotnet"
        return {"unit": unit, "invocationId": invocation, "bootId": self._boot(), "cgroup": expected_group, "device": device, "inode": inode, "pid": pid, "birthTicks": birth, "executable": executable, "properties": props, "active": active, "members": members, "expiresUtc": self.stage["expiresUtc"], "managerState": "exited" if substate == "exited" else active_state, "execMainStatus": exec_status}

    def persist_generation(self, generation, token):
        return self._write("sdk-generation.json", {"generation": generation, "intentSha256": token})

    def stop_exact(self, generation, deadlineSeconds):
        need(deadlineSeconds == 120, "finite exact cleanup grace required")
        current = self.observe(generation["unit"])
        for key in ("unit", "invocationId", "bootId", "cgroup"):
            need(current[key] == generation[key], "manager invocation changed; no foreign stop")
        if current["active"]:
            need(all(current[key] == generation[key] for key in ("device", "inode", "pid", "birthTicks", "executable")), "kernel SDK generation changed")
        self._command(["/usr/bin/systemctl", "stop", "--no-block", generation["unit"]])
        import time
        end = time.monotonic()+deadlineSeconds
        self._cleanup_deadline = end
        try:
            while time.monotonic() < end:
                item = self.observe(generation["unit"])
                for key in ("unit", "invocationId", "bootId", "cgroup"):
                    need(item[key] == generation[key], "cleanup manager invocation changed")
                if item["active"] is False and item["pid"] == 0 and item["members"] == [] and item["managerState"] in {"inactive", "failed"}: return
                time.sleep(0.05)
            raise ValueError("finite SDK terminal cleanup deadline elapsed")
        finally:
            self._cleanup_deadline = None

    def persist_cleanup(self, receipt):
        return self._write("sdk-cleanup.json", receipt)


TRUSTED_ROOT_PUBLIC_KEY_SHA256 = None


def bounded_read(path, maximum):
    from pathlib import Path
    import stat
    path = Path(path)
    for part in [path, *path.parents]:
        if part.exists() or part.is_symlink():
            info = part.lstat()
            need(not stat.S_ISLNK(info.st_mode) and not (getattr(info, "st_file_attributes", 0) & 1024), "linked input refused")
    need(path.is_file() and path.stat().st_size <= maximum, "bounded regular input required")
    with path.open("rb") as stream: raw = stream.read(maximum+1)
    need(len(raw) <= maximum, "input grew beyond bound")
    return raw


def verify_projection(contract_path, policy_path, projection, generated_outputs=None):
    from pathlib import Path
    import types
    raw = bounded_read(contract_path, 65536)
    need(hashlib.sha256(raw).hexdigest() == PHASE_CONTRACT_SHA256, "protected phase verifier seal differs")
    module = types.ModuleType("employee_protected_phase_contract")
    module.__file__ = str(contract_path)
    exec(compile(raw, str(contract_path), "exec"), module.__dict__)
    root = Path(projection)
    need(root.is_dir() and not root.is_symlink(), "actual sealed projection required")
    source_policy = module.verify_source_policy(bounded_read(policy_path, 262144))
    expected = {row["path"] for view in ("baseline", "candidate", "dependencies") for row in source_policy["capsules"][view]["rows"]}
    inventory = {}; observed_outputs = {}
    total = 0
    for path in root.rglob("*"):
        need(not path.is_symlink(), "projection links refused")
        if path.is_dir(): continue
        relative = path.relative_to(root).as_posix()
        if relative in {".employee-source-owner", "intake-receipt.json"}: continue
        need(path.is_file() and path.stat().st_size <= 8*1024**2, "bounded regular source required")
        raw = path.read_bytes(); total += len(raw)
        need(total <= 64*1024**2 and len(inventory)+len(observed_outputs) < 4096, "source projection bound exceeded")
        if relative in expected: inventory[relative] = raw
        else: observed_outputs[relative] = {"sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}
    need(observed_outputs == (generated_outputs or {}), "new/generated files need separately qualified exact output inventory")
    module.verify_source_bytes(bounded_read(policy_path, 262144), inventory)
    return {"rawFilesVerified": len(inventory), "sourcePolicySha256": SOURCE_POLICY_SHA256}


def execute_qualification(stage, root_policy_raw, grant_raw, backend_receipt_raw, evidence, source_verifier, backend_factory=RealLinuxBackend, clock=None, verify_root_policy=None, actual_baseline_review_raw=None, verify_baseline_review=None):
    need(HEX64.fullmatch(TRUSTED_ROOT_PUBLIC_KEY_SHA256 or ""), "trusted independently reviewed Root public key absent; no backend instantiated")
    need(verify_root_policy is not None and verify_root_policy(root_policy_raw, TRUSTED_ROOT_PUBLIC_KEY_SHA256) is True, "original detached policy authentication unimplemented/unverified")
    policy = strict_json(root_policy_raw)
    keys(policy, {"stageSha256", "grantSha256", "backendReceiptSha256", "backendModuleSha256", "phaseContractSha256", "sourcePolicySha256", "authoritySchemaIntegrationReviewed", "actualBaselineReviewSha256"})
    need(policy["stageSha256"] == seal(stage) and policy["phaseContractSha256"] == PHASE_CONTRACT_SHA256 and policy["sourcePolicySha256"] == SOURCE_POLICY_SHA256, "original Root exact phase source binding differs")
    need(policy["authoritySchemaIntegrationReviewed"] is True, "original stage grant schema integration not reviewed; published all-phases grant cannot auto-convert")
    if stage["phaseId"].startswith("candidate-"):
        need(HEX64.fullmatch(policy["actualBaselineReviewSha256"] or "") and type(actual_baseline_review_raw) is bytes and hashlib.sha256(actual_baseline_review_raw).hexdigest() == policy["actualBaselineReviewSha256"] and verify_baseline_review is not None and verify_baseline_review(actual_baseline_review_raw, policy["actualBaselineReviewSha256"]) is True, "candidate cannot waive actual independently authenticated baseline outcome review")
    else:
        need(policy["actualBaselineReviewSha256"] is None, "baseline does not invent a future outcome review")
    need(hashlib.sha256(grant_raw).hexdigest() == policy["grantSha256"] == stage["rootGrantSha256"] and hashlib.sha256(backend_receipt_raw).hexdigest() == policy["backendReceiptSha256"] == stage["backendQualificationSha256"], "original grant/backend receipt differs")
    receipt = strict_json(backend_receipt_raw)
    keys(receipt, {"runtimeQualified", "sdkSourceSha256", "fixtureTransportQualified", "fixtureRegistrySha256", "nativeExecutableInventory"})
    from pathlib import Path
    need(hashlib.sha256(Path(__file__).read_bytes()).hexdigest() == receipt["sdkSourceSha256"] == policy["backendModuleSha256"] and receipt["runtimeQualified"] is True and receipt["fixtureTransportQualified"] is True and HEX64.fullmatch(receipt["fixtureRegistrySha256"] or ""), "actual independent backend/fixture qualification absent")
    need(stage["phaseId"] not in {"baseline-focused", "candidate-focused", "candidate-suite"}, "provider phases blocked: sealed scoped DOCKER_HOST/runtime transport custody not integrated; default daemon forbidden")
    source_verifier()
    clock = clock or (lambda: dt.datetime.now(dt.timezone.utc))
    backend = backend_factory(evidence, stage["sdkExecutableSha256"])
    backend.stage = stage
    backend.native_inventory = receipt["nativeExecutableInventory"]
    owner = SDKOwner(backend, stage, lambda raw, pin: hashlib.sha256(raw).hexdigest() == policy["grantSha256"], lambda pin: pin == policy["backendReceiptSha256"], clock=clock)
    failure = None; result = None; cleanup = None
    try:
        owner.acquire(grant_raw, clock())
        import time
        deadline = min(clock()+dt.timedelta(seconds=stage["phaseSeconds"]), utc(stage["expiresUtc"])-dt.timedelta(seconds=RESERVE))
        while clock() < deadline:
            item = backend.observe(owner.plan["unit"])
            owner._same_generation_or_terminal(item)
            if not item["active"] and not item["members"]:
                result = {"actualExitStatus": item["execMainStatus"], "actualManagerState": item["managerState"], "nativeCasesClaimed": 0, "behaviorReviewed": False}
                break
            time.sleep(0.05)
        need(result is not None, "actual phase deadline elapsed")
    except BaseException as caught:
        failure = caught
    finally:
        if owner.generation is not None:
            try: cleanup = owner.cleanup(clock())
            except BaseException as caught:
                if failure is None: failure = caught
        backend._write("phase-result.json", {"actual": result, "cleanup": cleanup, "failureType": type(failure).__name__ if failure else None, "sourceOnlyCodeQualification": True})
    if failure is not None: raise failure
    need(cleanup is not None and cleanup["terminal"] is True, "actual SDK cleanup absent")
    return result


def main(argv=None):
    import argparse
    from pathlib import Path
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("source-preflight", "qualification"), required=True)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--root-policy", type=Path)
    parser.add_argument("--grant", type=Path)
    parser.add_argument("--backend-receipt", type=Path)
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--phase-contract", type=Path)
    parser.add_argument("--source-policy", type=Path)
    parser.add_argument("--projection", type=Path)
    args = parser.parse_args(argv)
    # No automatic grant conversion or claimed backend qualification exists.
    if args.mode == "qualification":
        need(HEX64.fullmatch(TRUSTED_ROOT_PUBLIC_KEY_SHA256 or ""), "Original Employee phase authority and independently qualified fixture transport/backend are not integrated")
        need(all(value is not None for value in (args.root_policy, args.grant, args.backend_receipt, args.evidence, args.phase_contract, args.source_policy, args.projection)), "original qualification inputs required")
        result = execute_qualification(strict_json(bounded_read(args.stage, 16384)), bounded_read(args.root_policy, 16384), bounded_read(args.grant, 16384), bounded_read(args.backend_receipt, 16384), args.evidence, lambda: verify_projection(args.phase_contract, args.source_policy, args.projection))
        print(json.dumps(result, sort_keys=True)); return
    raw = bounded_read(args.stage, 16384)
    stage = strict_json(raw)
    plan = unit_plan(stage, dt.datetime.now(dt.timezone.utc))
    print(json.dumps({"sourceOnly": True, "nativeExecutionGranted": False, "plan": plan, "realBackendImplemented": True, "realBackendQualified": False, "authoritySchemaIntegrationComplete": False}, sort_keys=True))


if __name__ == "__main__":
    main()
