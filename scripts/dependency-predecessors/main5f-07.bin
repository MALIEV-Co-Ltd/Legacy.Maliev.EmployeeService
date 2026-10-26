"""Authenticate the reviewed action-only source profile; grants remain false."""
import hashlib
import json
import os
from pathlib import Path
import stat

PROFILE = "employee-artifact-v7.0.2-20261026"
FIELD = "artifactTransportSuccessor"
METADATA = "scripts/employee-metadata-source-successor.json"
PAGINATION = "scripts/employee-pagination-focused-manifest.json"
WORKFLOW_TEST = "Legacy.Maliev.EmployeeService.Tests/Workflows/WorkflowContractTests.cs"
ARCHIVES = {
    METADATA: ("scripts/transport-predecessors/employee-metadata-before-artifact7.json", 166836,
               "8a3e3b4023fa9c7b3c30b7ab592b34bded58a3cadca1c7ec2f90ebae967ffbe4",
               "25c8cdfc529260865733c371eec24a54cdc65db7"),
    PAGINATION: ("scripts/transport-predecessors/employee-pagination-before-artifact7.json", 76414,
                 "426ad58ed350a3f8e84a8de737b783b39306b2d2c443137fbc20e163c04cdddf",
                 "d856be3d5bc42e7d122208884aca51a159f9345c"),
    WORKFLOW_TEST: ("scripts/transport-predecessors/workflow-contract-before-artifact7.cs.txt", 22225,
                    "b52d36f9fc6784d9e6ab767c5639e71d018a11a2561d4423c0a3894fbe4f84ca",
                    "2c3fbba3b8f251e561f298ad80bac4c1a40ac9cf"),
}
MODULE = "scripts/employee_artifact_transport_profile.py"
ADDED = {MODULE, *(row[0] for row in ARCHIVES.values())}
TRANSPORT = {
    ".github/workflows/_build-and-test.yml",
    ".github/workflows/employee-address-input-focused.yml",
    ".github/workflows/employee-authority-hosted-preflight.yml",
    ".github/workflows/employee-metadata-focused.yml",
    ".github/workflows/employee-owned-receipts.yml",
    ".github/workflows/employee-pagination-focused.yml",
    ".github/workflows/employee-qualification-controller.yml",
    ".github/workflows/employee-source-intake.yml", WORKFLOW_TEST,
}
METADATA_CHANGED = TRANSPORT | {
    PAGINATION, "scripts/verify_employee_metadata_proof.py", "scripts/test_employee_metadata_proof.py",
    "scripts/verify_employee_pagination_focused.py", "scripts/test_employee_pagination_focused_validator.py",
}
PAGINATION_CHANGED = {
    ".github/workflows/_build-and-test.yml", ".github/workflows/employee-pagination-focused.yml", WORKFLOW_TEST,
    "scripts/verify_employee_pagination_focused.py", "scripts/test_employee_pagination_focused_validator.py",
}


def require(value, reason):
    if not value:
        raise ValueError("Transport profile: " + reason)


def blob(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()


def exact(left, right):
    """Compare JSON values without Python's bool/int or int/float equivalence."""
    encode = lambda value: json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)
    return encode(left) == encode(right)


def read(root, name):
    require(isinstance(name, str) and all(p not in ("", ".", "..") for p in name.split("/"))
            and "\\" not in name and ":" not in name, "unsafe relative path")
    root = Path(root)
    path = root / name
    for node in (path, *path.parents):
        if node == root.parent:
            break
        require(not node.is_symlink() and not (hasattr(node, "is_junction") and node.is_junction()), "linked source")
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1 and 0 < before.st_size <= 1048576, "bounded regular source required")
    identity = lambda row: (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns)
    with path.open("rb") as stream:
        require(identity(os.fstat(stream.fileno())) == identity(before), "source replaced before read")
        raw = stream.read(1048577)
        require(identity(os.fstat(stream.fileno())) == identity(before), "source changed during read")
    require(len(raw) == before.st_size and identity(path.lstat()) == identity(before), "source changed after read")
    return raw


