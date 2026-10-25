#!/usr/bin/env python3
"""Reset one existing active web administrator on the reviewed native I04 site.

No service restart, bootstrap, migration, SQL-login change or account activation.
Secrets use an echo-free TTY and subprocess stdin/environment, never argv/files.
The old account hash is backed up privately before an audited transactional reset.
Identity V3 wire format: https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasher.cs.html
"""
import argparse
import base64
from datetime import datetime, timezone
import fcntl
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import stat
import struct
import subprocess
import sys
import uuid
import warnings

REVISION = '20261025-admin-reset1'
SERVICE = 'prodimt-pedidos-api.service'
SERVER = '127.0.0.1,14330'
DATABASE = 'ProdimtPedidos'
DOMAIN = 'pedidos.prodimt.site'
RELEASE = Path('/var/www/prodimt-pedidos/backend/releases/20261024-i04-b630072')
BACKUPS = Path('/var/backups/prodimt-pedidos')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SQL_TOOLS = ('/opt/mssql-tools/bin/sqlcmd', '/opt/mssql-tools18/bin/sqlcmd')
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
OPTIONS = ('SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000; '
           'SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; '
           'SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; '
           'SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;\n')


class RecoveryError(Exception):
    """Only fixed, non-sensitive issue codes may leave this script."""


def require(condition, issue):
    if not condition:
        raise RecoveryError(issue)


def command(args, *, data=None, env=None, timeout=25):
    try:
        return subprocess.run(args, input=data, text=True, capture_output=True,
                              env=CLEAN_ENV if env is None else env,
                              timeout=timeout, check=False)
    except (OSError, UnicodeError, subprocess.TimeoutExpired):
        raise RecoveryError('subprocess_failed_or_timed_out') from None


def prompt_secret(prompt):
    try:
        with open('/dev/tty', 'w', encoding='utf-8') as tty, warnings.catch_warnings():
            require(tty.isatty(), 'interactive_tty_required')
            warnings.simplefilter('error', getpass.GetPassWarning)
            return getpass.getpass(prompt, stream=tty)
    except (OSError, EOFError, getpass.GetPassWarning):
        raise RecoveryError('interactive_tty_required') from None


def new_password():
    value = prompt_secret('Nueva contraseña WEB (mínimo 16 caracteres; oculta): ')
    repeated = prompt_secret('Repite la nueva contraseña WEB (oculta): ')
    require(value == repeated, 'password_confirmation_mismatch')
    require(16 <= len(value) <= 128 and not any(ord(c) < 32 or ord(c) == 127 for c in value)
            and bool(value.strip()), 'new_password_requires_16_to_128_printable_characters')
    return value


def identity_hash(password):
    # ASP.NET Identity V3: marker, PRF=HMACSHA512, iterations, salt length,
    # random 128-bit salt and 256-bit subkey. No plaintext reaches SQL.
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac('sha512', password.encode('utf-8'), salt, 100000, dklen=32)
    return base64.b64encode(b'\x01' + struct.pack('>III', 2, 100000, len(salt)) + salt + key).decode('ascii')


def sql_text(value):
    # Hex-encoded UTF-16LE avoids quotes, GO, sqlcmd commands and substitutions.
    return 'CONVERT(nvarchar(max),0x' + value.encode('utf-16le').hex() + ')'


def parse_json(output):
    require(isinstance(output, str) and len(output) < 1024 * 1024, 'sql_response_invalid')
    lines = output.strip().splitlines()
    if len(lines) >= 3 and lines[0].strip() == 'ProdimtRecoveryJson' and re.fullmatch(r'-+', lines[1].strip()):
        lines = lines[2:]
    try:
        return json.loads(''.join(lines))
    except (ValueError, TypeError):
        raise RecoveryError('sql_response_invalid') from None


def snapshot_sql(user):
    return ("SELECT (SELECT DB_NAME() AS database_name, "
            "(SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count, "
            "(SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId="
            "N'20261024202253_AddOrderChangeNotices') AS i04_migration_present, "
            "JSON_QUERY((SELECT Id AS id,UserName AS user_name,PasswordHash AS password_hash,"
            "DisplayName AS display_name,CAST(IsActive AS int) AS is_active,CreatedAt AS created_at "
            "FROM dbo.AdminUsers WHERE UserName=" + sql_text(user) +
            " FOR JSON PATH)) AS users FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS ProdimtRecoveryJson;")


