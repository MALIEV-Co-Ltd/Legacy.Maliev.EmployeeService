"""Pure synthetic Linux qualification controls; no live Linux/manager/SDK."""
import contextlib
import datetime as dt
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import employee_linux_qualification as m


class Reader:
    def local(self):
        return {'bootId': '11111111-2222-3333-4444-555555555555', 'pid': 123,
                'birthTicks': '999', 'cgroup': '/synthetic', 'cgroupDevice': 1,
                'cgroupInode': 2, 'memFreeKiB': 4096, 'cgroupVersion': 2, 'pidfdObserved': True}


class Helpers:
    def __init__(self): self.calls = []; self.released = False; self.bad = False
    def get(self, name):
        self.calls.append(name)
        return b'{"type":"u","data":[1]}' if self.bad else json.dumps(
            {'type': 's', 'data': ['255 (synthetic)' if name == 'Version' else 'running']}).encode()
    def release_evidence(self):
        self.released = True
        return [{'pid': 456, 'birthTicks': '1001', 'exitCode': 0, 'cleanupVerified': True, 'remainingMembers': []}]


class ProbeControls(unittest.TestCase):
    def test_synthetic_observation_never_certifies_runtime(self):
        helpers = Helpers()
        result = m.Probe(Reader(), helpers, synthetic=True).capture()
        self.assertFalse(result['runtimeQualified'])
        self.assertFalse(result['nativeExecutionGranted'])
        self.assertFalse(result['providerExecutionGranted'])
        self.assertFalse(result['actualLinuxObservations'])
        self.assertEqual(['Version', 'SystemState'], helpers.calls)
        self.assertTrue(helpers.released)
        self.assertIn('exact-owned true unit lifecycle', result['unrun'])
    def test_fake_collectors_cannot_claim_actual(self):
        with self.assertRaises(ValueError): m.Probe(Reader(), Helpers())
    def test_unknown_layout_refuses_and_releases(self):
        helpers = Helpers(); helpers.bad = True
        with self.assertRaises(ValueError): m.Probe(Reader(), helpers, synthetic=True).capture()
        self.assertTrue(helpers.released)
        self.assertEqual(['Version'], helpers.calls)
    def test_true_unit_refuses_even_fabricated_policy(self):
        with self.assertRaises(PermissionError): m.true_unit_qualification(policy={'reviewed': True})
    def test_true_unit_cli_refuses_before_evidence_or_helper_read(self):
        with self.assertRaises(PermissionError): m.main(['--mode', 'true-unit', '--helper', 'missing'])
    def test_source_check_has_no_helper_construction(self):
        with patch.object(m, 'ReadOnlyHelpers', side_effect=AssertionError('should not construct')):
            out = io.StringIO()
            with contextlib.redirect_stdout(out): m.main([])
            result = json.loads(out.getvalue())
            self.assertFalse(result['liveProbeExecuted'])
            self.assertFalse(result['trueUnitQualificationEligible'])
    def test_non_linux_readonly_reader_refuses(self):
        with patch.object(m.sys, 'platform', 'win32'):
            with self.assertRaises(ValueError): m.LinuxReader()
    def test_fixed_readonly_command_whitelist(self):
        self.assertEqual({'Version', 'SystemState'}, set(m.COMMANDS))
        for name, argv in m.COMMANDS.items():
            self.assertEqual('/usr/bin/busctl', argv[0])
            self.assertEqual('--timeout=2s', argv[3])
            self.assertEqual('get-property', argv[4])
            self.assertEqual(name, argv[-1])
            self.assertFalse(any(x in argv for x in ('call', 'set-property', 'start', 'stop', '/usr/bin/dotnet')))
    def test_unknown_helper_operation_before_backend_call(self):
        helper = object.__new__(m.ReadOnlyHelpers)
        helper.backend = None
        with self.assertRaises(ValueError): helper.get('StartTransientUnit')
    def test_helper_cleanup_missing_terminal_refuses(self):
        helper = object.__new__(m.ReadOnlyHelpers)
        class Backend: command_receipts = [{'cleanupVerified': False, 'remainingMembers': [999], 'exitCode': None}]
        helper.backend = Backend()
        with self.assertRaises(ValueError): helper.release_evidence()
    def test_helper_terminal_receipt_requires_birth_and_caps(self):
        helper = object.__new__(m.ReadOnlyHelpers)
        class Backend: command_receipts = []
        helper.backend = Backend()
        row = {'cleanupVerified': True, 'remainingMembers': [], 'exitCode': 0,
               'pid': 123, 'birthTicks': '1001', 'executable': '/usr/bin/busctl',
               'memoryBytes': 256*1024**2, 'cpuSeconds': 10, 'timeoutSeconds': 3}
        helper.backend.command_receipts = [row]
        self.assertEqual([row], helper.release_evidence())
        for name, value in (('pid', True), ('birthTicks', None), ('memoryBytes', 0), ('timeoutSeconds', 0)):
            helper.backend.command_receipts = [dict(row, **{name: value})]
            with self.subTest(name=name), self.assertRaises(ValueError): helper.release_evidence()
    def test_sealed_helper_bytes_load_without_process(self):
        path = Path(__file__).resolve().parent/'employee_linux_readonly_helpers.py'
        module = m.sealed_helper(path)
        self.assertEqual(2, len(module.READ_ONLY_ARGV))
        self.assertTrue(callable(module.FiniteReadOnlyHelpers._command))
        self.assertFalse(hasattr(module.FiniteReadOnlyHelpers, 'start_transient'))
    def test_wrong_helper_seal_refused_before_exec(self):
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp)/'unsealed.py'; file.write_text('raise AssertionError("executed")')
            with self.assertRaises(ValueError): m.sealed_helper(file)


