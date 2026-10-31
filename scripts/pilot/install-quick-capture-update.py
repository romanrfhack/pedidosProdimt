#!/usr/bin/env python3
"""Install I07 quick order capture on the existing native I06.

Verified COPY_ONLY backup precedes the additive migration. Preserve configuration
and accounts. Failures restore application links, never restore SQL automatically.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import getpass
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import shutil
import stat
import subprocess
import sys
import time
import types
import uuid
import zipfile
import warnings

EXPECTED_PACKAGE_SHA256 = 'REPLACE_PACKAGE_SHA256'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
OLD_NAME = 'catalog-965dc65d82d2'
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
SERVER = '127.0.0.1,14330'
DATABASE = 'ProdimtPedidos'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
MIGRATIONS = [
    '20260527190837_InitialCreate', '20260528021837_AddOrderAuditLogs',
    '20260528025208_AddPilotAuthentication', '20260528035015_AddCatalogManagementSupport',
    '20260528131416_AddCatalogExternalCodesAndImportSupport',
    '20261024164610_AddPilotCatalogAndOrderSnapshots', '20261024181206_AddDailyOrderOperations',
    '20261024181934_ExpandOrderAuditSnapshots', '20261024202253_AddOrderChangeNotices',
    '20261028142346_AddCustomerRestDays', '20261028202031_AddCustomerCaptureAliases',
]
OPTIONS = ('SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000; '
           'SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; '
           'SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; '
           'SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;\n')
PERMISSION_QUERY = """SELECT DB_NAME() AS database_name,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261024202253_AddOrderChangeNotices') AS i04,
 HAS_PERMS_BY_NAME(N'dbo.AdminUsers',N'OBJECT',N'SELECT') AS read_admin,
 HAS_PERMS_BY_NAME(N'dbo.AdminUsers',N'OBJECT',N'UPDATE') AS update_admin,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'SELECT') AS read_customers,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'UPDATE') AS update_customers,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'INSERT') AS write_audit,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'SELECT') AS read_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'INSERT') AS insert_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'UPDATE') AS update_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'DELETE') AS delete_profiles,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'SELECT') AS read_operations,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'INSERT') AS write_operations,
 HAS_PERMS_BY_NAME(N'dbo.OrderAuditLogs',N'OBJECT',N'INSERT') AS write_order_audit
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
ADMIN_QUERY = """SELECT DB_NAME() AS database_name,
 IS_SRVROLEMEMBER(N'sysadmin') AS sysadmin, IS_SRVROLEMEMBER(N'dbcreator') AS dbcreator,
 IS_ROLEMEMBER(N'db_owner') AS db_owner,
 HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'BACKUP DATABASE') AS backup_permission,
 HAS_PERMS_BY_NAME(NULL,N'SERVER',N'CREATE ANY DATABASE') AS verify_permission,
 HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION') AS view_definition,
 (SELECT SUM(CAST(size AS bigint))*8/1024 FROM sys.database_files) AS files_mib
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
SCHEMA_QUERY = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Customers') AND name=N'RestDaysMask') AS columns_count,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
   WHERE c.object_id=OBJECT_ID(N'dbo.Customers') AND c.name=N'RestDaysMask'
   AND TYPE_NAME(c.system_type_id)=N'int' AND c.max_length=4 AND c.is_nullable=0
   AND REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N'')=N'0') AS valid_column,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Customers')
   AND name=N'CK_Customers_RestDaysMask') AS checks_count,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Customers')
   AND name=N'CK_Customers_RestDaysMask' AND is_disabled=0 AND is_not_trusted=0
   AND REPLACE(REPLACE(REPLACE(REPLACE(LOWER(definition),N' ',N''),N'(',N''),N')',N''),N'\n',N'')
       IN (N'[restdaysmask]>=0and[restdaysmask]<=127',N'[restdaysmask]between0and127')) AS valid_check
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
ALIAS_SCHEMA_QUERY = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerFrequentProducts') AND name=N'CaptureAlias') AS columns_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerFrequentProducts') AND name=N'CaptureAlias'
   AND TYPE_NAME(system_type_id)=N'nvarchar' AND max_length=32 AND is_nullable=1 AND default_object_id=0) AS valid_column,
 (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerFrequentProducts')
   AND name=N'IX_CustomerFrequentProducts_CustomerId_CaptureAlias') AS indexes_count,
 (SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.CustomerFrequentProducts')
   AND i.name=N'IX_CustomerFrequentProducts_CustomerId_CaptureAlias' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=1
   AND REPLACE(REPLACE(REPLACE(REPLACE(UPPER(i.filter_definition),N' ',N''),N'(',N''),N')',N''),N'\n',N'')=N'[CAPTUREALIAS]ISNOTNULL'
   AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id)=2
   AND (SELECT COUNT(*) FROM sys.index_columns k JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id
     WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.is_included_column=0 AND k.is_descending_key=0
       AND ((k.key_ordinal=1 AND c.name=N'CustomerId') OR (k.key_ordinal=2 AND c.name=N'CaptureAlias')))=2) AS valid_index
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
COUNTS_QUERY = """SELECT
 (SELECT COUNT_BIG(*) FROM dbo.Customers) AS customers,
 (SELECT COUNT_BIG(*) FROM dbo.Orders) AS orders,
 (SELECT COUNT_BIG(*) FROM dbo.OrderLines) AS lines,
 (SELECT COUNT_BIG(*) FROM dbo.AdminUsers) AS administrators,
 (SELECT COUNT_BIG(*) FROM dbo.AuditLogs) AS audits,
 (SELECT COUNT_BIG(*) FROM dbo.OrderAuditLogs) AS order_audits,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerFrequentProducts) AS profiles,
 (SELECT COUNT_BIG(*) FROM dbo.Products) AS products,
 (SELECT COUNT_BIG(*) FROM dbo.Machines) AS machines
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


