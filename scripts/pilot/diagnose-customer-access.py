#!/usr/bin/env python3
"""Inspect the exact delivered I09 preflight without changing application or SQL.

No SQL-administrator credentials, apply mode, service mutation or configuration
write. Only the original pinned installer's preflight is invoked. Its fixed SQL
metadata result is captured before the original equality guard and redacted.
"""
import argparse
import builtins
import hashlib
import json
import os
from pathlib import Path
import resource
import stat
import subprocess
import sys
import types

REVISION = '20261031-customer-access-diagnosis1'
INSTALLER_SHA256 = 'd9623be63603d0448b532828f0c2063e0da703a27ea84d303f072507f1bc68c2'
PAYLOAD_SHA256 = '90c0e6505212f5165df56107a70d19085bb920ebdbf63c9bfc083fb7e0723d5c'
SOURCE = '6cfdc7ff366a2caa7dac399b9842b3630a30328a'
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SERVER = '127.0.0.1,14330'
DATABASE = 'ProdimtPedidos'
SQL_TOOLS = ('/opt/mssql-tools/bin/sqlcmd', '/opt/mssql-tools18/bin/sqlcmd')
COUNTERS = ('migrations_count', 'i07', 'customer_channel', 'admin_channel')
PERMISSIONS = ('read_customers', 'update_customers', 'read_tokens', 'insert_tokens', 'update_tokens',
               'read_profiles', 'read_orders', 'insert_orders', 'insert_lines', 'read_operations',
               'insert_operations', 'insert_order_audit', 'insert_audit')
KEYS = ('database_name', *COUNTERS, *PERMISSIONS)
SAFE_ISSUES = frozenset(('database_permissions_schema_or_channels_unexpected', 'previous_release_not_pinned',
    'mixed_application_releases', 'current_release_not_symlink', 'production_configuration_unexpected',
    'sql_target_unexpected', 'previous_health_failed', 'previous_frontend_not_served',
    'service_identity_unexpected', 'running_release_unexpected', 'service_configuration_changed',
    'current_release_changed', 'directory_owner_or_path_unexpected', 'file_owner_or_type_unexpected',
    'existing_configuration_not_in_package', 'disk_space_insufficient', 'local_command_failed'))


class DiagnosisError(Exception):
    """Only fixed non-sensitive codes may be reported."""


def require(condition, issue):
    if not condition:
        raise DiagnosisError(issue)


