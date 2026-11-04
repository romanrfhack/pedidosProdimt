"""Validate C1's exact SQL in a generated, disposable SQL Server database.

Credentials remain in the sqlcmd child environment. No configured catalog,
application release, service or production backup is accepted by this helper.
SQL statements are the hash-bound package statements, not substitute DDL.
"""
import re
import subprocess
import uuid

BASE_IDS = [
    '20260527190837_InitialCreate', '20260528021837_AddOrderAuditLogs',
    '20260528025208_AddPilotAuthentication', '20260528035015_AddCatalogManagementSupport',
    '20260528131416_AddCatalogExternalCodesAndImportSupport',
    '20261024164610_AddPilotCatalogAndOrderSnapshots', '20261024181206_AddDailyOrderOperations',
    '20261024181934_ExpandOrderAuditSnapshots', '20261024202253_AddOrderChangeNotices',
    '20261028142346_AddCustomerRestDays', '20261028202031_AddCustomerCaptureAliases',
    '20261101154618_AddSharedProductsAndCustomerPrices', '20261101190000_AddRecoverableCustomerAccessTokens',
    '20261102152307_AddCustomerProductSetup', '20261102235000_AddSecurityAdministration',
    '20261103010000_AddProductionAllocation', '20261103020000_AddDeliveryOperations',
    '20261103030000_AddOrderingSchedules',
]
MIGRATION = '20261104041928_ExpandAuditMetadata'
SCHEMA = 'prodimt.consolidation.c1.sql-validation.v1'
APPLICATION_USER = 'prodimt_pedidos_app'
ROLLBACK_MARKER = 'PRODIMT_C1_EXPECTED_ROLLBACK'
DOWNGRADE_MARKER = 'Audit metadata exceeds the previous column capacity; retain the expanded schema.'