def prompt_secret(prompt):
    try:
        with open('/dev/tty', 'w', encoding='utf-8') as tty, warnings.catch_warnings():
            require(tty.isatty(), 'interactive_tty_required')
            warnings.simplefilter('error', getpass.GetPassWarning)
            return getpass.getpass(prompt, stream=tty)
    except (OSError, EOFError, getpass.GetPassWarning):
        raise InstallError('interactive_tty_required') from None


class InstallError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path, limit=1048576):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        st = os.fstat(stream.fileno())
        require(stat.S_ISREG(st.st_mode) and st.st_nlink == 1 and st.st_uid in (0, 33)
                and not st.st_mode & 0o022, 'file_owner_or_type_unexpected')
        data = stream.read(limit + 1)
        require(len(data) <= limit, 'file_too_large')
        return data


def command(args, timeout=30):
    try:
        result = subprocess.run(args, stdin=subprocess.DEVNULL, capture_output=True, text=True,
                                env=CLEAN_ENV, timeout=timeout, check=False)
        require(result.returncode == 0, 'local_command_failed')
        return result.stdout
    except (OSError, subprocess.TimeoutExpired, UnicodeError):
        raise InstallError('local_command_failed') from None


def safe_name(name):
    path = PurePosixPath(name)
    return (isinstance(name, str) and bool(path.parts) and name not in ('', '.') and not path.is_absolute() and path.as_posix() == name
            and not any(part in ('', '.', '..') for part in path.parts) and ':' not in name and '\\' not in name)


