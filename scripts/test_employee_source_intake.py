import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import employee_source_intake as intake

class IntakeControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.helper = intake.accepted_helper()
        scripts = Path(__file__).resolve().parent
        cls.raw = (scripts/'employee-source-policy.json').read_bytes()
        cls.policy = cls.helper.parse_json(cls.raw)
        cls.capsules = {role:(scripts/'employee-source-packet'/f'{role}.zip').read_bytes()
                        for role in intake.COUNTS}

    def reject_policy(self, mutate):
        policy = copy.deepcopy(self.policy)
        mutate(policy)
        with self.assertRaises(ValueError):
            intake.validate_policy(policy,self.helper)

    def test_exact_frozen_graph(self):
        policy,files = intake.validate_inputs(self.raw,self.capsules,self.helper)
        self.assertEqual(357,len(files))
        self.assertFalse(policy['nativeExecutionGranted'])
        self.assertEqual(371,policy['forecastCases']['full'])

    def test_exact_four_overlay_differences(self):
        _,files = intake.validate_inputs(self.raw,self.capsules,self.helper)
        baseline = {p.removeprefix('baseline/'):raw for p,raw in files.items() if p.startswith('baseline/')}
        candidate = {p.removeprefix('candidate/'):raw for p,raw in files.items() if p.startswith('candidate/')}
        self.assertEqual(set(baseline),set(candidate))
        self.assertEqual({'Legacy.Maliev.EmployeeService.Api/Controllers/SignaturesController.cs',
            'Legacy.Maliev.EmployeeService.Tests/EmployeeSignaturePersistenceHttpTests.cs',
            'Legacy.Maliev.EmployeeService.Tests/EmployeeRouteAcceptanceHttpTests.cs',intake.OWNER_TEST},
            {p for p in baseline if baseline[p] != candidate[p]})

    def test_foreign_repository_rejected(self):
        self.reject_policy(lambda p:p.update(repository='MALIEV-Co-Ltd/Legacy.Maliev.CustomerService'))

    def test_base_drift_rejected(self):
        self.reject_policy(lambda p:p.update(baseCommit='0'*40))

    def test_native_grant_rejected(self):
        self.reject_policy(lambda p:p.update(nativeExecutionGranted=True))

    def test_unknown_policy_field_rejected(self):
        self.reject_policy(lambda p:p.update(execute='dotnet'))

    def test_dependency_drift_rejected(self):
        self.reject_policy(lambda p:p['dependencyCommits'].update({'Legacy.Maliev.ServiceDefaults':'b'*40}))

    def test_raw_policy_byte_drift_rejected(self):
        with self.assertRaises(ValueError):
            intake.load_policy(self.raw+b' ',self.helper)

    def test_duplicate_json_rejected(self):
        with self.assertRaises(ValueError):
            self.helper.parse_json(b'{"repository":"a","repository":"b"}')

    def test_capsule_byte_drift_rejected(self):
        capsules = dict(self.capsules)
        capsules['baseline'] += b'x'
        with self.assertRaises(ValueError):
            intake.validate_inputs(self.raw,capsules,self.helper)

    def test_capsule_role_swap_rejected(self):
        capsules = dict(self.capsules)
        capsules['baseline'],capsules['candidate'] = capsules['candidate'],capsules['baseline']
        with self.assertRaises(ValueError):
            intake.validate_inputs(self.raw,capsules,self.helper)

    def test_path_traversal_rejected(self):
        self.reject_policy(lambda p:p['capsules']['baseline']['rows'][0].update(path='baseline/../escape'))

    def test_path_case_alias_rejected(self):
        self.reject_policy(lambda p:p['capsules']['baseline']['rows'][1].update(
            path=p['capsules']['baseline']['rows'][0]['path'].upper()))

    def test_capsule_unknown_role_rejected(self):
        capsules = dict(self.capsules,foreign=b'x')
        with self.assertRaises(ValueError):
            intake.validate_inputs(self.raw,capsules,self.helper)

    def test_invalid_input_writes_nothing(self):
        with tempfile.TemporaryDirectory() as parent:
            root = Path(parent)/'employee-owner-source'
            with self.assertRaises(ValueError):
                intake.materialize(self.raw+b' ',self.capsules,root,'1-1',self.helper)
            self.assertFalse(root.exists())

    def test_materialization_readback_and_owned_release(self):
        with tempfile.TemporaryDirectory() as parent:
            root = Path(parent)/'employee-owner-source'
            receipt = intake.materialize(self.raw,self.capsules,root,'1-1',self.helper)
            self.assertEqual(357,receipt['rawFiles'])
            _,files = intake.validate_inputs(self.raw,self.capsules,self.helper)
            for name,raw in files.items():
                self.assertEqual(raw,(root/name).read_bytes())
            self.assertTrue(intake.release(root,'1-1',self.helper)['sourceRootAbsent'])

    def test_foreign_owner_release_preserves_source(self):
        with tempfile.TemporaryDirectory() as parent:
            root = Path(parent)/'employee-owner-source'
            root.mkdir(); (root/'.employee-source-owner').write_bytes(b'2-1')
            with self.assertRaises(ValueError):
                intake.release(root,'1-1',self.helper)
            self.assertTrue(root.exists())

    def test_symlink_source_parent_rejected(self):
        with tempfile.TemporaryDirectory() as parent:
            real = Path(parent)/'real'; real.mkdir()
            link = Path(parent)/'linked'
            try:
                link.symlink_to(real,target_is_directory=True)
            except OSError:
                self.skipTest('platform does not permit symlink creation')
            with self.assertRaises(ValueError):
                intake.materialize(self.raw,self.capsules,link/'employee-owner-source','1-1',self.helper)
            self.assertFalse((real/'employee-owner-source').exists())

if __name__ == '__main__':
    unittest.main()
