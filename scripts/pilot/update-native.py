#!/usr/bin/env python3
"""Plan/apply the reviewed I04 update to one existing native PRODIMT service.

No package installation, account creation, Nginx changes, or automatic database
restore. The exact previously delivered ZIP is the only accepted payload.
"""
import argparse
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
import fcntl
import getpass
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shlex
import shutil
import signal
import socket
import stat
import subprocess
import sys
import time
import types
import uuid
import warnings
import zipfile

UPDATER_REVISION = '20261025-sql4-protocol'
EXPECTED_PACKAGE_SHA = '43520c99c4906603fc3613e46ebdac9a460f02c78193511f3dae3bcff34bdf32'
SOURCE_COMMIT = 'b630072ce67a691c3757d0590cc7f7d45f423d52'
PACKAGE_ROOT = 'prodimt-i04-nativo/'
ROOT = Path('/var/www/prodimt-pedidos')
SERVICE = 'prodimt-pedidos-api.service'
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
PRIVATE_ROOT = Path('/var/backups/prodimt-pedidos')
SQL_BACKUP_ROOT = Path('/var/opt/mssql/backup')
RELEASE = '20261024-i04-b630072'
OLD_BACKEND = ROOT / 'backend/releases/20260528151723-492bbe9'
OLD_FRONTEND = ROOT / 'frontend/releases/20260529120920-frontend-api-relative'
OLD_DLL_SHA = '2be34fbea60ab862e8615d431585b61e06f024a368142536a6a46faa3f8055ab'
SERVER, DATABASE, APP_LOGIN = '127.0.0.1,14330', 'ProdimtPedidos', 'prodimt_pedidos_app'
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
MIGRATIONS = [
    '20260527190837_InitialCreate', '20260528021837_AddOrderAuditLogs',
    '20260528025208_AddPilotAuthentication', '20260528035015_AddCatalogManagementSupport',
    '20260528131416_AddCatalogExternalCodesAndImportSupport',
    '20261024164610_AddPilotCatalogAndOrderSnapshots', '20261024181206_AddDailyOrderOperations',
    '20261024181934_ExpandOrderAuditSnapshots', '20261024202253_AddOrderChangeNotices']
ADMIN_QUERY = """SELECT DB_NAME() AS database_name, USER_NAME() AS database_user,
 IS_SRVROLEMEMBER('sysadmin') AS sysadmin, IS_SRVROLEMEMBER('dbcreator') AS dbcreator,
 IS_ROLEMEMBER('db_owner') AS db_owner,
 HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','BACKUP DATABASE') AS backup_permission,
 HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE') AS verify_permission,
 (SELECT COUNT_BIG(*) FROM dbo.Orders) AS orders_count
 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"""
APP_IDENTITY_QUERY = 'SELECT DB_NAME() AS database_name, USER_NAME() AS database_user FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;'
DML_QUERY = """SELECT
 HAS_PERMS_BY_NAME('dbo.OrderOperations','OBJECT','SELECT') AS operations_select,
 HAS_PERMS_BY_NAME('dbo.OrderOperations','OBJECT','INSERT') AS operations_insert,
 HAS_PERMS_BY_NAME('dbo.OrderOperations','OBJECT','UPDATE') AS operations_update,
 HAS_PERMS_BY_NAME('dbo.OrderOperations','OBJECT','DELETE') AS operations_delete,
 HAS_PERMS_BY_NAME('dbo.OrderChangeNotices','OBJECT','SELECT') AS notices_select,
 HAS_PERMS_BY_NAME('dbo.OrderChangeNotices','OBJECT','INSERT') AS notices_insert,
 HAS_PERMS_BY_NAME('dbo.OrderChangeNotices','OBJECT','UPDATE') AS notices_update,
 HAS_PERMS_BY_NAME('dbo.OrderChangeNotices','OBJECT','DELETE') AS notices_delete
 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"""


class UpdateError(Exception):
    """Messages are fixed issue codes, never raw configuration or command output."""


@dataclass(repr=False)
class Credentials:
    login: str
    password: str


def require(condition, code):
    if not condition:
        raise UpdateError(code)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def safe_relative(name):
    path = PurePosixPath(name)
    return bool(name) and not name.startswith('/') and '\\' not in name and '\0' not in name and all(
        part not in ('', '.', '..') for part in name.split('/')) and str(path) == name


def verify_package(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(fd, 'rb') as stream:
            require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), 'package_not_regular_file')
            data = stream.read(64 * 1024 * 1024 + 1)
        require(len(data) <= 64 * 1024 * 1024, 'package_too_large')
        require(sha(data) == EXPECTED_PACKAGE_SHA, 'package_sha_mismatch')
        files = {}
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            entries = archive.infolist()
            require(len(entries) <= 300, 'package_too_many_entries')
            require(sum(entry.file_size for entry in entries) <= 100 * 1024 * 1024, 'package_expansion_too_large')
            for entry in entries:
                require(entry.filename.startswith(PACKAGE_ROOT), 'package_root_invalid')
                name = entry.filename[len(PACKAGE_ROOT):]
                require(safe_relative(name), 'package_path_invalid')
                require(name not in files and not entry.is_dir(), 'package_duplicate_or_directory')
                kind = stat.S_IFMT(entry.external_attr >> 16)
                require(kind in (0, stat.S_IFREG), 'package_special_file')
                require(not entry.flag_bits & 1, 'package_encrypted')
                files[name] = archive.read(entry)
        require('SHA256SUMS.txt' in files and 'MANIFIESTO.json' in files, 'package_manifest_missing')
        checksums = {}
        for line in files['SHA256SUMS.txt'].decode('ascii').splitlines():
            match = re.fullmatch(r'([0-9a-f]{64})  (.+)', line)
            require(match is not None and safe_relative(match[2]), 'package_checksum_format')
            require(match[2] not in checksums, 'package_checksum_duplicate')
            checksums[match[2]] = match[1]
        require(set(checksums) == set(files) - {'SHA256SUMS.txt'}, 'package_checksum_coverage')
        require(all(sha(files[name]) == digest for name, digest in checksums.items()), 'package_file_hash_mismatch')
        manifest = json.loads(files['MANIFIESTO.json'])
        require(manifest.get('application_source_commit') == SOURCE_COMMIT, 'package_source_mismatch')
        require(manifest['application_build']['migrations']['migrationIds'] == MIGRATIONS, 'package_migration_mismatch')
        require(manifest['application_build']['migrations']['sha256'] == sha(files['migrations-idempotent.sql']), 'package_sql_hash_mismatch')
        required = ('inspect-native.py', 'backend/Prodimt.Pedidos.Api.dll',
                    'backend/Prodimt.Pedidos.Api.runtimeconfig.json', 'backend/appsettings.json',
                    'backend/appsettings.Production.json', 'frontend/index.html')
        require(all(name in files for name in required), 'package_payload_missing')
        require(not any(name.endswith(('.env', '.bak')) or 'appsettings.Development' in name for name in files), 'package_unexpected_private_file')
        return {'files': files, 'manifest': manifest, 'sha256': sha(data)}
    except UpdateError:
        raise
    except (OSError, ValueError, KeyError, UnicodeError, zipfile.BadZipFile):
        raise UpdateError('package_unreadable') from None