def load_package(path):
    require(re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PACKAGE_SHA256) is not None
            and re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE) is not None, 'installer_not_pinned')
    require(sha(regular(path, 40 * 1024 * 1024)) == EXPECTED_PACKAGE_SHA256, 'package_hash_mismatch')
    with zipfile.ZipFile(path) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 200 and len({row.filename for row in rows}) == len(rows)
                and sum(row.file_size for row in rows) <= 100 * 1024 * 1024, 'package_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and not stat.S_ISLNK(row.external_attr >> 16) for row in rows), 'package_path_invalid')
        files = {row.filename: archive.read(row) for row in rows}
    manifest = json.loads(files.pop('manifest.json'))
    require(manifest.get('sourceCommit') == EXPECTED_SOURCE and manifest.get('feature') == 'admin-quick-capture-v1'
            and set(manifest.get('files', {})) == set(files), 'package_manifest_unexpected')
    require(all(meta == {'bytes': len(files[name]), 'sha256': sha(files[name])}
                for name, meta in manifest['files'].items()), 'package_file_hash_mismatch')
    require(all(name in ('inspect-native.py', 'migrations-i07.sql') or name.startswith(('backend/', 'frontend/')) for name in files)
            and {'inspect-native.py', 'migrations-i07.sql', 'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'} <= set(files),
            'package_file_set_invalid')
    return files


def directory_safe(path):
    st = path.lstat()
    require(stat.S_ISDIR(st.st_mode) and st.st_uid == 0 and not st.st_mode & 0o022
            and path.resolve(strict=True) == path, 'directory_owner_or_path_unexpected')


