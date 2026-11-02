"""I18 native delivery guards, batch boundaries and downgrade recovery."""
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    specification = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(result)
    return result


builder = load('i18_builder_tests', ROOT / 'scripts/pilot/build-identity-package.py')
installer = load('i18_installer_tests', ROOT / 'scripts/pilot/install-identity-update.py')


def migration():
    ddl = 'BEGIN TRANSACTION;\n'
    ddl += 'ALTER TABLE [AdminUsers] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);\n'
    ddl += 'ALTER TABLE [AdminUsers] ADD [SessionRevision] uniqueidentifier NOT NULL;\n'
    ddl += ''.join('CREATE TABLE [' + name + '] ([Id] uniqueidentifier);\n' for name in installer.SECURITY_TABLES)
    ddl += ''.join('CREATE INDEX [i' + str(i) + '] ON [SecurityRoles]([Id]);\n' for i in range(5)) + '\n'
    seed = ("IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision') AND [object_id] = OBJECT_ID(N'[SecurityStates]'))\n"
            '    SET IDENTITY_INSERT [SecurityStates] ON;\n'
            "INSERT INTO [SecurityStates] VALUES(1,'18180000-0000-0000-0000-000000000002');\n"
            "IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision') AND [object_id] = OBJECT_ID(N'[SecurityStates]'))\n"
            '    SET IDENTITY_INSERT [SecurityStates] OFF;\n'
            "INSERT INTO [SecurityRoles] VALUES ('18180000-0000-0000-0000-000000000001');\n"
            "INSERT INTO [SecurityPermissions] VALUES (N'security.manage');\n"
            "INSERT INTO [SecurityUserRoles] SELECT [Id], '18180000-0000-0000-0000-000000000001' FROM [AdminUsers];\n"
            'INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion])\n'
            "VALUES (N'" + builder.MIGRATION + "', N'10.0.8');\n\nCOMMIT;\nGO\n\n")
    return ddl + seed


def supports():
    return {
        'native-deployment-support.py': (ROOT / 'scripts/pilot/install-quick-capture-update.py').read_bytes(),
        'commercial-catalog-support.py': (ROOT / 'scripts/pilot/install-commercial-catalog-update.py').read_bytes(),
        'token-management-support.py': (ROOT / 'scripts/pilot/install-access-token-management-update.py').read_bytes(),
    }


