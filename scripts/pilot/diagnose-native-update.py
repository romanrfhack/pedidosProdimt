#!/usr/bin/env python3
"""Read-only diagnosis after the native I04 migration failure.

The exact original ZIP supplies its trusted inspector in memory. This program
never replays the migration script, prompts for credentials, stops a service,
changes a release, or writes a report to the server. Only allowlisted metadata
and numeric SQL/session properties reach stdout.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import types
import zipfile

REVISION = '20261025-sql-diagnosis1'
PACKAGE_SHA = '43520c99c4906603fc3613e46ebdac9a460f02c78193511f3dae3bcff34bdf32'
PACKAGE_ROOT = 'prodimt-i04-nativo/'
SERVICE = 'prodimt-pedidos-api.service'
ROOT = Path('/var/www/prodimt-pedidos')
OLD = {'backend': ROOT / 'backend/releases/20260528151723-492bbe9',
       'frontend': ROOT / 'frontend/releases/20260529120920-frontend-api-relative'}
NEW = {part: ROOT / part / 'releases/20261024-i04-b630072' for part in OLD}
BACKUP = Path('/var/opt/mssql/backup/prodimt-i04-20261025T142158Z-11ceb110bb44/ProdimtPedidos-copy-only.bak')
PRIVATE = Path('/var/backups/prodimt-pedidos/i04-20261025T142158Z-11ceb110bb44')
SERVER, DATABASE, LOGIN = '127.0.0.1,14330', 'ProdimtPedidos', 'prodimt_pedidos_app'
SQL_TOOLS = ('/opt/mssql-tools/bin/sqlcmd', '/opt/mssql-tools18/bin/sqlcmd')
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
MIGRATIONS = (
    '20260527190837_InitialCreate', '20260528021837_AddOrderAuditLogs',
    '20260528025208_AddPilotAuthentication', '20260528035015_AddCatalogManagementSupport',
    '20260528131416_AddCatalogExternalCodesAndImportSupport',
    '20261024164610_AddPilotCatalogAndOrderSnapshots', '20261024181206_AddDailyOrderOperations',
    '20261024181934_ExpandOrderAuditSnapshots', '20261024202253_AddOrderChangeNotices')
SQL_HISTORY = 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'
SQL_STATE = """SELECT DB_NAME() AS database_name,
 (SELECT compatibility_level FROM sys.databases WHERE database_id=DB_ID()) AS compatibility_level,
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS server_version,
 CAST(SESSIONPROPERTY('QUOTED_IDENTIFIER') AS int) AS quoted_identifier,
 CAST(SESSIONPROPERTY('ANSI_NULLS') AS int) AS ansi_nulls,
 CAST(SESSIONPROPERTY('ANSI_PADDING') AS int) AS ansi_padding,
 CAST(SESSIONPROPERTY('ANSI_WARNINGS') AS int) AS ansi_warnings,
 CAST(SESSIONPROPERTY('ARITHABORT') AS int) AS arithabort,
 CAST(SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL') AS int) AS concat_null_yields_null,
 CAST(SESSIONPROPERTY('NUMERIC_ROUNDABORT') AS int) AS numeric_roundabort,
 HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') AS can_view_database_definition,
 (SELECT COUNT_BIG(*) FROM dbo.Orders) AS orders_count,
 (SELECT MAX(CAST(is_unique AS int)) FROM sys.indexes
  WHERE object_id=OBJECT_ID(N'dbo.Orders') AND name=N'IX_Orders_CustomerId_OrderDate_SequenceNumber') AS order_sequence_index_unique,
 COL_LENGTH(N'dbo.Products',N'UnitCode') AS products_unit_code_bytes,
 COL_LENGTH(N'dbo.Orders',N'Version') AS orders_version_bytes,
 COL_LENGTH(N'dbo.Orders',N'CustomerNameSnapshot') AS orders_customer_snapshot_bytes,
 COL_LENGTH(N'dbo.CustomerFrequentProducts',N'InterpretationPosition') AS profile_position_bytes,
 COL_LENGTH(N'dbo.Orders',N'Source') AS orders_source_bytes,
 COL_LENGTH(N'dbo.Orders',N'ExternalOrderKey') AS orders_external_key_bytes,
 COL_LENGTH(N'dbo.OrderAuditLogs',N'MetadataJson') AS audit_metadata_bytes,
 CASE WHEN OBJECT_ID(N'dbo.OrderOperations',N'U') IS NULL THEN NULL ELSE 1 END AS operations_visible,
 CASE WHEN OBJECT_ID(N'dbo.OrderChangeNotices',N'U') IS NULL THEN NULL ELSE 1 END AS notices_visible,
 (SELECT MAX(CAST(is_unique AS int)) FROM sys.indexes
  WHERE object_id=OBJECT_ID(N'dbo.OrderChangeNotices') AND name=N'IX_OrderChangeNotices_OrderId_OrderVersion_MachineId') AS notice_revision_index_unique,
 (SELECT MAX(CAST(has_filter AS int)) FROM sys.indexes
  WHERE object_id=OBJECT_ID(N'dbo.OrderChangeNotices') AND name=N'IX_OrderChangeNotices_OrderId_OrderVersion_MachineId') AS notice_revision_index_filtered
 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER;"""
QUERIES = (SQL_HISTORY, SQL_STATE)
SESSION_FIELDS = ('quoted_identifier', 'ansi_nulls', 'ansi_padding', 'ansi_warnings',
                  'arithabort', 'concat_null_yields_null', 'numeric_roundabort')
SCHEMA_FIELDS = ('order_sequence_index_unique', 'products_unit_code_bytes', 'orders_version_bytes',
                 'orders_customer_snapshot_bytes', 'profile_position_bytes', 'orders_source_bytes',
                 'orders_external_key_bytes', 'audit_metadata_bytes', 'operations_visible', 'notices_visible',
                 'notice_revision_index_unique', 'notice_revision_index_filtered')


class DiagnosisError(Exception):
    """Only fixed non-sensitive codes are allowed."""


def require(value, issue):
    if not value:
        raise DiagnosisError(issue)


def command(args, *, env=None, timeout=20):
    try:
        completed = subprocess.run(args, env=CLEAN_ENV if env is None else env,
                                   stdin=subprocess.DEVNULL, capture_output=True,
                                   text=True, timeout=timeout, check=False)
        return completed.returncode, completed.stdout
    except (OSError, UnicodeError, subprocess.TimeoutExpired):
        return 127, ''


def trusted_payload(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(fd, 'rb') as stream:
            require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), 'package_not_regular')
            data = stream.read(64 * 1024 * 1024 + 1)
        require(hashlib.sha256(data).hexdigest() == PACKAGE_SHA, 'package_sha_mismatch')
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            wanted = ('inspect-native.py', 'backend/Prodimt.Pedidos.Api.dll', 'frontend/index.html')
            return {name: archive.read(PACKAGE_ROOT + name) for name in wanted}
    except DiagnosisError:
        raise
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        raise DiagnosisError('package_unreadable') from None


def inspector_module(payload):
    module = types.ModuleType('verified_prodimt_diagnostic_inspector')
    module.__file__ = '<verified-payload>/inspect-native.py'
    exec(compile(payload['inspect-native.py'], module.__file__, 'exec'), module.__dict__)
    return module


def integer(value, minimum=0, maximum=2**63 - 1):
    return value if type(value) is int and minimum <= value <= maximum else None


def version(value):
    return value if isinstance(value, str) and re.fullmatch(r'\d{1,3}(?:\.\d{1,5}){1,3}', value) else None


def migration_summary(rows):
    require(isinstance(rows, list) and all(isinstance(row, dict) and isinstance(row.get('MigrationId'), str) for row in rows),
            'migration_metadata_unrecognized')
    known = [row['MigrationId'] for row in rows if row['MigrationId'] in MIGRATIONS]
    unknown = sum(row['MigrationId'] not in MIGRATIONS for row in rows)
    prefix = not unknown and known == list(MIGRATIONS[:len(known)])
    return {'known_migrations': known, 'known_count': len(known), 'unknown_count': unknown,
            'is_expected_prefix': prefix, 'expected_total': len(MIGRATIONS),
            'pending_known_count': len(MIGRATIONS) - len(set(known)),
            'automatic_retry_authorized': False}


def state_summary(raw):
    require(isinstance(raw, dict) and raw.get('database_name') == DATABASE, 'database_identity_mismatch')
    return {'database': DATABASE, 'compatibility_level': integer(raw.get('compatibility_level'), maximum=200),
            'server_version': version(raw.get('server_version')),
            'session_without_quoted_identifier_flag': {name: integer(raw.get(name), maximum=1) for name in SESSION_FIELDS},
            'orders_count': integer(raw.get('orders_count')),
            'can_view_database_definition': integer(raw.get('can_view_database_definition'), maximum=1),
            'schema_metadata': {name: integer(raw.get(name), minimum=-1, maximum=65536) for name in SCHEMA_FIELDS},
            'null_schema_metadata_may_mean_not_visible': True}


def query_sql(inspector, tool, connection, query):
    require(tool in SQL_TOOLS and query in QUERIES and inspector.permitted_connection(connection), 'sql_query_not_authorized')
    env = dict(CLEAN_ENV, SQLCMDUSER=LOGIN, SQLCMDPASSWORD=connection['password'])
    wrapped = 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
    # Deliberately preserve tty3's protocol: no -I flag and no SET QUOTED_IDENTIFIER.
    args = [tool, '-S', SERVER, '-d', DATABASE, '-C', '-b', '-l', '5', '-t', '15', '-x',
            '-y', '0', '-w', '65535', '-Q', 'SET NOCOUNT ON; SET LOCK_TIMEOUT 3000; ' + wrapped]
    code, output = command(args, env=env)
    require(code == 0, 'sql_read_failed')
    try:
        return inspector.parse_sql_output(output)
    except inspector.InspectionError:
        raise DiagnosisError('sql_metadata_unrecognized') from None


def metadata(path):
    try:
        st = path.lstat()
        return {'exists': True, 'regular_file': stat.S_ISREG(st.st_mode), 'symlink': stat.S_ISLNK(st.st_mode),
                'bytes': st.st_size, 'uid': st.st_uid, 'gid': st.st_gid, 'mode': oct(st.st_mode & 0o777)}
    except FileNotFoundError:
        return {'exists': False}
    except OSError:
        return {'issue': 'metadata_unavailable'}


def release_kind(path, part):
    try:
        resolved = path.resolve(strict=True)
        return 'previous' if resolved == OLD[part] else 'i04_staged' if resolved == NEW[part] else 'unexpected'
    except OSError:
        return 'unavailable'


def staged_summary(payload):
    result = {}
    for part, name in (('backend', 'Prodimt.Pedidos.Api.dll'), ('frontend', 'index.html')):
        item = {'directory_exists': NEW[part].is_dir(), 'directory_is_symlink': NEW[part].is_symlink()}
        try:
            path = NEW[part] / name
            st = path.lstat()
            require(stat.S_ISREG(st.st_mode) and st.st_size <= 20 * 1024 * 1024, 'staged_probe_file_unexpected')
            item['probe_matches_package'] = hashlib.sha256(path.read_bytes()).digest() == hashlib.sha256(payload[part + '/' + name]).digest()
        except (OSError, DiagnosisError):
            item['probe_matches_package'] = False
        result[part] = item
    return result


def health():
    args = ['curl', '--silent', '--show-error', '--noproxy', '*', '--max-time', '5', '--max-filesize', '65536',
            '--resolve', 'pedidos.prodimt.site:443:127.0.0.1', '--write-out', '\n%{http_code}',
            'https://pedidos.prodimt.site/health/db']
    code, output = command(args, timeout=8)
    try:
        body, status = output.rsplit('\n', 1)
        return {'http_status': int(status) if status.isdigit() else None,
                'database_reachable': code == 0 and status == '200' and json.loads(body).get('database') == 'reachable'}
    except (ValueError, TypeError, AttributeError):
        return {'database_reachable': False}


def collect(package, result):
    require(os.geteuid() == 0, 'diagnosis_requires_root')
    payload = trusted_payload(package)
    inspector = inspector_module(payload)
    code, text = command(['systemctl', 'show', SERVICE, '--no-pager', '--property=MainPID,ActiveState'])
    require(code == 0, 'service_metadata_unavailable')
    props = dict(row.split('=', 1) for row in text.splitlines() if '=' in row)
    active = props.get('ActiveState')
    require(active in ('active', 'inactive', 'failed', 'activating', 'deactivating', 'reloading'), 'service_state_unrecognized')
    pid = int(props.get('MainPID', '0'))
    result['service'] = {'active_state': active, 'pid': pid,
                         'current_releases': {part: release_kind(ROOT / part / 'current', part) for part in OLD}}
    result['backup'] = {'path': str(BACKUP), **metadata(BACKUP)}
    result['private_snapshot'] = {'path': str(PRIVATE), 'exists': PRIVATE.is_dir()}
    result['staged_releases'] = staged_summary(payload)
    result['health'] = health()
    require(active == 'active' and pid > 0, 'service_not_running')
    identity = inspector.process_identity(pid)
    cwd = Path(f'/proc/{pid}/cwd').resolve(strict=True)
    result['service']['process_release'] = release_kind(cwd, 'backend')
    environment = inspector.process_environment(pid)
    argv = inspector.read_limited(f'/proc/{pid}/cmdline').rstrip('\0').split('\0')
    cfg, _ = inspector.configuration(environment, argv, cwd)
    connection = inspector.connection_fields(inspector.first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING'))
    require(inspector.permitted_connection(connection), 'sql_target_or_login_unexpected')
    inspector.assert_same_process(pid, identity)
    try:
        tool = next((name for name in SQL_TOOLS if Path(name).is_file() and os.access(name, os.X_OK)), None)
        require(tool is not None, 'sqlcmd_unavailable')
        code, help_text = command([tool, '-?'])
        match = re.search(r'(?im)^\s*Version\s+(\d{1,3}(?:\.\d{1,5}){1,3})\b', help_text)
        result['sqlcmd'] = {'path': tool, 'version': version(match[1]) if match else None,
                            'quoted_identifier_flag_used': False}
        result['sql'] = state_summary(query_sql(inspector, tool, connection, SQL_STATE))
        result['sql']['migration_history'] = migration_summary(query_sql(inspector, tool, connection, SQL_HISTORY))
        inspector.assert_same_process(pid, identity)
        result['status'] = 'inspected'
    finally:
        connection['password'] = ''
    return result


def inspect(package):
    result = {'revision': REVISION, 'mode': 'read-only', 'changes_made': False, 'secret_values_printed': False,
              'migration_replay_attempted': False, 'automatic_retry_authorized': False}
    try:
        return collect(package, result)
    except DiagnosisError as error:
        result.update(status='incomplete', issue=str(error))
    except Exception:
        result.update(status='incomplete', issue='diagnosis_incomplete')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True, help='Previously delivered original native I04 ZIP')
    args = parser.parse_args()
    try:
        report = inspect(args.package)
    except DiagnosisError as error:
        report = {'revision': REVISION, 'mode': 'read-only', 'changes_made': False,
                  'secret_values_printed': False, 'issue': str(error)}
    except Exception:
        report = {'revision': REVISION, 'mode': 'read-only', 'changes_made': False,
                  'secret_values_printed': False, 'issue': 'diagnosis_incomplete'}
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report.get('status') == 'inspected' else 1


if __name__ == '__main__':
    sys.exit(main())
