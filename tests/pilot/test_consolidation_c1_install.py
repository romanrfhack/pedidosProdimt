"""C1 native deploy boundaries: exact base, ordered gates and forward recovery."""
import importlib.util
from contextlib import contextmanager
import itertools
import io
import json
import os
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
    def setUp(self):
        reader = patch.object(installer, 'private', side_effect=lambda path: path.read_bytes())
        reader.start()
        self.addCleanup(reader.stop)

    def permission_transport(self, value, audit=None):
        """Independent observed I23 profile; never derive audit bits from the contract."""
        observed = dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1) if audit is None else audit
        mapping = {}
        for filename, queries in (
                ('install-commercial-catalog-update.py', [('PRICE_PERMISSIONS', 'EXPECTED_PRICE_PERMISSIONS')]),
                ('install-customer-products-update.py', [('SETUP_PERMISSIONS', 'EXPECTED_SETUP_PERMISSIONS')]),
                ('install-identity-update.py', [('SECURITY_PERMISSIONS', 'EXPECTED_SECURITY_PERMISSIONS')]),
                ('install-ordering-schedules-on-delivery-update.py', [
                    ('PRODUCTION_SQL_PERMISSIONS', 'EXPECTED_PRODUCTION_PERMISSIONS'),
                    ('SCHEDULE_PERMISSIONS', 'EXPECTED_SCHEDULE_PERMISSIONS')]),
                ('install-delivery-update.py', [('DELIVERY_SQL_PERMISSIONS', 'EXPECTED_DELIVERY_PERMISSIONS')])):
            support = load('permissions_' + filename.replace('-', '_'), PILOT / filename)
            mapping.update({getattr(support, query): getattr(support, expected).copy() for query, expected in queries})
        commercial = load('c1_permission_identity', PILOT / 'install-commercial-catalog-update.py')
        identity = dict(commercial.EXPECTED_PERMISSIONS, migrations_count=18)
        mapping[commercial.PERMISSION_QUERY] = identity
        mapping[validation.AUDIT_PERMISSIONS] = observed
        value.sql_tool, value.fields = '/synthetic-sqlcmd', {}
        value.inspector.query_sql = Mock(side_effect=lambda tool, fields, query: mapping[query])
        return observed, identity, mapping

    @contextmanager
    def preflight_fixture(self, own=False):
        """Real C1 preflight/identity/schema/permission guards, simulated OS/SQL I/O."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backups = root / 'backups'
            backups.mkdir(mode=0o700)
            env = root / 'api.env'
            raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
            env.write_bytes(raw)
            name = 'consolidation-c1-' + installer.EXPECTED_SOURCE[:12] if own else installer.PREVIOUS_PAIRS[0][0]
            for part in ('backend', 'frontend'):
                release = root / part / 'releases' / name
                release.mkdir(parents=True)
                (root / part / 'current').symlink_to(release)
                (release / ('Prodimt.Pedidos.Api.dll' if part == 'backend' else 'index.html')).write_bytes(b'synthetic')
            token = load('c1_preflight_token', PILOT / 'install-access-token-management-update.py')
            key = token.environment_key(raw)
            config = {token.KEY_CONFIG.lower(): key, 'connectionstrings:pedidos':
                'Server=127.0.0.1,14330;Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password=synthetic;'}
            original_factory = installer.module
            def factory(module_name, source):
                result = original_factory(module_name, source)
                if module_name == 'c1_native_support':
                    result.directory_safe = Mock()
                    result.command = Mock(side_effect=AssertionError('preflight cannot change service or SQL'))
                if module_name == 'c1_token_support':
                    result.validate_environment_file = Mock()
                if module_name == 'i19_inventory':
                    result.SQL_TOOLS = (os.sys.executable,)
                    result.process_environment = Mock(return_value={})
                    result.configuration = Mock(return_value=(config, {'environment': 'Production'}))
                    result.policy_flags = Mock(return_value=dict(provider_is_sqlserver=True,
                        jwt_compatible_with_production_policy=True, jwt_lifetime_compatible=True,
                        connection_compatible_with_production_policy=True, development_seed_enabled=False,
                        automatic_migrations_enabled=False, https_redirection_enabled=True))
                    result.query_sql = Mock(side_effect=lambda tool, fields, query: mapping[query])
                return result
            with patch.object(installer, 'ROOT', root), patch.object(installer, 'BACKUPS', backups), \
                    patch.object(installer, 'ENV_FILE', env), patch.object(installer, 'module', side_effect=factory), \
                    patch.object(installer, 'regular', side_effect=lambda path: path.read_bytes()):
                value = self.instance()
                audit, identity, mapping = self.permission_transport(value)
                ids = validation.BASE_IDS + ([installer.MIGRATION] if own else [])
                identity['migrations_count'] = len(ids)
                inventory = original_factory('fixture_inventory', supports()['inspect-native.py'])
                mapping[inventory.SQL_MIGRATIONS] = [{'MigrationId': item} for item in ids]
                for filename, pairs in (
                        ('install-commercial-catalog-update.py', [('NEW_SCHEMA', 'EXPECTED_SCHEMA')]),
                        ('install-customer-products-update.py', [('TOKEN_SHAPE', 'EXPECTED_TOKEN_SCHEMA'), ('SETUP_SHAPE', 'EXPECTED_SETUP_SCHEMA')]),
                        ('install-identity-update.py', [('SECURITY_SCHEMA', 'EXPECTED_SECURITY_SCHEMA')]),
                        ('install-ordering-schedules-on-delivery-update.py', [('PRODUCTION_SHAPE', 'EXPECTED_PRODUCTION_SCHEMA'), ('SCHEDULE_SHAPE', 'EXPECTED_SCHEDULE_SHAPE')])):
                    module = load('preflight_' + filename.replace('-', '_'), PILOT / filename)
                    mapping.update({getattr(module, query): getattr(module, expected) for query, expected in pairs})
                schema = load('c1_preflight_delivery_schema', PILOT / 'delivery-schema-contract.py')
                mapping[schema.schema_query(False)] = json.loads(supports()['delivery-schema.json'])
                for row in mapping[schema.schema_query(False)]['checks']:
                    row.pop('definition')
                mapping[validation.AUDIT_SHAPE] = (validation.EXPECTED_AUDIT_SHAPE_AFTER if own
                                                  else validation.EXPECTED_AUDIT_SHAPE_BEFORE)
                value.process = Mock(return_value=(1, root / 'backend/releases' / name,
                    [os.sys.executable, 'Prodimt.Pedidos.Api.dll']))
                value.http = Mock(side_effect=lambda path: (200, b'{"database":"reachable"}') if path == '/health/db'
                    else (200, b'{"status":"ok"}') if path == '/health'
                    else (200, b'synthetic') if path == '/admin/operacion' else (401, b''))
                value.unit_configuration = {}
                yield value, audit, identity, mapping, config

    def test_real_preflight_accepts_only_exact_i23_and_captures_immutable_1111(self):
        with self.preflight_fixture() as (value, audit, _, _, _):
            value.preflight()
            self.assertEqual(value.state['migrations_before'], 18)
            self.assertEqual(value.audit_permissions_before, (1, 1, 1, 1))
            audit['DELETE'] = 0
            self.assertEqual(value.audit_permissions_before, (1, 1, 1, 1))
            with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                value.app_permissions(validation.BASE_IDS)
            self.assertFalse(value.stop_requested)

    def test_1111_does_not_allow_other_database_login_server_or_admin_privileges(self):
        for key, bad in (('database_name', 'Unknown'), ('application_user', 'dbo'),
                         ('app_is_db_owner', 1), ('app_is_sysadmin', 1), ('migrations_count', 17)):
            with self.subTest(key=key), self.preflight_fixture() as (value, _, identity, _, _):
                identity[key] = bad
                with self.assertRaisesRegex(installer.InstallError, 'application_permissions_unexpected'):
                    value.preflight()
                self.assertFalse(value.stop_requested)
        for old, new in (('127.0.0.1,14330', 'unknown,1433'), ('ProdimtPedidos', 'Unknown'),
                         ('prodimt_pedidos_app', 'sa')):
            with self.subTest(target=new), self.preflight_fixture() as (value, _, _, _, config):
                config['connectionstrings:pedidos'] = config['connectionstrings:pedidos'].replace(old, new)
                with self.assertRaisesRegex(installer.InstallError, 'sql_target_unexpected'):
                    value.preflight()

    def test_already_current_checks_fixed_permissions_without_creating_historical_baseline(self):
        with self.preflight_fixture(own=True) as (value, _, _, _, _):
            value.verify_stage, value.health = Mock(), Mock()
            value.preflight()
            self.assertEqual(value.state['status'], 'already_current')
            self.assertIsNone(value.audit_permissions_before)
            self.assertIs(value.state['audit_permissions_historical_comparison'], False)
            self.assertFalse(value.state['recovery_pending'])
            value.verify_stage.assert_any_call('backend')
            value.verify_stage.assert_any_call('frontend')
            value.health.assert_called_once_with(True)
            value.apply()
            self.assertFalse(value.stop_requested)
        with self.preflight_fixture(own=True) as (value, audit, _, _, _):
            audit.update(UPDATE=0, DELETE=0)
            with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                value.preflight()
            self.assertIsNone(value.audit_permissions_before)

    def test_already_current_real_health_path_checks_permissions_again(self):
        with self.preflight_fixture(own=True) as (value, audit, _, _, _):
            value.verify_stage = Mock()
            base = type(value).__mro__[1]
            with patch.object(base, 'health'):
                value.preflight()
                self.assertEqual(value.state['status'], 'already_current')
                audit['DELETE'] = 0
                with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                    value.health(True)
            self.assertIsNone(value.audit_permissions_before)

    def test_read_marker_recovers_durable_reference_and_never_recaptures_current_permissions(self):
        with self.preflight_fixture() as (value, audit, _, _, _):
            value.preflight()
            value.snapshot = installer.BACKUPS / 'consolidation-c1-update-20261104T000000Z-aaaaaaaaaaaa'
            value.snapshot.mkdir(mode=0o700)
            (value.snapshot / 'api.env').write_bytes(value.environment)
            value.browser_gate_sha256 = 'b' * 64
            value.persist_audit_reference()
            value.write_marker()
            value.audit_permissions_before = None
            value.recovery = value.read_marker()
            self.assertEqual(value.audit_permissions_before, (1, 1, 1, 1))
            audit['INSERT'] = 0
            with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                value.app_permissions(validation.BASE_IDS)
            (value.snapshot / 'audit-permissions.json').unlink()
            value.audit_permissions_before = None
            with self.assertRaisesRegex(installer.InstallError, 'audit_reference_missing_or_invalid'):
                value.read_marker()
            self.assertIsNone(value.audit_permissions_before)

    def test_all_other_bit_vectors_and_invalid_audit_values_block_without_secret_output(self):
        value = self.instance()
        audit, _, mapping = self.permission_transport(value)
        for bits in itertools.product((0, 1), repeat=4):
            if bits == (1, 1, 1, 1):
                continue
            audit.update(zip(('SELECT', 'INSERT', 'UPDATE', 'DELETE'), bits))
            with self.subTest(bits=bits), self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                value.app_permissions(validation.BASE_IDS)
        invalid = [None, [], {}, dict(SELECT=1, INSERT=1, UPDATE=1), dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1, extra='private-canary')]
        for key in ('SELECT', 'INSERT', 'UPDATE', 'DELETE'):
            for bad in (None, True, False, 'private-canary', '1', 1.0, 2, -1, [], {}):
                invalid.append(dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1) | {key: bad})
        for bad in invalid:
            mapping[validation.AUDIT_PERMISSIONS] = bad
            with self.assertRaises(installer.InstallError):
                value.app_permissions(validation.BASE_IDS)
            self.assertNotIn('private-canary', json.dumps(value.state))

    def test_permission_drift_before_backup_before_ddl_and_after_ddl_blocks_activation(self):
        for when in ('before-backup', 'before-ddl', 'after-ddl'):
            with self.subTest(when=when), tempfile.TemporaryDirectory() as folder:
                value, raw, _ = self.migration_fixture(Path(folder))
                def drift():
                    value.audit_observed.update(UPDATE=0, DELETE=0)
                if when == 'before-backup':
                    drift()
                elif when == 'before-ddl':
                    def backup():
                        value.state['verified_database_backup'] = '/verified'
                        drift()
                    value.database_backup.side_effect = backup
                else:
                    def ddl(sql, timeout):
                        value.permission_identity['migrations_count'] = 19
                        drift()
                    value.sql_run.side_effect = ddl
                with patch.object(installer, 'regular', return_value=raw):
                    with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                        value.backup_and_migrate()
                if when != 'after-ddl':
                    value.sql_run.assert_not_called()
                self.assertNotIn('migrated_schema_verified', value.state)

    def test_drift_during_stage_prevents_stop_and_does_not_overwrite_reference(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder))
            self.recovery_fixture(value)
            before = (value.snapshot / 'audit-permissions.json').read_bytes()
            value.preflight, value.verify_browser_gate, value.authenticate_admin, value.validate_before_apply = Mock(), Mock(), Mock(), Mock()
            value.original_dll, value.overrides = raw, {}
            value.stage = Mock(side_effect=lambda: value.audit_observed.update(DELETE=0))
            value.runtime.command = Mock()
            with patch.object(installer, 'regular', return_value=raw):
                with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                    value.apply()
            self.assertFalse(value.stop_requested)
            value.runtime.command.assert_not_called()
            self.assertEqual((value.snapshot / 'audit-permissions.json').read_bytes(), before)

    def test_missing_or_modified_reference_is_not_recaptured_even_with_verified_19(self):
        for change in ('missing', 'source', 'payload', 'baselineSource', 'historyBefore', 'previous', 'auditPermissions', 'extra'):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as folder:
                value, _, _ = self.migration_fixture(Path(folder))
                self.recovery_fixture(value, verified=True)
                ids = validation.BASE_IDS + [installer.MIGRATION]
                value.schemas.return_value = ids
                value.state['migration_ids_before'] = ids
                value.permission_identity['migrations_count'] = 19
                path = value.snapshot / 'audit-permissions.json'
                if change == 'missing':
                    path.unlink()
                else:
                    record = json.loads(path.read_bytes())
                    record[change] = dict(SELECT=1, INSERT=1, UPDATE=0, DELETE=0) if change == 'auditPermissions' else 'different'
                    path.write_text(json.dumps(record))
                with self.assertRaises(installer.InstallError):
                    value.backup_and_migrate()
                value.sql_run.assert_not_called()
                value.database_backup.assert_not_called()
                if change == 'missing':
                    self.assertFalse(path.exists())

    def test_marker_and_snapshot_must_agree_on_permissions_and_binding(self):
        for change in ('source', 'payload', 'snapshot', 'auditReferenceSha256', 'auditPermissions'):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as folder:
                value, _, _ = self.migration_fixture(Path(folder))
                self.recovery_fixture(value)
                marker = json.loads(value.marker.read_bytes())
                marker[change] = dict(SELECT=1, INSERT=1, UPDATE=0, DELETE=0) if change == 'auditPermissions' else 'different'
                value.marker.write_text(json.dumps(marker))
                with self.assertRaisesRegex(installer.InstallError, 'audit_reference_marker_mismatch'):
                    value.backup_and_migrate()
                value.sql_run.assert_not_called()

    def test_reference_is_exclusive_private_and_fsynced_before_stop(self):
        with tempfile.TemporaryDirectory() as folder:
            value, _, _ = self.migration_fixture(Path(folder))
            path = value.snapshot / 'audit-permissions.json'
            before = path.read_bytes()
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)
            with self.assertRaisesRegex(installer.InstallError, 'audit_reference_already_exists'):
                value.persist_audit_reference()
            self.assertEqual(path.read_bytes(), before)
            with patch.object(installer.os, 'fsync', wraps=installer.os.fsync) as sync:
                self.recovery_fixture(value)
            self.assertGreaterEqual(sync.call_count, 2)
            record = json.loads(value.marker.read_bytes())
            self.assertEqual(record['auditReferenceSha256'], installer.sha(before))
            self.assertEqual(record['auditPermissions'], dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1))

    def test_pre_ddl_rollback_requires_durable_unchanged_permissions_and_keeps_lock_on_error(self):
        for failure in (None, 'drift', 'unreadable', 'missing-reference'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as folder:
                value, _, _ = self.migration_fixture(Path(folder))
                with patch.object(installer, 'BACKUPS', value.snapshot):
                    value.write_marker()
                value.stop_requested = True
                value.runtime.command, value.switch, value.health = Mock(), Mock(), Mock()
                if failure == 'drift':
                    value.audit_observed['UPDATE'] = 0
                elif failure == 'unreadable':
                    value.inspector.query_sql.side_effect = OSError('private-canary')
                elif failure == 'missing-reference':
                    (value.snapshot / 'audit-permissions.json').unlink()
                value.rollback()
                if failure:
                    self.assertTrue(value.state['recovery_manual_required'])
                    self.assertTrue(value.state['recovery_pending'])
                    self.assertTrue(value.marker.exists())
                    self.assertNotIn('previous_health_verified', value.state)
                    value.switch.assert_not_called()
                    value.health.assert_not_called()
                else:
                    self.assertTrue(value.state['previous_health_verified'])
                    self.assertFalse(value.state['recovery_pending'])
                    self.assertFalse(value.marker.exists())

    def test_verified_recovery_rejects_permission_drift_without_touching_new_business_data(self):
        with tempfile.TemporaryDirectory() as folder:
            value, _, _ = self.migration_fixture(Path(folder))
            self.recovery_fixture(value, verified=True)
            ids = validation.BASE_IDS + [installer.MIGRATION]
            value.schemas.return_value = ids
            value.state['migration_ids_before'] = ids
            value.permission_identity['migrations_count'] = 19
            value.audit_observed.update(UPDATE=0, DELETE=0)
            with self.assertRaisesRegex(installer.InstallError, 'audit_permissions_unexpected'):
                value.backup_and_migrate()
            value.sql_run.assert_not_called()
            value.preservation.assert_not_called()

    def test_exact_sql_gate_requires_complete_result_and_captured_integer_1111(self):
        flags = ('exactSql', 'rollbackVerified', 'replayVerified', 'applicationPermissions',
            'auditPermissionsPreserved', 'preservationVerified', 'cleanupVerified', 'strictMetadata',
            'unicodeAuditVerified', 'lossyDowngradeRejected', 'safeDowngradeVerified')
        good = dict.fromkeys(flags, True) | dict(schema=validation.SCHEMA, status='passed',
            auditPermissionsBefore=dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1))
        cases = [good, {'status': 'passed'}, good | {'schema': 'other'},
                 good | {'auditPermissionsBefore': dict(SELECT=1, INSERT=1, UPDATE=0, DELETE=0)},
                 good | {'auditPermissionsBefore': dict(SELECT=1, INSERT=1, UPDATE=True, DELETE=1)}]
        cases.extend({key: child for key, child in good.items() if key != missing} for missing in flags)
        for result in cases:
            original = installer.module
            def factory(name, raw):
                module = original(name, raw)
                if name == 'c1_exact_sql_validation':
                    module.validate_exact_sql = Mock(return_value=result)
                return module
            with patch.object(installer, 'module', side_effect=factory), \
                    patch.object(installer, 'run_portable_validation', return_value={'passed': 7}):
                value = self.instance()
                value.dotnet_executable, value.sql_tool = '/synthetic-dotnet', '/synthetic-sqlcmd'
                value.sql_login, value.sql_password = 'synthetic', 'private-canary'
                value.inspector.parse_sql_output = json.loads
                if result is good:
                    value.validate_before_apply()
                    self.assertTrue(value.state['validation_databases_cleaned'])
                else:
                    with self.assertRaisesRegex(installer.InstallError, 'exact_sql_validation_incomplete'):
                        value.validate_before_apply()
                self.assertNotIn('private-canary', json.dumps(value.state))

    def test_real_i23_18_with_1111_is_admitted_and_preserved_during_metadata_expansion(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, events = self.migration_fixture(Path(folder))
            observed, identity = value.audit_observed, value.permission_identity
            def execute(sql, timeout):
                self.assertEqual(sql, upgrade())
                self.assertEqual(events, ['verified-backup'])
                identity['migrations_count'] = 19
                events.append('c1-ddl')
            value.sql_run.side_effect = execute
            with patch.object(installer, 'regular', return_value=raw), \
                    patch.object(installer, 'private', side_effect=lambda path: path.read_bytes()):
                value.backup_and_migrate()
            self.assertEqual(observed, dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1))
            self.assertTrue(value.state['audit_permissions_preserved'])
            self.assertEqual(value.state['migrations_after'], 19)

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
        value.audit_observed, value.permission_identity, value.permission_mapping = self.permission_transport(value)
        value.audit_permissions_before = value.app_permissions(validation.BASE_IDS)
        value.persist_audit_reference()
        value.marker = directory / 'pending.json'
        value.old = {part: Path('/old') / name for part, name in zip(('backend', 'frontend'), installer.PREVIOUS_PAIRS[0])}
        value.history_before = validation.BASE_IDS.copy()
        value.browser_gate_sha256 = 'a' * 64
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
            value.permission_identity['migrations_count'] = 19
            events.append('c1-ddl')
        value.sql_run = Mock(side_effect=execute)
        return value, raw, events

    def recovery_fixture(self, value, verified=False):
        if verified:
            value.state['migrated_schema_verified'] = True
        with patch.object(installer, 'BACKUPS', value.snapshot):
            value.write_marker()
        value.recovery = json.loads(value.marker.read_bytes())

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
            self.recovery_fixture(value)
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
            self.recovery_fixture(value)
            value.permission_identity['migrations_count'] = 19
            with self.assertRaisesRegex(installer.InstallError, 'recovery_preservation_snapshot_required'):
                value.backup_and_migrate()
            value.database_backup.assert_not_called()
            value.sql_run.assert_not_called()
            self.assertFalse((Path(folder) / 'business-preservation.json').exists())

    def test_recovery_cannot_rebaseline_mutated_data(self):
        with tempfile.TemporaryDirectory() as folder:
            value, raw, _ = self.migration_fixture(Path(folder))
            (Path(folder) / 'business-preservation.json').write_text(json.dumps({'tables': 'different'}))
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
        value.preserved_app_permissions = Mock()
        value.state['migration_ids_before'] = validation.BASE_IDS.copy()
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
        with tempfile.TemporaryDirectory() as folder:
            value, _, _ = self.migration_fixture(Path(folder))
            ids = validation.BASE_IDS + [installer.MIGRATION]
            value.state['migration_ids_before'] = ids
            value.schemas = Mock(return_value=ids)
            self.recovery_fixture(value, verified=True)
            value.permission_identity['migrations_count'] = 19
            value.preservation.reset_mock()
            value.backup_and_migrate()
            value.sql_run.assert_not_called()
            value.preservation.assert_not_called()
            value.database_backup.assert_not_called()
            self.assertTrue(value.state['migration_recovery_already_verified'])

    def test_verified_recovery_cannot_resume_when_schema_is_not_target(self):
        with tempfile.TemporaryDirectory() as folder:
            value, _, _ = self.migration_fixture(Path(folder))
            self.recovery_fixture(value, verified=True)
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
