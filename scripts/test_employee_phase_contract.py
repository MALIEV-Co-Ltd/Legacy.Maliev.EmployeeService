"""Synthetic pure controls only; no observation in this file is runtime evidence."""
import copy
import datetime as dt
import json
from pathlib import Path
import unittest
import employee_phase_contract as c


class Controls(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 10, 8, 12, tzinfo=dt.timezone.utc)
        self.source = Path(__file__).with_name("employee-source-policy.json").read_bytes()
        self.binding = c.phase_binding(self.source, "a"*40, "b"*64)
        self.grant = {"schemaVersion": 1, "binding": self.binding, "runId": "100", "attempt": 1,
                      "bootId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "startsUtc": "2026-10-08T11:59:00Z",
                      "expiresUtc": "2026-10-08T12:30:00Z", "allowedPhaseIds": [p["id"] for p in c.phase_plan()],
                      "phaseSeconds": 300, "backendQualificationSha256": "b"*64}
        self.policy = {"binding": self.binding, "rootGrantSha256": "", "backendQualificationSha256": "b"*64,
                       "maxAllocationSeconds": 3600, "maxPhaseSeconds": 600}
        self.obs = {"binding": self.binding, "runId": "100", "attempt": 1, "bootId": self.grant["bootId"],
                    "checkedUtc": "2026-10-08T12:00:00Z", "freePhysicalKiB": 4194304,
                    "competingNativeProcesses": [], "ownedNativeProcesses": [], "sourceVerified": True, "custodyReceiptSha256": "c"*64,
                    "sdk": {"owner": "100:1", "pid": 100, "birth": "12345", "executable": "/usr/bin/supervisor",
                            "unit": "synthetic.service", "invocationId": "synthetic", "cgroup": "/synthetic",
                            "expiresUtc": "2026-10-08T12:30:00Z", "memoryBytes": 2*1024**3, "swapBytes": 0,
                            "wholeUnitKill": True, "independentCustodianLive": True}, "providers": []}
        for kind, token in (("postgres18", "d"), ("redis", "e")):
            self.obs["providers"].append({"kind": kind, "id": token*64, "createdRaw": "synthetic-created",
                "imageId": "f"*64, "specSha256": "1"*64, "owner": "100:1", "expiresUtc": "2026-10-08T12:30:00Z",
                "disposable": True, "persistentData": False, "loopbackOnly": True, "cleanupCustodianLive": True})
        self.pre = {"phaseId": "baseline-restore", "binding": self.binding, "runId": "100", "attempt": 1,
                    "bootId": self.grant["bootId"], "completedUtc": "2026-10-08T11:59:59Z", "terminal": True,
                    "cleanupVerified": True, "remainingProcesses": [], "remainingProviders": [],
                    "receiptSha256": "2"*64, "outcomeReviewed": True}

    def gate(self, phase="baseline-restore", pre=None, root=True, backend=True):
        raw = json.dumps(self.grant, sort_keys=True).encode()
        self.policy["rootGrantSha256"] = c.digest(raw)
        return c.validate_admission(self.policy, raw, self.obs, pre, phase, self.now,
                                    lambda *_: root, lambda *_: backend)

    def refused(self, **kwargs):
        with self.assertRaises((ValueError, TypeError)):
            self.gate(**kwargs)

    def test_fixed_12_phase_plan_and_filters(self):
        plan = c.phase_plan()
        self.assertEqual(12, len(plan))
        self.assertEqual("reviewed-baseline-observation-12", plan[2]["expected"])
        self.assertEqual("passed-15", plan[5]["expected"])
        self.assertEqual("passed-371", plan[6]["expected"])
        self.assertEqual(c.OWNER_FILTER, plan[2]["argv"][plan[2]["argv"].index("--filter")+1])
        self.assertIn("SameOwnerUpdate", plan[5]["argv"][plan[5]["argv"].index("--filter")+1])
        self.assertNotIn("--filter", plan[6]["argv"])

    def test_positive_synthetic_contract_never_grants_execution(self):
        self.assertFalse(self.gate()["executionGrantedByThisModule"])

    def test_published_draft_policy_has_no_runtime_authority(self):
        policy = c.draft_policy(self.source, "a"*40, "b"*64)
        self.assertIsNone(policy["rootGrantSha256"])
        self.assertIsNone(policy["backendQualificationSha256"])

    def test_duplicate_authority_json_refused(self):
        with self.assertRaises(ValueError): c.strict_json(b'{"runId":"100","runId":"101"}')

    def test_nonfinite_authority_json_refused(self):
        with self.assertRaises(ValueError): c.strict_json(b'{"budget":NaN}')

    def test_unpinned_authority_blocks_before_verifier(self):
        self.policy["rootGrantSha256"] = None
        with self.assertRaises(ValueError):
            c.validate_admission(self.policy, b"{}", self.obs, None, "baseline-restore", self.now,
                                 lambda *_: self.fail("must not verify unpinned authority"), lambda *_: True)

    def test_root_verifier_refusal(self): self.refused(root=False)
    def test_backend_verifier_refusal(self): self.refused(backend=False)
    def test_source_policy_byte_drift(self):
        with self.assertRaises(ValueError): c.phase_binding(self.source+b"\n", "a"*40, "b"*64)
    def test_raw_source_missing_files(self):
        with self.assertRaises(ValueError): c.verify_source_bytes(self.source, {})
    def test_foreign_policy_binding(self):
        self.policy["binding"] = copy.deepcopy(self.binding)
        self.policy["binding"]["repository"] = "foreign/repo"
        self.refused()
    def test_current_main_drift(self):
        self.grant["binding"] = copy.deepcopy(self.binding)
        self.grant["binding"]["controllerSourceHead"] = "0"*40
        self.refused()
    def test_command_plan_mutation(self):
        self.grant["allowedPhaseIds"].reverse()
        self.refused()
    def test_wrong_run(self): self.obs["runId"] = "101"; self.refused()
    def test_wrong_attempt(self): self.obs["attempt"] = 2; self.refused()
    def test_wrong_boot(self): self.obs["bootId"] = "0"*36; self.refused()
    def test_memory_one_kib_below_floor(self): self.obs["freePhysicalKiB"] -= 1; self.refused()
    def test_competing_native_process(self): self.obs["competingNativeProcesses"] = [{"pid": 999}]; self.refused()
    def test_stale_observation(self): self.obs["checkedUtc"] = "2026-10-08T11:59:57Z"; self.refused()
    def test_future_observation(self): self.obs["checkedUtc"] = "2026-10-08T12:00:01Z"; self.refused()
    def test_expired_authority(self): self.grant["expiresUtc"] = "2026-10-08T11:59:59Z"; self.refused()
    def test_missing_cleanup_reserve(self): self.grant["expiresUtc"] = "2026-10-08T12:06:59Z"; self.refused()
    def test_sdk_custodian_lost(self): self.obs["sdk"]["independentCustodianLive"] = False; self.refused()
    def test_sdk_uncapped(self): self.obs["sdk"]["memoryBytes"] = 0; self.refused()
    def test_provider_foreign(self): self.obs["providers"][0]["owner"] = "foreign"; self.refused()
    def test_provider_persistent(self): self.obs["providers"][0]["persistentData"] = True; self.refused()
    def test_provider_nonloopback(self): self.obs["providers"][0]["loopbackOnly"] = False; self.refused()
    def test_provider_partial_identity(self): self.obs["providers"][0]["id"] = "abcd"; self.refused()
    def test_provider_cleanup_lost(self): self.obs["providers"][0]["cleanupCustodianLive"] = False; self.refused()
    def test_backend_qualification_drift(self): self.grant["backendQualificationSha256"] = "a"*64; self.refused()
    def test_source_reobservation_missing(self): self.obs["sourceVerified"] = False; self.refused()
    def test_successor_requires_actual_retirement(self):
        self.assertEqual("baseline-build", self.gate("baseline-build", self.pre)["phase"]["id"])
    def test_predecessor_wrong_phase(self): self.pre["phaseId"] = "candidate-suite"; self.refused(phase="baseline-build", pre=self.pre)
    def test_predecessor_not_terminal(self): self.pre["terminal"] = False; self.refused(phase="baseline-build", pre=self.pre)
    def test_predecessor_cleanup_unknown(self): self.pre["cleanupVerified"] = False; self.refused(phase="baseline-build", pre=self.pre)
    def test_predecessor_process_remaining(self): self.pre["remainingProcesses"] = [{"pid": 100}]; self.refused(phase="baseline-build", pre=self.pre)
    def test_predecessor_provider_remaining(self): self.pre["remainingProviders"] = ["d"*64]; self.refused(phase="baseline-build", pre=self.pre)
    def test_baseline_outcome_not_reviewed(self): self.pre["outcomeReviewed"] = False; self.refused(phase="baseline-build", pre=self.pre)
    def test_unknown_phase(self): self.refused(phase="caller-chosen")

    def test_restore_does_not_invent_fixture_allocations(self):
        self.obs["providers"] = []
        self.assertFalse(self.gate()["executionGrantedByThisModule"])

    def test_owned_sdk_census_is_distinct_from_competing_processes(self):
        self.obs["ownedNativeProcesses"] = [{"pid": 100}]
        self.assertFalse(self.gate()["executionGrantedByThisModule"])

    def test_controller_head_is_observed_not_transport_main(self):
        binding = c.phase_binding(self.source, "e"*40, "f"*64)
        self.assertEqual("e"*40, binding["controllerSourceHead"])
        self.assertNotEqual(binding["controllerSourceHead"], binding["transportMain"])

    def test_scaffold_plan_does_not_claim_actual_ef(self):
        row = next(row for row in c.phase_plan() if row["id"] == "candidate-scaffold")
        self.assertEqual("powershell-orchestration-only", row["expected"])


if __name__ == "__main__": unittest.main()