def validate_snapshot(data, user):
    require(isinstance(data, dict) and data.get('database_name') == DATABASE
            and data.get('migrations_count') == 9 and data.get('i04_migration_present') == 1,
            'database_not_reviewed_i04')
    users = data.get('users')
    require(isinstance(users, list) and len(users) == 1 and isinstance(users[0], dict),
            'web_administrator_not_unique_or_missing')
    row = users[0]
    require(row.get('is_active') == 1, 'web_administrator_inactive')
    require(isinstance(row.get('user_name'), str) and row['user_name'].casefold() == user.casefold(),
            'web_administrator_mismatch')
    require(isinstance(row.get('password_hash'), str) and 0 < len(row['password_hash']) <= 500,
            'existing_hash_unexpected')
    try:
        uuid.UUID(row['id'])
    except (KeyError, ValueError, TypeError, AttributeError):
        raise RecoveryError('web_administrator_id_invalid') from None
    return row


def reset_sql(row, password_hash, operation_id):
    administrator = str(uuid.UUID(row['id']))
    operation = str(uuid.UUID(operation_id))
    return ("DECLARE @id uniqueidentifier='" + administrator + "';\n"
            "BEGIN TRANSACTION;\n"
            "UPDATE dbo.AdminUsers WITH (UPDLOCK) SET PasswordHash=" + sql_text(password_hash) +
            " WHERE Id=@id AND IsActive=1 AND UserName COLLATE Latin1_General_100_BIN2=" + sql_text(row['user_name']) +
            " AND PasswordHash COLLATE Latin1_General_100_BIN2=" + sql_text(row['password_hash']) + ";\n"
            "IF @@ROWCOUNT <> 1 THROW 51001,N'Administrator changed; recovery aborted',1;\n"
            "INSERT INTO dbo.AuditLogs (Id,EntityType,EntityId,EventType,OccurredAt,ActorType,"
            "ActorId,ActorDisplayName,Summary,MetadataJson) VALUES ('" + operation + "',N'AdminUser',"
            "CONVERT(nvarchar(36),@id),N'AdminUserPasswordReset',SYSUTCDATETIME(),2,NULL,"
            "N'Recuperacion local VPS',N'Contrasena restablecida mediante recuperacion local autorizada.',"
            "N'{\"method\":\"native-admin-recovery\",\"revision\":\"" + REVISION + "\"}');\n"
            "COMMIT TRANSACTION;\n"
            "SELECT (SELECT 1 AS password_reset,1 AS audit_recorded FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)"
            " AS ProdimtRecoveryJson;\n")


