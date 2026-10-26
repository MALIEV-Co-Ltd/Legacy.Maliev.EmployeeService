"""Real current dependency profile controls; no native/subprocess/network calls."""
import copy
import ast
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import runpy
import sys
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest import mock
import employee_dependency_profile as d

REAL=Path(__file__).resolve().parents[1]

class DependencyProfileControls(unittest.TestCase):
    def setUp(self):
        self.owner=tempfile.TemporaryDirectory(prefix='employee-dependency-control-')
        self.addCleanup(self.owner.cleanup)
        self.root=Path(self.owner.name)/'source'
        shutil.copytree(REAL,self.root,ignore=shutil.ignore_patterns('.git','.dependencies','__pycache__','bin','obj','runner-*','.metadata-baseline'))

    def manifest(self,source=None):
        return d.transport.object_json(d.read(self.root,source or d.METADATA))

    def write(self,manifest,source=None):
        (self.root/(source or d.METADATA)).write_bytes((json.dumps(manifest,indent=2)+'\n').encode())

    def check(self):
        return d.authenticate_metadata(self.root,self.manifest())

    def reseal(self,path,raw):
        (self.root/path).write_bytes(raw)
        manifest=self.manifest()
        row=d.identity(path,raw)
        next(r for r in manifest['candidate'] if r['path']==path).update(row)
        delta=manifest[d.FIELD]['changedBindings']
        found=next((r for r in delta if r['path']==path),None)
        if found is None:
            old=next(r for r in d.predecessor(self.root,d.METADATA)['candidate'] if r['path']==path)
            delta.append(dict(path=path,before=old,after=row))
        else:found['after']=row
        self.write(manifest)

    def test_actual_current_profiles_and_full_source_inventory(self):
        old=self.check()
        d.authenticate_pagination(self.root,self.manifest(d.PAGINATION))
        self.assertEqual(161,len(old['baseline']))
        self.assertEqual(208,len(self.manifest()['candidate']))
        self.assertEqual(88,len(self.manifest(d.PAGINATION)['codePins']))
        import verify_employee_metadata_proof as guard
        manifest=self.manifest()
        guard.source_inventory(self.root,d.expanded_rows(manifest),('.git','.dependencies','.metadata-baseline','runner-metadata',d.METADATA),guard.checkout_contracts(manifest))

    def test_original_transport_assertions_execute_in_removed_owned_view(self):
        with d.historical_view(self.root) as history:
            d.transport.authenticate_metadata(history,d.predecessor(self.root,d.METADATA))
            d.transport.authenticate_pagination(history,d.predecessor(self.root,d.PAGINATION))
            self.assertEqual(d.archive(self.root,d.METADATA),(history/d.METADATA).read_bytes())
        self.assertFalse(history.exists())

    def test_stale_main_profile_is_refused(self):
        self.write(d.predecessor(self.root,d.METADATA))
        with self.assertRaises(ValueError):self.check()

    def test_mixed_pagination_is_refused(self):
        self.write(d.predecessor(self.root,d.PAGINATION),d.PAGINATION)
        with self.assertRaises(ValueError):self.check()

    def test_changed_main_archive_is_refused(self):
        path=d.ARCHIVES[d.METADATA]['archive'];(self.root/path).write_bytes(d.read(self.root,path)+b' ')
        with self.assertRaises(ValueError):self.check()

    def test_changed_accepted_transport_module_is_refused_after_reseal(self):
        path='scripts/employee_artifact_transport_profile.py'
        self.reseal(path,d.read(self.root,path)+b'\n')
        with self.assertRaises(ValueError):self.check()

    def test_resealed_unreviewed_production_source_is_refused(self):
        path='Legacy.Maliev.EmployeeService.Api/Program.cs'
        self.reseal(path,d.read(self.root,path)+b'\n// drift\n')
        with self.assertRaises(ValueError):self.check()

    def test_resealed_different_dependency_version_is_refused(self):
        path='Directory.Build.props';self.reseal(path,d.read(self.root,path).replace(b'10.0.13',b'10.0.14'))
        with self.assertRaises(ValueError):self.check()

    def test_resealed_required_api_reference_removal_is_refused(self):
        path='Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj'
        raw=d.read(self.root,path);line=b'    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.13" />\n'
        self.assertEqual(1,raw.count(line));self.reseal(path,raw.replace(line,b''))
        with self.assertRaises(ValueError):self.check()

    def test_resealed_strict_assertion_removal_is_refused(self):
        path=d.transport.WORKFLOW_TEST;raw=d.read(self.root,path)
        line=b'        Assert.Equal("10.0.13", reference.Attribute("Version")?.Value);\n'
        self.assertEqual(1,raw.count(line));self.reseal(path,raw.replace(line,b''))
        with self.assertRaises(ValueError):self.check()

    def test_resealed_action_regression_is_refused(self):
        path='.github/workflows/_build-and-test.yml'
        self.reseal(path,d.read(self.root,path).replace(b'cf430e030ddbb5b0abf93d22962f4752f3646cd9',b'043fb46d1a93c77aae656e7c1c64a875d1fc6a0a'))
        with self.assertRaises(ValueError):self.check()

    def test_resealed_original_test_removal_is_refused(self):
        path='scripts/test_employee_metadata_proof.py'
        self.reseal(path,d.read(self.root,path)+b'\n# altered test\n')
        with self.assertRaises(ValueError):self.check()

    def test_false_grants_and_boolean_types_are_preserved(self):
        for value in [True,0]:
            manifest=self.manifest();manifest['grants']['sdkGranted']=value;self.write(manifest)
            with self.assertRaises(ValueError):self.check()
            self.write(d.transport.object_json(d.read(REAL,d.METADATA)))

    def test_original_roster_order_is_preserved(self):
        manifest=self.manifest();manifest['candidate'].reverse();self.write(manifest)
        with self.assertRaises(ValueError):self.check()

    def test_duplicate_manifest_key_is_refused(self):
        path=self.root/d.METADATA;raw=path.read_bytes();path.write_bytes(b'{"schemaVersion":1,'+raw.lstrip()[1:])
        with self.assertRaises(ValueError):self.check()

    def test_added_input_omission_is_refused(self):
        manifest=self.manifest();manifest[d.FIELD]['additionalInputs'].pop();self.write(manifest)
        with self.assertRaises(ValueError):self.check()

    def test_new_helper_change_is_refused(self):
        path=self.root/d.MODULE;path.write_bytes(path.read_bytes()+b'\n')
        with self.assertRaises(ValueError):self.check()

    def test_consistently_resealed_new_helper_replacement_is_refused(self):
        path=d.MODULE;raw=d.read(self.root,path)+b'\n# post-import replacement\n'
        (self.root/path).write_bytes(raw)
        page=self.manifest(d.PAGINATION)
        next(row for row in page[d.FIELD]['additionalInputs'] if row['path']==path).update(d.identity(path,raw,'sha256'))
        self.write(page,d.PAGINATION)
        metadata=self.manifest()
        next(row for row in metadata[d.FIELD]['additionalInputs'] if row['path']==path).update(d.identity(path,raw))
        page_row=d.identity(d.PAGINATION,d.read(self.root,d.PAGINATION))
        next(row for row in metadata['candidate'] if row['path']==d.PAGINATION).update(page_row)
        next(row for row in metadata[d.FIELD]['changedBindings'] if row['path']==d.PAGINATION)['after']=page_row
        self.write(metadata)
        # Both current manifests and the transitive page row are resealed, so
        # refusal must identify the post-import helper replacement itself.
        with self.assertRaisesRegex(ValueError,'loaded dependency verifier changed'):
            self.check()

    def test_current_profile_required_before_historical_projection(self):
        self.write(d.predecessor(self.root,d.METADATA))
        with self.assertRaises(ValueError):
            with d.historical_view(self.root):self.fail('Historical-only admission')

    def test_changed_new_controls_cannot_be_resealed(self):
        path=d.CONTROLS;raw=d.read(self.root,path)+b'\n'
        (self.root/path).write_bytes(raw);manifest=self.manifest()
        next(r for r in manifest[d.FIELD]['additionalInputs'] if r['path']==path).update(d.identity(path,raw))
        self.write(manifest)
        with self.assertRaises(ValueError):self.check()

    def test_artifact_profile_history_is_immutable(self):
        manifest=self.manifest();manifest['artifactTransportSuccessor']['profile']='unreviewed';self.write(manifest)
        with self.assertRaises(ValueError):self.check()

    def test_inherited_reader_refuses_hardlinked_source(self):
        path=self.root/'Directory.Build.props'
        os.link(path,self.root/'same-source-hardlink')
        with self.assertRaises(ValueError):d.read(self.root,'Directory.Build.props')

    def test_inherited_reader_refuses_change_between_lstat_and_open(self):
        path=self.root/'Directory.Build.props';original=Path.open
        raw=path.read_bytes()
        def replace_before_open(current,*args,**kwargs):
            if current==path:
                with original(path,'wb') as output:output.write(raw+b'changed')
            return original(current,*args,**kwargs)
        with mock.patch.object(Path,'open',autospec=True,side_effect=replace_before_open):
            with self.assertRaises(ValueError):d.read(self.root,'Directory.Build.props')

    def final_source_fixture(self,lane):
        # Synthetic source receipts only. No TRX/native baseline result exists.
        spec=importlib.util.spec_from_file_location('_dependency_address_current',self.root/'scripts/verify_employee_address_input_proof.py')
        core=importlib.util.module_from_spec(spec);spec.loader.exec_module(core)
        plan=self.root/f'scripts/employee-{lane}-input-proof.json'
        evidence=self.root/f'runner-metadata/{lane}-input';evidence.mkdir(parents=True)
        custody=self.root/d.METADATA
        (evidence/'source.json').write_text(json.dumps(dict(planSha256=hashlib.sha256(plan.read_bytes()).hexdigest(),
            candidateSuccessorSha256=hashlib.sha256(custody.read_bytes()).hexdigest())))
        baseline=Path(self.owner.name)/f'employee-{lane}-input-native-123-1';baseline.mkdir()
        environment=dict(GITHUB_RUN_ID='123',GITHUB_RUN_ATTEMPT='1',RUNNER_TEMP=self.owner.name)
        return core,baseline,environment,custody

    def test_actual_address_final_source_checks_expanded_current_then_refuses_absent_baseline(self):
        core,baseline,environment,custody=self.final_source_fixture('address')
        with mock.patch.dict(os.environ,environment),mock.patch.object(core.shared,'CUSTODY',custody):
            with self.assertRaises(core.shared.SourceRefusal) as failure:
                core.final_source(str(baseline))
        # Before the dispatch fix, this is source-path-set-drift on the current
        # tree. Afterwards the unchanged baseline check refuses its missing file.
        self.assertEqual('source-input-unreadable',failure.exception.category)
        self.assertEqual(core.manifest()['baseline'][0]['path'],failure.exception.path)

    def test_actual_signature_cli_delegates_current_inventory_then_refuses_absent_baseline(self):
        core,baseline,environment,custody=self.final_source_fixture('signature')
        output=io.StringIO()
        with mock.patch.dict(os.environ,environment),mock.patch.object(core.shared,'CUSTODY',custody), \
             mock.patch.dict(sys.modules,{'verify_employee_address_input_proof':core}), \
             mock.patch.object(sys,'argv',['signature-proof','final-source',str(baseline)]),contextlib.redirect_stderr(output):
            with self.assertRaises(SystemExit) as failure:
                runpy.run_path(str(self.root/'scripts/verify_employee_signature_input_proof.py'),run_name='__main__')
        self.assertEqual(2,failure.exception.code)
        diagnostic=json.loads(output.getvalue())
        self.assertEqual('source-input-unreadable',diagnostic['category'])
        self.assertEqual(core.manifest()['baseline'][0]['path'],diagnostic['path'])
        self.assertIs(False,diagnostic['executionGranted'])
        self.assertIs(False,diagnostic['sdkGranted'])

    def test_current_inventory_dispatch_preserves_all_baseline_calls(self):
        def inventories(raw):
            return [ast.dump(node,include_attributes=False) for node in ast.walk(ast.parse(raw))
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr=='source_inventory'
                and not (isinstance(node.args[0],ast.Name) and node.args[0].id in ('ROOT','root'))]
        path='scripts/verify_employee_address_input_proof.py'
        self.assertEqual(inventories(d.archive(self.root,path)),inventories(d.read(self.root,path)))
        # Signature baseline calls and all metadata baseline calls are also
        # independently compared to their immutable main5f source witnesses.
        path='scripts/verify_employee_metadata_proof.py'
        self.assertEqual(inventories(d.archive(self.root,path)),inventories(d.read(self.root,path)))
        signature=ast.parse(d.read(self.root,'scripts/verify_employee_signature_input_proof.py'))
        calls=[node for node in ast.walk(signature) if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr=='source_inventory']
        self.assertEqual(2,len(calls))
        self.assertTrue(all(isinstance(call.args[0],ast.Name) and call.args[0].id=='destination' for call in calls))
        original=next(row for row in d.predecessor(self.root,d.METADATA)['candidate'] if row['path']=='scripts/verify_employee_signature_input_proof.py')
        self.assertEqual(original,d.identity(original['path'],d.read(self.root,original['path'])))

if __name__=='__main__':unittest.main()
