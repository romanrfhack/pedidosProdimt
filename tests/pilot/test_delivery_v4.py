"""I22 v4 preserves a custom legacy driver role while seeding a distinct system role."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


fixtures = load('i22_v4_package_fixtures', ROOT / 'tests/pilot/test_delivery_package.py')
native = load('i22_v4_native_fixtures', ROOT / 'tests/pilot/test_delivery_validation.py')
installer, schema, validation = fixtures.installer, fixtures.schema, fixtures.validation
COLLISION_FIELDS = ('role_id_count', 'role_name_count', 'permission_id_count', 'permission_key_count')


def real_migration():
    return (ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text(encoding='utf-8-sig')


def legacy_helper_source():
    current = (ROOT / 'scripts/pilot/validate-delivery-sql.py').read_bytes()
    signature = b'                       clean_environment, parse_output, transport_factory=Transport, baseline_fixture=None):\n'
    hook = b'        if baseline_fixture is not None:\n            baseline_fixture(transport, database)\n'
    if current.count(signature) != 1 or current.count(hook) != 1:
        raise AssertionError('The validation source no longer has the reviewed two-anchor hook')
    return current.replace(signature, signature.replace(b', baseline_fixture=None', b'')).replace(hook, b'')


class LegacySyntheticSql(native.SyntheticSql):
    """Observe callback ordering and preserve a legacy role/grants/user fingerprint."""
    def __init__(self, files):
        super().__init__(files)
        self.legacy_installed = False
        self.order = []
        self.fingerprints = []
        self.corrupt_legacy_on_upgrade = False

    def run(self, database, sql, timeout=120, expected_failure=False):
        if sql == installer.LEGACY_VALIDATION_FIXTURE_SQL:
            self.order.append('legacy-fixture')
            self.legacy_installed = True
        elif sql.startswith('CREATE USER [prodimt_pedidos_app]'):
            self.order.append('application-user')
        elif installer.MIGRATION in sql:
            self.order.append('rollback-attempt' if expected_failure else 'upgrade-or-replay')
        return super().run(database, sql, timeout, expected_failure)

    def query(self, query, as_application=False):
        if query == validation.history_query():
            self.order.append('history')
        if query == installer.SECURITY_FINGERPRINT:
            self.order.append('security-fingerprint')
            value = dict(roles='legacy-custom-role', role_permissions='two-existing-grants',
                         users='one-existing-user', user_roles='legacy-user-assignment') if self.legacy_installed else {'empty': True}
            if self.corrupt_legacy_on_upgrade and self.upgraded:
                value['role_permissions'] = 'changed-by-migration'
            self.fingerprints.append(value.copy())
            return value
        return super().query(query, as_application)


class DeliveryV4Tests(unittest.TestCase):
    def exact(self, fake, callback=installer.delivery_validation_legacy_fixture):
        return validation.validate_exact_sql(fake.files, installer, schema, '/synthetic/sqlcmd',
            '127.0.0.1,14330', 'synthetic-admin', 'not-a-real-password', 'OPTIONS\n',
            {'PATH': '/usr/bin:/bin'}, json.loads, fake.factory, baseline_fixture=callback)

    def test_real_ef_sql_changes_only_the_new_system_role_name(self):
        original = real_migration()
        corrected = installer.canonical_delivery_role_sql(original)
        old = "N''Repartidor - Camioneta''"
        new = "N''Repartidor - Camioneta (sistema)''"
        self.assertEqual(original.count(old), 1)
        self.assertEqual(corrected.count(new), 1)
        self.assertNotIn(old, corrected)
        self.assertEqual(corrected.replace(new, old), original)
        self.assertEqual(installer.CANONICAL_DELIVERY_ROLE_NAME, 'Repartidor - Camioneta (sistema)')
        self.assertEqual(installer.DELIVERY_ROLE, '22220000-0000-0000-0000-000000000001')
        self.assertNotIn('UPDATE [SecurityRoles]', corrected)
        self.assertNotIn('DELETE FROM [SecurityRoles]', corrected)

    def test_exact_upgrade_uses_corrected_name_without_rewriting_payload_sql(self):
        original = real_migration()
        files = dict(fixtures.supports(), **{'migrations-i22.sql': original.encode()})
        before = files['migrations-i22.sql']
        exact = installer.migration_sql_for_sqlcmd(files)
        self.assertIn("N''Repartidor - Camioneta (sistema)''", exact)
        self.assertEqual(files['migrations-i22.sql'], before)
        self.assertEqual(exact.count('BEGIN TRANSACTION;'), 1)
        self.assertEqual(exact.count('COMMIT;'), 1)
        without_grants = exact.replace('GO\n\n' + installer.DELIVERY_GRANTS + '\nCOMMIT;\nGO\n\n', 'COMMIT;\nGO\n\n')
        self.assertEqual(without_grants, installer.canonical_delivery_role_sql(original))

    def test_role_transform_refuses_unknown_missing_duplicate_or_already_transformed_layout(self):
        original = real_migration()
        marker = 'INSERT INTO [SecurityRoles] ([Id], [Name], [Description], [IsActive], [IsSystem])'
        variants = (None, '', original.replace(marker, marker.replace('[Name]', '[OtherName]')),
                    original.replace("N''Repartidor - Camioneta''", "N''Other role''"),
                    original + "\nSELECT N''Repartidor - Camioneta'';",
                    installer.canonical_delivery_role_sql(original))
        for sql in variants:
            with self.subTest(sql=repr(sql)[:80]), self.assertRaises(installer.InstallError):
                installer.canonical_delivery_role_sql(sql)

    def test_seed_inventory_allows_legacy_label_and_checks_only_reserved_system_identity(self):
        reader = Mock(return_value=dict.fromkeys(COLLISION_FIELDS, 0))
        installer.require_delivery_seed_space(reader)
        reader.assert_called_once_with(installer.DELIVERY_SEED_COLLISIONS)
        self.assertIn("Name=N'Repartidor - Camioneta (sistema)'", installer.DELIVERY_SEED_COLLISIONS)
        self.assertNotIn("Name=N'Repartidor - Camioneta'", installer.DELIVERY_SEED_COLLISIONS)
        self.assertNotIn('DELETE', installer.DELIVERY_SEED_COLLISIONS)
        self.assertNotIn('UPDATE', installer.DELIVERY_SEED_COLLISIONS)

    def test_each_reserved_collision_blocks_before_writes(self):
        for field in COLLISION_FIELDS:
            with self.subTest(field=field):
                reader = Mock(return_value=dict.fromkeys(COLLISION_FIELDS, 0) | {field: 1})
                with self.assertRaisesRegex(installer.InstallError, 'delivery_seed_collision'):
                    installer.require_delivery_seed_space(reader)
                reader.assert_called_once_with(installer.DELIVERY_SEED_COLLISIONS)

    def test_seed_inventory_rejects_wrong_shape_bool_negative_or_unknown_value(self):
        valid = dict.fromkeys(COLLISION_FIELDS, 0)
        rows = (None, [], {}, {k: v for k, v in valid.items() if k != COLLISION_FIELDS[0]},
                valid | {'unexpected': 0}, valid | {COLLISION_FIELDS[0]: False},
                valid | {COLLISION_FIELDS[0]: -1}, valid | {COLLISION_FIELDS[0]: '0'})
        for value in rows:
            with self.subTest(value=value), self.assertRaisesRegex(installer.InstallError, 'delivery_seed_inventory_unexpected'):
                installer.require_delivery_seed_space(lambda _: value)

    def test_legacy_fixture_requires_exact_generated_database_equal_to_transport_target(self):
        good = 'ProdimtI22Validation_' + 'a' * 32
        transport = types.SimpleNamespace(database=good, run=Mock())
        installer.delivery_validation_legacy_fixture(transport, good)
        transport.run.assert_called_once_with(good, installer.LEGACY_VALIDATION_FIXTURE_SQL, timeout=120)
        for value in ('ProdimtPedidos', 'master', 'ProdimtI02Test_' + 'a' * 32,
                      'ProdimtI22Validation_' + 'a' * 31, 'ProdimtI22Validation_' + 'A' * 32,
                      'ProdimtI22Validation_' + 'a' * 32 + ';DROP DATABASE x', None):
            with self.subTest(value=value), self.assertRaises(installer.InstallError):
                installer.delivery_validation_legacy_fixture(transport, value)
        other = types.SimpleNamespace(database='ProdimtI22Validation_' + 'b' * 32, run=Mock())
        with self.assertRaises(installer.InstallError):
            installer.delivery_validation_legacy_fixture(other, good)
        other.run.assert_not_called()
        self.assertEqual(transport.run.call_count, 1)

    def test_legacy_fixture_contains_only_synthetic_role_two_baseline_grants_and_one_user(self):
        sql = installer.LEGACY_VALIDATION_FIXTURE_SQL
        self.assertEqual(sql.count('BEGIN TRANSACTION;'), 1)
        self.assertEqual(sql.count('COMMIT;'), 1)
        self.assertEqual(sql.count('INSERT INTO dbo.SecurityRoles'), 1)
        self.assertEqual(sql.count('INSERT INTO dbo.AdminUsers'), 1)
        self.assertEqual(sql.count('INSERT INTO dbo.SecurityRolePermissions'), 1)
        self.assertEqual(sql.count('INSERT INTO dbo.SecurityUserRoles'), 1)
        self.assertIn("N'Repartidor - Camioneta'", sql)
        self.assertIn('18180000-0000-0000-0001-000000000001', sql)
        self.assertIn('18180000-0000-0000-0001-000000000013', sql)
        self.assertNotIn(installer.DELIVERY_ROLE, sql)
        self.assertNotIn('delivery.manage', sql)
        self.assertNotIn('USE ', sql)
        self.assertNotIn('UPDATE ', sql)
        self.assertNotIn('DELETE ', sql)

    def test_known_validation_helper_overlay_changes_only_signature_and_callback_hook(self):
        old = legacy_helper_source()
        current = (ROOT / 'scripts/pilot/validate-delivery-sql.py').read_bytes()
        self.assertEqual(hashlib.sha256(old).hexdigest(), installer.LEGACY_VALIDATION_HELPER_SHA256)
        self.assertEqual(hashlib.sha256(current).hexdigest(), installer.CURRENT_VALIDATION_HELPER_SHA256)
        changed = installer.adapt_delivery_validation_source(old)
        self.assertEqual(changed, current)
        self.assertEqual(installer.adapt_delivery_validation_source(changed), changed)
        self.assertEqual(changed.replace(b', baseline_fixture=None', b'').replace(
            b'        if baseline_fixture is not None:\n            baseline_fixture(transport, database)\n', b''), old)
        compile(changed, 'adapted-delivery-validation', 'exec')

    def test_unrecognized_validation_source_or_mutated_layout_is_rejected(self):
        old = legacy_helper_source()
        for raw in (b'', old + b'\n', old.replace(b'before = transport.query(history_query())', b'before = []')):
            with self.subTest(sha=hashlib.sha256(raw).hexdigest()), self.assertRaises(installer.InstallError):
                installer.adapt_delivery_validation_source(raw)

    def test_legacy_callback_runs_after_app_user_and_before_history_or_fingerprint(self):
        fake = LegacySyntheticSql(fixtures.supports())
        summary = self.exact(fake)
        self.assertTrue(summary['rollbackVerified'])
        self.assertTrue(summary['replayVerified'])
        self.assertTrue(summary['cleanupVerified'])
        self.assertLess(fake.order.index('application-user'), fake.order.index('legacy-fixture'))
        self.assertLess(fake.order.index('legacy-fixture'), fake.order.index('history'))
        self.assertLess(fake.order.index('legacy-fixture'), fake.order.index('security-fingerprint'))
        self.assertEqual(fake.order.count('legacy-fixture'), 1)
        self.assertEqual(fake.order.count('rollback-attempt'), 1)
        self.assertEqual(fake.order.count('upgrade-or-replay'), 2)
        self.assertGreaterEqual(len(fake.fingerprints), 3)
        self.assertTrue(all(value == fake.fingerprints[0] for value in fake.fingerprints))
        self.assertEqual(fake.fingerprints[0]['role_permissions'], 'two-existing-grants')
        self.assertEqual(fake.fingerprints[0]['user_roles'], 'legacy-user-assignment')
        self.assertFalse(fake.exists)

    def test_change_to_legacy_grants_fails_validation_and_still_cleans_generated_database(self):
        fake = LegacySyntheticSql(fixtures.supports())
        fake.corrupt_legacy_on_upgrade = True
        with self.assertRaisesRegex(validation.ValidationFailure, 'validation_existing_security_changed'):
            self.exact(fake)
        self.assertFalse(fake.exists)

    def preflight(self, migrated, collisions):
        """Exercise the real preflight gate with an isolated metadata transport."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backups = root / 'backups'; backups.mkdir()
            env = root / 'api.env'
            raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
            env.write_bytes(raw)
            release = 'delivery-' + installer.EXPECTED_SOURCE[:12] if migrated else installer.BASE_BACKEND
            for part in ('backend', 'frontend'):
                target = root / part / 'releases' / release
                target.mkdir(parents=True)
                (root / part / 'current').symlink_to(target)
                (target / ('Prodimt.Pedidos.Api.dll' if part == 'backend' else 'index.html')).write_bytes(b'synthetic')
            reader = Mock(return_value=collisions)
            original = installer.module
            def factory(name, code):
                result = original(name, code)
                if name == 'reviewed_i18_native_support':
                    result.directory_safe = Mock()
                    result.command = Mock(side_effect=AssertionError('no service command'))
                    result.shutil = types.SimpleNamespace(disk_usage=lambda _: types.SimpleNamespace(free=1024**3))
                elif name == 'reviewed_i18_token_support':
                    result.validate_environment_file = Mock()
                elif name == 'i19_inventory':
                    result.SQL_TOOLS = (os.sys.executable,)
                    result.process_environment = Mock(return_value={})
                    result.configuration = Mock(return_value=({fixtures.token.KEY_CONFIG: fixtures.token.environment_key(raw),
                        'ConnectionStrings:Pedidos': 'synthetic'}, {'environment': 'Production'}))
                    result.first = lambda cfg, *keys, default=None: cfg.get(keys[0], default)
                    result.policy_flags = Mock(return_value=dict(provider_is_sqlserver=True, jwt_compatible_with_production_policy=True,
                        jwt_lifetime_compatible=True, connection_compatible_with_production_policy=True,
                        development_seed_enabled=False, automatic_migrations_enabled=False, https_redirection_enabled=True))
                    result.connection_fields = Mock(return_value={})
                    result.permitted_connection = Mock(return_value=True)
                    result.query_sql = reader
                return result
            with patch.object(installer, 'ROOT', root), patch.object(installer, 'BACKUPS', backups), \
                    patch.object(installer, 'ENV_FILE', env), patch.object(installer, 'regular', side_effect=lambda p: p.read_bytes()), \
                    patch.object(installer, 'load_payload', return_value=fixtures.supports()), patch.object(installer, 'module', side_effect=factory):
                instance = installer.make_installer(Path('/synthetic.zip'))
                ids = fixtures.DeliveryPackageTests().base(instance) + ([installer.MIGRATION] if migrated else [])
                instance.schemas = Mock(return_value=ids)
                instance.app_permissions = Mock()
                instance.process = Mock(return_value=(1, root / 'backend/releases' / release, ['/synthetic/dotnet']))
                instance.unit_configuration = {}
                instance.http = Mock(side_effect=lambda p: (200, b'{"database":"reachable"}') if p == '/health/db'
                    else (200, b'synthetic') if p == '/admin/reparto' else (401, b''))
                instance.verify_stage = Mock()
                instance.health = Mock()
                try:
                    instance.preflight()
                    issue = None
                except installer.InstallError as error:
                    issue = str(error)
            return issue, reader, instance

    def test_real_preflight_at_history16_blocks_collision_without_staging_or_stop(self):
        for field in COLLISION_FIELDS:
            with self.subTest(field=field):
                issue, reader, instance = self.preflight(False, dict.fromkeys(COLLISION_FIELDS, 0) | {field: 1})
                self.assertEqual(issue, 'delivery_seed_collision')
                self.assertTrue(any(call.args[-1] == installer.DELIVERY_SEED_COLLISIONS for call in reader.call_args_list))
                self.assertFalse(instance.stop_requested)
                self.assertFalse(instance.state['changes_made'])

    def test_real_preflight_at_history16_accepts_empty_space_and_history17_does_not_recheck_seed(self):
        issue, reader, instance = self.preflight(False, dict.fromkeys(COLLISION_FIELDS, 0))
        self.assertIsNone(issue)
        self.assertEqual(instance.state['status'], 'planned')
        self.assertEqual(reader.call_count, 1)
        issue, reader, instance = self.preflight(True, dict.fromkeys(COLLISION_FIELDS, 999))
        self.assertIsNone(issue)
        self.assertEqual(instance.state['status'], 'already_current')
        reader.assert_not_called()

    def test_security_fingerprint_excludes_new_grants_only_for_admin_and_canonical_role(self):
        keys = ','.join("N'" + key + "'" for key in installer.DELIVERY_PERMISSIONS)
        predicate = ("WHERE NOT (p.[Key] IN (" + keys + ") AND rp.RoleId IN "
            "('18180000-0000-0000-0000-000000000001','" + installer.DELIVERY_ROLE +
            "')) ORDER BY rp.RoleId,rp.PermissionId")
        self.assertIn(predicate, installer.SECURITY_FINGERPRINT)
        self.assertNotIn("WHERE p.[Key] NOT IN (" + keys + ") ORDER BY rp.RoleId", installer.SECURITY_FINGERPRINT)
        self.assertIn("SELECT * FROM dbo.SecurityRoles WHERE Id<>'" + installer.DELIVERY_ROLE + "' ORDER BY Id",
            installer.SECURITY_FINGERPRINT)
        # The fixture's custom legacy role and its grants remain inside the
        # fingerprint even if it receives a delivery permission in the future.
        self.assertNotIn('24240000-0000-0000-0000-000000000001', installer.SECURITY_FINGERPRINT)

    def test_administrative_collision_gate_precedes_portable_runner_and_all_deployment_work(self):
        instance = fixtures.DeliveryPackageTests().instance()
        instance.state['migration_ids_before'] = fixtures.DeliveryPackageTests().base(instance)
        instance.sql_json = Mock(return_value=dict.fromkeys(COLLISION_FIELDS, 0) | {'role_name_count': 1})
        instance.stage = Mock(side_effect=AssertionError('must not stage'))
        instance.runtime.command = Mock(side_effect=AssertionError('must not operate a service'))
        with patch.object(installer, 'run_portable_validation') as portable:
            with self.assertRaisesRegex(installer.InstallError, 'delivery_seed_collision'):
                instance.validate_before_apply()
        instance.sql_json.assert_called_once_with(installer.DELIVERY_SEED_COLLISIONS)
        portable.assert_not_called()
        instance.stage.assert_not_called()
        instance.runtime.command.assert_not_called()
        self.assertFalse(instance.stop_requested)
        self.assertFalse(instance.state['changes_made'])
        self.assertFalse(hasattr(instance, 'snapshot'))

    def test_preflight_authorizes_collision_query_through_real_inventory_allowlist(self):
        zero = dict.fromkeys(COLLISION_FIELDS, 0)
        issue, _, instance = self.preflight(False, zero)
        self.assertIsNone(issue)
        inventory = installer.module('i22_v4_real_inventory', (ROOT / 'scripts/pilot/inspect-native.py').read_bytes())
        inventory.QUERIES = instance.inspector.QUERIES
        inventory.SQL_TOOLS = ('fixture-sqlcmd',)
        inventory.permitted_connection = lambda _: True
        inventory.command = Mock(return_value=(0, 'ProdimtInspectionJson\n--------------------\n' + json.dumps(zero)))
        self.assertIn(installer.DELIVERY_SEED_COLLISIONS, inventory.QUERIES)
        self.assertEqual(inventory.query_sql('fixture-sqlcmd', {'password': 'synthetic'},
            installer.DELIVERY_SEED_COLLISIONS), zero)
        with self.assertRaisesRegex(inventory.InspectionError, 'sql_query_not_authorized'):
            inventory.query_sql('fixture-sqlcmd', {'password': 'synthetic'}, installer.DELIVERY_SEED_COLLISIONS + '\n')
        inventory.command.assert_called_once()


if __name__ == '__main__':
    unittest.main()
