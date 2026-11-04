"""Exercise the exact I23 schedules transaction over delivery schema 17 in an internally named temporary DB.

No production catalog, deployment paths, service operation, backup or restore is
used here. The caller supplies already-authenticated SQL administrator transport
credentials in memory; only the generated test database can be modified.
"""
import json
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
]
MIGRATION = '20261103030000_AddOrderingSchedules'
SCHEMA = 'prodimt.ordering-schedules.validation.v1'
APPLICATION_USER = 'prodimt_pedidos_app'
ROLLBACK_MARKER = 'PRODIMT_I23_EXPECTED_ROLLBACK'


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
    return tuple(sorted({int(value) for value in re.findall(r'(?im)^\s*Msg\s+(\d{1,10})\b', output)
                         if 0 < int(value) <= 2147483647}))[:8]


def validate_baseline(sql):
    require(isinstance(sql, str) and 0 < len(sql) < 2 * 1024 * 1024,
            'validation_baseline_size_unexpected')
    ids = re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql)
    require(ids == BASE_IDS, 'validation_baseline_history_unexpected')
    require(not re.search(r'\b(?:USE|(?:CREATE|DROP|ALTER)\s+DATABASE|BACKUP|RESTORE)\b', sql, re.I)
            and not re.search(r'\[[^]]+\]\.\[[^]]+\]\.\[', sql),
            'validation_baseline_target_unexpected')
    require(sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == len(BASE_IDS),
            'validation_baseline_transactions_unexpected')


def generated_name(value):
    return isinstance(value, str) and re.fullmatch(r'(?:ProdimtI23Validation|ProdimtI02Test)_[a-f0-9]{32}', value) is not None


