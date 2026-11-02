#!/usr/bin/env python3
"""Install I16 on the verified I15 frontend/I12 backend with SQL backup.

Adds customer-product follow-up and optional machine inheritance, 13 to 14.
Preserves every token, its stable recovery key, prices, orders and configuration.
Application rollback never restores SQL automatically.
"""
import argparse
import base64
from datetime import datetime, timezone
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import secrets
import signal
import stat
import sys
import types
import uuid
import zipfile

REVISION = '20261102-customer-products-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
PREVIOUS = dict(backend='access-tokens-65560ca3cf98', frontend='customer-catalog-afb8f3ad9c33')
PRICE_MIGRATION = '20261101154618_AddSharedProductsAndCustomerPrices'
TOKEN_MIGRATION = '20261101190000_AddRecoverableCustomerAccessTokens'
MIGRATION = '20261102152307_AddCustomerProductSetup'
KEY_ENV = 'CustomerAccessTokens__EncryptionKey'
KEY_CONFIG = 'CustomerAccessTokens:EncryptionKey'
TOKEN_SCHEMA = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
  AND name IN (N'ProtectedToken',N'MetadataVersion',N'IsReminderLink')) AS columns_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND name=N'ProtectedToken'
  AND TYPE_NAME(system_type_id)=N'nvarchar' AND max_length=2048 AND is_nullable=1 AND default_object_id=0) AS protected_column,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'MetadataVersion'
  AND TYPE_NAME(c.system_type_id)=N'int' AND c.max_length=4 AND c.is_nullable=0
  AND REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N'')=N'1') AS version_column,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'IsReminderLink'
  AND TYPE_NAME(c.system_type_id)=N'bit' AND c.max_length=1 AND c.is_nullable=0
  AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''),N'[',N''),N']',N''))
   IN (N'0',N'cast0asbit',N'convertbit,0')) AS reminder_column
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_TOKEN_SCHEMA = dict(columns_count=3, protected_column=1, version_column=1, reminder_column=1)
# SELECT permits structural metadata; SQL Server may hide default/check SQL
# definitions without VIEW DEFINITION. The application never needs that right.
# Verify literal expressions later with the existing SQL administrator, before
# changing files or data, and keep read-only/already-current checks least-privilege.
TOKEN_SHAPE = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
  AND name IN (N'ProtectedToken',N'MetadataVersion',N'IsReminderLink')) AS columns_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND name=N'ProtectedToken'
  AND TYPE_NAME(system_type_id)=N'nvarchar' AND max_length=2048 AND is_nullable=1 AND default_object_id=0) AS protected_column,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND name=N'MetadataVersion'
  AND TYPE_NAME(system_type_id)=N'int' AND max_length=4 AND is_nullable=0 AND default_object_id<>0) AS version_column,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND name=N'IsReminderLink'
  AND TYPE_NAME(system_type_id)=N'bit' AND max_length=1 AND is_nullable=0 AND default_object_id<>0) AS reminder_column
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
TOKEN_COUNTS = """SELECT
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens) AS tokens,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1) AS active_tokens,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1 AND ProtectedToken IS NULL) AS active_legacy_tokens,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE ProtectedToken IS NOT NULL) AS protected_tokens,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1 AND ProtectedToken IS NOT NULL) AS active_protected_tokens,
 (SELECT COALESCE(SUM(CONVERT(bigint,MetadataVersion)),0) FROM dbo.CustomerAccessTokens) AS metadata_versions,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE MetadataVersion<1) AS invalid_metadata,
 (SELECT COUNT_BIG(*) FROM dbo.AuditLogs WHERE EntityType=N'CustomerAccessToken'
  AND EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade') AS upgrade_audits
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


def setup_schema_query(definitions=True):
    columns = [
        ('CustomerFrequentProducts','UseCustomerDefaultMachine','bit',1,0),
        ('CustomerProductSetupRequests','Id','uniqueidentifier',16,0),
        ('CustomerProductSetupRequests','CustomerId','uniqueidentifier',16,0),
        ('CustomerProductSetupRequests','ProductId','uniqueidentifier',16,0),
        ('CustomerProductSetupRequests','ProductNameSnapshot','nvarchar',400,0),
        ('CustomerProductSetupRequests','UnitCode','nvarchar',32,0),
        ('CustomerProductSetupRequests','Version','int',4,0),
        ('CustomerProductSetupRequests','IsResolved','bit',1,0),
        ('CustomerProductSetupRequests','CreatedAt','datetimeoffset',10,0),
        ('CustomerProductSetupRequests','LastRequestedAt','datetimeoffset',10,0),
        ('CustomerProductSetupRequests','ResolvedAt','datetimeoffset',10,1),
        ('CustomerProductSetupRequests','ResolvedBy','nvarchar',400,1),
        ('CustomerProductSetupRequests','ResolvedByName','nvarchar',400,1),
        ('CustomerProductSetupOrders','SetupRequestId','uniqueidentifier',16,0),
        ('CustomerProductSetupOrders','OrderId','uniqueidentifier',16,0),
        ('CustomerProductSetupOrders','Source','nvarchar',32,0),
        ('CustomerProductSetupOrders','RequestedAt','datetimeoffset',10,0),
        ('CustomerProductSetupOrders','ActorId','nvarchar',400,1),
        ('CustomerProductSetupOrders','ActorDisplayName','nvarchar',400,1)]
    values = ','.join("(N'%s',N'%s',N'%s',%d,%d)" % row for row in columns)
    default_definition = """ AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''),N'[',N''),N']',N''))
    IN (N'1',N'cast1asbit',N'convertbit,1')""" if definitions else ''
    check_definition = """ AND REPLACE(REPLACE(REPLACE(REPLACE(LOWER(definition),N' ',N''),N'(',N''),N')',N''),N'\n',N'')=N'[version]>0'""" if definitions else ''
    return """SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (N'CustomerProductSetupRequests',N'CustomerProductSetupOrders')) AS tables_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerProductSetupRequests')) AS request_columns,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerProductSetupOrders')) AS link_columns,
 (SELECT COUNT(*) FROM (VALUES """ + values + """) e(t,n,k,l,z)
   JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.t) AND c.name=e.n
   WHERE TYPE_NAME(c.system_type_id)=e.k AND c.max_length=e.l AND c.is_nullable=e.z
    AND (e.k<>N'datetimeoffset' OR c.scale=7)) AS valid_columns,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id=OBJECT_ID(N'dbo.CustomerFrequentProducts') AND c.name=N'UseCustomerDefaultMachine'
   """ + default_definition + """) AS inherit_default,
 (SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.CustomerProductSetupRequests')
   AND i.name=N'IX_CustomerProductSetupRequests_CustomerId_ProductId' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0
   AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id)=2
   AND (SELECT COUNT(*) FROM sys.index_columns k JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id
     WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.is_descending_key=0
      AND ((k.key_ordinal=1 AND c.name=N'CustomerId') OR (k.key_ordinal=2 AND c.name=N'ProductId')))=2) AS pair_index,
 (SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns k ON k.object_id=i.object_id AND k.index_id=i.index_id
   JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id
   WHERE i.object_id=OBJECT_ID(N'dbo.CustomerProductSetupOrders') AND i.is_primary_key=1 AND i.is_disabled=0
     AND ((k.key_ordinal=1 AND c.name=N'SetupRequestId') OR (k.key_ordinal=2 AND c.name=N'OrderId'))) AS link_key_columns,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerProductSetupRequests')
   AND name=N'CK_ProductSetup_Version' AND is_disabled=0 AND is_not_trusted=0
   """ + check_definition + """) AS trusted_check,
 (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0
   AND name IN (N'FK_CustomerProductSetupRequests_Customers_CustomerId',N'FK_CustomerProductSetupRequests_Products_ProductId',
     N'FK_CustomerProductSetupOrders_CustomerProductSetupRequests_SetupRequestId',N'FK_CustomerProductSetupOrders_Orders_OrderId')) AS trusted_foreign_keys
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