def parse_env_file(text, *, diagnostics=None):
    """Read EnvironmentFile data without a shell, matching systemd 255 states.

    Unlike a shell, unquoted interior spaces and quotes remain literal. Quotes
    at the start of a value may span lines, and closing quotes return to the
    value prefix state. See systemd.exec(5) and systemd v255 src/basic/env-file.c.
    Only fixed counters/state names and physical line numbers reach diagnostics.
    """
    info = diagnostics if diagnostics is not None else {}
    info.update(assignments=0, ignored_without_assignment=0, ignored_invalid_names=0,
                overwritten_assignments=0, multiline_values=0, continuations=0)
    for offset, char in enumerate(text):
        code = ord(char)
        if code == 0 or code == 0xfeff or 0xd800 <= code <= 0xdfff or 0xfdd0 <= code <= 0xfdef or code & 0xffff in (0xfffe, 0xffff):
            info.update(failure_kind='invalid_unicode_character', failure_line=text.count('\n', 0, offset) + 1)
            raise UpdateError('environment_file_invalid_character')

    result, state, key, value = {}, 'before_key', [], []
    trailing_space = None
    assignment_line, physical_line = 1, 1
    multiline = False
    whitespace, newlines = ' \t\r\n', '\r\n'

    def emit(trim=False):
        nonlocal key, value, trailing_space, multiline
        name = ''.join(key).rstrip(whitespace)
        if re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', name):
            info['assignments'] += 1
            info['overwritten_assignments'] += int(name in result)
            info['multiline_values'] += int(multiline)
            result[name] = ''.join(value[:trailing_space] if trim and trailing_space is not None else value)
        else:
            # systemd removes invalid environment names before executing a unit.
            info['ignored_invalid_names'] += 1
        key, value, trailing_space, multiline = [], [], None, False

    for char in text:
        if state == 'before_key':
            if char in '#;':
                state = 'comment'
            elif char not in whitespace:
                key, assignment_line, state = [char], physical_line, 'key'
        elif state == 'key':
            if char in newlines:
                info['ignored_without_assignment'] += 1
                key, state = [], 'before_key'
            elif char == '=':
                state, trailing_space = 'before_value', None
            else:
                key.append(char)
        elif state == 'before_value':
            if char in newlines:
                emit()
                state = 'before_key'
            elif char == "'":
                state = 'single_quote'
            elif char == '"':
                state = 'double_quote'
            elif char == '\\':
                state = 'value_escape'
            elif char not in whitespace:
                value.append(char)
                state = 'value'
        elif state == 'value':
            if char in newlines:
                emit(trim=True)
                state = 'before_key'
            elif char == '\\':
                state, trailing_space = 'value_escape', None
            else:
                if char not in whitespace:
                    trailing_space = None
                elif trailing_space is None:
                    trailing_space = len(value)
                value.append(char)
        elif state == 'value_escape':
            state = 'value'
            if char in newlines:
                info['continuations'] += 1
            else:
                value.append(char)
        elif state == 'single_quote':
            if char == "'":
                state = 'before_value'
            else:
                value.append(char)
        elif state == 'double_quote':
            if char == '"':
                state = 'before_value'
            elif char == '\\':
                state = 'double_quote_escape'
            else:
                value.append(char)
        elif state == 'double_quote_escape':
            state = 'double_quote'
            if char in '\\"$`':
                value.append(char)
            elif char == '\n':
                info['continuations'] += 1
            else:
                value.extend(('\\', char))
        elif state == 'comment':
            # Since systemd 254, a backslash does not continue a comment.
            if char in newlines:
                state = 'before_key'
        if char == '\n':
            physical_line += 1
            if state not in ('before_key', 'key', 'comment'):
                multiline = True
    info['eof_state'] = state
    if state == 'key':
        info['ignored_without_assignment'] += 1
    elif state not in ('before_key', 'comment'):
        # systemd also emits values ending at EOF inside a quote or escape.
        emit(trim=state == 'value')
        if state in ('single_quote', 'double_quote', 'value_escape', 'double_quote_escape'):
            info.update(eof_assignment_line=assignment_line)
    return result


def command(args, *, env=None, timeout=30, input_text=None):
    try:
        kwargs = {'input': input_text} if input_text is not None else {'stdin': subprocess.DEVNULL}
        result = subprocess.run(args, capture_output=True, text=True, check=False, timeout=timeout,
                                env=CLEAN_ENV if env is None else env, **kwargs)
        return result.returncode, result.stdout
    except (OSError, UnicodeError, subprocess.TimeoutExpired):
        return 127, ''


