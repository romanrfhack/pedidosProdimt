import importlib.util
import hashlib
import io
import json
from pathlib import Path
import stat
import subprocess
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('customer_channel_repair', ROOT / 'scripts/pilot/repair-native-customer-channel.py')
R = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(R)
SECRET = 'SYNTHETIC_PRIVATE_SQL_PASSWORD'
CHECKS = dict(database_name=R.DATABASE, migrations_count=11, i07=1, read_channels=1,
              insert_channels=1, read_audit=1, insert_audit=1, unique_type_index=1,
              customer_channels=0, valid_customer_channels=0)
RESULT = dict(created=1, channel_verified=1, existing_data_counts_preserved=1,
              audit_inserted=1, transaction_committed=1)


def inspector():
    return types.SimpleNamespace(SQL_TOOLS=('/opt/mssql-tools/bin/sqlcmd',),
        permitted_connection=lambda values: values.get('password') == SECRET,
        parse_sql_output=json.loads, assert_same_process=lambda *args: None)


class CustomerChannelRepairTests(unittest.TestCase):
    def run_repair(self, apply, results):
        values = {'password': SECRET}
        with patch.object(R.os, 'geteuid', return_value=0), patch.object(R, 'load_inspector', return_value=inspector()), \
                patch.object(R, 'service_configuration', return_value=(values, 1, 'identity')), \
                patch.object(R.Path, 'is_file', return_value=True), patch.object(R.os, 'access', return_value=True), \
                patch.object(R, 'sql_json', side_effect=results) as sql:
            report = R.repair(Path('synthetic.zip'), apply)
        self.assertEqual({}, values)
        self.assertNotIn(SECRET, json.dumps(report))
        return report, sql

    def test_read_only_does_not_apply(self):
        report, sql = self.run_repair(False, [dict(CHECKS)])
        self.assertEqual('planned', report['status'])
        self.assertEqual(1, sql.call_count)
        self.assertEqual(R.PREFLIGHT_SQL, sql.call_args.args[3])
        self.assertFalse(report['changes_may_have_occurred'])

    def test_missing_channel_is_created_once_and_verified(self):
        report, sql = self.run_repair(True, [dict(CHECKS), dict(RESULT), dict(CHECKS, customer_channels=1, valid_customer_channels=1)])
        self.assertEqual('repaired', report['status'])
        self.assertTrue(report['channel_verified'])
        self.assertTrue(report['existing_data_counts_preserved'])
        self.assertFalse(report['application_save_verified'])
        self.assertEqual([R.PREFLIGHT_SQL, R.REPAIR_SQL, R.PREFLIGHT_SQL], [call.args[3] for call in sql.call_args_list])

    def test_repair_retry_does_not_create_or_claim_another_change(self):
        checks = dict(CHECKS, customer_channels=1, valid_customer_channels=1)
        report, _ = self.run_repair(True, [checks, dict(RESULT, created=0, audit_inserted=0), checks])
        self.assertEqual('already_configured', report['status'])
        self.assertFalse(report['changes_made'])

    def test_wrong_database_history_permissions_and_incompatible_channel_block_write(self):
        for changes in (dict(database_name='other'), dict(migrations_count=10), dict(i07=0),
                        dict(insert_channels=0), dict(insert_audit=0), dict(unique_type_index=0),
                        dict(read_channels=None), dict(read_audit=0), dict(customer_channels=0, valid_customer_channels=1),
                        dict(customer_channels=1, valid_customer_channels=0), dict(customer_channels=2)):
            with self.subTest(changes=changes):
                report, sql = self.run_repair(True, [dict(CHECKS, **changes)])
                self.assertEqual('failed', report['status'])
                self.assertEqual(1, sql.call_count)
                self.assertFalse(report['changes_may_have_occurred'])

    def test_unknown_result_after_write_is_not_reported_as_success(self):
        report, _ = self.run_repair(True, [dict(CHECKS), {}])
        self.assertEqual('failed', report['status'])
        self.assertEqual('repair_result_unrecognized', report['issue'])
        self.assertTrue(report['changes_may_have_occurred'])

    def test_postcheck_failure_keeps_committed_change_explicit(self):
        report, _ = self.run_repair(True, [dict(CHECKS), dict(RESULT), R.RepairError('sql_operation_failed')])
        self.assertEqual('failed', report['status'])
        self.assertTrue(report['changes_made'])
        self.assertTrue(report['changes_may_have_occurred'])

    def test_sql_credentials_only_in_clean_environment_and_no_raw_streams(self):
        fake = subprocess.CompletedProcess([], 1, 'Msg 229, Level 14, State 5, Server synthetic\n' + SECRET, SECRET)
        report = {}
        with patch.object(R.subprocess, 'run', return_value=fake) as run:
            with self.assertRaisesRegex(R.RepairError, '^sql_operation_failed$'):
                R.sql_json(inspector(), '/opt/mssql-tools/bin/sqlcmd', {'password': SECRET}, R.REPAIR_SQL, report)
            args, kwargs = run.call_args.args[0], run.call_args.kwargs
            self.assertIn('-I', args)
            self.assertIn('-x', args)
            self.assertNotIn(SECRET, str(args))
            self.assertEqual(R.OPTIONS + R.REPAIR_SQL, kwargs['input'])
            self.assertEqual({}, kwargs['env'])  # Cleared after the process exits.
        self.assertEqual([229], report['sql_error_numbers'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_unapproved_sql_blocked_before_transport(self):
        with patch.object(R.subprocess, 'run') as run:
            with self.assertRaises(R.RepairError):
                R.sql_json(inspector(), '/opt/mssql-tools/bin/sqlcmd', {'password': SECRET}, 'DELETE FROM dbo.Orders', {})
            run.assert_not_called()

    def test_root_guard_precedes_package_execution(self):
        with patch.object(R.os, 'geteuid', return_value=1), patch.object(R, 'load_inspector') as load:
            report = R.repair(Path('synthetic.zip'), True)
            load.assert_not_called()
        self.assertEqual('root_required', report['issue'])

    def test_wrong_package_pin_blocks_execution_of_archive_source(self):
        stream = io.BytesIO(b'not trusted')
        stream.fileno = lambda: 10
        with patch.object(R.os, 'open', return_value=10), \
                patch.object(R.os, 'fdopen', return_value=stream), \
                patch.object(R.os, 'fstat', return_value=types.SimpleNamespace(st_mode=0o100644, st_uid=0, st_nlink=1)), \
                patch.object(R.zipfile, 'ZipFile') as archive:
            with self.assertRaisesRegex(R.RepairError, '^i09_package_hash_mismatch$'):
                R.load_inspector(Path('synthetic.zip'))
            archive.assert_not_called()

    def test_loader_uses_only_manifest_bound_inspector_from_the_pinned_i09_archive(self):
        source = (ROOT / 'scripts/pilot/inspect-native.py').read_bytes()
        for wrong_identity in (False, True):
            with self.subTest(wrong_identity=wrong_identity), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / 'payload.zip'
                manifest = dict(feature='customer-access-links', sourceCommit=R.PAYLOAD_SOURCE,
                    migrationsRequired=11, previousBackendRelease=R.RELEASE,
                    previousFrontendReleases=list(R.FRONTEND_RELEASES),
                    files={'inspect-native.py': dict(bytes=len(source), sha256=hashlib.sha256(source).hexdigest())})
                if wrong_identity:
                    manifest['previousFrontendReleases'] = ['client-config-bbbbbbbbbbbb']
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('manifest.json', json.dumps(manifest))
                    archive.writestr('inspect-native.py', source)
                meta = types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_uid=0, st_nlink=1)
                with patch.object(R, 'PACKAGE_SHA256', hashlib.sha256(path.read_bytes()).hexdigest()), \
                        patch.object(R.os, 'fstat', return_value=meta):
                    if wrong_identity:
                        with self.assertRaisesRegex(R.RepairError, '^i09_package_identity_unexpected$'):
                            R.load_inspector(path)
                    else:
                        loaded = R.load_inspector(path)
                        self.assertEqual({'probe': 1}, loaded.parse_sql_output('{"probe":1}'))

    def test_service_identity_allows_only_i07_backend_and_pinned_i07_i08_frontends(self):
        spec = importlib.util.spec_from_file_location('customer_repair_inventory', ROOT / 'scripts/pilot/inspect-native.py')
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)
        for frontend in (*R.FRONTEND_RELEASES, 'client-config-bbbbbbbbbbbb'):
            with self.subTest(frontend=frontend), tempfile.TemporaryDirectory() as temporary:
                app = Path(temporary)
                backend = app / 'backend/releases' / R.RELEASE
                front = app / 'frontend/releases' / frontend
                backend.mkdir(parents=True)
                front.mkdir(parents=True)
                (app / 'backend/current').symlink_to(backend)
                (app / 'frontend/current').symlink_to(front)
                dll = backend / 'Prodimt.Pedidos.Api.dll'
                dll.write_bytes(b'fictional')
                (backend / 'appsettings.json').write_text('{}')
                properties = '\n'.join(('ActiveState=active', 'User=www-data', 'MainPID=424242',
                                         'WorkingDirectory=' + str(app / 'backend/current')))
                environment = {'ASPNETCORE_ENVIRONMENT': 'Production', 'Persistence__Provider': 'SqlServer',
                    'ConnectionStrings__Pedidos': 'Server=127.0.0.1,14330;Database=ProdimtPedidos;'
                        'User Id=prodimt_pedidos_app;Password=' + SECRET + ';Encrypt=True;TrustServerCertificate=True',
                    'Authentication__Jwt__SigningKey': 'Fictional-JWT-key-012345678901234567890123456789',
                    'Authentication__Jwt__AccessTokenMinutes': '120'}
                original_resolve = Path.resolve
                def resolve(path, *args, **kwargs):
                    return backend if str(path) == '/proc/424242/cwd' else original_resolve(path, *args, **kwargs)
                with patch.object(R, 'ROOT', app), patch.object(Path, 'resolve', new=resolve), \
                        patch.object(inventory, 'command', return_value=(0, properties)), \
                        patch.object(inventory, 'process_identity', return_value='identity'), \
                        patch.object(inventory, 'assert_same_process'), \
                        patch.object(inventory, 'read_limited', side_effect=lambda path:
                            'dotnet\0' + str(dll) + '\0' if str(path) == '/proc/424242/cmdline' else Path(path).read_text()), \
                        patch.object(inventory, 'process_environment', return_value=environment):
                    if frontend not in R.FRONTEND_RELEASES:
                        with self.assertRaisesRegex(R.RepairError, '^running_release_not_expected_i07_i08$'):
                            R.service_configuration(inventory)
                    else:
                        values, pid, identity = R.service_configuration(inventory)
                        self.assertEqual(SECRET, values['password'])
                        self.assertEqual((424242, 'identity'), (pid, identity))
                        inventory.assert_same_process.assert_called_once_with(pid, identity)

    def test_sql_never_bootstraps_accounts_or_changes_existing_rows(self):
        text = R.REPAIR_SQL.upper()
        self.assertNotIn('UPDATE ', text)
        self.assertNotIn('DELETE ', text)
        self.assertNotIn('ALTER ', text)
        self.assertNotIn('ADMINUSERS', text)
        self.assertNotIn('PASSWORD', text)
        self.assertIn('INSERT INTO DBO.SALESCHANNELS', text)
        self.assertIn('INSERT INTO DBO.AUDITLOGS', text)
        self.assertIn('ROLLBACK TRANSACTION', text)
        self.assertIn("N'CUSTOMER',0", text)
        self.assertIn('SYSDATETIMEOFFSET(),2', text)
        self.assertIn("TYPE COLLATE LATIN1_GENERAL_100_BIN2=N'CUSTOMER'", text)
        self.assertIn("DATALENGTH(TYPE)=DATALENGTH(N'CUSTOMER')", text)


if __name__ == '__main__':
    unittest.main()
