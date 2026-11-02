"""Exercise the exact I22 SQL transaction in an internally named temporary DB.

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
    '20261103010000_AddProductionAllocation',
]
MIGRATION = '20261103020000_AddDeliveryOperations'
SCHEMA = 'prodimt.delivery.validation.v1'
APPLICATION_USER = 'prodimt_pedidos_app'
ROLLBACK_MARKER = 'PRODIMT_I22_EXPECTED_ROLLBACK'


class ValidationFailure(Exception):
    def __init__(self, issue, cleanup_database=None):
        super().__init__(issue)
        self.cleanup_database = cleanup_database


def require(condition, issue):
    if not condition:
        raise ValidationFailure(issue)


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
    return isinstance(value, str) and re.fullmatch(r'(?:ProdimtI22Validation|ProdimtI02Test)_[a-f0-9]{32}', value) is not None


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
            require(result.returncode != 0 and ROLLBACK_MARKER in result.stdout,
                    'validation_expected_rollback_not_observed')
            return ''
        require(result.returncode == 0, 'validation_sql_operation_failed')
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
    exact_upgrade = installer.migration_sql_for_sqlcmd(files)
    database = 'ProdimtI22Validation_' + uuid.uuid4().hex
    transport = transport_factory(tool, server, login, password, options, clean_environment, parse_output, database)
    attempted = False
    cleanup = False
    issue = None
    try:
        attempted = True
        transport.run('master', 'CREATE DATABASE [' + database + '];', timeout=60)
        transport.run(database, baseline, timeout=240)
        # The generated DB has no login or access to production. Match the
        # existing app's broad reader/writer roles, then verify I22 DENYs win.
        transport.run(database, "CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;\n"
            "ALTER ROLE [db_datareader] ADD MEMBER [prodimt_pedidos_app];\n"
            "ALTER ROLE [db_datawriter] ADD MEMBER [prodimt_pedidos_app];\n")
        before = transport.query(history_query())
        require(before == [{'MigrationId': value} for value in BASE_IDS], 'validation_baseline_history_mismatch')
        empty = {key: [] for key in contract}
        require(schema.matches(transport.query(schema.schema_query(True)), empty), 'validation_baseline_delivery_schema_present')
        security_before = transport.query(installer.SECURITY_FINGERPRINT)
        # Force an error after the exact DDL and grants, before their COMMIT.
        ending = 'COMMIT;\nGO\n\n'
        require(exact_upgrade.endswith(ending), 'validation_upgrade_commit_unexpected')
        rollback_sql = exact_upgrade.removesuffix(ending) + "SELECT N'" + ROLLBACK_MARKER + "';\nTHROW 51022, 'I22 expected test rollback', 1;\nGO\n"
        transport.run(database, rollback_sql, timeout=180, expected_failure=True)
        require(transport.query(history_query()) == before
                and schema.matches(transport.query(schema.schema_query(True)), empty)
                and transport.query(installer.SECURITY_FINGERPRINT) == security_before,
                'validation_transaction_rollback_failed')
        transport.run(database, exact_upgrade, timeout=180)
        expected_history = [{'MigrationId': value} for value in BASE_IDS + [MIGRATION]]
        require(transport.query(history_query()) == expected_history, 'validation_upgrade_history_mismatch')
        require(schema.matches(transport.query(schema.schema_query(True)), contract),
                'validation_strict_schema_mismatch')
        require(schema.matches(transport.query(schema.schema_query(False), as_application=True), contract, strict=False),
                'validation_application_schema_mismatch')
        require(transport.query(installer.DELIVERY_SQL_PERMISSIONS, as_application=True) == installer.EXPECTED_DELIVERY_PERMISSIONS,
                'validation_application_permissions_mismatch')
        require(transport.query(installer.DELIVERY_ROLE_DATA) == dict(driver_roles=1, driver_grants=2, driver_total_grants=2),
                'validation_delivery_role_mismatch')
        require(transport.query(installer.SECURITY_FINGERPRINT) == security_before,
                'validation_existing_security_changed')
        counts_before = transport.query(installer.DELIVERY_COUNTS)
        require(counts_before == dict.fromkeys(installer.DELIVERY_TABLES, 0), 'validation_install_created_delivery_data')
        revision = 'SELECT CONVERT(varchar(36),Revision) AS revision FROM dbo.SecurityStates WHERE Id=1 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
        revision_before = transport.query(revision)
        # Idempotent replay must not reseed the role, update revision or add rows.
        transport.run(database, exact_upgrade, timeout=180)
        require(transport.query(history_query()) == expected_history
                and transport.query(installer.DELIVERY_COUNTS) == counts_before
                and transport.query(revision) == revision_before,
                'validation_upgrade_replay_changed_data')
    except ValidationFailure as error:
        issue = str(error)
    except Exception:
        issue = 'validation_sql_unexpected_failure'
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
        raise ValidationFailure(issue)
    return dict(status='passed', exactSql=True, strictMetadata=True, applicationPermissions=True,
                rollbackVerified=True, replayVerified=True, cleanupVerified=True)