class ParserControls(unittest.TestCase):
    def test_scalar_string_property_uses_original_byte_hash(self):
        raw = b'{"type":"s","data":"255.4-1ubuntu8"}'
        result = m.manager_property(raw, 'Version')
        self.assertEqual('255.4-1ubuntu8', result['value'])
        self.assertEqual(m.hashlib.sha256(raw).hexdigest(), result['rawSha256'])
    def test_scalar_system_state_is_typed_and_bounded(self):
        self.assertEqual('degraded', m.manager_property(b'{"type":"s","data":"degraded"}', 'SystemState')['value'])
        for raw in (b'{"type":"s","data":"future"}', b'{"type":"s","data":"running\\nprivate"}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.manager_property(raw, 'SystemState')
    def test_scalar_wrong_type_and_container_layouts_still_refuse(self):
        for raw in (b'{"type":"s","data":true}', b'{"type":"s","data":255}',
                    b'{"type":"s","data":null}', b'{"type":"s","data":{"value":"255"}}',
                    b'{"type":"s","data":[["255"]]}', b'{"type":"s","data":["255","extra"]}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.manager_property(raw, 'Version')
    def test_scalar_duplicate_unknown_and_empty_fields_still_refuse(self):
        for raw in (b'{"type":"s","data":"255","data":"256"}', b'{"type":"s","data":"255","x":1}',
                    b'{"type":"u","data":"255"}', b'{"type":"s","data":""}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.manager_property(raw, 'Version')
    def test_manager_signature_and_raw_hash(self):
        raw = b'{"type":"s","data":["255 synthetic"]}'
        self.assertEqual('255 synthetic', m.manager_property(raw, 'Version')['value'])
        self.assertEqual(64, len(m.manager_property(raw, 'Version')['rawSha256']))
    def test_unknown_manager_signature_refused(self):
        with self.assertRaises(ValueError): m.manager_property(b'{"type":"as","data":[["255"]]}', 'Version')
    def test_manager_extra_and_duplicate_fields_refused(self):
        for raw in (b'{"type":"s","data":["255"],"extra":1}',
                    b'{"type":"s","type":"s","data":["255"]}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.manager_property(raw, 'Version')
    def test_manager_unknown_state_refused(self):
        with self.assertRaises(ValueError): m.manager_property(b'{"type":"s","data":["future-unknown"]}', 'SystemState')
    def test_manager_controls_empty_array_and_nonfinite_refused(self):
        for raw in (b'{"type":"s","data":["255\\nsecret"]}', b'{"type":"s","data":[]}', b'{"x":NaN}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.manager_property(raw, 'Version')
    def test_memfree_physical_not_available(self):
        self.assertEqual(123, m.mem_free(b'MemFree: 123 kB\nMemAvailable: 999999 kB\n'))
    def test_duplicate_missing_or_wrong_mem_units_refused(self):
        for raw in (b'MemAvailable: 9 kB', b'MemFree: 1 kB\nMemFree: 2 kB', b'MemFree: 1 MB'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.mem_free(raw)
    def test_unified_cgroup_exact_path(self):
        self.assertEqual('/system.slice/owned.service', m.unified_cgroup(b'0::/system.slice/owned.service\n'))
        self.assertEqual('/', m.unified_cgroup(b'0::/\n'))
    def test_unknown_cgroup_and_traversal_refused(self):
        for raw in (b'1:cpu:/owned\n', b'0::/a/../b\n', b'0::/a\n0::/b\n', b'0:://a\n'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): m.unified_cgroup(raw)
    def test_proc_stat_parentheses_birth(self):
        fields = ['S']+['1']*18+['998']+['0']*4
        raw = ('123 (synthetic (worker)) '+' '.join(fields)).encode()
        self.assertEqual('998', m.proc_birth(raw, 123))
    def test_proc_wrong_pid_bool_and_short_layout_refused(self):
        for raw, pid in ((b'123 (x) S 1', 123), (b'123 (x) S 1', True), (b'124 (x) '+b'1 '*25, 123)):
            with self.subTest(pid=pid), self.assertRaises(ValueError): m.proc_birth(raw, pid)
    def test_all_input_bounds_refused(self):
        for fn in (m.parse, m.mem_free):
            with self.assertRaises(ValueError): fn(b' '*65537)
        with self.assertRaises(ValueError): m.unified_cgroup(b' '*8193)
    def test_bounded_file_read_and_release(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'raw'; path.write_bytes(b'12345')
            with self.assertRaises(ValueError): m.bounded_read(path, 4)
            self.assertEqual(b'12345', m.bounded_read(path, 5))
    def test_directory_not_regular_source_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError): m.bounded_read(temp)


if __name__ == '__main__': unittest.main()
