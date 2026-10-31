"""I07 auth diagnosis: read-only boundary, exact pins and private failure output."""
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


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


diagnosis = module('capture_auth_diagnosis_test', 'diagnose-capture-auth.py')
installer = module('capture_auth_installer_test', 'install-quick-capture-update.py')
SECRET = 'Fictional-only-secret-88417-ñ'
LOGIN = 'private-synthetic-sql-login'


class CaptureAuthDiagnosisTests(unittest.TestCase):
    def test_odbc_login_failure_without_msg_header_is_identified_and_redacted(self):
        result = diagnosis.classify_error('', 'Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : '
            "Login failed for user '" + LOGIN + "'. " + SECRET)
        self.assertEqual('sql_login_rejected', result['category'])
        self.assertEqual([], result['sql_error_numbers'])
        self.assertNotIn(LOGIN, json.dumps(result))
        self.assertNotIn(SECRET, json.dumps(result))

    def test_driver_categories_include_disabled_locked_expired_database_tls_options_and_network(self):
        cases = {
            "[28000] Private login diagnostic": 'sql_login_rejected',
            'Error de inicio de sesión del usuario privado': 'sql_login_rejected',
            'Login failed (18470)': 'sql_login_disabled',
            'Login failed (18486)': 'sql_login_locked',
            'Login failed (18487)': 'sql_password_expired_or_change_required',
            'Login failed (18488)': 'sql_password_expired_or_change_required',
            'Cannot open database requested by the login (4060)': 'sql_database_access_failed',
            'SSL Provider: certificate verify failed': 'sql_tls_or_certificate_failed',
            '[IM002] Data source name not found': 'sql_odbc_driver_unavailable',
            "Can't open lib 'private-driver-path'": 'sql_odbc_driver_unavailable',
            "Sqlcmd: Error: Invalid option '-I'": 'sqlcmd_option_failed',
            'Invalid value for connection string attribute': 'sql_client_attribute_failed',
            '[HYT00] Login timeout expired': 'sql_connection_or_query_timeout',
            '[08001] TCP Provider: Connection refused': 'sql_connection_failed',
            'Msg 229, Level 14, State 5, Server synthetic, Line 1\nPrivate database detail': 'sql_permissions_failed',
            'Msg 207, Level 16, State 1, Server synthetic, Line 1\nPrivate column': 'sql_statement_failed',
            'An unrecognized private failure': 'sql_client_failure_unclassified'
        }
        for message, category in cases.items():
            with self.subTest(category=category):
                result = diagnosis.classify_error(message, SECRET)
                self.assertEqual(category, result['category'])
                self.assertNotIn(SECRET, json.dumps(result))
                self.assertNotIn(message, json.dumps(result))

    def test_only_anchored_sql_headers_and_allowlisted_states_are_retained(self):
        result = diagnosis.classify_error('Private number 88417 [88417]',
            'Msg 207, Level 16, State 1, Server synthetic, Line 1\n[SQLSTATE: 08001]')
        self.assertEqual([207], result['sql_error_numbers'])
        self.assertEqual(['08001'], result['sqlstates'])
        self.assertNotIn('88417', json.dumps(result))

    def fixture(self):
        instance = installer.Installer(Path('fictional.zip'))
        instance.sql_tool = '/opt/mssql-tools/bin/sqlcmd'
        class InspectionError(Exception): pass
        instance.inspector = types.SimpleNamespace(SQL_TOOLS=(instance.sql_tool,), SQL_MIGRATIONS='SELECT MigrationId FOR JSON PATH;',
            parse_sql_output=json.loads, InspectionError=InspectionError)
        report = {'sql_reads': []}
        process = diagnosis.ReadOnlyProcess(installer, instance, report)
        args = [instance.sql_tool, '-S', installer.SERVER, '-d', installer.DATABASE,
                '-C', '-I', '-b', '-l', '5', '-t', '25', '-x', '-y', '0', '-w', '65535']
        return instance, report, process, args

    def test_read_only_allowlist_uses_the_original_session_and_secrets_only_in_environment(self):
        instance, report, process, args = self.fixture()
        for name, query in diagnosis.read_queries(installer, instance).items():
            with self.subTest(stage=name), patch.object(diagnosis.subprocess, 'run',
                    return_value=subprocess.CompletedProcess([], 0, '{}', '')) as run:
                script = installer.OPTIONS + 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
                process.run(args, input=script, env=dict(installer.CLEAN_ENV, SQLCMDUSER=LOGIN, SQLCMDPASSWORD=SECRET),
                            timeout=30, capture_output=True, text=True, check=False)
                self.assertNotIn(SECRET, ' '.join(run.call_args.args[0]))
                self.assertEqual(SECRET, run.call_args.kwargs['env']['SQLCMDPASSWORD'])
                self.assertEqual(name, report['sql_reads'][-1]['stage'])
        self.assertNotIn(SECRET, json.dumps(report))
        self.assertNotIn(LOGIN, json.dumps(report))

    def test_writes_and_non_allowlisted_sql_are_blocked_before_process_execution(self):
        _, _, process, args = self.fixture()
        for script in ('ALTER TABLE dbo.Customers ADD Private int;', 'BACKUP DATABASE private;',
                       'SELECT 1; DROP TABLE dbo.Customers;', 'SELECT * FROM dbo.AdminUsers;'):
            with self.subTest(script=script), patch.object(diagnosis.subprocess, 'run') as run:
                with self.assertRaisesRegex(diagnosis.DiagnosisError, '^non_read_only_sql_blocked$'):
                    process.run(args, input=script)
                run.assert_not_called()
        for command in (['systemctl', 'stop', installer.SERVICE], ['systemctl', 'restart', installer.SERVICE],
                        ['bash', '-c', 'unsafe'], ['curl', 'https://' + installer.DOMAIN + '/health/db', '--data', '{}']):
            with self.subTest(command=command[0]), patch.object(diagnosis.subprocess, 'run') as run:
                with self.assertRaises(diagnosis.DiagnosisError): process.run(command)
                run.assert_not_called()

    def test_failed_sql_read_is_classified_without_the_original_streams(self):
        instance, report, process, args = self.fixture()
        query = installer.OPTIONS + 'SELECT (' + installer.ADMIN_QUERY.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        with patch.object(diagnosis.subprocess, 'run', return_value=subprocess.CompletedProcess(args, 1, '',
                'Sqlcmd: Error: Login failed for user ' + LOGIN + ' ' + SECRET)):
            process.run(args, input=query)
        self.assertEqual('administrator_permissions', report['sql_reads'][0]['stage'])
        self.assertEqual('sql_login_rejected', report['sql_reads'][0]['category'])
        self.assertNotIn(SECRET, json.dumps(report))
        self.assertNotIn(LOGIN, json.dumps(report))

    def test_original_authentication_uses_only_allowlisted_reads_through_schema_verification(self):
        instance, report, process, args = self.fixture()
        responses = dict(
            administrator_permissions=dict(database_name=installer.DATABASE, sysadmin=1, dbcreator=0, db_owner=1,
                backup_permission=1, verify_permission=1, view_definition=1, files_mib=8),
            migration_history=[{'MigrationId': x} for x in installer.MIGRATIONS[:10]],
            rest_days_schema=dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1),
            capture_alias_schema=dict(columns_count=0, valid_column=0, indexes_count=0, valid_index=0),
            rest_days_data={'invalid': 0})
        backup = Mock()
        backup.lstat.return_value = types.SimpleNamespace(st_mode=stat.S_IFDIR | 0o750, st_uid=999, st_gid=988)
        backup.resolve.return_value = backup
        query_map = {installer.OPTIONS + 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;': name
            for name, query in diagnosis.read_queries(installer, instance).items()}
        def returned(_args, **kwargs):
            return subprocess.CompletedProcess(_args, 0, json.dumps(responses[query_map[kwargs['input']]]), '')
        proxy = types.SimpleNamespace(run=process.run, TimeoutExpired=subprocess.TimeoutExpired)
        with patch.object(installer, 'prompt_secret', side_effect=[LOGIN, SECRET]), \
                patch.object(installer, 'SQL_BACKUPS', backup), \
                patch.object(installer.shutil, 'disk_usage', return_value=types.SimpleNamespace(free=10**12)), \
                patch.object(installer, 'subprocess', proxy), \
                patch.object(diagnosis.subprocess, 'run', side_effect=returned):
            instance.authenticate_admin()
        self.assertEqual(10, instance.state['migrations_before'])
        self.assertEqual(list(responses), [row['stage'] for row in report['sql_reads']])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_admin_metadata_output_only_contains_boolean_permissions(self):
        _, report, process, args = self.fixture()
        data = dict(database_name=installer.DATABASE, sysadmin=1, dbcreator=0, db_owner=1,
                    backup_permission=1, verify_permission=1, view_definition=1, private_password=SECRET)
        query = installer.OPTIONS + 'SELECT (' + installer.ADMIN_QUERY.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        with patch.object(diagnosis.subprocess, 'run', return_value=subprocess.CompletedProcess(args, 0, json.dumps(data), '')):
            process.run(args, input=query)
        self.assertEqual(1, report['administrator_permissions']['sysadmin'])
        self.assertNotIn(SECRET, json.dumps(report))
        self.assertNotIn('private_password', json.dumps(report))

    def test_timeout_reports_exception_type_only(self):
        _, report, process, args = self.fixture()
        query = installer.OPTIONS + 'SELECT (' + installer.ADMIN_QUERY.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        with patch.object(diagnosis.subprocess, 'run', side_effect=subprocess.TimeoutExpired(SECRET, 30)):
            with self.assertRaises(subprocess.TimeoutExpired): process.run(args, input=query)
        self.assertEqual('TimeoutExpired', report['sql_reads'][0]['process_error'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_trusted_loader_matches_delivered_pin_and_rejects_changed_code_before_execution(self):
        raw = (ROOT / 'scripts/pilot/install-quick-capture-update.py').read_text().replace(
            "EXPECTED_PACKAGE_SHA256 = 'REPLACE_PACKAGE_SHA256'",
            "EXPECTED_PACKAGE_SHA256 = '06c1a7fd68ec997b7ba7efa92ddf65de6b35fae58dc800ed7ec2596a0741755b'").replace(
            "EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'", "EXPECTED_SOURCE = '46fed604732eddca0c7ac199b58bb0b429545b89'")
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'pinned.py'; path.write_text(raw)
            owner = types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_nlink=1, st_uid=0)
            with patch.object(diagnosis.os, 'fstat', return_value=owner):
                loaded = diagnosis.trusted_installer(path)
                self.assertEqual('46fed604732eddca0c7ac199b58bb0b429545b89', loaded.EXPECTED_SOURCE)
                path.write_text('raise RuntimeError("must not execute")')
                with self.assertRaisesRegex(diagnosis.DiagnosisError, '^installer_sha256_mismatch$'):
                    diagnosis.trusted_installer(path)

    def test_diagnosis_only_invokes_preflight_and_authentication_and_clears_secrets(self):
        class InstallError(Exception): pass
        for failure in (None, InstallError('sql_operation_failed'), ValueError(SECRET)):
            with self.subTest(failure=type(failure).__name__):
                instance = Mock()
                instance.state = dict(database_checks={'database_name': 'ProdimtPedidos'}, migrations_before=10)
                instance.sql_tool = '/opt/mssql-tools/bin/sqlcmd'
                instance.sql_login, instance.sql_password = LOGIN, SECRET
                instance.authenticate_admin.side_effect = failure
                fake = types.SimpleNamespace(Installer=Mock(return_value=instance), InstallError=InstallError)
                with patch.object(diagnosis, 'trusted_installer', return_value=fake):
                    report = diagnosis.diagnose(Path('payload.zip'), Path('installer.py'))
                instance.preflight.assert_called_once_with()
                instance.authenticate_admin.assert_called_once_with()
                for name in ('apply', 'stage', 'database_backup', 'backup_and_migrate', 'switch', 'rollback', 'health'):
                    getattr(instance, name).assert_not_called()
                self.assertEqual('', instance.sql_password)
                self.assertFalse(report['changes_made'])
                self.assertFalse(report['database_migrations_run'])
                self.assertEqual('inspected' if failure is None else 'failed', report['status'])
                self.assertNotIn(SECRET, json.dumps(report))

    def test_complete_original_preflight_and_authentication_keep_devnull_and_private_output(self):
        # Run both original methods, including process(), command(), http() and
        # the real inspector/configuration parser. Only host files/transport are
        # fictional. Reverting DEVNULL on the adapter reproduces the user's
        # AttributeError before the very first subprocess can run.
        for reject_admin in (False, True):
            with self.subTest(reject_admin=reject_admin), tempfile.TemporaryDirectory() as tmp:
                native = module('native_full_preflight_fixture', 'install-quick-capture-update.py')
                root = Path(tmp); app = root / 'app'; env_file = root / 'api.env'
                old = {part: app / part / 'releases' / native.OLD_NAME for part in ('backend','frontend')}
                for part, release in old.items():
                    release.mkdir(parents=True); (app / part / 'current').symlink_to(release)
                dll = old['backend'] / 'Prodimt.Pedidos.Api.dll'; dll.write_bytes(b'fictional')
                native.ROOT, native.BACKUPS, native.ENV_FILE = app, root / 'backups', env_file
                environment = {'ASPNETCORE_ENVIRONMENT':'Production', 'Persistence__Provider':'SqlServer',
                    'ConnectionStrings__Pedidos':'Server=127.0.0.1,14330;Database=ProdimtPedidos;'
                        'User Id=prodimt_pedidos_app;Password='+SECRET+';Encrypt=True;TrustServerCertificate=True',
                    'Authentication__Jwt__SigningKey':'Fictional-JWT-key-012345678901234567890123456789',
                    'Authentication__Jwt__AccessTokenMinutes':'120'}
                proc_environment = ('\0'.join(k+'='+v for k,v in environment.items())+'\0').encode()
                cmdline = ('dotnet\0'+str(dll)+'\0').encode()
                properties = '\n'.join(['ActiveState=active','MainPID=424242','User=www-data','Group=',
                    'WorkingDirectory='+str(app/'backend/current'),
                    'EnvironmentFiles='+str(env_file)+' (ignore_errors=no)', 'Environment=',
                    'FragmentPath=/synthetic/api.service','DropInPaths='])
                permissions = dict(database_name=native.DATABASE,migrations_count=10,i04=1,read_admin=1,update_admin=1,
                    read_customers=1,update_customers=1,write_audit=1,read_profiles=1,insert_profiles=1,update_profiles=1,
                    delete_profiles=1,read_operations=1,write_operations=1,write_order_audit=1)
                responses = dict(administrator_permissions=dict(database_name=native.DATABASE,sysadmin=1,dbcreator=0,
                    db_owner=1,backup_permission=1,verify_permission=1,view_definition=1,files_mib=8),
                    migration_history=[{'MigrationId': x} for x in native.MIGRATIONS[:10]],
                    rest_days_schema=dict(columns_count=1,valid_column=1,checks_count=1,valid_check=1),
                    capture_alias_schema=dict(columns_count=0,valid_column=0,indexes_count=0,valid_index=0),
                    rest_days_data={'invalid':0})
                payload = {'inspect-native.py':(ROOT/'scripts/pilot/inspect-native.py').read_bytes()}
                backup = Mock(); backup.resolve.return_value = backup
                backup.lstat.return_value = types.SimpleNamespace(st_mode=stat.S_IFDIR|0o750,st_uid=999,st_gid=988)
                native.SQL_BACKUPS = backup
                original_resolve, original_open, original_is_file = Path.resolve, Path.open, Path.is_file
                seen = []
                def resolve(path, *args, **kwargs):
                    return old['backend'] if str(path)=='/proc/424242/cwd' else original_resolve(path,*args,**kwargs)
                def opened(path, *args, **kwargs):
                    return io.BytesIO(proc_environment) if str(path)=='/proc/424242/environ' else original_open(path,*args,**kwargs)
                def regular(path, *args):
                    if str(path)=='/proc/424242/cmdline':return cmdline
                    if path==env_file:return b'fictional environment'
                    return path.read_bytes()
                def transport(args, **kwargs):
                    seen.append(args)
                    if args[0]=='systemctl':
                        self.assertEqual(subprocess.DEVNULL,kwargs['stdin'])
                        return subprocess.CompletedProcess(args,0,properties,'')
                    if args[0]=='curl':
                        self.assertEqual(subprocess.DEVNULL,kwargs['stdin'])
                        return subprocess.CompletedProcess(args,0,'{"database":"reachable"}\n200','')
                    self.assertEqual('/opt/mssql-tools/bin/sqlcmd',args[0])
                    if '-Q' in args:return subprocess.CompletedProcess(args,0,json.dumps(permissions),'')
                    source = kwargs['input']
                    if reject_admin and 'IS_SRVROLEMEMBER' in source:
                        return subprocess.CompletedProcess(args,1,'','Sqlcmd: Login failed for user '+LOGIN+' '+SECRET)
                    if 'IS_SRVROLEMEMBER' in source:row=responses['administrator_permissions']
                    elif '__EFMigrationsHistory' in source:row=responses['migration_history']
                    elif 'AS invalid' in source:row=responses['rest_days_data']
                    elif 'CaptureAlias' in source:row=responses['capture_alias_schema']
                    else:row=responses['rest_days_schema']
                    return subprocess.CompletedProcess(args,0,json.dumps(row),'')
                with patch.object(diagnosis,'trusted_installer',return_value=native), \
                        patch.object(native,'load_package',return_value=payload), \
                        patch.object(native,'regular',side_effect=regular), patch.object(native,'directory_safe'), \
                        patch.object(native,'prompt_secret',side_effect=[LOGIN,SECRET]), \
                        patch.object(native.shutil,'disk_usage',return_value=types.SimpleNamespace(free=10**12)), \
                        patch.object(Path,'resolve',new=resolve), patch.object(Path,'open',new=opened), \
                        patch.object(Path,'is_file',new=lambda path: str(path)=='/opt/mssql-tools/bin/sqlcmd' or original_is_file(path)), \
                        patch.object(diagnosis.subprocess,'run',side_effect=transport):
                    report = diagnosis.diagnose(root/'payload.zip',root/'installer.py')
                self.assertTrue(report.get('preflight_completed'),report)
                self.assertEqual('failed' if reject_admin else 'inspected',report['status'])
                if reject_admin:self.assertEqual('sql_login_rejected',report['sql_reads'][0]['category'])
                else:self.assertEqual(10,report['migrations_before'])
                self.assertEqual(['systemctl','/opt/mssql-tools/bin/sqlcmd','curl'],[a[0] for a in seen[:3]])
                self.assertFalse(report['changes_made'])
                self.assertFalse(report['database_migrations_run'])
                self.assertNotIn(SECRET,json.dumps(report))
                self.assertNotIn(LOGIN,json.dumps(report))


if __name__ == '__main__':
    unittest.main()
