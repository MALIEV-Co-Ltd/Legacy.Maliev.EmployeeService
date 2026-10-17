"""Refusal controls for ordinary Address input proof. Never starts native workers."""
import copy
import unittest
import uuid
import xml.etree.ElementTree as ET
import verify_employee_signature_input_proof as proof
import verify_employee_metadata_proof as shared

def fixture(phase):
    plan = proof.manifest()
    selected = proof.select(plan, phase)
    tag = lambda name: "{" + proof.URI + "}" + name
    root = ET.Element(tag("TestRun"))
    results = ET.SubElement(root, tag("Results"))
    definitions = ET.SubElement(root, tag("TestDefinitions"))
    entries = ET.SubElement(root, tag("TestEntries"))
    summary = ET.SubElement(root, tag("ResultSummary"), outcome="Failed" if phase == "baseline" else "Completed")
    wanted = {name: "0" for name in proof.COUNTERS}
    failures = sum(outcome == "Failed" for _, outcome in selected)
    wanted.update(total=str(len(selected)), executed=str(len(selected)),
                  passed=str(len(selected) - failures), failed=str(failures))
    ET.SubElement(summary, tag("Counters"), wanted)
    infos = ET.SubElement(summary, tag("RunInfos")) if failures else None
    for index, (row, outcome) in enumerate(selected):
        test_id, execution_id = str(uuid.UUID(int=index + 1)), str(uuid.UUID(int=index + 10001))
        method = plan["methods"][row["m"]]
        definition = ET.SubElement(definitions, tag("UnitTest"), id=test_id, name=row["name"],
                                   storage="/tmp/legacy.maliev.employeeservice.tests.dll")
        ET.SubElement(definition, tag("Execution"), id=execution_id)
        ET.SubElement(definition, tag("TestMethod"), className=method["class"], name=method["method"],
                      codeBase="/tmp/Legacy.Maliev.EmployeeService.Tests.dll")
        ET.SubElement(entries, tag("TestEntry"), testId=test_id, executionId=execution_id)
        result = ET.SubElement(results, tag("UnitTestResult"), testId=test_id, executionId=execution_id,
                               testName=row["name"], outcome=outcome)
        if outcome == "Failed":
            error = ET.SubElement(ET.SubElement(result, tag("Output")), tag("ErrorInfo"))
            ET.SubElement(error, tag("Message")).text = "Assert.Throws() Failure: No exception was thrown"
            ET.SubElement(error, tag("StackTrace")).text = "at " + method["class"] + "." + method["method"] + "()"
            info = ET.SubElement(infos, tag("RunInfo"), outcome="Error")
            ET.SubElement(info, tag("Text")).text = "[xUnit.net 00:00:01.23]     " + row["name"] + " [FAIL]"
    return root, plan

def validate(root, plan, phase):
    return proof.validate_trx(ET.tostring(root, encoding="utf-8"), plan, phase)

