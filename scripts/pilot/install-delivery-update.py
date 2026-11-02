#!/usr/bin/env python3
"""Install I22 delivery over the exact integrated I19/I21 native releases.

Read-only unless --apply is supplied. Verified SQL backup precedes all DDL;
exact migration identities, environment and token keys remain guarded.
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

REVISION = '20261102-delivery-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
BASE_BACKEND = 'production-d3a11420b202'
PREVIOUS_PAIRS = ((BASE_BACKEND, BASE_BACKEND), (BASE_BACKEND, 'orders-catalog-867df56de3bb'))
SETUP_MIGRATION = '20261102152307_AddCustomerProductSetup'
IDENTITY_MIGRATION = '20261102235000_AddSecurityAdministration'
PRODUCTION_MIGRATION = '20261103010000_AddProductionAllocation'
MIGRATION = '20261103020000_AddDeliveryOperations'
DELIVERY_ROLE = '22220000-0000-0000-0000-000000000001'
DELIVERY_TABLES = ('CustomerDeliveryLocations','DeliveryAssignments','DeliveryEvents','DeliveryRoutes','DeliveryTrips')
DELIVERY_PERMISSIONS = ('delivery.read.own','delivery.operate.own','delivery.manage')
SUPPORT_FILES = ('inspect-native.py','native-deployment-support.py','commercial-catalog-support.py',
                 'token-management-support.py','customer-products-support.py','security-administration-support.py',
                 'production-support.py','delivery-schema-contract.py')
SQL_FILES = ('migrations-i22.sql','delivery-schema.json')


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


def private(path):
    raw = regular(path)
    meta = path.lstat()
    require(meta.st_uid == 0 and meta.st_mode & 0o7777 == 0o600, 'private_file_identity_unexpected')
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
    required = set(SUPPORT_FILES + SQL_FILES) | {'backend/Prodimt.Pedidos.Api.dll','backend/appsettings.json','frontend/index.html'}
    require(manifest.get('feature') == 'delivery-operations-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 16
            and manifest.get('migrationsAfter') == 17 and manifest.get('previousPairs') == [list(pair) for pair in PREVIOUS_PAIRS]
            and manifest.get('revokeTokens') is False and manifest.get('encryptionKeyInPackage') is False
            and set(manifest.get('files', {})) == set(files) and required <= set(files)
            and all(name in required or name.startswith(('backend/','frontend/')) for name in files), 'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]),sha256=sha(files[name])) for name,meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all(not name.endswith(('.map','.pdb','.env','.key','.pem','.pfx','.p12','.trace','.dmp','.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    sql = files['migrations-i22.sql'].decode('utf-8-sig')
    validate_migration_sql(sql)
    schema = module('i22_delivery_schema_validator',files['delivery-schema-contract.py'])
    require(json.loads(files['delivery-schema.json']) == schema.contract_from_sql(sql), 'delivery_schema_contract_mismatch')
    return files


def validate_migration_sql(sql):
    require(isinstance(sql,str) and sql.startswith('BEGIN TRANSACTION;\n') and sql.endswith('COMMIT;\nGO\n\n')
            and sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1, 'delivery_transaction_unexpected')
    revision = 'UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;'
    require(sql.count(revision) == 1, 'security_revision_update_unexpected')
    stripped = sql.replace(revision,'').replace('ON DELETE NO ACTION','')
    require(not re.search(r'\b(?:DELETE|UPDATE|TRUNCATE|DROP|USE|GRANT|DENY|ALTER)\b',stripped,re.I), 'delivery_migration_mutation_unexpected')
    require(re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.",sql) == [MIGRATION]
            and set(re.findall(r'(?im)^\s*CREATE TABLE \[([^]]+)\]',sql)) == set(DELIVERY_TABLES)
            and all(key in sql for key in DELIVERY_PERMISSIONS) and DELIVERY_ROLE in sql, 'delivery_migration_unexpected')
    require(set(re.findall(r'\bINSERT INTO \[([^]]+)\]',sql,re.I)) ==
            {'SecurityPermissions','SecurityRoles','SecurityRolePermissions','__EFMigrationsHistory'}, 'delivery_seed_targets_unexpected')

def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


DELIVERY_GRANTS = ''.join('GRANT SELECT,INSERT' + (',UPDATE' if table != 'DeliveryEvents' else '') +
    ' ON dbo.' + table + ' TO [prodimt_pedidos_app];\n' +
    ('DENY UPDATE,DELETE' if table == 'DeliveryEvents' else 'DENY DELETE') +
    ' ON dbo.' + table + ' TO [prodimt_pedidos_app];\n' for table in DELIVERY_TABLES)
DELIVERY_SQL_PERMISSIONS = 'SELECT '+','.join(
    "HAS_PERMS_BY_NAME(N'dbo."+table+"',N'OBJECT',N'"+operation+"') AS ["+table+'_'+operation+']'
    for table in DELIVERY_TABLES for operation in ('SELECT','INSERT','UPDATE','DELETE'))+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
EXPECTED_DELIVERY_PERMISSIONS = {table+'_'+operation:int(operation in ('SELECT','INSERT') or
    (table != 'DeliveryEvents' and operation == 'UPDATE')) for table in DELIVERY_TABLES for operation in ('SELECT','INSERT','UPDATE','DELETE')}
DELIVERY_COUNTS = 'SELECT '+','.join('(SELECT COUNT(*) FROM dbo.'+table+') AS ['+table+']'
    for table in DELIVERY_TABLES)+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
DELIVERY_ROLE_DATA = """SELECT
 (SELECT COUNT(*) FROM dbo.SecurityRoles WHERE Id='22220000-0000-0000-0000-000000000001' AND IsActive=1 AND IsSystem=1) AS driver_roles,
 (SELECT COUNT(*) FROM dbo.SecurityRolePermissions rp JOIN dbo.SecurityPermissions p ON p.Id=rp.PermissionId
  WHERE rp.RoleId='22220000-0000-0000-0000-000000000001' AND p.[Key] IN (N'delivery.read.own',N'delivery.operate.own')) AS driver_grants,
 (SELECT COUNT(*) FROM dbo.SecurityRolePermissions rp WHERE rp.RoleId='22220000-0000-0000-0000-000000000001') AS driver_total_grants
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""

