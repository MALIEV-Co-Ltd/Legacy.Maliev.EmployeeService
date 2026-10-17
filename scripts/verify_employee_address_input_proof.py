"""Finite ordinary-CI #79 proof; no SDK/process execution or private custody authority."""
from __future__ import annotations
import collections
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET
import verify_employee_metadata_proof as shared

ROOT = Path(__file__).resolve().parents[1]
PLAN = ROOT / "scripts/employee-address-input-proof.json"
EVIDENCE = ROOT / "runner-metadata/address-input"
URI, NS, COUNTERS = shared.URI, shared.NS, shared.COUNTERS
FILE_CAP = shared.FILE_CAP
GRANTS = shared.GRANTS
BASE_HEAD = "b34700f3d65b5055bc9991a1fd6656b5e3259e87"
BASE_TREE = "fcc2fda97b255b09415886b895c70e9faca93417"
COUNTS = {"characterizer": 347, "baseline": 118, "candidate": 614}
METHOD_COUNTS = {
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputOracleTests.PublicScalar_CharacterizesCoercionAndKind": 76,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputOracleTests.MutableSource_UsesPublicTypedReader": 118,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputOracleTests.MutableSource_PreservesOrderedNullAndErrors": 33,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputOracleTests.ConfiguredInvariantCulture_DoesNotFollowThreadCulture": 2,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.CandidateMatchesSource": 118,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.CandidateOrdersMatchSource": 33,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.CurrentNativeBaseline_IgnoresIgnoredMetadata": 118,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.MalformedSyntax_KeepsCurrentNativePath": 4,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.PropertySpelling_RemainsCaseInsensitive": 6,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputBindingTests.RegistrationScopeAndWriterRemainNative": 1,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.HttpScalar_ValidatesBeforeWritesWithoutInputAuthority": 68,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.HttpOrders_DoNotHideMalformedEarlierOccurrences": 24,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.ValidBodyIdentityAndAudit_HaveNoWriteAuthority": 2,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.DeniedActor_HasNoAddressAuditOrCacheEffects": 4,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.VersionedAddress_ValidInputKeepsPreconditionsAndServerAudits": 4,
    "Legacy.Maliev.EmployeeService.Tests.AddressInput.EmployeeAddressInputHttpTests.VersionedAddress_MalformedInputDoesNotChangeEtagState": 3
}
need, one, guid = shared.need, shared.one, shared.guid

def manifest():
    result = shared.object_json(shared.bounded_read(PLAN))
    need(result["schemaVersion"] == 1 and result["issue"] == 79 and result["grants"] == GRANTS, "Source-only plan")
    need(result["base"] == {"head": BASE_HEAD, "tree": BASE_TREE}, "Exact native baseline")
    need(result["counts"] == COUNTS and result["baselineOutcomes"] == {"Passed": 65, "Failed": 53}, "Exact phase counts")
    need(len(result["methods"]) == 16 and len(result["rows"]) == 614, "Exact authored method/row roster")
    need(len(result["baseline"]) == 173 and len({row["path"] for row in result["baseline"]}) == 173, "Complete baseline source")
    overlay = result["baselineOverlay"]
    need(len(overlay) == 4 and len(set(overlay)) == 4
         and all(path.startswith("Legacy.Maliev.EmployeeService.Tests/AddressInput/") and path.endswith(".cs") for path in overlay),
         "Tests-only four-file overlay")
    names = set()
    counts = collections.Counter()
    baseline = collections.Counter()
    for row in result["rows"]:
        method = result["methods"][row["m"]]
        need(method["class"].startswith("Legacy.Maliev.EmployeeService.Tests.AddressInput.")
             and re.fullmatch(r"[A-Za-z0-9_]+", method["method"]), "Owned test method")
        need(len(row["a"]) == len(method["argumentNames"]), "Argument shape")
        def display(argument):
            if isinstance(argument, str):
                return json.dumps(argument, ensure_ascii=False)
            need(isinstance(argument, dict) and set(argument) == {"enum"}
                 and argument["enum"] in ("Unauthorized", "Forbidden", "PreconditionRequired", "BadRequest",
                                         "PreconditionFailed", "NoContent"), "Exact status argument")
            return argument["enum"]
        arguments = ", ".join(name + ": " + display(value) for name, value in zip(method["argumentNames"], row["a"]))
        expected = method["class"] + "." + method["method"] + ("(" + arguments + ")" if row["a"] else "")
        need(row["name"] == expected and expected not in names, "Exact unique displayed identity")
        names.add(expected)
        phases = row["phases"]
        need(isinstance(phases, list) and len(phases) == len(set(phases))
             and "candidate" in phases and set(phases) <= set(COUNTS), "Closed phase membership")
        counts.update(phases)
        if "baseline" in phases:
            need(method["class"].endswith(".EmployeeAddressInputBindingTests")
                 and method["method"] == "CandidateMatchesSource" and row["baselineOutcome"] in ("Passed", "Failed"),
                 "Only current-native candidate binding RED")
            baseline[row["baselineOutcome"]] += 1
        else:
            need("baselineOutcome" not in row, "Unselected RED outcome refused")
        expected_characterizer = method["class"].endswith(".EmployeeAddressInputOracleTests") or (
            method["class"].endswith(".EmployeeAddressInputBindingTests")
            and method["method"] == "CurrentNativeBaseline_IgnoresIgnoredMetadata")
        need(("characterizer" in phases) == expected_characterizer, "Exact source/native characterizer")
    observed_methods = collections.Counter(result["methods"][row["m"]]["class"] + "." +
                                             result["methods"][row["m"]]["method"] for row in result["rows"])
    need(observed_methods == METHOD_COUNTS, "Exact independent class/method cardinalities")
    need(counts == COUNTS and baseline == {"Passed": 65, "Failed": 53}, "Phase cardinality")
    return result