SETUP_SCHEMA = setup_schema_query()
SETUP_SHAPE = setup_schema_query(definitions=False)
EXPECTED_SETUP_SCHEMA = dict(tables_count=2, request_columns=12, link_columns=6, valid_columns=19,
    inherit_default=1, pair_index=1, link_key_columns=2, trusted_check=1, trusted_foreign_keys=4)
SETUP_PERMISSIONS = """SELECT
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupRequests',N'OBJECT',N'SELECT') AS read_requests,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupRequests',N'OBJECT',N'INSERT') AS insert_requests,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupRequests',N'OBJECT',N'UPDATE') AS update_requests,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupRequests',N'OBJECT',N'DELETE') AS delete_requests,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupOrders',N'OBJECT',N'SELECT') AS read_links,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupOrders',N'OBJECT',N'INSERT') AS insert_links,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupOrders',N'OBJECT',N'UPDATE') AS update_links,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductSetupOrders',N'OBJECT',N'DELETE') AS delete_links
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_SETUP_PERMISSIONS = dict(read_requests=1, insert_requests=1, update_requests=1, delete_requests=0,
    read_links=1, insert_links=1, update_links=0, delete_links=0)
SETUP_GRANTS = """BEGIN TRANSACTION;
GRANT SELECT, INSERT, UPDATE ON dbo.CustomerProductSetupRequests TO [prodimt_pedidos_app];
DENY DELETE ON dbo.CustomerProductSetupRequests TO [prodimt_pedidos_app];
GRANT SELECT, INSERT ON dbo.CustomerProductSetupOrders TO [prodimt_pedidos_app];
DENY UPDATE, DELETE ON dbo.CustomerProductSetupOrders TO [prodimt_pedidos_app];
COMMIT TRANSACTION;
"""
SETUP_COUNTS = """SELECT
 (SELECT COUNT_BIG(*) FROM dbo.CustomerProductSetupRequests) AS requests,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerProductSetupOrders) AS links,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerProductSetupRequests WHERE IsResolved=0) AS open_requests,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerFrequentProducts WHERE UseCustomerDefaultMachine=1) AS inherit_default,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerFrequentProducts WHERE UseCustomerDefaultMachine=0) AS independent
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
DATA_FINGERPRINT = """SELECT
 CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo.CustomerAccessTokens ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS tokens,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT Id,UnitPriceSnapshot,CurrencyCodeSnapshot,PriceRevisionId,PriceEffectiveFrom
  FROM dbo.OrderLines ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS prices
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


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
    required = {'inspect-native.py', 'native-deployment-support.py', 'commercial-catalog-support.py', 'migrations-i16.sql',
                'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'customer-additional-products-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 13 and manifest.get('migrationsAfter') == 14
            and manifest.get('previousReleases') == PREVIOUS and manifest.get('revokeLegacyTokens') is False
            and manifest.get('encryptionKeyInPackage') is False and set(manifest.get('files', {})) == set(files)
            and required <= set(files) and all(name in required or name.startswith(('backend/', 'frontend/')) for name in files),
            'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12', '.trace', '.dmp', '.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    validate_migration(files['migrations-i16.sql'].decode('utf-8-sig'))
    return files


def canonical_key(value):
    require(isinstance(value, str), 'token_encryption_key_invalid')
    try:
        raw = base64.b64decode(value, validate=True)
    except (ValueError, TypeError):
        raise InstallError('token_encryption_key_invalid') from None
    require(len(raw) == 32 and base64.b64encode(raw).decode('ascii') == value, 'token_encryption_key_invalid')
    return value


def environment_key(raw):
    try:
        text = raw.decode('utf-8')
    except UnicodeError:
        raise InstallError('environment_encoding_unexpected') from None
    found = []
    for row in text.splitlines():
        name, separator, value = row.partition('=')
        if separator and name.strip().casefold() == KEY_ENV.casefold():
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in ('"', "'"):
                value = value[1:-1]
            found.append(canonical_key(value))
    require(len(found) <= 1, 'duplicate_token_encryption_key')
    return found[0] if found else None


def validate_environment_file(path):
    meta = path.lstat()
    require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
            and meta.st_mode & 0o7777 == 0o600 and path.resolve(strict=True) == path, 'environment_file_identity_unexpected')


def valid_counts(data, keys):
    return isinstance(data, dict) and set(data) == set(keys) and all(type(value) is int and value >= 0 for value in data.values())


def validate_migration(sql):
    require(isinstance(sql, str) and sql.startswith('BEGIN TRANSACTION;\n')
        and sql.endswith('COMMIT;\nGO\n\n') and sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1,
        'incremental_migration_transaction_unexpected')
    require(not re.search(r'(?im)^\s*(DELETE|UPDATE|TRUNCATE|DROP|EXEC(?:UTE)?|USE|GRANT|DENY)\s', sql)
        and re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) == [MIGRATION]
        and re.findall(r'(?im)^\s*ALTER TABLE \[([^]]+)\]', sql) == ['CustomerFrequentProducts']
        and set(re.findall(r'(?im)^\s*CREATE TABLE \[([^]]+)\]', sql)) == {'CustomerProductSetupRequests','CustomerProductSetupOrders'}
        and re.findall(r'(?im)^\s*INSERT INTO \[([^]]+)\]', sql) == ['__EFMigrationsHistory']
        and all(name in sql for name in ('UseCustomerDefaultMachine','DEFAULT CAST(1 AS bit)',
            'CK_ProductSetup_Version','IX_CustomerProductSetupRequests_CustomerId_ProductId'))
        and not re.search(r'\b(TokenHash|ProtectedToken|EncryptionKey)\b', sql), 'incremental_migration_unexpected')
    return sql


def migration_sql_for_sqlcmd(sql):
    """Commit additive DDL, history and minimal grants in one transaction.

    The batch boundary lets SQL Server resolve the new table names before GRANT.
    GO keeps the same sqlcmd connection and transaction. A failed grant therefore
    rolls back both the schema and history, allowing the original preflight again.
    """
    validate_migration(sql)
    tail = 'COMMIT;\nGO\n\n'
    require(re.findall(r'(?mi)^GO[^\r\n]*$', sql) == ['GO'], 'migration_batch_layout_unexpected')
    grants = SETUP_GRANTS.removeprefix('BEGIN TRANSACTION;\n').removesuffix('COMMIT TRANSACTION;\n')
    return sql[:-len(tail)] + 'GO\n\n' + grants + tail


def make_installer(package):
    files = load_payload(package)
    runtime = types.ModuleType('reviewed_i16_deployment_support')
    exec(compile(files['native-deployment-support.py'], 'native-deployment-support.py', 'exec'), runtime.__dict__)
    commercial = types.ModuleType('reviewed_i16_commercial_schema')
    exec(compile(files['commercial-catalog-support.py'], 'commercial-catalog-support.py', 'exec'), commercial.__dict__)
    runtime.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    migrations = runtime.MIGRATIONS + [PRICE_MIGRATION, TOKEN_MIGRATION, MIGRATION]

    class CustomerProductsInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('customer-products-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.token_encryption_key = None
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, tokens_preserved=False, encryption_key_rotated=False, encryption_key_effective_verified=False)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.customer-products-stage-' + uuid.uuid4().hex)
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

        def preflight(self):
            self.state['preflight_step'] = 'directories-and-releases'
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_release_link_required')
                self.old[part] = link.resolve(strict=True)
            own = self.old == self.new
            require(own or all(path == ROOT / part / 'releases' / PREVIOUS[part] for part, path in self.old.items()),
                    'previous_release_not_supported')
            require(all(path.parent == ROOT / part / 'releases' for part, path in self.old.items()), 'previous_release_path_unexpected')
            self.state['preflight_step'] = 'running-service'
            pid, cwd, argv = self.process(self.old['backend'])
            self.original_dll = regular(cwd / 'Prodimt.Pedidos.Api.dll')
            validate_environment_file(ENV_FILE)
            self.environment = regular(ENV_FILE)
            self.token_encryption_key = environment_key(self.environment)
            inspector = types.ModuleType('reviewed_i16_inventory')
            exec(compile(files['inspect-native.py'], 'inspect-native.py', 'exec'), inspector.__dict__)
            inspector.QUERIES = (commercial.PERMISSION_QUERY, commercial.NEW_SCHEMA, commercial.PRICE_PERMISSIONS,
                                 TOKEN_SHAPE, TOKEN_COUNTS, SETUP_SHAPE, SETUP_PERMISSIONS, DATA_FINGERPRINT, inspector.SQL_MIGRATIONS)
            self.inspector = inspector
            self.state['preflight_step'] = 'effective-configuration'
            configuration, source = inspector.configuration(inspector.process_environment(pid), argv, cwd)
            require(source['environment'] == 'Production' and inspector.policy_flags(configuration) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False, 'https_redirection_enabled': True},
                'production_configuration_unexpected')
            effective_key = inspector.first(configuration, KEY_CONFIG, default=None)
            require(self.token_encryption_key is not None and effective_key is not None
                    and canonical_key(effective_key) == self.token_encryption_key, 'token_encryption_key_missing_or_conflicting')
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
            count = self.app_checks['migrations_count']
            rows = inspector.query_sql(self.sql_tool, self.fields, inspector.SQL_MIGRATIONS)
            require(rows == [{'MigrationId': value} for value in migrations[:count]], 'migration_history_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, TOKEN_SHAPE) == EXPECTED_TOKEN_SCHEMA,
                    'token_management_schema_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, SETUP_SHAPE) ==
                    (EXPECTED_SETUP_SCHEMA if count == 14 else dict.fromkeys(EXPECTED_SETUP_SCHEMA, 0)), 'product_setup_schema_unexpected')
            if count == 14:
                require(inspector.query_sql(self.sql_tool, self.fields, SETUP_PERMISSIONS) == EXPECTED_SETUP_PERMISSIONS,
                        'product_setup_permissions_unexpected')
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
            self.state.update(env_preserved=True, encryption_key_present=self.token_encryption_key is not None,
                              schema_shape_verified=True)
            if own:
                self.state['preflight_step'] = 'installed-files-and-effective-key'
                self.verify_stage('backend')
                self.verify_stage('frontend')
                self.health(True)
                self.state.update(status='already_current', phase='completed', health_verified=True, frontend_verified=True,
                                  tokens_preserved=True)
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
            require(self.sql_json('SELECT COUNT_BIG(*) AS invalid FROM dbo.Customers WHERE RestDaysMask NOT BETWEEN 0 AND 127 '
                                  'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == {'invalid': 0}, 'rest_days_data_invalid')
            require(self.sql_json("SELECT COUNT_BIG(*) AS invalid FROM dbo.CustomerFrequentProducts WHERE CaptureAlias IS NOT NULL "
                "AND (LEN(CaptureAlias) NOT BETWEEN 1 AND 16 OR CaptureAlias COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Z]%') "
                'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == {'invalid': 0}, 'capture_alias_data_invalid')
            require(self.sql_json(commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            require(self.sql_json(TOKEN_SCHEMA) == EXPECTED_TOKEN_SCHEMA, 'token_management_schema_unexpected')
            require(self.sql_json(SETUP_SCHEMA) == (EXPECTED_SETUP_SCHEMA if count == 14 else dict.fromkeys(EXPECTED_SETUP_SCHEMA, 0)),
                    'product_setup_schema_unexpected')
            return count

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-i16-' + self.snapshot.name.removeprefix('customer-products-update-'))
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
            require(valid_counts(counts, ('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits',
                                         'profiles', 'products', 'machines')), 'database_counts_unrecognized')
            monetary = self.sql_json(commercial.PRICE_COUNTS)
            tokens = self.sql_json(TOKEN_COUNTS)
            fingerprint = self.sql_json(DATA_FINGERPRINT)
            setup_counts = self.sql_json(SETUP_COUNTS) if before == 14 else dict(requests=0, links=0, open_requests=0,
                inherit_default=counts['profiles'], independent=0)
            require(valid_counts(monetary, ('books','revisions','consolidations','priced_lines'))
                    and valid_counts(tokens, ('tokens','active_tokens','active_legacy_tokens','protected_tokens',
                        'active_protected_tokens','metadata_versions','invalid_metadata','upgrade_audits'))
                    and tokens['invalid_metadata'] == 0, 'protected_data_counts_unrecognized')
            require(isinstance(fingerprint, dict) and set(fingerprint) == {'tokens','prices'}
                    and all(isinstance(x,str) and re.fullmatch(r'[A-F0-9]{64}',x) for x in fingerprint.values()),
                    'protected_data_fingerprint_unrecognized')
            require(valid_counts(setup_counts, ('requests','links','open_requests','inherit_default','independent')),
                    'setup_counts_unrecognized')
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            validate_environment_file(ENV_FILE)
            require(regular(ENV_FILE) == self.environment and environment_key(self.environment) == self.token_encryption_key,
                    'environment_or_token_key_changed')
            if before == 13:
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(migration_sql_for_sqlcmd(files['migrations-i16.sql'].decode('utf-8-sig')), timeout=180)
            require(self.verify_schema() == 14, 'migrated_schema_unexpected')
            require(self.sql_json(runtime.COUNTS_QUERY) == counts and self.sql_json(commercial.PRICE_COUNTS) == monetary
                    and self.sql_json(TOKEN_COUNTS) == tokens and self.sql_json(DATA_FINGERPRINT) == fingerprint
                    and self.sql_json(SETUP_COUNTS) == setup_counts, 'existing_data_changed')
            self.verify_application_permissions()
            self.state.update(migrations_after=14, migrated_schema_verified=True, existing_data_counts_preserved=True,
                price_history_append_only_verified=True, tokens_preserved=True, machine_inheritance_preserved=True,
                setup_permissions_verified=True, encryption_key_snapshot_verified=True)

        def verify_application_permissions(self):
            require(not commercial.permission_mismatches(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PERMISSION_QUERY), (14,)),
                    'application_permissions_changed')
            require(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS,
                    'price_history_permissions_unexpected')
            require(self.inspector.query_sql(self.sql_tool, self.fields, SETUP_PERMISSIONS) == EXPECTED_SETUP_PERMISSIONS,
                    'product_setup_permissions_unexpected')

        def verify_effective_key(self):
            pid, cwd, argv = self.process(self.new['backend'])
            cfg, source = self.inspector.configuration(self.inspector.process_environment(pid), argv, cwd)
            require(source.get('environment') == 'Production' and self.token_encryption_key is not None
                    and self.inspector.first(cfg, KEY_CONFIG, default=None) == self.token_encryption_key,
                    'token_encryption_key_not_effective_in_running_process')
            validate_environment_file(ENV_FILE)
            require(regular(ENV_FILE) == self.environment and environment_key(self.environment) == self.token_encryption_key,
                    'token_encryption_key_changed_after_restart')
            self.state['encryption_key_effective_verified'] = True

        def health(self, new):
            super().health(new)
            if new:
                require(self.http('/api/admin/customers/00000000-0000-0000-0000-000000000000/access-tokens')[0] == 401,
                        'token_management_endpoint_not_protected')
                for endpoint in ('/api/admin/product-setups/count', '/api/admin/product-setups',
                    '/api/admin/customers/00000000-0000-0000-0000-000000000000/additional-products',
                    '/api/customer-orders/00000000-0000-0000-0000-000000000000/additional-products'):
                    require(self.http(endpoint)[0] == 401, 'product_setup_endpoint_not_protected')
                self.verify_effective_key()
                self.verify_application_permissions()
                self.state['frontend_verified'] = True

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.state['phase'] = 'private-snapshot-and-stage'
            self.snapshot = BACKUPS / ('customer-products-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
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
            if self.stop_requested:
                super().rollback()
                self.state.update(service_restarted=True, encryption_key_retained_after_rollback=True)


    return CustomerProductsInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-customer-products-payload.zip'))
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
