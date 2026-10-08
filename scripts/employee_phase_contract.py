"""Pure Employee hosted phase contract. No process/provider/dispatch implementation."""
import datetime as dt
import hashlib
import json
import re

REPOSITORY = "MALIEV-Co-Ltd/Legacy.Maliev.EmployeeService"
TRANSPORT_MAIN = "20e31e5b11a7d34a692e07d96ef6366adc50df10"
BASE = "bb7333e8e6153b19de394fb3c45925c6dc948934"
SOLUTION = "Legacy.Maliev.EmployeeService.slnx"
TESTS = "Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj"
OWNER_FILTER = "FullyQualifiedName~EmployeeSignatureOwnerBoundaryHttpTests"
ADAPTED_FILTER = OWNER_FILTER + "|FullyQualifiedName~SignatureReassignment_RejectsDifferentOwnerWithoutInventedForeignKey|FullyQualifiedName~Signature_CreateQueryNamedLocationAndSameOwnerUpdate_PreserveAuthorizedMetadataWithoutCloudIo"
DEPENDENCIES = {"Defaults": "c40a7f82cea347b949444dcd7fb730f2b8dc3c0e", "Contracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}
FLOOR_KIB = 4194304
RESERVE_SECONDS = 120
SOURCE_POLICY_SHA256 = "88507d27eeee6b7b4095bb556a5c7d1fc2d1b859786d0d5ba891c5bada0be895"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def utc(value):
    if not isinstance(value, str) or not value.endswith("Z"):
        raise ValueError("Explicit UTC timestamp required")
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.utcoffset() != dt.timedelta(0):
        raise ValueError("UTC required")
    return parsed


def require(condition, message):
    if not condition:
        raise ValueError(message)


def exact_keys(value, keys):
    require(isinstance(value, dict) and set(value) == set(keys), "Unexpected or missing schema fields")


def strict_json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "Duplicate JSON field")
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON")))


def phase_plan():
    """Fixed argv; backend must supply literal cwd/env without shell interpolation."""
    rows = []
    for view in ("baseline", "candidate"):
        rows.extend([
            {"id": view + "-restore", "view": view, "argv": ["dotnet", "restore", SOLUTION], "expected": "exit-zero"},
            {"id": view + "-build", "view": view, "argv": ["dotnet", "build", SOLUTION, "--configuration", "Release", "--no-restore", "-warnaserror", "-nr:false", "-p:UseSharedCompilation=false"], "expected": "zero-warning-error"},
            {"id": view + "-focused", "view": view, "argv": ["dotnet", "test", TESTS, "--configuration", "Release", "--no-build", "--no-restore", "--filter", OWNER_FILTER if view == "baseline" else ADAPTED_FILTER, "--logger", "trx", "--results-directory", "results/" + view + "-focused"], "expected": "reviewed-baseline-observation-12" if view == "baseline" else "passed-15"},
        ])
    rows.extend([
        {"id": "candidate-suite", "view": "candidate", "argv": ["dotnet", "test", SOLUTION, "--configuration", "Release", "--no-build", "--no-restore", "--logger", "trx", "--collect", "XPlat Code Coverage", "--results-directory", "results/candidate-suite"], "expected": "passed-371"},
        {"id": "candidate-format", "view": "candidate", "argv": ["dotnet", "format", SOLUTION, "--verify-no-changes", "--no-restore"], "expected": "exit-zero"},
        {"id": "candidate-audit", "view": "candidate", "argv": ["dotnet", "list", SOLUTION, "package", "--vulnerable", "--include-transitive", "--no-restore"], "expected": "no-vulnerabilities"},
        {"id": "candidate-scaffold", "view": "candidate", "argv": ["pwsh", "-NoProfile", "-File", "tooling/Test-EmployeeScaffoldContract.ps1", "-EvidencePath", "results/employee-scaffold-orchestration.json"], "expected": "powershell-orchestration-only"},
        {"id": "candidate-secret-scan", "view": "candidate", "argv": ["gitleaks", "dir", ".", "--redact", "--exit-code", "1"], "expected": "exit-zero"},
        {"id": "candidate-coverage", "view": "candidate", "argv": ["python3", "scripts/verify-runner-coverage.py", "results/candidate-suite"], "expected": "four-assemblies-80-no-exclusions"},
    ])
    return rows


