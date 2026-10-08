"""Synthetic SDK custody controls; no real Linux/SDK/systemd commands executed."""
import copy
import datetime as dt
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import employee_linux_sdk_owner as m


class FakeBackend:
    def __init__(self, stage, now):
        self.stage = stage; self.now = now; self.actions = []
        self.observation = {"bootId": stage["bootId"], "observedUtc": now.isoformat().replace("+00:00", "Z"), "memFreeKiB": m.FLOOR_KIB, "competingProcesses": [], "candidateExecutableCensusQualified": True}
        self.item = None; self.fast_terminal = False

    def preflight(self): return copy.deepcopy(self.observation)
    def register_intent(self, plan):
        self.actions.append("intent")
        self.item = {"unit": plan["unit"], "invocationId": "a"*32, "bootId": self.stage["bootId"], "cgroup": "/system.slice/"+plan["unit"], "device": 1, "inode": 2, "pid": 42, "birthTicks": "100", "executable": "/usr/bin/dotnet", "properties": copy.deepcopy(plan["properties"]), "active": True, "members": [42], "expiresUtc": self.stage["expiresUtc"], "managerState": "active", "execMainStatus": 0}
        return "c"*64
    def terminal(self, state="inactive"):
        self.item.update(active=False, members=[], pid=0, birthTicks=None, executable=None, device=None, inode=None, managerState=state)
    def start_transient(self, plan, token):
        self.actions.append("start")
        if self.fast_terminal: self.terminal("exited")
    def observe(self, unit): return copy.deepcopy(self.item)
    def persist_generation(self, generation, token): self.actions.append("generation")
    def stop_exact(self, generation, deadlineSeconds):
        assert deadlineSeconds == 120
        self.actions.append("stop"); self.terminal()
    def persist_cleanup(self, receipt): self.actions.append("cleanup")


