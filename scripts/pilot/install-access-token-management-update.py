#!/usr/bin/env python3
"""Install I12 on exact I11 with verified SQL backup and a persistent recovery key.

Legacy revocation is authorized, audited and atomic. Application rollback never
restores SQL or removes/rotates the dedicated key retained in api.env.
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

REVISION = '20261101-access-token-management-install2'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
PREVIOUS = 'prices-148a52dd7428'
PRICE_MIGRATION = '20261101154618_AddSharedProductsAndCustomerPrices'
MIGRATION = '20261101190000_AddRecoverableCustomerAccessTokens'
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
TOKEN_BASE_COUNTS = """SELECT
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens) AS tokens,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1) AS active_tokens,
 (SELECT COUNT_BIG(*) FROM dbo.AuditLogs WHERE EntityType=N'CustomerAccessToken'
  AND EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade') AS upgrade_audits
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
CLEANUP_LEGACY_SQL = r"""
IF DB_NAME()<>N'ProdimtPedidos' THROW 51120,'token_upgrade_target_mismatch',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WITH (HOLDLOCK))<>13
  OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261101190000_AddRecoverableCustomerAccessTokens')
  THROW 51121,'token_upgrade_requires_thirteen_migrations',1;
 DECLARE @revoked TABLE(Id uniqueidentifier NOT NULL);
 UPDATE dbo.CustomerAccessTokens WITH (UPDLOCK,HOLDLOCK)
 SET IsActive=0,MetadataVersion=MetadataVersion+1
 OUTPUT inserted.Id INTO @revoked(Id)
 WHERE IsActive=1 AND ProtectedToken IS NULL;
 INSERT INTO dbo.AuditLogs(Id,EntityType,EntityId,EventType,OccurredAt,ActorType,ActorId,ActorDisplayName,Summary,MetadataJson)
 SELECT NEWID(),N'CustomerAccessToken',CONVERT(nvarchar(36),Id),N'CustomerAccessTokenRevokedForRecoveryUpgrade',
  TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),2,N'i12-token-upgrade',N'Mantenimiento PRODIMT',
  N'Acceso anterior revocado para habilitar gestión segura de tokens.',
  N'{"method":"native-access-token-management","revision":"20261101-access-token-management-install1","reason":"legacy-only-catchup"}'
 FROM @revoked;
 IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @revoked) THROW 51122,'token_upgrade_audit_count_mismatch',1;
 DECLARE @count int=(SELECT COUNT(*) FROM @revoked);
 IF EXISTS(SELECT 1 FROM dbo.CustomerAccessTokens WHERE IsActive=1 AND ProtectedToken IS NULL)
  THROW 51123,'active_legacy_accesses_remain',1;
 COMMIT TRANSACTION;
 SELECT (SELECT @count AS revoked,1 AS transaction_committed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS ProdimtInspectionJson;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
"""


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
    required = {'inspect-native.py', 'native-deployment-support.py', 'commercial-catalog-support.py', 'migrations-i12.sql',
                'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'access-token-management-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 12 and manifest.get('migrationsAfter') == 13
            and manifest.get('previousRelease') == PREVIOUS and manifest.get('revokeLegacyTokens') is True
            and manifest.get('encryptionKeyInPackage') is False and set(manifest.get('files', {})) == set(files)
            and required <= set(files) and all(name in required or name.startswith(('backend/', 'frontend/')) for name in files),
            'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12', '.trace', '.dmp', '.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    sql = files['migrations-i12.sql'].decode('utf-8-sig')
    require(MIGRATION in sql and all(name in sql for name in ('ProtectedToken', 'MetadataVersion', 'IsReminderLink',
            'CustomerAccessTokenRevokedForRecoveryUpgrade')) and not re.search(r'(?im)^\s*(DELETE|TRUNCATE|DROP|EXEC(?:UTE)?)\s', sql),
            'incremental_sql_unexpected')
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


def migration_sql_for_sqlcmd(sql):
    """Split only the reviewed I12 DDL/DML boundary, keeping its one transaction.

    SQL Server compiles the original script's UPDATE before its ALTERs execute.
    sqlcmd's GO submits a second batch on the same connection/transaction, after
    the new columns exist. Reject unfamiliar layouts rather than splitting SQL
    generally or changing any statement from the verified payload.
    """
    require(isinstance(sql, str), 'migration_batch_layout_unexpected')
    guard = ("IF NOT EXISTS (\n    SELECT * FROM [__EFMigrationsHistory]\n"
             "    WHERE [MigrationId] = N'" + MIGRATION + "'\n)\nBEGIN\n")
    additions = (
        'ALTER TABLE [CustomerAccessTokens] ADD [IsReminderLink] bit NOT NULL DEFAULT CAST(0 AS bit);',
        'ALTER TABLE [CustomerAccessTokens] ADD [MetadataVersion] int NOT NULL DEFAULT 1;',
        'ALTER TABLE [CustomerAccessTokens] ADD [ProtectedToken] nvarchar(1024) NULL;',
    )
    prefix = 'BEGIN TRANSACTION;\n' + '\n'.join(guard + '    ' + statement + '\nEND;\n' for statement in additions) + '\n'
    require(sql.startswith(prefix), 'migration_batch_layout_unexpected')
    tail = sql[len(prefix):]
    already_split = tail.startswith('GO\n\n')
    if already_split:
        tail = tail[len('GO\n\n'):]
    original = prefix + tail
    history_tail = (guard + '    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\n'
                    "    VALUES (N'" + MIGRATION + "', N'10.0.8');\nEND;\n\nCOMMIT;\nGO\n\n")
    require(tail.startswith(guard + '    DECLARE @revoked TABLE (Id uniqueidentifier NOT NULL);\n')
            and original.count(guard) == 5 and original.count('DECLARE @revoked TABLE') == 1
            and original.count('UPDATE dbo.CustomerAccessTokens') == 1
            and original.count('INSERT INTO dbo.AuditLogs') == 1
            and original.count('BEGIN TRANSACTION;') == original.count('COMMIT;') == 1
            and re.findall(r'(?mi)^GO[^\r\n]*$', original) == ['GO']
            and original.endswith('\n' + history_tail), 'migration_batch_layout_unexpected')
    return sql if already_split else prefix + 'GO\n\n' + tail


def make_installer(package):
    files = load_payload(package)
    runtime = types.ModuleType('reviewed_i12_deployment_support')
    exec(compile(files['native-deployment-support.py'], 'native-deployment-support.py', 'exec'), runtime.__dict__)
    commercial = types.ModuleType('reviewed_i12_commercial_schema')
    exec(compile(files['commercial-catalog-support.py'], 'commercial-catalog-support.py', 'exec'), commercial.__dict__)
    runtime.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    migrations = runtime.MIGRATIONS + [PRICE_MIGRATION, MIGRATION]

    class AccessTokenManagementInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('access-tokens-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.token_encryption_key = None
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, legacy_tokens_verified=False, encryption_key_created=False,
                encryption_key_rotated=False, encryption_key_effective_verified=False)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.access-token-stage-' + uuid.uuid4().hex)
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
            require(own or all(path == ROOT / part / 'releases' / PREVIOUS for part, path in self.old.items()),
                    'previous_release_not_supported')
            require(all(path.parent == ROOT / part / 'releases' for part, path in self.old.items()), 'previous_release_path_unexpected')
            self.state['preflight_step'] = 'running-service'
            pid, cwd, argv = self.process(self.old['backend'])
            self.original_dll = regular(cwd / 'Prodimt.Pedidos.Api.dll')
            validate_environment_file(ENV_FILE)
            self.environment = regular(ENV_FILE)
            self.token_encryption_key = environment_key(self.environment)
            inspector = types.ModuleType('reviewed_i12_inventory')
            exec(compile(files['inspect-native.py'], 'inspect-native.py', 'exec'), inspector.__dict__)
            inspector.QUERIES = (commercial.PERMISSION_QUERY, commercial.NEW_SCHEMA, commercial.PRICE_PERMISSIONS,
                                 TOKEN_SCHEMA, TOKEN_COUNTS, TOKEN_BASE_COUNTS)
            self.inspector = inspector
            self.state['preflight_step'] = 'effective-configuration'
            configuration, source = inspector.configuration(inspector.process_environment(pid), argv, cwd)
            require(source['environment'] == 'Production' and inspector.policy_flags(configuration) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False, 'https_redirection_enabled': True},
                'production_configuration_unexpected')
            effective_key = inspector.first(configuration, KEY_CONFIG, default=None)
            if effective_key is not None:
                require(canonical_key(effective_key) == self.token_encryption_key, 'token_encryption_key_configuration_conflict')
            self.fields = inspector.connection_fields(inspector.first(configuration, 'ConnectionStrings:Pedidos',
                                                                       'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(self.fields), 'sql_target_unexpected')
            self.sql_tool = next((tool for tool in inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool, 'sqlcmd_required')
            self.state['preflight_step'] = 'application-sql-read'
            self.app_checks = inspector.query_sql(self.sql_tool, self.fields, commercial.PERMISSION_QUERY)
            mismatch = commercial.permission_mismatches(self.app_checks, (13,) if own else (12, 13))
            self.state['database_checks'] = {key: value for key, value in self.app_checks.items()
                                            if key not in ('database_name', 'application_user')}
            self.state['database_mismatches'] = mismatch
            require(not mismatch, 'database_permissions_schema_or_channels_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA,
                    'commercial_schema_unexpected')
            require(inspector.query_sql(self.sql_tool, self.fields, commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS,
                    'price_history_permissions_unexpected')
            schema = inspector.query_sql(self.sql_tool, self.fields, TOKEN_SCHEMA)
            count = self.app_checks['migrations_count']
            require(schema == (EXPECTED_TOKEN_SCHEMA if count == 13 else dict.fromkeys(EXPECTED_TOKEN_SCHEMA, 0)),
                    'token_management_schema_unexpected')
            if count == 13:
                require(self.token_encryption_key is not None, 'token_encryption_key_missing_for_migrated_database')
                data = inspector.query_sql(self.sql_tool, self.fields, TOKEN_COUNTS)
                require(valid_counts(data, ('tokens', 'active_tokens', 'active_legacy_tokens', 'protected_tokens',
                    'active_protected_tokens', 'metadata_versions', 'invalid_metadata', 'upgrade_audits')) and data['invalid_metadata'] == 0,
                    'token_management_data_unexpected')
                self.state['legacy_cleanup_required'] = data['active_legacy_tokens'] > 0
            else:
                data = inspector.query_sql(self.sql_tool, self.fields, TOKEN_BASE_COUNTS)
                require(valid_counts(data, ('tokens', 'active_tokens', 'upgrade_audits')), 'token_counts_unrecognized')
                self.state['legacy_tokens_pending_revocation'] = data['active_tokens']
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
            self.state.update(env_preserved=True, encryption_key_present=self.token_encryption_key is not None)
            if own and not self.state.get('legacy_cleanup_required'):
                self.state['preflight_step'] = 'installed-files-and-effective-key'
                self.verify_stage('backend')
                self.verify_stage('frontend')
                self.health(True)
                self.state.update(status='already_current', phase='completed', health_verified=True, frontend_verified=True,
                                  legacy_tokens_verified=True)
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
            require(self.sql_json(TOKEN_SCHEMA) == (EXPECTED_TOKEN_SCHEMA if count == 13 else dict.fromkeys(EXPECTED_TOKEN_SCHEMA, 0)),
                    'token_management_schema_unexpected')
            return count

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-i12-' + self.snapshot.name.removeprefix('access-token-management-update-'))
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

        def prepare_encryption_key(self):
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            validate_environment_file(ENV_FILE)
            runtime.directory_safe(ENV_FILE.parent)
            require(regular(ENV_FILE) == self.environment, 'environment_changed_before_key_persistence')
            existing = environment_key(self.environment)
            require(existing == self.token_encryption_key, 'token_encryption_key_changed')
            key = existing or base64.b64encode(secrets.token_bytes(32)).decode('ascii')
            canonical_key(key)
            if existing is None:
                appended = self.environment + (b'' if self.environment.endswith(b'\n') else b'\n') + (KEY_ENV + '=' + key + '\n').encode('ascii')
                temporary = ENV_FILE.with_name('.api.env.i12-' + uuid.uuid4().hex)
                try:
                    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
                    with os.fdopen(fd, 'wb') as stream:
                        stream.write(appended)
                        stream.flush()
                        os.fsync(stream.fileno())
                    os.chown(temporary, 0, 0)
                    temporary.chmod(0o600)
                    require(regular(ENV_FILE) == self.environment, 'environment_changed_before_key_persistence')
                    os.replace(temporary, ENV_FILE)
                    directory_fd = os.open(ENV_FILE.parent, os.O_RDONLY | os.O_DIRECTORY)
                    try:
                        os.fsync(directory_fd)
                    finally:
                        os.close(directory_fd)
                    self.environment = appended
                finally:
                    if temporary.exists():
                        temporary.unlink()
                self.state['encryption_key_created'] = True
            self.token_encryption_key = key
            validate_environment_file(ENV_FILE)
            require(regular(ENV_FILE) == self.environment and environment_key(self.environment) == key, 'token_encryption_key_persistence_failed')
            for name, raw in (('api.env.with-token-key', self.environment), ('customer-access-token-encryption.key', (key + '\n').encode('ascii'))):
                path = self.snapshot / name
                fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
                with os.fdopen(fd, 'wb') as stream:
                    stream.write(raw)
                    stream.flush()
                    os.fsync(stream.fileno())
                path.chmod(0o600)
            directory_fd = os.open(self.snapshot, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)
            self.state.update(encryption_key_present=True, encryption_key_snapshot_verified=True, env_preserved=True)

        def backup_and_migrate(self):
            before = self.verify_schema()
            require(before == self.state['migrations_before'], 'database_changed_during_staging')
            counts = self.sql_json(runtime.COUNTS_QUERY)
            require(valid_counts(counts, ('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits',
                                         'profiles', 'products', 'machines')), 'database_counts_unrecognized')
            monetary = self.sql_json(commercial.PRICE_COUNTS)
            require(valid_counts(monetary, ('books', 'revisions', 'consolidations', 'priced_lines')), 'commercial_counts_unrecognized')
            tokens = self.sql_json(TOKEN_COUNTS if before == 13 else TOKEN_BASE_COUNTS)
            token_fields = ('tokens', 'active_tokens', 'upgrade_audits') if before == 12 else (
                'tokens', 'active_tokens', 'active_legacy_tokens', 'protected_tokens', 'active_protected_tokens',
                'metadata_versions', 'invalid_metadata', 'upgrade_audits')
            require(valid_counts(tokens, token_fields), 'token_counts_unrecognized')
            require(before == 12 or tokens['invalid_metadata'] == 0, 'token_management_data_unexpected')
            revoked = tokens['active_tokens'] if before == 12 else tokens['active_legacy_tokens']
            migration_sql = migration_sql_for_sqlcmd(files['migrations-i12.sql'].decode('utf-8-sig')) if before == 12 else None
            self.database_backup()
            self.prepare_encryption_key()
            if before == 12:
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(migration_sql, timeout=180)
            require(self.verify_schema() == 13, 'migrated_schema_unexpected')
            result = self.inspector.parse_sql_output(self.sql_run(CLEANUP_LEGACY_SQL))
            require(valid_counts(result, ('revoked', 'transaction_committed')) and result['transaction_committed'] == 1,
                    'legacy_cleanup_result_unrecognized')
            require(result['revoked'] == (0 if before == 12 else revoked), 'legacy_cleanup_revocation_count_unexpected')
            expected_counts = counts | {'audits': counts['audits'] + revoked}
            require(self.sql_json(runtime.COUNTS_QUERY) == expected_counts and self.sql_json(commercial.PRICE_COUNTS) == monetary,
                    'existing_data_counts_changed')
            after = self.sql_json(TOKEN_COUNTS)
            expected_tokens = dict(tokens=tokens['tokens'], active_tokens=tokens['active_tokens'] - revoked,
                active_legacy_tokens=0, protected_tokens=0 if before == 12 else tokens['protected_tokens'],
                active_protected_tokens=0 if before == 12 else tokens['active_protected_tokens'],
                metadata_versions=(tokens['tokens'] if before == 12 else tokens['metadata_versions']) + revoked,
                invalid_metadata=0, upgrade_audits=tokens['upgrade_audits'] + revoked)
            require(after == expected_tokens, 'legacy_revocation_counts_unexpected')
            require(not commercial.permission_mismatches(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PERMISSION_QUERY), (13,)),
                    'application_permissions_changed')
            require(self.inspector.query_sql(self.sql_tool, self.fields, commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS,
                    'price_history_permissions_unexpected')
            self.state.update(migrations_after=13, migrated_schema_verified=True, existing_data_counts_preserved=True,
                              price_history_append_only_verified=True, legacy_tokens_verified=True, legacy_tokens_revoked=revoked)

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
                self.verify_effective_key()
                self.state['frontend_verified'] = True

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.state['phase'] = 'private-snapshot-and-stage'
            self.snapshot = BACKUPS / ('access-token-management-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
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
            self.state['phase'] = 'database-backup-key-and-migration'
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
            # Retain api.env's dedicated key even when returning to I11. New
            # encrypted tokens would otherwise become unrecoverable on retry.
            if self.stop_requested:
                super().rollback()
                self.state.update(service_restarted=True, encryption_key_retained_after_rollback=True)

    return AccessTokenManagementInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-access-token-management-payload.zip'))
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
