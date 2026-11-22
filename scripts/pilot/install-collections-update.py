#!/usr/bin/env python3
"""Inventory or install route collections over the reviewed machine-workspace SQL21 base."""
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
import shutil
import stat
import sys
import types
import unicodedata
import uuid
import zipfile

EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
MIGRATION = '20261122201338_AddDeliveryCollections'
FEATURE = 'delivery-collections-v1'
BASE_RELEASE = 'machine-workspace-0d64f5bd2aef'
BASE_MIGRATIONS = (
    '20260527190837_InitialCreate',
    '20260528021837_AddOrderAuditLogs',
    '20260528025208_AddPilotAuthentication',
    '20260528035015_AddCatalogManagementSupport',
    '20260528131416_AddCatalogExternalCodesAndImportSupport',
    '20261024164610_AddPilotCatalogAndOrderSnapshots',
    '20261024181206_AddDailyOrderOperations',
    '20261024181934_ExpandOrderAuditSnapshots',
    '20261024202253_AddOrderChangeNotices',
    '20261028142346_AddCustomerRestDays',
    '20261028202031_AddCustomerCaptureAliases',
    '20261101154618_AddSharedProductsAndCustomerPrices',
    '20261101190000_AddRecoverableCustomerAccessTokens',
    '20261102152307_AddCustomerProductSetup',
    '20261102235000_AddSecurityAdministration',
    '20261103010000_AddProductionAllocation',
    '20261103020000_AddDeliveryOperations',
    '20261103030000_AddOrderingSchedules',
    '20261104041928_ExpandAuditMetadata',
    '20261108143413_AddDeliveryTripDepartureAndDayDrivers',
    '20261117145405_AddMachineWorkspace',
)
ASSEMBLIES = ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Application.dll',
              'Prodimt.Pedidos.Domain.dll', 'Prodimt.Pedidos.Infrastructure.dll')
SUPPORT = ('inspect-native.py', 'native-deployment-support.py', 'frontend-asset-support.py')
TABLES = ('CollectionCharges', 'CollectionChargeValuations', 'CollectionPayments',
          'CollectionCashReceipts', 'CollectionCashReceiptLines')
HISTORY_QUERY = 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'


class InstallError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


def safe_name(name):
    path = PurePosixPath(name)
    return bool(name) and path.as_posix() == name and not path.is_absolute() and ':' not in name and '\\' not in name \
        and all(part not in ('', '.', '..') and not part.startswith('.') for part in name.split('/'))


def regular(path, limit=100 * 1024 * 1024):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid in (0, 33) and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'file_identity_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'file_too_large')
    return raw


def sync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def write_private(path, raw):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    sync_directory(path.parent)


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE)
            and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 1200 and len({row.filename for row in entries}) == len(entries)
                and sum(row.file_size for row in entries) <= 250 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and stat.S_IFMT(row.external_attr >> 16) in (0, stat.S_IFREG) for row in entries), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in entries}
    manifest = json.loads(files.pop('manifest.json'))
    required = set(SUPPORT) | {'migration.sql', 'backend/appsettings.json', 'frontend/index.html',
                              'frontend/3rdpartylicenses.txt', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                              'backend/Prodimt.Pedidos.Api.deps.json', *('backend/' + name for name in ASSEMBLIES)}
    require(manifest.get('feature') == FEATURE and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == list(BASE_MIGRATIONS)
            and manifest.get('migrationsAfter') == [*BASE_MIGRATIONS, MIGRATION]
            and manifest.get('basePair') == [BASE_RELEASE, BASE_RELEASE]
            and required <= set(files) and set(manifest.get('files', {})) == set(files), 'payload_manifest_invalid')
    require(all(name in set(SUPPORT) | {'migration.sql'} or name.startswith(('backend/', 'frontend/')) for name in files)
            and all(not name.endswith(('.env', '.key', '.pem', '.pfx', '.pdb', '.map', '.log'))
                    and 'appsettings.Development' not in name for name in files), 'payload_private_file')
    require(all(meta == {'bytes': len(files[name]), 'sha256': sha(files[name])}
                for name, meta in manifest['files'].items()), 'payload_content_mismatch')
    validate_migration(files['migration.sql'].decode('utf-8-sig'))
    assets = module('collections_frontend_assets', files['frontend-asset-support.py'])
    assets.validate_asset_closure({name[9:]: data for name, data in files.items() if name.startswith('frontend/')})
    return files, manifest


