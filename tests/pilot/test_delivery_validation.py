"""Pre-deployment validation gates, exact SQL isolation and private child input."""
import importlib.util
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fixtures = load('i22_validation_fixtures', ROOT / 'tests/pilot/test_delivery_package.py')
installer, schema, validation = fixtures.installer, fixtures.schema, fixtures.validation


class SyntheticSql:
    def __init__(self, files):
        self.files = files
        self.contract = json.loads(files['delivery-schema.json'])
        self.exists, self.upgraded = False, False
        self.calls = []
        self.creation_uncertain = False
        self.rollback_bad = False
        self.permissions_bad = False
        self.cleanup_bad = False

    def factory(self, tool, server, login, password, options, clean_environment, parse_output, database):
        self.database = database
        return self

    def run(self, database, sql, timeout=120, expected_failure=False):
        if database not in ('master', self.database):
            raise AssertionError('Only the generated DB and master may be targeted')
        self.calls.append((database, sql, expected_failure))
        if sql.startswith('CREATE DATABASE'):
            self.exists = True
            if self.creation_uncertain:
                raise validation.ValidationFailure('validation_sql_transport_failed')
        elif 'DROP DATABASE [' in sql:
            if self.cleanup_bad:
                raise validation.ValidationFailure('validation_sql_transport_failed')
            self.exists = False
        elif 'THEN 1 ELSE 0 END AS cleaned' in sql:
            return json.dumps({'cleaned': int(not self.exists)})
        elif installer.MIGRATION in sql:
            if expected_failure:
                if self.rollback_bad:
                    self.upgraded = True
            else:
                self.upgraded = True
        return ''

    def query(self, query, as_application=False):
        if query == validation.history_query():
            return [{'MigrationId': value} for value in validation.BASE_IDS + ([installer.MIGRATION] if self.upgraded else [])]
        if query in (schema.schema_query(True), schema.schema_query(False)):
            data = self.contract if self.upgraded else {key: [] for key in self.contract}
            data = json.loads(json.dumps(data))
            if as_application:
                for check in data['checks']:
                    check.pop('definition', None)
            return data
        if query == installer.SECURITY_FINGERPRINT:
            return {'synthetic': 'unchanged'}
        if query == installer.DELIVERY_SQL_PERMISSIONS:
            result = installer.EXPECTED_DELIVERY_PERMISSIONS.copy()
            if self.permissions_bad:
                result['DeliveryEvents_UPDATE'] = 1
            return result
        if query == installer.DELIVERY_ROLE_DATA:
            return dict(driver_roles=1, driver_grants=2, driver_total_grants=2)
        if query == installer.DELIVERY_COUNTS:
            return dict.fromkeys(installer.DELIVERY_TABLES, 0)
        if query.startswith('SELECT CONVERT(varchar(36),Revision)'):
            return {'revision': 'synthetic-revision'}
        raise AssertionError('Unexpected fixture query')


class SyntheticChild:
    def __init__(self, output, timeout=False, returncode=0):
        self.output, self.timeout, self.returncode = output, timeout, returncode
        self.killed, self.communications = False, 0

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False

    def communicate(self, timeout):
        self.communications += 1
        if self.timeout and self.communications == 1:
            raise subprocess.TimeoutExpired('dotnet', timeout, output=self.output)
        return self.output, 'synthetic private stderr'

    def kill(self):
        self.killed = True
        self.returncode = -9


