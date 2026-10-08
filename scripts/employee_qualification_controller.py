"""Executable Employee sealed-source preflight; native admission remains closed."""
import argparse
import datetime as dt
import hashlib
import json
import os
import re
from pathlib import Path
import employee_phase_contract as contract
import employee_source_intake as intake


class QualificationRefused(ValueError):
    pass


def require_runtime_connection(authority_raw=None):
    # No SDK/provider implementation or authority is fabricated from source
    # approval. This boundary must change only in a separately reviewed successor.
    if authority_raw is None:
        raise QualificationRefused("Independent Employee Root authority is not pinned")
    raise QualificationRefused("Independent Employee SDK/provider backend qualification is not pinned")


def read_inventory(root, helper):
    root = Path(root)
    helper.reject_links(root)
    inventory = {}
    metadata = {".employee-source-owner", "intake-receipt.json"}
    for path in sorted(root.rglob("*")):
        helper.reject_links(path)
        if path.is_dir():
            continue
        if not path.is_file():
            raise ValueError("Nonregular source projection entry")
        relative = path.relative_to(root).as_posix()
        if relative not in metadata:
            inventory[relative] = path.read_bytes()
    return inventory


def controller_observation(scripts, proc_root=Path("/proc"), platform=None):
    platform = os.name if platform is None else platform
    names = ("employee_qualification_controller.py", "employee_phase_contract.py",
             "employee_source_intake.py", "sealed_source_capsule.py", "employee-source-policy.json")
    seals = {name: hashlib.sha256((Path(scripts)/name).read_bytes()).hexdigest() for name in names}
    code_sha = hashlib.sha256(json.dumps(seals, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    head = os.environ.get("GITHUB_SHA")
    if head is not None and (len(head) != 40 or any(c not in "0123456789abcdef" for c in head)):
        raise ValueError("Observed source head is noncanonical")
    boot_id = None
    resource = {"platform": platform, "freePhysicalKiB": None, "nativeProcesses": None,
                "physicalFloorEstimateSatisfied": False, "candidateNameScanComplete": False,
                "nativeAdmissionQualified": False, "censusScope": "best-effort-proc-comm-name-candidates"}
    if platform == "posix" and proc_root.is_dir():
        try:
            boot_id = (proc_root/"sys/kernel/random/boot_id").read_text().strip()
            if not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", boot_id):
                raise ValueError("Noncanonical observed boot ID")
            memory = (proc_root/"meminfo").read_text()
            for line in memory.splitlines():
                if line.startswith("MemFree:"):
                    resource["freePhysicalKiB"] = int(line.split()[1])
                if line.startswith("MemAvailable:"):
                    resource["availableIncludingReclaimableKiB"] = int(line.split()[1])
            entries = list(proc_root.iterdir())
            if len(entries) > 8192:
                raise ValueError("Process census bound exceeded")
            native = []
            for entry in entries:
                if not entry.name.isdecimal():
                    continue
                try:
                    name = (entry/"comm").read_text().strip()
                    if name in {"dotnet", "testhost", "MSBuild", "VBCSCompiler", "datacollector", "csc", "vstest", "docker", "dockerd", "containerd", "postgres", "redis-server"}:
                        stat = (entry/"stat").read_text().rsplit(")", 1)[1].split()
                        native.append({"pid": int(entry.name), "birthTicks": stat[19], "executableName": name})
                except FileNotFoundError:
                    continue
            resource.update(nativeProcesses=native, candidateNameScanComplete=True,
                            physicalFloorEstimateSatisfied=type(resource["freePhysicalKiB"]) is int and resource["freePhysicalKiB"] >= contract.FLOOR_KIB)
        except (OSError, ValueError, IndexError) as error:
            resource["observationErrorType"] = type(error).__name__
    return {"observedUtc": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"),
            "actualLinuxBootId": boot_id, "allocationReusableForNative": False, "sourceHead": head, "codeSeals": seals, "controllerCodeSha256": code_sha,
            "runId": os.environ.get("GITHUB_RUN_ID"), "attempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
            "resources": resource, "nativeExecutionGranted": False}


def preflight(scripts, root, owner, fetch=False):
    """Concrete local raw-capsule custody, readback and exact-owner release."""
    scripts = Path(scripts)
    helper = intake.accepted_helper()
    raw = (scripts / "employee-source-policy.json").read_bytes()
    policy = intake.load_policy(raw, helper)
    capsules = {view: helper.fetch_git_blob(intake.REPOSITORY, policy["capsules"][view]["oid"])
                if fetch else (scripts / "employee-source-packet" / (view + ".zip")).read_bytes()
                for view in intake.COUNTS}
    # Check all bytes before creating ownership. A failed fresh-root validation
    # never grants permission to remove a pre-existing/foreign projection.
    intake.validate_inputs(raw, capsules, helper)
    root = Path(root)
    if root.exists() or root.is_symlink():
        raise ValueError("Preflight requires absent source root")
    acquired = False
    first_error = None
    result = None
    try:
        try:
            receipt = intake.materialize(raw, capsules, root, owner, helper)
        finally:
            # materialize may fail after its owner marker is durable. Retain
            # exact marker custody so that partial materialization is released.
            marker = root / ".employee-source-owner"
            if root.is_dir() and not root.is_symlink() and marker.is_file() and not marker.is_symlink():
                acquired = marker.read_bytes() == owner.encode()
        inventory = read_inventory(root, helper)
        contract.verify_source_bytes(raw, inventory)
        observation = controller_observation(scripts)
        # A local source preflight may honestly have no hosted head; it cannot
        # form any native binding in that case.
        draft = contract.draft_policy(raw, observation["sourceHead"], observation["controllerCodeSha256"]) if observation["sourceHead"] else None
        result = {"sourceOnly": True, "nativeExecutionGranted": False,
                  "actualNativeCases": 0, "backendQualified": False,
                  "materialization": receipt, "rawFilesVerified": len(inventory),
                  "fixedPhases": contract.phase_plan(), "draftAdmission": draft,
                  "controllerObservation": observation, "sourceInventoryStage": "immutable-before-generated-output"}
    except BaseException as error:
        first_error = error
    finally:
        if acquired:
            try:
                cleanup = intake.release(root, owner, helper)
                if result is not None:
                    result["sourceCleanup"] = cleanup
            except BaseException as cleanup_error:
                if first_error is not None:
                    raise QualificationRefused("Source preflight failed and owned cleanup could not be verified") from cleanup_error
                raise
    if first_error is not None:
        raise first_error
    return result


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", required=True, choices=("preflight-only", "qualification"))
    parser.add_argument("--evidence", required=True, type=Path)
    args = parser.parse_args(argv)
    if args.mode == "qualification":
        require_runtime_connection()
    parent = Path(os.environ["RUNNER_TEMP"])
    if not parent.is_absolute() or not parent.is_dir():
        raise ValueError("Existing absolute runner temp required")
    owner = os.environ["EMPLOYEE_SOURCE_OWNER"]
    evidence = args.evidence
    helper = intake.accepted_helper()
    helper.reject_links(evidence)
    if not evidence.is_absolute() or parent not in evidence.parents:
        raise ValueError("Evidence must be within runner temp")
    evidence.mkdir(mode=0o700, exist_ok=True)
    result = preflight(Path(__file__).resolve().parent, parent / "employee-owner-source", owner,
                       fetch=os.environ.get("GITHUB_ACTIONS") == "true")
    helper.write_new(evidence, "source-observation.json", (json.dumps(result, sort_keys=True)+"\n").encode())
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