class Recovery:
    def __init__(self, user):
        self.user = user
        self.sql_tool = None
        self.sql_login = ''
        self.sql_password = ''
        self.state = {'revision': REVISION, 'status': 'planned', 'web_user': user,
                      'database': DATABASE, 'changes_made': False,
                      'password_reset': False, 'login_verified': False,
                      'secret_values_printed': False}

    def service_ready(self):
        result = command(['systemctl', 'show', SERVICE, '--property=ActiveState,MainPID'])
        props = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
        require(result.returncode == 0 and props.get('ActiveState') == 'active'
                and props.get('MainPID', '').isdigit() and int(props['MainPID']) > 0,
                'prodimt_service_not_active')
        require(Path('/proc/' + props['MainPID'] + '/cwd').resolve(strict=True) == RELEASE,
                'prodimt_service_not_i04')

    def sql(self, script):
        args = [self.sql_tool, '-S', SERVER, '-d', DATABASE, '-C', '-I', '-b', '-l', '5',
                '-t', '20', '-x', '-y', '0', '-w', '65535']
        env = dict(CLEAN_ENV, SQLCMDUSER=self.sql_login, SQLCMDPASSWORD=self.sql_password)
        result = command(args, data=OPTIONS + script, env=env)
        if result.returncode:
            self.state['sql_error_numbers'] = sorted({int(n) for n in re.findall(
                r'(?m)^\s*Msg ([1-9][0-9]{0,5}), Level ', result.stdout + '\n' + result.stderr)})[:10]
            raise RecoveryError('sql_operation_failed')
        return parse_json(result.stdout)

    def http(self, path, *, payload=None, token=None):
        args = ['/usr/bin/curl', '--silent', '--show-error', '--noproxy', '*',
                '--max-time', '15', '--max-filesize', '1048576',
                '--resolve', DOMAIN + ':443:127.0.0.1', '--write-out', '\n%{http_code}',
                'https://' + DOMAIN + path]
        if payload is not None:
            args += ['--header', 'Content-Type: application/json', '--data-binary', '@-']
            data = json.dumps(payload)
        elif token is not None:
            require(re.fullmatch(r'[A-Za-z0-9_.-]{20,8192}', token) is not None, 'login_token_invalid')
            args += ['--config', '-']
            data = 'header = "Authorization: Bearer ' + token + '"\n'
        else:
            data = None
        result = command(args, data=data)
        require(result.returncode == 0 and '\n' in result.stdout, 'https_request_failed')
        body, code = result.stdout.rsplit('\n', 1)
        require(code.isdigit(), 'https_response_invalid')
        return int(code), body

    def verify_login(self, password):
        code, body = self.http('/api/auth/admin/login', payload={'userName': self.user, 'password': password})
        self.state['login_http_status'] = code
        require(code == 200, 'web_login_failed_after_reset')
        try:
            response = json.loads(body)
            token = response['accessToken']
            require(response.get('tokenType') == 'Bearer' and isinstance(token, str), 'web_login_response_invalid')
        except (ValueError, KeyError, TypeError):
            raise RecoveryError('web_login_response_invalid') from None
        code, body = self.http('/api/admin/users', token=token)
        self.state['admin_http_status'] = code
        require(code == 200, 'admin_access_failed_after_reset')
        try:
            rows = json.loads(body)
            require(isinstance(rows, list) and any(isinstance(row, dict)
                    and row.get('userName', '').casefold() == self.user.casefold()
                    and row.get('isActive') is True for row in rows), 'admin_access_response_invalid')
        except (ValueError, TypeError, AttributeError):
            raise RecoveryError('admin_access_response_invalid') from None
        self.state['login_verified'] = True

    def backup(self, row, operation_id):
        st = BACKUPS.lstat()
        require(stat.S_ISDIR(st.st_mode) and st.st_uid == 0 and not st.st_mode & 0o022
                and BACKUPS.resolve(strict=True) == BACKUPS, 'private_backup_parent_unexpected')
        name = 'admin-reset-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12]
        directory = BACKUPS / name
        directory.mkdir(mode=0o700)
        target = directory / 'administrator-before.json'
        fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            json.dump({'revision': REVISION, 'database': DATABASE, 'operation_id': operation_id,
                       'administrator': row}, stream, ensure_ascii=False)
            stream.flush()
            os.fsync(stream.fileno())
        self.state['private_account_backup'] = str(target)

    def apply(self):
        self.state['phase'] = 'preflight'
        require(os.geteuid() == 0, 'root_required')
        self.service_ready()
        self.sql_tool = next((path for path in SQL_TOOLS if Path(path).is_file()), None)
        require(self.sql_tool is not None, 'sqlcmd_not_found')
        code, _ = self.http('/api/admin/users')
        require(code == 401, 'admin_endpoint_not_protected_or_unavailable')
        self.state['phase'] = 'sql-authentication'
        self.sql_login = prompt_secret('Usuario administrador SQL existente (oculto): ').strip()
        self.sql_password = prompt_secret('Contraseña SQL (oculta; no se guarda): ')
        require(re.fullmatch(r'[A-Za-z0-9_.@\\-]{1,128}', self.sql_login) is not None
                and self.sql_login != 'prodimt_pedidos_app' and bool(self.sql_password), 'sql_input_invalid')
        self.state['phase'] = 'account-inspection'
        row = validate_snapshot(self.sql(snapshot_sql(self.user)), self.user)
        self.state['phase'] = 'new-web-password'
        password = new_password()
        password_hash = identity_hash(password)
        operation = str(uuid.uuid4())
        self.state['phase'] = 'private-account-backup'
        self.backup(row, operation)
        self.service_ready()
        self.state.update(phase='password-reset', changes_may_have_occurred=True, operation_id=operation)
        receipt = self.sql(reset_sql(row, password_hash, operation))
        require(receipt == {'password_reset': 1, 'audit_recorded': 1}, 'reset_receipt_invalid')
        self.state.update(password_reset=True, changes_made=True, audit_recorded=True)
        self.state['phase'] = 'verify-login'
        self.verify_login(password)
        self.state.update(status='completed', phase='completed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--user', default='admin')
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    require(re.fullmatch(r'[A-Za-z0-9_.@-]{1,120}', args.user) is not None, 'web_user_invalid')
    recovery = Recovery(args.user)
    if not args.apply:
        print(json.dumps(recovery.state, ensure_ascii=False, indent=2))
        return 0
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    os.umask(0o077)
    fd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        st = os.fstat(fd)
        require(stat.S_ISREG(st.st_mode) and st.st_nlink == 1 and st.st_uid == 0
                and st.st_mode & 0o7777 == 0o600, 'recovery_lock_unexpected')
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RecoveryError('another_native_operation_is_running') from None
        try:
            recovery.apply()
        except (Exception, KeyboardInterrupt) as error:
            recovery.state.update(status='failed', issue=str(error) if isinstance(error, RecoveryError)
                                  else 'operation_interrupted' if isinstance(error, KeyboardInterrupt)
                                  else 'unexpected_local_failure')
        finally:
            recovery.sql_password = ''
        print(json.dumps(recovery.state, ensure_ascii=False, indent=2))
        return 0 if recovery.state['status'] == 'completed' else 1
    finally:
        os.close(fd)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt) as error:
        issue = str(error) if isinstance(error, RecoveryError) else 'recovery_not_started'
        print(json.dumps({'revision': REVISION, 'status': 'failed', 'issue': issue,
                          'changes_made': False, 'secret_values_printed': False}))
        sys.exit(1)