def security_fingerprint_query():
    excluded=','.join("N'"+key+"'" for key in DELIVERY_PERMISSIONS)
    queries={'users':'SELECT * FROM dbo.AdminUsers ORDER BY Id','roles':"SELECT * FROM dbo.SecurityRoles WHERE Id<>'"+DELIVERY_ROLE+"' ORDER BY Id",
        'user_roles':'SELECT * FROM dbo.SecurityUserRoles ORDER BY UserId,RoleId',
        'user_permissions':'SELECT * FROM dbo.SecurityUserPermissions ORDER BY UserId,PermissionId',
        'state':'SELECT Id FROM dbo.SecurityStates ORDER BY Id',
        'permissions':'SELECT * FROM dbo.SecurityPermissions WHERE [Key] NOT IN ('+excluded+') ORDER BY Id',
        'role_permissions':'SELECT rp.* FROM dbo.SecurityRolePermissions rp JOIN dbo.SecurityPermissions p ON p.Id=rp.PermissionId WHERE p.[Key] NOT IN ('+excluded+') ORDER BY rp.RoleId,rp.PermissionId'}
    return 'SELECT '+','.join("CONVERT(varchar(64),HASHBYTES('SHA2_256',("+query+" FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS ["+key+']'
        for key,query in queries.items())+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


SECURITY_FINGERPRINT = security_fingerprint_query()


def migration_sql_for_sqlcmd(files):
    sql = files['migrations-i22.sql'].decode('utf-8-sig')
    validate_migration_sql(sql)
    # The GO keeps the same connection/transaction and avoids SQL207 on new tables.
    return sql.removesuffix('COMMIT;\nGO\n\n') + 'GO\n\n' + DELIVERY_GRANTS + '\nCOMMIT;\nGO\n\n'


