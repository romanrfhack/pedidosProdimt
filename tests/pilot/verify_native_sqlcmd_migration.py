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

    def container_command(args, *, env=None, timeout=30, input_text=None):
        require(args[0] == SQLCMD and '-b' in args and '-x' in args, 'sqlcmd_protocol_unexpected')
        require(args[args.index('-S') + 1] == 'localhost', 'sqlcmd_server_not_disposable')
        require(args[args.index('-d') + 1] in ('master', target), 'sqlcmd_database_not_disposable')
        require(env is not None and set(env) == set(update.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'},
                'sqlcmd_environment_not_clean')
        require(env['SQLCMDUSER'] == 'sa' and env['SQLCMDPASSWORD'] == password,
                'sqlcmd_test_identity_unexpected')
        require(password not in '\0'.join(args), 'sqlcmd_password_in_arguments')
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
    finally:
        if created:
            update.DATABASE = 'master'
            updater.sql_run(credentials, query='ALTER DATABASE [' + target +
                            '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + target + '];', timeout=90)
        credentials.password = ''
        password = ''
    print('Native ODBC sqlcmd migration contract passed: default QI OFF/error 1934; fixed 5-to-9 upgrade, '
          'required session options, repeat and preserved synthetic data.')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Diagnostic streams can contain SQL/data. Only locally assigned issue
        # codes and exception class names enter CI output.
        issue = str(error) if isinstance(error, FixtureFailure) else type(error).__name__
        print('Native ODBC sqlcmd migration contract failed: ' + issue, file=sys.stderr)
        sys.exit(1)
