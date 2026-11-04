"""C1 native deploy boundaries: exact base, ordered gates and forward recovery."""
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PILOT = ROOT / 'scripts/pilot'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


installer = load('c1_installer_under_test', PILOT / 'install-consolidation-c1.py')
validation = load('c1_validation_for_installer', PILOT / 'validate-consolidation-c1-sql.py')


def upgrade():
    return ("BEGIN TRANSACTION;\nALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(max) NULL;\n"
            "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\n"
            "VALUES (N'" + installer.MIGRATION + "', N'10.0.8');\nCOMMIT;\nGO\n\n")


def downgrade():
    return ("BEGIN TRANSACTION;\nIF EXISTS (SELECT 1 FROM [AuditLogs] WHERE DATALENGTH([MetadataJson]) > 4000)\n"
            " THROW 51024, N'" + validation.DOWNGRADE_MARKER + "', 1;\n"
            "ALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(2000) NULL;\n"
            "DELETE FROM [__EFMigrationsHistory]\nWHERE [MigrationId] = N'" + installer.MIGRATION + "';\nCOMMIT;\nGO\n\n")


def supports():
    sources = {
        'inspect-native.py': 'inspect-native.py',
        'native-deployment-support.py': 'install-quick-capture-update.py',
        'commercial-catalog-support.py': 'install-commercial-catalog-update.py',
        'token-management-support.py': 'install-access-token-management-update.py',
        'customer-products-support.py': 'install-customer-products-update.py',
        'security-administration-support.py': 'install-identity-update.py',
        'delivery-support.py': 'install-delivery-update.py',
        'delivery-schema-contract.py': 'delivery-schema-contract.py',
        'ordering-schedules-support.py': 'install-ordering-schedules-on-delivery-update.py',
        'validate-consolidation-c1-sql.py': 'validate-consolidation-c1-sql.py'}
    files = {target: (PILOT / source).read_bytes() for target, source in sources.items()}
    schema = load('c1_install_schema', PILOT / 'delivery-schema-contract.py')
    files['delivery-schema.json'] = json.dumps(schema.contract_from_sql(
        (ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text())).encode()
    files['migrations-c1.sql'] = upgrade().encode()
    files['rollback-c1.sql'] = downgrade().encode()
    files['validation-baseline.sql'] = ''.join(
        "BEGIN TRANSACTION;\nINSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\nVALUES (N'" + value +
        "', N'10.0.8');\nCOMMIT;\nGO\n\n" for value in validation.BASE_IDS).encode()
    return files


def gate(**changes):
    value = dict(schema='prodimt.c1.browser-gate.v1', status='passed', sourceCommit=installer.EXPECTED_SOURCE,
                 api='mock', suite='all', expectedCases=240, collected=240, passed=240,
                 failed=0, skipped=0, flaky=0, exitCode=0, sqlServerIncluded=False,
                 humanAcceptance=False, reportSha256='a' * 64)
    value.update(changes)
    return json.dumps(value).encode()


class C1InstallTests(unittest.TestCase):
    def instance(self):
        with patch.object(installer, 'load_payload', return_value=supports()):
            value = installer.make_installer(Path('/synthetic-package'))
        value.inspector = types.SimpleNamespace(SQL_MIGRATIONS='history')
        return value

    def test_exact_installed_pair_and_no_sibling_baselines(self):
        self.assertEqual(installer.PREVIOUS_PAIRS, (('ordering-schedules-i23-aa802e210d15',) * 2,))
        pairs = list(installer.PREVIOUS_PAIRS) + [('delivery-0cfe3ef511d8', 'delivery-flow-426c360e1365'),
            ('ordering-schedules-i23-aa802e210d15', 'other')]
        for pair in pairs:
            with self.subTest(pair=pair), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for part, name in zip(('backend', 'frontend'), pair):
                    release = root / part / 'releases' / name
                    release.mkdir(parents=True)
                    (root / part / 'current').symlink_to(release)
                with patch.object(installer, 'ROOT', root), patch.object(installer, 'BACKUPS', root / 'backups'):
                    value = self.instance()
                    value.runtime.directory_safe = Mock()
                    value.process = Mock(side_effect=installer.InstallError('accepted-pair'))
                    with self.assertRaises(installer.InstallError) as error:
                        value.preflight()
                    self.assertEqual(str(error.exception), 'accepted-pair' if pair in installer.PREVIOUS_PAIRS else 'previous_release_not_supported')
                    self.assertFalse(value.stop_requested)
                    self.assertFalse(value.state['changes_made'])

    def test_history_requires_exact_18_or_19_ids_not_only_count(self):
        value = self.instance()
        for ids in (validation.BASE_IDS, validation.BASE_IDS + [installer.MIGRATION]):
            self.assertEqual(value.history(lambda _: [{'MigrationId': name} for name in ids]), ids)
        for ids in (validation.BASE_IDS[:-1], validation.BASE_IDS[:-1] + ['other'],
                    validation.BASE_IDS + ['other'], validation.BASE_IDS + [installer.MIGRATION, 'other']):
            with self.assertRaises(installer.InstallError):
                value.history(lambda _: [{'MigrationId': name} for name in ids])

    def test_missing_browser_evidence_precedes_sql_credentials_and_staging(self):
        value = self.instance()
        value.preflight = Mock()
        value.verify_browser_gate = Mock(side_effect=installer.InstallError('browser-missing'))
        value.authenticate_admin, value.validate_before_apply, value.stage = Mock(), Mock(), Mock()
        with self.assertRaisesRegex(installer.InstallError, 'browser-missing'):
            value.apply()
        value.authenticate_admin.assert_not_called()
        value.validate_before_apply.assert_not_called()
        value.stage.assert_not_called()
        self.assertFalse(value.stop_requested)

    def test_browser_gate_requires_same_source_all_cases_and_no_omissions(self):
        value = self.instance()
        with patch.object(installer, 'root_regular', return_value=gate()):
            value.verify_browser_gate()
        self.assertTrue(value.state['browser_gate_verified'])
        for changes in ({'sourceCommit': 'other'}, {'passed': 239}, {'skipped': 1}, {'flaky': 1},
                        {'failed': 1}, {'collected': 239}, {'api': 'real'}, {'exitCode': 1}, {'suite': 'partial'}, {'expectedCases': 239}, {'reportSha256': 'bad'}):
            with self.subTest(changes=changes), patch.object(installer, 'root_regular', return_value=gate(**changes)):
                with self.assertRaises(installer.InstallError):
                    value.verify_browser_gate()

    def test_fresh_successful_browser_gate_for_same_source_allows_recovery(self):
        value = self.instance()
        value.recovery = dict(browserGateSha256='older-successful-report-hash')
        with patch.object(installer, 'root_regular', return_value=gate(reportSha256='b' * 64)):
            value.verify_browser_gate()
        self.assertTrue(value.state['browser_gate_verified'])
        with patch.object(installer, 'root_regular', return_value=gate(sourceCommit='different-source')):
            with self.assertRaisesRegex(installer.InstallError, 'browser_gate_incomplete'):
                value.verify_browser_gate()

    def test_isolated_sql_failure_precedes_snapshot_stage_and_stop(self):
        value = self.instance()
        value.preflight, value.verify_browser_gate, value.authenticate_admin = Mock(), Mock(), Mock()
        value.validate_before_apply = Mock(side_effect=installer.InstallError('sql-validation-failed'))
        value.stage, value.write_marker = Mock(), Mock()
        with self.assertRaisesRegex(installer.InstallError, 'sql-validation-failed'):
            value.apply()
        value.stage.assert_not_called()
        value.write_marker.assert_not_called()
        self.assertFalse(value.stop_requested)
        self.assertFalse(value.state['changes_made'])

    def migration_fixture(self, directory, mutate=False):
        value = self.instance()
        value.snapshot = directory
        value.state['migration_ids_before'] = validation.BASE_IDS.copy()
        value.schemas = Mock(return_value=validation.BASE_IDS.copy())
        value.history = Mock(return_value=validation.BASE_IDS + [installer.MIGRATION])
        value.verify_schema = Mock(return_value=19)
        value.app_permissions = Mock()
        raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
        value.environment = raw
        token = load('c1_test_token', PILOT / 'install-access-token-management-update.py')
        value.token_encryption_key = token.environment_key(raw)
        value.preservation = Mock(side_effect=[{'tables': 'original'}, {'tables': 'changed' if mutate else 'original'}])
        events = []
        def backup():
            events.append('verified-backup')
            value.state['verified_database_backup'] = '/verified-backup'
        value.database_backup = Mock(side_effect=backup)
        def execute(sql, timeout):
            self.assertEqual(events, ['verified-backup'])
            self.assertEqual(sql, upgrade())
            self.assertEqual(json.loads((directory / 'business-preservation.json').read_text()), {'tables': 'original'})
            events.append('c1-ddl')
        value.sql_run = Mock(side_effect=execute)
        return value, raw, events

    def test_verified_backup_and_durable_fingerprint_precede_exact_delta(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, events = self.migration_fixture(Path(folder))
            with patch.object(installer, 'regular', return_value=raw):
                value.backup_and_migrate()
            self.assertEqual(events, ['verified-backup', 'c1-ddl'])
            self.assertEqual(value.state['migrations_after'], 19)
            self.assertTrue(value.state['audit_metadata_preserved'])
            self.assertEqual((Path(folder) / 'business-preservation.json').stat().st_mode & 0o777, 0o600)

    def test_missing_backup_prevents_any_production_ddl(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder))
            value.database_backup = Mock()
            with patch.object(installer, 'regular', return_value=raw):
                with self.assertRaisesRegex(installer.InstallError, 'verified_database_backup_required'):
                    value.backup_and_migrate()
            value.sql_run.assert_not_called()

    def test_any_business_mutation_blocks_activation(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder), mutate=True)
            with patch.object(installer, 'regular', return_value=raw):
                with self.assertRaisesRegex(installer.InstallError, 'existing_business_data_changed'):
                    value.backup_and_migrate()
            self.assertNotIn('migrated_schema_verified', value.state)

    def test_interruption_after_marker_before_fingerprint_recovers_only_from_exact_18(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, events = self.migration_fixture(Path(folder))
            value.recovery = dict(migrationVerified=False)
            self.assertFalse((Path(folder) / 'business-preservation.json').exists())
            with patch.object(installer, 'regular', return_value=raw):
                value.backup_and_migrate()
            self.assertEqual(events, ['verified-backup', 'c1-ddl'])
            self.assertEqual(value.state['migrations_after'], 19)

    def test_unverified_19_without_fingerprint_never_creates_a_new_baseline(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder))
            ids = validation.BASE_IDS + [installer.MIGRATION]
            value.state['migration_ids_before'] = ids
            value.schemas = Mock(return_value=ids)
            value.recovery = dict(migrationVerified=False)
            with self.assertRaisesRegex(installer.InstallError, 'recovery_preservation_snapshot_required'):
                value.backup_and_migrate()
            value.database_backup.assert_not_called()
            value.sql_run.assert_not_called()
            self.assertFalse((Path(folder) / 'business-preservation.json').exists())

    def test_recovery_cannot_rebaseline_mutated_data(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder))
            (Path(folder) / 'business-preservation.json').write_text(json.dumps({'tables': 'different'}))
            with patch.object(installer, 'private', return_value=json.dumps({'tables': 'different'}).encode()):
                with self.assertRaisesRegex(installer.InstallError, 'existing_business_data_changed_since_snapshot'):
                    value.backup_and_migrate()
            value.database_backup.assert_not_called()
            value.sql_run.assert_not_called()

    def test_migration_verified_checkpoint_is_written_before_first_activation(self):
        value = self.instance()
        value.preflight, value.verify_browser_gate, value.authenticate_admin, value.validate_before_apply = Mock(), Mock(), Mock(), Mock()
        value.recovery = dict(migrationVerified=False)
        value.snapshot = Path('/synthetic-snapshot')
        value.old = {part: Path('/old') for part in ('backend', 'frontend')}
        value.environment = value.original_dll = b'synthetic'
        value.overrides = {}
        events = []
        value.stage = Mock()
        value.write_marker = Mock(side_effect=lambda: events.append(('checkpoint', value.state.get('migrated_schema_verified', False))))
        value.runtime.command = Mock(side_effect=lambda args, **kwargs: events.append(tuple(args[:2])))
        value.backup_and_migrate = Mock(side_effect=lambda: value.state.update(migrated_schema_verified=True))
        value.switch = Mock(side_effect=lambda _: events.append(('activate',)))
        value.health = Mock()
        value.marker = Mock()
        with patch.object(installer, 'regular', return_value=b'synthetic'):
            value.apply()
        self.assertEqual(events[:4], [('checkpoint', False), ('systemctl', 'stop'), ('checkpoint', True), ('activate',)])
        self.assertEqual(value.state['status'], 'updated')

    def test_verified_recovery_preserves_new_app_writes_without_repeating_ddl(self):
        value = self.instance()
        ids = validation.BASE_IDS + [installer.MIGRATION]
        value.state['migration_ids_before'] = ids
        value.schemas = Mock(return_value=ids)
        value.recovery = dict(migrationVerified=True)
        value.verify_schema = Mock(return_value=19)
        value.app_permissions, value.sql_run, value.preservation, value.database_backup = Mock(), Mock(), Mock(), Mock()
        value.backup_and_migrate()
        value.sql_run.assert_not_called()
        value.preservation.assert_not_called()
        value.database_backup.assert_not_called()
        value.app_permissions.assert_called_once_with(ids)
        self.assertTrue(value.state['migration_recovery_already_verified'])

    def test_verified_recovery_cannot_resume_when_schema_is_not_target(self):
        value = self.instance()
        value.state['migration_ids_before'] = validation.BASE_IDS
        value.schemas = Mock(return_value=validation.BASE_IDS)
        value.recovery = dict(migrationVerified=True)
        value.verify_schema, value.sql_run, value.database_backup = Mock(), Mock(), Mock()
        with self.assertRaisesRegex(installer.InstallError, 'verified_recovery_schema_changed'):
            value.backup_and_migrate()
        value.sql_run.assert_not_called()
        value.database_backup.assert_not_called()

    def test_post_ddl_failure_preserves_forward_recovery_with_service_stopped(self):
        value = self.instance()
        value.stop_requested = True
        value.history_before = validation.BASE_IDS
        value.schemas = Mock(return_value=validation.BASE_IDS + [installer.MIGRATION])
        value.sql_json, value.runtime.command = Mock(), Mock()
        value.switch, value.health = Mock(), Mock()
        value.rollback()
        self.assertTrue(value.state['recovery_pending'])
        self.assertTrue(value.state['application_stopped_for_security'])
        self.assertFalse(value.state['database_automatically_restored'])
        value.switch.assert_not_called()
        value.health.assert_not_called()

    def test_history_alone_does_not_authorize_old_binary_after_partial_schema_change(self):
        value = self.instance()
        value.stop_requested = True
        value.history_before = validation.BASE_IDS
        value.schemas = Mock(side_effect=installer.InstallError('audit_metadata_schema_unexpected'))
        value.sql_json, value.runtime.command, value.switch = Mock(), Mock(), Mock()
        value.rollback()
        self.assertTrue(value.state['recovery_pending'])
        value.switch.assert_not_called()

    def test_current_release_skips_browser_sql_and_restart(self):
        value = self.instance()
        value.state['status'] = 'already_current'
        value.preflight, value.verify_browser_gate, value.authenticate_admin = Mock(), Mock(), Mock()
        value.apply()
        value.verify_browser_gate.assert_not_called()
        value.authenticate_admin.assert_not_called()
        self.assertFalse(value.stop_requested)

    def portable(self, output, returncode=0, cleanup_result=True):
        child = Mock()
        child.communicate.return_value = (output, 'secret stderr never emitted')
        child.returncode = returncode
        context = Mock()
        context.__enter__ = Mock(return_value=child)
        context.__exit__ = Mock(return_value=None)
        cleanup = Mock(return_value=cleanup_result)
        delivery = types.SimpleNamespace(validated_dotnet_executable=Mock(return_value=Path('/dotnet')))
        return context, cleanup, delivery

    def test_portable_requires_all_seven_cases_and_cleans_both_generated_databases(self):
        names = ['ProdimtI02Test_' + 'a' * 32, 'ProdimtI02Test_' + 'b' * 32]
        output = '\n'.join('DATABASE ' + name for name in names) + '\n' + json.dumps(dict(
            schema='prodimt.consolidation.c1.validation.v1', status='passed', passed=7, expected=7, cleanupVerified=True))
        context, cleanup, delivery = self.portable(output)
        with patch.object(installer.subprocess, 'Popen', return_value=context) as process:
            result = installer.run_portable_validation({}, '/dotnet', 'synthetic secret', {}, cleanup, delivery)
        self.assertEqual(result['passed'], 7)
        self.assertEqual([call.args[0] for call in cleanup.call_args_list], names)
        self.assertEqual(process.call_args.args[0][-1], '--validate-c1')
        self.assertNotIn('synthetic secret', str(process.call_args.args))

    def test_portable_delivery_only_summary_is_rejected(self):
        output = 'DATABASE ProdimtI02Test_' + 'a' * 32 + '\n' + json.dumps(dict(
            schema='prodimt.delivery.validation.v1', status='passed', passed=3, expected=3, cleanupVerified=True))
        context, cleanup, delivery = self.portable(output)
        with patch.object(installer.subprocess, 'Popen', return_value=context):
            with self.assertRaisesRegex(installer.InstallError, 'portable_validation_incomplete'):
                installer.run_portable_validation({}, '/dotnet', '', {}, cleanup, delivery)
        self.assertEqual(cleanup.call_count, 1)

    def test_portable_failed_cleanup_still_attempts_every_announced_database(self):
        output = 'DATABASE ProdimtI02Test_' + 'a' * 32 + '\nDATABASE ProdimtI02Test_' + 'b' * 32 + '\n'
        context, cleanup, delivery = self.portable(output, returncode=1, cleanup_result=False)
        with patch.object(installer.subprocess, 'Popen', return_value=context):
            with self.assertRaisesRegex(installer.InstallError, 'portable_validation_database_cleanup_failed'):
                installer.run_portable_validation({}, '/dotnet', '', {}, cleanup, delivery)
        self.assertEqual(cleanup.call_count, 2)


if __name__ == '__main__':
    unittest.main()
