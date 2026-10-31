#!/usr/bin/env python3
"""Install I09 on the pinned native I07/I08 without database writes or migrations.

Reuse the reviewed I05 application activation/rollback implementation only after
its exact bytes have passed the pinned I09 payload manifest validation.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import shutil
import signal
import stat
import types
import uuid
import zipfile

REVISION = '20261031-customer-access-install2'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
I07 = 'capture-46fed604732e'
PERMISSION_QUERY = """SELECT DB_NAME() AS database_name,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261028202031_AddCustomerCaptureAliases') AS i07,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'Customer' AND IsInternal=0) AS customer_channel,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture' AND IsInternal=1) AS admin_channel,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'SELECT') AS read_customers,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'UPDATE') AS update_customers,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'SELECT') AS read_tokens,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'INSERT') AS insert_tokens,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'UPDATE') AS update_tokens,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'SELECT') AS read_profiles,
 HAS_PERMS_BY_NAME(N'dbo.Orders',N'OBJECT',N'SELECT') AS read_orders,
 HAS_PERMS_BY_NAME(N'dbo.Orders',N'OBJECT',N'INSERT') AS insert_orders,
 HAS_PERMS_BY_NAME(N'dbo.OrderLines',N'OBJECT',N'INSERT') AS insert_lines,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'SELECT') AS read_operations,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'INSERT') AS insert_operations,
 HAS_PERMS_BY_NAME(N'dbo.OrderAuditLogs',N'OBJECT',N'INSERT') AS insert_order_audit,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'INSERT') AS insert_audit
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_DATABASE = dict(database_name='ProdimtPedidos', migrations_count=11, i07=1, customer_channel=1, admin_channel=1,
                         **{name: 1 for name in ('read_customers', 'update_customers', 'read_tokens', 'insert_tokens',
                            'update_tokens', 'read_profiles', 'read_orders', 'insert_orders', 'insert_lines',
                            'read_operations', 'insert_operations', 'insert_order_audit', 'insert_audit')})


class InstallError(Exception):
    """Only fixed issue codes may be reported."""


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def database_check_report(observed):
    """Keep a finite, non-secret explanation before rejecting preflight."""
    values = observed if isinstance(observed, dict) else {}
    checks = {}
    for name, expected in EXPECTED_DATABASE.items():
        actual = values.get(name)
        check = {'matches_expected': name in values and actual == expected,
                 'present': name in values}
        if name != 'database_name':
            check.update(expected=expected,
                         observed=actual if type(actual) is int and 0 <= actual <= 2147483647 else None)
        checks[name] = check
    return {'checks': checks,
            'mismatched_fields': [name for name, result in checks.items() if not result['matches_expected']],
            'unexpected_field_count': len(set(values) - set(EXPECTED_DATABASE)),
            'result_is_object': isinstance(observed, dict)}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path, limit=1024 * 1024):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_nlink == 1 and meta.st_uid in (0, 33)
                and not meta.st_mode & 0o022, 'file_owner_or_type_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'file_too_large')
    return raw


