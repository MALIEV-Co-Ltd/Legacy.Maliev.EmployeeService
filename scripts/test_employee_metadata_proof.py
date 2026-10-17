"""Synthetic controls only. They establish parser behavior, never .NET acceptance."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid
from unittest import mock
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("metadata_proof", HERE / "verify_employee_metadata_proof.py")
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)
ROSTER = v.object_json(v.bounded_read(HERE / "employee-metadata-roster.json"))
def q(name):
    return "{" + v.URI + "}" + name

def fixture(phase):
    root = ET.Element(q("TestRun"))
    results = ET.SubElement(root, q("Results"))
    definitions = ET.SubElement(root, q("TestDefinitions"))
    entries = ET.SubElement(root, q("TestEntries"))
    counts = {"Passed": 0, "Failed": 0}
    for index, (row, outcome) in enumerate(v.select(ROSTER, phase), 1):
        identity = str(uuid.UUID(int=index))
        execution = str(uuid.UUID(int=index + 10000))
        result = ET.SubElement(results, q("UnitTestResult"), testId=identity, executionId=execution,
                               testName=row["name"], outcome=outcome)
        definition = ET.SubElement(definitions, q("UnitTest"), id=identity, name=row["name"],
                                   storage="Legacy.Maliev.EmployeeService.Tests.dll")
        ET.SubElement(definition, q("Execution"), id=execution)
        method = ROSTER["methods"][row["m"]]
        ET.SubElement(definition, q("TestMethod"), className=method["class"], name=method["method"],
                      codeBase="Legacy.Maliev.EmployeeService.Tests.dll")
        ET.SubElement(entries, q("TestEntry"), testId=identity, executionId=execution)
        if outcome == "Failed":
            error = ET.SubElement(ET.SubElement(result, q("Output")), q("ErrorInfo"))
            if row["m"] == 4:
                message = ("System.Text.Json.JsonException : The JSON value could not be converted to System.String. "
                           "Path: $." + row["a"][0] + " | LineNumber: 0 | BytePositionInLine: 80.")
            else:
                message = 'Assert.Equal() Failure: Strings differ\nExpected: "first"\nActual:   null'
            ET.SubElement(error, q("Message")).text = message
            ET.SubElement(error, q("StackTrace")).text = "at " + method["class"] + "." + method["method"] + "()"
        counts[outcome] += 1
    summary = ET.SubElement(root, q("ResultSummary"), outcome="Failed" if phase == "baseline" else "Completed")
    values = {name: "0" for name in v.COUNTERS}
    values.update(total=str(sum(counts.values())), executed=str(sum(counts.values())),
                  passed=str(counts["Passed"]), failed=str(counts["Failed"]))
    ET.SubElement(summary, q("Counters"), **values)
    return root

def encoded(root):
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)

class TrxControls(unittest.TestCase):
    def setUp(self):
        self.root = fixture("candidate")
    def refuse(self, root=None, phase="candidate"):
        with self.assertRaises((ValueError, ET.ParseError, UnicodeError)):
            v.validate_trx(encoded(root if root is not None else self.root), ROSTER, phase)
    def test_exact_candidate_442(self):
        self.assertEqual(v.validate_trx(encoded(self.root), ROSTER, "candidate")["passed"], 442)
    def test_separate_reader_119(self):
        self.assertEqual(v.validate_trx(encoded(fixture("reader")), ROSTER, "reader")["passed"], 119)
    def test_exact_baseline_88_pass_88_classified_fail(self):
        result = v.validate_trx(encoded(fixture("baseline")), ROSTER, "baseline")
        self.assertEqual((result["passed"], result["failed"]), (88, 88))
        self.assertEqual(result["categories"], {"native-consumed-primitive-path": 72, "ordered-null-overwrite": 16})
    def test_missing_result(self):
        self.root[0].remove(self.root[0][-1])
        self.refuse()
    def test_extra_result(self):
        self.root[0].append(copy.deepcopy(self.root[0][0]))
        self.refuse()
    def test_duplicate_result_join(self):
        self.root[0][1].attrib.update(self.root[0][0].attrib)
        self.refuse()
    def test_duplicate_definition_id(self):
        self.root[1][1].set("id", self.root[1][0].get("id"))
        self.refuse()
    def test_duplicate_definition_name(self):
        self.root[1][1].set("name", self.root[1][0].get("name"))
        self.refuse()
    def test_duplicate_execution(self):
        self.root[1][1][0].set("id", self.root[1][0][0].get("id"))
        self.refuse()
    def test_duplicate_entry(self):
        self.root[2][1].attrib.update(self.root[2][0].attrib)
        self.refuse()
    def test_unjoined_execution(self):
        self.root[0][0].set("executionId", str(uuid.UUID(int=99999)))
        self.refuse()
    def test_noncanonical_guid(self):
        self.root[0][0].set("testId", "not-a-guid")
        self.refuse()
    def test_renamed_result(self):
        self.root[0][0].set("testName", "renamed")
        self.refuse()
    def test_changed_class_method_and_assembly(self):
        for field in ("className", "name", "codeBase"):
            with self.subTest(field=field):
                root = fixture("candidate")
                root[1][0][1].set(field, "Other")
                self.refuse(root)
    def test_skipped_aborted_infrastructure_outcomes(self):
        for outcome in ("NotExecuted", "Aborted", "Error", "Inconclusive", "Failed", "Timeout"):
            with self.subTest(outcome=outcome):
                self.root[0][0].set("outcome", outcome)
                self.refuse()
    def test_missing_unknown_and_noncanonical_counters(self):
        for mutation in ("missing", "unknown", "negative", "leading", "decimal", "huge"):
            with self.subTest(mutation=mutation):
                root = fixture("candidate")
                counters = root[3][0]
                if mutation == "missing":
                    del counters.attrib["warning"]
                elif mutation == "unknown":
                    counters.set("unexpected", "0")
                else:
                    counters.set("passed", {"negative": "-1", "leading": "0442", "decimal": "442.0", "huge": "999999"}[mutation])
                self.refuse(root)
    def test_all_nonpass_counters_refused(self):
        for counter in v.COUNTERS[3:]:
            with self.subTest(counter=counter):
                root = fixture("candidate")
                root[3][0].set(counter, "1")
                self.refuse(root)
    def test_contradictory_summary(self):
        self.root[3].set("outcome", "Failed")
        self.refuse()
    def test_infrastructure_runinfo(self):
        ET.SubElement(ET.SubElement(self.root[3], q("RunInfos")), q("RunInfo"), outcome="Error")
        self.refuse()
    def test_unowned_error_in_passed_case(self):
        ET.SubElement(ET.SubElement(self.root[0][0], q("Output")), q("ErrorInfo"))
        self.refuse()
    def test_generic_red_fixture_failure_refused(self):
        root = fixture("baseline")
        error = root.find(".//t:ErrorInfo", v.NS)
        error.find("t:Message", v.NS).text = "System.InvalidOperationException : PostgreSQL fixture failed"
        self.refuse(root, "baseline")
    def test_wrong_red_field_path_refused(self):
        root = fixture("baseline")
        for error in root.findall(".//t:ErrorInfo", v.NS):
            message = error.find("t:Message", v.NS)
            if message.text.startswith("System.Text.Json"):
                message.text = message.text.replace("Path: $.", "Path: $.Wrong")
                break
        self.refuse(root, "baseline")
    def test_unrelated_red_stack_refused(self):
        root = fixture("baseline")
        root.find(".//t:ErrorInfo/t:StackTrace", v.NS).text = "at Docker.Fixture.Start()"
        self.refuse(root, "baseline")
    def test_wrong_null_assertion_refused(self):
        root = fixture("baseline")
        error = root.find(".//t:ErrorInfo", v.NS)
        error.find("t:Message", v.NS).text = 'Assert.Equal() Failure:\nExpected: "other"\nActual: null'
        self.refuse(root, "baseline")
    def test_baseline_unexpected_pass_refused(self):
        root = fixture("baseline")
        next(result for result in root[0] if result.get("outcome") == "Failed").set("outcome", "Passed")
        self.refuse(root, "baseline")
    def test_dtd_entity_and_foreign_namespace_refused(self):
        data = encoded(self.root)
        for candidate in (b'<!DOCTYPE TestRun [<!ENTITY x "y">]>' + data,
                          data.replace(v.URI.encode(), b"urn:foreign")):
            with self.subTest(candidate=candidate[:30]):
                with self.assertRaises((ValueError, ET.ParseError)):
                    v.validate_trx(candidate, ROSTER, "candidate")
    def test_oversized_before_xml_parse(self):
        with self.assertRaises(ValueError):
            v.validate_trx(b" " * (v.FILE_CAP + 1), ROSTER, "candidate")
    def test_hidden_rows_outside_selected_container_refused(self):
        for name, index in (("UnitTestResult", 0), ("UnitTest", 1), ("TestEntry", 2)):
            with self.subTest(name=name):
                root = fixture("candidate")
                hidden = ET.SubElement(root, q("Hidden"))
                hidden.append(copy.deepcopy(root[index][0]))
                self.refuse(root)
    def test_nested_rows_inside_selected_rows_refused(self):
        for name, index in (("UnitTestResult", 0), ("UnitTest", 1), ("TestEntry", 2)):
            with self.subTest(name=name):
                root = fixture("candidate")
                ET.SubElement(root[index][0], q(name))
                self.refuse(root)
    def test_foreign_namespace_hidden_rows_refused(self):
        for name in ("UnitTestResult", "UnitTest", "TestEntry", "ResultSummary", "Counters", "RunInfo", "ErrorInfo"):
            with self.subTest(name=name):
                root = fixture("candidate")
                ET.SubElement(ET.SubElement(root, q("Hidden")), "{urn:foreign}" + name)
                self.refuse(root)
    def test_hidden_or_nested_containers_refused(self):
        for name in ("TestRun", "Results", "TestDefinitions", "TestEntries", "ResultSummary", "Counters"):
            for location in ("hidden", "nested"):
                with self.subTest(name=name, location=location):
                    root = fixture("candidate")
                    parent = ET.SubElement(root, q("Hidden")) if location == "hidden" else root[0][0]
                    ET.SubElement(parent, q(name))
                    self.refuse(root)
    def test_hidden_execution_and_method_refused(self):
        for name in ("Execution", "TestMethod"):
            with self.subTest(name=name):
                root = fixture("candidate")
                ET.SubElement(ET.SubElement(root, q("Hidden")), q(name))
                self.refuse(root)
    def test_hidden_runinfo_with_benign_or_error_outcome_refused(self):
        for outcome in ("Information", "Passed", "Error", "Failed"):
            with self.subTest(outcome=outcome):
                root = fixture("candidate")
                ET.SubElement(ET.SubElement(root, q("Hidden")), q("RunInfo"), outcome=outcome)
                self.refuse(root)
    def test_nested_runinfo_and_runinfos_refused(self):
        for name in ("RunInfo", "RunInfos"):
            with self.subTest(name=name):
                root = fixture("candidate")
                infos = ET.SubElement(root[3], q("RunInfos"))
                info = ET.SubElement(infos, q("RunInfo"), outcome="Information")
                ET.SubElement(info, q(name), outcome="Information")
                self.refuse(root)
    def test_duplicate_direct_runinfos_refused(self):
        ET.SubElement(self.root[3], q("RunInfos"))
        ET.SubElement(self.root[3], q("RunInfos"))
        self.refuse()
    def test_direct_summary_runinfo_refused(self):
        ET.SubElement(self.root[3], q("RunInfo"), outcome="Information")
        self.refuse()
    def test_hidden_errorinfo_in_candidate_refused(self):
        ET.SubElement(ET.SubElement(self.root, q("Hidden")), q("ErrorInfo"))
        self.refuse()
    def test_baseline_errorinfo_moved_without_changing_global_count_refused(self):
        root = fixture("baseline")
        result = next(result for result in root[0] if result.get("outcome") == "Failed")
        output = result.find("t:Output", v.NS)
        error = output.find("t:ErrorInfo", v.NS)
        output.remove(error)
        ET.SubElement(root, q("Hidden")).append(error)
        self.refuse(root, "baseline")
    def test_baseline_nested_errorinfo_refused(self):
        root = fixture("baseline")
        error = root.find(".//t:ErrorInfo", v.NS)
        ET.SubElement(error, q("ErrorInfo"))
        self.refuse(root, "baseline")
    def test_valid_information_runinfo_at_exact_location(self):
        infos = ET.SubElement(self.root[3], q("RunInfos"))
        ET.SubElement(infos, q("RunInfo"), outcome="Information")
        self.assertEqual(v.validate_trx(encoded(self.root), ROSTER, "candidate")["selected"], 442)
    def test_exact_row_moved_outside_container_without_duplicate_refused(self):
        for index in (0, 1, 2):
            with self.subTest(index=index):
                root = fixture("candidate")
                item = root[index][0]
                root[index].remove(item)
                ET.SubElement(root, q("Hidden")).append(item)
                self.refuse(root)
    def test_invalid_utf8(self):
        with self.assertRaises(UnicodeError):
            v.validate_trx(b"\xff", ROSTER, "candidate")

class FileControls(unittest.TestCase):
    def test_bounded_reader_and_symlink(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "input"
            path.write_bytes(b"valid")
            self.assertEqual(v.bounded_read(path, 5), b"valid")
            with self.assertRaises(ValueError):
                v.bounded_read(path, 4)
            link = Path(directory) / "link"
            link.symlink_to(path)
            with self.assertRaises(ValueError):
                v.bounded_read(link)
            with self.assertRaises(ValueError):
                v.bounded_read(Path(directory))
    def test_sparse_oversize_rejected_before_read(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "large"
            with path.open("wb") as stream:
                stream.truncate(v.FILE_CAP + 1)
            with self.assertRaises(ValueError):
                v.bounded_read(path)
    def test_duplicate_json_key(self):
        with self.assertRaises(ValueError):
            v.object_json(b'{"value":1,"value":2}')
    def test_path_traversal_and_absolute(self):
        for name in ("../x", "/x", "a//x", "C:/x", "a\\x", "a/./x"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                v.relative_file(HERE, name)
    def test_inventory_missing_extra_modified_and_link(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "source"
            path.write_bytes(b"source")
            row = {"path": "source", "bytes": 6, "blob": v.git_blob(b"source")}
            self.assertEqual(v.source_inventory(root, [row]), 6)
            path.write_bytes(b"edited")
            with self.assertRaises(ValueError):
                v.source_inventory(root, [row])
            path.write_bytes(b"source")
            extra = root / "extra"
            extra.write_bytes(b"extra")
            with self.assertRaises(ValueError):
                v.source_inventory(root, [row])
            extra.unlink()
            path.unlink()
            with self.assertRaises(v.SourceRefusal):
                v.source_inventory(root, [row])
    def test_evidence_cardinality_and_exit(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "candidate.trx").write_bytes(encoded(fixture("candidate")))
            with self.assertRaises(ValueError):
                v.verify_phase("candidate", path, 1)
            self.assertEqual(v.verify_phase("candidate", path, 0)["selected"], 442)
            (path / "extra.trx").write_bytes(b"extra")
            with self.assertRaises(ValueError):
                v.verify_phase("candidate", path, 0)
    def test_exact_forecast_groups(self):
        roster = copy.deepcopy(ROSTER)
        roster["groups"]["publicReader"][1] = roster["groups"]["publicReader"][0]
        with self.assertRaises(ValueError):
            v.select(roster, "reader")
    def test_final_output_allowlist_is_exact_project_bin_obj_only(self):
        manifest = v.object_json(v.bounded_read(HERE / "employee-metadata-source-successor.json"))
        outputs = v.generated_outputs(manifest)
        self.assertEqual(len(outputs), 10)
        self.assertTrue(all(name.endswith(("/bin", "/obj")) for name in outputs))
        changed = copy.deepcopy(manifest)
        changed["generatedOutputDirectories"].append("Legacy.Maliev.EmployeeService.Api")
        with self.assertRaises(ValueError):
            v.generated_outputs(changed)
    def test_final_inventory_ignores_only_declared_output_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "source"
            path.write_bytes(b"source")
            row = {"path": "source", "bytes": 6, "blob": v.git_blob(b"source")}
            output = root / "Project" / "obj"
            output.mkdir(parents=True)
            (output / "generated").write_bytes(b"generated")
            self.assertEqual(v.source_inventory(root, [row], ("Project/obj",)), 6)
            extra = root / "Project" / "unknown"
            extra.write_bytes(b"unknown")
            with self.assertRaises(ValueError):
                v.source_inventory(root, [row], ("Project/obj",))
    def test_final_inventory_cannot_exclude_bound_source_from_hashing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "source"
            path.write_bytes(b"edited")
            row = {"path": "source", "bytes": 6, "blob": v.git_blob(b"source")}
            with self.assertRaises(ValueError):
                v.source_inventory(root, [row], ("source",))
    def test_declared_output_symlink_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "target").mkdir()
            (root / "output").symlink_to(root / "target", target_is_directory=True)
            with self.assertRaises(ValueError):
                v.source_inventory(root, [], ("output", "target"))
    def test_source_successor_never_grants_sdk_or_runtime(self):
        self.assertEqual(v.GRANTS, {"executionGranted": False, "sdkGranted": False, "runtimeQualified": False})

class FinalSourceControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        projects = ["Legacy.Maliev.EmployeeService." + name for name in ("Api", "Application", "Data", "Domain", "Tests")]
        program = projects[0] + "/Program.cs"
        test_root = projects[-1]
        test_project = test_root + "/" + test_root + ".csproj"
        base_files = {project + "/" + project + ".csproj": b"project" for project in projects}
        base_files[program] = b"native-program"
        checkout_files = {}
        for pin in v.CHECKOUT_REPRESENTATIONS:
            raw = v.bounded_read(HERE.parent / pin["path"])
            canonical = v.canonical_source_bytes({"path": pin["path"], "bytes": pin["gitBytes"], "blob": pin["gitBlob"]},
                                                 raw, v.CHECKOUT_REPRESENTATIONS)
            base_files[pin["path"]] = canonical
            checkout_files[pin["path"]] = raw
        candidate_files = dict(base_files)
        candidate_files[program] = b"candidate-program"
        candidate_files[test_project] = b"test-only-package"
        overlay = sorted([test_project] + [test_root + "/Metadata/Test" + str(index) + ".cs" for index in range(5)])
        for name in overlay:
            candidate_files.setdefault(name, b"authored-test")
        helper = projects[0] + "/Serialization/EmployeeMetadataJsonOptions.cs"
        candidate_files[helper] = b"candidate-helper"
        def bindings(files):
            return [{"path": name, "bytes": len(data), "blob": v.git_blob(data)} for name, data in sorted(files.items())]
        self.manifest = {"schemaVersion": 1, "grants": v.GRANTS, "baseCommit": "base",
                         "featureTree": "feature", "baseline": bindings(base_files),
                         "candidate": bindings(candidate_files), "baselineTestOverlay": overlay,
                         "productionHelper": helper,
                         "generatedOutputDirectories": sorted(project + "/" + leaf for project in projects for leaf in ("bin", "obj"))}
        self.manifest["checkoutRepresentations"] = copy.deepcopy(v.CHECKOUT_REPRESENTATIONS)
        for name, data in candidate_files.items():
            data = checkout_files.get(name, data)
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        baseline_files = dict(base_files)
        baseline_files.update({name: candidate_files[name] for name in overlay})
        for name, data in baseline_files.items():
            data = checkout_files.get(name, data)
            path = self.root / ".metadata-baseline" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        custody = self.root / "scripts/employee-metadata-source-successor.json"
        custody.parent.mkdir(parents=True)
        custody.write_text(json.dumps(self.manifest), encoding="utf-8")
        evidence = self.root / "runner-metadata"
        evidence.mkdir()
        before = {"successorSha256": v.hashlib.sha256(custody.read_bytes()).hexdigest(),
                  "baseCommit": "base", "featureTree": "feature", **v.GRANTS}
        (evidence / "source.json").write_text(json.dumps(before), encoding="utf-8")
        (evidence / "baseline-source.json").write_text(json.dumps({
            "kind": "tests-only-baseline", "baseCommit": "base", "overlayPaths": overlay,
            "sourcePaths": len(baseline_files), **v.GRANTS}), encoding="utf-8")
        self.program = program
        self.custody = custody
        self.addCleanup(mock.patch.stopall)
        mock.patch.object(v, "ROOT", self.root).start()
        mock.patch.object(v, "CUSTODY", custody).start()
    def test_final_source_matches_initial_provenance(self):
        result = v.verify_final_source()
        self.assertEqual(result["baselineProgram"], "exact original binding")
        self.assertEqual(result["baselineHelper"], "absent")
        self.assertEqual(result["baselineOverlayPaths"], 6)
    def test_final_candidate_source_mutation_refused(self):
        (self.root / self.program).write_bytes(b"changed")
        with self.assertRaises(ValueError):
            v.verify_final_source()
    def test_final_baseline_program_mutation_refused(self):
        (self.root / ".metadata-baseline" / self.program).write_bytes(b"candidate-program")
        with self.assertRaises(ValueError):
            v.verify_final_source()
    def test_final_baseline_helper_insertion_refused(self):
        path = self.root / ".metadata-baseline" / self.manifest["productionHelper"]
        path.parent.mkdir()
        path.write_bytes(b"injected")
        with self.assertRaises(ValueError):
            v.verify_final_source()
    def test_final_successor_mutation_refused(self):
        with self.custody.open("a", encoding="utf-8") as stream:
            stream.write("\n")
        with self.assertRaises(ValueError):
            v.verify_final_source()
    def test_final_overlay_provenance_mutation_refused(self):
        receipt = self.root / "runner-metadata/baseline-source.json"
        body = json.loads(receipt.read_text())
        body["overlayPaths"] = []
        receipt.write_text(json.dumps(body), encoding="utf-8")
        with self.assertRaises(ValueError):
            v.verify_final_source()

class CheckoutRepresentationControls(unittest.TestCase):
    def test_exact_two_checkout_representations_preserve_git_oid(self):
        self.assertEqual(len(v.CHECKOUT_REPRESENTATIONS), 2)
        for pin in v.CHECKOUT_REPRESENTATIONS:
            raw = v.bounded_read(HERE.parent / pin["path"])
            row = {"path": pin["path"], "bytes": pin["gitBytes"], "blob": pin["gitBlob"]}
            canonical = v.canonical_source_bytes(row, raw, v.CHECKOUT_REPRESENTATIONS)
            self.assertEqual((len(raw), v.git_blob(raw)), (pin["checkoutBytes"], pin["checkoutBlob"]))
            self.assertEqual((len(canonical), v.git_blob(canonical)), (pin["gitBytes"], pin["gitBlob"]))
    def test_lf_instead_of_expected_crlf_checkout_refused(self):
        for pin in v.CHECKOUT_REPRESENTATIONS:
            raw = v.bounded_read(HERE.parent / pin["path"]).replace(b"\r\n", b"\n")
            row = {"path": pin["path"], "bytes": pin["gitBytes"], "blob": pin["gitBlob"]}
            with self.assertRaises(v.SourceRefusal):
                v.canonical_source_bytes(row, raw, v.CHECKOUT_REPRESENTATIONS)
    def test_mixed_or_modified_ps1_checkout_refused(self):
        for pin in v.CHECKOUT_REPRESENTATIONS:
            raw = v.bounded_read(HERE.parent / pin["path"])
            row = {"path": pin["path"], "bytes": pin["gitBytes"], "blob": pin["gitBlob"]}
            for changed in (raw.replace(b"\r\n", b"\n", 1), raw + b"x"):
                with self.subTest(path=pin["path"]), self.assertRaises(v.SourceRefusal):
                    v.canonical_source_bytes(row, changed, v.CHECKOUT_REPRESENTATIONS)
    def test_other_file_never_normalizes_newlines(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "other.ps1").write_bytes(b"line\r\n")
            row = {"path": "other.ps1", "bytes": 5, "blob": v.git_blob(b"line\n")}
            with self.assertRaises(v.SourceRefusal):
                v.source_inventory(root, [row], checkout=v.CHECKOUT_REPRESENTATIONS)
    def test_new_checkout_path_or_canonical_oid_drift_refused(self):
        manifest = {"checkoutRepresentations": copy.deepcopy(v.CHECKOUT_REPRESENTATIONS)}
        manifest["checkoutRepresentations"].append({"path": "other.ps1"})
        with self.assertRaises(v.SourceRefusal):
            v.checkout_contracts(manifest)
        pin = v.CHECKOUT_REPRESENTATIONS[0]
        raw = v.bounded_read(HERE.parent / pin["path"])
        with self.assertRaises(v.SourceRefusal):
            v.canonical_source_bytes({"path": pin["path"], "bytes": pin["gitBytes"], "blob": "0" * 40},
                                     raw, v.CHECKOUT_REPRESENTATIONS)
    def test_diagnostic_retains_only_fixed_category_and_safe_path(self):
        error = v.SourceRefusal("checkout-representation-drift", "tooling/Test-EmployeeScaffoldContract.ps1")
        self.assertEqual(str(error), "checkout-representation-drift")
        self.assertEqual(error.path, "tooling/Test-EmployeeScaffoldContract.ps1")
        for path in ("../secret", "/absolute", "unsafe\npayload", "payload: value"):
            self.assertIsNone(v.SourceRefusal("source-binding-drift", path).path)

class TypedGitProvenanceControls(unittest.TestCase):
    def test_exact_typed_base_reconstructs_frozen_commit(self):
        self.assertEqual(v.roster_base_commit(ROSTER), "972cc8f9a8e0cd8da69642f8423c14443f4236f7")
    def test_old_scalar_and_unknown_keys_refused(self):
        for value in ("972cc8f9a8e0cd8da69642f8423c14443f4236f7",
                      {**ROSTER["base"], "extra": True}):
            roster = copy.deepcopy(ROSTER)
            roster["base"] = value
            with self.assertRaises(ValueError):
                v.roster_base_commit(roster)
    def test_wrong_type_or_object_format_refused(self):
        for key, value in (("objectType", "blob"), ("objectFormat", "sha256")):
            roster = copy.deepcopy(ROSTER)
            roster["base"][key] = value
            with self.assertRaises(ValueError):
                v.roster_base_commit(roster)
    def test_octet_count_and_scalar_type_refused(self):
        for value in (ROSTER["base"]["bytes"][:-1], ROSTER["base"]["bytes"] + [0], "not-octets"):
            roster = copy.deepcopy(ROSTER)
            roster["base"]["bytes"] = value
            with self.assertRaises(ValueError):
                v.roster_base_commit(roster)
    def test_invalid_or_wrong_octet_refused(self):
        for value in (-1, 256, True, 1.0, 0):
            roster = copy.deepcopy(ROSTER)
            roster["base"]["bytes"][0] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                v.roster_base_commit(roster)
    def test_roster_schema_version_refused(self):
        roster = copy.deepcopy(ROSTER)
        roster["schemaVersion"] = 1
        with self.assertRaises(ValueError):
            v.roster_base_commit(roster)
    def test_every_metadata_native_command_uses_explicit_local_dependencies(self):
        workflow = v.bounded_read(HERE.parent / ".github/workflows/employee-metadata-focused.yml").decode("utf-8")
        commands = [line for line in workflow.splitlines() if "timeout --signal=TERM" in line and "dotnet " in line]
        self.assertEqual(len(commands), 7)
        for line in commands:
            self.assertIn("-p:UseLocalMalievDependencies=true", line)
            self.assertIn('-p:MalievWorkspaceRoot="$MalievWorkspaceRoot"', line)
        self.assertEqual(workflow.count("export GITHUB_ACTIONS=false"), 5)
    def test_temporary_diagnostic_is_removed_from_ordinary_workflow(self):
        workflow = v.bounded_read(HERE.parent / ".github/workflows/_build-and-test.yml").decode("utf-8")
        self.assertNotIn("Diagnose existing", workflow)
        self.assertNotIn("employee_gitleaks_provenance", workflow)
        self.assertIn("Legacy.Maliev.Workflows/actions/dotnet-validate@e3a6093324a24968876782153286f52db8b29fd8", workflow)
        self.assertFalse((HERE / "employee_gitleaks_provenance.py").exists())

if __name__ == "__main__":
    unittest.main()
