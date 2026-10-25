"""Exercise the updater's real ODBC sqlcmd transport against disposable CI SQL.

The transport changes only the container boundary. Updater.sql_run/migrate keep
their real flags, clean credential environment, GO input and session prologue.
No production target, file, secret or data is used by this fixture.
"""
import argparse
import importlib.util
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid


CONTAINER = 'prodimt-i02-sqlserver'
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
SESSION_GUARD = """
GO
IF ISNULL(CAST(SESSIONPROPERTY('QUOTED_IDENTIFIER') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('ANSI_NULLS') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('ANSI_PADDING') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('ANSI_WARNINGS') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('ARITHABORT') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL') AS int),-1) <> 1
 OR ISNULL(CAST(SESSIONPROPERTY('NUMERIC_ROUNDABORT') AS int),-1) <> 0
    THROW 51000, N'Native CLI session options invalid', 1;
GO
"""


class FixtureFailure(Exception):
    """Only fixed test issue codes are permitted, never raw CLI diagnostics."""


def require(condition, issue):
    if not condition:
        raise FixtureFailure(issue)


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--script', type=Path, required=True)
    arguments = parser.parse_args()
    baseline = arguments.baseline.read_bytes()
    full_script = arguments.script.read_bytes()
    require(0 < len(baseline) < len(full_script) < 1024 * 1024, 'generated_scripts_size_unexpected')

    # The workflow creates this exact synthetic connection, with a masked random
    # password. It remains in memory/env, never a docker/sqlcmd command argument.
    connection = os.environ.get('PRODIMT_SQL_TEST_CONNECTION', '')
    match = re.fullmatch(
        r'Server=127\.0\.0\.1,14333;Database=master;User Id=sa;Password=([0-9a-f]{48}Aa1!);Encrypt=True;TrustServerCertificate=True',
        connection)
    require(match is not None, 'synthetic_sql_connection_unexpected')
    password = match[1]
    root = Path(__file__).parents[2]
    update = load_module('native_cli_update_fixture', root / 'scripts/pilot/update-native.py')
    inventory = load_module('native_cli_inventory_fixture', root / 'scripts/pilot/inspect-native.py')
    target = 'ProdimtNativeCli_' + uuid.uuid4().hex
    require(re.fullmatch(r'ProdimtNativeCli_[0-9a-f]{32}', target), 'disposable_database_name_invalid')
    update.SERVER = 'localhost'  # The sqlcmd process runs inside the test container.
    update.DATABASE = 'master'
    updater = update.Updater('unused-synthetic-package.zip')
    updater.inspector = inventory
    updater.sql_tool = SQLCMD
    credentials = update.Credentials('sa', password)
    last_result = {}
    restricted_metadata = False

    def container_command(args, *, env=None, timeout=30, input_text=None):
        require(args[0] == SQLCMD and '-b' in args and '-x' in args, 'sqlcmd_protocol_unexpected')
        require(args[args.index('-S') + 1] == 'localhost', 'sqlcmd_server_not_disposable')
        require(args[args.index('-d') + 1] in ('master', target), 'sqlcmd_database_not_disposable')
        require(env is not None and set(env) == set(update.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'},
                'sqlcmd_environment_not_clean')
        require(env['SQLCMDUSER'] == 'sa' and env['SQLCMDPASSWORD'] == password,
                'sqlcmd_test_identity_unexpected')
        require(password not in '\0'.join(args), 'sqlcmd_password_in_arguments')
        if restricted_metadata:
            # Use real database impersonation to exercise metadata permissions,
            # rather than faking a response for the baseline guard.
            require('-Q' in args and input_text is None, 'restricted_probe_must_be_query_only')
            args = list(args)
            query_position = args.index('-Q') + 1
            args[query_position] = ("EXECUTE AS USER=N'native_resume_reader';\n" +
                                    args[query_position] + '\nREVERT;')
        transport = ['/usr/bin/docker', 'exec', '--interactive',
                     '--env', 'SQLCMDUSER', '--env', 'SQLCMDPASSWORD', CONTAINER, *args]
        result = subprocess.run(transport, input=input_text, capture_output=True, text=True,
                                check=False, env=env, timeout=timeout)
        last_result.update(code=result.returncode, stdout=result.stdout, stderr=result.stderr,
                           args=list(args))
        return result.returncode, result.stdout

    # This adapter does not simulate SQL output or behavior: every call executes
    # the actual CLI over the generated SQL against one throwaway database.
    update.command = container_command
    created = False
    try:
        updater.sql_run(credentials, query='CREATE DATABASE [' + target + '];', timeout=90)
        created = True
        update.DATABASE = target
        updater.sql_run(credentials, query='CREATE TABLE dbo.NativeCliProbe (Id int NULL);')

        # Show why SELECT-only CLI checks missed the migration defect. ODBC
        # sqlcmd without -I has QI OFF and rejects filtered-index DDL (1934).
        no_i_args = [SQLCMD, '-S', 'localhost', '-d', target, '-C', '-b', '-l', '5', '-t', '20',
                     '-x', '-y', '0', '-w', '65535', '-Q',
                     'SET NOCOUNT ON; SELECT (SELECT CAST(SESSIONPROPERTY(\'QUOTED_IDENTIFIER\') AS int) AS q '
                     'FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS ProdimtInspectionJson;']
        env = dict(update.CLEAN_ENV, SQLCMDUSER='sa', SQLCMDPASSWORD=password)
        code, output = container_command(no_i_args, env=env)
        require(code == 0 and inventory.parse_sql_output(output) == {'q': 0}, 'odbc_default_quoted_identifier_not_off')
        negative_args = [SQLCMD, '-S', 'localhost', '-d', target, '-C', '-b', '-l', '5', '-t', '20', '-x']
        code, _ = container_command(negative_args, env=env, input_text=(
            'SET NOCOUNT ON;\nSET XACT_ABORT ON;\nBEGIN TRANSACTION;\nGO\n'
            'CREATE UNIQUE INDEX IX_NativeCliProbe_Id ON dbo.NativeCliProbe (Id) WHERE Id IS NOT NULL;\nGO\n'
            'COMMIT TRANSACTION;\nGO\n'))
        diagnostics = last_result['stdout'] + '\n' + last_result['stderr']
        require(code != 0 and re.search(r'\b1934\b', diagnostics) is not None,
                'odbc_filtered_index_did_not_reproduce_1934')
        check = updater.sql_json(credentials, """SELECT
          (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NativeCliProbe')
            AND name=N'IX_NativeCliProbe_Id') AS index_count
          FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;""")
        require(check == {'index_count': 0}, 'negative_probe_left_an_index')

        updater.payload = {'files': {'migrations-idempotent.sql': baseline}}
        updater.migrate(credentials)
        rows = updater.sql_json(credentials, inventory.SQL_MIGRATIONS)
        require([row.get('MigrationId') for row in rows] == update.MIGRATIONS[:5], 'five_migration_baseline_failed')

        # Reusing staged releases is allowed only after an administrator proves
        # that no unrecorded migration DDL remains. Exercise that exact guard
        # against real catalog metadata and the original five-migration schema.
        def baseline_rejected(expected_issue, kind):
            try:
                updater.verify_resume_schema_baseline(credentials)
            except update.UpdateError as error:
                require(str(error) == expected_issue, 'resume_baseline_wrong_rejection_' + kind)
            else:
                raise FixtureFailure('resume_baseline_accepted_' + kind)

        updater.verify_resume_schema_baseline(credentials)
        # Reproduce the user's exact transition through the original native
        # script protocol. sqlcmd abort/disconnect must roll back all DDL in
        # the first new migration, including its earlier DROP INDEX/ADD COLUMN.
        code, _ = container_command(negative_args, env=env, input_text=(
            'SET NOCOUNT ON;\nSET XACT_ABORT ON;\nSET LOCK_TIMEOUT 10000;\nGO\n' +
            full_script.decode('utf-8-sig')))
        diagnostics = last_result['stdout'] + '\n' + last_result['stderr']
        require(code != 0 and re.search(r'\b1934\b', diagnostics) is not None,
                'original_native_upgrade_did_not_reproduce_1934')
        updater.verify_resume_schema_baseline(credentials)

        for kind, inject, restore in (
            ('partial_column',
             'ALTER TABLE dbo.Products ADD UnitCode nvarchar(16) NULL;',
             'ALTER TABLE dbo.Products DROP COLUMN UnitCode;'),
            ('missing_old_index',
             'DROP INDEX IX_Orders_CustomerId_OrderDate_SequenceNumber ON dbo.Orders;',
             'CREATE INDEX IX_Orders_CustomerId_OrderDate_SequenceNumber ON dbo.Orders '
             '(CustomerId,OrderDate,SequenceNumber);'),
            ('altered_audit_column',
             'ALTER TABLE dbo.OrderAuditLogs ALTER COLUMN MetadataJson nvarchar(max) NULL;',
             'ALTER TABLE dbo.OrderAuditLogs ALTER COLUMN MetadataJson nvarchar(2000) NULL;'),
        ):
            updater.sql_run(credentials, query=inject)
            try:
                baseline_rejected('resume_database_schema_baseline_unexpected', kind)
            finally:
                updater.sql_run(credentials, query=restore)
            updater.verify_resume_schema_baseline(credentials)

        updater.sql_run(credentials, query="""CREATE USER [native_resume_reader] WITHOUT LOGIN;
          GRANT SELECT ON SCHEMA::dbo TO [native_resume_reader];""")
        restricted_metadata = True
        try:
            baseline_rejected('resume_database_schema_baseline_unexpected', 'restricted_metadata')
        finally:
            restricted_metadata = False
        updater.verify_resume_schema_baseline(credentials)

        updater.sql_run(credentials, query="""INSERT INTO dbo.Customers
          (Id,Name,PhoneNumber,IsActive,CreatedAt,UpdatedAt,ExternalCode)
          VALUES ('9ef84f64-8ff4-4a47-a114-ce22ea244589',N'Synthetic native CLI customer',N'5550000000',0,
            SYSUTCDATETIME(),SYSUTCDATETIME(),N'SYNTHETIC-NATIVE-CLI');
          INSERT INTO dbo.AdminUsers (Id,UserName,PasswordHash,DisplayName,IsActive,CreatedAt)
          VALUES ('9ef84f64-8ff4-4a47-a114-ce22ea244590',N'synthetic-native-cli-admin',
            N'synthetic-existing-hash',N'Synthetic CLI administrator',0,SYSUTCDATETIME());""")

        # Fail on the same connection that executes the GO-delimited script.
        # A THROW guard avoids depending on interactive stdout/header formatting.
        updater.payload = {'files': {'migrations-idempotent.sql': full_script + SESSION_GUARD.encode()}}
        for _ in range(2):
            updater.migrate(credentials)
            require('-I' in last_result['args'], 'fixed_native_command_missing_I')
            rows = updater.sql_json(credentials, inventory.SQL_MIGRATIONS)
            require([row.get('MigrationId') for row in rows] == update.MIGRATIONS, 'nine_migration_upgrade_failed')

        baseline_rejected('resume_database_history_unexpected', 'already_upgraded_history')

        result = updater.sql_json(credentials, """SELECT
          (SELECT COUNT(*) FROM dbo.Customers WHERE ExternalCode=N'SYNTHETIC-NATIVE-CLI'
            AND Name=N'Synthetic native CLI customer' AND IsActive=0) AS preserved_customers,
          (SELECT COUNT(*) FROM dbo.AdminUsers WHERE UserName=N'synthetic-native-cli-admin'
            AND PasswordHash=N'synthetic-existing-hash' AND IsActive=0) AS preserved_administrators,
          (SELECT COUNT(*) FROM dbo.Orders) AS orders_count,
          CAST((SELECT is_unique FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Orders')
            AND name=N'IX_Orders_CustomerId_OrderDate_SequenceNumber') AS int) AS order_index_unique,
          CAST(CASE WHEN OBJECT_ID(N'dbo.OrderOperations',N'U') IS NOT NULL
            AND OBJECT_ID(N'dbo.OrderChangeNotices',N'U') IS NOT NULL THEN 1 ELSE 0 END AS int) AS new_tables,
          (SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OrderAuditLogs')
            AND name=N'MetadataJson') AS audit_max_length
          FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;""")
        require(result == {'preserved_customers': 1, 'preserved_administrators': 1, 'orders_count': 0,
                           'order_index_unique': 1, 'new_tables': 1, 'audit_max_length': -1},
                'native_cli_schema_or_preservation_failed')

        # The separate root recovery tool must update only the selected active
        # web account, atomically with audit, while preserving the old inactive
        # administrator and the rest of this synthetic database.
        recovery = load_module('native_admin_reset_fixture', root / 'scripts/pilot/reset-native-admin.py')
        recovery.SERVER = 'localhost'
        recovery.DATABASE = target
        recovery_user = 'synthetic-recovery-admin'
        resetter = recovery.Recovery(recovery_user)
        resetter.sql_tool = SQLCMD
        resetter.sql_login = credentials.login
        resetter.sql_password = credentials.password

        def reset_container_command(args, *, data=None, env=None, timeout=25):
            code, output = container_command(args, env=env, timeout=timeout, input_text=data)
            return subprocess.CompletedProcess(args, code, output, last_result['stderr'])

        # Keep the recovery tool's own stdin, output-width, -I/-x and clean-env
        # protocol; only translate its process boundary into the CI container.
        recovery.command = reset_container_command
        updater.sql_run(credentials, query="""INSERT INTO dbo.AdminUsers
          (Id,UserName,PasswordHash,DisplayName,IsActive,CreatedAt) VALUES
          ('91c8c3c3-319a-4bed-8dd9-d4118deef85f',N'synthetic-recovery-admin',
          N'fictional-prior-recovery-hash',N'Fictional recovery display',1,'2026-10-24T12:00:00+00:00');""")

        def account_snapshot():
            return recovery.validate_snapshot(resetter.sql(recovery.snapshot_sql(recovery_user)), recovery_user)

        before = account_snapshot()
        new_hash = recovery.identity_hash('Fictional recovery password ñ 2026!')
        operation = str(uuid.uuid4())
        receipt = resetter.sql(recovery.reset_sql(before, new_hash, operation))
        require(receipt == {'password_reset': 1, 'audit_recorded': 1}, 'reset_receipt_failed')
        after = account_snapshot()
        require(after == dict(before, password_hash=new_hash), 'reset_changed_unrelated_account_fields')

        def reset_rejected(script, code, kind):
            try:
                resetter.sql(script)
            except recovery.RecoveryError:
                require(code in resetter.state.get('sql_error_numbers', []),
                        'reset_wrong_sql_failure_' + kind)
            else:
                raise FixtureFailure('reset_accepted_' + kind)

        reset_rejected(recovery.reset_sql(before, new_hash, str(uuid.uuid4())), 51001, 'stale_password_hash')
        updater.sql_run(credentials, script="""CREATE TRIGGER dbo.NativeRecoveryRejectAudit ON dbo.AuditLogs
          AFTER INSERT AS BEGIN THROW 51003,N'Fictional audit failure',1; END;""")
        try:
            reset_rejected(recovery.reset_sql(after, recovery.identity_hash('Fictional second password 2026!'),
                                             str(uuid.uuid4())), 51003, 'audit_failure')
        finally:
            updater.sql_run(credentials, query='DROP TRIGGER dbo.NativeRecoveryRejectAudit;')
        require(account_snapshot() == after, 'reset_audit_failure_did_not_roll_back_password')
        audit = updater.sql_json(credentials, """SELECT
          (SELECT COUNT(*) FROM dbo.AuditLogs WHERE EventType=N'AdminUserPasswordReset') AS resets,
          (SELECT COUNT(*) FROM dbo.AuditLogs WHERE EventType=N'AdminUserPasswordReset'
            AND ActorType=2 AND MetadataJson NOT LIKE N'%hash%' AND MetadataJson NOT LIKE N'%password%') AS safe_audits,
          (SELECT COUNT(*) FROM dbo.AdminUsers WHERE UserName=N'synthetic-native-cli-admin'
            AND PasswordHash=N'synthetic-existing-hash' AND IsActive=0) AS preserved_inactive_admins,
          (SELECT COUNT(*) FROM dbo.AdminUsers) AS admins,
          (SELECT COUNT(*) FROM dbo.Customers) AS customers,
          (SELECT COUNT(*) FROM dbo.Orders) AS orders_count
          FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;""")
        require(audit == {'resets': 1, 'safe_audits': 1, 'preserved_inactive_admins': 1,
                         'admins': 2, 'customers': 1, 'orders_count': 0}, 'reset_scope_or_audit_invalid')
    finally:
        if created:
            update.DATABASE = 'master'
            updater.sql_run(credentials, query='ALTER DATABASE [' + target +
                            '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + target + '];', timeout=90)
        credentials.password = ''
        password = ''
    print('Native ODBC sqlcmd migration contract passed: default QI OFF/error 1934; fixed 5-to-9 upgrade, '
          'required session options, repeat and preserved synthetic data; resume baseline rejects '
          'partial DDL, restricted metadata and already-upgraded history; original failed script '
          'rolls back to the complete five-migration baseline; web admin reset preserves scope, '
          'rejects stale credentials and rolls back the password when audit fails.')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Diagnostic streams can contain SQL/data. Only locally assigned issue
        # codes and exception class names enter CI output.
        issue = str(error) if isinstance(error, FixtureFailure) else type(error).__name__
        print('Native ODBC sqlcmd migration contract failed: ' + issue, file=sys.stderr)
        sys.exit(1)
