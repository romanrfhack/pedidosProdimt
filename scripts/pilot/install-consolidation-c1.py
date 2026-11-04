#!/usr/bin/env python3
"""Install the integrated consolidation C1 release over the exact installed I23.

Read-only by default. Seven isolated EF/HTTP cases and exact SQL 18-to-19
must pass and be cleaned before staging or stopping the application. A
verified COPY_ONLY backup precedes production DDL. Recovery after DDL is
forward only, with the same pinned package.
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
import shutil
import stat
import subprocess
import sys
import tempfile
import types
import uuid
import zipfile

REVISION = '20261104-consolidation-c1-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
BASELINE_SOURCE = 'aa802e210d15c70784f79dea2580709166b843c7'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
PREVIOUS_PAIRS = (('ordering-schedules-i23-aa802e210d15', 'ordering-schedules-i23-aa802e210d15'),)
SETUP_MIGRATION = '20261102152307_AddCustomerProductSetup'
IDENTITY_MIGRATION = '20261102235000_AddSecurityAdministration'
PRODUCTION_MIGRATION = '20261103010000_AddProductionAllocation'
DELIVERY_MIGRATION = '20261103020000_AddDeliveryOperations'
SCHEDULE_MIGRATION = '20261103030000_AddOrderingSchedules'
MIGRATION = '20261104041928_ExpandAuditMetadata'
SUPPORT_FILES = ('inspect-native.py', 'native-deployment-support.py', 'commercial-catalog-support.py',
    'token-management-support.py', 'customer-products-support.py', 'security-administration-support.py',
    'delivery-support.py', 'delivery-schema-contract.py', 'ordering-schedules-support.py', 'validate-consolidation-c1-sql.py')
SQL_FILES = ('migrations-c1.sql', 'rollback-c1.sql', 'validation-baseline.sql', 'delivery-schema.json')
VALIDATION_REQUIRED = ('validation/Prodimt.Pedidos.DeploymentValidation.dll',
    'validation/Prodimt.Pedidos.DeploymentValidation.runtimeconfig.json', 'validation/Prodimt.Pedidos.DeploymentValidation.deps.json')
VALIDATED_ASSEMBLIES = ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Application.dll',
    'Prodimt.Pedidos.Domain.dll', 'Prodimt.Pedidos.Infrastructure.dll')

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


def root_regular(path, limit):
    raw = regular(path, limit)
    require(path.lstat().st_uid == 0, 'root_owned_file_required')
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


def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path, 100 * 1024 * 1024)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 1000 and len({row.filename for row in rows}) == len(rows)
                and sum(row.file_size for row in rows) <= 200 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and not stat.S_ISLNK(row.external_attr >> 16) for row in rows), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in rows}
    require('manifest.json' in files, 'payload_manifest_missing')
    manifest = json.loads(files.pop('manifest.json'))
    required = set(SUPPORT_FILES + SQL_FILES + VALIDATION_REQUIRED) | {'backend/Prodimt.Pedidos.Api.dll',
        'backend/Prodimt.Pedidos.Api.runtimeconfig.json', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'consolidation-c1-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('baselineSource') == BASELINE_SOURCE and manifest.get('migration') == MIGRATION
            and manifest.get('migrationsBefore') == 18 and manifest.get('migrationsAfter') == 19
            and manifest.get('previousPairs') == [list(pair) for pair in PREVIOUS_PAIRS]
            and manifest.get('encryptionKeyInPackage') is False and manifest.get('revokeTokens') is False
            and set(manifest.get('files', {})) == set(files) and required <= set(files)
            and all(name in required or name.startswith(('backend/', 'frontend/', 'validation/')) for name in files),
            'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()),
            'payload_file_hash_mismatch')
    require(all('backend/' + name in files and 'validation/' + name in files
                and files['backend/' + name] == files['validation/' + name] for name in VALIDATED_ASSEMBLIES),
            'validation_application_binary_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12', '.trace', '.dmp', '.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    validation = module('c1_payload_sql_validator', files['validate-consolidation-c1-sql.py'])
    require(manifest.get('migrationIds') == validation.BASE_IDS + [MIGRATION], 'payload_migration_identity_unexpected')
    validation.validate_migration(files['migrations-c1.sql'].decode('utf-8-sig'))
    validation.validate_rollback(files['rollback-c1.sql'].decode('utf-8-sig'))
    validation.validate_baseline(files['validation-baseline.sql'].decode('utf-8-sig'))
    return files


def run_portable_validation(files, dotnet, connection, clean_environment, cleanup_database, delivery):
    """Execute exact C1 application binaries without SDK or operational env."""
    target = delivery.validated_dotnet_executable(dotnet)
    with tempfile.TemporaryDirectory(prefix='prodimt-c1-validation-') as folder:
        directory = Path(folder)
        directory.chmod(0o700)
        for name, raw in files.items():
            if not name.startswith('validation/'):
                continue
            require(safe_name(name), 'validation_file_path_invalid')
            path = directory / name
            path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
            fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'wb') as stream:
                stream.write(raw)
        assembly = directory / VALIDATION_REQUIRED[0]
        environment = dict(clean_environment, PRODIMT_SQL_TEST_CONNECTION=connection,
            DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
        output, issue, returncode = '', None, None
        try:
            with subprocess.Popen([str(target), 'exec', str(assembly), '--validate-c1'], cwd=assembly.parent,
                    stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, env=environment) as child:
                try:
                    output, _ = child.communicate(timeout=600)
                    returncode = child.returncode
                except (subprocess.TimeoutExpired, KeyboardInterrupt, InstallError):
                    child.kill()
                    output, _ = child.communicate(timeout=15)
                    issue = 'portable_validation_interrupted_or_timed_out'
        except (OSError, subprocess.TimeoutExpired, UnicodeError):
            issue = 'portable_validation_process_failed'
        names = list(dict.fromkeys(re.findall(r'(?m)^DATABASE (ProdimtI02Test_[a-f0-9]{32})\s*$', output)))
        # Clean every announced disposable database independently even when an earlier cleanup fails.
        failed_cleanup = []
        for name in names:
            try:
                if cleanup_database(name) is not True:
                    failed_cleanup.append(name)
            except Exception:
                failed_cleanup.append(name)
        if failed_cleanup:
            error = InstallError('portable_validation_database_cleanup_failed')
            error.cleanup_database = failed_cleanup[0]
            raise error from None
        require(issue is None, issue or 'portable_validation_process_failed')
        require(1 <= len(names) <= 2 and len(output) < 1024 * 1024, 'portable_validation_database_marker_unrecognized')
        require(returncode == 0, 'portable_validation_failed')
        try:
            summary = json.loads(output.strip().splitlines()[-1])
        except (ValueError, IndexError):
            raise InstallError('portable_validation_summary_unrecognized') from None
        require(isinstance(summary, dict) and summary.get('schema') == 'prodimt.consolidation.c1.validation.v1'
                and summary.get('status') == 'passed' and summary.get('passed') == 7
                and summary.get('expected') == 7 and summary.get('cleanupVerified') is True,
                'portable_validation_incomplete')
        return dict(status='passed', passed=7, expected=7, cleanupVerified=True)


def make_installer(package):
    files = load_payload(package)
    runtime = module('c1_native_support', files['native-deployment-support.py'])
    commercial = module('c1_commercial_support', files['commercial-catalog-support.py'])
    token = module('c1_token_support', files['token-management-support.py'])
    setup = module('c1_setup_support', files['customer-products-support.py'])
    security = module('c1_security_support', files['security-administration-support.py'])
    delivery = module('c1_delivery_support', files['delivery-support.py'])
    schedules = module('c1_schedule_contracts', files['ordering-schedules-support.py'])
    schema = module('c1_delivery_schema', files['delivery-schema-contract.py'])
    validation = module('c1_exact_sql_validation', files['validate-consolidation-c1-sql.py'])
    contract = json.loads(files['delivery-schema.json'])
    for support in (runtime, commercial, token, setup, security, delivery, schedules):
        support.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    migrations = runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION, SETUP_MIGRATION,
        IDENTITY_MIGRATION, PRODUCTION_MIGRATION, DELIVERY_MIGRATION, SCHEDULE_MIGRATION, MIGRATION]
    require(len(migrations) == 19 and migrations == validation.BASE_IDS + [MIGRATION], 'migration_contract_invalid')
    known = (migrations[:-1], migrations)

    class C1Installer(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('consolidation-c1-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.recovery = None
            self.marker = BACKUPS / 'consolidation-c1-pending.json'
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
                temporary = target.parent / ('.production-stage-' + uuid.uuid4().hex)
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
            require(type(value.get('migrationVerified')) is bool, 'recovery_phase_invalid')
            require(value.get('source') == EXPECTED_SOURCE and value.get('payload') == EXPECTED_PAYLOAD_SHA256, 'different_update_recovery_pending')
            snapshot = Path(value.get('snapshot', ''))
            require(snapshot.parent == BACKUPS and re.fullmatch(r'consolidation-c1-update-[0-9]{8}T[0-9]{6}Z-[a-f0-9]{12}', snapshot.name), 'recovery_snapshot_invalid')
            runtime.directory_safe(snapshot)
            require(snapshot.stat().st_mode & 0o7777 == 0o700, 'recovery_snapshot_mode_invalid')
            require(tuple(value.get('previous', [])) in PREVIOUS_PAIRS and value.get('historyBefore') in known[:-1], 'recovery_base_invalid')
            require(private(snapshot / 'api.env') == regular(ENV_FILE), 'recovery_environment_changed')
            return value

        def write_marker(self):
            value = dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256, snapshot=str(self.snapshot),
                         previous=[self.old[part].name for part in ('backend', 'frontend')], historyBefore=self.history_before, browserGateSha256=self.browser_gate_sha256,
                         migrationVerified=self.state.get('migrated_schema_verified') is True
                         or bool(self.recovery and self.recovery.get('migrationVerified') is True))
            temporary = self.marker.with_name('.consolidation-c1-pending-' + uuid.uuid4().hex)
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'w') as stream:
                json.dump(value, stream)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.marker)
            directory_fd = os.open(BACKUPS, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)

        def schemas(self, reader, strict=False):
            ids = self.history(reader)
            require(reader(commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            require(reader(setup.TOKEN_SCHEMA if strict else setup.TOKEN_SHAPE) == setup.EXPECTED_TOKEN_SCHEMA, 'token_management_schema_unexpected')
            require(reader(setup.SETUP_SCHEMA if strict else setup.SETUP_SHAPE) == setup.EXPECTED_SETUP_SCHEMA, 'product_setup_schema_unexpected')
            require(reader(security.SECURITY_SCHEMA) == security.EXPECTED_SECURITY_SCHEMA, 'security_schema_unexpected')
            require(reader(schedules.PRODUCTION_SCHEMA if strict else schedules.PRODUCTION_SHAPE) == schedules.EXPECTED_PRODUCTION_SCHEMA, 'production_schema_unexpected')
            require(reader(schedules.SCHEDULE_SHAPE) == schedules.EXPECTED_SCHEDULE_SHAPE, 'ordering_schedule_schema_unexpected')
            require(schema.matches(reader(schema.schema_query(strict)), contract, strict), 'delivery_schema_unexpected')
            expected = validation.EXPECTED_AUDIT_SHAPE_AFTER if MIGRATION in ids else validation.EXPECTED_AUDIT_SHAPE_BEFORE
            require(reader(validation.AUDIT_SHAPE) == expected, 'audit_metadata_schema_unexpected')
            return ids

        def app_permissions(self, ids):
            read = lambda query: self.inspector.query_sql(self.sql_tool, self.fields, query)
            require(not commercial.permission_mismatches(read(commercial.PERMISSION_QUERY), (len(ids),)), 'application_permissions_unexpected')
            for query, expected, issue in (
                    (commercial.PRICE_PERMISSIONS, commercial.EXPECTED_PRICE_PERMISSIONS, 'price_history_permissions_unexpected'),
                    (setup.SETUP_PERMISSIONS, setup.EXPECTED_SETUP_PERMISSIONS, 'setup_permissions_unexpected'),
                    (security.SECURITY_PERMISSIONS, security.EXPECTED_SECURITY_PERMISSIONS, 'security_permissions_unexpected'),
                    (schedules.PRODUCTION_SQL_PERMISSIONS, schedules.EXPECTED_PRODUCTION_PERMISSIONS, 'production_permissions_unexpected'),
                    (delivery.DELIVERY_SQL_PERMISSIONS, delivery.EXPECTED_DELIVERY_PERMISSIONS, 'delivery_permissions_unexpected'),
                    (schedules.SCHEDULE_PERMISSIONS, schedules.EXPECTED_SCHEDULE_PERMISSIONS, 'ordering_schedule_permissions_unexpected'),
                    (validation.AUDIT_PERMISSIONS, validation.EXPECTED_AUDIT_PERMISSIONS, 'audit_permissions_unexpected')):
                require(read(query) == expected, issue)

        def preflight(self):
            for name in ('production-pending.json', 'delivery-pending.json', 'ordering-schedules-pending.json', 'ordering-schedules-i23-pending.json'):
                path = BACKUPS / name
                require(not path.exists() and not path.is_symlink(), 'different_update_recovery_pending')
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
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
                security.SECURITY_SCHEMA, security.SECURITY_PERMISSIONS, schedules.PRODUCTION_SCHEMA, schedules.PRODUCTION_SHAPE, schedules.PRODUCTION_SQL_PERMISSIONS,
                schedules.SCHEDULE_SHAPE, schedules.SCHEDULE_PERMISSIONS, validation.AUDIT_SHAPE, validation.AUDIT_PERMISSIONS, schema.schema_query(True), schema.schema_query(False),
                delivery.DELIVERY_SQL_PERMISSIONS, self.inspector.SQL_MIGRATIONS)
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
            if self.recovery and self.recovery.get('migrationVerified') is True:
                require(ids == migrations, 'verified_recovery_schema_changed')
            if not self.recovery:
                expected = migrations if own else migrations[:-1]
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
            executable = argv[0] if Path(argv[0]).is_absolute() else shutil.which(argv[0], path=runtime.CLEAN_ENV['PATH'])
            require(executable is not None, 'validation_dotnet_runtime_unavailable')
            self.dotnet_executable = executable
            self.state.update(base_release_verified=True, env_preserved=True, encryption_key_present=True,
                              migration_ids_before=ids, migrations_before=len(ids))
            if self.recovery:
                self.state.update(status='recovery_pending', phase='completed', recovery_pending=True, private_snapshot=str(self.snapshot))
            else:
                code, body = self.http('/health/db')
                require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
                require(self.http('/admin/operacion') == (200, regular(self.old['frontend'] / 'index.html')), 'previous_frontend_not_served')
                require(self.http('/api/admin/customers')[0] == 401, 'admin_authorization_unexpected')
                self.state['health_verified'] = True
                if own:
                    self.verify_stage('backend')
                    self.verify_stage('frontend')
                    self.health(True)
                    self.state.update(status='already_current', phase='completed', frontend_verified=True, tokens_preserved=True, migrations_after=19)

        def verify_schema(self):
            ids = self.schemas(self.sql_json, strict=True)
            require(self.sql_json(runtime.SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1), 'rest_days_schema_unexpected')
            require(self.sql_json(runtime.ALIAS_SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1), 'capture_alias_schema_unexpected')
            return len(ids)

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-c1-' + uuid.uuid4().hex)
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
            if self.recovery and self.recovery.get('migrationVerified') is True:
                # The C1 process may already have accepted legitimate writes before a health failure.
                # Preservation was durably verified before first activation; do not compare later
                # business state to that historical snapshot or run DDL again.
                require(ids == migrations and self.verify_schema() == 19, 'verified_recovery_schema_changed')
                self.app_permissions(migrations)
                self.record_migration_verified()
                self.state['migration_recovery_already_verified'] = True
                return
            observed = self.preservation()
            record = self.snapshot / 'business-preservation.json'
            if record.exists() or record.is_symlink():
                before = json.loads(private(record))
                require(observed == before, 'existing_business_data_changed_since_snapshot')
            else:
                require(ids == migrations[:-1], 'recovery_preservation_snapshot_required')
                before = observed
                fd = os.open(record, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
                with os.fdopen(fd, 'w') as stream:
                    json.dump(before, stream)
                    stream.flush()
                    os.fsync(stream.fileno())
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            require(regular(ENV_FILE) == self.environment and token.environment_key(self.environment) == self.token_encryption_key,
                    'configuration_changed_before_migration')
            if MIGRATION not in ids:
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(files['migrations-c1.sql'].decode('utf-8-sig'), timeout=180)
            require(self.history(self.sql_json) == migrations and self.verify_schema() == 19, 'migrated_schema_unexpected')
            require(self.preservation() == before, 'existing_business_data_changed')
            self.app_permissions(migrations)
            self.record_migration_verified()

        def record_migration_verified(self):
            self.state.update(migrations_after=19, migrated_schema_verified=True, existing_data_counts_preserved=True,
                audit_metadata_preserved=True, price_history_append_only_verified=True, tokens_preserved=True,
                security_permissions_verified=True, production_permissions_verified=True, delivery_permissions_verified=True,
                ordering_schedule_permissions_verified=True, existing_delivery_preserved=True)

        def preservation(self):
            queries = (runtime.COUNTS_QUERY, commercial.PRICE_COUNTS, token.TOKEN_COUNTS,
                       setup.DATA_FINGERPRINT, setup.SETUP_COUNTS, schedules.SECURITY_FINGERPRINT,
                       schedules.DELIVERY_FINGERPRINT, validation.PRESERVATION_QUERY)
            return [self.sql_json(query) for query in queries]

        def health(self, new):
            super().health(new)
            if new:
                require(self.http('/api/admin/ordering-schedules')[0] == 401 and self.http('/api/admin/security')[0] == 401 and self.http('/api/admin/security/me')[0] == 401 and self.http('/api/admin/operations/production/')[0] == 401
                        and self.http('/api/admin/delivery')[0] == 401 and self.http('/api/delivery')[0] == 401,
                        'security_endpoint_not_protected')
                pid, cwd, argv = self.process(self.new['backend'])
                cfg, source = self.inspector.configuration(self.inspector.process_environment(pid), argv, cwd)
                require(source.get('environment') == 'Production'
                        and self.inspector.first(cfg, token.KEY_CONFIG, default=None) == self.token_encryption_key
                        and regular(ENV_FILE) == self.environment, 'configuration_or_token_key_changed')
                self.state.update(frontend_verified=True, encryption_key_effective_verified=True)

        def validate_before_apply(self):
            self.state['phase'] = 'isolated-http-and-sql-validation'
            connection = delivery.validation_connection(runtime.SERVER, self.sql_login, self.sql_password)
            def cleanup(database):
                return validation.cleanup_generated_database(self.sql_tool, runtime.SERVER, self.sql_login,
                    self.sql_password, runtime.OPTIONS, runtime.CLEAN_ENV, self.inspector.parse_sql_output, database)
            try:
                portable = run_portable_validation(files, self.dotnet_executable, connection, runtime.CLEAN_ENV, cleanup, delivery)
            except InstallError as error:
                if getattr(error, 'cleanup_database', None):
                    self.state['validation_database_cleanup_required'] = error.cleanup_database
                raise
            self.state['portable_validation'] = portable
            self.state['phase'] = 'isolated-exact-sql-validation'
            try:
                result = validation.validate_exact_sql(files, self.sql_tool, runtime.SERVER, self.sql_login,
                    self.sql_password, runtime.OPTIONS, runtime.CLEAN_ENV, self.inspector.parse_sql_output)
            except validation.ValidationFailure as error:
                if getattr(error, 'cleanup_database', None):
                    self.state['validation_database_cleanup_required'] = error.cleanup_database
                self.state['validation_sql_error_numbers'] = list(getattr(error, 'sql_error_numbers', ()))
                if getattr(error, 'phase', None):
                    self.state['validation_sql_phase'] = error.phase
                raise InstallError(str(error)) from None
            require(isinstance(result, dict) and result.get('status') == 'passed' and all(
                result.get(key) is True for key in ('exactSql', 'rollbackVerified', 'replayVerified',
                    'applicationPermissions', 'preservationVerified', 'cleanupVerified')), 'exact_sql_validation_incomplete')
            self.state.update(exact_sql_validation=result, isolated_validation_verified=True, validation_databases_cleaned=True)

        def verify_browser_gate(self):
            path = getattr(self, 'browser_gate_path', Path('/root/prodimt-c1-browser-gate.json'))
            raw = root_regular(path, 65536)
            try:
                gate = json.loads(raw)
            except (ValueError, UnicodeError):
                raise InstallError('browser_gate_unrecognized') from None
            require(isinstance(gate, dict) and gate.get('schema') == 'prodimt.c1.browser-gate.v1'
                    and gate.get('status') == 'passed' and gate.get('sourceCommit') == EXPECTED_SOURCE
                    and gate.get('api') == 'mock' and gate.get('suite') == 'all' and gate.get('expectedCases') == 240
                    and gate.get('passed') == 240 and gate.get('collected') == 240 and gate.get('exitCode') == 0
                    and gate.get('sqlServerIncluded') is False and gate.get('humanAcceptance') is False
                    and re.fullmatch(r'[a-f0-9]{64}', str(gate.get('reportSha256', '')))
                    and all(gate.get(key) == 0 for key in ('failed', 'skipped', 'flaky')), 'browser_gate_incomplete')
            self.browser_gate_sha256 = sha(raw)
            # A repeated successful run for the same pinned source has a new timestamp/report hash.
            # Source and payload identity, rather than evidence bytes, govern forward recovery.
            self.state.update(browser_gate_verified=True, browser_tests_passed=240, browser_api='mock')

        def validate_only(self):
            self.preflight()
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.validate_before_apply()
            self.state.update(status='validated', phase='completed', changes_made=False,
                              production_changes_made=False)

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'browser-gate'
            self.verify_browser_gate()
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.validate_before_apply()
            if not self.recovery:
                self.snapshot = BACKUPS / ('consolidation-c1-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
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
            # Persist the completed preservation boundary before public C1 can receive writes.
            self.write_marker()
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
                safe = self.schemas(self.sql_json, strict=True) == self.history_before and not self.recovery
            except (Exception, KeyboardInterrupt):
                safe = False
            if not safe:
                self.state.update(recovery_manual_required=True, recovery_pending=True, application_stopped_for_security=True,
                                  database_automatically_restored=False)
                return
            super().rollback()
            self.marker.unlink(missing_ok=True)
            self.state.update(previous_health_verified=True, recovery_pending=False, service_restarted=True)

    return C1Installer()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--browser-gate', type=Path, default=Path('/root/prodimt-c1-browser-gate.json'))
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-consolidation-c1-payload.zip'))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--apply', action='store_true')
    mode.add_argument('--validate-only', action='store_true')
    args = parser.parse_args()
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    installer = make_installer(args.package)
    installer.browser_gate_path = args.browser_gate
    installer.state['mode'] = 'validate-only' if args.validate_only else 'apply' if args.apply else 'read-only'
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
            if args.validate_only:
                installer.validate_only()
            elif args.apply:
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
        return 0 if installer.state['status'] in ('planned', 'updated', 'already_current', 'recovery_pending', 'validated') else 1
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
