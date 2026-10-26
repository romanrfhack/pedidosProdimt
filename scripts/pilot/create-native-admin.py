#!/usr/bin/env python3
"""Create one generated web administrator, verify login, then hand off locally.

Requires the exact previously delivered recovery module already on the VPS.
No existing account, password, SQL login, configuration or service is changed.
The generated web credential is stored root-only to survive a lost response.
"""
import argparse
import base64
import fcntl
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import resource
import secrets
import stat
import sys
import types
import uuid

REVISION = '20261026-admin-create1'
USER = 'roman.prodimt'
SOURCE = Path('/root/PRODIMT_Restablecer_Admin.py')
SOURCE_SHA256 = '5072118445363332090066bae939ec3977539a95a997008a02f1dc52e7111a15'
DIRECTORY = Path('/var/backups/prodimt-pedidos/admin-create-roman-prodimt')
RECORD = 'credentials.json'


class CreationError(Exception):
    """Only fixed, public issue codes may be passed here."""


def require(condition, issue):
    if not condition:
        raise CreationError(issue)


def checked_read(path, mode=None):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        st = os.fstat(stream.fileno())
        require(stat.S_ISREG(st.st_mode) and st.st_uid == 0 and st.st_nlink == 1
                and not st.st_mode & 0o022 and (mode is None or st.st_mode & 0o7777 == mode),
                'private_file_permissions_unexpected')
        data = stream.read(32769)
        require(len(data) <= 32768, 'private_file_too_large')
        return data


def module_from_bytes(raw):
    require(hashlib.sha256(raw).hexdigest() == SOURCE_SHA256, 'previous_recovery_module_mismatch')
    module = types.ModuleType('prodimt_reviewed_recovery')
    exec(compile(raw, str(SOURCE), 'exec'), module.__dict__)
    return module


def new_record(base):
    password = secrets.token_urlsafe(24)
    return {'revision': REVISION, 'web_user': USER, 'database': base.DATABASE,
            'server': base.SERVER, 'domain': base.DOMAIN, 'user_id': str(uuid.uuid4()),
            'operation_id': str(uuid.uuid4()), 'password': password,
            'password_hash': base.identity_hash(password)}


def validate_record(base, record):
    require(isinstance(record, dict) and set(record) == {'revision', 'web_user', 'database', 'server',
            'domain', 'user_id', 'operation_id', 'password', 'password_hash'}, 'credential_record_invalid')
    require(all(isinstance(value, str) for value in record.values()), 'credential_record_invalid')
    require(record['revision'] == REVISION and record['web_user'] == USER
            and record['database'] == base.DATABASE and record['server'] == base.SERVER
            and record['domain'] == base.DOMAIN, 'credential_record_target_mismatch')
    require(re.fullmatch(r'[A-Za-z0-9_-]{32}', record['password']) is not None, 'credential_record_invalid')
    try:
        require(str(uuid.UUID(record['user_id'])) == record['user_id']
                and str(uuid.UUID(record['operation_id'])) == record['operation_id'], 'credential_record_invalid')
        raw = base64.b64decode(record['password_hash'], validate=True)
        require(len(raw) == 61 and raw[:13] == b'\x01\x00\x00\x00\x02\x00\x01\x86\xa0\x00\x00\x00\x10',
                'credential_record_hash_invalid')
        derived = hashlib.pbkdf2_hmac('sha512', record['password'].encode(), raw[13:29], 100000, dklen=32)
        require(hmac.compare_digest(raw[29:], derived), 'credential_record_hash_invalid')
    except (ValueError, TypeError):
        raise CreationError('credential_record_invalid') from None
    return record


def private_directory(path, *, exact_mode=None):
    st = path.lstat()
    require(stat.S_ISDIR(st.st_mode) and st.st_uid == 0 and not st.st_mode & 0o022
            and path.resolve(strict=True) == path
            and (exact_mode is None or st.st_mode & 0o7777 == exact_mode), 'private_directory_unexpected')


