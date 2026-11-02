#!/usr/bin/env python3
"""Verify the exact I19 native ODBC transactions on disposable I12/I16/I18 bases.

Accepts only the synthetic CI connection or the private local-SQL test runner.
Creates randomly named databases and never accesses an installed application,
production database, environment file, token key, or production backup.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import types
import uuid


class FixtureFailure(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise FixtureFailure(issue)


def module(name, path):
    specification = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(result)
    return result


def synthetic_connection():
    value = os.environ.get('PRODIMT_SQL_TEST_CONNECTION', '')
    match = re.fullmatch(r'Server=127\.0\.0\.1,([0-9]{4,5});(?:Database=master;)?'
        r'User Id=sa;Password=([^;]+);Encrypt=(True|False);TrustServerCertificate=True;?', value)
    require(match is not None and 1024 <= int(match[1]) <= 65535, 'synthetic_connection_required')
    port, password = int(match[1]), match[2]
    ci = port == 14333 and re.fullmatch(r'[0-9a-f]{48}Aa1!', password) and match[3] == 'True'
    local = False
    if os.environ.get('PRODIMT_SQL_TEST_STATE'):
        folder = Path(os.environ['PRODIMT_SQL_TEST_STATE'])
        config = folder / 'private-config.json'
        require(folder.name.startswith('sql-run-') and folder.resolve() == folder
            and config.is_file() and not config.is_symlink(), 'local_runner_identity_invalid')
        identity = config.stat()
        require(identity.st_uid == os.getuid() and stat.S_IMODE(identity.st_mode) == 0o600,
            'local_runner_configuration_not_private')
        saved = json.loads(config.read_text())
        local = saved.get('port') == port and saved.get('connection_string') == value \
            and re.fullmatch(r'Sql-[A-Za-z0-9_-]{24,}!7a', password) is not None
    require(bool(ci or local), 'synthetic_connection_not_recognized')
    return port, password, local


def security_snapshot_query():
    tables = {
        'users': ('AdminUsers', 'Id'), 'roles': ('SecurityRoles', 'Id'),
        'permissions': ('SecurityPermissions', 'Id'), 'states': ('SecurityStates', 'Id'),
        'user_roles': ('SecurityUserRoles', 'UserId,RoleId'),
        'role_permissions': ('SecurityRolePermissions', 'RoleId,PermissionId'),
        'user_permissions': ('SecurityUserPermissions', 'UserId,PermissionId'),
    }
    return 'SELECT ' + ','.join(
        "CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo." + table +
        ' ORDER BY ' + order + ' FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS [' + label + ']'
        for label, (table, order) in tables.items()) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--setup-migration', type=Path, required=True)
    parser.add_argument('--identity-migration', type=Path, required=True)
    parser.add_argument('--migration', type=Path, required=True)
    parser.add_argument('--sqlcmd', type=Path,
        help='Native ODBC sqlcmd executable; otherwise use PATH or the existing CI container.')
    args = parser.parse_args()
    inputs = {'baseline': args.baseline, 'setup': args.setup_migration,
        'identity': args.identity_migration, 'production': args.migration}
    input_hashes = {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in inputs.items()}
    root = Path(__file__).resolve().parents[2]
    pilot = root / 'scripts/pilot'
    runtime = module('i19_native_transport', pilot / 'install-quick-capture-update.py')
    inventory = module('i19_native_inventory', pilot / 'inspect-native.py')
    token = module('i19_baseline_token_support', pilot / 'install-access-token-management-update.py')
    setup = module('i19_product_setup_support', pilot / 'install-customer-products-update.py')
    identity = module('i19_security_support', pilot / 'install-identity-update.py')
    identity_builder = module('i19_identity_normalizer', pilot / 'build-identity-package.py')
    installer = module('i19_exact_migration_installer', pilot / 'install-production-update.py')
    baseline = args.baseline.read_text(encoding='utf-8-sig')
    require(0 < len(baseline) < 2 * 1024 * 1024 and token.MIGRATION in baseline
        and installer.SETUP_MIGRATION not in baseline and installer.IDENTITY_MIGRATION not in baseline
        and installer.MIGRATION not in baseline
        and not re.search(r'(?im)^\s*(?:USE|CREATE\s+DATABASE|DROP\s+DATABASE)\b', baseline),
        'baseline_must_be_the_historical_i12_schema')
    # Repair only the already reviewed historical I12 SQLcmd batch boundary.
    # The input file and historical migrations remain unchanged.
    boundary = baseline.rfind('BEGIN TRANSACTION;\n')
    require(boundary >= 0 and token.MIGRATION in baseline[boundary:], 'i12_boundary_missing')
    baseline = baseline[:boundary] + token.migration_sql_for_sqlcmd(baseline[boundary:])
    files = {
        'customer-products-support.py': (pilot / 'install-customer-products-update.py').read_bytes(),
        'security-administration-support.py': (pilot / 'install-identity-update.py').read_bytes(),
        'migrations-i16.sql': args.setup_migration.read_bytes(),
        'migrations-i18.sql': identity_builder.migration_sql_for_sqlcmd(
            args.identity_migration.read_text(encoding='utf-8-sig')).encode(),
        'migrations-i19.sql': args.migration.read_bytes(),
    }
    base = runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION]
    increments = [installer.SETUP_MIGRATION, installer.IDENTITY_MIGRATION, installer.MIGRATION]
    final_ids = base + increments
    require(len(base) == 13 and len(final_ids) == 16, 'migration_manifest_unexpected')
    scripts = {migration: installer.migration_sql_for_sqlcmd(files, migration) for migration in increments}
    for migration, sql in scripts.items():
        require(sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1
            and sql.endswith('COMMIT;\nGO\n\n') and migration in sql,
            'exact_migration_transaction_unexpected')

    port, password, local = synthetic_connection()
    configured = args.sqlcmd or os.environ.get('PRODIMT_SQLCMD_PATH') or shutil.which('sqlcmd')
    if configured:
        sqlcmd = str(Path(configured).resolve())
        require(Path(sqlcmd).is_file() and os.access(sqlcmd, os.X_OK), 'native_sqlcmd_not_executable')
        inventory.SQL_TOOLS = (sqlcmd,)
        prefix = []
        runtime.SERVER = f'127.0.0.1,{port}'
    else:
        require(not local and port == 14333, 'local_runner_requires_native_sqlcmd')
        sqlcmd = '/opt/mssql-tools18/bin/sqlcmd'
        prefix = ['/usr/bin/docker', 'exec', '--interactive', '--env', 'SQLCMDUSER',
            '--env', 'SQLCMDPASSWORD', 'prodimt-i02-sqlserver']
        runtime.SERVER = 'localhost'
    runtime.DATABASE = 'master'
    instance = runtime.Installer('unused-synthetic-i19-package.zip')
    instance.inspector, instance.sql_tool = inventory, sqlcmd
    instance.sql_login, instance.sql_password = 'sa', password
    permitted_databases = {'master'}
    calls = []

    def native_run(argv, *, input, capture_output, text, env, timeout, check):
        require(argv[0] == sqlcmd and all(flag in argv for flag in ('-C', '-I', '-b', '-x', '-y', '-w'))
            and argv[argv.index('-S') + 1] == runtime.SERVER
            and argv[argv.index('-d') + 1] in permitted_databases,
            'native_transport_or_target_changed')
        require(capture_output is True and text is True and check is False
            and isinstance(input, str) and input.startswith(runtime.OPTIONS), 'native_sqlcmd_protocol_changed')
        require(set(env) == set(runtime.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'}
            and all(env[key] == value for key, value in runtime.CLEAN_ENV.items())
            and env['SQLCMDUSER'] == 'sa' and env['SQLCMDPASSWORD'] == password
            and password not in '\0'.join(argv), 'native_secret_handling_changed')
        calls.append(hashlib.sha256(input[len(runtime.OPTIONS):].encode()).hexdigest())
        return subprocess.run(prefix + argv, input=input, capture_output=True, text=True,
            env=env, timeout=timeout, check=False)

    runtime.subprocess = types.SimpleNamespace(run=native_run, TimeoutExpired=subprocess.TimeoutExpired)
    execute, query = instance.sql_run, instance.sql_json

    def history():
        return [item['MigrationId'] for item in query(inventory.SQL_MIGRATIONS)]

    def exact(migration):
        execute(scripts[migration], timeout=180)
        require(calls[-1] == hashlib.sha256(scripts[migration].encode()).hexdigest(),
            'migration_not_sent_verbatim')

    def as_app(statement):
        return inventory.parse_sql_output(execute("EXECUTE AS USER=N'prodimt_pedidos_app';\nSELECT (" +
            statement.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;\nREVERT;'))

    def expect_error(statement, number):
        instance.state.pop('sql_error_numbers', None)
        try:
            execute(statement, timeout=180)
        except runtime.InstallError:
            require(number in instance.state.get('sql_error_numbers', []),
                'fault_injection_did_not_reach_expected_sql_error_' + str(number))
        else:
            raise FixtureFailure('fault_injection_unexpectedly_committed')

    def app_principal(existing=()):
        execute('CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;\n' + '\n'.join(
            'GRANT SELECT ON dbo.' + table + ' TO [prodimt_pedidos_app];'
            for table in ('Products', 'Orders', 'OrderLines', 'Customers', 'Machines',
                'AdminUsers', 'CustomerFrequentProducts', 'CustomerAccessTokens')))
        # Dropping the fixture principal to force GRANT failure removes its
        # pre-existing rights too. Restore only the rights of the selected base.
        if installer.SETUP_MIGRATION in existing:
            execute(setup.SETUP_GRANTS)
        if installer.IDENTITY_MIGRATION in existing:
            execute(identity.SECURITY_GRANTS)

    routes = [('i12', []), ('i16', [installer.SETUP_MIGRATION]),
        ('i18', [installer.IDENTITY_MIGRATION])]
    results = []
    current_stage = 'initialization'
    try:
        for route, present in routes:
            target = 'ProdimtI19Cli_' + uuid.uuid4().hex
            permitted_databases.add(target)
            created = False
            try:
                current_stage = route + ':baseline'
                execute('CREATE DATABASE [' + target + '];', timeout=120)
                created = True
                runtime.DATABASE = target
                execute(baseline, timeout=180)
                require(history() == base, route + '_baseline_history_changed')
                customer, product, channel, order, first_user, second_user, role = (str(uuid.uuid4()) for _ in range(7))
                execute("""