def read_file(path, limit=1024 * 1024):
    try:
        with Path(path).open('rb') as stream:
            data = stream.read(limit + 1)
        require(len(data) <= limit, 'configuration_too_large')
        return data
    except OSError:
        raise UpdateError('configuration_unreadable') from None


def plain_owned_directory(path, *, uid=0, private=False):
    path = Path(path)
    try:
        st = path.lstat()
    except OSError:
        raise UpdateError('required_directory_missing') from None
    require(stat.S_ISDIR(st.st_mode) and st.st_uid == uid, 'directory_owner_or_type_unexpected')
    require(st.st_mode & 0o022 == 0, 'directory_writable_by_other_users')
    if private:
        require(st.st_mode & 0o077 == 0, 'private_directory_permissions_unexpected')
    return st


def structural_directories():
    return (ROOT, ROOT / 'backend', ROOT / 'frontend', ROOT / 'backend/releases', ROOT / 'frontend/releases')


def structure_metadata(path):
    path = Path(path)
    st = path.lstat()
    require(path.resolve(strict=True) == path and stat.S_ISDIR(st.st_mode), 'structure_path_not_canonical_directory')
    require(st.st_uid in (0, 33) and st.st_gid in (0, 33) and st.st_mode & 0o002 == 0, 'structure_owner_or_permissions_unsupported')
    return {'path': str(path), 'device': st.st_dev, 'inode': st.st_ino, 'uid': st.st_uid, 'gid': st.st_gid, 'mode': st.st_mode & 0o777}


def secure_create_directory(path, *, uid=0, gid=0, mode=0o700):
    path = Path(path)
    require(not path.exists() and not path.is_symlink(), 'owned_destination_already_exists')
    path.mkdir(mode=mode)
    os.chown(path, uid, gid)
    os.chmod(path, mode)


def exclusive_write(path, data, mode=0o600):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.chmod(path, mode)


@contextmanager
def own_lock():
    require(os.geteuid() == 0, 'apply_requires_root')
    fd = os.open(LOCK, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        st = os.fstat(fd)
        require(stat.S_ISREG(st.st_mode) and st.st_uid == 0 and st.st_nlink == 1 and st.st_mode & 0o077 == 0,
                'lock_file_unexpected')
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise UpdateError('another_native_update_is_running') from None
        yield
    finally:
        os.close(fd)


def interrupt_update(signum, frame):
    raise UpdateError('update_interrupted')


@contextmanager
def interruption_guard(ignore=False):
    signals = (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)
    previous = {number: signal.getsignal(number) for number in signals}
    try:
        for number in signals:
            signal.signal(number, signal.SIG_IGN if ignore else interrupt_update)
        yield
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)


