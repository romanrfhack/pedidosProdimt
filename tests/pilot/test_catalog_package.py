"""Native I06 package integrity and activation rollback with fictional files."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import subprocess
import types
import unittest
from unittest.mock import patch
import zipfile


ROOT = Path(__file__).parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


installer = module('catalog_install_fixture', 'install-catalog-update.py')
builder = module('catalog_build_fixture', 'build-catalog-package.py')
SOURCE = 'a' * 40


class CatalogPackageTests(unittest.TestCase):
    def fixture(self, folder):
        backend, frontend = folder / 'backend', folder / 'frontend'
        backend.mkdir()
        frontend.mkdir()
        (backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'fictional-assembly')
        (backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
        (backend / 'appsettings.json').write_text('{}')
        (frontend / 'index.html').write_text('<script src="main.js"></script>')
        (frontend / 'main.js').write_text('var e={apiBaseUrl:""};var path="/api/admin/account/change-password";var day="/api/admin/customers/day-status";var label="Descanso programado";')
        migration = folder / 'migration.sql'
        migration.write_text('ALTER TABLE [Customers] ADD [RestDaysMask] int NOT NULL DEFAULT 0;\n-- CK_Customers_RestDaysMask 20261028142346_AddCustomerRestDays')
        bundle = folder / 'bundle.zip'
        result = builder.build(backend, frontend, migration, bundle, SOURCE)
        with zipfile.ZipFile(bundle) as archive:
            payload = folder / 'payload.zip'
            payload.write_bytes(archive.read('prodimt-catalog-update/prodimt-catalog-payload.zip'))
        return result, payload

    def test_bundle_pins_installer_and_carries_exact_hashed_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            result, payload = self.fixture(root)
            with zipfile.ZipFile(root / 'bundle.zip') as archive:
                script = archive.read('prodimt-catalog-update/PRODIMT_Instalar_I06.py').decode()
                self.assertIn("EXPECTED_PACKAGE_SHA256 = '" + result['payload_sha256'] + "'", script)
                self.assertIn("EXPECTED_SOURCE = '" + SOURCE + "'", script)
            original = installer.regular
            with patch.object(installer, 'EXPECTED_PACKAGE_SHA256', result['payload_sha256']), \
                    patch.object(installer, 'EXPECTED_SOURCE', SOURCE), \
                    patch.object(installer, 'regular', side_effect=lambda path, limit: Path(path).read_bytes()):
                files = installer.load_package(payload)
            self.assertEqual(b'fictional-assembly', files['backend/Prodimt.Pedidos.Api.dll'])
            self.assertIn('inspect-native.py', files)

    def test_package_tamper_is_rejected_before_execution(self):
        with tempfile.TemporaryDirectory() as tmp:
            result, payload = self.fixture(Path(tmp))
            payload.write_bytes(payload.read_bytes() + b'changed')
            with patch.object(installer, 'EXPECTED_PACKAGE_SHA256', result['payload_sha256']), \
                    patch.object(installer, 'EXPECTED_SOURCE', SOURCE), \
                    patch.object(installer, 'regular', side_effect=lambda path, limit: Path(path).read_bytes()):
                with self.assertRaisesRegex(installer.InstallError, '^package_hash_mismatch$'):
                    installer.load_package(payload)

    def test_paths_refuse_traversal_absolute_and_noncanonical_names(self):
        for path in ('../bad', '/absolute', '.', '', 'backend/../bad', 'backend//bad', 'C:bad', 'backend\\bad'):
            with self.subTest(path=path): self.assertFalse(installer.safe_name(path))
        self.assertTrue(installer.safe_name('backend/es/messages.dll'))

    def test_failed_activation_restores_old_links_and_health_in_order(self):
        instance = installer.Installer('fictional.zip')
        instance.stop_requested = True
        events = []
        with patch.object(installer, 'command', side_effect=lambda args, **kw: events.append(args[1]) or ''), \
                patch.object(instance, 'switch', side_effect=lambda target: events.append('old-links') if target == instance.old else None), \
                patch.object(instance, 'health', side_effect=lambda new: events.append('old-health') if not new else None):
            instance.rollback()
        self.assertEqual(['stop', 'old-links', 'start', 'old-health'], events)
        self.assertTrue(instance.state['previous_health_verified'])

    def test_readonly_failure_never_stops_or_switches(self):
        instance = installer.Installer('fictional.zip')
        with patch.object(instance, 'preflight', side_effect=installer.InstallError('guarded')), \
                patch.object(installer, 'command') as command, patch.object(instance, 'stage') as stage:
            with self.assertRaises(installer.InstallError): instance.apply()
            instance.rollback()
        command.assert_not_called()
        stage.assert_not_called()

    def test_switch_refuses_to_replace_regular_current(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'backend').mkdir()
            (root / 'backend/current').write_text('unrelated-file')
            with patch.object(installer, 'ROOT', root):
                instance = installer.Installer('fictional.zip')
                with self.assertRaisesRegex(installer.InstallError, '^refuse_to_replace_unexpected_current$'):
                    instance.switch(instance.new)
            self.assertEqual('unrelated-file', (root / 'backend/current').read_text())

    def prepared(self, count=9):
        instance = installer.Installer('fictional.zip')
        instance.state['migrations_before'] = count
        instance.files = {'migrations-i06.sql': b'fictional-incremental-sql'}
        instance.inspector = types.SimpleNamespace(query_sql=lambda *args: dict(
            database_name=installer.DATABASE, migrations_count=10, i04=1, read_admin=1, update_admin=1,
            read_customers=1, update_customers=1, write_audit=1))
        instance.sql_tool, instance.fields = 'synthetic-sqlcmd', {}
        return instance

    def test_backup_verified_before_ddl_and_counts_preserved(self):
        instance = self.prepared()
        counts = dict(customers=23, orders=7, lines=15, administrators=2, audits=9, order_audits=4)
        events = []
        def backup():
            events.append('verified-backup')
            instance.state['verified_database_backup'] = '/synthetic/private-backup.bak'
        with patch.object(instance, 'verify_schema', side_effect=[9, 10]), \
                patch.object(instance, 'sql_json', return_value=counts), \
                patch.object(instance, 'database_backup', side_effect=backup), \
                patch.object(instance, 'sql_run', side_effect=lambda *args, **kw: events.append('ddl')):
            instance.backup_and_migrate()
        self.assertEqual(['verified-backup', 'ddl'], events)
        self.assertTrue(instance.state['existing_data_counts_preserved'])

    def test_backup_failure_prevents_migration(self):
        instance = self.prepared()
        with patch.object(instance, 'verify_schema', return_value=9), \
                patch.object(instance, 'sql_json', return_value=dict(customers=23, orders=7, lines=15,
                    administrators=2, audits=9, order_audits=4)), \
                patch.object(instance, 'database_backup', side_effect=installer.InstallError('backup-not-verified')), \
                patch.object(instance, 'sql_run') as execute:
            with self.assertRaisesRegex(installer.InstallError, 'backup-not-verified'):
                instance.backup_and_migrate()
        execute.assert_not_called()
        self.assertFalse(instance.state['migrations_may_have_changed'])

    def test_completed_migration_retry_still_backs_up_but_does_not_repeat_ddl(self):
        instance = self.prepared(10)
        counts = dict(customers=23, orders=7, lines=15, administrators=2, audits=9, order_audits=4)
        def backup(): instance.state['verified_database_backup'] = '/synthetic/retry-backup.bak'
        with patch.object(instance, 'verify_schema', return_value=10), \
                patch.object(instance, 'sql_json', return_value=counts), \
                patch.object(instance, 'database_backup', side_effect=backup) as backed_up, \
                patch.object(instance, 'sql_run') as execute:
            instance.backup_and_migrate()
        backed_up.assert_called_once()
        execute.assert_not_called()
        self.assertFalse(instance.state['database_migrations_run'])

    def test_partial_schema_and_unknown_history_rejected(self):
        instance = self.prepared()
        instance.inspector.SQL_MIGRATIONS = 'history'
        for rows, schema in (([{'MigrationId': 'unexpected'}], {}),
            ([{'MigrationId': value} for value in installer.MIGRATIONS[:9]],
             dict(columns_count=1, valid_column=0, checks_count=0, valid_check=0))):
            with self.subTest(rows=len(rows)), patch.object(instance, 'sql_json', side_effect=[rows, schema]):
                with self.assertRaises(installer.InstallError): instance.verify_schema()

    def test_sql_identity_only_in_clean_environment_and_safe_options_in_stdin(self):
        instance = self.prepared()
        instance.sql_login, instance.sql_password = 'fictional-login', 'fictional-secret'
        instance.inspector.SQL_TOOLS = (instance.sql_tool,)
        with patch.object(installer.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, '', '')) as run:
            instance.sql_run('SELECT 1;')
        args = run.call_args.args[0]
        kwargs = run.call_args.kwargs
        self.assertNotIn(instance.sql_password, ' '.join(args))
        self.assertIn('-I', args)
        self.assertIn('-x', args)
        self.assertEqual(dict(installer.CLEAN_ENV, SQLCMDUSER=instance.sql_login,
                              SQLCMDPASSWORD=instance.sql_password), kwargs['env'])
        self.assertTrue(kwargs['input'].startswith(installer.OPTIONS))
        self.assertIn('SET ARITHABORT ON;', kwargs['input'])

    def test_sql_error_discloses_only_numeric_headers(self):
        instance = self.prepared()
        instance.sql_login, instance.sql_password = 'fictional-login', 'fictional-secret'
        instance.inspector.SQL_TOOLS = (instance.sql_tool,)
        with patch.object(installer.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1,
                'Msg 1934, Level 16, State 1, Server synthetic, Line 1\nfictional-secret', '')):
            with self.assertRaisesRegex(installer.InstallError, '^sql_operation_failed$'):
                instance.sql_run('SELECT 1;')
        self.assertEqual([1934], instance.state['sql_error_numbers'])
        self.assertNotIn('fictional-secret', json.dumps(instance.state))

    def test_production_json_override_preserved_even_if_not_in_payload(self):
        instance = self.prepared()
        instance.files = {'backend/appsettings.json': b'new-default'}
        instance.overrides = {'backend/appsettings.json': b'old-default', 'backend/appsettings.Production.json': b'private-config'}
        self.assertEqual({'appsettings.json': b'old-default', 'appsettings.Production.json': b'private-config'}, instance.expected('backend'))


if __name__ == '__main__':
    unittest.main()
