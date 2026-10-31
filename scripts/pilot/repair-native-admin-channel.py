#!/usr/bin/env python3
"""Add only the missing administrative sales channel to the verified native I07.

Uses the running application's limited SQL identity. No SQL administrator,
bootstrap, migrations, account changes, service restart or order replay.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import resource
import stat
import subprocess
import types
import zipfile

REVISION = '20261030-admin-channel-repair1'
PACKAGE_SHA256 = '06c1a7fd68ec997b7ba7efa92ddf65de6b35fae58dc800ed7ec2596a0741755b'
ROOT = Path('/var/www/prodimt-pedidos')
SERVICE = 'prodimt-pedidos-api.service'
RELEASE = 'capture-46fed604732e'
SERVER = '127.0.0.1,14330'
DATABASE = 'ProdimtPedidos'
LOGIN = 'prodimt_pedidos_app'
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
OPTIONS = ('SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 8000; '
           'SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; '
           'SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; '
           'SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;\n')
PREFLIGHT_SQL = """SELECT (SELECT DB_NAME() AS database_name,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261028202031_AddCustomerCaptureAliases') AS i07,
 HAS_PERMS_BY_NAME(N'dbo.SalesChannels',N'OBJECT',N'SELECT') AS read_channels,
 HAS_PERMS_BY_NAME(N'dbo.SalesChannels',N'OBJECT',N'INSERT') AS insert_channels,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'SELECT') AS read_audit,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'INSERT') AS insert_audit,
 (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SalesChannels')
  AND name=N'IX_SalesChannels_Type' AND is_unique=1 AND is_disabled=0 AND has_filter=0) AS unique_type_index,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture') AS admin_channels,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture' AND IsInternal=1
  AND Type COLLATE Latin1_General_100_BIN2=N'AdminManualCapture' AND LEN(LTRIM(RTRIM(Name)))>0) AS valid_admin_channels
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS ProdimtInspectionJson;"""

# This exact SQL is also exercised in disposable SQL Server/HTTP tests. The
# production target is fixed; tests replace only its database-name assertion.
REPAIR_SQL = r"""
IF DB_NAME()<>N'ProdimtPedidos' THROW 51001,'repair_target_mismatch',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WITH (HOLDLOCK))<>11
  OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261028202031_AddCustomerCaptureAliases')
  THROW 51002,'repair_requires_i07',1;
 DECLARE @channel uniqueidentifier, @created bit=0, @audit uniqueidentifier=NEWID();
 DECLARE @orders bigint=(SELECT COUNT_BIG(*) FROM dbo.Orders WITH (HOLDLOCK)),
  @operations bigint=(SELECT COUNT_BIG(*) FROM dbo.OrderOperations WITH (HOLDLOCK)),
  @customers bigint=(SELECT COUNT_BIG(*) FROM dbo.Customers WITH (HOLDLOCK)),
  @products bigint=(SELECT COUNT_BIG(*) FROM dbo.Products WITH (HOLDLOCK)),
  @profiles bigint=(SELECT COUNT_BIG(*) FROM dbo.CustomerFrequentProducts WITH (HOLDLOCK)),
  @channels bigint=(SELECT COUNT_BIG(*) FROM dbo.SalesChannels WITH (HOLDLOCK)),
  @audits bigint=(SELECT COUNT_BIG(*) FROM dbo.AuditLogs WITH (HOLDLOCK));
 IF (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture')>1
  THROW 51003,'admin_channel_not_unique',1;
 SELECT @channel=Id FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture';
 IF @channel IS NOT NULL AND NOT EXISTS
  (SELECT 1 FROM dbo.SalesChannels WHERE Id=@channel AND IsInternal=1
   AND Type COLLATE Latin1_General_100_BIN2=N'AdminManualCapture' AND LEN(LTRIM(RTRIM(Name)))>0)
  THROW 51004,'existing_admin_channel_incompatible',1;
 IF @channel IS NULL
 BEGIN
  SET @channel=NEWID();
  INSERT INTO dbo.SalesChannels (Id,Name,Type,IsInternal)
   VALUES (@channel,N'Captura administrativa',N'AdminManualCapture',1);
  INSERT INTO dbo.AuditLogs (Id,EntityType,EntityId,EventType,OccurredAt,ActorType,ActorId,ActorDisplayName,Summary,MetadataJson)
   VALUES (@audit,N'SalesChannel',CONVERT(nvarchar(36),@channel),N'AdminSalesChannelRepaired',
    SYSDATETIMEOFFSET(),N'System',N'native-maintenance',N'Mantenimiento PRODIMT',
    N'Canal de captura administrativa faltante agregado.',
    N'{"method":"native-admin-channel-repair","revision":"20261030-admin-channel-repair1"}');
  SET @created=1;
 END;
 IF (SELECT COUNT_BIG(*) FROM dbo.Orders)<>@orders
  OR (SELECT COUNT_BIG(*) FROM dbo.OrderOperations)<>@operations
  OR (SELECT COUNT_BIG(*) FROM dbo.Customers)<>@customers
  OR (SELECT COUNT_BIG(*) FROM dbo.Products)<>@products
  OR (SELECT COUNT_BIG(*) FROM dbo.CustomerFrequentProducts)<>@profiles
  OR (SELECT COUNT_BIG(*) FROM dbo.SalesChannels)<>@channels+CONVERT(int,@created)
  OR (SELECT COUNT_BIG(*) FROM dbo.AuditLogs)<>@audits+CONVERT(int,@created)
  THROW 51005,'repair_counts_changed',1;
 IF NOT EXISTS (SELECT 1 FROM dbo.SalesChannels WHERE Id=@channel AND Type=N'AdminManualCapture' AND IsInternal=1)
  OR (@created=1 AND NOT EXISTS (SELECT 1 FROM dbo.AuditLogs WHERE Id=@audit AND EntityId=CONVERT(nvarchar(36),@channel)
   AND EventType=N'AdminSalesChannelRepaired'))
  THROW 51006,'repair_verification_failed',1;
 COMMIT TRANSACTION;
 SELECT (SELECT CONVERT(int,@created) AS created,1 AS channel_verified,1 AS existing_data_counts_preserved,
  CONVERT(int,@created) AS audit_inserted,1 AS transaction_committed
  FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS ProdimtInspectionJson;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
"""


class RepairError(Exception):
    """Only a fixed, non-sensitive issue code may be passed."""


def require(condition, issue):
    if not condition:
        raise RepairError(issue)


def load_inspector(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'package_owner_or_type_unexpected')
        raw = stream.read(40 * 1024 * 1024 + 1)
    require(hashlib.sha256(raw).hexdigest() == PACKAGE_SHA256, 'i07_package_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        require(archive.namelist().count('inspect-native.py') == 1, 'inspector_not_unique')
        info = archive.getinfo('inspect-native.py')
        require(info.file_size <= 1024 * 1024, 'inspector_too_large')
        source = archive.read(info)
    module = types.ModuleType('verified_channel_repair_inspector')
    exec(compile(source, '<verified-i07-inspector>', 'exec'), module.__dict__)
    return module


def service_configuration(inspector):
    code, output = inspector.command(['systemctl', 'show', SERVICE,
                                      '--property=ActiveState,User,MainPID,WorkingDirectory'])
    require(code == 0, 'service_metadata_unavailable')
    data = dict(row.split('=', 1) for row in output.splitlines() if '=' in row)
    require(data.get('ActiveState') == 'active' and data.get('User') == 'www-data'
            and data.get('MainPID', '').isdigit() and int(data['MainPID']) > 0
            and data.get('WorkingDirectory') == str(ROOT / 'backend/current'), 'service_identity_unexpected')
    pid = int(data['MainPID'])
    identity = inspector.process_identity(pid)
    for part in ('backend', 'frontend'):
        current = ROOT / part / 'current'
        require(current.is_symlink() and current.resolve(strict=True) == ROOT / part / 'releases' / RELEASE,
                'running_release_not_i07')
    cwd = Path(f'/proc/{pid}/cwd').resolve(strict=True)
    require(cwd == ROOT / 'backend/releases' / RELEASE, 'process_release_not_i07')
    argv = inspector.read_limited(f'/proc/{pid}/cmdline').rstrip('\0').split('\0')
    dll = [Path(arg) for arg in argv if Path(arg).name == 'Prodimt.Pedidos.Api.dll']
    require(len(dll) == 1 and (dll[0] if dll[0].is_absolute() else cwd / dll[0]).resolve(strict=True)
            == cwd / 'Prodimt.Pedidos.Api.dll', 'process_arguments_unexpected')
    cfg, _ = inspector.configuration(inspector.process_environment(pid), argv, cwd)
    require(inspector.policy_flags(cfg).get('provider_is_sqlserver'), 'provider_not_sqlserver')
    values = inspector.connection_fields(inspector.first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING'))
    require(inspector.permitted_connection(values), 'sql_target_not_expected')
    inspector.assert_same_process(pid, identity)
    return values, pid, identity


def sql_json(inspector, tool, values, script, report):
    require(tool in inspector.SQL_TOOLS and inspector.permitted_connection(values), 'sql_target_not_expected')
    require(script in (PREFLIGHT_SQL, REPAIR_SQL), 'sql_script_not_authorized')
    env = dict(CLEAN_ENV, SQLCMDUSER=LOGIN, SQLCMDPASSWORD=values['password'])
    try:
        result = subprocess.run([tool, '-S', SERVER, '-d', DATABASE, '-C', '-I', '-b', '-x',
                                 '-l', '5', '-t', '20', '-y', '0', '-w', '65535'],
                                input=OPTIONS + script, env=env, capture_output=True,
                                text=True, timeout=30, check=False)
    finally:
        env.clear()
    if result.returncode:
        report['sql_error_numbers'] = sorted({int(n) for n in re.findall(
            r'(?m)^\s*Msg ([1-9][0-9]{0,5}), Level [0-9]{1,2}, State [0-9]{1,3},',
            result.stdout + '\n' + result.stderr)})[:10]
        raise RepairError('sql_operation_failed')
    return inspector.parse_sql_output(result.stdout)


def validate_preflight(data):
    require(isinstance(data, dict) and data.get('database_name') == DATABASE
            and data.get('migrations_count') == 11 and data.get('i07') == 1, 'database_not_i07')
    require(all(data.get(key) == 1 for key in ('read_channels', 'insert_channels', 'read_audit', 'insert_audit')),
            'application_permissions_insufficient')
    require(data.get('unique_type_index') == 1, 'sales_channel_unique_index_missing')
    require((data.get('admin_channels'), data.get('valid_admin_channels')) in ((0, 0), (1, 1)),
            'existing_admin_channel_incompatible')


def repair(package, apply=False):
    report = dict(revision=REVISION, mode='apply' if apply else 'read-only', status='failed', phase='preflight',
                  changes_made=False, changes_may_have_occurred=False, channel_created=False,
                  channel_verified=False, existing_data_counts_preserved=False,
                  database_migrations_run=False, service_restarted=False, order_replay_attempted=False,
                  application_save_verified=False, secret_values_printed=False)
    values = None
    try:
        require(os.geteuid() == 0, 'root_required')
        inspector = load_inspector(package)
        values, pid, identity = service_configuration(inspector)
        tool = next((p for p in inspector.SQL_TOOLS if Path(p).is_file() and os.access(p, os.X_OK)), None)
        require(tool is not None, 'sqlcmd_not_found')
        report['phase'] = 'database-read-only-checks'
        data = sql_json(inspector, tool, values, PREFLIGHT_SQL, report)
        validate_preflight(data)
        report['channel_missing'] = data['admin_channels'] == 0
        if not apply:
            report.update(status='planned', phase='completed', channel_verified=data['valid_admin_channels'] == 1)
            return report
        inspector.assert_same_process(pid, identity)
        report.update(phase='repair-transaction', changes_may_have_occurred=True)
        result = sql_json(inspector, tool, values, REPAIR_SQL, report)
        require(isinstance(result, dict) and result.get('created') in (0, 1)
                and all(result.get(k) == 1 for k in ('channel_verified', 'existing_data_counts_preserved', 'transaction_committed'))
                and result.get('audit_inserted') == result['created'], 'repair_result_unrecognized')
        created = result['created'] == 1
        report.update(changes_made=created, changes_may_have_occurred=created, channel_created=created,
                      existing_data_counts_preserved=True, audit_inserted=created)
        report['phase'] = 'post-repair-verification'
        inspector.assert_same_process(pid, identity)
        after = sql_json(inspector, tool, values, PREFLIGHT_SQL, report)
        validate_preflight(after)
        require(after['admin_channels'] == 1 and after['valid_admin_channels'] == 1, 'channel_not_verified')
        report.update(status='repaired' if created else 'already_configured', phase='completed', channel_verified=True)
    except RepairError as exc:
        report['issue'] = str(exc)
    except Exception:
        report['issue'] = 'repair_failed_without_sensitive_details'
    finally:
        if values is not None:
            values.clear()
    return report


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-quick-capture-payload.zip'))
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    report = repair(args.package, args.apply)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report['status'] == 'failed' else 0


if __name__ == '__main__':
    raise SystemExit(main())