class Transport:
    def __init__(self, tool, server, login, password, options, clean_environment, parse_output, database):
        require(generated_name(database), 'validation_database_name_unexpected')
        self.tool, self.server, self.login, self.password = tool, server, login, password
        self.options, self.environment, self.parse_output = options, clean_environment, parse_output
        self.database = database

    def run(self, database, sql, timeout=120, expected_failure=False):
        require(database in ('master', self.database) and generated_name(self.database),
                'validation_database_target_unexpected')
        args = [self.tool, '-S', self.server, '-d', database, '-C', '-I', '-b', '-l', '5',
                '-t', str(max(10, timeout - 5)), '-x', '-y', '0', '-w', '65535']
        environment = dict(self.environment, SQLCMDUSER=self.login, SQLCMDPASSWORD=self.password)
        try:
            result = subprocess.run(args, input=self.options + sql, capture_output=True, text=True,
                                    env=environment, timeout=timeout, check=False)
        except (OSError, subprocess.TimeoutExpired, UnicodeError):
            raise ValidationFailure('validation_sql_transport_failed') from None
        if expected_failure:
            output = (result.stdout or '') + '\n' + (result.stderr or '')
            numbers = error_numbers(output)
            if not (result.returncode != 0 and 51023 in numbers
                    and set(numbers) <= {51023, 3621} and ROLLBACK_MARKER in output):
                raise ValidationFailure('validation_expected_rollback_not_observed',
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
            raise ValidationFailure('validation_metadata_unrecognized') from None


def history_query():
    return 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'


def cleanup_generated_database(tool, server, login, password, options, clean_environment, parse_output, database,
                               transport_factory=Transport):
    """Also cleans a runner-created DB after a killed or timed-out child."""
    require(generated_name(database), 'validation_database_name_unexpected')
    transport = transport_factory(tool, server, login, password, options, clean_environment, parse_output, database)
    transport.run('master', "IF DB_ID(N'" + database + "') IS NOT NULL BEGIN\n"
        'ALTER DATABASE [' + database + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\n'
        'DROP DATABASE [' + database + '];\nEND;', timeout=60)
    query = "SELECT CASE WHEN DB_ID(N'" + database + "') IS NULL THEN 1 ELSE 0 END AS cleaned FOR JSON PATH,WITHOUT_ARRAY_WRAPPER"
    output = transport.run('master', 'SELECT (' + query + ') AS ProdimtInspectionJson;')
    return parse_output(output) == {'cleaned': 1}


def validate_exact_sql(files, installer, schema, tool, server, login, password, options,
                       clean_environment, parse_output, transport_factory=Transport):
    baseline = files['validation-baseline.sql'].decode('utf-8-sig')
    validate_baseline(baseline)
    contract = json.loads(files['delivery-schema.json'])
    upgrade = installer.migration_sql_for_sqlcmd(files)
    database = 'ProdimtI23Validation_' + uuid.uuid4().hex
    transport = transport_factory(tool, server, login, password, options, clean_environment, parse_output, database)
    attempted, cleanup, issue = False, False, None
    empty_schedule = dict.fromkeys(installer.EXPECTED_SCHEDULE_SHAPE, 0)
    expected_before = [{'MigrationId': value} for value in BASE_IDS]
    try:
        attempted = True
        transport.phase = 'create-database'
        transport.run('master', 'CREATE DATABASE [' + database + '];', timeout=60)
        transport.phase = 'baseline'
        transport.run(database, baseline, timeout=240)
        transport.phase = 'baseline-permissions'
        transport.run(database, 'CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;\n'
            'ALTER ROLE [db_datareader] ADD MEMBER [prodimt_pedidos_app];\n'
            'ALTER ROLE [db_datawriter] ADD MEMBER [prodimt_pedidos_app];\n' + installer.DELIVERY_GRANTS)
        require(transport.query(history_query()) == expected_before, 'validation_baseline_history_mismatch')
        require(schema.matches(transport.query(schema.schema_query(True)), contract), 'validation_delivery_schema_mismatch')
        require(transport.query(installer.SCHEDULE_SHAPE) == empty_schedule, 'validation_schedule_already_present')
        security = transport.query(installer.SECURITY_FINGERPRINT)
        delivery = transport.query(installer.DELIVERY_FINGERPRINT)
        ending = 'COMMIT;\nGO\n\n'
        require(upgrade.endswith(ending), 'validation_upgrade_commit_unexpected')
        rollback = (upgrade.removesuffix(ending) + "SELECT N'" + ROLLBACK_MARKER +
                    "';\nGO\n\nTHROW 51023, '" + ROLLBACK_MARKER + "', 1;\nGO\n")
        transport.phase = 'rollback-probe'
        transport.run(database, rollback, timeout=180, expected_failure=True)
        transport.phase = 'rollback-verification'
        require(transport.query(history_query()) == expected_before
                and transport.query(installer.SCHEDULE_SHAPE) == empty_schedule
                and transport.query(installer.SECURITY_FINGERPRINT) == security
                and transport.query(installer.DELIVERY_FINGERPRINT) == delivery,
                'validation_transaction_rollback_failed')
        transport.phase = 'upgrade'
        transport.run(database, upgrade, timeout=180)
        expected_after = expected_before + [{'MigrationId': MIGRATION}]
        require(transport.query(history_query()) == expected_after, 'validation_upgrade_history_mismatch')
        require(transport.query(installer.SCHEDULE_SHAPE) == installer.EXPECTED_SCHEDULE_SHAPE,
                'validation_schedule_schema_mismatch')
        require(transport.query(installer.SCHEDULE_PERMISSIONS, as_application=True) == installer.EXPECTED_SCHEDULE_PERMISSIONS,
                'validation_schedule_permissions_mismatch')
        require(transport.query(installer.DELIVERY_SQL_PERMISSIONS, as_application=True) == installer.EXPECTED_DELIVERY_PERMISSIONS,
                'validation_delivery_permissions_changed')
        require(schema.matches(transport.query(schema.schema_query(True)), contract), 'validation_delivery_schema_changed')
        transport.phase = 'replay'
        transport.run(database, upgrade, timeout=180)
        require(transport.query(history_query()) == expected_after
                and transport.query(installer.SECURITY_FINGERPRINT) == security
                and transport.query(installer.DELIVERY_FINGERPRINT) == delivery,
                'validation_upgrade_replay_changed_data')
    except ValidationFailure as error:
        issue = error
    except Exception:
        issue = ValidationFailure('validation_sql_unexpected_failure')
    finally:
        if attempted:
            try:
                cleanup = cleanup_generated_database(tool, server, login, password, options,
                    clean_environment, parse_output, database, transport_factory)
            except Exception:
                cleanup = False
    if not cleanup:
        raise ValidationFailure('validation_database_cleanup_failed', database)
    if issue:
        raise issue
    return dict(status='passed', exactSql=True, strictMetadata=True, applicationPermissions=True,
                rollbackVerified=True, replayVerified=True, deliveryPreserved=True, cleanupVerified=True)
