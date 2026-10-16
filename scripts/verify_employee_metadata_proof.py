"""Bounded ordinary-CI #77 evidence. Never invokes SDK, subprocesses or sealed custody."""
from __future__ import annotations
import collections
import hashlib
import json
import itertools
import os
from pathlib import Path
import re
import stat
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ROSTER = ROOT / "scripts/employee-metadata-roster.json"
CUSTODY = ROOT / "scripts/employee-metadata-source-successor.json"
URI = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
NS = {"t": URI}
FILE_CAP = 4 * 1024 * 1024
AGGREGATE_CAP = 12 * 1024 * 1024
COUNTERS = ("total", "executed", "passed", "failed", "error", "timeout", "aborted",
            "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted",
            "disconnected", "warning", "completed", "inProgress", "pending")
GRANTS = {"executionGranted": False, "sdkGranted": False, "runtimeQualified": False}

def need(value, message):
    if not value:
        raise ValueError(message)

def bounded_read(path, cap=FILE_CAP):
    """Reject size/type/link before allocation and detect replacement during read."""
    path = Path(path)
    before = path.lstat()
    need(stat.S_ISREG(before.st_mode) and not path.is_symlink(), "Regular non-link input required")
    need(0 < before.st_size <= cap, "Input byte bound")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as stream:
        opened = os.fstat(stream.fileno())
        identity = lambda item: (item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns)
        need(identity(opened) == identity(before), "Input replaced before open")
        data = stream.read(cap + 1)
        need(identity(os.fstat(stream.fileno())) == identity(opened), "Input changed during read")
    need(len(data) == before.st_size and len(data) <= cap, "Input read bound")
    need(identity(path.lstat()) == identity(before), "Input path changed during read")
    return data

def object_json(data):
    def unique(items):
        result = {}
        for key, value in items:
            need(key not in result, "Duplicate JSON key")
            result[key] = value
        return result
    return json.loads(data.decode("utf-8", errors="strict"), object_pairs_hook=unique,
                      parse_constant=lambda value: need(False, "Nonfinite JSON number"))

def git_blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()

def relative_file(root, name):
    need(isinstance(name, str) and "\\" not in name and ":" not in name, "Relative path required")
    parts = name.split("/")
    need(all(part not in ("", ".", "..") for part in parts), "Unsafe source path")
    current = Path(root)
    for part in parts:
        current = current / part
        need(not current.is_symlink(), "Source path link refused")
    return current

def source_inventory(root, rows, excluded=()):
    """All tracked source bytes; generated outputs are excluded only after build."""
    expected = {row["path"]: row for row in rows}
    need(len(expected) == len(rows), "Duplicate source binding")
    budget = 0
    for name, row in expected.items():
        data = bounded_read(relative_file(root, name))
        budget += len(data)
        need(budget <= AGGREGATE_CAP, "Source aggregate byte bound")
        need(len(data) == row["bytes"] and git_blob(data) == row["blob"], "Source binding drift: " + name)
    actual = set()
    for directory, dirs, names in os.walk(root, followlinks=False):
        rel = Path(directory).relative_to(root)
        for name in dirs + names:
            need(not (Path(directory) / name).is_symlink(), "Unexpected source link")
        dirs[:] = [name for name in dirs if (rel / name).as_posix() not in excluded]
        actual.update((rel / name).as_posix() for name in names)
    need(actual == set(expected) | set(excluded).intersection(actual), "Unexpected or missing source path")
    return budget

def verify_source(root=ROOT, baseline=None):
    manifest_data = bounded_read(CUSTODY)
    manifest = object_json(manifest_data)
    need(manifest["schemaVersion"] == 1 and manifest["grants"] == GRANTS, "Source-only successor required")
    own = CUSTODY.relative_to(ROOT).as_posix()
    # The enclosing immutable packet anchors this manifest, avoiding self-hash cycles.
    excluded = (".git", ".dependencies", ".metadata-baseline", "runner-metadata", own)
    count = source_inventory(root, manifest["candidate"], excluded)
    if baseline is not None:
        source_inventory(baseline, manifest["baseline"], (".git",))
    return {"kind": "ordinary-source-only", "sourceBytes": count,
            "baseCommit": manifest["baseCommit"], "featureTree": manifest["featureTree"],
            "successorSha256": hashlib.sha256(manifest_data).hexdigest(), **GRANTS}