INSERT INTO dbo.Customers(Id,Name,PhoneNumber,IsActive,CreatedAt,UpdatedAt)
 VALUES('""" + customer + """',N'Synthetic I19 native client',N'',1,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
INSERT INTO dbo.Products(Id,Name,IsActive,UnitCode) VALUES('""" + product + """',N'Synthetic native kilograms',1,N'kg');
INSERT INTO dbo.CustomerFrequentProducts(CustomerId,ProductId,SortOrder,IsActive)
 VALUES('""" + customer + """','""" + product + """',1,1);
INSERT INTO dbo.SalesChannels(Id,Name,Type,IsInternal) VALUES('""" + channel + """',N'Synthetic channel',N'Customer',0);
INSERT INTO dbo.Orders(Id,CustomerId,SalesChannelId,OrderDate,SubmittedAt,Status,SequenceNumber,IsLate,RequiresAdminReview)
 VALUES('""" + order + """','""" + customer + """','""" + channel + """','2026-11-02',SYSDATETIMEOFFSET(),N'Submitted',1,0,0);
INSERT INTO dbo.OrderLines(Id,OrderId,ProductId,Quantity,UnitCode,UnitPriceSnapshot,CurrencyCodeSnapshot)
 VALUES(NEWID(),'""" + order + """','""" + product + """',10,N'kg',0,N'MXN');
INSERT INTO dbo.CustomerAccessTokens(Id,CustomerId,TokenHash,DisplayName,IsActive,CreatedAt,ProtectedToken,MetadataVersion,IsReminderLink)
 VALUES(NEWID(),'""" + customer + """',N'synthetic-i19-hash',N'Synthetic protected access',1,SYSDATETIMEOFFSET(),N'v1.synthetic-opaque-preserved',7,1);
INSERT INTO dbo.AdminUsers(Id,UserName,DisplayName,PasswordHash,IsActive,CreatedAt) VALUES
 ('""" + first_user + """',N'i19_first',N'Synthetic manager',N'synthetic-hash-preserved-one',1,SYSDATETIMEOFFSET()),
 ('""" + second_user + """',N'i19_second',N'Synthetic worker',N'synthetic-hash-preserved-two',1,SYSDATETIMEOFFSET());
INSERT INTO dbo.AuditLogs(Id,EntityType,EntityId,EventType,OccurredAt,ActorType,Summary)
 VALUES(NEWID(),N'Fixture',N'synthetic-i19',N'FixtureCreated',SYSDATETIMEOFFSET(),1,N'Synthetic audit preserved');
""")
                counts = query(runtime.COUNTS_QUERY)
                fingerprint = query(setup.DATA_FINGERPRINT)
                tokens = query(token.TOKEN_COUNTS)
                def customize_security():
                    execute("DELETE FROM dbo.SecurityUserRoles WHERE UserId='" + second_user + "';\n"
                        "INSERT INTO dbo.SecurityRoles(Id,Name,Description,IsActive,IsSystem) VALUES('" + role + "',N'Synthetic operator',N'Preserved custom role',1,0);\n"
                        "INSERT INTO dbo.SecurityRolePermissions(RoleId,PermissionId) VALUES('" + role + "','18180000-0000-0000-0001-000000000001');\n"
                        "INSERT INTO dbo.SecurityUserRoles(UserId,RoleId) VALUES('" + second_user + "','" + role + "');\n"
                        "INSERT INTO dbo.SecurityUserPermissions(UserId,PermissionId) VALUES('" + second_user + "','18180000-0000-0000-0001-000000000006');")
                if present:
                    app_principal()
                    exact(present[0])
                    if installer.IDENTITY_MIGRATION in present:
                        customize_security()
                    else:
                        request_id = str(uuid.uuid4())
                        execute("INSERT INTO dbo.CustomerProductSetupRequests(Id,CustomerId,ProductId,ProductNameSnapshot,UnitCode,Version,IsResolved,CreatedAt,LastRequestedAt) "
                            "VALUES('" + request_id + "','" + customer + "','" + product + "',N'Synthetic existing setup',N'kg',2,0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());\n"
                            "INSERT INTO dbo.CustomerProductSetupOrders(SetupRequestId,OrderId,Source,RequestedAt) VALUES('" + request_id + "','" + order + "',N'Customer',SYSDATETIMEOFFSET());")
                    execute('DROP USER [prodimt_pedidos_app];')
                require(history() == sorted(base + present), route + '_base_route_history_invalid')
                before_history = history()
                existing_security = query(security_snapshot_query()) if installer.IDENTITY_MIGRATION in present else None
                first_missing = next(item for item in increments if item not in before_history)
                current_stage = route + ':grant-rollback'
                expect_error(scripts[first_missing], 15151)
                require(calls[-1] == hashlib.sha256(scripts[first_missing].encode()).hexdigest(),
                    'grant_fault_did_not_execute_exact_installer_sql')
                require(history() == before_history and query(runtime.COUNTS_QUERY) == counts
                    and query(setup.DATA_FINGERPRINT) == fingerprint and query(token.TOKEN_COUNTS) == tokens,
                    route + '_grant_failure_changed_history_or_existing_data')
                if existing_security is not None:
                    require(query(security_snapshot_query()) == existing_security,
                        route + '_grant_failure_changed_existing_security')
                absent_schema, absent_expected = (setup.SETUP_SCHEMA, setup.EXPECTED_SETUP_SCHEMA) \
                    if first_missing == installer.SETUP_MIGRATION else (identity.SECURITY_SCHEMA, identity.EXPECTED_SECURITY_SCHEMA)
                require(query(absent_schema) == dict.fromkeys(absent_expected, 0),
                    route + '_grant_failure_left_partial_schema')
                app_principal(present)
                applied = []
                for migration in increments[:-1]:
                    if migration not in history():
                        current_stage = route + ':apply-' + migration
                        exact(migration)
                        applied.append(migration)
                require(history() == final_ids[:-1], route + '_pre_production_history_invalid')
                security_before = query(installer.SECURITY_FINGERPRINT)
                setup_before = query(setup.SETUP_COUNTS)
                revision_before = query('SELECT Revision FROM dbo.SecurityStates FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;')
                current_stage = route + ':production-grant-rollback'
                execute('DROP USER [prodimt_pedidos_app];')
                expect_error(scripts[installer.MIGRATION], 15151)
                require(calls[-1] == hashlib.sha256(scripts[installer.MIGRATION].encode()).hexdigest(),
                    'production_grant_fault_did_not_send_exact_installer_sql')
                require(history() == final_ids[:-1]
                    and query(installer.PRODUCTION_SCHEMA) == dict.fromkeys(installer.EXPECTED_PRODUCTION_SCHEMA, 0)
                    and query(installer.SECURITY_FINGERPRINT) == security_before
                    and query('SELECT Revision FROM dbo.SecurityStates FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == revision_before
                    and query(setup.SETUP_COUNTS) == setup_before and query(runtime.COUNTS_QUERY) == counts,
                    route + '_production_grant_failure_left_partial_increment')
                app_principal(increments[:-1])
                current_stage = route + ':audit-rollback'
                execute("GO\n\nCREATE TRIGGER dbo.I19FixtureAuditFailure ON dbo.AuditLogs AFTER INSERT AS THROW 51191,'synthetic_i19_audit_failure',1;")
                sql = scripts[installer.MIGRATION]
                fault = sql.removesuffix('COMMIT;\nGO\n\n') + "GO\n\nINSERT INTO dbo.AuditLogs(Id,EntityType,EntityId,EventType,OccurredAt,ActorType,Summary) VALUES(NEWID(),N'Fixture',N'synthetic-fault',N'FaultInjected',SYSDATETIMEOFFSET(),1,N'Synthetic audit failure');\nCOMMIT;\nGO\n\n"
                expect_error(fault, 51191)
                require(history() == final_ids[:-1]
                    and query(installer.PRODUCTION_SCHEMA) == dict.fromkeys(installer.EXPECTED_PRODUCTION_SCHEMA, 0)
                    and query(installer.SECURITY_FINGERPRINT) == security_before
                    and query('SELECT Revision FROM dbo.SecurityStates FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == revision_before
                    and query(runtime.COUNTS_QUERY) == counts,
                    route + '_audit_failure_left_ddl_grants_seed_history_or_data')
                execute('DROP TRIGGER dbo.I19FixtureAuditFailure;')
                current_stage = route + ':apply-i19'
                exact(installer.MIGRATION)
                applied.append(installer.MIGRATION)
                migrated_history, production_schema = history(), query(installer.PRODUCTION_SCHEMA)
                if migrated_history != final_ids or production_schema != installer.EXPECTED_PRODUCTION_SCHEMA:
                    print(json.dumps(dict(migration_history=migrated_history, production_schema=production_schema,
                        expected_production_schema=installer.EXPECTED_PRODUCTION_SCHEMA)), file=sys.stderr)
                    print(json.dumps(query("SELECT name,definition FROM sys.check_constraints WHERE parent_object_id IN "
                        "(OBJECT_ID(N'dbo.ProductionBatches'),OBJECT_ID(N'dbo.ProductionAllocations')) ORDER BY name FOR JSON PATH;")), file=sys.stderr)
                require(migrated_history == final_ids and production_schema == installer.EXPECTED_PRODUCTION_SCHEMA,
                    route + '_production_schema_definitions_or_sixteen_migrations_invalid')
                require(query(setup.SETUP_SCHEMA) == setup.EXPECTED_SETUP_SCHEMA
                    and query(identity.SECURITY_SCHEMA) == identity.EXPECTED_SECURITY_SCHEMA,
                    route + '_integrated_i16_i18_schema_invalid')
                require(query(installer.SECURITY_FINGERPRINT) == security_before
                    and query(runtime.COUNTS_QUERY) == counts and query(setup.DATA_FINGERPRINT) == fingerprint
                    and query(token.TOKEN_COUNTS) == tokens and query(setup.SETUP_COUNTS) == setup_before,
                    route + '_upgrade_changed_existing_credentials_prices_tokens_setups_or_roles')
                require(query("SELECT (SELECT COUNT(*) FROM dbo.SecurityPermissions) AS total,"
                    "(SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE IsSystem=1 AND IsActive=1) AS system_permissions,"
                    "(SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE [Key] IN (N'operations.assign',N'operations.produce',N'operations.allocate')) AS production_permissions "
                    'FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == dict(total=18, system_permissions=18, production_permissions=3),
                    route + '_eighteen_system_permissions_missing')
                require(query("SELECT (SELECT COUNT(*) FROM dbo.Products WHERE RequiresMachine=1) AS products,"
                    "(SELECT COUNT(*) FROM dbo.CustomerFrequentProducts WHERE RequiresMachine=1 AND UseCustomerDefaultMachine=1) AS profiles,"
                    "(SELECT COUNT(*) FROM dbo.OrderLines WHERE RequiresMachine=1) AS lines FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
                    == dict(products=1, profiles=1, lines=1), route + '_historical_true_defaults_changed')
                actual_app_shape = as_app(installer.PRODUCTION_SHAPE)
                actual_production_grants = as_app(installer.PRODUCTION_SQL_PERMISSIONS)
                actual_setup_grants = as_app(setup.SETUP_PERMISSIONS)
                actual_security_grants = as_app(identity.SECURITY_PERMISSIONS)
                grants_valid = actual_app_shape == installer.EXPECTED_PRODUCTION_SCHEMA \
                    and actual_production_grants == installer.EXPECTED_PRODUCTION_PERMISSIONS \
                    and actual_setup_grants == setup.EXPECTED_SETUP_PERMISSIONS \
                    and actual_security_grants == identity.EXPECTED_SECURITY_PERMISSIONS
                if not grants_valid:
                    print(json.dumps(dict(application_shape=actual_app_shape, production_grants=actual_production_grants,
                        setup_grants=actual_setup_grants, security_grants=actual_security_grants)), file=sys.stderr)
                require(grants_valid,
                    route + '_application_schema_visibility_or_exact_grants_invalid')
                current_stage = route + ':retry-withdrawn-grants'
                if installer.IDENTITY_MIGRATION not in present:
                    customize_security()
                execute("DELETE FROM dbo.SecurityRolePermissions WHERE RoleId='18180000-0000-0000-0000-000000000001' AND PermissionId='18180000-0000-0000-0001-000000000018';\n"
                    "DELETE FROM dbo.SecurityUserPermissions WHERE UserId='" + second_user + "';")
                stable_security = query(security_snapshot_query())
                require([migration for migration in increments if migration not in history()] == [],
                    route + '_retry_did_not_skip_applied_migration_ids')
                # Also prove the exact guarded SQL cannot restore withdrawn
                # application-level role grants if its transport is repeated.
                for migration in increments:
                    exact(migration)
                require(query(security_snapshot_query()) == stable_security and history() == final_ids
                    and query(runtime.COUNTS_QUERY) == counts and query(setup.DATA_FINGERPRINT) == fingerprint,
                    route + '_retry_regranted_or_changed_existing_data')
                results.append(dict(base=route, migrations_before=len(before_history), migrations_after=16,
                    missing_migrations_applied=applied, grant_rollback=True, audit_rollback=True,
                    production_grant_rollback=True,
                    schema_definitions_verified=True, exact_minimal_grants=True,
                    true_defaults_preserved=True, withdrawn_security_preserved=True))
            finally:
                runtime.DATABASE = 'master'
                if created:
                    execute('ALTER DATABASE [' + target + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + target + '];', timeout=120)
                permitted_databases.discard(target)
        require(input_hashes == {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in inputs.items()},
            'migration_inputs_changed_during_verification')
        print(json.dumps(dict(status='passed', routes=results, native_sqlcmd_calls=len(calls),
            historical_input_sha256=input_hashes,
            exact_sql_sha256={migration: hashlib.sha256(sql.encode()).hexdigest() for migration, sql in scripts.items()},
            historical_inputs_modified=False, secret_values_printed=False), sort_keys=True))
    except Exception:
        print(json.dumps(dict(stage=current_stage, sql_error_numbers=instance.state.get('sql_error_numbers', []),
            secret_values_printed=False)), file=sys.stderr)
        raise
    finally:
        instance.sql_password = ''


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        issue = str(error) if isinstance(error, FixtureFailure) else 'disposable_i19_native_verification_failed'
        print(json.dumps(dict(status='failed', issue=issue, secret_values_printed=False)))
        raise SystemExit(1)
