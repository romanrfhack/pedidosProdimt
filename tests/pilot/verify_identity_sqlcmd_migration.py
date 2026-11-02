"""Exercise the exact packaged I18 SQL through the native ODBC sqlcmd transport.

Only an explicitly synthetic CI connection and random disposable database are
accepted. No application installation, production credential, key or backup is
accessed. The builder's DDL/seed normalization is the same used for the ZIP.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import types
import uuid

CONTAINER = 'prodimt-i02-sqlserver'
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
I12 = '20261101190000_AddRecoverableCustomerAccessTokens'
I18 = '20261102235000_AddSecurityAdministration'
PHASE = 'initializing'
SQL_DIAGNOSTIC_STATE = {}


def phase(label):
    global PHASE
    PHASE = label


class FixtureFailure(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise FixtureFailure(issue)


def load_module(name, path):
    specification = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(result)
    return result



def verify_restricted_token_metadata(execute, query, inventory, token_support, installer):
    """Reproduce the app principal's hidden default definitions in disposable SQL.

    The app is granted only table DML. Exact defaults remain an administrator
    prerequisite; hidden definitions are never reported as verified defaults.
    Every malformed-schema variation is enclosed in a rolled-back transaction.
    """
    phase('metadata-create-restricted-principal')
    principal = 'i18_fixture_limited_metadata'
    execute("CREATE USER [" + principal + "] WITHOUT LOGIN;\n"
            "GRANT SELECT,INSERT,UPDATE,DELETE ON dbo.CustomerAccessTokens TO [" + principal + "];\n")

    def as_user(sql):
        wrapped = ("EXECUTE AS USER=N'" + principal + "';\nSELECT (" + sql.rstrip().removesuffix(';') +
                   ') AS ProdimtInspectionJson;\nREVERT;')
        return inventory.parse_sql_output(execute(wrapped))

    phase('metadata-inspect-hidden-defaults')
    visibility = as_user("""SELECT
      HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION') AS database_view_definition,
      HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'VIEW DEFINITION') AS object_view_definition,
      (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
       AND name IN(N'MetadataVersion',N'IsReminderLink') AND default_object_id<>0) AS bound_defaults,
      (SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
       AND parent_column_id IN(COLUMNPROPERTY(OBJECT_ID(N'dbo.CustomerAccessTokens'),N'MetadataVersion','ColumnId'),
                              COLUMNPROPERTY(OBJECT_ID(N'dbo.CustomerAccessTokens'),N'IsReminderLink','ColumnId'))
       AND definition IS NULL) AS hidden_default_definitions
      FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;""")
    require(visibility == {'database_view_definition': 0, 'object_view_definition': 0,
                           'bound_defaults': 2, 'hidden_default_definitions': 2},
            'restricted_principal_metadata_visibility_not_reproduced')
    phase('metadata-reproduce-original-mismatch')
    original = as_user(token_support.TOKEN_SCHEMA)
    require(original == dict(columns_count=3, protected_column=1, version_column=0, reminder_column=0),
            'original_token_schema_hidden_default_mismatch_not_reproduced')
    require(query(token_support.TOKEN_SCHEMA) == token_support.EXPECTED_TOKEN_SCHEMA,
            'administrator_baseline_defaults_not_exact')
    phase('metadata-check-new-limited-preflight')
    application = as_user(installer.APP_TOKEN_SCHEMA)
    require(not installer.app_token_schema_mismatches(application)
            and application.get('version_default_visible') == 0
            and application.get('reminder_default_visible') == 0
            and application.get('version_default_valid') == 0
            and application.get('reminder_default_valid') == 0
            and application.get('can_view_definition') == 0,
            'valid_hidden_default_structure_not_accepted_for_read_only_plan')
    phase('metadata-check-new-admin-preflight')
    admin_application = query(installer.APP_TOKEN_SCHEMA)
    require(not installer.app_token_schema_mismatches(admin_application)
            and admin_application.get('version_default_visible') == 1
            and admin_application.get('reminder_default_visible') == 1
            and admin_application.get('can_view_definition') == 1,
            'administrator_visible_default_structure_unexpected')

    def drop_version_default():
        # The identifier comes only from this freshly-created disposable schema;
        # QUOTENAME keeps generated SQL scoped to that one constraint.
        return """DECLARE @fixture_default sysname=(SELECT dc.name FROM sys.default_constraints dc
          JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
          WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CustomerAccessTokens') AND c.name=N'MetadataVersion');
          IF @fixture_default IS NULL THROW 51181,'fixture_default_missing',1;
          DECLARE @fixture_sql nvarchar(max)=N'ALTER TABLE dbo.CustomerAccessTokens DROP CONSTRAINT '+QUOTENAME(@fixture_default);
          EXEC(@fixture_sql);
        """

    combined = ('SELECT JSON_QUERY((' + installer.APP_TOKEN_SCHEMA.rstrip().removesuffix(';') + ')) AS application,'
                'JSON_QUERY((' + token_support.TOKEN_SCHEMA.rstrip().removesuffix(';') + ')) AS administrator '
                'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;')

    def changed_schema(ddl, *, restricted):
        # All schema mutations are test-only and rolled back in the SAME session.
        impersonation = "EXECUTE AS USER=N'" + principal + "';\n" if restricted else ''
        revert = 'REVERT;\n' if restricted else ''
        wrapped = ('BEGIN TRANSACTION;\n' + ddl + '\n' + impersonation + 'SELECT (' +
                   combined.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;\n' +
                   revert + 'ROLLBACK TRANSACTION;')
        result = inventory.parse_sql_output(execute(wrapped))
        require(query(token_support.TOKEN_SCHEMA) == token_support.EXPECTED_TOKEN_SCHEMA,
                'test_schema_variation_not_rolled_back')
        return result

    phase('metadata-reject-partial-schema')
    partial = changed_schema('ALTER TABLE dbo.CustomerAccessTokens DROP COLUMN ProtectedToken;', restricted=True)
    require(installer.app_token_schema_mismatches(partial['application'])
            and partial['application']['columns_count'] == 2, 'restricted_partial_schema_not_rejected')
    phase('metadata-reject-missing-default')
    missing = changed_schema(drop_version_default(), restricted=True)
    require(installer.app_token_schema_mismatches(missing['application'])
            and missing['application']['version_column'] == 0,
            'restricted_missing_default_binding_not_rejected')
    phase('metadata-reject-wrong-column-type')
    wrong_type = changed_schema('ALTER TABLE dbo.CustomerAccessTokens ALTER COLUMN ProtectedToken nvarchar(512) NULL;',
                                restricted=True)
    require(installer.app_token_schema_mismatches(wrong_type['application'])
            and wrong_type['application']['protected_column'] == 0,
            'restricted_wrong_column_type_not_rejected')
    wrong_value_ddl = (drop_version_default() +
                       'ALTER TABLE dbo.CustomerAccessTokens ADD CONSTRAINT DF_I18_FixtureWrongVersion DEFAULT 2 FOR MetadataVersion;')
    phase('metadata-defer-hidden-wrong-default')
    hidden_wrong = changed_schema(wrong_value_ddl, restricted=True)
    require(not installer.app_token_schema_mismatches(hidden_wrong['application'])
            and hidden_wrong['application']['version_default_visible'] == 0,
            'hidden_wrong_default_must_remain_pending_administrator_verification')
    phase('metadata-reject-visible-wrong-default')
    visible_wrong = changed_schema(wrong_value_ddl, restricted=False)
    require(installer.app_token_schema_mismatches(visible_wrong['application'])
            and visible_wrong['application']['version_default_visible'] == 1
            and visible_wrong['application']['version_default_valid'] == 0,
            'visible_wrong_default_not_rejected')
    require(visible_wrong['administrator'] != token_support.EXPECTED_TOKEN_SCHEMA
            and visible_wrong['administrator']['version_column'] == 0,
            'administrator_exact_wrong_default_not_rejected')
    phase('metadata-confirm-no-broadened-permissions')
    # No VIEW DEFINITION or broader metadata grant was added to make planning pass.
    require(as_user("SELECT HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION') AS database_view_definition,"
                    "HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'VIEW DEFINITION') AS object_view_definition "
                    "FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;") ==
            {'database_view_definition': 0, 'object_view_definition': 0}, 'metadata_permissions_were_broadened')
    phase('metadata-remove-restricted-principal')
    execute('DROP USER [' + principal + '];')
    return dict(original_hidden_default_mismatch_reproduced=True,
                read_only_structure_without_metadata_grant_verified=True,
                malformed_schema_rejected=True, administrator_exact_defaults_enforced=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--script', type=Path, required=True)
    arguments = parser.parse_args()
    phase('validate-generated-fixtures')
    root = Path(__file__).resolve().parents[2]
    baseline = arguments.baseline.read_text(encoding='utf-8-sig')
    original = arguments.script.read_text(encoding='utf-8-sig')
    require(0 < len(baseline) < 2 * 1024 * 1024 and I12 in baseline and I18 not in baseline
            and not re.search(r'(?im)^\s*(?:USE|CREATE\s+DATABASE|DROP\s+DATABASE)\b', baseline),
            'generated_i12_baseline_unexpected')
    require(0 < len(original) < 256 * 1024 and I18 in original and 'IF NOT EXISTS' in original,
            'generated_i18_idempotent_upgrade_unexpected')
    # The disposable baseline still includes I12's known historical207 boundary.
    # Reuse its exact reviewed repair for setup; this does not modify the I18
    # script under test or any historical migration source.
    token_support = load_module('i18_baseline_i12_batch_repair', root / 'scripts/pilot/install-access-token-management-update.py')
    final_transaction = baseline.rfind('BEGIN TRANSACTION;\n')
    require(final_transaction >= 0 and I12 in baseline[final_transaction:], 'baseline_i12_final_transaction_missing')
    baseline = baseline[:final_transaction] + token_support.migration_sql_for_sqlcmd(baseline[final_transaction:])
    builder = load_module('i18_packaged_sql_builder', root / 'scripts/pilot/build-identity-package.py')
    installer = load_module('i18_packaged_sql_validator', root / 'scripts/pilot/install-identity-update.py')
    script = builder.migration_sql_for_sqlcmd(original)
    require(builder.migration_sql_for_sqlcmd(script) == script, 'migration_normalization_not_idempotent')
    installer.validate_migration_sql(script)
    runtime = load_module('i18_native_sqlcmd_runtime', root / 'scripts/pilot/install-quick-capture-update.py')
    inventory = load_module('i18_sqlcmd_inventory', root / 'scripts/pilot/inspect-native.py')
    connection = os.environ.get('PRODIMT_SQL_TEST_CONNECTION', '')
    match = re.fullmatch(
        r'Server=127\.0\.0\.1,14333;Database=master;User Id=sa;Password=([0-9a-f]{48}Aa1!);Encrypt=True;TrustServerCertificate=True',
        connection)
    require(match is not None, 'synthetic_sql_connection_unexpected')
    password = match[1]
    target = 'ProdimtI18Cli_' + uuid.uuid4().hex
    require(re.fullmatch(r'ProdimtI18Cli_[0-9a-f]{32}', target), 'disposable_database_name_invalid')
    runtime.SERVER, runtime.DATABASE = 'localhost', 'master'
    instance = runtime.Installer('unused-synthetic-package.zip')
    instance.inspector, instance.sql_tool = inventory, SQLCMD
    instance.sql_login, instance.sql_password = 'sa', password
    calls = []
    global SQL_DIAGNOSTIC_STATE
    SQL_DIAGNOSTIC_STATE = instance.state

    def container_run(args, *, input, capture_output, text, env, timeout, check):
        require(args[0] == SQLCMD and all(flag in args for flag in ('-C', '-I', '-b', '-x', '-y', '-w')),
                'native_sqlcmd_protocol_unexpected')
        require(args[args.index('-S') + 1] == 'localhost'
                and args[args.index('-d') + 1] in ('master', target), 'sqlcmd_target_not_disposable')
        require(capture_output is True and text is True and check is False and isinstance(input, str)
                and input.startswith(runtime.OPTIONS), 'native_sqlcmd_input_or_options_changed')
        require(set(env) == set(runtime.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'}
                and all(env[key] == value for key, value in runtime.CLEAN_ENV.items())
                and env['SQLCMDUSER'] == 'sa' and env['SQLCMDPASSWORD'] == password,
                'native_sqlcmd_environment_not_clean')
        require(password not in '\0'.join(args), 'sqlcmd_password_in_arguments')
        transport = ['/usr/bin/docker', 'exec', '--interactive', '--env', 'SQLCMDUSER',
                     '--env', 'SQLCMDPASSWORD', CONTAINER, *args]
        result = subprocess.run(transport, input=input, capture_output=True, text=True,
                                check=False, env=env, timeout=timeout)
        calls.append(hashlib.sha256(input[len(runtime.OPTIONS):].encode()).hexdigest())
        return result

    runtime.subprocess = types.SimpleNamespace(run=container_run, TimeoutExpired=subprocess.TimeoutExpired)
    execute = instance.sql_run
    query = instance.sql_json
    one = '18181818-0000-0000-0000-000000000001'
    two = '18181818-0000-0000-0000-000000000002'
    created = False
    try:
        phase('create-disposable-database')
        execute('CREATE DATABASE [' + target + '];', timeout=120)
        created = True
        runtime.DATABASE = target
        phase('create-i12-baseline')
        execute(baseline, timeout=180)
        execute("INSERT INTO dbo.AdminUsers(Id,UserName,DisplayName,PasswordHash,IsActive,CreatedAt) VALUES "
                "('" + one + "',N'i18_fixture_one',N'Fictional manager',N'i18_synthetic_hash_one',1,SYSDATETIMEOFFSET()),"
                "('" + two + "',N'i18_fixture_two',N'Fictional inactive manager',N'i18_synthetic_hash_two',0,SYSDATETIMEOFFSET());")
        before = query("SELECT (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations,"
                       "(SELECT COUNT(*) FROM dbo.AdminUsers) AS users_count,"
                       "(SELECT COUNT(*) FROM dbo.AuditLogs) AS audits_count,"
                       "(SELECT COUNT(*) FROM dbo.CustomerAccessTokens) AS tokens_count "
                       "FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
        require(before == {'migrations': 13, 'users_count': 2, 'audits_count': 0, 'tokens_count': 0},
                'fixture_i12_baseline_unexpected')
        phase('verify-restricted-token-metadata')
        metadata_checks = verify_restricted_token_metadata(execute, query, inventory, token_support, installer)
        phase('verify-i18-seed-rollback')
        # Inject one failure between the exact DDL and seed batches. SQLcmd's
        # XACT_ABORT/-b connection shutdown must roll back ALL DDL and history.
        prefix, tail = script.split('GO\n\n', 1)
        fault = prefix + 'GO\n\nCREATE TRIGGER dbo.I18SeedFailure ON dbo.SecurityPermissions AFTER INSERT AS THROW 51180,\'synthetic_seed_failure\',1;\nGO\n\n' + tail
        try:
            execute(fault, timeout=180)
        except runtime.InstallError:
            require(51180 in instance.state.get('sql_error_numbers', []), 'seed_failure_not_observed')
        else:
            raise FixtureFailure('seed_failure_expected')
        rolled_back = query(installer.SECURITY_SCHEMA)
        require(rolled_back == dict.fromkeys(installer.EXPECTED_SECURITY_SCHEMA, 0), 'failed_upgrade_schema_not_rolled_back')
        require(query("SELECT COUNT(*) AS migrations FROM dbo.__EFMigrationsHistory FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;") == {'migrations': 13},
                'failed_upgrade_history_not_rolled_back')
        phase('apply-exact-packaged-i18-upgrade')
        execute(script, timeout=180)
        require(query(installer.SECURITY_SCHEMA) == installer.EXPECTED_SECURITY_SCHEMA, 'upgrade_schema_unexpected')
        data = query(installer.SECURITY_DATA)
        require(data == {'states_count': 1, 'administrators': 1, 'system_permissions': 15, 'system_grants': 15,
                         'active_administrators': 1, 'active_deleted_users': 0}, 'upgrade_seed_unexpected')
        require(query(installer.ADMIN_ACCESS_PRESERVED) == {'missing': 0}, 'existing_users_missing_administrator_role')
        preserved = query("SELECT (SELECT COUNT(*) FROM dbo.AdminUsers WHERE "
                          "(Id='" + one + "' AND PasswordHash=N'i18_synthetic_hash_one' AND IsActive=1) OR "
                          "(Id='" + two + "' AND PasswordHash=N'i18_synthetic_hash_two' AND IsActive=0)) AS preserved_users,"
                          "(SELECT COUNT(*) FROM dbo.AdminUsers WHERE IsDeleted=0 AND SessionRevision='00000000-0000-0000-0000-000000000000') AS initial_sessions,"
                          "(SELECT COUNT(*) FROM dbo.AuditLogs) AS audits_count,"
                          "(SELECT COUNT(*) FROM dbo.CustomerAccessTokens) AS tokens_count,"
                          "(SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations "
                          "FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
        require(preserved == {'preserved_users': 2, 'initial_sessions': 2, 'audits_count': 0, 'tokens_count': 0, 'migrations': 14},
                'upgrade_modified_existing_data_or_credentials')
        phase('verify-repeat-preserves-removed-role')
        # Idempotent transport repetition must not undo an administrator's later
        # explicit role removal (the native installer itself skips migrated SQL).
        execute("DELETE FROM dbo.SecurityUserRoles WHERE UserId='" + two + "';")
        execute(script, timeout=180)
        require(query(installer.ADMIN_ACCESS_PRESERVED) == {'missing': 1}, 'repeat_upgrade_restored_removed_role')
        require(query("SELECT COUNT(*) AS total FROM dbo.SecurityPermissions FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;") == {'total': 15},
                'repeat_upgrade_duplicated_permissions')
        print(json.dumps({'status': 'passed', 'migration': I18, 'exact_packaged_sql_sha256': hashlib.sha256(script.encode()).hexdigest(),
                          'native_sqlcmd_calls': len(calls), 'ddl_seed_rollback_verified': True,
                          'existing_credentials_preserved': True, 'repeat_does_not_restore_grants': True, 'token_metadata_checks': metadata_checks}))
    finally:
        runtime.DATABASE = 'master'
        if created:
            execute('ALTER DATABASE [' + target + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + target + '];', timeout=120)


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        issue = str(error) if isinstance(error, FixtureFailure) else 'disposable_i18_sqlcmd_verification_failed'
        # Only fixed phase labels, the exception class and numeric SQL codes are
        # surfaced. Raw exception text, SQL, stderr and credentials stay private.
        numbers = SQL_DIAGNOSTIC_STATE.get('sql_error_numbers', [])
        numbers = sorted({value for value in numbers if type(value) is int and 0 < value <= 999999})[:10]
        print(json.dumps({'status': 'failed', 'issue': issue, 'phase': PHASE,
                          'exception_type': type(error).__name__, 'sql_error_numbers': numbers,
                          'secret_values_printed': False}))
        raise SystemExit(1)