def make_installer(package):
    files = load_payload(package)
    runtime = module('reviewed_i18_native_support', files['native-deployment-support.py'])
    commercial = module('reviewed_i18_commercial_support', files['commercial-catalog-support.py'])
    token = module('reviewed_i18_token_support', files['token-management-support.py'])
    setup = module('i19_setup_support', files['customer-products-support.py'])
    security = module('i19_security_support', files['security-administration-support.py'])
    setup.InstallError = security.InstallError = InstallError
    runtime.InstallError = InstallError
    token.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    production = module('i22_baseline_production_support', files['production-support.py'])
    production.InstallError = InstallError
    schema = module('i22_delivery_schema_support', files['delivery-schema-contract.py'])
    contract = json.loads(files['delivery-schema.json'])
    base = runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION, SETUP_MIGRATION, IDENTITY_MIGRATION, PRODUCTION_MIGRATION]
    migrations = base + [MIGRATION]
    known = (base, migrations)
    SECURITY_SCHEMA, EXPECTED_SECURITY_SCHEMA = security.SECURITY_SCHEMA, security.EXPECTED_SECURITY_SCHEMA
    SECURITY_DATA, SECURITY_PERMISSIONS = security.SECURITY_DATA, security.SECURITY_PERMISSIONS
    EXPECTED_SECURITY_PERMISSIONS = security.EXPECTED_SECURITY_PERMISSIONS

    class DeliveryInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('delivery-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.recovery = None
            self.marker = BACKUPS / 'delivery-pending.json'
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, encryption_key_created=False, encryption_key_rotated=False,
                tokens_revoked=False)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.delivery-stage-' + uuid.uuid4().hex)
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
                    and data.get('administrators') == 1 and data.get('system_permissions') == (21 if MIGRATION in self.history(self.sql_json) else 18)
                    and data.get('system_grants') == (21 if MIGRATION in self.history(self.sql_json) else 18) and data.get('active_administrators', 0) >= 1
                    and data.get('active_deleted_users') == 0, 'security_seed_or_recovery_access_unexpected')

        def history(self, reader):
            rows = reader(self.inspector.SQL_MIGRATIONS)
            require(isinstance(rows, list) and all(isinstance(row, dict) and set(row) == {'MigrationId'} for row in rows), 'migration_history_unexpected')
            ids = [row['MigrationId'] for row in rows]
            require(ids in known, 'migration_history_unexpected')
            return ids

        def read_marker(self):
            if not self.marker.exists() and not self.marker.is_symlink():
                return None
            value = json.loads(private(self.marker))
            require(value.get('source') == EXPECTED_SOURCE and value.get('payload') == EXPECTED_PAYLOAD_SHA256, 'different_update_recovery_pending')
            snapshot = Path(value.get('snapshot', ''))
            require(snapshot.parent == BACKUPS and re.fullmatch(r'delivery-update-[0-9]{8}T[0-9]{6}Z-[a-f0-9]{12}', snapshot.name), 'recovery_snapshot_invalid')
            runtime.directory_safe(snapshot)
            require(snapshot.stat().st_mode & 0o7777 == 0o700, 'recovery_snapshot_mode_invalid')
            require(tuple(value.get('previous', [])) in PREVIOUS_PAIRS and value.get('historyBefore') in known[:-1], 'recovery_base_invalid')
            require(private(snapshot / 'api.env') == regular(ENV_FILE), 'recovery_environment_changed')
            return value

        def write_marker(self):
            value = dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256, snapshot=str(self.snapshot),
                         previous=[self.old[part].name for part in ('backend', 'frontend')], historyBefore=self.history_before)
            temporary = self.marker.with_name('.delivery-pending-' + uuid.uuid4().hex)
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'w') as stream:
                json.dump(value, stream)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.marker)

        def schemas(self, reader, strict=False):
            ids = self.history(reader)
            require(reader(commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            require(reader(setup.TOKEN_SCHEMA if strict else setup.TOKEN_SHAPE) == setup.EXPECTED_TOKEN_SCHEMA, 'token_management_schema_unexpected')
            require(reader(setup.SETUP_SCHEMA if strict else setup.SETUP_SHAPE) == (setup.EXPECTED_SETUP_SCHEMA if SETUP_MIGRATION in ids else dict.fromkeys(setup.EXPECTED_SETUP_SCHEMA, 0)), 'product_setup_schema_unexpected')
            require(reader(SECURITY_SCHEMA) == (EXPECTED_SECURITY_SCHEMA if IDENTITY_MIGRATION in ids else dict.fromkeys(EXPECTED_SECURITY_SCHEMA, 0)), 'security_schema_unexpected')
            require(reader(production.PRODUCTION_SCHEMA if strict else production.PRODUCTION_SHAPE) == production.EXPECTED_PRODUCTION_SCHEMA, 'production_schema_unexpected')
            expected = contract if MIGRATION in ids else {key: [] for key in contract}
            require(schema.matches(reader(schema.schema_query(strict)), expected, strict), 'delivery_schema_unexpected')
            return ids

        def app_permissions(self, ids):
            read = lambda query: self.inspector.query_sql(self.sql_tool, self.fields, query)
            require(not commercial.permission_mismatches(read(commercial.PERMISSION_QUERY), (len(ids),)), 'application_permissions_unexpected')
            require(read(commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS, 'price_history_permissions_unexpected')
            if SETUP_MIGRATION in ids:
                require(read(setup.SETUP_PERMISSIONS) == setup.EXPECTED_SETUP_PERMISSIONS, 'setup_permissions_unexpected')
            if IDENTITY_MIGRATION in ids:
                require(read(SECURITY_PERMISSIONS) == EXPECTED_SECURITY_PERMISSIONS, 'security_permissions_unexpected')
            require(read(production.PRODUCTION_SQL_PERMISSIONS) == production.EXPECTED_PRODUCTION_PERMISSIONS, 'production_permissions_unexpected')
            if MIGRATION in ids:
                require(read(DELIVERY_SQL_PERMISSIONS) == EXPECTED_DELIVERY_PERMISSIONS, 'delivery_permissions_unexpected')

        def preflight(self):
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            # A pending I19 transaction belongs to that updater, even if its
            # release links currently look healthy. Do not take over its state.
            require(not (BACKUPS / 'production-pending.json').exists()
                    and not (BACKUPS / 'production-pending.json').is_symlink(), 'previous_update_recovery_pending')
            self.recovery = self.read_marker()
            current = {}
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_release_link_required')
                current[part] = link.resolve(strict=True)
                require(current[part].parent == ROOT / part / 'releases', 'previous_release_path_unexpected')
            own = current == self.new
            if self.recovery:
                self.snapshot = Path(self.recovery['snapshot'])
                self.old = {part: ROOT / part / 'releases' / name for part, name in zip(('backend', 'frontend'), self.recovery['previous'])}
                require(all(current[part] in (self.old[part], self.new[part]) for part in current), 'recovery_release_changed')
                saved = json.loads(private(self.snapshot / 'process-input.json'))
                process_environment, argv = saved['environment'], saved['argv']
                self.unit_configuration = saved['unit']
                properties = runtime.command(['systemctl', 'show', SERVICE, '--property=ActiveState,MainPID,User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'])
                observed = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
                require({key: observed.get(key, '') for key in self.unit_configuration} == self.unit_configuration, 'recovery_service_configuration_changed')
                require(observed.get('ActiveState') in ('inactive', 'failed', 'active', 'activating'), 'recovery_service_state_unexpected')
                if observed.get('ActiveState') == 'active':
                    self.process(current['backend'])
                cwd = self.old['backend']
                self.history_before = self.recovery['historyBefore']
            else:
                self.old = current
                require(own or tuple(self.old[part].name for part in ('backend', 'frontend')) in PREVIOUS_PAIRS, 'previous_release_not_supported')
                pid, cwd, argv = self.process(self.old['backend'])
                process_environment = None
            token.validate_environment_file(ENV_FILE)
            self.environment = regular(ENV_FILE)
            self.token_encryption_key = token.environment_key(self.environment)
            require(self.token_encryption_key is not None, 'existing_token_encryption_key_required')
            self.inspector = module('i19_inventory', files['inspect-native.py'])
            self.inspector.QUERIES = (commercial.PERMISSION_QUERY, commercial.NEW_SCHEMA, commercial.PRICE_PERMISSIONS,
                setup.TOKEN_SCHEMA, setup.TOKEN_SHAPE, setup.SETUP_SCHEMA, setup.SETUP_SHAPE, setup.SETUP_PERMISSIONS,
                SECURITY_SCHEMA, SECURITY_PERMISSIONS, production.PRODUCTION_SCHEMA, production.PRODUCTION_SHAPE, production.PRODUCTION_SQL_PERMISSIONS,
                schema.schema_query(True), schema.schema_query(False), DELIVERY_SQL_PERMISSIONS,
                self.inspector.SQL_MIGRATIONS)
            if process_environment is None:
                process_environment = self.inspector.process_environment(pid)
            cfg, source = self.inspector.configuration(process_environment, argv, cwd)
            require(source.get('environment') == 'Production' and self.inspector.policy_flags(cfg) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True, 'jwt_lifetime_compatible': True,
                'connection_compatible_with_production_policy': True, 'development_seed_enabled': False,
                'automatic_migrations_enabled': False, 'https_redirection_enabled': True}, 'production_configuration_unexpected')
            require(self.inspector.first(cfg, token.KEY_CONFIG, default=None) == self.token_encryption_key, 'token_encryption_key_configuration_conflict')
            self.fields = self.inspector.connection_fields(self.inspector.first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(self.inspector.permitted_connection(self.fields), 'sql_target_unexpected')
            self.sql_tool = next((tool for tool in self.inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool, 'sqlcmd_required')
            read = lambda query: self.inspector.query_sql(self.sql_tool, self.fields, query)
            ids = self.schemas(read)
            if not self.recovery:
                expected = migrations if own else base
                require(ids == expected, 'release_and_migration_history_disagree')
                self.history_before = ids
            self.app_permissions(ids)
            self.original_dll = regular(self.old['backend'] / 'Prodimt.Pedidos.Api.dll')
            self.overrides = {}
            for name in ('appsettings.json', 'appsettings.Production.json'):
                path = self.old['backend'] / name
                if path.exists():
                    raw = regular(path)
                    if self.recovery:
                        require(raw == private(self.snapshot / name), 'recovery_configuration_changed')
                    self.overrides['backend/' + name] = raw
            require(runtime.shutil.disk_usage(ROOT).free >= 2 * sum(map(len, files.values())) + 100 * 1024 * 1024, 'disk_space_insufficient')
            self.process_input = dict(environment=process_environment, argv=argv, unit=self.unit_configuration)
            self.state.update(base_release_verified=True, env_preserved=True, encryption_key_present=True,
                              migration_ids_before=ids, migrations_before=len(ids))
            if self.recovery:
                self.state.update(status='recovery_pending', phase='completed', recovery_pending=True, private_snapshot=str(self.snapshot))
            else:
                code, body = self.http('/health/db')
                require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
                require(self.http('/admin/reparto') == (200, regular(self.old['frontend'] / 'index.html')), 'previous_frontend_not_served')
                require(self.http('/api/admin/customers')[0] == 401, 'admin_authorization_unexpected')
                self.state['health_verified'] = True
                if own:
                    self.verify_stage('backend')
                    self.verify_stage('frontend')
                    self.health(True)
                    self.state.update(status='already_current', phase='completed', frontend_verified=True, tokens_preserved=True)

        def verify_schema(self):
            ids = self.schemas(self.sql_json, strict=True)
            require(self.sql_json(runtime.SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1), 'rest_days_schema_unexpected')
            require(self.sql_json(runtime.ALIAS_SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1), 'capture_alias_schema_unexpected')
            return len(ids)

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-i22-' + uuid.uuid4().hex)
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
            record = self.snapshot / ('database-backup-' + uuid.uuid4().hex + '.json')
            record.write_text(json.dumps(dict(verified_backup=str(path))))
            record.chmod(0o600)

        def backup_and_migrate(self):
            ids = self.schemas(self.sql_json, strict=True)
            require(ids == self.state['migration_ids_before'], 'database_changed_during_staging')
            counts = self.sql_json(runtime.COUNTS_QUERY)
            require(token.valid_counts(counts, ('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits', 'profiles', 'products', 'machines')), 'database_counts_unrecognized')
            monetary = self.sql_json(commercial.PRICE_COUNTS)
            tokens = self.sql_json(token.TOKEN_COUNTS)
            fingerprint = self.sql_json(setup.DATA_FINGERPRINT)
            security_before = self.sql_json(SECURITY_FINGERPRINT) if IDENTITY_MIGRATION in ids else None
            setup_before = self.sql_json(setup.SETUP_COUNTS) if SETUP_MIGRATION in ids else None
            delivery_before = self.sql_json(DELIVERY_COUNTS) if MIGRATION in ids else dict.fromkeys(DELIVERY_TABLES, 0)
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            require(regular(ENV_FILE) == self.environment and token.environment_key(self.environment) == self.token_encryption_key, 'configuration_changed_before_migration')
            new_migration = MIGRATION not in ids
            if new_migration:
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(migration_sql_for_sqlcmd(files), timeout=180)
                ids = self.history(self.sql_json)
            require(ids == migrations and self.verify_schema() == 17, 'migrated_schema_unexpected')
            self.verify_security()
            if new_migration:
                require(self.sql_json(DELIVERY_ROLE_DATA) == dict(driver_roles=1, driver_grants=2, driver_total_grants=2),
                        'delivery_role_seed_unexpected')
            require(self.sql_json(DELIVERY_COUNTS) == delivery_before, 'existing_delivery_data_changed')
            if IDENTITY_MIGRATION not in self.state['migration_ids_before']:
                require(self.sql_json(security.ADMIN_ACCESS_PRESERVED) == {'missing': 0}, 'existing_administrator_access_not_preserved')
            if security_before is not None:
                require(self.sql_json(SECURITY_FINGERPRINT) == security_before, 'existing_security_changed')
            if setup_before is not None:
                require(self.sql_json(setup.SETUP_COUNTS) == setup_before, 'existing_product_setup_changed')
            require(self.sql_json(runtime.COUNTS_QUERY) == counts and self.sql_json(commercial.PRICE_COUNTS) == monetary
                    and self.sql_json(token.TOKEN_COUNTS) == tokens and self.sql_json(setup.DATA_FINGERPRINT) == fingerprint, 'existing_data_changed')
            self.app_permissions(migrations)
            self.state.update(migrations_after=17, migrated_schema_verified=True, existing_data_counts_preserved=True,
                price_history_append_only_verified=True, tokens_preserved=True, security_permissions_verified=True, production_permissions_verified=True, delivery_permissions_verified=True)

        def health(self, new):
            super().health(new)
            if new:
                require(self.http('/api/admin/security')[0] == 401 and self.http('/api/admin/security/me')[0] == 401 and self.http('/api/admin/operations/production/')[0] == 401
                        and self.http('/api/delivery')[0] == 401 and self.http('/api/admin/delivery')[0] == 401,
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
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            if not self.recovery:
                self.snapshot = BACKUPS / ('delivery-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
                self.snapshot.mkdir(mode=0o700)
                (self.snapshot / 'api.env').write_bytes(self.environment)
                (self.snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
                (self.snapshot / 'process-input.json').write_text(json.dumps(self.process_input))
                for name, raw in self.overrides.items():
                    (self.snapshot / name.split('/', 1)[1]).write_bytes(raw)
                for path in self.snapshot.iterdir():
                    path.chmod(0o600)
            self.state.update(changes_made=True, private_snapshot=str(self.snapshot))
            self.stage()
            if not self.recovery:
                self.links(self.old)
                self.process(self.old['backend'])
            require(regular(ENV_FILE) == self.environment and regular(self.old['backend'] / 'Prodimt.Pedidos.Api.dll') == self.original_dll
                    and all(regular(self.old['backend'] / name.split('/', 1)[1]) == raw for name, raw in self.overrides.items()), 'configuration_or_backend_changed_during_staging')
            self.write_marker()
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
            self.marker.unlink()
            self.state.update(status='updated', phase='completed', health_verified=True, recovery_pending=False, source_commit=EXPECTED_SOURCE,
                              new_releases={part: str(path) for part, path in self.new.items()})

        def rollback(self):
            if not self.stop_requested:
                return
            try:
                runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            except (Exception, KeyboardInterrupt):
                self.state.update(recovery_manual_required=True, recovery_pending=True, rollback_blocked_service_stop=True)
                return
            self.state['rollback_service_stopped'] = True
            try:
                safe = self.history(self.sql_json) == self.history_before and not self.recovery
            except (Exception, KeyboardInterrupt):
                safe = False
            if not safe:
                self.state.update(recovery_manual_required=True, recovery_pending=True, application_stopped_for_security=True,
                                  database_automatically_restored=False)
                return
            super().rollback()
            self.marker.unlink(missing_ok=True)
            self.state.update(previous_health_verified=True, recovery_pending=False)

    return DeliveryInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-delivery-payload.zip'))
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
        return 0 if installer.state['status'] in ('planned', 'updated', 'already_current', 'recovery_pending') else 1
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
