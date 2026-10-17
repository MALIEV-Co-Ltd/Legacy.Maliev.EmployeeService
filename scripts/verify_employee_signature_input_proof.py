"""Finite #81 Signature proof reusing the already reviewed bounded #79 evidence validator."""
from verify_employee_address_input_proof import *
import verify_employee_address_input_proof as core

PLAN = ROOT / "scripts/employee-signature-input-proof.json"
EVIDENCE = ROOT / "runner-metadata/signature-input"
BASE_HEAD = "a8728b1e7d020695680a23f93438f3cf6b9790e9"
BASE_TREE = "1098af62c1f3aeb3837565ddb0a2a43d99727af5"
COUNTS = {"characterizer": 269, "baseline": 118, "candidate": 481}
METHOD_COUNTS = {
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.CandidateMatchesSource": 118,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.CandidateOrdersMatchSource": 33,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.CurrentNativeBaseline_IgnoresIgnoredMetadata": 118,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.MalformedSyntax_KeepsCurrentNativePath": 4,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.PropertySpelling_RemainsCaseInsensitive": 6,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputBindingTests.RegistrationScopeAndWriterRemainNative": 1,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputOracleTests.MutableSource_UsesPublicTypedReader": 118,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputOracleTests.MutableSource_PreservesOrderedNullAndErrors": 33,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputHttpTests.HttpScalar_ValidatesBeforeWritesWithoutInputAuthority": 34,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputHttpTests.HttpOrders_DoNotHideMalformedEarlierOccurrences": 12,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputHttpTests.ValidBodyIdentityAndAudit_HaveNoWriteAuthority": 1,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputHttpTests.DeniedActor_HasNoSignatureAuditOrCacheEffects": 2,
    "Legacy.Maliev.EmployeeService.Tests.SignatureInput.EmployeeSignatureInputHttpTests.PostQuery_DoesNotAcquireJsonBodyValidation": 1
}

def manifest():
    result = shared.object_json(shared.bounded_read(PLAN))
    need(result["schemaVersion"] == 1 and result["issue"] == 81 and result["grants"] == GRANTS, "Source-only plan")
    need(result["base"] == {"head": BASE_HEAD, "tree": BASE_TREE}, "Exact native baseline")
    need(result["counts"] == COUNTS and result["baselineOutcomes"] == {"Passed": 65, "Failed": 53}, "Exact phase counts")
    need(len(result["methods"]) == 13 and len(result["rows"]) == 481, "Exact authored method/row roster")
    need(len(result["baseline"]) == 183 and len({row["path"] for row in result["baseline"]}) == 183, "Complete baseline source")
    overlay = result["baselineOverlay"]
    need(len(overlay) == 4 and len(set(overlay)) == 4
         and all(path.startswith("Legacy.Maliev.EmployeeService.Tests/SignatureInput/") and path.endswith(".cs") for path in overlay),
         "Tests-only four-file overlay")
    names = set()
    counts = collections.Counter()
    baseline = collections.Counter()
    for row in result["rows"]:
        method = result["methods"][row["m"]]
        need(method["class"].startswith("Legacy.Maliev.EmployeeService.Tests.SignatureInput.")
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
            need(method["class"].endswith(".EmployeeSignatureInputBindingTests")
                 and method["method"] == "CandidateMatchesSource" and row["baselineOutcome"] in ("Passed", "Failed"),
                 "Only current-native candidate binding RED")
            baseline[row["baselineOutcome"]] += 1
        else:
            need("baselineOutcome" not in row, "Unselected RED outcome refused")
        expected_characterizer = method["class"].endswith(".EmployeeSignatureInputOracleTests") or (
            method["class"].endswith(".EmployeeSignatureInputBindingTests")
            and method["method"] == "CurrentNativeBaseline_IgnoresIgnoredMetadata")
        need(("characterizer" in phases) == expected_characterizer, "Exact source/native characterizer")
    observed_methods = collections.Counter(result["methods"][row["m"]]["class"] + "." +
                                             result["methods"][row["m"]]["method"] for row in result["rows"])
    need(observed_methods == METHOD_COUNTS, "Exact independent class/method cardinalities")
    need(counts == COUNTS and baseline == {"Passed": 65, "Failed": 53}, "Phase cardinality")
    return result


def baseline_path(value):
    run_id, attempt = os.environ.get("GITHUB_RUN_ID", ""), os.environ.get("GITHUB_RUN_ATTEMPT", "")
    need(re.fullmatch(r"[0-9]+", run_id) and re.fullmatch(r"[0-9]+", attempt), "Finite hosted owner identity")
    temporary = Path(os.environ["RUNNER_TEMP"]).resolve()
    expected = temporary / ("employee-signature-input-native-" + run_id + "-" + attempt)
    supplied = Path(value)
    need(not supplied.is_symlink() and supplied.resolve() == expected and expected.parent == temporary
         and expected != ROOT and ROOT not in expected.parents, "Exact task-owned external baseline path")
    return expected


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
    need(not (destination / "Legacy.Maliev.EmployeeService.Api/Serialization/EmployeeSignatureInputJsonOptions.cs").exists(),
         "New production validator absent from baseline")
    return {"phase": "tests-only-current-native-baseline", "sourcePaths": len(plan["baseline"]) + 4,
            "overlayPaths": plan["baselineOverlay"], "baselineHead": BASE_HEAD, **GRANTS}


_original_validate_trx = core.validate_trx
def validate_trx(data, roster, phase):
    result = _original_validate_trx(data, roster, phase)
    if phase == "baseline":
        need(result["categories"] == {"expected-address-input-rejection": 53}, "Exact inherited typed-input category")
        result["categories"] = {"expected-signature-input-rejection": 53}
    return result

# This CLI process owns only this sealed plan; the original #79 CLI/defaults are unchanged.
core.PLAN, core.EVIDENCE = PLAN, EVIDENCE
core.BASE_HEAD, core.BASE_TREE, core.COUNTS = BASE_HEAD, BASE_TREE, COUNTS
core.METHOD_COUNTS = METHOD_COUNTS
core.manifest, core.baseline_path = manifest, baseline_path
core.prepare_baseline, core.validate_trx = prepare_baseline, validate_trx

if __name__ == "__main__":
    try:
        core.main()
    except shared.SourceRefusal as error:
        print(json.dumps({"signatureInputProof": "refused", "category": error.category,
                          "path": error.path, **GRANTS}), file=sys.stderr)
        sys.exit(2)
    except (ValueError, OSError, UnicodeError, ET.ParseError, KeyError, TypeError, IndexError):
        print(json.dumps({"signatureInputProof": "refused", **GRANTS}), file=sys.stderr)
        sys.exit(2)
