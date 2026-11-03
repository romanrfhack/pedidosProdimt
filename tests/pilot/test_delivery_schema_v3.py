"""I22 v3 fixes empty SQL arrays without weakening schema comparison or payload pins."""
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fixtures = load('i22_v3_package_fixtures', ROOT / 'tests/pilot/test_delivery_package.py')
diagnostic = load('i22_v3_diagnosis_fixtures', ROOT / 'tests/pilot/test_delivery_diagnosis.py')
native = load('i22_v3_native_fixtures', ROOT / 'tests/pilot/test_delivery_validation.py')
installer = fixtures.installer


def legacy_source():
    raw = (ROOT / 'scripts/pilot/delivery-schema-contract.py').read_bytes()
    return raw.replace(b'JSON_QUERY(COALESCE((SELECT ', b'JSON_QUERY((SELECT ').replace(
        b"FOR JSON PATH), N'[]')) AS", b'FOR JSON PATH)) AS')


class DeliverySchemaV3Tests(unittest.TestCase):
    def test_legacy_empty_query_reproduces_missing_sections_and_corrected_query_returns_arrays(self):
        legacy = installer.module('unadapted_legacy_schema', legacy_source())
        schema = installer.module('i22_delivery_schema_support', legacy_source())
        empty = {key: [] for key in ('columns','primaryKeys','foreignKeys','indexes','checks')}
        def observed(query):
            return empty if query.count("FOR JSON PATH), N'[]')) AS") == 5 else {}
        for strict in (False, True):
            self.assertEqual(observed(legacy.schema_query(strict)), {})
            self.assertFalse(legacy.matches(observed(legacy.schema_query(strict)), empty, strict))
            self.assertEqual(observed(schema.schema_query(strict)), empty)
            self.assertTrue(schema.matches(observed(schema.schema_query(strict)), empty, strict))
            self.assertFalse(schema.matches({}, empty, strict))

    def test_adapter_only_changes_five_null_fallbacks_and_is_idempotent(self):
        legacy = installer.module('unadapted_legacy_schema', legacy_source())
        for name in ('i22_delivery_schema_validator','i22_delivery_schema_support'):
            schema = installer.module(name, legacy_source())
            for strict in (False, True):
                old, fixed = legacy.schema_query(strict), schema.schema_query(strict)
                self.assertEqual(fixed, fixtures.schema.schema_query(strict))
                self.assertEqual(installer.correct_delivery_schema_query(fixed), fixed)
                self.assertEqual(fixed.count('JSON_QUERY(COALESCE((SELECT '), 5)
                self.assertEqual(fixed.replace('JSON_QUERY(COALESCE((SELECT ', 'JSON_QUERY((SELECT ').replace(
                    "FOR JSON PATH), N'[]')) AS", 'FOR JSON PATH)) AS'), old)
            self.assertEqual(schema.matches.__code__.co_code, legacy.matches.__code__.co_code)
            self.assertEqual(schema.canonical.__code__.co_code, legacy.canonical.__code__.co_code)

    def test_mixed_or_unrecognized_query_layout_is_refused(self):
        original = installer.module('unadapted_legacy_schema', legacy_source()).schema_query(False)
        for sql in ('SELECT 1', original.replace('JSON_QUERY((SELECT ', 'JSON_QUERY(COALESCE((SELECT ', 1),
                    original.replace(' AS checks\n', ' AS unrelated\n'), original + '\nJSON_QUERY(N\'[]\')'):
            with self.subTest(sql=sql[:40]), self.assertRaisesRegex(installer.InstallError, 'delivery_schema_query_layout_unexpected'):
                installer.correct_delivery_schema_query(sql)

    def test_full_schema_and_drift_checks_remain_strict(self):
        schema = installer.module('i22_delivery_schema_support', legacy_source())
        sql = (ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text(encoding='utf-8-sig')
        contract = schema.contract_from_sql(sql)
        self.assertTrue(schema.matches(contract, contract, True))
        self.assertFalse(schema.matches({}, contract, False))
        self.assertFalse(schema.matches({key: [] for key in contract}, contract, False))
        for section, key, value in [('columns','scale',2), ('foreignKeys','deleteAction',1),
                                    ('primaryKeys','disabled',1), ('checks','definition','[TripNumber] > 999')]:
            changed = json.loads(json.dumps(contract))
            changed[section][0][key] = value
            self.assertFalse(schema.matches(changed, contract, True))

    def test_real_preflight_accepts_empty_arrays_with_legacy_payload_schema(self):
        files = diagnostic.fixtures.supports()
        files['delivery-schema-contract.py'] = legacy_source()
        with patch.object(diagnostic.fixtures, 'supports', return_value=files):
            result, captures = diagnostic.DeliveryDiagnosisTests().composition(
                {key: [] for key in ('columns','primaryKeys','foreignKeys','indexes','checks')})
        self.assertEqual(result, dict(status='passed', issue=None))
        self.assertIs(captures[0]['comparison_result'], True)
        self.assertFalse(captures[0]['migrated_schema_expected'])

    def test_inspector_allowlist_accepts_corrected_query_and_refuses_old_query(self):
        schema = installer.module('i22_delivery_schema_support', legacy_source())
        legacy = installer.module('unadapted_legacy_schema', legacy_source())
        inventory = installer.module('fixture_inventory', (ROOT / 'scripts/pilot/inspect-native.py').read_bytes())
        inventory.SQL_TOOLS = ('fixture-sqlcmd',)
        inventory.QUERIES = (schema.schema_query(True), schema.schema_query(False))
        inventory.permitted_connection = lambda _: True
        empty = {key: [] for key in ('columns','primaryKeys','foreignKeys','indexes','checks')}
        inventory.command = Mock(return_value=(0,'ProdimtInspectionJson\n--------------------\n'+json.dumps(empty)))
        self.assertEqual(inventory.query_sql('fixture-sqlcmd',{'password':'synthetic'},schema.schema_query(False)),empty)
        with self.assertRaisesRegex(inventory.InspectionError,'sql_query_not_authorized'):
            inventory.query_sql('fixture-sqlcmd',{'password':'synthetic'},legacy.schema_query(False))
        inventory.command.assert_called_once()

    def test_exact_sql_validation_uses_the_same_corrected_metadata_queries(self):
        files = fixtures.supports()
        files['delivery-schema-contract.py'] = legacy_source()
        schema = installer.module('i22_delivery_schema_support', files['delivery-schema-contract.py'])
        transport = native.SyntheticSql(files)
        read = transport.query
        queries = []
        def query(sql, as_application=False):
            queries.append(sql)
            return read(sql, as_application)
        transport.query = query
        summary = native.validation.validate_exact_sql(files, installer, schema, '/synthetic/sqlcmd',
            '127.0.0.1,14330','synthetic','synthetic','OPTIONS\n',{'PATH':'/usr/bin:/bin'},json.loads,transport.factory)
        self.assertTrue(summary['cleanupVerified'])
        for strict in (False, True):
            self.assertIn(schema.schema_query(strict), queries)
            self.assertEqual(schema.schema_query(strict), fixtures.schema.schema_query(strict))


if __name__ == '__main__':
    unittest.main()