AUDIT_SHAPE = """SELECT COUNT(*) AS columns_count,
 COALESCE(SUM(CASE WHEN TYPE_NAME(user_type_id)=N'nvarchar' THEN 1 ELSE 0 END),0) AS nvarchar_columns,
 COALESCE(SUM(CASE WHEN is_nullable=1 THEN 1 ELSE 0 END),0) AS nullable_columns,
 COALESCE(MAX(max_length),0) AS max_length
 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLogs') AND name=N'MetadataJson'
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_AUDIT_SHAPE_BEFORE = dict(columns_count=1, nvarchar_columns=1, nullable_columns=1, max_length=4000)
EXPECTED_AUDIT_SHAPE_AFTER = dict(EXPECTED_AUDIT_SHAPE_BEFORE, max_length=-1)
AUDIT_PERMISSIONS = 'SELECT ' + ','.join(
    "HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'" + name + "') AS [" + name + ']'
    for name in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
EXPECTED_AUDIT_PERMISSIONS = dict(SELECT=1, INSERT=1, UPDATE=0, DELETE=0)

# All 33 business tables in baseline 18. PK ordering makes the digest independent
# of row scan order. The history table is checked separately, with exact IDs.
TABLE_KEYS = {
    'Customers': '[Id]', 'Machines': '[Id]', 'Products': '[Id]', 'SalesChannels': '[Id]',
    'CustomerMachineAssignments': '[CustomerId],[MachineId]',
    'CustomerFrequentProducts': '[CustomerId],[ProductId]', 'Orders': '[Id]', 'OrderLines': '[Id]',
    'OrderAuditLogs': '[Id]', 'AdminUsers': '[Id]', 'CustomerAccessTokens': '[Id]', 'AuditLogs': '[Id]',
    'OrderOperations': '[OperationKey]', 'OrderChangeNotices': '[Id]',
    'CustomerProductPriceBooks': '[Id]', 'ProductConsolidationOperations': '[Id]',
    'CustomerProductPriceRevisions': '[Id]', 'CustomerProductSetupRequests': '[Id]',
    'CustomerProductSetupOrders': '[SetupRequestId],[OrderId]', 'SecurityPermissions': '[Id]',
    'SecurityRoles': '[Id]', 'SecurityStates': '[Id]', 'SecurityUserPermissions': '[UserId],[PermissionId]',
    'SecurityRolePermissions': '[RoleId],[PermissionId]', 'SecurityUserRoles': '[UserId],[RoleId]',
    'ProductionBatches': '[Id]', 'ProductionAllocations': '[Id]',
    'CustomerDeliveryLocations': '[CustomerId]', 'DeliveryAssignments': '[Id]', 'DeliveryEvents': '[Id]',
    'DeliveryRoutes': '[Code]', 'DeliveryTrips': '[Id]', 'RouteOrderSchedules': '[RouteCode]',
}
# sqlcmd's interactive stdin has a finite line buffer even with a wide output
# setting. Keep each expression on its own line so an identifier is never split.
PRESERVATION_QUERY = 'SELECT ' + ',\n'.join(
    "CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((" + 'SELECT * FROM dbo.[' + table + '] ORDER BY ' + key +
    " FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) AS [" + table + ']'
    for table, key in sorted(TABLE_KEYS.items())) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
TABLES_QUERY = "SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') ORDER BY name FOR JSON PATH;"

EXISTING_AUDIT_ID = 'c1000000-0000-0000-0000-000000000001'
LONG_AUDIT_ID = 'c1000000-0000-0000-0000-000000000002'
EXISTING_METADATA = '{"note":"ñ漢字🧾"}'
LONG_METADATA = '{"note":"' + 'ñ漢字🧾' * 1000 + '"}'


def audit_insert(identifier, expression):
    return ('INSERT INTO dbo.AuditLogs (Id,EntityType,EntityId,EventType,OccurredAt,ActorType,Summary,MetadataJson) '
        "VALUES ('" + identifier + "',N'C1Synthetic',N'synthetic',N'C1Validation',"
        "'2026-11-04T00:00:00+00:00',1,N'Validación sintética de auditoría'," + expression + ');')


SEED_SQL = audit_insert(EXISTING_AUDIT_ID, "N'" + EXISTING_METADATA + "'")
LONG_INSERT_SQL = audit_insert(LONG_AUDIT_ID,
    "N'{\"note\":\"'+REPLICATE(CAST(N'ñ漢字🧾' AS nvarchar(max)),1000)+N'\"}'")
LONG_DELETE_SQL = "DELETE FROM dbo.AuditLogs WHERE Id='" + LONG_AUDIT_ID + "';"
EXISTING_QUERY = "SELECT MetadataJson FROM dbo.AuditLogs WHERE Id='" + EXISTING_AUDIT_ID + "' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"
LONG_QUERY = "SELECT MetadataJson,DATALENGTH(MetadataJson) AS bytes FROM dbo.AuditLogs WHERE Id='" + LONG_AUDIT_ID + "' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"


class ValidationFailure(Exception):
    def __init__(self, issue, cleanup_database=None, sql_error_numbers=(), phase=None):
        super().__init__(issue)
        self.cleanup_database = cleanup_database
        self.sql_error_numbers = tuple(sql_error_numbers)
        self.phase = phase


def require(condition, issue):
    if not condition:
        raise ValidationFailure(issue)


def error_numbers(output):
    return tuple(sorted({int(number) for number in re.findall(r'(?im)^\s*Msg\s+(\d{1,10})\b', output)
                         if 0 < int(number) <= 2147483647}))[:8]


def generated_name(value):
    return isinstance(value, str) and re.fullmatch(r'(?:ProdimtC1Validation|ProdimtI02Test)_[a-f0-9]{32}', value) is not None


def _safe_script(sql, maximum=2 * 1024 * 1024):
    require(isinstance(sql, str) and 0 < len(sql) < maximum, 'validation_sql_size_unexpected')
    require(not re.search(r'\b(?:USE|(?:CREATE|DROP|ALTER)\s+DATABASE|BACKUP|RESTORE)\b', sql, re.I)
            and not re.search(r'\[[^]]+\]\.\[[^]]+\]\.\[', sql), 'validation_sql_target_unexpected')
    require(not re.search(r'(?im)^\s*(?::|!!)', sql), 'validation_sqlcmd_directive_unexpected')


def validate_baseline(sql):
    _safe_script(sql)
    ids = re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql)
    require(ids == BASE_IDS, 'validation_baseline_history_unexpected')
    require(sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == len(BASE_IDS),
            'validation_baseline_transactions_unexpected')


def _delta(sql):
    _safe_script(sql, 32768)
    require(sql.startswith('BEGIN TRANSACTION;\n') and sql.endswith('COMMIT;\nGO\n\n')
            and sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1,
            'validation_delta_transaction_unexpected')
    require(re.findall(r'(?im)^\s*(?:CREATE|DROP|ALTER) TABLE \[([^]]+)\]', sql) == ['AuditLogs']
            and not re.search(r'\b(?:CREATE TABLE|DROP TABLE|TRUNCATE|GRANT|DENY|UPDATE\s+\[)', sql, re.I),
            'validation_delta_targets_unexpected')
    require(sql.count('ALTER COLUMN [MetadataJson]') == 1, 'validation_delta_column_unexpected')


def validate_migration(sql):
    _delta(sql)
    require('ALTER COLUMN [MetadataJson] nvarchar(max) NULL;' in sql
            and re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) == [MIGRATION]
            and re.findall(r'(?im)^\s*INSERT INTO \[([^]]+)\]', sql) == ['__EFMigrationsHistory']
            and 'DELETE FROM' not in sql.upper() and 'THROW ' not in sql.upper(),
            'validation_upgrade_unexpected')


def validate_rollback(sql):
    _delta(sql)
    require('ALTER COLUMN [MetadataJson] nvarchar(2000) NULL;' in sql
            and 'DATALENGTH([MetadataJson]) > 4000' in sql and 'THROW 51024,' in sql
            and DOWNGRADE_MARKER in sql
            and re.findall(r'(?im)^\s*DELETE FROM \[([^]]+)\]', sql) == ['__EFMigrationsHistory']
            and "[MigrationId] = N'" + MIGRATION + "'" in sql
            and not re.findall(r'(?im)^\s*INSERT INTO', sql), 'validation_downgrade_unexpected')


class Transport:
    def __init__(self, tool, server, login, password, options, clean_environment, parse_output, database):
        require(generated_name(database), 'validation_database_name_unexpected')
        self.tool, self.server, self.login, self.password = tool, server, login, password
        self.options, self.environment, self.parse_output = options, clean_environment, parse_output
        self.database = database

    def run(self, database, sql, timeout=120, expected_failure=None):
        require(database in ('master', self.database) and generated_name(self.database), 'validation_database_target_unexpected')
        args = [self.tool, '-S', self.server, '-d', database, '-C', '-I', '-b', '-l', '5',
                '-t', str(max(10, timeout - 5)), '-x', '-y', '0', '-w', '65535']
        environment = dict(self.environment, SQLCMDUSER=self.login, SQLCMDPASSWORD=self.password)
        try:
            result = subprocess.run(args, input=self.options + sql, capture_output=True, text=True,
                env=environment, timeout=timeout, check=False)
        except (OSError, subprocess.TimeoutExpired, UnicodeError):
            raise ValidationFailure('validation_sql_transport_failed', phase=getattr(self, 'phase', None)) from None
        if expected_failure:
            number, marker = expected_failure
            output = (result.stdout or '') + '\n' + (result.stderr or '')
            numbers = error_numbers(output)
            if not (result.returncode != 0 and number in numbers and set(numbers) <= {number, 3621} and marker in output):
                raise ValidationFailure('validation_expected_failure_not_observed',
                    sql_error_numbers=numbers, phase=getattr(self, 'phase', None))
            return ''
        if result.returncode != 0:
            raise ValidationFailure('validation_sql_operation_failed',
                sql_error_numbers=error_numbers((result.stdout or '') + '\n' + (result.stderr or '')),
                phase=getattr(self, 'phase', None))
        return result.stdout

    def query(self, query, as_application=False):
        sql = 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
        if as_application:
            sql = "EXECUTE AS USER=N'" + APPLICATION_USER + "';\n" + sql + '\nREVERT;'
        try:
            return self.parse_output(self.run(self.database, sql))
        except ValidationFailure:
            raise
        except Exception:
            raise ValidationFailure('validation_metadata_unrecognized', phase=getattr(self, 'phase', None)) from None


def history_query():
    return 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'


def cleanup_generated_database(tool, server, login, password, options, clean_environment, parse_output, database,
                               transport_factory=Transport):
    require(generated_name(database), 'validation_database_name_unexpected')
    transport = transport_factory(tool, server, login, password, options, clean_environment, parse_output, database)
    transport.run('master', "IF DB_ID(N'" + database + "') IS NOT NULL BEGIN\n"
        'ALTER DATABASE [' + database + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\n'
        'DROP DATABASE [' + database + '];\nEND;', timeout=60)
    query = "SELECT CASE WHEN DB_ID(N'" + database + "') IS NULL THEN 1 ELSE 0 END AS cleaned FOR JSON PATH,WITHOUT_ARRAY_WRAPPER"
    return parse_output(transport.run('master', 'SELECT (' + query + ') AS ProdimtInspectionJson;')) == {'cleaned': 1}


def validate_exact_sql(files, tool, server, login, password, options, clean_environment, parse_output,
                       transport_factory=Transport):
    baseline = files['validation-baseline.sql'].decode('utf-8-sig')
    upgrade = files['migrations-c1.sql'].decode('utf-8-sig')
    downgrade = files['rollback-c1.sql'].decode('utf-8-sig')
    validate_baseline(baseline)
    validate_migration(upgrade)
    validate_rollback(downgrade)
    database = 'ProdimtC1Validation_' + uuid.uuid4().hex
    transport = transport_factory(tool, server, login, password, options, clean_environment, parse_output, database)
    attempted, cleaned, issue = False, False, None
    expected_before = [{'MigrationId': value} for value in BASE_IDS]
    expected_after = expected_before + [{'MigrationId': MIGRATION}]
    try:
        attempted = True
        transport.phase = 'create-database'
        transport.run('master', 'CREATE DATABASE [' + database + '];', timeout=60)
        transport.phase = 'baseline'
        transport.run(database, baseline, timeout=240)
        tables = transport.query(TABLES_QUERY)
        require(isinstance(tables, list) and all(isinstance(row, dict) and set(row) == {'name'}
                and isinstance(row['name'], str) for row in tables)
                and sorted(row['name'] for row in tables) == sorted([*TABLE_KEYS, '__EFMigrationsHistory']),
                'validation_baseline_tables_unexpected')
        require(transport.query(history_query()) == expected_before, 'validation_baseline_history_mismatch')
        require(transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_BEFORE, 'validation_baseline_audit_shape_mismatch')
        transport.phase = 'baseline-permissions-and-data'
        transport.run(database, 'CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;\n'
            'ALTER ROLE [db_datareader] ADD MEMBER [prodimt_pedidos_app];\n'
            'ALTER ROLE [db_datawriter] ADD MEMBER [prodimt_pedidos_app];\n'
            'DENY UPDATE,DELETE ON dbo.AuditLogs TO [prodimt_pedidos_app];\n' + SEED_SQL)
        before = transport.query(PRESERVATION_QUERY)
        require(isinstance(before, dict) and set(before) == set(TABLE_KEYS)
                and all(re.fullmatch(r'[a-fA-F0-9]{64}', value or '') for value in before.values()),
                'validation_preservation_fingerprint_unrecognized')
        require(transport.query(EXISTING_QUERY) == {'MetadataJson': EXISTING_METADATA}, 'validation_baseline_unicode_mismatch')
        transport.phase = 'rollback-probe'
        ending = 'COMMIT;\nGO\n\n'
        rollback = upgrade.removesuffix(ending) + "SELECT N'" + ROLLBACK_MARKER + "';\nGO\n\nTHROW 51025, '" + ROLLBACK_MARKER + "', 1;\nGO\n"
        transport.run(database, rollback, expected_failure=(51025, ROLLBACK_MARKER))
        transport.phase = 'rollback-verification'
        require(transport.query(history_query()) == expected_before
                and transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_BEFORE
                and transport.query(PRESERVATION_QUERY) == before, 'validation_transaction_rollback_failed')
        transport.phase = 'upgrade'
        transport.run(database, upgrade)
        require(transport.query(history_query()) == expected_after
                and transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_AFTER,
                'validation_upgraded_schema_mismatch')
        require(transport.query(PRESERVATION_QUERY) == before, 'validation_upgrade_changed_existing_data')
        require(transport.query(AUDIT_PERMISSIONS, as_application=True) == EXPECTED_AUDIT_PERMISSIONS,
                'validation_application_permissions_mismatch')
        transport.phase = 'application-unicode-audit'
        transport.run(database, "EXECUTE AS USER=N'" + APPLICATION_USER + "';\n" + LONG_INSERT_SQL + '\nREVERT;')
        require(transport.query(LONG_QUERY, as_application=True) == {
            'MetadataJson': LONG_METADATA, 'bytes': len(LONG_METADATA.encode('utf-16-le'))},
            'validation_unicode_audit_changed_or_truncated')
        with_long = transport.query(PRESERVATION_QUERY)
        transport.phase = 'reject-lossy-downgrade'
        transport.run(database, downgrade, expected_failure=(51024, DOWNGRADE_MARKER))
        require(transport.query(history_query()) == expected_after
                and transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_AFTER
                and transport.query(PRESERVATION_QUERY) == with_long, 'validation_lossy_downgrade_changed_data')
        transport.phase = 'safe-downgrade'
        # Only this synthetic UUID row in this disposable database is removed.
        transport.run(database, LONG_DELETE_SQL)
        transport.run(database, downgrade)
        require(transport.query(history_query()) == expected_before
                and transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_BEFORE
                and transport.query(PRESERVATION_QUERY) == before, 'validation_safe_downgrade_changed_data')
        transport.phase = 'upgrade-and-replay'
        transport.run(database, upgrade)
        transport.run(database, upgrade)
        require(transport.query(history_query()) == expected_after
                and transport.query(AUDIT_SHAPE) == EXPECTED_AUDIT_SHAPE_AFTER
                and transport.query(PRESERVATION_QUERY) == before
                and transport.query(AUDIT_PERMISSIONS, as_application=True) == EXPECTED_AUDIT_PERMISSIONS,
                'validation_upgrade_replay_changed_data')
    except ValidationFailure as error:
        if error.phase is None:
            error.phase = getattr(transport, 'phase', None)
        issue = error
    except Exception:
        issue = ValidationFailure('validation_sql_unexpected_failure', phase=getattr(transport, 'phase', None))
    finally:
        if attempted:
            try:
                cleaned = cleanup_generated_database(tool, server, login, password, options,
                    clean_environment, parse_output, database, transport_factory)
            except Exception:
                cleaned = False
    if not cleaned:
        raise ValidationFailure('validation_database_cleanup_failed', database)
    if issue:
        raise issue
    return dict(schema=SCHEMA, status='passed', exactSql=True, strictMetadata=True,
        applicationPermissions=True, preservationVerified=True, unicodeAuditVerified=True,
        lossyDowngradeRejected=True, safeDowngradeVerified=True, rollbackVerified=True,
        replayVerified=True, cleanupVerified=True)
