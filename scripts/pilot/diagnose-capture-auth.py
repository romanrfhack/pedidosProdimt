#!/usr/bin/env python3
"""Read-only I07 preflight and SQL-administrator diagnosis; never runs apply.

Only the exact installer delivered with PRODIMT_Captura_Rapida_I07.zip is
loaded. Its package verifies the inspector. SQL errors are classified without
printing driver output, credentials, connection strings or configuration.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import stat
import subprocess
import sys
import types
import unicodedata

REVISION = '20261030-sql-auth-diagnosis2'
INSTALLER_SHA256 = '42b1e7d869a41d3d42851925585035089e5d58450993ebb16b89bb9d44c5fb1b'
SQLSTATES = frozenset(('28000', '08001', '08004', '08S01', 'HYT00', 'HYT01', 'IM002', 'IM003'))


class DiagnosisError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise DiagnosisError(issue)


def classify_error(stdout, stderr):
    # Only fixed categories, allowlisted SQLSTATEs and anchored numeric headers
    # leave this function. Never return a substring of the diagnostic streams.
    raw = (stdout or '') + '\n' + (stderr or '')
    normalized = ''.join(c for c in unicodedata.normalize('NFKD', raw.casefold())
                         if not unicodedata.combining(c))
    numbers = {int(n) for n in re.findall(
        r'(?m)^\s*Msg ([1-9][0-9]{0,5}), Level [0-9]{1,2}, State [0-9]{1,3},', raw)}
    # Connection failures commonly appear as Sqlcmd/ODBC text without Msg headers.
    known = (18401, 18452, 18456, 18470, 18486, 18487, 18488, 4060, 4064, 916)
    numbers.update(n for n in known if re.search(r'\(' + str(n) + r'\)', raw))
    states = sorted(state for state in SQLSTATES if re.search(
        r'(?:\[' + state + r'\]|SQLSTATE\s*[:=]?\s*' + state + r'\b)', raw, re.I))
    def has(*phrases):
        return any(phrase in normalized for phrase in phrases)
    if 18470 in numbers or has('account is disabled', 'cuenta esta deshabilitada'):
        category = 'sql_login_disabled'
    elif 18486 in numbers or has('account is currently locked', 'cuenta esta bloqueada'):
        category = 'sql_login_locked'
    elif numbers.intersection((18487, 18488)) or has('password has expired', 'must change the password'):
        category = 'sql_password_expired_or_change_required'
    elif numbers.intersection((4060, 4064, 916)) or has('cannot open database', 'cannot open user default database',
            'no se puede abrir la base de datos', 'not able to access the database'):
        category = 'sql_database_access_failed'
    elif numbers.intersection((18401, 18452, 18456)) or '28000' in states or has(
            'login failed', 'error de inicio de sesion', 'inicio de sesion del usuario', 'authentication failed'):
        category = 'sql_login_rejected'
    elif has('certificate', 'certificado', 'ssl provider', 'tls handshake'):
        category = 'sql_tls_or_certificate_failed'
    elif states and set(states).intersection(('IM002', 'IM003')) or has("can't open lib", 'data source name not found'):
        category = 'sql_odbc_driver_unavailable'
    elif has('invalid option', 'unknown option', 'unknown flag', 'unrecognized option', 'invalid argument'):
        category = 'sqlcmd_option_failed'
    elif has('invalid value for connection string attribute', 'invalid value specified for connection string attribute'):
        category = 'sql_client_attribute_failed'
    elif set(states).intersection(('HYT00', 'HYT01')) or has('timeout expired', 'timed out', 'tiempo de espera'):
        category = 'sql_connection_or_query_timeout'
    elif set(states).intersection(('08001', '08004', '08S01')) or has(
            'tcp provider', 'connection refused', 'server was not found', 'network-related'):
        category = 'sql_connection_failed'
    elif numbers.intersection((229, 262, 297, 15151, 15247)) or has('permission was denied', 'permission denied'):
        category = 'sql_permissions_failed'
    elif numbers:
        category = 'sql_statement_failed'
    else:
        category = 'sql_client_failure_unclassified'
    return dict(category=category, sql_error_numbers=sorted(numbers)[:10], sqlstates=states)


def trusted_installer(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(fd, 'rb') as stream:
            metadata = os.fstat(stream.fileno())
            require(stat.S_ISREG(metadata.st_mode) and metadata.st_nlink == 1 and metadata.st_uid == 0
                    and not metadata.st_mode & 0o022, 'installer_owner_or_type_unexpected')
            raw = stream.read(1024 * 1024 + 1)
    except OSError:
        raise DiagnosisError('installer_unreadable') from None
    require(hashlib.sha256(raw).hexdigest() == INSTALLER_SHA256, 'installer_sha256_mismatch')
    module = types.ModuleType('verified_i07_read_only_diagnosis')
    module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


def read_queries(module, instance):
    return {
        'administrator_permissions': module.ADMIN_QUERY,
        'migration_history': instance.inspector.SQL_MIGRATIONS,
        'rest_days_schema': module.SCHEMA_QUERY,
        'capture_alias_schema': module.ALIAS_SCHEMA_QUERY,
        'rest_days_data': 'SELECT COUNT_BIG(*) AS invalid FROM dbo.Customers WHERE RestDaysMask NOT BETWEEN 0 AND 127 '
                          'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;',
        'capture_alias_data': "SELECT COUNT_BIG(*) AS invalid FROM dbo.CustomerFrequentProducts WHERE CaptureAlias IS NOT NULL "
            "AND (LEN(CaptureAlias) NOT BETWEEN 1 AND 16 OR CaptureAlias COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Z]%') "
            'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
    }


class ReadOnlyProcess:
    def __init__(self, module, instance, report):
        self.module, self.instance, self.report = module, instance, report

    def run(self, args, **kwargs):
        module, instance = self.module, self.instance
        stage = None
        if args[0] == 'systemctl':
            require(args[1:3] == ['show', module.SERVICE] and len(args) == 4
                    and args[3].startswith('--property='), 'non_read_only_command_blocked')
        elif args[0] == 'curl':
            require('https://' + module.DOMAIN + '/health/db' in args
                    and not any(x in args for x in ('--request', '-X', '--data', '-d', '--upload-file', '-T')),
                    'non_read_only_command_blocked')
        else:
            require(args[0] == instance.sql_tool and args[0] in instance.inspector.SQL_TOOLS,
                    'non_read_only_command_blocked')
            require(args == [instance.sql_tool, '-S', module.SERVER, '-d', module.DATABASE, '-C', '-I', '-b',
                             '-l', '5', '-t', '25', '-x', '-y', '0', '-w', '65535'], 'sql_protocol_unexpected')
            for name, query in read_queries(module, instance).items():
                wrapped = module.OPTIONS + 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
                if kwargs.get('input') == wrapped:
                    stage = name
                    break
            require(stage is not None, 'non_read_only_sql_blocked')
        try:
            result = subprocess.run(args, **kwargs)
        except (OSError, subprocess.TimeoutExpired, UnicodeError) as error:
            if stage:
                self.report['sql_reads'].append(dict(stage=stage, succeeded=False, process_error=type(error).__name__))
            raise
        if stage:
            row = dict(stage=stage, succeeded=result.returncode == 0, exit_code=result.returncode)
            if result.returncode:
                row.update(classify_error(result.stdout, result.stderr))
            elif stage == 'administrator_permissions':
                try:
                    metadata = instance.inspector.parse_sql_output(result.stdout)
                    if isinstance(metadata, dict) and metadata.get('database_name') == module.DATABASE:
                        names = ('sysadmin', 'dbcreator', 'db_owner', 'backup_permission', 'verify_permission', 'view_definition')
                        self.report['administrator_permissions'] = {name: metadata.get(name)
                            if type(metadata.get(name)) is int and metadata[name] in (0, 1) else None for name in names}
                except (ValueError, TypeError, instance.inspector.InspectionError):
                    pass
            self.report['sql_reads'].append(row)
        return result


def diagnose(package, installer):
    report = dict(revision=REVISION, mode='read-only', changes_made=False, database_migrations_run=False,
                  secret_values_printed=False, status='failed', phase='verify-installer', sql_reads=[])
    module = instance = None
    try:
        module = trusted_installer(installer)
        report['installer_sha256_verified'] = True
        instance = module.Installer(package)
        process = ReadOnlyProcess(module, instance, report)
        module.subprocess = types.SimpleNamespace(run=process.run, TimeoutExpired=subprocess.TimeoutExpired, DEVNULL=subprocess.DEVNULL)
        report['phase'] = 'application-read-only-preflight'
        instance.preflight()
        report['preflight_completed'] = True
        report['database_checks'] = instance.state['database_checks']
        report['sqlcmd_path'] = instance.sql_tool
        report['phase'] = 'administrator-read-only-checks'
        # These exact methods perform SELECTs, path checks and HTTPS GET only.
        # Never call apply, stage, backup_and_migrate, switch or rollback.
        instance.authenticate_admin()
        report.update(administrator_checks_completed=True, migrations_before=instance.state['migrations_before'],
                      status='inspected', phase='completed')
    except (Exception, KeyboardInterrupt) as error:
        if isinstance(error, DiagnosisError) or module is not None and isinstance(error, module.InstallError):
            report['issue'] = str(error)
        else:
            report['issue'] = 'diagnosis_interrupted' if isinstance(error, KeyboardInterrupt) else 'diagnostic_local_failure'
    finally:
        if instance is not None:
            instance.sql_login = instance.sql_password = ''
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-quick-capture-payload.zip'))
    parser.add_argument('--installer', type=Path, default=Path('/root/PRODIMT_Instalar_I07.py'))
    args = parser.parse_args()
    if os.geteuid() != 0:
        print(json.dumps(dict(mode='read-only', changes_made=False, status='failed', issue='root_required')))
        return 1
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    report = diagnose(args.package, args.installer)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report['status'] == 'inspected' else 1


if __name__ == '__main__':
    sys.exit(main())