def safe_name(name):
    path = PurePosixPath(name)
    return (isinstance(name, str) and bool(path.parts) and name not in ('', '.') and not path.is_absolute()
            and path.as_posix() == name and not any(part in ('', '.', '..') for part in path.parts)
            and ':' not in name and '\\' not in name)


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE)
            and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path, 50 * 1024 * 1024)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    import io
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 250 and len({row.filename for row in rows}) == len(rows)
                and sum(row.file_size for row in rows) <= 150 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and not stat.S_ISLNK(row.external_attr >> 16) for row in rows), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in rows}
    manifest = json.loads(files.pop('manifest.json'))
    required = {'inspect-native.py', 'install-password-update.py', 'backend/Prodimt.Pedidos.Api.dll',
                'backend/Prodimt.Pedidos.Api.runtimeconfig.json', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'customer-access-links' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('deliveryCutoff') == '18:00' and manifest.get('businessTimeZone') == 'America/Mexico_City'
            and manifest.get('previousBackendRelease') == I07 and manifest.get('migrationsRequired') == 11
            and set(manifest.get('files', {})) == set(files) and required <= set(files)
            and all(name in ('inspect-native.py', 'install-password-update.py') or name.startswith(('backend/', 'frontend/'))
                    for name in files), 'payload_manifest_unexpected')
    previous = manifest.get('previousFrontendReleases')
    require(isinstance(previous, list) and I07 in previous and 1 <= len(previous) <= 20
            and len(previous) == len(set(previous))
            and all(name == I07 or re.fullmatch(r'client-config-[a-f0-9]{12}', name) for name in previous),
            'previous_frontend_manifest_invalid')
    require(all(meta == {'bytes': len(files[name]), 'sha256': sha(files[name])}
                for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    return files, manifest


def make_installer(package):
    files, manifest = load_payload(package)
    runtime = types.ModuleType('reviewed_i09_activation_runtime')
    exec(compile(files['install-password-update.py'], 'install-password-update.py', 'exec'), runtime.__dict__)
    runtime.InstallError = InstallError
    runtime.EXPECTED_SOURCE = EXPECTED_SOURCE
    runtime.ROOT, runtime.BACKUPS, runtime.ENV_FILE = ROOT, BACKUPS, ENV_FILE
    runtime.SERVICE, runtime.DOMAIN = SERVICE, DOMAIN

    class CustomerAccessInstaller(runtime.Installer):
        def __init__(self):
            super().__init__(package)
            self.files = files
            self.runtime = runtime
            self.new = {part: ROOT / part / 'releases' / ('customer-access-' + EXPECTED_SOURCE[:12])
                        for part in ('backend', 'frontend')}
            self.old = {}
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                              database_migrations_run=False, database_writes_run=False, password_changed=False,
                              service_restarted=False, health_verified=False, frontend_verified=False,
                              secret_values_printed=False)

        def preflight(self):
            for path in (ROOT, BACKUPS, *(ROOT / part for part in self.new),
                         *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            for part in self.new:
                current = ROOT / part / 'current'
                require(current.is_symlink(), 'current_release_not_symlink')
                target = current.resolve(strict=True)
                allowed = ([I07] if part == 'backend' else manifest['previousFrontendReleases'])
                require(target in [ROOT / part / 'releases' / name for name in allowed] + [self.new[part]],
                        'previous_release_not_pinned')
                runtime.directory_safe(target)
                self.old[part] = target
            require(all(self.old[part] == self.new[part] for part in self.new)
                    or all(self.old[part] != self.new[part] for part in self.new), 'mixed_application_releases')
            self.links(self.old)
            pid, cwd, argv = self.process(self.old['backend'])
            self.environment = runtime.regular(ENV_FILE)
            inspector = types.ModuleType('reviewed_i09_inventory')
            exec(compile(self.files['inspect-native.py'], 'inspect-native.py', 'exec'), inspector.__dict__)
            configuration, origin = inspector.configuration(inspector.process_environment(pid), argv, cwd)
            require(origin['environment'] == 'Production' and inspector.policy_flags(configuration) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False,
                'https_redirection_enabled': True}, 'production_configuration_unexpected')
            fields = inspector.connection_fields(inspector.first(configuration, 'ConnectionStrings:Pedidos',
                                                                  'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(fields), 'sql_target_unexpected')
            inspector.QUERIES = (PERMISSION_QUERY,)
            tool = next((item for item in inspector.SQL_TOOLS if Path(item).is_file()), None)
            permissions = inspector.query_sql(tool, fields, PERMISSION_QUERY)
            self.state['database_check_report'] = database_check_report(permissions)
            require(permissions == EXPECTED_DATABASE, 'database_permissions_schema_or_channels_unexpected')
            self.state['database_checks'] = permissions
            self.overrides = {}
            for name in ('appsettings.json', 'appsettings.Production.json'):
                path = self.old['backend'] / name
                if path.exists():
                    require('backend/' + name in self.files, 'existing_configuration_not_in_package')
                    self.overrides['backend/' + name] = runtime.regular(path)
            require(shutil.disk_usage(ROOT).free >= 2 * sum(map(len, self.files.values())) + 100 * 1024 * 1024,
                    'disk_space_insufficient')
            code, body = self.http('/health/db')
            require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
            code, body = self.http('/')
            require(code == 200 and body == runtime.regular(self.old['frontend'] / 'index.html'),
                    'previous_frontend_not_served')

        def stage(self):
            # Publish a complete verified directory atomically; interrupted extraction
            # must not leave a partial final release that prevents a safe retry.
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.customer-access-stage-' + uuid.uuid4().hex)
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
                        shutil.rmtree(temporary)
                self.verify_stage(part)

        def health(self, new):
            # The reused implementation verifies process, DB health and every referenced JS/CSS byte.
            super().health(new)
            if new:
                customer = '/api/customer-orders/11111111-1111-1111-1111-111111111111'
                require(self.http(customer + '/next')[0] == 401
                        and self.http(customer + '/preview', post=True)[0] == 401
                        and self.http(customer + '/capture', post=True)[0] == 401,
                        'customer_endpoints_not_protected')
                require(runtime.regular(ENV_FILE) == self.environment, 'configuration_changed_during_update')
                self.state['frontend_verified'] = True

        def apply(self):
            self.preflight()
            if self.old == self.new:
                for part in self.new:
                    self.verify_stage(part)
                self.health(True)
                self.state.update(status='already_current', phase='completed', health_verified=True)
                return
            self.state['phase'] = 'private-snapshot-and-stage'
            snapshot = BACKUPS / ('customer-access-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
                                  + '-' + uuid.uuid4().hex[:12])
            snapshot.mkdir(mode=0o700)
            (snapshot / 'api.env').write_bytes(self.environment)
            (snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
            for name, raw in self.overrides.items():
                (snapshot / name.split('/', 1)[1]).write_bytes(raw)
            for path in snapshot.iterdir():
                path.chmod(0o600)
            self.state.update(changes_made=True, private_snapshot=str(snapshot))
            self.stage()
            self.links(self.old)
            self.process(self.old['backend'])
            require(runtime.regular(ENV_FILE) == self.environment
                    and all(runtime.regular(self.old['backend'] / name.split('/', 1)[1]) == raw
                            for name, raw in self.overrides.items()), 'configuration_changed_during_staging')
            self.state['phase'] = 'activate'
            self.stop_requested = True
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            self.switch(self.new)
            runtime.command(['systemctl', 'start', SERVICE], timeout=45)
            self.state['service_restarted'] = True
            self.state['phase'] = 'verify-application'
            self.health(True)
            self.state.update(status='updated', phase='completed', health_verified=True, source_commit=EXPECTED_SOURCE,
                              new_releases={part: str(path) for part, path in self.new.items()})

    return CustomerAccessInstaller()


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-customer-access-payload.zip'))
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    instance = None
    handlers = {}
    state = dict(revision=REVISION, status='failed', changes_made=False, database_migrations_run=False)
    try:
        require(os.geteuid() == 0, 'root_required')
        instance = make_installer(args.package)
        state = instance.state
        state['mode'] = 'apply' if args.apply else 'read-only'
        if not args.apply:
            instance.preflight()
            state.update(status='planned', phase='completed')
        else:
            fd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'r+') as lock:
                meta = os.fstat(lock.fileno())
                require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
                        and meta.st_mode & 0o7777 == 0o600, 'lock_unexpected')
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                def interrupted(number, frame):
                    raise InstallError('installation_interrupted')
                for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
                    handlers[number] = signal.getsignal(number)
                    signal.signal(number, interrupted)
                try:
                    instance.apply()
                except (Exception, KeyboardInterrupt) as error:
                    state.update(status='failed', issue=str(error) if isinstance(error, InstallError) else 'local_operation_failed')
                    for number in handlers:
                        signal.signal(number, signal.SIG_IGN)
                    try:
                        instance.rollback()
                    except (Exception, KeyboardInterrupt):
                        state['rollback_failed'] = True
    except InstallError as error:
        state.update(status='failed', issue=str(error))
    except (Exception, KeyboardInterrupt):
        state.update(status='failed', issue='installation_failed_without_sensitive_details')
    finally:
        for number, handler in handlers.items():
            signal.signal(number, handler)
    print(json.dumps(state, ensure_ascii=False, indent=2))
    return 1 if state['status'] == 'failed' else 0


if __name__ == '__main__':
    os.umask(0o022)
    raise SystemExit(main())