def validate_migration(sql):
    require(re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) == [MIGRATION]
            and sql.count('BEGIN TRANSACTION;') == 1 and sql.count('COMMIT;') == 1
            and sql.index('BEGIN TRANSACTION;') < sql.index('CREATE TABLE')
            and set(re.findall(r'CREATE TABLE \[([^]]+)\]', sql)) == set(TABLES)
            and all(name in TABLES for name in re.findall(r'ALTER TABLE \[([^]]+)\]', sql))
            and all(f'GRANT SELECT,INSERT ON dbo.{name} TO prodimt_pedidos_app;' in sql
                    and f'DENY UPDATE,DELETE ON dbo.{name} TO prodimt_pedidos_app;' in sql for name in TABLES)
            and all(name in sql for name in ('CK_CollectionCharge_Quantity', 'CK_CollectionCharge_Amount',
                'CK_CollectionValuation_Amount', 'CK_CollectionPayment_Amount',
                'CK_CollectionCashReceipt_Amount', 'CK_CollectionCashReceiptLine_Amount'))
            and all(key in sql for key in ('operations.collections.work', 'operations.collections.read',
                'operations.collections.receive'))
            and not re.search(r'\b(?:DROP\s+(?:TABLE|COLUMN|DATABASE)|TRUNCATE|DELETE\s+FROM|ALTER\s+LOGIN)\b', sql, re.I),
            'incremental_sql_unexpected')


def schema_query():
    tables = ','.join("(N'" + name + "')" for name in TABLES)
    return f"""SELECT
 (SELECT COUNT(*) FROM (VALUES {tables}) v(t) JOIN sys.tables t
  ON t.schema_id=SCHEMA_ID(N'dbo') AND t.name=v.t) AS tables_count,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN
 (N'CK_CollectionCharge_Quantity',N'CK_CollectionCharge_Amount',N'CK_CollectionValuation_Amount',
 N'CK_CollectionPayment_Amount',N'CK_CollectionCashReceipt_Amount',N'CK_CollectionCashReceiptLine_Amount')
 AND parent_object_id IN (SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')
 AND name IN (N'CollectionCharges',N'CollectionChargeValuations',N'CollectionPayments',
 N'CollectionCashReceipts',N'CollectionCashReceiptLines')) AND is_disabled=0 AND is_not_trusted=0) AS checks_count,
 CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.SecurityRoles r WHERE IsActive=1 AND
 (r.Id='25250000-0000-0000-0000-000000000001' OR
 REPLACE(REPLACE(LTRIM(RTRIM(r.Name)),N'–',N'-'),N'—',N'-') COLLATE Latin1_General_100_CI_AI=N'Administración - Mostrador') AND
 (SELECT COUNT(*) FROM dbo.SecurityRolePermissions rp JOIN dbo.SecurityPermissions p ON p.Id=rp.PermissionId
 WHERE rp.RoleId=r.Id AND p.[Key] IN (N'operations.collections.read',N'operations.collections.receive'))=2)
 THEN 1 ELSE 0 END AS int) AS receiver_present,
 (SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE [Key] IN
  (N'operations.collections.work',N'operations.collections.read',N'operations.collections.receive')
  AND IsActive=1 AND IsSystem=1) AS permissions_count
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


def permission_query():
    return 'SELECT ' + ','.join(
        f"HAS_PERMS_BY_NAME(N'dbo.{table}',N'OBJECT',N'{permission}') AS [{table}_{permission}]"
        for table in TABLES for permission in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


def expected_permissions():
    result = {f'{table}_{permission}': int(permission in ('SELECT', 'INSERT'))
              for table in TABLES for permission in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')}
    return result


def preservation_query():
    """Administrative metadata only; no customer rows, credentials or token values."""
    names = ','.join("N'" + name + "'" for name in TABLES)
    return f"""SELECT
 JSON_QUERY((SELECT s.name AS schema_name,t.name AS table_name,c.name AS column_name,
 c.column_id,c.system_type_id,c.user_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,
 c.is_computed,c.collation_name,dc.definition AS default_definition
 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 JOIN sys.columns c ON c.object_id=t.object_id
 LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
 WHERE t.is_ms_shipped=0 AND NOT(s.name=N'dbo' AND t.name IN ({names}))
 ORDER BY s.name,t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS columns_metadata,
 JSON_QUERY((SELECT s.name AS schema_name,t.name AS table_name,i.name AS index_name,
 i.is_unique,i.is_primary_key,i.type,i.is_disabled,i.filter_definition,ic.key_ordinal,
 ic.index_column_id,ic.is_descending_key,ic.is_included_column,c.name AS column_name
 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
 JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE t.is_ms_shipped=0 AND NOT(s.name=N'dbo' AND t.name IN ({names}))
 ORDER BY s.name,t.name,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS indexes_metadata,
 JSON_QUERY((SELECT p.class,p.major_id,p.minor_id,p.grantee_principal_id,p.grantor_principal_id,
 p.type,p.state FROM sys.database_permissions p LEFT JOIN sys.tables t ON p.class=1 AND t.object_id=p.major_id
 WHERE NOT(p.class=1 AND COALESCE(t.schema_id,-1)=SCHEMA_ID(N'dbo') AND COALESCE(t.name,N'') IN ({names}))
 ORDER BY p.class,p.major_id,p.minor_id,p.grantee_principal_id,p.grantor_principal_id,p.type,p.state
 FOR JSON PATH)) AS sql_permissions,
 JSON_QUERY((SELECT s.name AS schema_name,t.name AS table_name,SUM(p.rows) AS row_count
 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1)
 WHERE t.is_ms_shipped=0 AND NOT(s.name=N'dbo' AND t.name IN
 ({names},N'__EFMigrationsHistory',N'SecurityRoles',N'SecurityPermissions',N'SecurityRolePermissions',N'SecurityStates'))
 GROUP BY s.name,t.name ORDER BY s.name,t.name FOR JSON PATH)) AS unchanged_counts,
 JSON_QUERY((SELECT Id,Name,Description,IsActive,IsSystem FROM dbo.SecurityRoles ORDER BY Id
 FOR JSON PATH,INCLUDE_NULL_VALUES)) AS security_roles,
 JSON_QUERY((SELECT Id,[Key],Name,Module,Description,IsActive,IsSystem FROM dbo.SecurityPermissions ORDER BY Id
 FOR JSON PATH,INCLUDE_NULL_VALUES)) AS security_permissions,
 JSON_QUERY((SELECT RoleId,PermissionId FROM dbo.SecurityRolePermissions ORDER BY RoleId,PermissionId
 FOR JSON PATH)) AS security_grants,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT * FROM dbo.AdminUsers ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES))),2) AS users_hash,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT * FROM dbo.SecurityUserRoles ORDER BY UserId,RoleId FOR JSON PATH,INCLUDE_NULL_VALUES))),2) AS user_roles_hash,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT * FROM dbo.SecurityUserPermissions ORDER BY UserId,PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES))),2) AS user_permissions_hash
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


