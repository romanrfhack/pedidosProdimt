"""I09 read-only diagnosis captures guarded metadata without exposing secrets."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import subprocess
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).parents[2]
SECRET = 'Fictional-private-only-88417-ñ'


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


D = module('customer_access_diagnosis_fixture', 'diagnose-customer-access.py')


class CustomerAccessDiagnosisTests(unittest.TestCase):
    def expected(self):
        return dict(database_name=D.DATABASE, migrations_count=11, i07=1, customer_channel=1, admin_channel=1,
                    **{key: 1 for key in D.PERMISSIONS})

    def test_null_missing_denied_and_unexpected_values_remain_distinct_and_private(self):
        row = self.expected()
        row['database_name'] = SECRET
        row['read_customers'] = None
        row['update_customers'] = 0
        row['read_tokens'] = SECRET
        del row['insert_tokens']
        row['private_extra'] = SECRET
        report = {}
        D.capture_checks(row, self.expected(), report)
        self.assertFalse(report['database_checks']['database_name_matches'])
        self.assertIsNone(report['database_checks']['read_customers'])
        self.assertEqual(0, report['database_checks']['update_customers'])
        self.assertIsNone(report['database_checks']['read_tokens'])
        self.assertEqual(['insert_tokens'], report['missing_fields'])
        self.assertEqual(['read_customers', 'read_tokens'], report['invalid_fields'])
        self.assertEqual(['database_name', 'read_customers', 'update_customers', 'read_tokens', 'insert_tokens'],
                         report['database_mismatches'])
        self.assertTrue(report['unexpected_fields_present'])
        self.assertNotIn(SECRET, json.dumps(report))
        self.assertNotIn('private_extra', json.dumps(report))

    def test_mutating_commands_post_requests_and_arbitrary_sql_never_reach_subprocess(self):
        process = D.ReadOnlyProcess('SELECT 1 FOR JSON PATH;', {})
        cases = [(['systemctl', 'stop', D.SERVICE], {}), (['systemctl', 'restart', D.SERVICE], {}),
                 (['bash', '-c', 'unsafe'], {}), (['curl', 'https://' + D.DOMAIN + '/', '--data', '{}'], {}),
                 ([D.SQL_TOOLS[0], '-Q', 'SELECT 1; DROP TABLE dbo.Customers;'], {}),
                 ([D.SQL_TOOLS[0], '-Q', 'BACKUP DATABASE ProdimtPedidos;'], {}),
                 (['systemctl', 'show', D.SERVICE,
                   '--property=ActiveState,MainPID,User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'],
                  {'shell': True})]
        with patch.object(D.subprocess, 'run') as run:
            for args, kwargs in cases:
                with self.subTest(args=args), self.assertRaises(D.DiagnosisError):
                    process.run(args, **kwargs)
            run.assert_not_called()

    def test_guard_capture_rejects_non_allowlisted_query_before_read(self):
        native = types.SimpleNamespace(PERMISSION_QUERY='SELECT 1;', EXPECTED_DATABASE=self.expected())
        query = Mock()
        namespace = {'query_sql': query}
        D.guard_module('reviewed_i09_inventory', namespace, native, Mock(), {})
        with self.assertRaisesRegex(D.DiagnosisError, '^non_read_only_sql_blocked$'):
            namespace['query_sql']('/opt/mssql-tools/bin/sqlcmd', {'password': SECRET}, 'SELECT * FROM dbo.AdminUsers;')
        query.assert_not_called()

    def test_loader_rejects_changed_installer_before_execution_and_checks_payload_identity(self):
        raw = (ROOT / 'scripts/pilot/install-customer-access-update.py').read_text().replace(
            'REPLACE_SOURCE_COMMIT', D.SOURCE).replace('REPLACE_PAYLOAD_SHA256', D.PAYLOAD_SHA256)
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'installer.py'
            path.write_text(raw)
            owner = types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_nlink=1, st_uid=0)
            with patch.object(D.os, 'fstat', return_value=owner), \
                    patch.object(D, 'INSTALLER_SHA256', hashlib.sha256(raw.encode()).hexdigest()):
                loaded = D.trusted_installer(path)
                self.assertEqual(D.SOURCE, loaded.EXPECTED_SOURCE)
                path.write_text('raise RuntimeError("' + SECRET + '")')
                with self.assertRaisesRegex(D.DiagnosisError, '^installer_sha256_mismatch$'):
                    D.trusted_installer(path)

    def test_only_preflight_is_selected_and_unknown_execution_namespace_is_blocked(self):
        class InstallError(Exception):
            pass
        instance = Mock()
        native = types.SimpleNamespace(PERMISSION_QUERY='SELECT 1;', InstallError=InstallError,
                                      make_installer=Mock(return_value=instance))
        with patch.object(D, 'trusted_installer', return_value=native):
            report = D.diagnose(Path('package.zip'), Path('installer.py'))
        self.assertEqual('inspected', report['status'])
        instance.preflight.assert_called_once_with()
        for name in ('main', 'apply', 'stage', 'switch', 'rollback', 'health'):
            getattr(instance, name).assert_not_called()
        with self.assertRaisesRegex(D.DiagnosisError, '^unexpected_dependency_execution_blocked$'):
            native.exec(compile('raise RuntimeError("' + SECRET + '")', 'private.py', 'exec'), {'__name__': 'untrusted'})

    def test_complete_original_preflight_captures_differences_before_guard_with_no_mutation(self):
        for changed in (None, ('customer_channel', 0), ('insert_tokens', 0), ('read_profiles', None)):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                native = module('customer_access_original_preflight_fixture', 'install-customer-access-update.py')
                root = Path(temporary)
                native.ROOT, native.BACKUPS, native.ENV_FILE = root / 'app', root / 'backups', root / 'api.env'
                native.EXPECTED_SOURCE = D.SOURCE
                old = {part: native.ROOT / part / 'releases' / native.I07 for part in ('backend', 'frontend')}
                for part, release in old.items():
                    release.mkdir(parents=True)
                    (native.ROOT / part / 'current').symlink_to(release)
                dll = old['backend'] / 'Prodimt.Pedidos.Api.dll'
                dll.write_bytes(b'fictional')
                (old['frontend'] / 'index.html').write_bytes(b'fictional-index')
                (old['backend'] / 'appsettings.json').write_bytes(b'{}')
                native.ENV_FILE.write_bytes(b'private-existing-env')
                environment = {'ASPNETCORE_ENVIRONMENT': 'Production', 'Persistence__Provider': 'SqlServer',
                    'ConnectionStrings__Pedidos': 'Server=127.0.0.1,14330;Database=ProdimtPedidos;'
                        'User Id=prodimt_pedidos_app;Password=' + SECRET + ';Encrypt=True;TrustServerCertificate=True',
                    'Authentication__Jwt__SigningKey': 'Fictional-JWT-key-012345678901234567890123456789',
                    'Authentication__Jwt__AccessTokenMinutes': '120'}
                proc_environment = ('\0'.join(key + '=' + value for key, value in environment.items()) + '\0').encode()
                cmdline = ('dotnet\0' + str(dll) + '\0').encode()
                properties = '\n'.join(['ActiveState=active', 'MainPID=424242', 'User=www-data', 'Group=',
                    'WorkingDirectory=' + str(native.ROOT / 'backend/current'),
                    'EnvironmentFiles=' + str(native.ENV_FILE) + ' (ignore_errors=no)', 'Environment=',
                    'FragmentPath=/synthetic/api.service', 'DropInPaths='])
                row = self.expected()
                if changed is not None:
                    if changed[1] is None:
                        del row[changed[0]]  # SQL FOR JSON omits NULL without INCLUDE_NULL_VALUES.
                    else:
                        row[changed[0]] = changed[1]
                row['private_extra'] = SECRET
                if changed is None:
                    del row['private_extra']
                files = {'inspect-native.py': (ROOT / 'scripts/pilot/inspect-native.py').read_bytes(),
                         'install-password-update.py': (ROOT / 'scripts/pilot/install-password-update.py').read_bytes(),
                         'backend/appsettings.json': b'{}'}
                manifest = {'previousFrontendReleases': [native.I07]}
                original_guard = D.guard_module
                def guard(name, namespace, installer, process, report):
                    original_guard(name, namespace, installer, process, report)
                    if name == 'reviewed_i09_activation_runtime':
                        namespace['regular'] = lambda path, *args: cmdline if str(path) == '/proc/424242/cmdline' else path.read_bytes()
                        namespace['directory_safe'] = lambda path: None
                original_resolve, original_open, original_is_file = Path.resolve, Path.open, Path.is_file
                def resolve(path, *args, **kwargs):
                    return old['backend'] if str(path) == '/proc/424242/cwd' else original_resolve(path, *args, **kwargs)
                def opened(path, *args, **kwargs):
                    return io.BytesIO(proc_environment) if str(path) == '/proc/424242/environ' else original_open(path, *args, **kwargs)
                seen = []
                def transport(args, **kwargs):
                    seen.append(args)
                    self.assertEqual(subprocess.DEVNULL, kwargs['stdin'])
                    if args[0] == 'systemctl':
                        return subprocess.CompletedProcess(args, 0, properties, '')
                    if args[0] == 'curl':
                        body = '{"database":"reachable"}' if args[-1].endswith('/health/db') else 'fictional-index'
                        return subprocess.CompletedProcess(args, 0, body + '\n200', '')
                    self.assertEqual(D.SQL_TOOLS[0], args[0])
                    self.assertEqual(SECRET, kwargs['env']['SQLCMDPASSWORD'])
                    return subprocess.CompletedProcess(args, 0, json.dumps(row), '')
                with patch.object(D, 'trusted_installer', return_value=native), \
                        patch.object(native, 'load_payload', return_value=(files, manifest)), \
                        patch.object(D, 'guard_module', side_effect=guard), \
                        patch.object(Path, 'resolve', new=resolve), patch.object(Path, 'open', new=opened), \
                        patch.object(Path, 'is_file', new=lambda path: str(path) == D.SQL_TOOLS[0] or original_is_file(path)), \
                        patch.object(D.subprocess, 'run', side_effect=transport):
                    report = D.diagnose(root / 'payload.zip', root / 'installer.py')
                self.assertEqual('inspected', report['status'], report)
                self.assertTrue(report['sql_metadata_read_completed'], report)
                self.assertFalse(report['changes_made'])
                self.assertFalse(report['database_writes_run'])
                self.assertFalse(report['service_restarted'])
                if changed is None:
                    self.assertTrue(report['preflight_completed'], report)
                    self.assertTrue(report['database_contract_matches'])
                    self.assertEqual([], report['database_mismatches'])
                else:
                    self.assertFalse(report['preflight_completed'])
                    self.assertTrue(report['original_preflight_guard_blocked'])
                    self.assertEqual([changed[0]], report['database_mismatches'])
                self.assertNotIn(SECRET, json.dumps(report))
                self.assertNotIn('private_extra', json.dumps(report))
                self.assertTrue(all(args[0] in ('systemctl', 'curl', D.SQL_TOOLS[0]) for args in seen))


if __name__ == '__main__':
    unittest.main()