def object_json(raw):
    def unique(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key")
            result[key] = value
        return result
    return json.loads(raw.decode("utf-8"), object_pairs_hook=unique,
                      parse_constant=lambda value: require(False, "nonfinite JSON value"))


def predecessor_bytes(root, source):
    path, length, digest, git_blob = ARCHIVES[source]
    raw = read(root, path)
    require(len(raw) == length and hashlib.sha256(raw).hexdigest() == digest and blob(raw) == git_blob,
            "immutable predecessor differs")
    return raw


def predecessor(root, source):
    return object_json(predecessor_bytes(root, source))


def rows_by_path(rows, digest_key):
    require(isinstance(rows, list), "binding array required")
    result = {}
    for row in rows:
        require(isinstance(row, dict) and set(row) == {"path", "bytes", digest_key}, "binding shape differs")
        require(isinstance(row["path"], str) and row["path"] not in result, "duplicate binding")
        require(type(row["bytes"]) is int and row["bytes"] > 0, "binding length differs")
        result[row["path"]] = row
    return result


def authenticate(root, current, source, row_key, digest_key, changed):
    require(isinstance(current, dict), "manifest object required")
    profile = current.get(FIELD)
    require(isinstance(profile, dict) and profile.get("profile") == PROFILE, "reviewed profile required")
    require(set(profile) == {"profile", "predecessor", "changedBindings", "addedBindings", "independentReviewScopeSha256"}, "profile fields differ")
    previous = predecessor(root, source)
    require(set(current) == set(previous) | {FIELD}, "manifest fields differ")
    for key, value in previous.items():
        if key != row_key:
            require(exact(current[key], value), "historical field changed: " + key)
    path, length, digest, git_blob = ARCHIVES[source]
    require(exact(profile["predecessor"], {"source": source, "archivePath": path,
            "commit": "c36fb17585dd1d5f3a59f06636ec377fe8c60556", "bytes": length,
            "sha256": digest, "gitBlob": git_blob}), "predecessor identity differs")
    old = rows_by_path(previous[row_key], digest_key)
    new = rows_by_path(current[row_key], digest_key)
    require(set(new) == set(old) | ADDED, "binding set differs")
    require({name for name in old if old[name] != new[name]} == changed, "undeclared or missing changed binding")
    expected_changes = [{"path": name, "before": old[name], "after": new[name]} for name in sorted(changed)]
    expected_added = [new[name] for name in sorted(ADDED)]
    require(exact(profile["changedBindings"], expected_changes) and exact(profile["addedBindings"], expected_added), "declared delta differs")
    scope = {key: value for key, value in profile.items() if key != "independentReviewScopeSha256"}
    require(profile["independentReviewScopeSha256"] == hashlib.sha256(
        json.dumps(scope, sort_keys=True, separators=(",", ":")).encode()).hexdigest(), "review scope differs")
    for name in changed | ADDED:
        raw = read(root, name)
        actual = blob(raw) if digest_key == "blob" else hashlib.sha256(raw).hexdigest()
        require(new[name]["bytes"] == len(raw) and new[name][digest_key] == actual, "current source binding differs: " + name)
    for source_name in ARCHIVES:
        predecessor_bytes(root, source_name)
    return previous


def authenticate_metadata(root, manifest):
    previous = authenticate(root, manifest, METADATA, "candidate", "blob", METADATA_CHANGED)
    require(exact(manifest["grants"], {"executionGranted": False, "sdkGranted": False, "runtimeQualified": False}), "source-only grants required")
    original = rows_by_path(previous["candidate"], "blob")
    old_action = b"043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
    new_action = b"cf430e030ddbb5b0abf93d22962f4752f3646cd9"
    for name in TRANSPORT - {WORKFLOW_TEST}:
        raw = read(root, name)
        require(new_action in raw and old_action not in raw, "every artifact site must use the current immutable action")
        inverse = raw.replace(new_action, old_action).replace(b"# v7.0.2", b"# v7.0.1")
        require(len(inverse) == original[name]["bytes"] and blob(inverse) == original[name]["blob"],
                "workflow changes exceed exact artifact transport: " + name)
    historical = predecessor_bytes(root, WORKFLOW_TEST)
    anchor = b"        WorkflowContractValidator.Validate(Workflow);\n"
    require(historical.count(anchor) == 1, "historical workflow-test anchor differs")
    expected = historical.replace(old_action, new_action)
    rejection = b'        AssertMutationRejected("actions/upload-artifact@' + new_action + b'", "actions/upload-artifact@' + old_action + b'");\n'
    expected = expected.replace(anchor, anchor + rejection)
    require(read(root, WORKFLOW_TEST) == expected, "workflow test changes exceed strict action pin and previous-pin rejection")
    return previous


def authenticate_pagination(root, manifest):
    previous = authenticate(root, manifest, PAGINATION, "codePins", "sha256", PAGINATION_CHANGED)
    metadata = object_json(read(root, METADATA))
    authenticate_metadata(root, metadata)
    binding = rows_by_path(metadata["candidate"], "blob")[PAGINATION]
    raw = read(root, PAGINATION)
    require(binding["bytes"] == len(raw) and binding["blob"] == blob(raw), "mixed pagination and metadata profiles")
    return previous