def verify_preservation(before, after, *, static_only=False):
    """Permit only the migration's explicit security additions, retaining every old row."""
    if static_only:
        require(all(before.get(key) == after.get(key) for key in
            ('columns_metadata', 'indexes_metadata', 'sql_permissions')),
            'existing_database_metadata_or_permissions_changed')
        return
    security = {'security_roles', 'security_permissions', 'security_grants'}
    require(set(before) == set(after) and all(before[key] == after[key] for key in set(before) - security),
        'existing_database_metadata_or_permissions_changed')
    def indexed(rows, fields):
        require(isinstance(rows, list) and all(isinstance(row, dict) for row in rows), 'security_snapshot_invalid')
        result = {tuple(str(row[field]).lower() for field in fields): row for row in rows}
        require(len(result) == len(rows), 'security_snapshot_duplicates')
        return result
    additions = {}
    for key, fields in (('security_roles', ('Id',)), ('security_permissions', ('Id',)),
                        ('security_grants', ('RoleId', 'PermissionId'))):
        old, new = indexed(before[key], fields), indexed(after[key], fields)
        require(all(new.get(identity) == row for identity, row in old.items()), 'existing_security_row_changed')
        additions[key] = {identity: row for identity, row in new.items() if identity not in old}
    canonical = '25250000-0000-0000-0000-000000000001'
    def normalized(value):
        value = value.strip(' ').replace('–', '-').replace('—', '-')
        return ''.join(c for c in unicodedata.normalize('NFD', value) if not unicodedata.combining(c)).casefold()
    roles = [row for row in before['security_roles'] if row['IsActive'] and
        normalized(row['Name']) == normalized('Administración - Mostrador')]
    if len(roles) > 1:
        roles = [row for row in roles if row['Name'] == 'Administración - Mostrador']
    require(len(roles) <= 1, 'cashier_snapshot_ambiguous')
    receiver = str(roles[0]['Id']).lower() if roles else canonical
    new_roles = additions['security_roles']
    require(set(new_roles) == (set() if roles else {(canonical,)}), 'unexpected_security_role_added')
    if new_roles:
        row = new_roles[(canonical,)]
        require(row['IsActive'] is True and row['IsSystem'] is True and
            (row['Name'] == 'Administración - Mostrador' or re.fullmatch(r'Administración - Mostrador \(caja(?: [0-9]+)?\)', row['Name'])),
            'new_cashier_role_unexpected')
    keys = {24: 'operations.collections.work', 25: 'operations.collections.receive', 26: 'operations.collections.read'}
    permission_ids = {number: f'18180000-0000-0000-0001-{number:012}' for number in keys}
    require(set(additions['security_permissions']) == {(value,) for value in permission_ids.values()},
        'unexpected_security_permission_added')
    for number, identity in permission_ids.items():
        row = additions['security_permissions'][(identity,)]
        require(row['Key'] == keys[number] and row['IsActive'] is True and row['IsSystem'] is True,
            'new_collection_permission_unexpected')
    allowed = {('18180000-0000-0000-0000-000000000001', value) for value in permission_ids.values()}
    allowed.add(('22220000-0000-0000-0000-000000000001', permission_ids[24]))
    allowed.update((receiver, permission_ids[number]) for number in (25, 26))
    if new_roles:
        allowed.update((receiver, f'18180000-0000-0000-0001-{number:012}') for number in (1, 2))
    require(set(additions['security_grants']) == allowed, 'unexpected_security_grant_added')