def stored_record(base):
    private_directory(DIRECTORY.parent)
    if not DIRECTORY.exists():
        DIRECTORY.mkdir(mode=0o700)
    private_directory(DIRECTORY, exact_mode=0o700)
    require(set(p.name for p in DIRECTORY.iterdir()) <= {RECORD}, 'credential_directory_content_unexpected')
    path = DIRECTORY / RECORD
    if path.exists():
        try:
            return validate_record(base, json.loads(checked_read(path, mode=0o600)))
        except (ValueError, UnicodeError):
            raise CreationError('credential_record_invalid') from None
    record = validate_record(base, new_record(base))
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'w', encoding='utf-8') as stream:
        json.dump(record, stream, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    directory_fd = os.open(DIRECTORY, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(directory_fd)
    finally:
        os.close(directory_fd)
    return record


def baseline(base, data):
    # SQL Server can omit the JSON_QUERY property when its subquery has no
    # rows. The successful fixed query and schema checks distinguish this
    # legitimate empty account result from a failed SQL/JSON response.
    require(isinstance(data, dict) and data.get('database_name') == base.DATABASE
            and data.get('migrations_count') == 9 and data.get('i04_migration_present') == 1
            and isinstance(data.get('users', []), list) and len(data.get('users', [])) <= 1, 'database_not_reviewed_i04')
    return data.get('users', [])


def creation_sql(base, record):
    record = validate_record(base, record)
    text = base.sql_text
    return ("DECLARE @id uniqueidentifier='" + record['user_id'] + "', @operation uniqueidentifier='" +
            record['operation_id'] + "', @created int=0;\n"
            "DECLARE @metadata nvarchar(2000)=N'{\"method\":\"native-new-admin\",\"revision\":\"" + REVISION + "\"}';\n"
            "BEGIN TRANSACTION;\n"
            "IF EXISTS (SELECT 1 FROM dbo.AdminUsers WITH (UPDLOCK,HOLDLOCK) WHERE UserName=" + text(USER) + ")\n"
            "BEGIN\n"
            " IF NOT EXISTS (SELECT 1 FROM dbo.AdminUsers WHERE Id=@id AND IsActive=1"
            " AND UserName COLLATE Latin1_General_100_BIN2=" + text(USER) +
            " AND PasswordHash COLLATE Latin1_General_100_BIN2=" + text(record['password_hash']) + ")\n"
            " OR NOT EXISTS (SELECT 1 FROM dbo.AuditLogs WHERE Id=@operation AND EntityType=N'AdminUser'"
            " AND EntityId=CONVERT(nvarchar(36),@id) AND EventType=N'AdminUserCreated'"
            " AND ActorType=2 AND MetadataJson=@metadata)\n"
            " THROW 51021,N'Existing account does not belong to this operation',1;\n"
            "END\nELSE\nBEGIN\n"
            " INSERT INTO dbo.AdminUsers (Id,UserName,PasswordHash,DisplayName,IsActive,CreatedAt)"
            " VALUES (@id," + text(USER) + "," + text(record['password_hash']) +
            ",N'Administrador PRODIMT',1,SYSUTCDATETIME());\n"
            " INSERT INTO dbo.AuditLogs (Id,EntityType,EntityId,EventType,OccurredAt,ActorType,ActorId,"
            "ActorDisplayName,Summary,MetadataJson) VALUES (@operation,N'AdminUser',CONVERT(nvarchar(36),@id),"
            "N'AdminUserCreated',SYSUTCDATETIME(),2,NULL,N'Recuperacion local VPS',"
            "N'Cuenta administrativa nueva creada mediante recuperacion local autorizada.',@metadata);\n"
            " SET @created=1;\nEND;\nCOMMIT TRANSACTION;\n"
            "SELECT (SELECT @created AS account_created,1 AS account_verified,1 AS audit_verified"
            " FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS ProdimtRecoveryJson;\n")


def apply(base, recovery):
    state = recovery.state
    state['phase'] = 'preflight'
    recovery.service_ready()
    recovery.sql_tool = next((p for p in base.SQL_TOOLS if Path(p).is_file()), None)
    require(recovery.sql_tool is not None, 'sqlcmd_not_found')
    require(recovery.http('/api/admin/users')[0] == 401, 'admin_endpoint_not_protected_or_unavailable')
    state['phase'] = 'sql-authentication'
    recovery.sql_login = base.prompt_secret('Usuario administrador SQL existente (oculto): ').strip()
    recovery.sql_password = base.prompt_secret('Contraseña SQL (oculta; no se guarda): ')
    require(re.fullmatch(r'[A-Za-z0-9_.@\\-]{1,128}', recovery.sql_login) is not None
            and recovery.sql_login != 'prodimt_pedidos_app' and bool(recovery.sql_password), 'sql_input_invalid')
    state['phase'] = 'account-inspection'
    existing = baseline(base, recovery.sql(base.snapshot_sql(USER)))
    # A name collision without this tool's private record cannot adopt an old user.
    require(not existing or (DIRECTORY / RECORD).exists(), 'existing_web_user_not_owned_by_operation')
    state['phase'] = 'generate-or-reuse-private-credential'
    record = stored_record(base)
    state['private_credential_file'] = str(DIRECTORY / RECORD)
    recovery.service_ready()
    state.update(phase='create-account', changes_may_have_occurred=True, operation_id=record['operation_id'])
    receipt = recovery.sql(creation_sql(base, record))
    require(receipt in ({'account_created': 1, 'account_verified': 1, 'audit_verified': 1},
                        {'account_created': 0, 'account_verified': 1, 'audit_verified': 1}), 'creation_receipt_invalid')
    created = receipt['account_created'] == 1
    state.update(changes_made=created, account_created=created, existing_operation_reused=not created, audit_verified=True)
    state['phase'] = 'verify-created-account'
    row = base.validate_snapshot(recovery.sql(base.snapshot_sql(USER)), USER)
    require(row['id'].lower() == record['user_id'] and row['password_hash'] == record['password_hash'],
            'created_account_verification_failed')
    state['account_verified'] = True
    state['phase'] = 'verify-login'
    recovery.verify_login(record['password'])
    state.update(status='completed', phase='completed')
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    state = {'revision': REVISION, 'status': 'planned', 'web_user': USER,
             'changes_made': False, 'account_created': False, 'account_verified': False,
             'login_verified': False, 'credentials_printed': False}
    if not args.apply:
        print(json.dumps(state, indent=2))
        return 0
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    os.umask(0o077)
    base = module_from_bytes(checked_read(SOURCE))
    recovery = base.Recovery(USER)
    recovery.state = state
    fd = os.open(base.LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        st = os.fstat(fd)
        require(stat.S_ISREG(st.st_mode) and st.st_uid == 0 and st.st_nlink == 1
                and st.st_mode & 0o7777 == 0o600, 'recovery_lock_unexpected')
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise CreationError('another_native_operation_is_running') from None
        record = None
        try:
            record = apply(base, recovery)
        except (Exception, KeyboardInterrupt) as error:
            issue = str(error) if isinstance(error, (CreationError, base.RecoveryError)) else 'local_operation_failed'
            state.update(status='failed', issue=issue.replace('_after_reset', '_after_creation'))
        finally:
            recovery.sql_password = ''
        if state['status'] == 'completed':
            state['phase'] = 'credential-handoff'
            try:
                with open('/dev/tty', 'w', encoding='utf-8') as tty:
                    require(tty.isatty(), 'credential_handoff_requires_tty')
                    tty.write('\nACCESO WEB VERIFICADO — guarda estos datos; no los pegues en el chat.\n'
                              'Usuario: ' + USER + '\nContraseña: ' + record['password'] + '\n\n')
                    tty.flush()
                state.update(credentials_printed=True, phase='completed')
            except (OSError, CreationError):
                state.update(status='failed', issue='credential_handoff_failed')
        print(json.dumps(state, ensure_ascii=False, indent=2))
        return 0 if state['status'] == 'completed' else 1
    finally:
        os.close(fd)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt) as error:
        issue = str(error) if isinstance(error, CreationError) else 'creation_not_completed'
        print(json.dumps({'revision': REVISION, 'status': 'failed', 'issue': issue,
                          'changes_may_have_occurred': True, 'credentials_printed': False}))
        sys.exit(1)