def trusted_installer(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(fd, 'rb') as stream:
            meta = os.fstat(stream.fileno())
            require(stat.S_ISREG(meta.st_mode) and meta.st_nlink == 1 and meta.st_uid == 0
                    and not meta.st_mode & 0o022, 'installer_owner_or_type_unexpected')
            raw = stream.read(1024 * 1024 + 1)
    except OSError:
        raise DiagnosisError('installer_unreadable') from None
    require(hashlib.sha256(raw).hexdigest() == INSTALLER_SHA256, 'installer_sha256_mismatch')
    module = types.ModuleType('verified_i09_read_only_diagnosis')
    module.__file__ = str(path)
    builtins.exec(compile(raw, str(path), 'exec'), module.__dict__)
    require(module.EXPECTED_SOURCE == SOURCE and module.EXPECTED_PAYLOAD_SHA256 == PAYLOAD_SHA256,
            'installer_payload_identity_unexpected')
    require(tuple(module.EXPECTED_DATABASE) == KEYS, 'installer_metadata_contract_unexpected')
    return module


def capture_checks(result, expected, report):
    report['sql_metadata_read_completed'] = True
    if not isinstance(result, dict):
        report.update(database_result_is_object=False, database_mismatches=list(KEYS))
        return
    checks = {'database_name_matches': result.get('database_name') == DATABASE}
    missing, invalid = [], []
    for key in KEYS:
        if key not in result:
            missing.append(key)
        if key == 'database_name':
            if key in result and not isinstance(result[key], str):
                invalid.append(key)
            continue
        value = result.get(key)
        valid = type(value) is int and (0 <= value <= 10000 if key in COUNTERS else value in (0, 1))
        checks[key] = value if valid else None
        if key in result and not valid:
            invalid.append(key)
    report.update(database_result_is_object=True, database_checks=checks, missing_fields=missing,
                  invalid_fields=invalid, database_mismatches=[key for key in KEYS if result.get(key) != expected[key]],
                  database_contract_matches=result == expected,
                  unexpected_fields_present=bool(set(result) - set(KEYS)))


class ReadOnlyProcess:
    def __init__(self, query, report):
        self.query, self.report = query, report

    def run(self, args, **kwargs):
        require(isinstance(args, (tuple, list)) and args, 'non_read_only_command_blocked')
        if args[0] == 'systemctl':
            require(args == ['systemctl', 'show', SERVICE,
                '--property=ActiveState,MainPID,User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'],
                'non_read_only_command_blocked')
        elif args[0] == 'curl':
            prefix = ['curl', '--silent', '--show-error', '--noproxy', '*', '--max-time', '8',
                      '--max-filesize', '2097152', '--resolve', DOMAIN + ':443:127.0.0.1',
                      '--write-out', '\n%{http_code}']
            require(list(args) in [prefix + ['https://' + DOMAIN + path] for path in ('/health/db', '/')],
                    'non_read_only_command_blocked')
        else:
            wrapped = ('SET NOCOUNT ON; SET LOCK_TIMEOUT 3000; SELECT ('
                       + self.query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;')
            require(args[0] in SQL_TOOLS and list(args) == [args[0], '-S', SERVER, '-d', DATABASE, '-C', '-b',
                '-l', '5', '-t', '10', '-y', '0', '-w', '65535', '-x', '-Q', wrapped], 'non_read_only_sql_blocked')
        require('input' not in kwargs and not kwargs.get('shell', False), 'non_read_only_command_blocked')
        return subprocess.run(args, **kwargs)


def guard_module(name, namespace, installer, process, report):
    require(name in ('reviewed_i09_activation_runtime', 'reviewed_i09_inventory'), 'unexpected_dependency_execution_blocked')
    namespace['subprocess'] = types.SimpleNamespace(run=process.run, TimeoutExpired=subprocess.TimeoutExpired,
                                                   DEVNULL=subprocess.DEVNULL)
    if name == 'reviewed_i09_inventory':
        query_sql = namespace['query_sql']
        def capture_query(tool, values, query):
            require(query == installer.PERMISSION_QUERY, 'non_read_only_sql_blocked')
            result = query_sql(tool, values, query)
            capture_checks(result, installer.EXPECTED_DATABASE, report)
            return result
        namespace['query_sql'] = capture_query


def diagnose(package, installer_path):
    report = dict(revision=REVISION, mode='read-only', status='failed', phase='verify-installer',
                  changes_made=False, database_migrations_run=False, database_writes_run=False,
                  service_restarted=False, secret_values_printed=False, sql_metadata_read_completed=False,
                  preflight_completed=False)
    installer = None
    try:
        installer = trusted_installer(installer_path)
        report['installer_sha256_verified'] = True
        process = ReadOnlyProcess(installer.PERMISSION_QUERY, report)
        def guarded_exec(code, namespace):
            name = namespace.get('__name__')
            require(name in ('reviewed_i09_activation_runtime', 'reviewed_i09_inventory'),
                    'unexpected_dependency_execution_blocked')
            builtins.exec(code, namespace)
            guard_module(name, namespace, installer, process, report)
        installer.exec = guarded_exec
        report['phase'] = 'verify-payload'
        instance = installer.make_installer(package)
        report['payload_sha256_verified'] = True
        # Explicit method selection: never main/apply/stage/switch/rollback/health.
        report['phase'] = 'application-read-only-preflight'
        instance.preflight()
        report.update(preflight_completed=True, status='inspected', phase='completed')
    except (Exception, KeyboardInterrupt) as error:
        if isinstance(error, DiagnosisError):
            report['issue'] = str(error)
        elif installer is not None and isinstance(error, installer.InstallError) and str(error) in SAFE_ISSUES:
            report['issue'] = str(error)
        else:
            report['issue'] = 'diagnosis_interrupted' if isinstance(error, KeyboardInterrupt) else 'diagnostic_local_failure'
        if report['sql_metadata_read_completed'] and report.get('issue') == 'database_permissions_schema_or_channels_unexpected':
            report.update(status='inspected', phase='completed', original_preflight_guard_blocked=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-customer-access-payload.zip'))
    parser.add_argument('--installer', type=Path, default=Path('/root/PRODIMT_Instalar_I09.py'))
    args = parser.parse_args()
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    if os.geteuid() != 0:
        report = dict(revision=REVISION, mode='read-only', changes_made=False, status='failed', issue='root_required')
    else:
        report = diagnose(args.package, args.installer)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report['status'] == 'inspected' else 1


if __name__ == '__main__':
    raise SystemExit(main())