class Updater:
    def __init__(self, package):
        self.package_path = Path(package)
        self.payload = None
        self.inspector = None
        self.snapshot = {}
        self.token = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12]
        self.new = {part: ROOT / part / 'releases' / RELEASE for part in ('backend', 'frontend')}
        self.old = {'backend': OLD_BACKEND, 'frontend': OLD_FRONTEND}
        self.private_dir = PRIVATE_ROOT / ('i04-' + self.token)
        self.sql_backup_dir = SQL_BACKUP_ROOT / ('prodimt-i04-' + self.token)
        self.backup_file = self.sql_backup_dir / 'ProdimtPedidos-copy-only.bak'
        self.sql_tool = None
        self.app_credentials = None
        self.env_bytes = None
        self.unit_values = None
        self.old_links = {}
        self.structure = []
        self.config_overrides = {}
        self.state = {}

    def stage_name(self, phase):
        self.state['phase'] = phase
        if self.state.get('mode') == 'apply':
            print('PRODIMT: ' + phase, flush=True)

    def load_inspector(self):
        self.inspector = types.ModuleType('prodimt_verified_native_inspector')
        self.inspector.__file__ = '<verified-payload>/inspect-native.py'
        exec(compile(self.payload['files']['inspect-native.py'], self.inspector.__file__, 'exec'), self.inspector.__dict__)

    def systemd_values(self):
        keys = ('MainPID,ActiveState,User,Group,WorkingDirectory,EnvironmentFiles,Environment,ExecStart,'
                'PassEnvironment,UnsetEnvironment,FragmentPath,DropInPaths,KillMode,ControlGroup')
        code, output = command(['systemctl', 'show', SERVICE, '--no-pager', '--property=' + keys])
        require(code == 0, 'service_metadata_unavailable')
        return dict(row.split('=', 1) for row in output.splitlines() if '=' in row)

    def configuration_from_payload(self, environment, argv):
        inspector = self.inspector
        args = inspector.application_arguments(argv)
        cfg = {}
        for prefix in ('ASPNETCORE_', 'DOTNET_'):
            normalized = {}
            for key, value in environment.items():
                if key.upper().startswith(prefix):
                    inspector.add_unique(normalized, key[len(prefix):], value)
            cfg.update(normalized)
        cfg.update(args)
        require(cfg.get('environment', 'Production') == 'Production', 'candidate_environment_not_production')
        require(cfg.get('contentroot', str(ROOT / 'backend/current')) == str(ROOT / 'backend/current'), 'candidate_content_root_unexpected')
        for name in ('appsettings.json', 'appsettings.Production.json'):
            key = 'backend/' + name
            cfg.update(inspector.flatten_json(json.loads(self.config_overrides.get(key, self.payload['files'][key]), object_pairs_hook=inspector.json_object)))
        normalized = {}
        for key, value in environment.items():
            special = next((p for p in ('SQLCONNSTR_', 'SQLAZURECONNSTR_', 'CUSTOMCONNSTR_', 'MYSQLCONNSTR_') if key.upper().startswith(p)), None)
            inspector.add_unique(normalized, 'ConnectionStrings:' + key[len(special):] if special else key, value)
        cfg.update(normalized)
        cfg.update(args)
        return cfg

    def critical_configuration(self, cfg):
        first = self.inspector.first
        try:
            minutes = int(first(cfg, 'Authentication:Jwt:AccessTokenMinutes', default='720'))
            if not -2147483648 <= minutes <= 2147483647:
                minutes = 720
        except (ValueError, TypeError):
            minutes = 720
        return {
            'connection': first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING'),
            'jwt': first(cfg, 'Authentication:Jwt:SigningKey', 'PRODIMT_JWT_SIGNING_KEY'),
            'issuer': first(cfg, 'Authentication:Jwt:Issuer', default='Prodimt.Pedidos'),
            'audience': first(cfg, 'Authentication:Jwt:Audience', default='Prodimt.Pedidos'),
            'provider': first(cfg, 'Persistence:Provider', default='SqlServer'),
            'urls': first(cfg, 'urls', 'ASPNETCORE_URLS', default=None),
            'token_minutes': minutes,
            'https_redirect': str(first(cfg, 'Hosting:UseHttpsRedirection', default='true')).strip().lower(),
        }

    def inspect_restart_configuration(self):
        values = self.systemd_values()
        require(values.get('ActiveState') == 'active' and int(values.get('MainPID', 0)) > 0, 'service_not_running')
        require(values.get('WorkingDirectory') == str(ROOT / 'backend/current'), 'unit_working_directory_unexpected')
        require(values.get('User') == 'www-data' and values.get('Group') in ('', 'www-data'), 'unit_service_identity_unexpected')
        require(values.get('KillMode') == 'control-group' and values.get('ControlGroup') == '/system.slice/' + SERVICE,
                'unit_process_group_unsupported')
        require(values.get('EnvironmentFiles') == str(ENV_FILE) + ' (ignore_errors=no)', 'unit_environment_files_unexpected')
        require(not values.get('PassEnvironment') and not values.get('UnsetEnvironment'), 'unit_environment_overrides_unsupported')
        start = values.get('ExecStart', '')
        match = re.search(r'argv\[\]=(.*?)\s*;\s*ignore_errors=', start)
        require(match is not None, 'unit_execstart_unsupported')
        unit_argv = shlex.split(match[1])
        require(not any(arg == '--initialize-pilot' or arg.startswith('--initialize-pilot=') for arg in unit_argv), 'bootstrap_argument_not_allowed')
        require(len(unit_argv) >= 2 and unit_argv[0] in ('/usr/bin/dotnet', '/usr/share/dotnet/dotnet'), 'unit_execstart_unsupported')
        require(unit_argv[1] == str(ROOT / 'backend/current/Prodimt.Pedidos.Api.dll'), 'unit_execstart_not_current_release')
        st = ENV_FILE.lstat()
        require(stat.S_ISREG(st.st_mode) and st.st_nlink == 1 and st.st_uid == 0 and st.st_gid == 33 and st.st_mode & 0o027 == 0, 'environment_file_permissions_unexpected')
        self.env_bytes = read_file(ENV_FILE)
        restart_env = {}
        for row in shlex.split(values.get('Environment', '')):
            require('=' in row, 'unit_environment_format_unsupported')
            name, value = row.split('=', 1)
            require(re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', name) is not None, 'unit_environment_format_unsupported')
            restart_env[name] = value
        syntax = self.state['environment_file_syntax'] = {}
        try:
            env_text = self.env_bytes.decode('utf-8-sig')
        except UnicodeError:
            syntax['failure_kind'] = 'invalid_utf8'
            raise UpdateError('environment_file_encoding_unsupported') from None
        restart_env.update(parse_env_file(env_text, diagnostics=syntax))
        pid = int(values['MainPID'])
        identity = self.inspector.process_identity(pid)
        running_env = self.inspector.process_environment(pid)
        running_argv = self.inspector.read_limited(f'/proc/{pid}/cmdline').rstrip('\0').split('\0')
        require(not any(arg == '--initialize-pilot' or arg.startswith('--initialize-pilot=') for arg in running_argv), 'bootstrap_argument_not_allowed')
        cwd = Path(f'/proc/{pid}/cwd').resolve(strict=True)
        require(cwd == OLD_BACKEND, 'running_process_release_unexpected')
        current_cfg, source = self.inspector.configuration(running_env, running_argv, cwd)
        restart_cfg, restart_source = self.inspector.configuration(restart_env, unit_argv, ROOT / 'backend/current')
        require(source['environment'] == restart_source['environment'] == 'Production', 'service_environment_not_production')
        require(self.critical_configuration(current_cfg) == self.critical_configuration(restart_cfg), 'environment_file_differs_from_running_configuration')
        candidate = self.configuration_from_payload(restart_env, unit_argv)
        current_critical = self.critical_configuration(current_cfg)
        require(current_critical['urls'] == 'http://127.0.0.1:5080', 'application_listen_url_unexpected')
        if self.critical_configuration(candidate)['token_minutes'] != current_critical['token_minutes']:
            require(1 <= current_critical['token_minutes'] <= 1440, 'existing_token_lifetime_invalid')
            path = 'backend/appsettings.Production.json'
            original = self.payload['files'][path]
            adjusted = json.loads(original)
            adjusted.setdefault('Authentication', {}).setdefault('Jwt', {})['AccessTokenMinutes'] = current_critical['token_minutes']
            self.config_overrides[path] = (json.dumps(adjusted, ensure_ascii=False, indent=2) + '\n').encode()
            self.state['configuration_override'] = {'path': path, 'setting': 'Authentication:Jwt:AccessTokenMinutes',
                'preserved_minutes': current_critical['token_minutes'], 'package_sha256': sha(original),
                'staged_sha256': sha(self.config_overrides[path])}
            candidate = self.configuration_from_payload(restart_env, unit_argv)
        require(self.critical_configuration(candidate) == self.critical_configuration(current_cfg), 'candidate_loses_existing_critical_configuration')
        flags = self.inspector.policy_flags(candidate)
        require(all(flags[k] for k in ('provider_is_sqlserver', 'jwt_compatible_with_production_policy',
                                      'jwt_lifetime_compatible', 'connection_compatible_with_production_policy')) and
                not flags['development_seed_enabled'] and not flags['automatic_migrations_enabled'], 'candidate_production_policy_failed')
        parsed = self.inspector.connection_fields(self.critical_configuration(candidate)['connection'])
        require(self.inspector.permitted_connection(parsed), 'sql_target_or_login_unexpected')
        self.app_credentials = Credentials(APP_LOGIN, parsed['password'])
        self.inspector.assert_same_process(pid, identity)
        self.unit_values = values
        self.snapshot['pid'] = pid
        self.snapshot['process_identity'] = identity
        self.snapshot['env_sha256'] = sha(self.env_bytes)
        self.snapshot['unit_fingerprint'] = sha(json.dumps({k: v for k, v in values.items() if k != 'MainPID'}, sort_keys=True).encode())
        self.snapshot['source_environment'] = 'Production'

    def readonly_preflight(self):
        require(os.geteuid() == 0, 'inspection_requires_root_for_private_service_configuration')
        self.payload = verify_package(self.package_path)
        self.load_inspector()
        plain_owned_directory(ROOT.parent)
        self.structure = [structure_metadata(parent) for parent in structural_directories()]
        self.state['structure_adoption'] = [entry['path'] for entry in self.structure
                                           if (entry['uid'], entry['gid'], entry['mode']) != (0, 33, 0o755)]
        for part in ('backend', 'frontend'):
            current = ROOT / part / 'current'
            require(current.is_symlink() and current.resolve(strict=True) == self.old[part], 'current_release_unexpected')
            self.old_links[part] = os.readlink(current)
            require(not self.new[part].exists() and not self.new[part].is_symlink(), 'new_release_already_exists')
        require(sha(read_file(OLD_BACKEND / 'Prodimt.Pedidos.Api.dll', 20 * 1024 * 1024)) == OLD_DLL_SHA, 'current_backend_hash_mismatch')
        require(shutil.disk_usage(ROOT).free >= 256 * 1024 * 1024, 'insufficient_release_disk_space')
        backup_st = plain_owned_directory(SQL_BACKUP_ROOT, uid=999)
        require(backup_st.st_gid == 988, 'sql_backup_directory_identity_unexpected')
        self.inspect_restart_configuration()
        inventory = self.inspector.inspect()
        require(not inventory.get('issue') and inventory.get('sql', {}).get('queried') and not inventory['sql'].get('issue'), 'native_inventory_failed')
        require(inventory['sql'].get('migrations') == MIGRATIONS[:5], 'database_migration_baseline_unexpected')
        require(inventory['sql'].get('orders_count') == 0 and inventory['sql'].get('duplicate_order_groups') == 0, 'database_order_baseline_changed')
        require(inventory['sql'].get('metadata', {}).get('order_sequence_index_unique') == 0, 'database_index_baseline_unexpected')
        require(inventory.get('native_sql_service', {}).get('ActiveState') == 'active', 'native_sql_service_not_active')
        runtimes = inventory.get('installed_runtimes', [])
        for framework in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'):
            require(any(row.get('name') == framework and re.fullmatch(r'10\.0\.(?:1[3-9]|[2-9][0-9]|[1-9][0-9]{2,})', row.get('version', '')) for row in runtimes), 'required_dotnet_runtime_missing')
        self.sql_tool = next((tool for tool in self.inspector.SQL_TOOLS if Path(tool).is_file() and os.access(tool, os.X_OK)), None)
        require(self.sql_tool is not None, 'sqlcmd_not_available')
        identity = self.sql_json(self.app_credentials, APP_IDENTITY_QUERY)
        require(identity == {'database_name': DATABASE, 'database_user': APP_LOGIN}, 'application_database_principal_unexpected')
        size_mib = inventory['sql']['metadata'].get('database_files_MiB')
        require(isinstance(size_mib, int) and size_mib >= 0, 'database_size_unknown')
        require(shutil.disk_usage(SQL_BACKUP_ROOT).free >= (size_mib + 256) * 1024 * 1024, 'insufficient_backup_disk_space')
        self.state.update(package_sha256=EXPECTED_PACKAGE_SHA, old_releases={k: str(v) for k, v in self.old.items()},
                          new_releases={k: str(v) for k, v in self.new.items()}, database=DATABASE,
                          migrations_before=5, migrations_expected=9, env_preserved=True)

    def prompt_admin(self):
        try:
            # getpass uses stream only for prompts and opens its own raw TTY
            # input. Buffered r+ requires seekability, which a TTY lacks.
            with open('/dev/tty', 'w', encoding='utf-8') as tty, warnings.catch_warnings():
                require(tty.isatty(), 'apply_requires_interactive_tty')
                warnings.simplefilter('error', getpass.GetPassWarning)
                login = getpass.getpass('Usuario administrador SQL existente (oculto): ', stream=tty).strip()
                password = getpass.getpass('Contraseña SQL (oculta; no se guarda): ', stream=tty)
        except (OSError, getpass.GetPassWarning, EOFError):
            raise UpdateError('apply_requires_interactive_tty') from None
        require(re.fullmatch(r'[A-Za-z0-9_.@\\-]{1,128}', login) is not None and login != APP_LOGIN and bool(password), 'sql_admin_input_invalid')
        return Credentials(login, password)

    def sql_run(self, credentials, *, query=None, script=None, timeout=30):
        require(self.sql_tool in self.inspector.SQL_TOOLS, 'sqlcmd_path_unexpected')
        require(isinstance(credentials, Credentials) and credentials.login and credentials.password, 'sql_credentials_missing')
        env = dict(CLEAN_ENV, SQLCMDUSER=credentials.login, SQLCMDPASSWORD=credentials.password)
        args = [self.sql_tool, '-S', SERVER, '-d', DATABASE, '-C', '-I', '-b', '-l', '5', '-t', str(max(10, timeout - 5)), '-x']
        if query is not None:
            args += ['-y', '0', '-w', '65535', '-Q', 'SET NOCOUNT ON; SET LOCK_TIMEOUT 10000; ' + query]
        code, output = command(args, env=env, timeout=timeout, input_text=script)
        if code != 0:
            # Preserve only numeric SQL headers, never message text or credentials.
            self.state['sql_failure'] = {
                'exit_code': code,
                'error_numbers': sorted({int(number) for number in re.findall(
                    r'(?m)^\s*Msg ([1-9][0-9]{0,5}), Level [0-9]{1,2}, State [0-9]{1,3},', output)})[:10],
            }
            raise UpdateError('sql_operation_failed')
        return output

    def sql_json(self, credentials, query):
        wrapped = 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        try:
            return self.inspector.parse_sql_output(self.sql_run(credentials, query=wrapped))
        except self.inspector.InspectionError:
            raise UpdateError('sql_metadata_unrecognized') from None

    def preflight_admin(self, credentials):
        data = self.sql_json(credentials, ADMIN_QUERY)
        require(data.get('database_name') == DATABASE and data.get('orders_count') == 0, 'admin_database_baseline_unexpected')
        require(data.get('sysadmin') == 1 or (data.get('db_owner') == 1 and data.get('backup_permission') == 1 and
                (data.get('verify_permission') == 1 or data.get('dbcreator') == 1)), 'sql_admin_permissions_insufficient')

    def unchanged_before_stop(self):
        require(sha(read_file(ENV_FILE)) == self.snapshot['env_sha256'], 'environment_file_changed_during_update')
        current = self.systemd_values()
        fingerprint = sha(json.dumps({k: v for k, v in current.items() if k != 'MainPID'}, sort_keys=True).encode())
        require(fingerprint == self.snapshot['unit_fingerprint'], 'unit_changed_during_update')
        self.inspector.assert_same_process(self.snapshot['pid'], self.snapshot['process_identity'])
        for part in ('backend', 'frontend'):
            require((ROOT / part / 'current').is_symlink() and (ROOT / part / 'current').resolve(strict=True) == self.old[part], 'current_release_changed_during_update')
        require(sha(read_file(OLD_BACKEND / 'Prodimt.Pedidos.Api.dll', 20 * 1024 * 1024)) == OLD_DLL_SHA, 'current_backend_changed_during_update')

    def stage(self):
        for part in ('backend', 'frontend'):
            secure_create_directory(self.new[part], mode=0o755)
            prefix = part + '/'
            for name, data in self.payload['files'].items():
                if not name.startswith(prefix):
                    continue
                data = self.config_overrides.get(name, data)
                target = self.new[part] / name[len(prefix):]
                for directory in reversed(target.parent.parents):
                    if directory != self.new[part] and self.new[part] in directory.parents and not directory.exists():
                        secure_create_directory(directory, mode=0o755)
                if not target.parent.exists():
                    secure_create_directory(target.parent, mode=0o755)
                exclusive_write(target, data, 0o644)
                require(sha(read_file(target, 20 * 1024 * 1024)) == sha(data), 'staged_file_verification_failed')

    def adopt_structure(self):
        # Outer directories are secured first; no recursive ownership changes.
        for entry in self.structure:
            path = Path(entry['path'])
            require(path in structural_directories(), 'structure_path_outside_scope')
            require(structure_metadata(path) == entry, 'structure_changed_since_preflight')
            fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                st = os.fstat(fd)
                require((st.st_dev, st.st_ino) == (entry['device'], entry['inode']), 'structure_inode_changed')
                os.fchown(fd, 0, 33)
                os.fchmod(fd, 0o755)
            finally:
                os.close(fd)
        for entry in self.structure:
            current = structure_metadata(entry['path'])
            require((current['device'], current['inode'], current['uid'], current['gid'], current['mode']) ==
                    (entry['device'], entry['inode'], 0, 33, 0o755), 'structure_changed_during_adoption')
        self.state['structure_adopted'] = True

    def save_private_backup(self):
        if not PRIVATE_ROOT.exists():
            plain_owned_directory(PRIVATE_ROOT.parent)
            secure_create_directory(PRIVATE_ROOT)
        plain_owned_directory(PRIVATE_ROOT, private=True)
        secure_create_directory(self.private_dir)
        exclusive_write(self.private_dir / 'api.env', self.env_bytes)
        # Unit properties may contain secrets: this snapshot is never printed.
        exclusive_write(self.private_dir / 'unit-private.json', json.dumps(self.unit_values, ensure_ascii=False).encode())
        exclusive_write(self.private_dir / 'old-links.json', json.dumps(self.old_links).encode())
        exclusive_write(self.private_dir / 'structure-before.json', json.dumps(self.structure, indent=2).encode())
        if self.state.get('configuration_override'):
            exclusive_write(self.private_dir / 'configuration-override.json', json.dumps(self.state['configuration_override'], indent=2).encode())
        exclusive_write(self.private_dir / 'migrations-idempotent.sql', self.payload['files']['migrations-idempotent.sql'])
        self.state['private_backup_directory'] = str(self.private_dir)

    def stop(self):
        code, _ = command(['systemctl', 'stop', SERVICE], timeout=45)
        require(code == 0, 'prodimt_service_stop_failed')
        code, output = command(['systemctl', 'show', SERVICE, '--property=ActiveState,MainPID'])
        values = dict(row.split('=', 1) for row in output.splitlines() if '=' in row)
        require(code == 0 and values.get('ActiveState') == 'inactive' and values.get('MainPID') == '0', 'prodimt_service_not_stopped')
        events = Path('/sys/fs/cgroup/system.slice') / SERVICE / 'cgroup.events'
        if events.exists():
            require(re.search(r'(?m)^populated 0$', read_file(events).decode()) is not None, 'prodimt_process_group_not_empty')
        with socket.socket() as probe:
            probe.settimeout(1)
            require(probe.connect_ex(('127.0.0.1', 5080)) != 0, 'prodimt_port_still_in_use')

    def backup_database(self, credentials):
        self.preflight_admin(credentials)  # Recheck the 0-order baseline after writes have stopped.
        secure_create_directory(self.sql_backup_dir, uid=999, gid=988)
        path = str(self.backup_file)
        require(re.fullmatch(r'/var/opt/mssql/backup/prodimt-i04-[A-Za-z0-9-]+/ProdimtPedidos-copy-only\.bak', path) is not None, 'backup_path_unexpected')
        self.sql_run(credentials, query="BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + path + "' WITH COPY_ONLY, CHECKSUM;", timeout=300)
        st = self.backup_file.lstat()
        require(stat.S_ISREG(st.st_mode) and st.st_nlink == 1 and st.st_uid == 999 and st.st_size > 0, 'sql_backup_file_unexpected')
        os.chmod(self.backup_file, 0o600)
        self.sql_run(credentials, query="RESTORE VERIFYONLY FROM DISK=N'" + path + "' WITH CHECKSUM;", timeout=300)
        self.state['verified_database_backup'] = path
        exclusive_write(self.private_dir / 'database-backup.json', json.dumps({'path': path, 'bytes': st.st_size, 'copy_only': True, 'checksum': True, 'verifyonly_completed': True}).encode())

    def migrate(self, credentials):
        script = self.payload['files']['migrations-idempotent.sql'].decode('utf-8-sig')
        # Filtered indexes require these session options. SqlClient and ODBC
        # sqlcmd have different defaults; make the actual CLI path explicit.
        prologue = ('SET NOCOUNT ON;\nSET XACT_ABORT ON;\nSET LOCK_TIMEOUT 10000;\n'
                    'SET ANSI_NULLS ON;\nSET ANSI_PADDING ON;\nSET ANSI_WARNINGS ON;\n'
                    'SET ARITHABORT ON;\nSET CONCAT_NULL_YIELDS_NULL ON;\n'
                    'SET QUOTED_IDENTIFIER ON;\nSET NUMERIC_ROUNDABORT OFF;\nGO\n')
        self.sql_run(credentials, script=prologue + script, timeout=300)

    def verify_history(self, credentials):
        rows = self.sql_json(credentials, self.inspector.SQL_MIGRATIONS)
        require(isinstance(rows, list) and [row.get('MigrationId') for row in rows] == MIGRATIONS, 'post_migration_history_unexpected')
        metadata = self.sql_json(credentials, self.inspector.SQL_METADATA)
        require(metadata.get('database_name') == DATABASE and metadata.get('order_sequence_index_unique') == 1, 'post_migration_schema_unexpected')
        self.state.update(migrations_after=9, migrated_schema_verified=True)
        permissions = self.sql_json(self.app_credentials, DML_QUERY)
        for prefix, table in (('operations', 'OrderOperations'), ('notices', 'OrderChangeNotices')):
            missing = [verb.upper() for verb in ('select', 'insert', 'update', 'delete') if permissions.get(prefix + '_' + verb) != 1]
            if missing:
                self.sql_run(credentials, query='GRANT ' + ', '.join(missing) + ' ON OBJECT::[dbo].[' + table + '] TO [prodimt_pedidos_app];')
                self.state.setdefault('new_table_dml_grants', []).append({'table': table, 'permissions': missing})
        permissions = self.sql_json(self.app_credentials, DML_QUERY)
        require(all(permissions.get(prefix + '_' + verb) == 1 for prefix in ('operations', 'notices') for verb in ('select', 'insert', 'update', 'delete')), 'application_new_table_permissions_failed')
        self.state['application_new_table_dml_verified'] = True

    def replace_link(self, part, target):
        current = ROOT / part / 'current'
        require(current.is_symlink() and current.resolve(strict=True) in (self.old[part], self.new[part]), 'current_link_conflict')
        temporary = current.parent / ('.prodimt-i04-link-' + self.token)
        require(not temporary.exists() and not temporary.is_symlink(), 'temporary_link_conflict')
        os.symlink(target, temporary)
        try:
            os.replace(temporary, current)
        finally:
            if temporary.is_symlink():
                temporary.unlink()

    def switch_links(self):
        for part in ('backend', 'frontend'):
            self.replace_link(part, str(self.new[part]))

    def restore_links(self):
        failures = []
        for part in ('backend', 'frontend'):
            try:
                self.replace_link(part, self.old_links[part])
            except Exception:
                failures.append(part)
        for part in ('backend', 'frontend'):
            current = ROOT / part / 'current'
            if not current.is_symlink() or current.resolve() != self.old[part]:
                failures.append(part)
        require(not failures, 'previous_links_not_fully_restored')

    def start(self):
        code, _ = command(['systemctl', 'start', SERVICE], timeout=45)
        require(code == 0, 'prodimt_service_start_failed')

    def http_get(self, path, https=False):
        args = ['curl', '--silent', '--show-error', '--noproxy', '*', '--max-time', '5',
                '--max-filesize', '2097152', '--write-out', '\n%{http_code}']
        if https:
            args += ['--resolve', 'pedidos.prodimt.site:443:127.0.0.1', 'https://pedidos.prodimt.site' + path]
        else:
            args += ['http://127.0.0.1:5080' + path]
        code, output = command(args, timeout=8)
        if code or '\n' not in output:
            return 0, ''
        body, status_code = output.rsplit('\n', 1)
        return int(status_code) if status_code.isdigit() else 0, body

    def verify_health(self):
        for attempt in range(20):
            status_code, body = self.http_get('/health/db')
            https = status_code in (301, 302, 307, 308)
            if https:
                status_code, body = self.http_get('/health/db', https=True)
            try:
                healthy = status_code == 200 and json.loads(body).get('database') == 'reachable'
            except (ValueError, AttributeError):
                healthy = False
            if healthy:
                self.verify_process_release(self.new['backend'])
                require(all(self.http_get(path, https=https)[0] == 401 for path in
                            ('/api/admin/operations/changes', '/api/admin/operations/plan')), 'new_endpoints_not_ready')
                require(all(self.http_get(path, https=True)[0] == 401 for path in
                            ('/api/admin/operations/changes', '/api/admin/operations/plan')), 'https_new_endpoints_not_ready')
                expected_index = self.payload['files']['frontend/index.html']
                status_code, body = self.http_get('/', https=True)
                require(status_code == 200 and sha(body.encode()) == sha(expected_index), 'https_frontend_index_mismatch')
                assets = set(re.findall(r'(?:src|href)="([^"?#]+\.(?:js|css))"', expected_index.decode()))
                require(bool(assets), 'frontend_asset_references_missing')
                for asset in assets:
                    require(safe_relative(asset) and 'frontend/' + asset in self.payload['files'], 'frontend_asset_reference_unsupported')
                    status_code, body = self.http_get('/' + asset, https=True)
                    require(status_code == 200 and sha(body.encode()) == sha(self.payload['files']['frontend/' + asset]), 'https_frontend_asset_mismatch')
                self.state['health_verified'] = True
                self.state['health_used_https_proxy'] = https
                return
            if attempt < 19:
                time.sleep(1)
        raise UpdateError('updated_application_health_failed')

    def verify_process_release(self, expected):
        values = self.systemd_values()
        require(values.get('ActiveState') == 'active' and int(values.get('MainPID', '0')) > 0, 'application_service_not_active')
        pid = int(values['MainPID'])
        require(Path(f'/proc/{pid}/cwd').resolve(strict=True) == expected, 'application_running_release_mismatch')

    def verify_previous_health(self):
        for attempt in range(10):
            try:
                self.verify_process_release(self.old['backend'])
                code, body = self.http_get('/health/db')
                if code in (301, 302, 307, 308):
                    code, body = self.http_get('/health/db', https=True)
                if code == 200 and json.loads(body).get('database') == 'reachable':
                    self.state['previous_application_health_verified'] = True
                    return
            except (UpdateError, OSError, ValueError, AttributeError):
                pass
            if attempt < 9:
                time.sleep(1)
        raise UpdateError('previous_application_health_not_verified')

    def report(self, state):
        if state.get('private_backup_directory'):
            try:
                exclusive_write(self.private_dir / ('result-' + uuid.uuid4().hex[:8] + '.json'), json.dumps(state, ensure_ascii=False, indent=2).encode())
            except OSError:
                state['private_result_write_failed'] = True
        return dict(state)

    def run(self, apply=False):
        self.state = {'updater_revision': UPDATER_REVISION, 'mode': 'apply' if apply else 'plan', 'status': 'pending', 'phase': 'read-only-preflight',
                      'migrations_may_have_changed': False, 'database_automatically_restored': False}
        if not apply:
            return self.execute(False)
        try:
            # The entire failure recovery remains inside this same lock.
            with own_lock(), interruption_guard():
                return self.execute(True)
        except Exception as exc:
            self.state.update(status='failed', issue=str(exc) if isinstance(exc, UpdateError) else 'update_lock_unavailable')
            return self.report(self.state)

    def execute(self, apply):
        stopped = False
        credentials = None
        try:
            if not apply:
                self.readonly_preflight()
                self.state.update(status='ready', changes_made=False, admin_credentials_required_for_apply=True)
                return self.report(self.state)
            self.readonly_preflight()
            self.stage_name('admin-authentication')
            credentials = self.prompt_admin()
            self.preflight_admin(credentials)
            self.stage_name('prepare-private-backup-and-owned-directories')
            self.save_private_backup()
            self.adopt_structure()
            self.stage_name('stage-releases')
            self.stage()
            self.unchanged_before_stop()
            self.stage_name('stop-prodimt-api')
            stopped = True  # A failed stop can still have stopped the process.
            self.stop()
            self.stage_name('database-backup-and-verification')
            self.backup_database(credentials)
            self.stage_name('database-migrations')
            self.state['migrations_may_have_changed'] = True
            self.migrate(credentials)
            self.verify_history(credentials)
            self.stage_name('activate-releases')
            self.switch_links()
            self.start()
            self.stage_name('verify-application')
            self.verify_health()
            self.state.update(status='updated', changes_made=True, phase='completed')
            return self.report(self.state)
        except (Exception, KeyboardInterrupt) as exc:
            self.state['issue'] = str(exc) if isinstance(exc, UpdateError) else 'update_step_failed'
            self.state['status'] = 'failed' if apply else 'blocked'
            self.state['changes_made'] = bool(apply and self.state.get('phase') not in ('read-only-preflight', 'admin-authentication'))
            if stopped:
                self.state['rollback_errors'] = []
                with interruption_guard(ignore=True):
                    try:
                        self.stop()
                        self.state['stop_completed'] = True
                        self.restore_links()
                        self.state['restore_links_completed'] = True
                        self.start()
                        self.state['start_previous_completed'] = True
                        self.verify_previous_health()
                    except Exception:
                        step = 'stop' if not self.state.get('stop_completed') else 'restore_links' if not self.state.get('restore_links_completed') else 'start_previous' if not self.state.get('start_previous_completed') else 'previous_health'
                        self.state['rollback_errors'].append(step + '_failed')
                        self.state['manual_recovery_required'] = True
                self.state['previous_release_start_requested'] = self.state.get('start_previous_completed', False)
            return self.report(self.state)
        finally:
            if credentials is not None:
                credentials.password = ''
            if self.app_credentials is not None:
                self.app_credentials.password = ''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True, help='Exact previously delivered PRODIMT_I04_Nativo_20261024.zip')
    parser.add_argument('--apply', action='store_true', help='Apply after private interactive SQL administrator authentication')
    args = parser.parse_args()
    try:
        result = Updater(args.package).run(apply=args.apply)
    except Exception:
        result = {'status': 'failed', 'issue': 'native_update_unavailable', 'database_automatically_restored': False}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get('status') in ('ready', 'updated') else 1


if __name__ == '__main__':
    sys.exit(main())
