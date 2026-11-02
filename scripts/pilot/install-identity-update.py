#!/usr/bin/env python3
"""Install I18 security on exact I12 backend and I15/I17 frontend.

Verified SQL backup precedes additive migration13->14. Configuration and token
keys remain byte-identical; application rollback never restores SQL.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import signal
import stat
import sys
import types
import uuid
import zipfile

REVISION = '20261102-identity-administration-install2'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
PREVIOUS_BACKEND = 'access-tokens-65560ca3cf98'
PREVIOUS_FRONTENDS = ('customer-catalog-afb8f3ad9c33', 'pending-customers-cdbfec3071bc')
MIGRATION = '20261102235000_AddSecurityAdministration'
SECURITY_TABLES = ('SecurityRoles', 'SecurityPermissions', 'SecurityUserRoles',
                   'SecurityRolePermissions', 'SecurityUserPermissions', 'SecurityStates')
ADMIN_ROLE = '18180000-0000-0000-0000-000000000001'


# A DML-only application principal can inspect columns and default bindings,
# while SQL Server hides sys.default_constraints.definition. Do not expand its
# permissions to perform an installer check. Exact default values are still
# required by verify_schema(), using the SQL administrator before any writes.
APP_TOKEN_SCHEMA = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
  AND name IN (N'ProtectedToken',N'MetadataVersion',N'IsReminderLink')) AS columns_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND name=N'ProtectedToken'
  AND TYPE_NAME(system_type_id)=N'nvarchar' AND max_length=2048 AND is_nullable=1 AND default_object_id=0) AS protected_column,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  AND d.parent_object_id=c.object_id AND d.parent_column_id=c.column_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'MetadataVersion'
  AND TYPE_NAME(c.system_type_id)=N'int' AND c.max_length=4 AND c.is_nullable=0) AS version_column,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  AND d.parent_object_id=c.object_id AND d.parent_column_id=c.column_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'IsReminderLink'
  AND TYPE_NAME(c.system_type_id)=N'bit' AND c.max_length=1 AND c.is_nullable=0) AS reminder_column,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'MetadataVersion'
  AND d.definition IS NOT NULL) AS version_default_visible,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'MetadataVersion'
  AND REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N'')=N'1') AS version_default_valid,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'IsReminderLink'
  AND d.definition IS NOT NULL) AS reminder_default_visible,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'IsReminderLink'
  AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''),N'[',N''),N']',N''))
   IN (N'0',N'cast0asbit',N'convertbit,0')) AS reminder_default_valid,
 COALESCE(HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'VIEW DEFINITION'),0) AS can_view_definition
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_APP_TOKEN_STRUCTURE = dict(columns_count=3, protected_column=1, version_column=1, reminder_column=1)
APP_TOKEN_FLAGS = ('version_default_visible', 'version_default_valid', 'reminder_default_visible',
                   'reminder_default_valid', 'can_view_definition')


def app_token_schema_mismatches(data):
    keys = set(EXPECTED_APP_TOKEN_STRUCTURE) | set(APP_TOKEN_FLAGS)
    if not isinstance(data, dict) or set(data) != keys or any(type(value) is not int for value in data.values()):
        return ['metadata_format_unexpected']
    issues = [name for name, expected in EXPECTED_APP_TOKEN_STRUCTURE.items() if data[name] != expected]
    if any(data[name] not in (0, 1) for name in APP_TOKEN_FLAGS):
        return issues + ['metadata_flags_unexpected']
    for prefix in ('version', 'reminder'):
        visible, valid = data[prefix + '_default_visible'], data[prefix + '_default_valid']
        if valid > visible or (data['can_view_definition'] and not visible):
            issues.append(prefix + '_default_visibility_inconsistent')
        elif visible and not valid:
            issues.append(prefix + '_default_value_unexpected')
    return issues


def security_schema_query():
    names = ','.join("N'" + name + "'" for name in SECURITY_TABLES)
    return """SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (""" + names + """)) AS tables_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AdminUsers') AND name=N'IsDeleted'
  AND TYPE_NAME(system_type_id)=N'bit' AND is_nullable=0) AS deleted_column,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AdminUsers') AND name=N'SessionRevision'
  AND TYPE_NAME(system_type_id)=N'uniqueidentifier' AND is_nullable=0) AS session_column,
 (SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id IN
  (SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (""" + names + """)) AND type=N'PK') AS primary_keys,
 (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN
  (OBJECT_ID(N'dbo.SecurityUserRoles'),OBJECT_ID(N'dbo.SecurityRolePermissions'),OBJECT_ID(N'dbo.SecurityUserPermissions'))
  AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0) AS restricted_foreign_keys,
 (SELECT COUNT(*) FROM sys.indexes WHERE
  ((object_id=OBJECT_ID(N'dbo.SecurityRoles') AND name=N'IX_SecurityRoles_Name') OR
   (object_id=OBJECT_ID(N'dbo.SecurityPermissions') AND name=N'IX_SecurityPermissions_Key'))
  AND is_unique=1 AND is_disabled=0) AS unique_indexes,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.SecurityStates')
  AND name=N'CK_SecurityState_Singleton' AND is_disabled=0 AND is_not_trusted=0) AS singleton_check
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