def make_installer(package, base_frontend=None):
    files, manifest = load_payload(package)
    runtime = module('collections_native_support', files['native-deployment-support.py'])
    runtime.InstallError = InstallError
    inspector = module('collections_inventory_support', files['inspect-native.py'])

    class CollectionsInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.inspector = files, package, inspector
            self.new = {part: ROOT / part / 'releases' / ('collections-' + EXPECTED_SOURCE[:12])
                        for part in ('backend', 'frontend')}
            self.old = {}
            self.state = dict(status='planned', phase='inventory', source_commit=EXPECTED_SOURCE,
                              changes_made=False, database_migrations_run=False,
                              database_automatically_restored=False, secret_values_printed=False,
                              health_verified=False, recovery_pending=False)
            self.marker = BACKUPS / 'collections-pending.json'
            self.stop_requested = False
            self.base_frontend = manifest["basePair"][1] or base_frontend
            self.migration_verified = False
            self.preservation_verified = False
            self.sql_login = self.sql_password = ''

        def app_json(self, query):
            inspector.QUERIES = (query,)
            return inspector.query_sql(self.sql_tool, self.fields, query)

        def history(self, reader):
            rows = reader(HISTORY_QUERY)
            require(isinstance(rows, list) and all(isinstance(row, dict) and set(row) == {'MigrationId'}
                    for row in rows), 'migration_history_invalid')
            return [row['MigrationId'] for row in rows]

        def current_links(self):
            current = {}
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_symlink_required')
                target = link.resolve(strict=True)
                require(target.parent == ROOT / part / 'releases', 'release_path_unexpected')
                current[part] = target
            return current

        def preflight(self, inventory=False):
            require(base_frontend in (None, BASE_RELEASE), 'base_frontend_unexpected')
            current = self.current_links()
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            require(not any(path != self.marker for path in BACKUPS.glob('*-pending.json')),
                'previous_update_recovery_pending')
            self.environment = regular(ENV_FILE)
            self.recovery = None
            if self.marker.exists() or self.marker.is_symlink():
                require(self.marker.stat().st_uid == 0 and self.marker.stat().st_mode & 0o7777 == 0o600,
                        'recovery_marker_identity_unexpected')
                self.recovery = json.loads(regular(self.marker))
                require(self.recovery.get('source') == EXPECTED_SOURCE and self.recovery.get('payload') == EXPECTED_PAYLOAD_SHA256,
                        'another_update_recovery_pending')
                require(type(self.recovery.get('preservationVerified', False)) is bool,
                    'preservation_proof_invalid')
                self.snapshot = Path(self.recovery['snapshot'])
                require(self.snapshot.parent == BACKUPS and re.fullmatch(r'collections-update-[0-9]{8}T[0-9]{6}Z-[a-f0-9]{12}',
                        self.snapshot.name), 'recovery_snapshot_invalid')
                runtime.directory_safe(self.snapshot)
                require(self.snapshot.stat().st_mode & 0o7777 == 0o700, 'recovery_snapshot_mode_unexpected')
                self.old = {part: ROOT / part / 'releases' / self.recovery['previous'][part] for part in self.new}
                require(self.old['backend'].name == manifest['basePair'][0]
                        and (manifest['basePair'][1] is None or self.old['frontend'].name == manifest['basePair'][1])
                        and all(current[part] in (self.old[part], self.new[part]) for part in self.new),
                        'recovery_release_changed')
                require(regular(self.snapshot / 'api.env') == self.environment, 'recovery_environment_changed')
                saved = json.loads(regular(self.snapshot / 'process-input.json'))
                self.process_environment, self.argv, self.unit_configuration = saved['environment'], saved['argv'], saved['unit']
                self.baseline_hashes = saved['hashes']
                properties = runtime.command(['systemctl', 'show', SERVICE,
                    '--property=User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'])
                actual = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
                require(actual == self.unit_configuration, 'service_configuration_changed')
                cwd = self.old['backend']
                self.overrides = {f'backend/{name}': regular(self.snapshot / name)
                    for name in ('appsettings.json', 'appsettings.Production.json') if (self.snapshot / name).exists()}
                require(all(regular(cwd / name.split('/', 1)[1]) == raw for name, raw in self.overrides.items())
                        and all(sha(regular(cwd / name)) == value for name, value in self.baseline_hashes.items()),
                        'recovery_base_changed')
            else:
                self.old = current
                pid, cwd, self.argv = self.process(current['backend'])
                self.process_environment = inspector.process_environment(pid)
                self.overrides = {f'backend/{name}': regular(cwd / name) for name in ('appsettings.json', 'appsettings.Production.json')
                                  if (cwd / name).exists()}
                self.baseline_hashes = {name: sha(regular(cwd / name)) for name in ASSEMBLIES}
            cfg, source = inspector.configuration(self.process_environment, self.argv, cwd)
            require(source['environment'] == 'Production' and source['content_root'] == str(cwd)
                    and inspector.policy_flags(cfg) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False,
                'https_redirection_enabled': True}, 'production_configuration_unexpected')
            self.configuration = cfg
            self.fields = inspector.connection_fields(inspector.first(cfg, 'ConnectionStrings:Pedidos',
                'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(self.fields), 'database_target_unexpected')
            self.sql_tool = next((tool for tool in inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool is not None, 'sqlcmd_required')
            history = self.history(self.app_json)
            if not self.recovery:
                status, body = self.http('/health/db')
                require(status == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
                self.state['health_verified'] = True
            self.state.update(releases={part: path.name for part, path in current.items()}, migration_ids=history,
                              backend_hashes=self.baseline_hashes, env_preserved=True, recovery_pending=bool(self.recovery),
                              sql_target=dict(server=inspector.SERVER, database=inspector.DATABASE))
            if inventory:
                self.state.update(status='inventoried', phase='completed')
                return
            if not self.recovery and current == self.new:
                require(history == manifest['migrationsAfter'], 'current_migration_mismatch')
                self.verify_new_schema(logical_setup_required=False)
                for part in self.new:
                    self.verify_stage(part)
                self.health(True)
                self.refresh_report()
                self.state.update(status='already_current', phase='completed', frontend_verified=True)
                return
            if not self.recovery:
                require(self.base_frontend is not None and re.fullmatch('[A-Za-z0-9][A-Za-z0-9._-]{0,119}', self.base_frontend),
                        'base_frontend_pin_required_use_inventory')
                require([current['backend'].name, current['frontend'].name] ==
                        [manifest['basePair'][0], self.base_frontend], 'base_release_unexpected')
            require(history in (manifest['migrationsBefore'], manifest['migrationsAfter']), 'migration_history_unexpected')
            if not self.recovery:
                require(history == manifest['migrationsBefore'], 'untracked_migration_state')
            self.migration_verified = history == manifest['migrationsAfter']
            if self.recovery and self.recovery.get('preservationVerified', False):
                require(self.migration_verified, 'preservation_proof_migration_mismatch')
                self.verify_preservation_proof()
                self.preservation_verified = True
                self.state['preservation_verified_before_activation'] = True
            if self.migration_verified:
                self.verify_new_schema()
            else:
                shape = self.app_json("SELECT COUNT(*) AS new_tables FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN ("
                    + ','.join("N'" + name + "'" for name in TABLES) + ") FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
                require(shape == {'new_tables': 0}, 'partial_schema_detected')
            require(shutil.disk_usage(ROOT).free >= 2 * sum(len(raw) for raw in files.values()) + 100 * 1024 * 1024,
                    'release_disk_insufficient')
            self.state.update(base_release_verified=True, recovery_pending=bool(self.recovery))

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.collections-stage-' + uuid.uuid4().hex)
                temporary.mkdir(mode=0o700)
                try:
                    for name, raw in self.expected(part).items():
                        path = temporary / name
                        path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                        override = part + '/' + name in self.overrides
                        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                                             0o640 if override else 0o644)
                        with os.fdopen(descriptor, 'wb') as stream:
                            stream.write(raw)
                        os.chown(path, 0, 33 if override else 0)
                        path.chmod(0o640 if override else 0o644)
                    for directory in temporary.rglob('*'):
                        if directory.is_dir():
                            directory.chmod(0o755)
                    temporary.chmod(0o755)
                    temporary.rename(target)
                finally:
                    if temporary.exists():
                        shutil.rmtree(temporary)
                self.verify_stage(part)

        def database_backup(self):
            folder = runtime.SQL_BACKUPS / ('prodimt-' + self.snapshot.name)
            backup_root = runtime.SQL_BACKUPS.stat()
            require(stat.S_ISDIR(backup_root.st_mode) and runtime.SQL_BACKUPS.resolve(strict=True) == runtime.SQL_BACKUPS
                    and backup_root.st_uid == 999 and backup_root.st_gid == 988, 'sql_backup_directory_unexpected')
            require(shutil.disk_usage(runtime.SQL_BACKUPS).free >= (self.database_mib + 256) * 1024 * 1024,
                    'backup_disk_insufficient')
            if (self.snapshot / 'database-backup.json').exists():
                self.verify_backup()
                return
            # An earlier BACKUP may have completed before metadata was persisted.
            # Start another uniquely named attempt instead of truncating that file.
            folder = runtime.SQL_BACKUPS / ('prodimt-' + self.snapshot.name + '-' + uuid.uuid4().hex[:8])
            folder.mkdir(mode=0o700)
            os.chown(folder, backup_root.st_uid, backup_root.st_gid)
            path = folder / 'ProdimtPedidos-copy-only.bak'
            self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + str(path) + "' WITH COPY_ONLY,CHECKSUM;", timeout=300)
            backup = path.lstat()
            require(stat.S_ISREG(backup.st_mode) and backup.st_nlink == 1 and backup.st_uid == backup_root.st_uid
                    and path.resolve(strict=True) == path and backup.st_size > 0, 'backup_file_unexpected')
            path.chmod(0o600)
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            metadata = self.snapshot / 'database-backup.json'
            write_private(metadata, json.dumps(dict(path=str(path), bytes=backup.st_size)).encode())
            self.state['verified_database_backup'] = str(path)

        def verify_backup(self):
            value = json.loads(regular(self.snapshot / 'database-backup.json'))
            path = Path(value['path'])
            require(path.parent.parent == runtime.SQL_BACKUPS and re.fullmatch(re.escape('prodimt-' + self.snapshot.name) + '-[a-f0-9]{8}', path.parent.name)
                    and path.name == 'ProdimtPedidos-copy-only.bak' and path.lstat().st_size == value['bytes']
                    and not path.is_symlink(), 'recovery_backup_unexpected')
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            self.state['verified_database_backup'] = str(path)

        def verify_new_schema(self, *, logical_setup_required=True):
            actual = self.app_json(schema_query())
            if self.preservation_verified or not logical_setup_required:
                require(isinstance(actual, dict) and actual.get('receiver_present') in (0, 1),
                    'collections_schema_unexpected')
                actual = dict(actual, receiver_present=1)
            require(actual == dict(tables_count=5, checks_count=6, receiver_present=1, permissions_count=3),
                'collections_schema_unexpected')
            require(self.app_json(permission_query()) == expected_permissions(), 'collections_permissions_unexpected')
            self.state['schema_verified'] = True

        def health(self, new):
            super().health(new)
            if new:
                for path in ('/api/admin/collections/cashier',
                             '/api/delivery/trips/00000000-0000-0000-0000-000000000000/collections'):
                    require(self.http(path)[0] == 401, 'collections_endpoint_not_protected')

        def preserve_database(self, capture=False):
            value = self.sql_json(preservation_query())
            require(isinstance(value, dict) and set(value) == {'columns_metadata', 'indexes_metadata',
                'sql_permissions', 'unchanged_counts', 'security_roles', 'security_permissions', 'security_grants',
                'users_hash', 'user_roles_hash', 'user_permissions_hash'} and
                all(isinstance(value[key], list) for key in value if not key.endswith('_hash')) and
                all(isinstance(value[key], str) and re.fullmatch('[A-Fa-f0-9]{64}', value[key])
                    for key in value if key.endswith('_hash')),
                'database_preservation_metadata_invalid')
            path = self.snapshot / 'database-preservation.json'
            if capture and not path.exists():
                write_private(path, json.dumps(value, sort_keys=True).encode())
            before = json.loads(regular(path))
            if capture:
                require(before == value, 'existing_database_metadata_or_permissions_changed')
            else:
                verify_preservation(before, value, static_only=self.preservation_verified)
                self.state.update(existing_schema_preserved=True, existing_sql_permissions_preserved=True,
                    preservation_verified_before_activation=self.preservation_verified)
                if self.preservation_verified:
                    self.state['mutable_data_revalidation_skipped_after_activation'] = True
                else:
                    self.state.update(existing_row_counts_preserved=True, existing_security_rows_preserved=True,
                        existing_user_auth_preserved=True)

        def preservation_proof(self):
            return dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256,
                baselineSha256=sha(regular(self.snapshot / 'database-preservation.json')),
                migration_ids=manifest['migrationsAfter'], kind='full-before-activation')

        def verify_preservation_proof(self):
            path = self.snapshot / 'preservation-verified.json'
            require(path.exists() and not path.is_symlink(), 'preservation_proof_missing')
            meta = path.stat()
            require(meta.st_uid == 0 and meta.st_mode & 0o7777 == 0o600
                and json.loads(regular(path)) == self.preservation_proof(), 'preservation_proof_invalid')

        def record_preservation_proof(self):
            path = self.snapshot / 'preservation-verified.json'
            if not path.exists():
                write_private(path, json.dumps(self.preservation_proof(), sort_keys=True).encode())
            self.verify_preservation_proof()
            self.preservation_verified = True
            self.write_marker()
            self.state['preservation_verified_before_activation'] = True

        def refresh_report(self):
            current = self.current_links()
            require(current == self.new, 'post_update_release_mismatch')
            history = self.history(self.app_json)
            require(history == manifest['migrationsAfter'], 'post_update_migration_mismatch')
            hashes = {name: sha(regular(current['backend'] / name)) for name in ASSEMBLIES}
            require(all(value == sha(self.expected('backend')[name]) for name, value in hashes.items()),
                'post_update_backend_changed')
            require(regular(ENV_FILE) == self.environment, 'environment_changed')
            self.state.update(migration_ids=history, migrations_after=len(history), backend_hashes=hashes,
                releases={part: path.name for part, path in current.items()}, env_preserved=True)

        def write_marker(self):
            data = dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256, snapshot=str(self.snapshot),
                        previous={part: path.name for part, path in self.old.items()},
                        migrationVerified=self.migration_verified, preservationVerified=self.preservation_verified)
            target = self.marker.with_name('.collections-marker-' + uuid.uuid4().hex)
            descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(descriptor, 'w') as stream:
                json.dump(data, stream)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(target, self.marker)
            sync_directory(self.marker.parent)

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.sql_login = runtime.prompt_secret('Usuario administrador SQL existente (oculto): ').strip()
            self.sql_password = runtime.prompt_secret('Contraseña SQL (oculta): ')
            require(0 < len(self.sql_login) <= 128 and 0 < len(self.sql_password) <= 512
                    and not any(c in self.sql_login + self.sql_password for c in '\0\r\n'), 'sql_credentials_invalid')
            admin = self.sql_json(runtime.ADMIN_QUERY)
            require(admin.get('database_name') == runtime.DATABASE and admin.get('view_definition') == 1
                    and (admin.get('sysadmin') == 1 or admin.get('db_owner') == 1 and admin.get('backup_permission') == 1
                         and (admin.get('verify_permission') == 1 or admin.get('dbcreator') == 1)),
                    'sql_admin_permissions_insufficient')
            require(self.history(self.sql_json) == (manifest['migrationsAfter'] if self.migration_verified
                    else manifest['migrationsBefore']), 'administrative_history_mismatch')
            self.database_mib = admin.get('files_mib')
            require(isinstance(self.database_mib, int) and self.database_mib > 0, 'database_size_unrecognized')
            if self.migration_verified:
                self.verify_backup()
            if not self.recovery:
                self.snapshot = BACKUPS / ('collections-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
                self.snapshot.mkdir(mode=0o700)
                write_private(self.snapshot / 'api.env', self.environment)
                write_private(self.snapshot / 'process-input.json', json.dumps(dict(environment=self.process_environment,
                    argv=self.argv, unit=self.unit_configuration, hashes=self.baseline_hashes)).encode())
                for name, raw in self.overrides.items():
                    write_private(self.snapshot / name.split('/', 1)[1], raw)
            self.state.update(changes_made=True, private_snapshot=str(self.snapshot))
            self.state['phase'] = 'stage-releases'
            self.stage()
            for part in self.new:
                self.verify_stage(part)
            require(regular(ENV_FILE) == self.environment, 'environment_changed')
            if not self.recovery:
                self.links(self.old)
                self.process(self.old['backend'])
                require(all(sha(regular(self.old['backend'] / name)) == value for name, value in self.baseline_hashes.items()),
                        'base_backend_changed')
            candidate_argv = [str(self.new['backend'] / 'Prodimt.Pedidos.Api.dll')
                if Path(arg).name == 'Prodimt.Pedidos.Api.dll' else arg for arg in self.argv]
            candidate_cfg, candidate_source = inspector.configuration(self.process_environment,
                candidate_argv, self.new['backend'])
            require(candidate_cfg == self.configuration and candidate_source['content_root'] == str(self.new['backend']),
                    'effective_configuration_changed')
            self.write_marker()
            self.state['phase'] = 'stop-service'
            self.stop_requested = True
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            self.preserve_database(capture=not self.migration_verified)
            if not self.migration_verified:
                self.state['phase'] = 'backup-and-migrate'
                self.database_backup()
                self.state['migrations_may_have_changed'] = True
                self.sql_run(files['migration.sql'].decode('utf-8-sig'), timeout=120)
                require(self.history(self.sql_json) == manifest['migrationsAfter'], 'post_migration_history_unexpected')
                self.migration_verified = True
                self.verify_new_schema()
                self.state['database_migrations_run'] = True
                self.write_marker()
            self.preserve_database()
            if not self.preservation_verified:
                self.record_preservation_proof()
            self.state['phase'] = 'activate-releases'
            self.switch(self.new)
            runtime.command(['systemctl', 'start', SERVICE], timeout=45)
            self.health(True)
            self.refresh_report()
            self.marker.unlink()
            self.state.update(status='updated', phase='completed', health_verified=True, frontend_verified=True,
                              recovery_pending=False)

        def rollback(self):
            if not self.stop_requested:
                return
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            if self.history(self.sql_json) == manifest['migrationsBefore']:
                self.switch(self.old)
                runtime.command(['systemctl', 'start', SERVICE], timeout=45)
                self.health(False)
                self.marker.unlink(missing_ok=True)
                self.state.update(previous_health_verified=True, recovery_pending=False)
            else:
                self.state.update(recovery_pending=True, application_stopped_for_recovery=True)

    return CollectionsInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-collections-payload.zip'))
    parser.add_argument('--inventory', action='store_true')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--base-frontend', help='Nombre exacto obtenido en --inventory cuando el paquete no lo fija')
    args = parser.parse_args()
    require(not (args.inventory and args.apply), 'choose_inventory_or_apply')
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    installer = make_installer(args.package, args.base_frontend)
    descriptor = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        meta = os.fstat(descriptor)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
                and meta.st_mode & 0o7777 == 0o600, 'lock_identity_unexpected')
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            if args.apply:
                installer.apply()
            else:
                installer.preflight(inventory=args.inventory)
                if installer.state['status'] == 'planned':
                    installer.state['phase'] = 'completed'
        except (Exception, KeyboardInterrupt) as error:
            installer.state.update(status='failed', issue=str(error) if isinstance(error, InstallError) else 'local_operation_failed')
            try:
                installer.rollback()
            except (Exception, KeyboardInterrupt):
                installer.state.update(recovery_pending=True, recovery_check_failed=True)
        print(json.dumps(installer.state, ensure_ascii=False, indent=2))
        return 0 if installer.state['status'] in ('updated', 'already_current', 'planned', 'inventoried') else 1
    finally:
        installer.sql_password = ''
        if hasattr(installer, 'fields'):
            installer.fields.clear()
        os.close(descriptor)


if __name__ == '__main__':
    os.umask(0o077)
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt):
        print(json.dumps(dict(status='failed', issue='installation_not_started', changes_made=False)))
        sys.exit(1)
