"""Pure source custody controls: no SDK, providers, network or dispatch."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import employee_qualification_controller as q


class ConnectionControls(unittest.TestCase):
    def setUp(self):
        self.scripts = Path(__file__).resolve().parent
        self.env = patch.dict(q.os.environ, {"GITHUB_SHA": "a"*40, "GITHUB_ACTIONS": "false"})
        self.env.start()
        self.addCleanup(self.env.stop)

    def test_actual_357_raw_files_verified_and_owned_source_released(self):
        with tempfile.TemporaryDirectory(prefix="employee-preflight-control-") as parent:
            root = Path(parent) / "employee-owner-source"
            result = q.preflight(self.scripts, root, "100-1")
            self.assertEqual(357, result["rawFilesVerified"])
            self.assertEqual(12, len(result["fixedPhases"]))
            self.assertFalse(result["nativeExecutionGranted"])
            self.assertIsNone(result["draftAdmission"]["rootGrantSha256"])
            self.assertTrue(result["sourceCleanup"]["sourceRootAbsent"])
            self.assertFalse(root.exists())

    def test_qualification_refuses_before_materialization_or_environment_read(self):
        with patch.object(q, "preflight", side_effect=AssertionError("must not materialize")):
            with self.assertRaises(q.QualificationRefused):
                q.main(["--mode", "qualification", "--evidence", "uncreated"])

    def test_arbitrary_authority_cannot_adopt_missing_backend(self):
        with self.assertRaises(q.QualificationRefused):
            q.require_runtime_connection(b'{"nativeExecutionGranted":true}')

    def test_source_drift_still_releases_exact_owned_tree(self):
        with tempfile.TemporaryDirectory(prefix="employee-preflight-control-") as parent:
            root = Path(parent) / "employee-owner-source"
            original = q.read_inventory
            def drift(*args):
                rows = original(*args)
                name = next(iter(rows)); rows[name] += b"changed"
                return rows
            with patch.object(q, "read_inventory", side_effect=drift):
                with self.assertRaises(ValueError): q.preflight(self.scripts, root, "100-1")
            self.assertFalse(root.exists())

    def test_foreign_preexisting_root_is_not_removed(self):
        with tempfile.TemporaryDirectory(prefix="employee-preflight-control-") as parent:
            root = Path(parent) / "employee-owner-source"; root.mkdir()
            (root / "foreign").write_bytes(b"retained")
            with self.assertRaises(ValueError): q.preflight(self.scripts, root, "100-1")
            self.assertEqual(b"retained", (root / "foreign").read_bytes())

    def test_partial_materialization_with_durable_owner_is_released(self):
        with tempfile.TemporaryDirectory(prefix="employee-preflight-control-") as parent:
            root = Path(parent) / "employee-owner-source"
            def partial(raw, capsules, root, owner, helper):
                root.mkdir(); (root / ".employee-source-owner").write_bytes(owner.encode())
                raise OSError("synthetic write failure")
            with patch.object(q.intake, "materialize", side_effect=partial):
                with self.assertRaises(OSError): q.preflight(self.scripts, root, "100-1")
            self.assertFalse(root.exists())

    def test_real_cli_preflight_has_zero_native_evidence(self):
        with tempfile.TemporaryDirectory(prefix="employee-preflight-control-") as parent:
            text = io.StringIO()
            with patch.dict(q.os.environ, {"RUNNER_TEMP": parent, "EMPLOYEE_SOURCE_OWNER": "100-1", "GITHUB_SHA": "a"*40, "GITHUB_RUN_ID": "100", "GITHUB_RUN_ATTEMPT": "1"}), contextlib.redirect_stdout(text):
                q.main(["--mode", "preflight-only", "--evidence", str(Path(parent)/"evidence")])
            result = json.loads(text.getvalue())
            self.assertEqual(0, result["actualNativeCases"])
            self.assertFalse(result["backendQualified"])
            self.assertTrue(result["sourceCleanup"]["sourceRootAbsent"])

    def test_linux_observation_uses_memfree_not_reclaimable_available(self):
        with tempfile.TemporaryDirectory() as folder:
            proc = Path(folder)
            boot = proc / "sys/kernel/random"; boot.mkdir(parents=True)
            (boot / "boot_id").write_text("12345678-1234-1234-1234-123456789abc")
            (proc / "meminfo").write_text("MemFree: 100 kB\nMemAvailable: 9999999 kB\n")
            pid = proc / "42"; pid.mkdir()
            (pid / "comm").write_text("postgres\n")
            (pid / "stat").write_text("42 (postgres) " + " ".join(["S"] + ["0"]*18 + ["123"]))
            result = q.controller_observation(self.scripts, proc, platform="posix")
            self.assertEqual(100, result["resources"]["freePhysicalKiB"])
            self.assertFalse(result["resources"]["physicalFloorEstimateSatisfied"])
            self.assertFalse(result["resources"]["nativeAdmissionQualified"])
            self.assertEqual(42, result["resources"]["nativeProcesses"][0]["pid"])
            self.assertFalse(result["allocationReusableForNative"])
            self.assertEqual("12345678-1234-1234-1234-123456789abc", result["actualLinuxBootId"])

    def test_missing_linux_observation_is_not_qualified(self):
        with tempfile.TemporaryDirectory() as folder:
            result = q.controller_observation(self.scripts, Path(folder), platform="posix")
        self.assertFalse(result["resources"]["candidateNameScanComplete"])
        self.assertFalse(result["resources"]["nativeAdmissionQualified"])

    def test_hosted_fetch_uses_exact_three_policy_oids(self):
        helper = q.intake.accepted_helper()
        policy = q.intake.load_policy((self.scripts / "employee-source-policy.json").read_bytes(), helper)
        mapping = {policy["capsules"][view]["oid"]: (self.scripts / "employee-source-packet" / (view + ".zip")).read_bytes() for view in q.intake.COUNTS}
        with tempfile.TemporaryDirectory() as folder, patch.object(helper, "fetch_git_blob", side_effect=lambda repo, oid: mapping[oid]) as fetch, patch.object(q.intake, "accepted_helper", return_value=helper):
            result = q.preflight(self.scripts, Path(folder)/"employee-owner-source", "100-1", fetch=True)
        self.assertEqual(357, result["rawFilesVerified"])
        self.assertEqual(3, fetch.call_count)
        self.assertEqual(set(mapping), {call.args[1] for call in fetch.call_args_list})


if __name__ == "__main__": unittest.main()