def verify_source_policy(raw):
    require(isinstance(raw, bytes) and digest(raw) == SOURCE_POLICY_SHA256, "Published source policy raw seal changed")
    policy = strict_json(raw)
    require(policy["repository"] == REPOSITORY and policy["baseCommit"] == BASE, "Employee source binding changed")
    require(policy["nativeExecutionGranted"] is False, "Source transport may not grant execution")
    require(policy["acceptedModuleSha256"] == "44a8a5accac9da11422d606be02fe28487642215df511b5f1c4284296a453ee2", "Decoder seal changed")
    require(policy["reviewedCandidateManifestSha256"] == "066067909b8bdf93955f7cd7d6be7ca4923aa84bcb4380ffaf58aa7f2edce486", "Candidate review seal changed")
    require(policy["reviewedCandidatePatchSha256"] == "3c89cdfafbee04dc54c334c51611568d69ee4bcebdea32951a8bcef57fe579db", "Candidate patch changed")
    require(policy["baselineObserverSha256"] == "2040be722b37e44819a50fe217a0ce21911a6c0c03c2a8a481cb78fa6bcf7c92", "Observer seal changed")
    require(policy["dependencyCommits"] == {"Legacy.Maliev.ServiceDefaults": DEPENDENCIES["Defaults"], "Legacy.Maliev.CompatibilityContracts": DEPENDENCIES["Contracts"]}, "Dependency pins changed")
    require(policy["forecastCases"] == {"adapted": 3, "focused": 12, "full": 371}, "Forecast contract changed")
    for view, count in (("baseline", 103), ("candidate", 103), ("dependencies", 151)):
        rows = policy["capsules"][view]["rows"]
        require(len(rows) == count and len({row["path"] for row in rows}) == count, "Frozen view cardinality changed")
    return policy


def verify_source_bytes(source_policy_raw, inventory):
    """Caller must obtain raw bytes from independently owned, non-symlink reads."""
    policy = verify_source_policy(source_policy_raw)
    rows = [row for view in ("baseline", "candidate", "dependencies") for row in policy["capsules"][view]["rows"]]
    require(set(inventory) == {row["path"] for row in rows}, "Missing or unexpected projection files")
    for row in rows:
        raw = inventory[row["path"]]
        require(isinstance(raw, bytes) and len(raw) == row["bytes"] and digest(raw) == row["sha256"], "Raw projection differs")


def phase_binding(source_policy_raw, source_head, code_sha256):
    verify_source_policy(source_policy_raw)
    require(isinstance(source_head, str) and re.fullmatch("[0-9a-f]{40}", source_head), "Observed controller source head required")
    require(isinstance(code_sha256, str) and re.fullmatch("[0-9a-f]{64}", code_sha256), "Observed controller code seal required")
    return {"repository": REPOSITORY, "controllerSourceHead": source_head, "controllerCodeSha256": code_sha256, "transportMain": TRANSPORT_MAIN, "base": BASE,
            "sourcePolicySha256": digest(source_policy_raw), "dependencyCommits": DEPENDENCIES,
            "planSha256": digest(json.dumps(phase_plan(), sort_keys=True, separators=(",", ":")).encode())}


def draft_policy(source_policy_raw, source_head, code_sha256):
    return {"binding": phase_binding(source_policy_raw, source_head, code_sha256), "rootGrantSha256": None,
            "backendQualificationSha256": None, "maxAllocationSeconds": 3600,
            "maxPhaseSeconds": 600}