def select(roster, phase):
    need(phase in COUNTS, "Unknown phase")
    return [(row, row["baselineOutcome"] if phase == "baseline" else "Passed")
            for row in roster["rows"] if phase in row["phases"]]

def baseline_failure(result, row, method):
    error = one(one(result, "Output"), "ErrorInfo")
    message = one(error, "Message").text or ""
    stack = one(error, "StackTrace").text or ""
    need(method["class"] + "." + method["method"] in stack, "Selected binding test stack")
    need(method["method"] == "CandidateMatchesSource" and row["a"][0] in ("Id", "CreatedDate", "ModifiedDate"),
         "Exact unresolved Address input")
    need(message.startswith("Assert.Throws() Failure: No exception was thrown"),
         "Only missing current-native input rejection is expected RED")
    return "expected-address-input-rejection"

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
    need(len(errors) == (53 if phase == "baseline" else 0), "Exact global baseline errors")
    exact("ErrorInfo", errors)
    return summary, counters, infos

def validate_run_infos(infos, expected, phase):
    """Baseline error notices must bijectively identify already classified failures."""
    failed = {name for name, (_, outcome) in expected.items() if outcome == "Failed"}
    if phase == "baseline":
        need(len(infos) == 53 and all(info.get("outcome") == "Error" for info in infos),
             "Exact fifty-three baseline failure notices required")
    observed = set()
    for info in infos:
        outcome = info.get("outcome")
        if outcome == "Error":
            need(phase == "baseline", "Error RunInfo forbidden outside baseline")
            need(len(list(info)) == 1, "Exact baseline failure notice shape")
            text = one(info, "Text")
            need(not list(text), "Failure notice Text must be scalar")
            match = re.fullmatch(r"\[xUnit\.net [0-9]{2}:[0-5][0-9]:[0-5][0-9]\.[0-9]{2}\] {5}(.+) \[FAIL\]", text.text or "")
            need(match is not None, "Exact xUnit baseline failure notice syntax")
            name = match.group(1)
            need(name in failed and name not in observed, "Unique selected failed RunInfo join")
            observed.add(name)
        else:
            need(outcome in ("Passed", "Completed", "Information"), "Infrastructure RunInfo refused")
    need(observed == failed and len(observed) == (53 if phase == "baseline" else 0),
         "Complete baseline failed-result/RunInfo bijection")
    return len(observed)

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
    failure_notices = validate_run_infos(infos, expected, phase)
    need(not summary.findall(".//t:ErrorInfo", NS), "Infrastructure summary error refused")
    allowed_errors = 53 if phase == "baseline" else 0
    need(len(root.findall(".//t:ErrorInfo", NS)) == allowed_errors, "Unowned error refused")
    if phase == "baseline":
        need(categories == {"expected-address-input-rejection": 53}, "Exact RED categories")
    return {"phase": phase, "selected": len(expected), "passed": outcomes["Passed"], "failed": outcomes["Failed"],
            "categories": dict(categories), "baselineFailureNoticesJoined": failure_notices,
            "trxSha256": hashlib.sha256(data).hexdigest(),
            "measured": True, **GRANTS}


def baseline_path(value):
    run_id, attempt = os.environ.get("GITHUB_RUN_ID", ""), os.environ.get("GITHUB_RUN_ATTEMPT", "")
    need(re.fullmatch(r"[0-9]+", run_id) and re.fullmatch(r"[0-9]+", attempt), "Finite hosted owner identity")
    temporary = Path(os.environ["RUNNER_TEMP"]).resolve()
    expected = temporary / ("employee-address-input-native-" + run_id + "-" + attempt)
    supplied = Path(value)
    need(not supplied.is_symlink() and supplied.resolve() == expected and expected.parent == temporary
         and expected != ROOT and ROOT not in expected.parents, "Exact task-owned external baseline path")
    return expected

def proof_source():
    plan = manifest()
    result = shared.verify_source()
    need(result["successorSha256"] == hashlib.sha256(shared.bounded_read(shared.CUSTODY)).hexdigest(), "Candidate custody")
    return {"phase": "source", "baselineHead": BASE_HEAD, "baselineTree": BASE_TREE,
            "planSha256": hashlib.sha256(shared.bounded_read(PLAN)).hexdigest(),
            "candidateSuccessorSha256": result["successorSha256"], **GRANTS}