class Installer:
    def __init__(self, package):
        self.package = package
        self.files = {}
        self.old = {part: ROOT / part / 'releases' / OLD_NAME for part in ('backend', 'frontend')}
        self.new = {part: ROOT / part / 'releases' / ('capture-' + EXPECTED_SOURCE[:12]) for part in self.old}
        self.state = {'revision': '20261028-capture-install1', 'status': 'planned', 'changes_made': False,
                      'database_migrations_run': False, 'migrations_may_have_changed': False,
                      'database_automatically_restored': False, 'secret_values_printed': False, 'password_changed': False, 'health_verified': False}
        self.stop_requested = False

    def process(self, expected):
        properties = command(['systemctl', 'show', SERVICE,
            '--property=ActiveState,MainPID,User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'])
        values = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
        require(values.get('ActiveState') == 'active' and values.get('User') == 'www-data'
                and values.get('WorkingDirectory') == str(ROOT / 'backend/current')
                and values.get('EnvironmentFiles') == str(ENV_FILE) + ' (ignore_errors=no)'
                and values.get('MainPID', '').isdigit() and int(values['MainPID']) > 0, 'service_identity_unexpected')
        pid = int(values['MainPID'])
        cwd = Path(f'/proc/{pid}/cwd').resolve(strict=True)
        argv = regular(Path(f'/proc/{pid}/cmdline')).decode().rstrip('\0').split('\0')
        dll = [Path(arg) for arg in argv if Path(arg).name == 'Prodimt.Pedidos.Api.dll']
        require(cwd == expected and len(dll) == 1
                and (dll[0] if dll[0].is_absolute() else cwd / dll[0]).resolve(strict=True) == expected / 'Prodimt.Pedidos.Api.dll',
                'running_release_unexpected')
        configuration = {key: values.get(key, '') for key in ('User', 'Group', 'WorkingDirectory', 'Environment',
                                                             'EnvironmentFiles', 'FragmentPath', 'DropInPaths')}
        if hasattr(self, 'unit_configuration'):
            require(configuration == self.unit_configuration, 'service_configuration_changed')
        else:
            self.unit_configuration = configuration
        return pid, cwd, argv

    def links(self, expected):
        require(all((ROOT / part / 'current').is_symlink()
                    and (ROOT / part / 'current').resolve(strict=True) == path for part, path in expected.items()),
                'current_release_changed')

    def http(self, path, *, post=False):
        args = ['curl', '--silent', '--show-error', '--noproxy', '*', '--max-time', '8',
                '--max-filesize', '2097152', '--resolve', DOMAIN + ':443:127.0.0.1',
                '--write-out', '\n%{http_code}', 'https://' + DOMAIN + path]
        if post:
            args += ['--request', 'POST', '--header', 'Content-Type: application/json', '--data', '{}']
        raw = command(args, timeout=12)
        body, code = raw.rsplit('\n', 1)
        return int(code), body.encode()

    def preflight(self):
        self.files = load_package(self.package)
        self.links(self.old)
        pid, cwd, argv = self.process(self.old['backend'])
        for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.old)):
            directory_safe(path)
        self.environment = regular(ENV_FILE)
        inspector = types.ModuleType('reviewed_capture_inventory')
        exec(compile(self.files['inspect-native.py'], 'inspect-native.py', 'exec'), inspector.__dict__)
        environment = inspector.process_environment(pid)
        configuration, source = inspector.configuration(environment, argv, cwd)
        flags = inspector.policy_flags(configuration)
        require(source['environment'] == 'Production' and flags == {
            'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
            'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
            'development_seed_enabled': False, 'automatic_migrations_enabled': False, 'https_redirection_enabled': True},
                'production_configuration_unexpected')
        fields = inspector.connection_fields(inspector.first(configuration, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
        require(inspector.permitted_connection(fields), 'sql_target_unexpected')
        inspector.QUERIES = (PERMISSION_QUERY,)
        tool = next((p for p in inspector.SQL_TOOLS if Path(p).is_file()), None)
        self.inspector, self.sql_tool, self.fields = inspector, tool, fields
        permissions = inspector.query_sql(tool, fields, PERMISSION_QUERY)
        self.state['database_checks'] = permissions
        self.check_app_permissions(permissions, (10, 11))
        self.overrides = {}
        for name in ('appsettings.json', 'appsettings.Production.json'):
            path = self.old['backend'] / name
            if path.exists():
                self.overrides['backend/' + name] = regular(path)
        require(shutil.disk_usage(ROOT).free >= 2 * sum(map(len, self.files.values())) + 100 * 1024 * 1024,
                'disk_space_insufficient')
        code, body = self.http('/health/db')
        require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')

    def check_app_permissions(self, data, allowed_counts):
        require(isinstance(data, dict) and data.get('migrations_count') in allowed_counts
                and data == dict(database_name=DATABASE, migrations_count=data['migrations_count'], i04=1,
                                 read_admin=1, update_admin=1, read_customers=1, update_customers=1, write_audit=1,
                                 read_profiles=1, insert_profiles=1, update_profiles=1, delete_profiles=1,
                                 read_operations=1, write_operations=1, write_order_audit=1),
                'database_permissions_or_schema_unexpected')

    def sql_run(self, script, timeout=30):
        require(self.sql_tool in self.inspector.SQL_TOOLS and self.sql_login and self.sql_password,
                'sql_identity_or_tool_unexpected')
        args = [self.sql_tool, '-S', SERVER, '-d', DATABASE, '-C', '-I', '-b', '-l', '5',
                '-t', str(max(10, timeout - 5)), '-x', '-y', '0', '-w', '65535']
        try:
            result = subprocess.run(args, input=OPTIONS + script, capture_output=True, text=True,
                                    env=dict(CLEAN_ENV, SQLCMDUSER=self.sql_login, SQLCMDPASSWORD=self.sql_password),
                                    timeout=timeout, check=False)
        except (OSError, subprocess.TimeoutExpired, UnicodeError):
            raise InstallError('sql_operation_failed_or_timed_out') from None
        if result.returncode != 0:
            diagnostics = result.stdout + '\n' + result.stderr
            self.state['sql_error_numbers'] = sorted({int(n) for n in re.findall(
                r'(?m)^\s*Msg ([1-9][0-9]{0,5}), Level [0-9]{1,2}, State [0-9]{1,3},', diagnostics)})[:10]
            raise InstallError('sql_operation_failed')
        return result.stdout

    def sql_json(self, query):
        wrapped = 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        try:
            return self.inspector.parse_sql_output(self.sql_run(wrapped))
        except self.inspector.InspectionError:
            raise InstallError('sql_metadata_unrecognized') from None

    def authenticate_admin(self):
        self.sql_login = prompt_secret('Usuario administrador SQL existente (oculto): ').strip()
        self.sql_password = prompt_secret('Contraseña SQL (oculta; no se guarda): ')
        require(0 < len(self.sql_login) <= 128 and 0 < len(self.sql_password) <= 512
                and not any(c in self.sql_login + self.sql_password for c in '\0\r\n'), 'sql_credentials_invalid')
        data = self.sql_json(ADMIN_QUERY)
        require(data.get('database_name') == DATABASE and data.get('view_definition') == 1
                and (data.get('sysadmin') == 1 or (data.get('db_owner') == 1 and data.get('backup_permission') == 1
                and (data.get('verify_permission') == 1 or data.get('dbcreator') == 1))), 'sql_admin_permissions_insufficient')
        require(isinstance(data.get('files_mib'), int) and data['files_mib'] > 0, 'database_size_unrecognized')
        st = SQL_BACKUPS.lstat()
        require(stat.S_ISDIR(st.st_mode) and SQL_BACKUPS.resolve(strict=True) == SQL_BACKUPS
                and st.st_uid == 999 and st.st_gid == 988 and not st.st_mode & 0o027,
                'sql_backup_directory_identity_unexpected')
        require(shutil.disk_usage(SQL_BACKUPS).free >= (data['files_mib'] + 256) * 1024 * 1024,
                'sql_backup_disk_insufficient')
        self.state['migrations_before'] = self.verify_schema()

    def verify_schema(self):
        rows = self.sql_json(self.inspector.SQL_MIGRATIONS)
        require(rows in ([{'MigrationId': m} for m in MIGRATIONS[:10]], [{'MigrationId': m} for m in MIGRATIONS]),
                'migration_history_unexpected')
        count = len(rows)
        require(self.sql_json(SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1),
                'rest_days_schema_unexpected')
        expected = 0 if count == 10 else 1
        require(self.sql_json(ALIAS_SCHEMA_QUERY) == dict(columns_count=expected, valid_column=expected,
                indexes_count=expected, valid_index=expected), 'capture_alias_schema_unexpected')
        require(self.sql_json('SELECT COUNT_BIG(*) AS invalid FROM dbo.Customers WHERE RestDaysMask NOT BETWEEN 0 AND 127 '
                              'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == {'invalid': 0}, 'rest_days_data_invalid')
        if count == 11:
            require(self.sql_json("SELECT COUNT_BIG(*) AS invalid FROM dbo.CustomerFrequentProducts WHERE CaptureAlias IS NOT NULL "
                "AND (LEN(CaptureAlias) NOT BETWEEN 1 AND 16 OR CaptureAlias COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Z]%') "
                'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == {'invalid': 0}, 'capture_alias_data_invalid')
        return count

    def database_backup(self):
        folder = SQL_BACKUPS / ('prodimt-i07-' + self.snapshot.name.removeprefix('quick-capture-update-'))
        folder.mkdir(mode=0o700)
        os.chown(folder, 999, 988)
        folder.chmod(0o700)
        path = folder / 'ProdimtPedidos-copy-only.bak'
        self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + str(path) + "' WITH COPY_ONLY,CHECKSUM;", timeout=300)
        st = path.lstat()
        require(stat.S_ISREG(st.st_mode) and st.st_nlink == 1 and st.st_uid == 999 and st.st_gid == 988
                and path.resolve(strict=True) == path and st.st_size > 0, 'database_backup_file_unexpected')
        path.chmod(0o600)
        self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
        self.state['verified_database_backup'] = str(path)
        (self.snapshot / 'database-backup.json').write_text(json.dumps({'verified_backup': str(path)}))
        (self.snapshot / 'database-backup.json').chmod(0o600)

    def backup_and_migrate(self):
        before = self.verify_schema()
        require(before == self.state['migrations_before'], 'database_changed_during_staging')
        counts = self.sql_json(COUNTS_QUERY)
        require(isinstance(counts, dict) and set(counts) == {'customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits', 'profiles', 'products', 'machines'}
                and all(isinstance(value, int) and value >= 0 for value in counts.values()), 'database_counts_unrecognized')
        self.database_backup()
        require(bool(self.state.get('verified_database_backup')), 'verified_database_backup_required')
        if before == 10:
            self.state.update(migrations_may_have_changed=True, database_migrations_run=True)
            self.sql_run(self.files['migrations-i07.sql'].decode('utf-8-sig'), timeout=180)
        require(self.verify_schema() == 11, 'migrated_schema_unexpected')
        require(self.sql_json(COUNTS_QUERY) == counts, 'database_counts_changed')
        self.check_app_permissions(self.inspector.query_sql(self.sql_tool, self.fields, PERMISSION_QUERY), (11,))
        self.state.update(migrations_after=11, migrated_schema_verified=True, existing_data_counts_preserved=True)

    def expected(self, part):
        return {name[len(part) + 1:]: raw for name, raw in (self.files | self.overrides).items()
                if name.startswith(part + '/')}

    def verify_stage(self, part):
        root = self.new[part]
        directory_safe(root)
        expected = self.expected(part)
        expected_dirs = {'.'}
        for name in expected:
            expected_dirs.update(str(parent) for parent in PurePosixPath(name).parents)
        found = {str(p.relative_to(root)) for p in root.rglob('*') if not p.is_dir()}
        require(found == set(expected), 'staged_file_set_unexpected')
        require({'.'} | {str(p.relative_to(root)) for p in root.rglob('*') if p.is_dir()} == expected_dirs,
                'staged_directory_set_unexpected')
        for path in [root, *root.rglob('*')]:
            st = path.lstat()
            require(st.st_uid == 0 and not stat.S_ISLNK(st.st_mode) and path.resolve(strict=True) == path,
                    'staged_owner_or_link_unexpected')
            if path.is_dir():
                require(st.st_mode & 0o7777 == 0o755, 'staged_directory_mode_unexpected')
            else:
                name = str(path.relative_to(root))
                override = part + '/' + name in self.overrides
                require(st.st_nlink == 1 and st.st_gid == (33 if override else 0)
                        and st.st_mode & 0o7777 == (0o640 if override else 0o644)
                        and regular(path, 25 * 1024 * 1024) == expected[name], 'staged_file_changed')

    def stage(self):
        for part, target in self.new.items():
            if not target.exists():
                target.mkdir(mode=0o755)
                for name, raw in self.expected(part).items():
                    path = target / name
                    path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                    override = part + '/' + name in self.overrides
                    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o640 if override else 0o644)
                    with os.fdopen(fd, 'wb') as stream:
                        stream.write(raw)
                    os.chown(path, 0, 33 if override else 0)
                    path.chmod(0o640 if override else 0o644)
            self.verify_stage(part)

    def switch(self, target):
        for part, path in target.items():
            current = ROOT / part / 'current'
            require(current.is_symlink() and current.resolve(strict=True) in (self.old[part], self.new[part]),
                    'refuse_to_replace_unexpected_current')
            temporary = current.with_name('.capture-current-' + uuid.uuid4().hex)
            temporary.symlink_to(path)
            os.replace(temporary, current)

    def health(self, new):
        deadline = time.monotonic() + 45
        for attempt in range(15):
            try:
                self.process((self.new if new else self.old)['backend'])
                status, body = self.http('/health/db')
                require(status == 200 and json.loads(body).get('database') == 'reachable', 'health_not_ready')
                if new:
                    require(self.http('/api/admin/account/change-password', post=True)[0] == 401, 'password_endpoint_not_protected')
                    for endpoint in ('preview', 'capture'):
                        require(self.http('/api/admin/customers/00000000-0000-0000-0000-000000000000/orders/' + endpoint, post=True)[0] == 401,
                                'capture_endpoint_not_protected')
                    status, body = self.http('/admin/clientes-pendientes')
                    index = self.expected('frontend')['index.html']
                    require(status == 200 and body == index, 'frontend_index_mismatch')
                    require(self.http('/api/admin/customers/day-status')[0] == 401, 'day_status_endpoint_not_protected')
                    for asset in (name for name in self.expected('frontend') if name.endswith(('.js', '.css'))):
                        require(safe_name(asset), 'frontend_reference_unexpected')
                        status, body = self.http('/' + asset)
                        require(status == 200 and body == self.expected('frontend')[asset], 'frontend_asset_mismatch')
                return
            except (InstallError, OSError, ValueError):
                if attempt == 14 or time.monotonic() >= deadline:
                    raise InstallError('application_health_failed') from None
                time.sleep(1)

    def apply(self):
        self.state['phase'] = 'read-only-preflight'
        self.preflight()
        self.state['phase'] = 'sql-admin-authentication'
        self.authenticate_admin()
        self.state['phase'] = 'private-snapshot-and-stage'
        snapshot = BACKUPS / ('quick-capture-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
        snapshot.mkdir(mode=0o700)
        (snapshot / 'api.env').write_bytes(self.environment)
        (snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
        for path in snapshot.iterdir(): path.chmod(0o600)
        self.state.update(changes_made=True, private_snapshot=str(snapshot))
        self.snapshot = snapshot
        for name, raw in self.overrides.items():
            path = snapshot / name.split('/', 1)[1]
            path.write_bytes(raw)
            path.chmod(0o600)
        self.stage()
        self.links(self.old)
        self.process(self.old['backend'])
        require(regular(ENV_FILE) == self.environment and all(regular(self.old['backend'] / name.split('/', 1)[1]) == raw
                for name, raw in self.overrides.items()), 'configuration_changed_during_staging')
        self.state['phase'] = 'activate'
        self.stop_requested = True
        command(['systemctl', 'stop', SERVICE], timeout=45)
        self.state['phase'] = 'database-backup-and-migration'
        self.backup_and_migrate()
        self.state['phase'] = 'activate'
        self.switch(self.new)
        command(['systemctl', 'start', SERVICE], timeout=45)
        self.state['phase'] = 'verify-application'
        self.health(True)
        self.state.update(status='updated', phase='completed', health_verified=True,
                          source_commit=EXPECTED_SOURCE, new_releases={k: str(v) for k, v in self.new.items()})

    def rollback(self):
        if not self.stop_requested: return
        command(['systemctl', 'stop', SERVICE], timeout=45)
        self.switch(self.old)
        command(['systemctl', 'start', SERVICE], timeout=45)
        self.health(False)
        self.state['previous_health_verified'] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    installer = Installer(args.package)
    if not args.apply:
        print(json.dumps(installer.state, indent=2))
        return 0
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    fd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        st = os.fstat(fd)
        require(stat.S_ISREG(st.st_mode) and st.st_uid == 0 and st.st_nlink == 1
                and st.st_mode & 0o7777 == 0o600, 'lock_unexpected')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            installer.apply()
        except (Exception, KeyboardInterrupt) as error:
            installer.state.update(status='failed', issue=str(error) if isinstance(error, InstallError) else 'local_operation_failed')
            try:
                installer.rollback()
            except (Exception, KeyboardInterrupt):
                installer.state['rollback_failed'] = True
        print(json.dumps(installer.state, ensure_ascii=False, indent=2))
        return 0 if installer.state['status'] == 'updated' else 1
    finally:
        os.close(fd)


if __name__ == '__main__':
    os.umask(0o022)
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt):
        print(json.dumps({'status': 'failed', 'issue': 'installation_not_started'}))
        sys.exit(1)
