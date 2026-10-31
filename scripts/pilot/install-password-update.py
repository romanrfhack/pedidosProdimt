#!/usr/bin/env python3
"""Install the reviewed password feature on the existing native I04 deployment.

No SQL write/migration, credential rotation or Nginx change. The package builder
pins these two constants. Failures restore the old application under one lock.
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
import stat
import subprocess
import sys
import time
import types
import uuid
import zipfile

EXPECTED_PACKAGE_SHA256 = 'REPLACE_PACKAGE_SHA256'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
OLD_NAME = '20261024-i04-b630072'
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
PERMISSION_QUERY = """SELECT DB_NAME() AS database_name,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261024202253_AddOrderChangeNotices') AS i04,
 HAS_PERMS_BY_NAME(N'dbo.AdminUsers',N'OBJECT',N'SELECT') AS read_admin,
 HAS_PERMS_BY_NAME(N'dbo.AdminUsers',N'OBJECT',N'UPDATE') AS update_admin,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'INSERT') AS write_audit
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


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
    require(manifest.get('sourceCommit') == EXPECTED_SOURCE and manifest.get('feature') == 'admin-password-change-v1'
            and set(manifest.get('files', {})) == set(files), 'package_manifest_unexpected')
    require(all(meta == {'bytes': len(files[name]), 'sha256': sha(files[name])}
                for name, meta in manifest['files'].items()), 'package_file_hash_mismatch')
    require(all(name == 'inspect-native.py' or name.startswith(('backend/', 'frontend/')) for name in files)
            and {'inspect-native.py', 'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'} <= set(files),
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
        self.new = {part: ROOT / part / 'releases' / ('password-' + EXPECTED_SOURCE[:12]) for part in self.old}
        self.state = {'revision': '20261026-password-install1', 'status': 'planned', 'changes_made': False,
                      'database_migrations_run': False, 'password_changed': False, 'health_verified': False}
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
        inspector = types.ModuleType('reviewed_password_inventory')
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
        permissions = inspector.query_sql(tool, fields, PERMISSION_QUERY)
        self.state['database_checks'] = permissions
        require(permissions == {'database_name': 'ProdimtPedidos', 'migrations_count': 9, 'i04': 1,
                                'read_admin': 1, 'update_admin': 1, 'write_audit': 1}, 'database_permissions_or_schema_unexpected')
        self.overrides = {}
        for name in ('appsettings.json', 'appsettings.Production.json'):
            path = self.old['backend'] / name
            if path.exists():
                self.overrides['backend/' + name] = regular(path)
        require(shutil.disk_usage(ROOT).free >= 2 * sum(map(len, self.files.values())) + 100 * 1024 * 1024,
                'disk_space_insufficient')
        code, body = self.http('/health/db')
        require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')

    def expected(self, part):
        return {name[len(part) + 1:]: self.overrides.get(name, raw) for name, raw in self.files.items()
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
            temporary = current.with_name('.password-current-' + uuid.uuid4().hex)
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
                    status, body = self.http('/admin/cambiar-contrasena')
                    index = self.expected('frontend')['index.html']
                    require(status == 200 and body == index, 'frontend_index_mismatch')
                    for asset in set(re.findall(r'(?:src|href)="([^"?#]+\.(?:js|css))"', index.decode())):
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
        self.state['phase'] = 'private-snapshot-and-stage'
        snapshot = BACKUPS / ('password-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
        snapshot.mkdir(mode=0o700)
        (snapshot / 'api.env').write_bytes(self.environment)
        (snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
        for path in snapshot.iterdir(): path.chmod(0o600)
        self.state.update(changes_made=True, private_snapshot=str(snapshot))
        self.stage()
        self.links(self.old)
        self.process(self.old['backend'])
        require(regular(ENV_FILE) == self.environment and all(regular(self.old['backend'] / name.split('/', 1)[1]) == raw
                for name, raw in self.overrides.items()), 'configuration_changed_during_staging')
        self.state['phase'] = 'activate'
        self.stop_requested = True
        command(['systemctl', 'stop', SERVICE], timeout=45)
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