SECURITY_SCHEMA = security_schema_query()
EXPECTED_SECURITY_SCHEMA = dict(tables_count=6, deleted_column=1, session_column=1, primary_keys=6,
                                restricted_foreign_keys=6, unique_indexes=2, singleton_check=1)
SECURITY_DATA = """SELECT
 (SELECT COUNT(*) FROM dbo.SecurityStates WHERE Id=1 AND Revision<>'00000000-0000-0000-0000-000000000000') AS states_count,
 (SELECT COUNT(*) FROM dbo.SecurityRoles WHERE Id='18180000-0000-0000-0000-000000000001' AND IsActive=1 AND IsSystem=1) AS administrators,
 (SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE IsSystem=1 AND IsActive=1) AS system_permissions,
 (SELECT COUNT(*) FROM dbo.SecurityRolePermissions rp JOIN dbo.SecurityPermissions p ON p.Id=rp.PermissionId
  WHERE rp.RoleId='18180000-0000-0000-0000-000000000001' AND p.IsSystem=1 AND p.IsActive=1) AS system_grants,
 (SELECT COUNT(*) FROM dbo.AdminUsers u WHERE u.IsActive=1 AND u.IsDeleted=0 AND EXISTS
  (SELECT 1 FROM dbo.SecurityPermissions p WHERE p.[Key]=N'security.manage' AND p.IsActive=1 AND
   (EXISTS(SELECT 1 FROM dbo.SecurityUserPermissions up WHERE up.UserId=u.Id AND up.PermissionId=p.Id)
    OR EXISTS(SELECT 1 FROM dbo.SecurityUserRoles ur JOIN dbo.SecurityRoles r ON r.Id=ur.RoleId
     JOIN dbo.SecurityRolePermissions rp ON rp.RoleId=r.Id WHERE ur.UserId=u.Id AND r.IsActive=1 AND rp.PermissionId=p.Id)))) AS active_administrators,
 (SELECT COUNT(*) FROM dbo.AdminUsers WHERE IsDeleted=1 AND IsActive=1) AS active_deleted_users
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
SECURITY_PERMISSIONS = 'SELECT ' + ','.join(
    "HAS_PERMS_BY_NAME(N'dbo." + table + "',N'OBJECT',N'" + operation + "') AS [" + table + '_' + operation + ']'
    for table in SECURITY_TABLES for operation in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')
) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
EXPECTED_SECURITY_PERMISSIONS = {table + '_' + operation: 1
    for table in SECURITY_TABLES for operation in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')}
SECURITY_GRANTS = '\n'.join('GRANT SELECT,INSERT,UPDATE,DELETE ON dbo.' + table + ' TO [prodimt_pedidos_app];'
                            for table in SECURITY_TABLES)
ADMIN_ACCESS_PRESERVED = """SELECT COUNT_BIG(*) AS missing FROM dbo.AdminUsers u
 WHERE NOT EXISTS(SELECT 1 FROM dbo.SecurityUserRoles ur
 WHERE ur.UserId=u.Id AND ur.RoleId='18180000-0000-0000-0000-000000000001')
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""