class IdentityPackageTests(unittest.TestCase):
    def test_batch_boundary_preserves_statements_and_one_transaction(self):
        original = migration()
        fixed = builder.migration_sql_for_sqlcmd(original)
        self.assertEqual(fixed.replace('GO\n\n', '', 1), original)
        self.assertEqual(fixed.count('BEGIN TRANSACTION;'), 1)
        self.assertEqual(fixed.count('COMMIT;'), 1)
        self.assertEqual(builder.migration_sql_for_sqlcmd(fixed), fixed)
        installer.validate_migration_sql(fixed)

    def test_batch_boundary_precedes_whole_idempotent_guard(self):
        original = migration()
        index = original.index('IF EXISTS')
        guard = ("IF NOT EXISTS (\n    SELECT * FROM [__EFMigrationsHistory]\n"
                 "    WHERE [MigrationId] = N'" + builder.MIGRATION + "'\n)\nBEGIN\n")
        guarded = original[:index] + guard + original[index:].replace('\nCOMMIT;', '\nEND;\nCOMMIT;')
        fixed = builder.migration_sql_for_sqlcmd(guarded)
        self.assertIn('GO\n\n' + guard, fixed)
        self.assertNotIn('BEGIN\nGO', fixed)
        self.assertEqual(builder.migration_sql_for_sqlcmd(fixed), fixed)

    def test_unreviewed_sql_or_missing_ddl_boundary_is_rejected(self):
        for suffix in ('\nDELETE FROM dbo.AdminUsers;', '\nUPDATE dbo.CustomerAccessTokens SET IsActive=0;', '\nDROP TABLE dbo.Orders;'):
            with self.subTest(suffix=suffix), self.assertRaises(ValueError):
                builder.migration_sql_for_sqlcmd(migration() + suffix)
        with self.assertRaises(installer.InstallError):
            installer.validate_migration_sql(migration())

    def test_both_transaction_markers_must_occur_exactly_once(self):
        for changed in (migration().replace('BEGIN TRANSACTION;', '').replace('COMMIT;', ''),
                        migration().replace('BEGIN TRANSACTION;', 'BEGIN TRANSACTION;\nBEGIN TRANSACTION;')
                                   .replace('COMMIT;', 'COMMIT;\nCOMMIT;')):
            with self.subTest(sql=changed[:40]), self.assertRaises(ValueError):
                builder.migration_sql_for_sqlcmd(changed)

    def test_recovery_key_is_never_packaged(self):
        self.assertTrue(builder.contains_encryption_key(b'{"CustomerAccessTokens":{"EncryptionKey":"synthetic"}}'))
        self.assertTrue(builder.contains_encryption_key(b'{"CustomerAccessTokens__EncryptionKey":"synthetic"}'))
        self.assertFalse(builder.contains_encryption_key(b'{"CustomerAccessTokens":{"EncryptionKey":""}}'))

    def instance(self):
        with patch.object(installer, 'load_payload', return_value=supports()):
            return installer.make_installer(Path('/unused-synthetic-payload.zip'))

    def test_no_i12_key_or_revocation_side_effect_is_invoked_by_factory(self):
        instance = self.instance()
        self.assertFalse(instance.state['encryption_key_created'])
        self.assertFalse(instance.state['encryption_key_rotated'])
        self.assertFalse(instance.state['tokens_revoked'])
        self.assertFalse(instance.stop_requested)
        self.assertEqual(instance.new['backend'].name, 'identity-' + installer.EXPECTED_SOURCE[:12])

    def test_rollback_before_stop_does_not_operate_service_or_sql(self):
        instance = self.instance()
        instance.sql_json = Mock(side_effect=AssertionError('must not query'))
        instance.runtime.command = Mock(side_effect=AssertionError('must not stop'))
        instance.rollback()
        instance.sql_json.assert_not_called()
        instance.runtime.command.assert_not_called()

    def test_immediate_rollback_allowed_only_for_initial_security_graph(self):
        for migrated in (False, True):
            with self.subTest(migrated=migrated):
                instance = self.instance()
                instance.stop_requested = True
                instance.runtime.command = Mock()
                instance.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
                history = instance.runtime.MIGRATIONS + ['20261101154618_AddSharedProductsAndCustomerPrices',
                    '20261101190000_AddRecoverableCustomerAccessTokens'] + ([installer.MIGRATION] if migrated else [])
                instance.sql_json = Mock(side_effect=[[{'MigrationId': value} for value in history]] + ([{'safe': 1}] if migrated else []))
                base = Mock()
                instance.runtime.Installer.rollback = base
                instance.rollback()
                base.assert_called_once()
                self.assertTrue(instance.state['immediate_security_rollback_verified'])
                self.assertNotIn('recovery_manual_required', instance.state)

    def test_rollback_stops_service_before_sql_read_and_downgrade(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        history = instance.runtime.MIGRATIONS + ['20261101154618_AddSharedProductsAndCustomerPrices',
            '20261101190000_AddRecoverableCustomerAccessTokens', installer.MIGRATION]
        events = []
        def stop(*args, **kwargs):
            events.append('stop')
        def read(query):
            events.append('read')
            return [{'MigrationId': value} for value in history] if query == 'history' else {'safe': 1}
        instance.runtime.command = stop
        instance.sql_json = read
        instance.runtime.Installer.rollback = lambda self: events.append('downgrade')
        instance.rollback()
        self.assertEqual(events, ['stop', 'read', 'read', 'downgrade'])

    def test_failed_service_stop_never_reads_sql_or_downgrades(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.runtime.command = Mock(side_effect=installer.InstallError('synthetic_stop_failed'))
        instance.sql_json = Mock(side_effect=AssertionError('must not read'))
        instance.runtime.Installer.rollback = Mock(side_effect=AssertionError('must not downgrade'))
        instance.rollback()
        instance.sql_json.assert_not_called()
        instance.runtime.Installer.rollback.assert_not_called()
        self.assertTrue(instance.state['recovery_manual_required'])
        self.assertTrue(instance.state['rollback_blocked_service_stop'])

    def test_changed_security_revision_blocks_downgrade_and_stops_api(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        history = instance.runtime.MIGRATIONS + ['20261101154618_AddSharedProductsAndCustomerPrices',
            '20261101190000_AddRecoverableCustomerAccessTokens', installer.MIGRATION]
        instance.sql_json = Mock(side_effect=[[{'MigrationId': value} for value in history], {'safe': 0}])
        instance.runtime.command = Mock()
        base = Mock(side_effect=AssertionError('never start I12'))
        instance.runtime.Installer.rollback = base
        instance.rollback()
        base.assert_not_called()
        instance.runtime.command.assert_called_once_with(['systemctl', 'stop', installer.SERVICE], timeout=45)
        self.assertTrue(instance.state['recovery_manual_required'])
        self.assertTrue(instance.state['rollback_blocked_security_changes'])
        self.assertTrue(instance.state['application_stopped_for_security'])

    def test_uncertain_sql_state_blocks_downgrade(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        instance.sql_json = Mock(side_effect=OSError('synthetic read failure'))
        instance.runtime.command = Mock()
        base = Mock(side_effect=AssertionError('never start I12'))
        instance.runtime.Installer.rollback = base
        instance.rollback()
        base.assert_not_called()
        self.assertTrue(instance.state['recovery_manual_required'])

    def test_previous_release_guard_accepts_only_i12_with_i15_or_i17(self):
        for frontend, accepted in [('customer-catalog-afb8f3ad9c33', True),
                                   ('pending-customers-cdbfec3071bc', True),
                                   ('add-products-unreviewed-i16', False)]:
            with self.subTest(frontend=frontend), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for part, name in [('backend', installer.PREVIOUS_BACKEND), ('frontend', frontend)]:
                    target = root / part / 'releases' / name
                    target.mkdir(parents=True)
                    (root / part / 'current').symlink_to(target)
                with patch.object(installer, 'ROOT', root):
                    instance = self.instance()
                instance.runtime.directory_safe = Mock()
                instance.process = Mock(side_effect=installer.InstallError('release_accepted'))
                with patch.object(installer, 'ROOT', root), self.assertRaises(installer.InstallError) as error:
                    instance.preflight()
                self.assertEqual(str(error.exception), 'release_accepted' if accepted else 'previous_release_not_supported')
                self.assertFalse(instance.stop_requested)
                self.assertFalse(instance.state['changes_made'])

    def test_build_and_exported_installer_agree_on_manifest_and_pins(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            backend, frontend = directory / 'backend', directory / 'frontend'
            backend.mkdir(); frontend.mkdir()
            (backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'synthetic-backend')
            (backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
            (backend / 'appsettings.json').write_text('{}')
            (frontend / 'index.html').write_text('<html></html>')
            (frontend / '3rdpartylicenses.txt').write_text('fictional')
            (frontend / 'main.js').write_bytes(b'apiBaseUrl:"" /access-tokens /reveal /prices /security directPermissionIds roleIds')
            sql = directory / 'migration.sql'; sql.write_text(migration())
            output = directory / 'deliverable.zip'
            source = 'a' * 40
            with patch.object(builder, 'validate_source'):
                result = builder.build(backend, frontend, sql, output, source)
            with zipfile.ZipFile(output) as archive:
                prefix = builder.PREFIX
                exported = archive.read(prefix + 'PRODIMT_Instalar_I18.py')
                payload = archive.read(prefix + 'prodimt-identity-payload.zip')
                self.assertEqual(result['sha256'], builder.digest(output.read_bytes()))
                self.assertIn(builder.digest(payload).encode(), exported)
                self.assertIn(source.encode(), exported)
            import types
            generated = types.ModuleType('generated_identity_install')
            exec(compile(exported, 'generated_identity_install', 'exec'), generated.__dict__)
            with patch.object(generated, 'regular', return_value=payload):
                files = generated.load_payload(Path('/synthetic-payload.zip'))
            self.assertEqual(files['backend/Prodimt.Pedidos.Api.dll'], b'synthetic-backend')
            self.assertIn(b'GO\n\n', files['migrations-i18.sql'])
            with patch.object(generated, 'regular', return_value=payload + b'tampered'):
                with self.assertRaises(generated.InstallError):
                    generated.load_payload(Path('/synthetic-payload.zip'))

    def test_backup_failure_prevents_migration_and_key_change(self):
        instance = self.instance()
        instance.verify_schema = Mock(return_value=13)
        instance.state['migrations_before'] = 13
        counts = dict.fromkeys(('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits', 'profiles', 'products', 'machines'), 0)
        instance.sql_json = Mock(side_effect=[counts, {'synthetic': 1}, {'synthetic': 1}])
        instance.database_backup = Mock(side_effect=installer.InstallError('synthetic_backup_failed'))
        instance.sql_run = Mock(side_effect=AssertionError('migration must not run'))
        with self.assertRaises(installer.InstallError):
            instance.backup_and_migrate()
        instance.sql_run.assert_not_called()
        self.assertFalse(instance.state['database_migrations_run'])
        self.assertFalse(instance.state['encryption_key_created'])

    def test_installer_rejects_unpinned_payload_without_file_read(self):
        with patch.object(installer, 'regular', side_effect=AssertionError('must not read')):
            with self.assertRaises(installer.InstallError):
                installer.load_payload(Path('/unused.zip'))


if __name__ == '__main__':
    unittest.main()