class DeliveryValidationTests(unittest.TestCase):
    def exact(self, fake):
        return validation.validate_exact_sql(fake.files, installer, schema, '/synthetic/sqlcmd',
            '127.0.0.1,14330', 'synthetic-admin', 'not-a-real-password', 'OPTIONS\n',
            {'PATH': '/usr/bin:/bin'}, json.loads, fake.factory)

    def child_output(self):
        name = 'ProdimtI02Test_' + 'a' * 32
        summary = dict(schema='prodimt.delivery.validation.v1', status='passed', passed=3,
                       expected=3, cleanupVerified=True, cleanupDatabase=None)
        return name, 'DATABASE ' + name + '\n' + json.dumps(summary) + '\n'

    def test_exact_upgrade_rollback_grants_metadata_replay_and_cleanup_use_only_generated_db(self):
        fake = SyntheticSql(fixtures.supports())
        summary = self.exact(fake)
        self.assertTrue(summary['rollbackVerified'])
        self.assertTrue(summary['replayVerified'])
        self.assertTrue(summary['cleanupVerified'])
        self.assertFalse(fake.exists)
        self.assertRegex(fake.database, r'^ProdimtI22Validation_[a-f0-9]{32}$')
        self.assertTrue(all(database in ('master', fake.database) for database, _, _ in fake.calls))
        self.assertFalse(any('DROP DATABASE [ProdimtPedidos]' in sql or 'RESTORE ' in sql for _, sql, _ in fake.calls))
        self.assertEqual(sum(expected_failure for _, _, expected_failure in fake.calls), 1)

    def test_uncertain_create_response_still_attempts_drop_and_verifies_cleanup(self):
        fake = SyntheticSql(fixtures.supports())
        fake.creation_uncertain = True
        with self.assertRaisesRegex(validation.ValidationFailure, 'validation_sql_transport_failed'):
            self.exact(fake)
        self.assertFalse(fake.exists)
        self.assertTrue(any('DROP DATABASE [' + fake.database + ']' in sql for _, sql, _ in fake.calls))

    def test_failed_transaction_rollback_or_append_only_permission_blocks_and_cleans(self):
        for option, issue in [('rollback_bad', 'validation_transaction_rollback_failed'),
                              ('permissions_bad', 'validation_application_permissions_mismatch')]:
            with self.subTest(option=option):
                fake = SyntheticSql(fixtures.supports())
                setattr(fake, option, True)
                with self.assertRaisesRegex(validation.ValidationFailure, issue):
                    self.exact(fake)
                self.assertFalse(fake.exists)

    def test_cleanup_failure_returns_only_generated_catalog_name(self):
        fake = SyntheticSql(fixtures.supports())
        fake.cleanup_bad = True
        with self.assertRaises(validation.ValidationFailure) as caught:
            self.exact(fake)
        self.assertEqual(str(caught.exception), 'validation_database_cleanup_failed')
        self.assertEqual(caught.exception.cleanup_database, fake.database)

    def test_configured_or_operational_catalog_cannot_be_used_for_test_transport(self):
        for value in ('ProdimtPedidos', 'master', 'ProdimtI22Validation_../../../x', 'ProdimtI02Test_prod'):
            with self.subTest(value=value), self.assertRaises(validation.ValidationFailure):
                validation.Transport('/sqlcmd', 'server', 'login', 'password', '', {}, json.loads, value)

    def test_cleanup_metadata_uses_named_sqlcmd_json_column(self):
        fake = SyntheticSql(fixtures.supports())
        name = 'ProdimtI22Validation_' + 'b' * 32
        self.assertTrue(validation.cleanup_generated_database('/sqlcmd', 'server', 'login', 'password', '', {},
            json.loads, name, fake.factory))
        self.assertTrue(fake.calls[-1][1].startswith('SELECT (SELECT CASE WHEN DB_ID('))
        self.assertTrue(fake.calls[-1][1].endswith(') AS ProdimtInspectionJson;'))

    def test_ado_connection_quotes_semicolons_and_double_quotes_without_arguments(self):
        connection = installer.validation_connection('127.0.0.1,14330', 'test;login', 'test;"password')
        self.assertIn('User ID="test;login"', connection)
        self.assertIn('Password="test;""password"', connection)
        self.assertIn('Database=master', connection)
        self.assertIn('Connection Timeout=5', connection)
        for value in ('bad\nvalue', 'bad\0value'):
            with self.assertRaises(installer.InstallError):
                installer.validation_connection('server', 'login', value)

    def test_portable_runner_receives_secret_only_in_private_environment_and_proves_cleanup(self):
        name, output = self.child_output()
        child = SyntheticChild(output)
        cleanup = Mock(return_value=True)
        files = {name: b'synthetic' for name in installer.VALIDATION_REQUIRED}
        connection = 'Server=synthetic;Password="private-canary"'
        with patch.object(installer, 'validated_dotnet_executable', return_value=Path('/synthetic/dotnet')), \
                patch.object(installer.subprocess, 'Popen', return_value=child) as process:
            summary = installer.run_portable_validation(files, '/usr/bin/true', connection,
                {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8'}, cleanup)
        args, kwargs = process.call_args
        self.assertNotIn('private-canary', ' '.join(args[0]))
        self.assertEqual(kwargs['env']['PRODIMT_SQL_TEST_CONNECTION'], connection)
        self.assertNotIn('SQLCMDPASSWORD', kwargs['env'])
        self.assertEqual(args[0][-1], '--validate')
        cleanup.assert_called_once_with(name)
        self.assertEqual(summary['passed'], 3)

    def test_portable_timeout_kills_child_then_cleans_generated_db_before_failure(self):
        name, output = self.child_output()
        child = SyntheticChild(output, timeout=True)
        cleanup = Mock(return_value=True)
        with patch.object(installer, 'validated_dotnet_executable', return_value=Path('/synthetic/dotnet')), \
                patch.object(installer.subprocess, 'Popen', return_value=child):
            with self.assertRaisesRegex(installer.InstallError, 'portable_validation_interrupted_or_timed_out'):
                installer.run_portable_validation({name: b'x' for name in installer.VALIDATION_REQUIRED},
                    '/usr/bin/true', 'synthetic', {'PATH': '/usr/bin:/bin'}, cleanup)
        self.assertTrue(child.killed)
        cleanup.assert_called_once_with(name)

    def test_portable_rejects_false_success_or_missing_database_marker(self):
        _, output = self.child_output()
        for content in (output.replace('"passed": 3', '"passed": 2'), output.splitlines()[-1]):
            with self.subTest(content=content), patch.object(installer, 'validated_dotnet_executable', return_value=Path('/synthetic/dotnet')), \
                    patch.object(installer.subprocess, 'Popen', return_value=SyntheticChild(content)):
                with self.assertRaises(installer.InstallError):
                    installer.run_portable_validation({name: b'x' for name in installer.VALIDATION_REQUIRED},
                        '/usr/bin/true', 'synthetic', {'PATH': '/usr/bin:/bin'}, Mock(return_value=True))

    def test_unknown_or_operational_database_marker_is_never_dropped(self):
        _, output = self.child_output()
        output = output.replace('DATABASE ProdimtI02Test_' + 'a' * 32, 'DATABASE ProdimtPedidos')
        cleanup = Mock(return_value=True)
        with patch.object(installer, 'validated_dotnet_executable', return_value=Path('/synthetic/dotnet')), \
                patch.object(installer.subprocess, 'Popen', return_value=SyntheticChild(output)):
            with self.assertRaisesRegex(installer.InstallError, 'portable_validation_database_marker_unrecognized'):
                installer.run_portable_validation({name: b'x' for name in installer.VALIDATION_REQUIRED},
                    '/usr/bin/true', 'synthetic', {'PATH': '/usr/bin:/bin'}, cleanup)
        cleanup.assert_not_called()

    def test_baseline_normalization_only_adds_two_reviewed_go_boundaries(self):
        raw = (ROOT / 'tests/pilot/fixtures/i22-validation-baseline.sql').read_text(encoding='utf-8-sig')
        normalized = fixtures.builder.normalized_validation_baseline(raw)
        self.assertEqual(normalized.count('\nGO\n') - raw.count('\nGO\n'), 2)
        self.assertEqual(normalized.replace('GO\n\n', ''), raw.replace('GO\n\n', ''))
        self.assertEqual(normalized.count('BEGIN TRANSACTION;'), 16)

    def test_apply_validation_failure_precedes_snapshot_staging_marker_and_service_stop(self):
        instance = fixtures.DeliveryPackageTests().instance()
        instance.preflight = Mock()
        instance.authenticate_admin = Mock()
        instance.validate_before_apply = Mock(side_effect=installer.InstallError('synthetic_validation_failed'))
        instance.stage = Mock(side_effect=AssertionError('must not stage'))
        instance.write_marker = Mock(side_effect=AssertionError('must not write deployment state'))
        instance.runtime.command = Mock(side_effect=AssertionError('must not stop service'))
        with self.assertRaisesRegex(installer.InstallError, 'synthetic_validation_failed'):
            instance.apply()
        self.assertFalse(instance.stop_requested)
        self.assertFalse(instance.state['changes_made'])
        self.assertFalse(hasattr(instance, 'snapshot'))
        instance.stage.assert_not_called()
        instance.write_marker.assert_not_called()
        instance.runtime.command.assert_not_called()

    def test_validate_only_never_stages_or_changes_operational_links(self):
        instance = fixtures.DeliveryPackageTests().instance()
        instance.preflight = Mock()
        instance.authenticate_admin = Mock()
        instance.validate_before_apply = Mock()
        instance.stage = Mock(side_effect=AssertionError('no stage'))
        instance.validate_only()
        instance.validate_before_apply.assert_called_once()
        instance.stage.assert_not_called()
        self.assertEqual(instance.state['status'], 'validated')
        self.assertFalse(instance.state['production_changes_made'])
        self.assertFalse(instance.state['changes_made'])


if __name__ == '__main__':
    unittest.main()
