"""Reproduce I12's delivered SQL207 and verify the native batch-boundary repair.

Use the exact original ZIP SQL and the exact embedded I07 sql_run implementation.
Only the process/container boundary and disposable database target are adapted.
No production server, credential, key, backup or business record is accessed.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import types
import uuid


CONTAINER = 'prodimt-i02-sqlserver'
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
I11 = '20261101154618_AddSharedProductsAndCustomerPrices'
I12 = '20261101190000_AddRecoverableCustomerAccessTokens'
ORIGINAL_SQL_SHA256 = '7788b05db38a2cd78b2fbda67bcfd4924e9af99aab3f5b6b9ee7915cce340efe'
RUNTIME_SHA256 = '434a584d25d675a8bf7df3dc0f4c521ae8cdc8aef389ac1987129ba9a68bee66'
REVOCATION = 'CustomerAccessTokenRevokedForRecoveryUpgrade'


class FixtureFailure(Exception):
    """Messages contain fixed issue codes only, never SQL output or credentials."""


def require(condition, issue):
    if not condition:
        raise FixtureFailure(issue)


def load_module(name, path):
    specification = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    original_bytes = (root / 'tests/pilot/fixtures/i12-original-migration.sql').read_bytes()
    require(len(original_bytes) == 2211 and hashlib.sha256(original_bytes).hexdigest() == ORIGINAL_SQL_SHA256,
            'original_delivered_sql_fixture_changed')
    original = original_bytes.decode('utf-8-sig')
    baseline = arguments.baseline.read_text(encoding='utf-8-sig')
    require(0 < len(baseline) < 2 * 1024 * 1024 and I11 in baseline and I12 not in baseline
            and not re.search(r'(?im)^\s*(?:USE|CREATE\s+DATABASE|DROP\s+DATABASE)\b', baseline),
            'generated_i11_baseline_unexpected')
    runtime_path = root / 'scripts/pilot/install-quick-capture-update.py'
    require(hashlib.sha256(runtime_path.read_bytes()).hexdigest() == RUNTIME_SHA256,
            'embedded_native_sql_runtime_changed')
    runtime = load_module('i12_sqlcmd_original_runtime', runtime_path)
    inventory = load_module('i12_sqlcmd_inventory', root / 'scripts/pilot/inspect-native.py')
    repaired = load_module('i12_sqlcmd_repair', root / 'scripts/pilot/install-access-token-management-update.py')
    fixed = repaired.migration_sql_for_sqlcmd(original)
    require(fixed != original and repaired.migration_sql_for_sqlcmd(fixed) == fixed,
            'migration_repair_not_effective_or_idempotent')

    connection = os.environ.get('PRODIMT_SQL_TEST_CONNECTION', '')
    match = re.fullmatch(
        r'Server=127\.0\.0\.1,14333;Database=master;User Id=sa;Password=([0-9a-f]{48}Aa1!);Encrypt=True;TrustServerCertificate=True',
        connection)
    require(match is not None, 'synthetic_sql_connection_unexpected')
    password = match[1]
    target = 'ProdimtI12Cli_' + uuid.uuid4().hex
    require(re.fullmatch(r'ProdimtI12Cli_[0-9a-f]{32}', target), 'disposable_database_name_invalid')
    runtime.SERVER, runtime.DATABASE = 'localhost', 'master'
    instance = runtime.Installer('unused-synthetic-package.zip')
    instance.inspector, instance.sql_tool = inventory, SQLCMD
    instance.sql_login, instance.sql_password = 'sa', password
    calls = []

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
        # The generated password is inherited by name, never put into docker/sqlcmd argv.
        result = subprocess.run(transport, input=input, capture_output=True, text=True,
                                check=False, env=env, timeout=timeout)
        calls.append(hashlib.sha256(input[len(runtime.OPTIONS):].encode()).hexdigest())
        return result

    runtime.subprocess = types.SimpleNamespace(run=container_run, TimeoutExpired=subprocess.TimeoutExpired)

    def execute(sql, timeout=60):
        return instance.sql_run(sql, timeout=timeout)

    def query(sql):
        return instance.sql_json(sql)

    def expect_sql_error(sql, number, issue):
        instance.state.pop('sql_error_numbers', None)
        try:
            execute(sql)
        except runtime.InstallError:
            require(number in instance.state.get('sql_error_numbers', []), issue)
        else:
            raise FixtureFailure(issue)

    def counts():
        return query("""SELECT
          (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations,
          (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261101190000_AddRecoverableCustomerAccessTokens') AS i12,
          (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerAccessTokens')
            AND name IN(N'ProtectedToken',N'MetadataVersion',N'IsReminderLink')) AS token_columns,
          (SELECT COUNT(*) FROM dbo.CustomerAccessTokens) AS tokens,
          (SELECT COUNT(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1) AS active_tokens,
          (SELECT COUNT(*) FROM dbo.AuditLogs) AS audits,
          (SELECT COUNT(*) FROM dbo.AuditLogs WHERE EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade') AS upgrade_audits,
          (SELECT COUNT(*) FROM dbo.Customers) AS customers,
          (SELECT COUNT(*) FROM dbo.Orders) AS orders_count
          FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;""")

    def token_snapshot():
        return query("""SELECT CONVERT(varchar(36),Id) AS id,CONVERT(varchar(36),CustomerId) AS customer_id,
          TokenHash AS token_hash,DisplayName AS description,CONVERT(int,IsActive) AS active,
          CONVERT(varchar(48),CreatedAt,127) AS created_at,CONVERT(varchar(48),ExpiresAt,127) AS expires_at,
          CONVERT(varchar(48),LastUsedAt,127) AS last_used_at
          FROM dbo.CustomerAccessTokens ORDER BY TokenHash FOR JSON PATH,INCLUDE_NULL_VALUES;""")

    def audit_snapshot():
        return query("""SELECT CONVERT(varchar(36),Id) AS id,EntityId AS entity_id,EventType AS event_type,
          CONVERT(int,ActorType) AS actor_type,Summary AS summary,MetadataJson AS metadata
          FROM dbo.AuditLogs ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES;""")

    created = False
    try:
        execute('CREATE DATABASE [' + target + '];', timeout=90)
        created = True
        runtime.DATABASE = target
        execute(baseline, timeout=180)
        history = query(inventory.SQL_MIGRATIONS)
        require(len(history) == 12 and history[-1]['MigrationId'] == I11, 'i11_baseline_not_twelve_migrations')
        customer_id = str(uuid.uuid4())
        execute("""INSERT INTO dbo.Customers(Id,Name,PhoneNumber,IsActive,CreatedAt,UpdatedAt,ExternalCode)
          VALUES('""" + customer_id + """',N'Synthetic I12 native customer',N'',1,
            '2026-11-02T12:00:00+00:00','2026-11-02T12:00:00+00:00',N'SYNTHETIC-I12-SQLCMD');
          INSERT INTO dbo.AuditLogs(Id,EntityType,EntityId,EventType,OccurredAt,ActorType,Summary)
          VALUES(NEWID(),N'SyntheticHistory',N'i12-native-fixture',N'SyntheticBeforeI12',
            '2026-11-02T12:00:00+00:00',2,N'Historical synthetic audit retained.');""")
        for index in range(4):
            expiry = 'NULL' if index in (0, 3) else "'" + ('2020' if index == 1 else '2030') + "-01-01T12:00:00+00:00'"
            execute("INSERT INTO dbo.CustomerAccessTokens(Id,CustomerId,TokenHash,DisplayName,IsActive,ExpiresAt,CreatedAt,LastUsedAt) "
                    "VALUES('" + str(uuid.uuid4()) + "','" + customer_id + "',N'synthetic-i12-native-hash-" + str(index)
                    + "',N'Synthetic prior access " + str(index) + "'," + ('0' if index == 3 else '1') + ',' + expiry
                    + ",'2026-11-01T12:00:00+00:00','2026-11-01T13:00:00+00:00');")
        before = counts()
        prior_tokens, prior_audits = token_snapshot(), audit_snapshot()
        require(before == dict(migrations=12, i12=0, token_columns=0, tokens=4, active_tokens=3,
                               audits=1, upgrade_audits=0, customers=1, orders_count=0), 'synthetic_baseline_unexpected')

        expect_sql_error(original, 207, 'original_delivered_batch_did_not_reproduce_207')
        require(calls[-1] == hashlib.sha256(original.encode()).hexdigest(),
                'original_decoded_sql_not_sent_unchanged')
        require(counts() == before and token_snapshot() == prior_tokens and audit_snapshot() == prior_audits,
                'original_207_did_not_roll_back_to_complete_twelve_migration_baseline')
        print('I12 original ZIP SQL reproduced 207 through real ODBC sqlcmd; schema/history/tokens/audits remained at I11.')

        execute("""GO
CREATE TRIGGER dbo.RejectI12NativeAudit ON dbo.AuditLogs AFTER INSERT AS
BEGIN
 IF EXISTS(SELECT 1 FROM inserted WHERE EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade')
  THROW 51032,N'Synthetic I12 audit failure',1;
END;
GO
""")
        try:
            expect_sql_error(fixed, 51032, 'fixed_migration_audit_failure_did_not_throw_expected_error')
            require(counts() == before and token_snapshot() == prior_tokens and audit_snapshot() == prior_audits,
                    'fixed_migration_audit_failure_did_not_roll_back_ddl_revocation_and_history')
        finally:
            execute('DROP TRIGGER dbo.RejectI12NativeAudit;')
        print('I12 fixed GO batches share one transaction: audit THROW rolled back columns, revocation and migration history.')

        execute(fixed)
        require(counts() == before | dict(migrations=13, i12=1, token_columns=3, active_tokens=0,
                                         audits=4, upgrade_audits=3), 'fixed_migration_counts_invalid')
        require(token_snapshot() == [row | {'active': 0} for row in prior_tokens], 'legacy_token_identity_hash_or_metadata_changed')
        metadata = query("""SELECT TokenHash AS token_hash,MetadataVersion AS version,
          CONVERT(int,IsReminderLink) AS reminder,CASE WHEN ProtectedToken IS NULL THEN 1 ELSE 0 END AS no_cipher
          FROM dbo.CustomerAccessTokens ORDER BY TokenHash FOR JSON PATH;""")
        require(metadata == [dict(token_hash=row['token_hash'], version=2 if row['active'] else 1, reminder=0, no_cipher=1)
                             for row in prior_tokens], 'legacy_token_versions_or_recovery_fields_invalid')
        audits = audit_snapshot()
        revoked = [row for row in audits if row['event_type'] == REVOCATION]
        require(len(revoked) == 3 and {row['entity_id'].lower() for row in revoked}
                == {row['id'].lower() for row in prior_tokens if row['active']}
                and all(row['actor_type'] == 2 for row in revoked), 'legacy_revocation_audit_scope_invalid')
        require(all(row['token_hash'] not in json.dumps(audits) for row in prior_tokens), 'revocation_audit_discloses_token_hash')

        # An opaque synthetic protected value tests preservation, not encryption;
        # AES-GCM authentication is exercised by the separate protection tests.
        protected_id = str(uuid.uuid4())
        execute("INSERT INTO dbo.CustomerAccessTokens(Id,CustomerId,TokenHash,DisplayName,IsActive,ExpiresAt,CreatedAt,"
                "ProtectedToken,MetadataVersion,IsReminderLink) VALUES('" + protected_id + "','" + customer_id
                + "',N'synthetic-i12-current-protected-hash',N'Synthetic new protected access',1,NULL,"
                "'2026-11-02T12:00:00+00:00',N'v1.synthetic-protected-value-preserved',7,1);")
        stable_counts, stable_tokens, stable_audits = counts(), token_snapshot(), audit_snapshot()
        for _ in range(2):
            execute(fixed)
            require(counts() == stable_counts and token_snapshot() == stable_tokens and audit_snapshot() == stable_audits,
                    'repeated_fixed_script_changed_existing_tokens_or_audits')
        protected = query("""SELECT CONVERT(int,IsActive) AS active,MetadataVersion AS version,
          CONVERT(int,IsReminderLink) AS reminder,ProtectedToken AS protected_value
          FROM dbo.CustomerAccessTokens WHERE Id='""" + protected_id + "' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
        require(protected == dict(active=1, version=7, reminder=1, protected_value='v1.synthetic-protected-value-preserved'),
                'repeated_fixed_script_changed_new_protected_access')
        print('I12 fixed script migrated 12-to-13, audited each enabled legacy access once, preserved hashes and retained new protected access on repeat.')
    finally:
        try:
            if created:
                runtime.DATABASE = 'master'
                execute('ALTER DATABASE [' + target + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + target + '];', timeout=90)
        finally:
            instance.sql_password = ''
            password = ''


if __name__ == '__main__':
    try:
        main()
    except FixtureFailure as error:
        print('I12 native SQL regression failed: ' + str(error), file=sys.stderr)
        sys.exit(1)
    except (Exception, KeyboardInterrupt):
        print('I12 native SQL regression failed: unexpected_fixture_failure_without_sensitive_details', file=sys.stderr)
        sys.exit(1)