def prepare_baseline(destination):
    """Copy only five authored test files and test-only package project."""
    destination = Path(destination).resolve()
    need(destination == ROOT / ".metadata-baseline", "Exact disposable baseline path required")
    verify_source(baseline=destination)
    manifest = object_json(bounded_read(CUSTODY))
    original = {row["path"]: row for row in manifest["baseline"]}
    candidate = {row["path"]: row for row in manifest["candidate"]}
    copy_paths = manifest["baselineTestOverlay"]
    need(len(copy_paths) == 6 and len(set(copy_paths)) == 6, "Exact test overlay required")
    for name in copy_paths:
        need(name.startswith("Legacy.Maliev.EmployeeService.Tests/"), "Production overlay refused")
        target = relative_file(destination, name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(bounded_read(relative_file(ROOT, name)))
    expected = dict(original)
    for name in copy_paths:
        expected[name] = candidate[name]
    source_inventory(destination, list(expected.values()), (".git",))
    need(not (destination / manifest["productionHelper"]).exists(), "Baseline converter must be absent")
    return {"kind": "tests-only-baseline", "sourcePaths": len(expected), "overlayPaths": copy_paths,
            "baseCommit": manifest["baseCommit"], **GRANTS}

def generated_outputs(manifest):
    """Only the five checked-in projects' exact bin/obj directories are disposable."""
    project_roots = {row["path"].rsplit("/", 1)[0] for row in manifest["candidate"]
                     if row["path"].endswith(".csproj")}
    expected = sorted(root + "/" + leaf for root in project_roots for leaf in ("bin", "obj"))
    declared = manifest["generatedOutputDirectories"]
    need(len(project_roots) == 5 and declared == expected, "Exact ten generated output directories")
    return tuple(declared)

def verify_final_source():
    """Always-run read-only check; compare both trees to pre-build provenance."""
    manifest_data = bounded_read(CUSTODY)
    manifest = object_json(manifest_data)
    need(manifest["schemaVersion"] == 1 and manifest["grants"] == GRANTS, "Source-only successor required")
    own = CUSTODY.relative_to(ROOT).as_posix()
    outputs = generated_outputs(manifest)
    before = object_json(bounded_read(ROOT / "runner-metadata/source.json"))
    baseline_before = object_json(bounded_read(ROOT / "runner-metadata/baseline-source.json"))
    need(before["successorSha256"] == hashlib.sha256(manifest_data).hexdigest()
         and before["baseCommit"] == manifest["baseCommit"]
         and before["featureTree"] == manifest["featureTree"]
         and all(before[key] is value for key, value in GRANTS.items()), "Initial source provenance changed")
    copy_paths = manifest["baselineTestOverlay"]
    need(len(copy_paths) == 6 and len(set(copy_paths)) == 6
         and all(name.startswith("Legacy.Maliev.EmployeeService.Tests/") for name in copy_paths),
         "Exact tests-only baseline overlay")
    need(baseline_before["kind"] == "tests-only-baseline"
         and baseline_before["baseCommit"] == manifest["baseCommit"]
         and baseline_before["overlayPaths"] == copy_paths
         and all(baseline_before[key] is value for key, value in GRANTS.items()), "Initial baseline provenance changed")
    excluded = (".git", ".dependencies", ".metadata-baseline", "runner-metadata", own) + outputs
    candidate_bytes = source_inventory(ROOT, manifest["candidate"], excluded)
    candidate = {row["path"]: row for row in manifest["candidate"]}
    baseline_rows = {row["path"]: row for row in manifest["baseline"]}
    for name in copy_paths:
        baseline_rows[name] = candidate[name]
    need(baseline_before["sourcePaths"] == len(baseline_rows), "Baseline source cardinality changed")
    baseline_root = relative_file(ROOT, ".metadata-baseline")
    need(baseline_root.is_dir(), "Baseline directory missing")
    baseline_bytes = source_inventory(baseline_root, list(baseline_rows.values()), (".git",) + outputs)
    need(not (baseline_root / manifest["productionHelper"]).exists(), "Native baseline helper must remain absent")
    return {"kind": "final-ordinary-source-only", "candidateBytes": candidate_bytes,
            "baselineBytes": baseline_bytes, "successorSha256": before["successorSha256"],
            "baselineProgram": "exact original binding", "baselineOverlayPaths": len(copy_paths),
            "baselineHelper": "absent", "declaredOutputDirectories": list(outputs), **GRANTS}

def guid(value):
    need(isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", value), "Canonical GUID required")
    need(str(uuid.UUID(value)) == value.lower(), "Canonical GUID required")
    return value.lower()

def one(parent, name):
    items = parent.findall("t:" + name, NS)
    need(len(items) == 1, "Exactly one " + name + " required")
    return items[0]

def select(roster, phase):
    need(phase in ("reader", "baseline", "candidate"), "Unknown phase")
    rows = roster["rows"]
    need(len(rows) == 442 and len({row["name"] for row in rows}) == 442, "Exact 442 forecast roster required")
    need(len(roster["methods"]) == 18, "Exact 18 methods required")
    if phase == "baseline":
        selected = roster["baselineRed"]
        need(len(selected) == 176 and len({row["r"] for row in selected}) == 176, "Exact baseline roster")
        need(collections.Counter(row["forecast"] for row in selected) == {"Passed": 88, "Failed": 88}, "Exact baseline classifications")
        return [(rows[item["r"]], item["forecast"]) for item in selected]
    indices = roster["groups"]["publicReader"] if phase == "reader" else list(range(442))
    need(len(indices) == (119 if phase == "reader" else 442) and len(set(indices)) == len(indices), "Exact phase roster")
    return [(rows[index], "Passed") for index in indices]

def baseline_failure(result, row, method):
    error = one(one(result, "Output"), "ErrorInfo")
    message = one(error, "Message").text or ""
    stack = one(error, "StackTrace").text or ""
    need(method["class"] + "." + method["method"] in stack, "Failure must reach selected test method")
    field = row["a"][0]
    need(field in ("Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "Bucket", "ObjectName"), "Exact consumed field")
    if method["method"] == "ConsumedField_MatchesPublicTypedReader":
        need(row["a"][1] in ("integer", "decimal", "exponent", "negative_zero", "capital_exponent",
                             "large_integer", "large_exponent", "true", "false"), "Primitive RED category")
        need(re.match(r"\ASystem\.Text\.Json\.JsonException\s*:", message), "Native JSON conversion failure required")
        need("The JSON value could not be converted to " in message and "Path: $." + field + " |" in message,
             "Native consumed-field failure path required")
        return "native-consumed-primitive-path"
    need(method["method"] == "OccurrenceOrder_MatchesNullIgnoringProjection" and row["a"][1] in ("null_after", "repeat_null"),
         "Ordered null RED category")
    need(message.startswith("Assert.Equal() Failure:") and 'Expected: "first"' in message
         and re.search(r"Actual:\s+null(?:\s|$)", message), "Exact ordered-null assertion failure required")
    return "ordered-null-overwrite"

def exact_locations(root, count, phase):
    """Every critical element in the bounded document must occupy its schema path."""
    def exact(name, allowed):
        actual = list(root.iter("{" + URI + "}" + name))
        need(len(actual) == len(allowed) and set(actual) == set(allowed),
             "Global count/location mismatch: " + name)

    results = one(root, "Results")
    definitions = one(root, "TestDefinitions")
    entries = one(root, "TestEntries")
    summary = one(root, "ResultSummary")
    counters = one(summary, "Counters")
    rows, tests, joins = list(results), list(definitions), list(entries)
    need(len(rows) == len(tests) == len(joins) == count, "Exact global evidence cardinality")
    for name, container in (("TestRun", root), ("Results", results),
                            ("TestDefinitions", definitions), ("TestEntries", entries),
                            ("ResultSummary", summary), ("Counters", counters)):
        exact(name, [container])
    for name, elements in (("UnitTestResult", rows), ("UnitTest", tests), ("TestEntry", joins)):
        need(all(element.tag == "{" + URI + "}" + name for element in elements), "Exact evidence child type")
        exact(name, elements)
    exact("Execution", [one(test, "Execution") for test in tests])
    exact("TestMethod", [one(test, "TestMethod") for test in tests])

    run_infos = summary.findall("t:RunInfos", NS)
    need(len(run_infos) <= 1, "At most one direct summary RunInfos")
    exact("RunInfos", run_infos)
    infos = list(run_infos[0]) if run_infos else []
    need(all(info.tag == "{" + URI + "}RunInfo" for info in infos), "Only direct RunInfo children")
    exact("RunInfo", infos)

    errors = []
    for row in rows:
        outputs = row.findall("t:Output", NS)
        need(len(outputs) <= 1, "At most one direct result Output")
        if row.get("outcome") == "Failed":
            need(phase == "baseline" and len(outputs) == 1, "Only baseline failure output accepted")
            errors.append(one(outputs[0], "ErrorInfo"))
    need(len(errors) == (88 if phase == "baseline" else 0), "Exact global baseline errors")
    exact("ErrorInfo", errors)
    return summary, counters, infos

def validate_trx(data, roster, phase):
    need(isinstance(data, bytes) and 0 < len(data) <= FILE_CAP, "TRX byte bound")
    text = data.decode("utf-8", errors="strict")
    need("<!DOCTYPE" not in text.upper() and "<!ENTITY" not in text.upper(), "DTD/entities forbidden")
    root = ET.fromstring(text)
    need(root.tag == "{" + URI + "}TestRun", "Exact TRX namespace/root required")
    need(sum(1 for _ in root.iter()) <= 30000, "TRX element bound")
    need(all(element.tag.startswith("{" + URI + "}") for element in root.iter()), "Foreign XML namespace")
    expected = {row["name"]: (row, outcome) for row, outcome in select(roster, phase)}
    summary, counters, infos = exact_locations(root, len(expected), phase)
    results = list(one(root, "Results"))
    definitions = list(one(root, "TestDefinitions"))
    entries = list(one(root, "TestEntries"))
    need(len(results) == len(definitions) == len(entries) == len(expected), "Exact result/definition/entry cardinality")
    by_id = {}
    definition_names = set()
    execution_ids = set()
    for definition in definitions:
        need(definition.tag == "{" + URI + "}UnitTest", "Only UnitTest definitions")
        test_id = guid(definition.get("id"))
        name = definition.get("name")
        need(test_id not in by_id and name in expected and name not in definition_names, "Unique selected definitions")
        execution = guid(one(definition, "Execution").get("id"))
        need(execution not in execution_ids, "Unique definition execution")
        method = one(definition, "TestMethod")
        planned = roster["methods"][expected[name][0]["m"]]
        need(method.get("className") == planned["class"] and method.get("name") == planned["method"], "Exact class/method binding")
        need(Path(method.get("codeBase", "")).name.lower() == "legacy.maliev.employeeservice.tests.dll"
             and Path(definition.get("storage", "")).name.lower() == "legacy.maliev.employeeservice.tests.dll", "Exact test assembly")
        by_id[test_id] = (name, execution)
        definition_names.add(name)
        execution_ids.add(execution)
    pairs = set()
    entry_pairs = set()
    for entry in entries:
        need(entry.tag == "{" + URI + "}TestEntry", "Only TestEntry entries")
        pair = (guid(entry.get("testId")), guid(entry.get("executionId")))
        need(pair not in entry_pairs and pair[0] in by_id and by_id[pair[0]][1] == pair[1], "Exact unique entry join")
        entry_pairs.add(pair)
    outcomes = collections.Counter()
    categories = collections.Counter()
    for result in results:
        need(result.tag == "{" + URI + "}UnitTestResult", "Only UnitTestResult results")
        pair = (guid(result.get("testId")), guid(result.get("executionId")))
        need(pair in entry_pairs and pair not in pairs, "Exact unique result join")
        name, execution = by_id[pair[0]]
        need(pair[1] == execution and result.get("testName") == name, "Result identity join")
        row, required = expected[name]
        outcome = result.get("outcome")
        need(outcome == required, "Unexpected per-row outcome")
        errors = result.findall(".//t:ErrorInfo", NS)
        if required == "Failed":
            need(phase == "baseline" and len(errors) == 1, "Only classified baseline failures accepted")
            categories[baseline_failure(result, row, roster["methods"][row["m"]])] += 1
        else:
            need(not errors, "Passed result contains error")
        pairs.add(pair)
        outcomes[outcome] += 1
    need(pairs == entry_pairs and len(pairs) == len(by_id), "Complete three-way join")
    wanted_summary = "Failed" if phase == "baseline" else "Completed"
    need(summary.get("outcome") in (("Failed",) if wanted_summary == "Failed" else ("Passed", "Completed")), "Summary outcome")
    need(set(counters.attrib) == set(COUNTERS), "Closed typed counter schema")
    values = {}
    for name in COUNTERS:
        raw = counters.get(name)
        need(re.fullmatch(r"0|[1-9][0-9]*", raw or ""), "Canonical integer counter")
        values[name] = int(raw)
    wanted = {name: 0 for name in COUNTERS}
    wanted.update(total=len(expected), executed=len(expected), passed=outcomes["Passed"], failed=outcomes["Failed"])
    need(values == wanted, "Contradictory summary counters")
    for info in infos:
        need(info.get("outcome") in ("Passed", "Completed", "Information"), "Infrastructure RunInfo refused")
    need(not summary.findall(".//t:ErrorInfo", NS), "Infrastructure summary error refused")
    allowed_errors = 88 if phase == "baseline" else 0
    need(len(root.findall(".//t:ErrorInfo", NS)) == allowed_errors, "Unowned error refused")
    if phase == "baseline":
        need(categories == {"native-consumed-primitive-path": 72, "ordered-null-overwrite": 16}, "Exact RED categories")
    return {"phase": phase, "selected": len(expected), "passed": outcomes["Passed"], "failed": outcomes["Failed"],
            "categories": dict(categories), "trxSha256": hashlib.sha256(data).hexdigest(),
            "measured": True, **GRANTS}

def verify_phase(phase, directory, exit_code):
    path = Path(directory)
    need(path.is_dir() and not path.is_symlink(), "Evidence directory required")
    # Inspect every entry before reading; never glob+read an unbounded artifact set.
    paths = list(itertools.islice(path.iterdir(), 2))
    need(len(paths) == 1 and paths[0].suffix == ".trx", "Exactly one separate TRX required")
    size = paths[0].lstat().st_size
    need(0 < size <= FILE_CAP and size <= AGGREGATE_CAP, "Artifact aggregate byte bound")
    need(exit_code == (1 if phase == "baseline" else 0), "Exact test command exit code")
    roster = object_json(bounded_read(ROSTER))
    return validate_trx(bounded_read(paths[0]), roster, phase)

def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "source" and len(sys.argv) == 2:
        result = verify_source()
    elif mode == "final-source" and len(sys.argv) == 2:
        result = verify_final_source()
    elif mode == "prepare-baseline" and len(sys.argv) == 3:
        result = prepare_baseline(sys.argv[2])
    elif mode == "verify" and len(sys.argv) == 5:
        result = verify_phase(sys.argv[2], sys.argv[3], int(sys.argv[4]))
    else:
        raise ValueError("Expected source | final-source | prepare-baseline PATH | verify PHASE DIRECTORY EXIT")
    print(json.dumps(result, sort_keys=True))

if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, UnicodeError, ET.ParseError, KeyError, TypeError) as error:
        # No exception body/test payload/PII is printed; artifacts retain raw evidence.
        print(json.dumps({"metadataProof": "refused", "category": type(error).__name__, **GRANTS}), file=sys.stderr)
        sys.exit(2)