class CustodyControls(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 10, 8, 12, tzinfo=dt.timezone.utc)
        self.stage = {"schemaVersion": 1, "sourceOnly": True, "sourceHead": "a"*40, "codeSha256": "b"*64, "phaseContractSha256": m.PHASE_CONTRACT_SHA256, "sourcePolicySha256": m.SOURCE_POLICY_SHA256, "sdkExecutableSha256": "c"*64, "runId": "100", "attempt": 1, "bootId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "startsUtc": "2026-10-08T12:00:00Z", "expiresUtc": "2026-10-08T12:20:00Z", "phaseSeconds": 600, "phaseId": "baseline-build", "argv": copy.deepcopy(m.FIXED_SDK_PHASES["baseline-build"]), "workingDirectory": "/private/employee/baseline", "rootGrantSha256": None, "backendQualificationSha256": "d"*64}
        grant = {"stageSha256": m.seal(self.stage), "runId": "100", "attempt": 1, "bootId": self.stage["bootId"], "expiresUtc": self.stage["expiresUtc"]}
        self.raw = json.dumps(grant, sort_keys=True).encode()
        self.stage["rootGrantSha256"] = hashlib.sha256(self.raw).hexdigest()
        self.backend = FakeBackend(self.stage, self.now)
        self.owner = m.SDKOwner(self.backend, self.stage, lambda *_: True, lambda *_: True, clock=lambda: self.now)

    def acquired(self): return self.owner.acquire(self.raw, self.now)
    def test_structured_service_caps_and_no_shell(self):
        plan = m.unit_plan(self.stage, self.now)
        self.assertTrue(plan["unit"].endswith(".service"))
        props = plan["properties"]
        for key, expected in {"Type": "exec", "MemoryMax": 3*1024**3, "MemorySwapMax": 0, "CPUQuotaPerSecUSec": 250000, "TasksMax": 128, "KillMode": "control-group", "RuntimeMaxUSec": 600000000, "TimeoutStopUSec": 5000000, "RemainAfterExit": True}.items(): self.assertEqual(expected, props[key])
        self.assertFalse(plan["executionGranted"])

    def test_original_grant_then_custody_then_start_then_generation(self):
        self.acquired()
        self.assertEqual(["intent", "start", "generation"], self.backend.actions)

    def test_absent_authority_no_backend_mutation(self):
        self.stage["rootGrantSha256"] = None
        with self.assertRaises(ValueError): self.acquired()
        self.assertEqual([], self.backend.actions)

    def test_root_verifier_refusal_no_mutation(self):
        self.owner.verify_root = lambda *_: False
        with self.assertRaises(ValueError): self.acquired()
        self.assertEqual([], self.backend.actions)

    def test_unqualified_backend_no_mutation(self):
        self.owner.verify_backend = lambda *_: False
        with self.assertRaises(ValueError): self.acquired()
        self.assertEqual([], self.backend.actions)

    def test_changed_original_root_bytes_refused(self):
        with self.assertRaises(ValueError): self.owner.acquire(self.raw+b" ", self.now)

    def test_physical_floor_uses_memfree_and_refuses_one_kib_short(self):
        self.backend.observation["memFreeKiB"] -= 1
        with self.assertRaises(ValueError): self.acquired()
        self.assertEqual([], self.backend.actions)

    def test_unknown_census_never_qualifies(self):
        self.backend.observation["candidateExecutableCensusQualified"] = False
        with self.assertRaises(ValueError): self.acquired()

    def test_competing_executable_refused(self):
        self.backend.observation["competingProcesses"] = [{"pid": 77, "birthTicks": "101", "executable": "/foreign/dotnet"}]
        with self.assertRaises(ValueError): self.acquired()

    def test_stale_observation_refused(self):
        self.backend.observation["observedUtc"] = "2026-10-08T11:59:57Z"
        with self.assertRaises(ValueError): self.acquired()

    def test_admission_repeated_after_intent_before_start(self):
        original = self.backend.register_intent
        def change(plan):
            token = original(plan); self.backend.observation["memFreeKiB"] = 0; return token
        self.backend.register_intent = change
        with self.assertRaises(ValueError): self.acquired()
        self.assertEqual(["intent"], self.backend.actions)

    def test_acquisition_replay_refused(self):
        self.acquired()
        with self.assertRaises(ValueError): self.acquired()

    def test_terminal_mainpid_zero_and_removed_cgroup_supported(self):
        self.acquired()
        result = self.owner.cleanup(self.now)
        self.assertTrue(result["terminal"])
        self.assertEqual("inactive", result["managerResult"])
        self.assertFalse(result["runtimeQualificationClaimed"])

    def test_fast_exited_phase_retains_manager_witness_then_deactivates(self):
        self.backend.fast_terminal = True
        result = self.acquired()
        self.assertEqual(0, result["generation"]["pid"])
        self.owner.cleanup(self.now)
        self.assertIn("stop", self.backend.actions)

    def test_foreign_invocation_no_stop(self):
        self.acquired(); self.backend.item["invocationId"] = "f"*32
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)
        self.assertNotIn("stop", self.backend.actions)

    def test_reused_pid_birth_no_stop(self):
        self.acquired(); self.backend.item["birthTicks"] = "101"
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)
        self.assertNotIn("stop", self.backend.actions)

    def test_terminal_replaced_cgroup_no_cleanup_success(self):
        self.acquired(); self.backend.terminal(); self.backend.item.update(device=1, inode=999)
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)

    def test_terminal_remaining_members_refused(self):
        self.acquired(); self.backend.terminal(); self.backend.item["members"] = [77]
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)

    def test_cleanup_after_expiry_still_settles_exact_owned_generation(self):
        self.acquired(); self.now += dt.timedelta(hours=1)
        self.assertTrue(self.owner.cleanup(self.now)["terminal"])

    def test_uncertain_acquisition_cannot_adopt_by_name(self):
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)

    def test_boolean_schema_refused(self):
        self.stage["schemaVersion"] = True
        with self.assertRaises(ValueError): m.unit_plan(self.stage, self.now)

    def test_extra_unfrozen_dotnet_argument_refused(self):
        self.stage["argv"].append("--arbitrary")
        with self.assertRaises(ValueError): m.unit_plan(self.stage, self.now)

    def test_non_sdk_phase_refused(self):
        self.stage["phaseId"] = "candidate-scaffold"; self.stage["argv"] = ["/usr/bin/dotnet", "tool"]
        with self.assertRaises(ValueError): m.unit_plan(self.stage, self.now)

    def test_scope_and_shell_cannot_replace_service_plan(self):
        self.stage["argv"] = ["/bin/sh", "-c", "dotnet build"]
        with self.assertRaises(ValueError): m.unit_plan(self.stage, self.now)

    def test_all_five_stop_budgets_reserved(self):
        self.stage["expiresUtc"] = "2026-10-08T12:12:34Z"
        with self.assertRaises(ValueError): m.unit_plan(self.stage, self.now)

    def test_backend_root_policy_null_refuses_before_instantiation(self):
        with patch.object(m, "TRUSTED_ROOT_PUBLIC_KEY_SHA256", None):
            with self.assertRaises(ValueError): m.execute_qualification(self.stage, b"{}", b"{}", b"{}", "uncreated", lambda: self.fail("no projection"), backend_factory=lambda *_: self.fail("no backend"))

    def test_cli_qualification_null_root_refuses_before_stage_read(self):
        with self.assertRaises(ValueError): m.main(["--mode", "qualification", "--stage", "uncreated"])

    def test_real_cgroup_snapshot_preserves_all_descendant_members(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root/"cgroup.procs").write_text("42\n")
            child = root/"child"; child.mkdir(); (child/"cgroup.procs").write_text("77\n")
            backend = m.RealLinuxBackend(root, "a"*64)
            directories, members = backend._cgroup_snapshot(root)
            self.assertEqual([42, 77], members); self.assertEqual(2, len(directories))

    def test_missing_descendant_procs_is_uncertainty_not_zero_members(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root/"cgroup.procs").write_text("42\n")
            (root/"child").mkdir()
            backend = m.RealLinuxBackend(root, "a"*64)
            with self.assertRaises(FileNotFoundError): backend._cgroup_snapshot(root)

    def test_real_adapter_requires_explicit_binary_pin(self):
        with self.assertRaises(ValueError): m.RealLinuxBackend(Path.cwd(), None)

    def test_original_authority_duplicate_fields_refused(self):
        with self.assertRaises(ValueError): m.strict_json(b'{"runId":"100","runId":"101"}')

    def test_candidate_cannot_replace_actual_baseline_review_with_boolean(self):
        stage = dict(self.stage, phaseId="candidate-build", argv=m.FIXED_SDK_PHASES["candidate-build"])
        policy = {"stageSha256": m.seal(stage), "grantSha256": stage["rootGrantSha256"], "backendReceiptSha256": stage["backendQualificationSha256"], "backendModuleSha256": "a"*64, "phaseContractSha256": m.PHASE_CONTRACT_SHA256, "sourcePolicySha256": m.SOURCE_POLICY_SHA256, "authoritySchemaIntegrationReviewed": True, "actualBaselineReviewSha256": "b"*64}
        with patch.object(m, "TRUSTED_ROOT_PUBLIC_KEY_SHA256", "a"*64):
            with self.assertRaisesRegex(ValueError, "candidate cannot waive"):
                m.execute_qualification(stage, json.dumps(policy).encode(), b"{}", b"{}", "uncreated", lambda: self.fail("no projection"), backend_factory=lambda *_: self.fail("no backend"), verify_root_policy=lambda *_: True)

    def test_bounded_reader_refuses_oversized_stage(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"stage.json"; path.write_bytes(b"x"*16385)
            with self.assertRaises(ValueError): m.bounded_read(path, 16384)

    def test_boolean_terminal_pid_is_not_actual_zero(self):
        self.acquired(); self.backend.terminal(); self.backend.item["pid"] = False
        with self.assertRaises(ValueError): self.owner.cleanup(self.now)

    def test_boolean_or_duplicate_member_pid_refused(self):
        for members in ([42, True], [42, 42]):
            self.backend.item = None
            original = self.backend.observe
            def altered(unit):
                item = original(unit); item["members"] = members; return item
            self.backend.observe = altered
            with self.assertRaises(ValueError): self.acquired()
            self.backend.observe = original

    def test_busctl_wrong_signature_cannot_be_normalized_as_true(self):
        backend = m.RealLinuxBackend(Path.cwd(), "a"*64)
        backend._command = lambda *_, **__: b'{"type":"s","data":["true"]}'
        with self.assertRaises(ValueError): backend._property("employee.service", "org.freedesktop.systemd1.Service", "SendSIGKILL")

    def test_cleanup_receipt_timestamp_is_fresh_after_actual_terminal_wait(self):
        self.acquired()
        entry = self.now
        original = self.backend.stop_exact
        def delayed(generation, deadlineSeconds):
            self.now += dt.timedelta(seconds=30)
            return original(generation, deadlineSeconds)
        self.backend.stop_exact = delayed
        receipt = self.owner.cleanup(entry)
        self.assertEqual("2026-10-08T12:00:30Z", receipt["observedUtc"])

    def test_projection_verifier_executes_sealed_bytes_not_reopened_path(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); projection = root/"projection"; projection.mkdir()
            contract = root/"contract.py"; policy = root/"policy.json"
            trusted = b'def verify_source_policy(raw):\n return {"capsules": {view: {"rows": []} for view in ("baseline", "candidate", "dependencies")}}\ndef verify_source_bytes(raw, inventory):\n assert inventory == {}\n'
            contract.write_bytes(trusted); policy.write_bytes(b"{}")
            read = m.bounded_read
            def replace_after_sealed_read(path, maximum):
                raw = read(path, maximum)
                if Path(path) == contract:
                    contract.write_bytes(b'raise AssertionError("unsealed reopened bytes executed")\n')
                return raw
            with patch.object(m, "PHASE_CONTRACT_SHA256", hashlib.sha256(trusted).hexdigest()), patch.object(m, "bounded_read", side_effect=replace_after_sealed_read):
                result = m.verify_projection(contract, policy, projection)
            self.assertEqual(0, result["rawFilesVerified"])
            self.assertIn(b"unsealed", contract.read_bytes())


if __name__ == "__main__":
    unittest.main()