SECURITY_ROLLBACK_SAFE = """SELECT CASE WHEN
 (SELECT COUNT(*) FROM dbo.SecurityStates WHERE Id=1 AND Revision='18180000-0000-0000-0000-000000000002')=1
 AND (SELECT COUNT(*) FROM dbo.SecurityRoles)=1
 AND (SELECT COUNT(*) FROM dbo.SecurityRoles WHERE Id='18180000-0000-0000-0000-000000000001' AND IsSystem=1 AND IsActive=1)=1
 AND (SELECT COUNT(*) FROM dbo.SecurityPermissions)=15
 AND (SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE IsSystem=1 AND IsActive=1)=15
 AND (SELECT COUNT(*) FROM dbo.SecurityRolePermissions WHERE RoleId='18180000-0000-0000-0000-000000000001')=15
 AND (SELECT COUNT(*) FROM dbo.SecurityRolePermissions)=15
 AND (SELECT COUNT(*) FROM dbo.SecurityUserPermissions)=0
 AND (SELECT COUNT(*) FROM dbo.SecurityUserRoles)=(SELECT COUNT(*) FROM dbo.AdminUsers)
 AND NOT EXISTS(SELECT 1 FROM dbo.AdminUsers u WHERE u.IsDeleted=1 OR NOT EXISTS
  (SELECT 1 FROM dbo.SecurityUserRoles ur WHERE ur.UserId=u.Id AND ur.RoleId='18180000-0000-0000-0000-000000000001'))
 THEN 1 ELSE 0 END AS safe FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


class InstallError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def error_issue(error, inspector=None):
    return str(error) if isinstance(error, (InstallError, getattr(inspector, 'InspectionError', InstallError))) else 'local_operation_failed'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path, limit=40 * 1024 * 1024):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid in (0, 33) and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'file_identity_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'file_too_large')
    return raw


def safe_name(name):
    pure = PurePosixPath(name)
    return bool(pure.parts) and pure.as_posix() == name and not pure.is_absolute() and name not in ('', '.') \
        and not any(part in ('', '.', '..') for part in pure.parts) and ':' not in name and '\\' not in name


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 250 and len({row.filename for row in rows}) == len(rows)
                and sum(row.file_size for row in rows) <= 100 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and not stat.S_ISLNK(row.external_attr >> 16) for row in rows), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in rows}
    manifest = json.loads(files.pop('manifest.json'))
    required = {'inspect-native.py', 'native-deployment-support.py', 'commercial-catalog-support.py', 'token-management-support.py',
                'migrations-i18.sql', 'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'identity-administration-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 13 and manifest.get('migrationsAfter') == 14
            and manifest.get('previousBackend') == PREVIOUS_BACKEND
            and manifest.get('previousFrontends') == list(PREVIOUS_FRONTENDS)
            and manifest.get('encryptionKeyInPackage') is False and manifest.get('revokeTokens') is False
            and set(manifest.get('files', {})) == set(files) and required <= set(files)
            and all(name in required or name.startswith(('backend/', 'frontend/')) for name in files), 'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12', '.trace', '.dmp', '.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    sql = files['migrations-i18.sql'].decode('utf-8-sig')
    validate_migration_sql(sql)
    return files


def validate_migration_sql(sql):
    require(isinstance(sql, str) and MIGRATION in sql and all(name in sql for name in SECURITY_TABLES)
            and 'SessionRevision' in sql and 'IsDeleted' in sql
            and not re.search(r'(?im)^\s*(DELETE|UPDATE|TRUNCATE|DROP|USE|GRANT|DENY)\s', sql)
            and sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1,
            'incremental_sql_unexpected')
    # Seed statements reference newly created tables; DDL and seed batches use
    # the SAME sqlcmd connection/transaction.
    boundary = sql.find('GO\n\n')
    first_seed = sql.find("IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision')")
    require(boundary > 0 and boundary < first_seed and sql.count('CREATE TABLE [Security') == 6,
            'migration_batch_boundary_missing')
    inserts = {target.replace('[', '').replace(']', '').removeprefix('dbo.') for target in
               re.findall(r'(?im)^\s*INSERT\s+(?:INTO\s+)?([^\s(]+)', sql)}
    permitted = {'__EFMigrationsHistory', 'SecurityRoles', 'SecurityPermissions',
                 'SecurityRolePermissions', 'SecurityUserRoles', 'SecurityStates'}
    require(inserts <= permitted and inserts >= {'__EFMigrationsHistory', 'SecurityUserRoles'},
            'migration_seed_targets_unexpected')


def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


def make_installer(package):
    files = load_payload(package)
    runtime = module('reviewed_i18_native_support', files['native-deployment-support.py'])
    commercial = module('reviewed_i18_commercial_support', files['commercial-catalog-support.py'])
    token = module('reviewed_i18_token_support', files['token-management-support.py'])
    runtime.InstallError = InstallError
    token.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    migrations = runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION, MIGRATION]

    class IdentityInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('identity-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, encryption_key_created=False, encryption_key_rotated=False,
                tokens_revoked=False, token_schema_defaults_pending_admin=False, token_schema_defaults_verified=False)

        def check_app_token_schema(self, data):
            keys = set(EXPECTED_APP_TOKEN_STRUCTURE) | set(APP_TOKEN_FLAGS)
            self.state['token_schema_checks'] = {key: value for key, value in data.items()
                if key in keys and type(value) is int} if isinstance(data, dict) else {}
            mismatches = app_token_schema_mismatches(data)
            self.state['token_schema_mismatches'] = mismatches
            require(not mismatches, 'token_management_schema_unexpected')
            pending = not all(data[prefix + '_default_visible'] for prefix in ('version', 'reminder'))
            self.state.update(token_schema_defaults_pending_admin=pending, token_schema_defaults_verified=not pending)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.identity-stage-' + uuid.uuid4().hex)
                temporary.mkdir(mode=0o700)
                try:
                    for name, raw in self.expected(part).items():
                        path = temporary / name
                        path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                        override = part + '/' + name in self.overrides
                        mode = 0o640 if override else 0o644
                        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
                        with os.fdopen(fd, 'wb') as stream:
                            stream.write(raw)
                        os.chown(path, 0, 33 if override else 0)
                        path.chmod(mode)
                    temporary.chmod(0o755)
                    temporary.rename(target)
                finally:
                    if temporary.exists():
                        runtime.shutil.rmtree(temporary)
                self.verify_stage(part)

        def verify_security(self):
            data = self.sql_json(SECURITY_DATA)
            require(isinstance(data, dict) and data.get('states_count') == 1
                    and data.get('administrators') == 1 and data.get('system_permissions') == 15
                    and data.get('system_grants') == 15 and data.get('active_administrators', 0) >= 1
                    and data.get('active_deleted_users') == 0, 'security_seed_or_recovery_access_unexpected')

        def preflight(self):
            self.state['preflight_step'] = 'directories-and-releases'
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_release_link_required')
                self.old[part] = link.resolve(strict=True)
            own = self.old == self.new
            supported = self.old['backend'] == ROOT / 'backend/releases' / PREVIOUS_BACKEND and \
                self.old['frontend'] in {ROOT / 'frontend/releases' / name for name in PREVIOUS_FRONTENDS}
            require(own or supported, 'previous_release_not_supported')
            require(all(path.parent == ROOT / part / 'releases' for part, path in self.old.items()), 'previous_release_path_unexpected')
            self.state['preflight_step'] = 'running-service'
            pid, cwd, argv = self.process(self.old['backend'])
            self.original_dll = regular(cwd / 'Prodimt.Pedidos.Api.dll')
            token.validate_environment_file(ENV_FILE)
            self.environment = regular(ENV_FILE)
            self.token_encryption_key = token.environment_key(self.environment)
            require(self.token_encryption_key is not None, 'existing_token_encryption_key_required')
            inspector = module('reviewed_i18_inventory', files['inspect-native.py'])
            inspector.QUERIES = (commercial.PERMISSION_QUERY, commercial.NEW_SCHEMA, commercial.PRICE_PERMISSIONS,
                                 APP_TOKEN_SCHEMA, token.TOKEN_SCHEMA, token.TOKEN_COUNTS, SECURITY_SCHEMA, SECURITY_DATA, SECURITY_PERMISSIONS)
            self.inspector = inspector
            self.state['preflight_step'] = 'effective-configuration'
            configuration, source = inspector.configuration(inspector.process_environment(pid), argv, cwd)
            require(source['environment'] == 'Production' and inspector.policy_flags(configuration) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False, 'https_redirection_enabled': True},
                'production_configuration_unexpected')
            require(inspector.first(configuration, token.KEY_CONFIG, default=None) == self.token_encryption_key,
                    'token_encryption_key_configuration_conflict')
            self.fields = inspector.connection_fields(inspector.first(configuration, 'ConnectionStrings:Pedidos',
                                                                       'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(self.fields), 'sql_target_unexpected')
            self.sql_tool = next((tool for tool in inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool, 'sqlcmd_required')
            self.state['preflight_step'] = 'application-sql-read'
            self.app_checks = inspector.query_sql(self.sql_tool, self.fields, commercial.PERMISSION_QUERY)
            mismatch = commercial.permission_mismatches(self.app_checks, (14,) if own else (13, 14))
            self.state['database_checks'] = {key: value for key, value in self.app_checks.items()
                                            if key not in ('database_name', 'application_user')}
            self.state['database_mismatches'] = mismatch
            require(not mismatch, 'database_permissions_schema_or_channels_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA,
                    'commercial_schema_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS,
                    'price_history_permissions_unexpected')
            self.check_app_token_schema(inspector.query_sql(self.sql_tool, self.fields, APP_TOKEN_SCHEMA))
            count = self.app_checks['migrations_count']
            expected = EXPECTED_SECURITY_SCHEMA if count == 14 else dict.fromkeys(EXPECTED_SECURITY_SCHEMA, 0)
            require(inspector.query_sql(self.sql_tool, self.fields, SECURITY_SCHEMA) == expected, 'security_schema_unexpected')
            if count == 14:
                require(inspector.query_sql(self.sql_tool, self.fields, SECURITY_PERMISSIONS) == EXPECTED_SECURITY_PERMISSIONS,
                        'security_application_permissions_unexpected')
            self.overrides = {}
            for name in ('appsettings.json', 'appsettings.Production.json'):
                path = self.old['backend'] / name
                if path.exists():
                    self.overrides['backend/' + name] = regular(path)
            self.state['preflight_step'] = 'previous-application-health'
            require(runtime.shutil.disk_usage(ROOT).free >= 2 * sum(map(len, files.values())) + 100 * 1024 * 1024,
                    'disk_space_insufficient')
            code, body = self.http('/health/db')
            require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
            require(self.http('/admin/catalogos') == (200, regular(self.old['frontend'] / 'index.html')), 'previous_frontend_not_served')
            require(self.http('/api/admin/customers')[0] == 401, 'admin_authorization_unexpected')
            self.state.update(env_preserved=True, encryption_key_present=True, health_verified=True, base_release_verified=True)
            if own:
                self.state['preflight_step'] = 'installed-files-and-effective-key'
                self.verify_stage('backend')
                self.verify_stage('frontend')
                self.health(True)
                self.state.update(status='already_current', phase='completed', health_verified=True, frontend_verified=True)
            self.state['preflight_step'] = 'completed'

        def verify_schema(self):
            rows = self.sql_json(self.inspector.SQL_MIGRATIONS)
            require(rows in ([{'MigrationId': value} for value in migrations[:-1]],
                             [{'MigrationId': value} for value in migrations]), 'migration_history_unexpected')
            count = len(rows)
            require(self.sql_json(runtime.SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1),
                    'rest_days_schema_unexpected')
            require(self.sql_json(runtime.ALIAS_SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1),
                    'capture_alias_schema_unexpected')
            require(self.sql_json(commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            require(self.sql_json(token.TOKEN_SCHEMA) == token.EXPECTED_TOKEN_SCHEMA, 'token_management_schema_unexpected')
            self.state.update(token_schema_defaults_pending_admin=False, token_schema_defaults_verified=True)
            expected = EXPECTED_SECURITY_SCHEMA if count == 14 else dict.fromkeys(EXPECTED_SECURITY_SCHEMA, 0)
            require(self.sql_json(SECURITY_SCHEMA) == expected, 'security_schema_unexpected')
            if count == 14:
                self.verify_security()
            return count

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-i18-' + self.snapshot.name.removeprefix('identity-update-'))
            folder.mkdir(mode=0o700)
            os.chown(folder, 999, 988)
            folder.chmod(0o700)
            path = folder / 'ProdimtPedidos-copy-only.bak'
            self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + str(path) + "' WITH COPY_ONLY,CHECKSUM;", timeout=300)
            meta = path.lstat()
            require(stat.S_ISREG(meta.st_mode) and meta.st_nlink == 1 and meta.st_uid == 999 and meta.st_gid == 988
                    and path.resolve(strict=True) == path and meta.st_size > 0, 'database_backup_file_unexpected')
            path.chmod(0o600)
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            self.state['verified_database_backup'] = str(path)
            record = self.snapshot / 'database-backup.json'
            record.write_text(json.dumps(dict(verified_backup=str(path))))
            record.chmod(0o600)

        def backup_and_migrate(self):
            before = self.verify_schema()
            require(before == self.state['migrations_before'], 'database_changed_during_staging')
            counts = self.sql_json(runtime.COUNTS_QUERY)
            require(token.valid_counts(counts, ('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits',
                                         'profiles', 'products', 'machines')), 'database_counts_unrecognized')
            monetary = self.sql_json(commercial.PRICE_COUNTS)
            tokens = self.sql_json(token.TOKEN_COUNTS)
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            require(regular(ENV_FILE) == self.environment and token.environment_key(self.environment) == self.token_encryption_key,
                    'configuration_changed_before_migration')
            if before == 13:
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(files['migrations-i18.sql'].decode('utf-8-sig'), timeout=180)
                require(self.sql_json(ADMIN_ACCESS_PRESERVED) == {'missing': 0}, 'existing_administrator_access_not_preserved')
            self.sql_run(SECURITY_GRANTS)
            require(self.verify_schema() == 14, 'migrated_schema_unexpected')
            require(self.sql_json(runtime.COUNTS_QUERY) == counts and self.sql_json(commercial.PRICE_COUNTS) == monetary
                    and self.sql_json(token.TOKEN_COUNTS) == tokens, 'existing_data_counts_changed')
            require(not commercial.permission_mismatches(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PERMISSION_QUERY), (14,)),
                    'application_permissions_changed')
            require(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS,
                    'price_history_permissions_unexpected')
            require(self.inspector.query_sql(self.sql_tool, self.fields, SECURITY_PERMISSIONS) == EXPECTED_SECURITY_PERMISSIONS,
                    'security_application_permissions_unexpected')
            self.state.update(migrations_after=14, migrated_schema_verified=True, existing_data_counts_preserved=True,
                              price_history_append_only_verified=True, tokens_preserved=True, security_permissions_verified=True)

        def health(self, new):
            super().health(new)
            if new:
                require(self.http('/api/admin/security')[0] == 401 and self.http('/api/admin/security/me')[0] == 401,
                        'security_endpoint_not_protected')
                pid, cwd, argv = self.process(self.new['backend'])
                cfg, source = self.inspector.configuration(self.inspector.process_environment(pid), argv, cwd)
                require(source.get('environment') == 'Production'
                        and self.inspector.first(cfg, token.KEY_CONFIG, default=None) == self.token_encryption_key
                        and regular(ENV_FILE) == self.environment, 'configuration_or_token_key_changed')
                self.state.update(frontend_verified=True, encryption_key_effective_verified=True)

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                if self.state['token_schema_defaults_pending_admin']:
                    self.state['phase'] = 'sql-admin-schema-verification'
                    self.authenticate_admin()
                    self.state['phase'] = 'completed'
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.state['phase'] = 'private-snapshot-and-stage'
            self.snapshot = BACKUPS / ('identity-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
            self.snapshot.mkdir(mode=0o700)
            (self.snapshot / 'api.env').write_bytes(self.environment)
            (self.snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
            for path in self.snapshot.iterdir():
                path.chmod(0o600)
            for name, raw in self.overrides.items():
                path = self.snapshot / name.split('/', 1)[1]
                path.write_bytes(raw)
                path.chmod(0o600)
            self.state.update(changes_made=True, private_snapshot=str(self.snapshot))
            self.stage()
            self.links(self.old)
            self.process(self.old['backend'])
            require(regular(ENV_FILE) == self.environment and regular(self.old['backend'] / 'Prodimt.Pedidos.Api.dll') == self.original_dll
                    and all(regular(self.old['backend'] / name.split('/', 1)[1]) == raw for name, raw in self.overrides.items()),
                    'configuration_or_backend_changed_during_staging')
            self.state['phase'] = 'stop-prodimt-api'
            self.stop_requested = True
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            self.state['phase'] = 'database-backup-and-migration'
            self.backup_and_migrate()
            self.state['phase'] = 'activate-releases'
            self.switch(self.new)
            runtime.command(['systemctl', 'start', SERVICE], timeout=45)
            self.state['service_restarted'] = True
            self.state['phase'] = 'verify-application'
            self.health(True)
            self.state.update(status='updated', phase='completed', health_verified=True, source_commit=EXPECTED_SOURCE,
                              new_releases={part: str(path) for part, path in self.new.items()})

        def rollback(self):
            if not self.stop_requested:
                return
            # Stop I18 BEFORE inspecting grants. Otherwise an API mutation could
            # race between the safe-revision read and the old binary starting.
            try:
                runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
                self.state['rollback_service_stopped'] = True
            except Exception:
                self.state.update(rollback_blocked_service_stop=True, recovery_manual_required=True)
                return
            # I12 authorizes every active internal account as full administrator.
            # SQL/read uncertainty fails closed: never infer safe recovery.
            try:
                history = self.sql_json(self.inspector.SQL_MIGRATIONS)
                if history == [{'MigrationId': value} for value in migrations]:
                    safe = self.sql_json(SECURITY_ROLLBACK_SAFE) == {'safe': 1}
                else:
                    safe = history == [{'MigrationId': value} for value in migrations[:-1]]
            except Exception:
                safe = False
            if not safe:
                self.state.update(rollback_blocked_security_changes=True, recovery_manual_required=True,
                                  database_automatically_restored=False, application_stopped_for_security=True)
                return
            super().rollback()
            self.state.update(service_restarted=True, immediate_security_rollback_verified=True)

    return IdentityInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-identity-payload.zip'))
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    installer = make_installer(args.package)
    installer.state['mode'] = 'apply' if args.apply else 'read-only'
    fd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    previous = {}
    try:
        meta = os.fstat(fd)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1 and meta.st_mode & 0o7777 == 0o600,
                'lock_identity_unexpected')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        def interrupted(number, frame):
            raise InstallError('installation_interrupted')
        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            previous[number] = signal.signal(number, interrupted)
        try:
            if args.apply:
                installer.apply()
            else:
                installer.preflight()
                if installer.state['status'] != 'already_current':
                    installer.state['phase'] = 'completed'
        except (Exception, KeyboardInterrupt) as error:
            installer.state.update(status='failed', issue=error_issue(error, getattr(installer, 'inspector', None)))
            for number in previous:
                signal.signal(number, signal.SIG_IGN)
            try:
                installer.rollback()
            except (Exception, KeyboardInterrupt):
                installer.state['rollback_failed'] = True
        print(json.dumps(installer.state, ensure_ascii=False, indent=2))
        return 0 if installer.state['status'] in ('planned', 'updated', 'already_current') else 1
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)
        os.close(fd)


if __name__ == '__main__':
    os.umask(0o022)
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt):
        print(json.dumps(dict(revision=REVISION, status='failed', issue='installation_not_started', secret_values_printed=False)))
        sys.exit(1)