class TrxControls(unittest.TestCase):
    def test_all_three_exact_phases(self):
        for phase in proof.COUNTS:
            root, plan = fixture(phase)
            result = validate(root, plan, phase)
            self.assertEqual(result["selected"], proof.COUNTS[phase])
            self.assertEqual(result["failed"], 53 if phase == "baseline" else 0)

    def refuse(self, mutation, phase="candidate"):
        root, plan = fixture(phase)
        mutation(root)
        with self.assertRaises((ValueError, KeyError, TypeError)):
            validate(root, plan, phase)

    def test_missing_result_refused(self):
        self.refuse(lambda root: root.find("t:Results", proof.NS).remove(root.find("t:Results/t:UnitTestResult", proof.NS)))
    def test_duplicate_result_refused(self):
        self.refuse(lambda root: root.find("t:Results", proof.NS).append(copy.deepcopy(root.find("t:Results/t:UnitTestResult", proof.NS))))
    def test_duplicate_definition_refused(self):
        self.refuse(lambda root: root.find("t:TestDefinitions", proof.NS).append(copy.deepcopy(root.find("t:TestDefinitions/t:UnitTest", proof.NS))))
    def test_duplicate_execution_refused(self):
        self.refuse(lambda root: root.findall("t:TestDefinitions/t:UnitTest/t:Execution", proof.NS)[1].set(
            "id", root.find("t:TestDefinitions/t:UnitTest/t:Execution", proof.NS).get("id")))
    def test_unknown_entry_refused(self):
        self.refuse(lambda root: root.find("t:TestEntries/t:TestEntry", proof.NS).set("testId", str(uuid.uuid4())))
    def test_result_entry_execution_mismatch_refused(self):
        self.refuse(lambda root: root.find("t:Results/t:UnitTestResult", proof.NS).set("executionId", str(uuid.uuid4())))
    def test_display_name_mismatch_refused(self):
        self.refuse(lambda root: root.find("t:Results/t:UnitTestResult", proof.NS).set("testName", "unknown"))
    def test_wrong_class_refused(self):
        self.refuse(lambda root: root.find("t:TestDefinitions/t:UnitTest/t:TestMethod", proof.NS).set("className", "Other"))
    def test_wrong_method_refused(self):
        self.refuse(lambda root: root.find("t:TestDefinitions/t:UnitTest/t:TestMethod", proof.NS).set("name", "Other"))
    def test_wrong_assembly_refused(self):
        self.refuse(lambda root: root.find("t:TestDefinitions/t:UnitTest/t:TestMethod", proof.NS).set("codeBase", "/tmp/Other.dll"))
    def test_wrong_storage_refused(self):
        self.refuse(lambda root: root.find("t:TestDefinitions/t:UnitTest", proof.NS).set("storage", "/tmp/Other.dll"))
    def test_skipped_result_refused(self):
        self.refuse(lambda root: root.find("t:Results/t:UnitTestResult", proof.NS).set("outcome", "NotExecuted"))
    def test_warning_counter_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:Counters", proof.NS).set("warning", "1"))
    def test_noncanonical_counter_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:Counters", proof.NS).set("passed", "0481"))
    def test_unknown_counter_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:Counters", proof.NS).set("unknown", "0"))
    def test_missing_counter_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:Counters", proof.NS).attrib.pop("pending"))
    def test_counter_contradiction_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:Counters", proof.NS).set("total", "613"))
    def test_hidden_result_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary", proof.NS).append(copy.deepcopy(
            root.find("t:Results/t:UnitTestResult", proof.NS))))
    def test_hidden_error_refused(self):
        self.refuse(lambda root: ET.SubElement(root.find("t:ResultSummary", proof.NS), "{" + proof.URI + "}ErrorInfo"))
    def test_unknown_error_notice_refused(self):
        self.refuse(lambda root: ET.SubElement(ET.SubElement(root.find("t:ResultSummary", proof.NS),
            "{" + proof.URI + "}RunInfos"), "{" + proof.URI + "}RunInfo", outcome="Error"))
    def test_baseline_notice_missing_refused(self):
        self.refuse(lambda root: root.find("t:ResultSummary/t:RunInfos", proof.NS).remove(
            root.find("t:ResultSummary/t:RunInfos/t:RunInfo", proof.NS)), "baseline")
    def test_baseline_notice_duplicate_refused(self):
        def mutate(root):
            infos = root.findall("t:ResultSummary/t:RunInfos/t:RunInfo/t:Text", proof.NS)
            infos[1].text = infos[0].text
        self.refuse(mutate, "baseline")
    def test_baseline_notice_unknown_refused(self):
        self.refuse(lambda root: setattr(root.find("t:ResultSummary/t:RunInfos/t:RunInfo/t:Text", proof.NS),
            "text", "[xUnit.net 00:00:01.23]     unrelated [FAIL]"), "baseline")
    def test_baseline_failure_stack_refused(self):
        self.refuse(lambda root: setattr(root.find("t:Results/t:UnitTestResult/t:Output/t:ErrorInfo/t:StackTrace", proof.NS),
            "text", "unrelated"), "baseline")
    def test_baseline_failure_category_refused(self):
        self.refuse(lambda root: setattr(root.find("t:Results/t:UnitTestResult/t:Output/t:ErrorInfo/t:Message", proof.NS),
            "text", "infrastructure timeout"), "baseline")
    def test_foreign_namespace_refused(self):
        self.refuse(lambda root: ET.SubElement(root, "{foreign}UnitTestResult"))
    def test_dtd_and_entities_refused(self):
        root, plan = fixture("candidate")
        data = ET.tostring(root, encoding="utf-8")
        for declaration in (b"<!DOCTYPE x>", b"<!ENTITY x 'x'>"):
            with self.assertRaises(ValueError):
                proof.validate_trx(declaration + data, plan, "candidate")
    def test_byte_and_element_bounds_refused(self):
        root, plan = fixture("candidate")
        with self.assertRaises(ValueError):
            proof.validate_trx(b"x" * (proof.FILE_CAP + 1), plan, "candidate")
        for _ in range(30001):
            ET.SubElement(root, "{" + proof.URI + "}Marker")
        with self.assertRaises(ValueError):
            validate(root, plan, "candidate")
    def test_malformed_guid_refused(self):
        self.refuse(lambda root: root.find("t:Results/t:UnitTestResult", proof.NS).set("testId", "not-guid"))

class ArchiveRepresentationControls(unittest.TestCase):
    def pins(self):
        plan = proof.manifest()
        rows = {row["path"]: row for row in plan["baseline"]}
        for pin in shared.CHECKOUT_REPRESENTATIONS:
            yield rows[pin["path"]], shared.bounded_read(proof.ROOT / pin["path"])

    def test_archive_crlf_contracts_bind_exact_git_sources(self):
        for row, raw in self.pins():
            canonical = shared.canonical_source_bytes(row, raw, shared.CHECKOUT_REPRESENTATIONS)
            self.assertEqual(len(canonical), row["bytes"])
            self.assertEqual(shared.git_blob(canonical), row["blob"])
            self.assertNotEqual(raw, canonical)

    def test_archive_canonical_lf_is_not_an_accepted_crlf_representation(self):
        for row, raw in self.pins():
            with self.assertRaises(shared.SourceRefusal):
                shared.canonical_source_bytes(row, raw.replace(b"\r\n", b"\n"), shared.CHECKOUT_REPRESENTATIONS)

    def test_archive_modified_crlf_source_is_refused(self):
        for row, raw in self.pins():
            with self.assertRaises(shared.SourceRefusal):
                shared.canonical_source_bytes(row, raw + b"# changed\r\n", shared.CHECKOUT_REPRESENTATIONS)

if __name__ == "__main__":
    unittest.main()