def baseline_rows(plan, candidate):
    expected = {row["path"]: row for row in plan["baseline"]}
    for name in plan["baselineOverlay"]:
        need(name in candidate, "Overlay source binding required")
        expected[name] = candidate[name]
    return list(expected.values())

def prepare_baseline(value):
    destination = baseline_path(value)
    proof_source()
    plan = manifest()
    # git archive applies .gitattributes, including the two sealed CRLF contracts.
    custody = shared.object_json(shared.bounded_read(shared.CUSTODY))
    checkout = shared.checkout_contracts(custody)
    shared.source_inventory(destination, plan["baseline"], (), checkout)
    candidate = {row["path"]: row for row in custody["candidate"]}
    for name in plan["baselineOverlay"]:
        target = shared.relative_file(destination, name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(shared.bounded_read(shared.relative_file(ROOT, name)))
    shared.source_inventory(destination, baseline_rows(plan, candidate), (), checkout)
    need(not (destination / "Legacy.Maliev.EmployeeService.Api/Serialization/EmployeeAddressInputJsonOptions.cs").exists(),
         "New production validator absent from baseline")
    return {"phase": "tests-only-current-native-baseline", "sourcePaths": len(plan["baseline"]) + 4,
            "overlayPaths": plan["baselineOverlay"], "baselineHead": BASE_HEAD, **GRANTS}

def final_source(value):
    destination = baseline_path(value)
    plan = manifest()
    before = shared.object_json(shared.bounded_read(EVIDENCE / "source.json"))
    need(before["planSha256"] == hashlib.sha256(shared.bounded_read(PLAN)).hexdigest()
         and before["candidateSuccessorSha256"] == hashlib.sha256(shared.bounded_read(shared.CUSTODY)).hexdigest(),
         "Source custody changed after admission")
    custody = shared.object_json(shared.bounded_read(shared.CUSTODY))
    outputs = shared.generated_outputs(custody)
    own = shared.CUSTODY.relative_to(ROOT).as_posix()
    exclusions = (".git", ".dependencies", ".metadata-baseline", "runner-metadata", own) + outputs
    shared.source_inventory(ROOT, custody["candidate"], exclusions, shared.checkout_contracts(custody))
    candidate = {row["path"]: row for row in custody["candidate"]}
    shared.source_inventory(destination, baseline_rows(plan, candidate), outputs, shared.checkout_contracts(custody))
    return {"phase": "final-source", "candidateSuccessorSha256": before["candidateSuccessorSha256"],
            "baselineHead": BASE_HEAD, "baselineProductionUnchanged": True, "overlayPaths": 4, **GRANTS}

def verify_phase(phase, directory, exit_code):
    need(phase in COUNTS and exit_code == (1 if phase == "baseline" else 0), "Exact native test exit")
    path = Path(directory)
    need(path.is_dir() and not path.is_symlink(), "Evidence directory")
    paths = list(itertools.islice(path.iterdir(), 2))
    need(len(paths) == 1 and paths[0].suffix == ".trx", "Exactly one separate TRX")
    return validate_trx(shared.bounded_read(paths[0]), manifest(), phase)

def cleanup_baseline(value):
    destination = baseline_path(value)
    if destination.exists():
        need(destination.is_dir(), "Baseline directory")
        for directory, dirs, names in os.walk(destination, followlinks=False):
            need(not any((Path(directory) / name).is_symlink() for name in dirs + names), "Cleanup link refused")
        shutil.rmtree(destination)
    need(not destination.exists(), "Owned baseline removal failed")
    return {"phase": "baseline-cleanup", "absent": True, "persistentData": False, **GRANTS}

def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "source" and len(sys.argv) == 2:
        result = proof_source()
    elif mode == "prepare-baseline" and len(sys.argv) == 3:
        result = prepare_baseline(sys.argv[2])
    elif mode == "final-source" and len(sys.argv) == 3:
        result = final_source(sys.argv[2])
    elif mode == "cleanup-baseline" and len(sys.argv) == 3:
        result = cleanup_baseline(sys.argv[2])
    elif mode == "verify" and len(sys.argv) == 5:
        result = verify_phase(sys.argv[2], sys.argv[3], int(sys.argv[4]))
    else:
        raise ValueError("Expected source | prepare-baseline PATH | final-source PATH | cleanup-baseline PATH | verify PHASE DIRECTORY EXIT")
    print(json.dumps(result, sort_keys=True))

if __name__ == "__main__":
    try:
        main()
    except shared.SourceRefusal as error:
        print(json.dumps({"addressInputProof": "refused", "category": error.category,
                          "path": error.path, **GRANTS}), file=sys.stderr)
        sys.exit(2)
    except (ValueError, OSError, UnicodeError, ET.ParseError, KeyError, TypeError, IndexError):
        print(json.dumps({"addressInputProof": "refused", **GRANTS}), file=sys.stderr)
        sys.exit(2)