def validate_admission(policy, grant_raw, observation, predecessor, phase_id, now, verify_root, verify_backend):
    """Pure gate only. Root/backend verifiers are independent, unimplemented boundaries.

    Fresh observation must be produced after authority/source/custody checks and
    immediately before execution by an independently qualified owner. This
    function cannot certify that external observations are truthful.
    """
    exact_keys(policy, {"binding", "rootGrantSha256", "backendQualificationSha256", "maxAllocationSeconds", "maxPhaseSeconds"})
    expected_binding = {"repository": REPOSITORY, "controllerSourceHead": policy["binding"].get("controllerSourceHead"), "controllerCodeSha256": policy["binding"].get("controllerCodeSha256"), "transportMain": TRANSPORT_MAIN, "base": BASE,
                        "sourcePolicySha256": SOURCE_POLICY_SHA256, "dependencyCommits": DEPENDENCIES,
                        "planSha256": digest(json.dumps(phase_plan(), sort_keys=True, separators=(",", ":")).encode())}
    require(policy["binding"] == expected_binding, "Foreign or mutable controller policy")
    require(isinstance(policy["binding"]["controllerSourceHead"], str) and re.fullmatch("[0-9a-f]{40}", policy["binding"]["controllerSourceHead"]), "Controller source head missing")
    require(isinstance(policy["binding"]["controllerCodeSha256"], str) and re.fullmatch("[0-9a-f]{64}", policy["binding"]["controllerCodeSha256"]), "Controller code seal missing")
    require(type(policy["maxAllocationSeconds"]) is int and type(policy["maxPhaseSeconds"]) is int, "Integer finite policy budgets required")
    for key in ("rootGrantSha256", "backendQualificationSha256"):
        require(isinstance(policy[key], str) and re.fullmatch("[0-9a-f]{64}", policy[key]), "Independent authority/backend pin missing")
    require(digest(grant_raw) == policy["rootGrantSha256"], "Root authority bytes differ")
    require(verify_root(grant_raw, policy["rootGrantSha256"]) is True, "Trusted Root verification failed")
    grant = strict_json(grant_raw)
    exact_keys(grant, {"schemaVersion", "binding", "runId", "attempt", "bootId", "startsUtc", "expiresUtc", "allowedPhaseIds", "phaseSeconds", "backendQualificationSha256"})
    require(type(grant["schemaVersion"]) is int and grant["schemaVersion"] == 1 and grant["binding"] == policy["binding"], "Authority scope differs")
    require(isinstance(grant["runId"], str) and grant["runId"].isdecimal() and type(grant["attempt"]) is int and grant["attempt"] > 0, "Hosted run/attempt required")
    require(isinstance(grant["bootId"], str) and re.fullmatch("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", grant["bootId"]), "Actual host boot binding required")
    start, end = utc(grant["startsUtc"]), utc(grant["expiresUtc"])
    require(start <= now < end and 0 < (end-start).total_seconds() <= policy["maxAllocationSeconds"] <= 7200, "Authority expired or excessive")
    phases = phase_plan()
    require(grant["allowedPhaseIds"] == [row["id"] for row in phases], "Fixed ordered phase plan required")
    require(type(grant["phaseSeconds"]) is int and 0 < grant["phaseSeconds"] <= policy["maxPhaseSeconds"] <= 1200, "Finite phase budget required")
    require((end-now).total_seconds() >= grant["phaseSeconds"] + RESERVE_SECONDS, "Cleanup reserve unavailable")
    require(grant["backendQualificationSha256"] == policy["backendQualificationSha256"], "Backend qualification differs")
    exact_keys(observation, {"binding", "runId", "attempt", "bootId", "checkedUtc", "freePhysicalKiB", "competingNativeProcesses", "ownedNativeProcesses", "sourceVerified", "sdk", "providers", "custodyReceiptSha256"})
    require(observation["binding"] == grant["binding"] and all(observation[key] == grant[key] for key in ("runId", "attempt", "bootId")), "Observed execution identity differs")
    require(0 <= (now-utc(observation["checkedUtc"])).total_seconds() <= 2, "Admission observation is stale")
    require(type(observation["freePhysicalKiB"]) is int and observation["freePhysicalKiB"] >= FLOOR_KIB, "Physical 4GiB floor required")
    require(observation["competingNativeProcesses"] == [] and isinstance(observation["ownedNativeProcesses"], list) and observation["sourceVerified"] is True, "Prior native work or unsealed sources")
    require(re.fullmatch("[0-9a-f]{64}", observation["custodyReceiptSha256"] or ""), "Immutable custody receipt required")
    sdk = observation["sdk"]
    exact_keys(sdk, {"owner", "pid", "birth", "executable", "unit", "invocationId", "cgroup", "expiresUtc", "memoryBytes", "swapBytes", "wholeUnitKill", "independentCustodianLive"})
    require(sdk["owner"] == grant["runId"] + ":" + str(grant["attempt"]) and type(sdk["pid"]) is int and sdk["pid"] > 0, "Exact owned SDK identity required")
    require(all(isinstance(sdk[k], str) and sdk[k] for k in ("birth", "executable", "unit", "invocationId", "cgroup")), "Retained SDK generation unavailable")
    require(type(sdk["memoryBytes"]) is int and 0 < sdk["memoryBytes"] <= 3*1024**3 and sdk["swapBytes"] == 0 and sdk["wholeUnitKill"] is True and sdk["independentCustodianLive"] is True, "SDK containment unavailable")
    require(now + dt.timedelta(seconds=grant["phaseSeconds"]+RESERVE_SECONDS) <= utc(sdk["expiresUtc"]) <= end, "SDK owner outlives allocation or lacks reserve")
    providers = observation["providers"]
    require(isinstance(providers, list) and len(providers) <= 256, "Bounded actual provider registry required")
    for provider in providers:
        exact_keys(provider, {"kind", "id", "createdRaw", "imageId", "specSha256", "owner", "expiresUtc", "disposable", "persistentData", "loopbackOnly", "cleanupCustodianLive"})
        require(all(re.fullmatch("[0-9a-f]{64}", provider[k]) for k in ("id", "imageId", "specSha256")), "Full provider identity/spec required")
        require(provider["owner"] == sdk["owner"] and provider["disposable"] is True and provider["persistentData"] is False and provider["loopbackOnly"] is True and provider["cleanupCustodianLive"] is True, "Foreign/uncontained provider")
        require(isinstance(provider["createdRaw"], str) and provider["createdRaw"] and now+dt.timedelta(seconds=grant["phaseSeconds"]+RESERVE_SECONDS) <= utc(provider["expiresUtc"]) <= end, "Provider finite custody unavailable")
    require(len({p["id"] for p in providers}) == len(providers), "Provider identity reused")
    require(verify_backend(observation, policy["backendQualificationSha256"]) is True, "Independent actual backend proof unavailable")
    index = next((i for i, row in enumerate(phases) if row["id"] == phase_id), None)
    require(index is not None, "Unknown phase")
    if index == 0:
        require(predecessor is None, "Unexpected prior phase")
    else:
        exact_keys(predecessor, {"phaseId", "binding", "runId", "attempt", "bootId", "completedUtc", "terminal", "cleanupVerified", "remainingProcesses", "remainingProviders", "receiptSha256", "outcomeReviewed"})
        require(predecessor["phaseId"] == phases[index-1]["id"] and predecessor["binding"] == grant["binding"] and all(predecessor[k] == grant[k] for k in ("runId", "attempt", "bootId")), "Wrong predecessor")
        require(start <= utc(predecessor["completedUtc"]) <= utc(observation["checkedUtc"]), "Predecessor observation order invalid")
        require(predecessor["terminal"] is True and predecessor["cleanupVerified"] is True and predecessor["remainingProcesses"] == [] and predecessor["remainingProviders"] == [] and predecessor["outcomeReviewed"] is True, "Actual predecessor retirement/outcome required")
        require(re.fullmatch("[0-9a-f]{64}", predecessor["receiptSha256"] or ""), "Predecessor immutable receipt missing")
    return {"phase": phases[index], "deadlineUtc": min(end-dt.timedelta(seconds=RESERVE_SECONDS), now+dt.timedelta(seconds=grant["phaseSeconds"])).isoformat(), "executionGrantedByThisModule": False}
