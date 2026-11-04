"""C1 gate safety, exact transitions, uncertain results and secret-free errors."""
import importlib.util
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('c1_sql_gate', ROOT / 'scripts/pilot/validate-consolidation-c1-sql.py')
validation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validation)


def files():
    baseline = ''.join("BEGIN TRANSACTION;\nINSERT INTO [__EFMigrationsHistory] VALUES (N'" + migration +
        "', N'10.0.8');\nCOMMIT;\nGO\n\n" for migration in validation.BASE_IDS)
    up = ("BEGIN TRANSACTION;\nIF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + validation.MIGRATION +
        "')\nBEGIN\nALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(max) NULL;\n"
        "INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'" + validation.MIGRATION +
        "', N'10.0.8');\nEND;\nCOMMIT;\nGO\n\n")
    down = ("BEGIN TRANSACTION;\nIF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + validation.MIGRATION +
        "')\nBEGIN\nIF EXISTS (SELECT 1 FROM [AuditLogs] WHERE DATALENGTH([MetadataJson]) > 4000)\n"
        "THROW 51024, '" + validation.DOWNGRADE_MARKER + "', 1;\n"
        "ALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(2000) NULL;\n"
        "DELETE FROM [__EFMigrationsHistory]\nWHERE [MigrationId] = N'" + validation.MIGRATION +
        "';\nEND;\nCOMMIT;\nGO\n\n")
    return dict(zip(('validation-baseline.sql', 'migrations-c1.sql', 'rollback-c1.sql'),
        (value.encode() for value in (baseline, up, down))))


class SyntheticSql:
    def __init__(self):
        self.files = files()
        self.calls = []
        self.exists = self.expanded = self.long = False
        self.creation_uncertain = self.cleanup_bad = self.rollback_bad = self.permissions_bad = False
        self.changed_data = self.bad_unicode = self.lossy_downgrade = self.safe_downgrade_bad = False
        self.reverse_table_collation = False
        self.omit_empty_fingerprints = False
        # Independent historical db_datareader/db_datawriter fixture, not the
        # constant whose correctness these tests must detect.
        self.audit_permissions = dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1)
        self.permission_drift_phase = None
        self.permission_observations = []

    def factory(self, tool, server, login, password, options, clean_environment, parse_output, database):
        self.database = database
        return self

    def run(self, database, sql, timeout=120, expected_failure=None):
        if database not in ('master', self.database):
            raise AssertionError('A configured catalog may never be targeted')
        self.calls.append((database, sql, expected_failure))
        if sql.startswith('CREATE DATABASE'):
            self.exists = True
            if self.creation_uncertain:
                raise validation.ValidationFailure('validation_sql_transport_failed')
        elif sql.startswith('IF DB_ID'):
            if self.cleanup_bad:
                raise validation.ValidationFailure('validation_sql_transport_failed')
            self.exists = False
        elif 'THEN 1 ELSE 0 END AS cleaned' in sql:
            return json.dumps({'cleaned': int(not self.exists)})
        elif expected_failure and expected_failure[0] == 51025:
            self.expanded = self.rollback_bad
        elif expected_failure and expected_failure[0] == 51024:
            if self.lossy_downgrade:
                self.long = False
        elif sql == self.files['migrations-c1.sql'].decode():
            self.expanded = True
        elif sql == self.files['rollback-c1.sql'].decode():
            self.expanded = False
        elif validation.LONG_INSERT_SQL in sql:
            if "EXECUTE AS USER=N'prodimt_pedidos_app'" not in sql:
                raise AssertionError('The long write must exercise application identity')
            self.long = True
        elif sql == validation.LONG_DELETE_SQL:
            self.long = False
        return ''

    def query(self, query, as_application=False):
        if query == validation.TABLES_QUERY:
            return [{'name': name} for name in sorted([*validation.TABLE_KEYS, '__EFMigrationsHistory'], reverse=self.reverse_table_collation)]
        if query == validation.history_query():
            return [{'MigrationId': value} for value in validation.BASE_IDS + ([validation.MIGRATION] if self.expanded else [])]
        if query == validation.AUDIT_SHAPE:
            if self.safe_downgrade_bad and self.phase == 'safe-downgrade':
                return validation.EXPECTED_AUDIT_SHAPE_AFTER.copy()
            return (validation.EXPECTED_AUDIT_SHAPE_AFTER if self.expanded else validation.EXPECTED_AUDIT_SHAPE_BEFORE).copy()
        if query == validation.PRESERVATION_QUERY:
            result = dict.fromkeys(validation.TABLE_KEYS, 'a' * 64)
            if self.omit_empty_fingerprints:
                return {'AuditLogs': 'a' * 64}
            if self.long:
                result['AuditLogs'] = 'b' * 64
            if self.changed_data and self.expanded:
                result['Customers'] = 'c' * 64
            return result
        if query == validation.EXISTING_QUERY:
            return {'MetadataJson': validation.EXISTING_METADATA}
        if query == validation.AUDIT_PERMISSIONS:
            if not as_application:
                raise AssertionError('Audit permissions must be inspected as application identity')
            observed = self.audit_permissions.copy()
            if self.permissions_bad or self.phase == self.permission_drift_phase:
                observed['UPDATE'] = 0
            self.permission_observations.append((self.phase, observed.copy()))
            return observed
        if query == validation.LONG_QUERY:
            if not as_application:
                raise AssertionError('Long data must be read by application identity')
            return {'MetadataJson': 'corrupt' if self.bad_unicode else validation.LONG_METADATA,
                    'bytes': len(validation.LONG_METADATA.encode('utf-16-le'))}
        raise AssertionError('Unknown synthetic query')


class ConsolidationC1SqlValidationTests(unittest.TestCase):
    def gate(self, fake):
        return validation.validate_exact_sql(fake.files, '/sqlcmd', 'synthetic', 'synthetic-admin',
            'private-canary', 'OPTIONS\n', {'PATH': '/usr/bin:/bin'}, json.loads, fake.factory)

    def test_exact_upgrade_lossy_and_safe_downgrade_replay_unicode_permissions_and_cleanup(self):
        fake = SyntheticSql()
        summary = self.gate(fake)
        self.assertEqual(summary['status'], 'passed')
        for name in ('exactSql', 'rollbackVerified', 'replayVerified', 'lossyDowngradeRejected',
                     'safeDowngradeVerified', 'unicodeAuditVerified', 'preservationVerified', 'cleanupVerified'):
            self.assertIs(summary[name], True)
        self.assertFalse(fake.exists)
        self.assertRegex(fake.database, r'^ProdimtC1Validation_[a-f0-9]{32}$')
        self.assertTrue(all(database in ('master', fake.database) for database, _, _ in fake.calls))
        self.assertEqual([failure[0] for _, _, failure in fake.calls if failure], [51025, 51024])
        self.assertEqual(sum(sql == fake.files['migrations-c1.sql'].decode() for _, sql, _ in fake.calls), 3)
        self.assertFalse(any('ProdimtPedidos' in sql or 'RESTORE' in sql for _, sql, _ in fake.calls))
        self.assertEqual([phase for phase, _ in fake.permission_observations], [
            'baseline-permissions-and-data', 'rollback-verification', 'upgrade',
            'reject-lossy-downgrade', 'safe-downgrade', 'upgrade-and-replay'])
        self.assertTrue(all(bits == dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1)
                            for _, bits in fake.permission_observations))
        self.assertFalse(any('DENY' in sql.upper() for _, sql, _ in fake.calls))

    def test_permissions_drift_at_every_transition_blocks_and_cleans_database(self):
        for phase in ('baseline-permissions-and-data', 'rollback-verification', 'upgrade',
                      'reject-lossy-downgrade', 'safe-downgrade', 'upgrade-and-replay'):
            with self.subTest(phase=phase):
                fake = SyntheticSql()
                fake.permission_drift_phase = phase
                with self.assertRaisesRegex(validation.ValidationFailure, 'validation_application_permissions_mismatch'):
                    self.gate(fake)
                self.assertFalse(fake.exists)

    def test_only_exact_integer_1111_is_a_valid_contract(self):
        import itertools
        for bits in itertools.product((0, 1), repeat=4):
            self.assertEqual(validation.audit_permissions_valid(dict(zip(('SELECT', 'INSERT', 'UPDATE', 'DELETE'), bits))),
                             bits == (1, 1, 1, 1))
        for bad in (None, [], {}, dict(SELECT=1, INSERT=1, UPDATE=1),
                    dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1, extra=1)):
            self.assertFalse(validation.audit_permissions_valid(bad))
        for key in ('SELECT', 'INSERT', 'UPDATE', 'DELETE'):
            for bad in (None, True, False, '1', '0', 1.0, -1, 2, [], {}):
                value = dict(SELECT=1, INSERT=1, UPDATE=1, DELETE=1)
                value[key] = bad
                self.assertFalse(validation.audit_permissions_valid(value))

    def test_uncertain_create_response_still_cleans_only_generated_database(self):
        fake = SyntheticSql()
        fake.creation_uncertain = True
        with self.assertRaisesRegex(validation.ValidationFailure, 'validation_sql_transport_failed'):
            self.gate(fake)
        self.assertFalse(fake.exists)
        self.assertTrue(any('DROP DATABASE [' + fake.database + ']' in sql for _, sql, _ in fake.calls))

    def test_table_identity_comparison_is_independent_of_sql_collation_order(self):
        fake = SyntheticSql()
        fake.reverse_table_collation = True
        self.assertEqual(self.gate(fake)['status'], 'passed')

    def test_preservation_query_respects_sqlcmd_input_line_capacity(self):
        self.assertLess(max(map(len, validation.PRESERVATION_QUERY.splitlines())), 512)
        self.assertEqual(validation.PRESERVATION_QUERY.count('HASHBYTES'), len(validation.TABLE_KEYS))

    def test_omitted_empty_table_digests_cannot_pass_preservation_validation(self):
        fake = SyntheticSql()
        fake.omit_empty_fingerprints = True
        with self.assertRaisesRegex(validation.ValidationFailure, 'validation_preservation_fingerprint_unrecognized'):
            self.gate(fake)
        self.assertFalse(fake.exists)

    def test_schema_data_permission_unicode_and_downgrade_failures_block_and_cleanup(self):
        cases = [('rollback_bad', 'validation_transaction_rollback_failed'),
                 ('changed_data', 'validation_upgrade_changed_existing_data'),
                 ('permissions_bad', 'validation_application_permissions_mismatch'),
                 ('bad_unicode', 'validation_unicode_audit_changed_or_truncated'),
                 ('lossy_downgrade', 'validation_lossy_downgrade_changed_data'),
                 ('safe_downgrade_bad', 'validation_safe_downgrade_changed_data')]
        for option, issue in cases:
            with self.subTest(option=option):
                fake = SyntheticSql()
                setattr(fake, option, True)
                with self.assertRaisesRegex(validation.ValidationFailure, issue):
                    self.gate(fake)
                self.assertFalse(fake.exists)

    def test_cleanup_failure_exposes_only_generated_database_identifier(self):
        fake = SyntheticSql()
        fake.cleanup_bad = True
        with self.assertRaises(validation.ValidationFailure) as caught:
            self.gate(fake)
        self.assertEqual(str(caught.exception), 'validation_database_cleanup_failed')
        self.assertEqual(caught.exception.cleanup_database, fake.database)
        self.assertNotIn('private-canary', str(caught.exception))

    def test_generated_database_names_and_targets_are_strict(self):
        valid = ['ProdimtC1Validation_' + 'a' * 32, 'ProdimtI02Test_' + 'b' * 32]
        for name in valid:
            self.assertTrue(validation.generated_name(name))
        for name in ('master', 'ProdimtPedidos', 'ProdimtC1Validation_prod',
                     'ProdimtC1Validation_' + 'A' * 32, valid[0] + '\n', '../' + valid[0]):
            with self.subTest(name=name), self.assertRaises(validation.ValidationFailure):
                validation.Transport('/sqlcmd', 'server', 'user', 'password', '', {}, json.loads, name)
        transport = validation.Transport('/sqlcmd', 'server', 'user', 'password', '', {}, json.loads, valid[0])
        with self.assertRaisesRegex(validation.ValidationFailure, 'validation_database_target_unexpected'):
            transport.run('ProdimtPedidos', 'SELECT 1;')

    def test_baseline_requires_exact_history_and_all_transactions(self):
        baseline = files()['validation-baseline.sql'].decode()
        validation.validate_baseline(baseline)
        for text in (baseline.replace(validation.BASE_IDS[-1], validation.MIGRATION), baseline.replace('COMMIT;', '', 1),
                     baseline + '\nUSE ProdimtPedidos;', baseline + '\n:r malicious.sql',
                     baseline + '\nCREATE DATABASE [unexpected];', baseline + '\nSELECT * FROM [server].[db].[table];'):
            with self.subTest(text=text[-80:]), self.assertRaises(validation.ValidationFailure):
                validation.validate_baseline(text)

    def test_upgrade_and_downgrade_match_only_the_expected_additive_delta(self):
        fixture = files()
        up, down = (fixture[key].decode() for key in ('migrations-c1.sql', 'rollback-c1.sql'))
        validation.validate_migration(up)
        validation.validate_rollback(down)
        for text in (up.replace('nvarchar(max)', 'nvarchar(1000)'), up.replace('AuditLogs', 'Orders'),
                     up.replace(validation.MIGRATION, 'unexpected'), up.replace('COMMIT;', 'DROP TABLE [Orders];\nCOMMIT;'),
                     up.replace('COMMIT;', 'DELETE FROM [AuditLogs];\nCOMMIT;')):
            with self.subTest(text=text[-120:]), self.assertRaises(validation.ValidationFailure):
                validation.validate_migration(text)
        for text in (down.replace('THROW 51024,', 'THROW 51026,'), down.replace('DATALENGTH', 'LEN'),
                     down.replace('> 4000', '> 8000'), down.replace('DELETE FROM [__EFMigrationsHistory]', 'DELETE FROM [AuditLogs]')):
            with self.subTest(text=text[-120:]), self.assertRaises(validation.ValidationFailure):
                validation.validate_rollback(text)

    def test_sqlcmd_receives_secret_in_environment_only_and_errors_are_redacted(self):
        name = 'ProdimtC1Validation_' + 'c' * 32
        transport = validation.Transport('/sqlcmd', 'server', 'user', 'private-canary', 'OPTIONS\n',
            {'PATH': '/usr/bin:/bin'}, json.loads, name)
        result = subprocess.CompletedProcess([], 1, 'Msg 2628, Level 16\nprivate-canary', '')
        with patch.object(validation.subprocess, 'run', return_value=result) as run:
            with self.assertRaises(validation.ValidationFailure) as caught:
                transport.run(name, 'synthetic SQL')
        self.assertEqual(caught.exception.sql_error_numbers, (2628,))
        self.assertNotIn('private-canary', str(caught.exception))
        args, kwargs = run.call_args
        self.assertNotIn('private-canary', ' '.join(args[0]))
        self.assertEqual(kwargs['env']['SQLCMDPASSWORD'], 'private-canary')
        self.assertEqual(kwargs['input'], 'OPTIONS\nsynthetic SQL')
        self.assertIn('-x', args[0])

    def test_expected_error_requires_nonzero_exit_correct_number_marker_and_no_other_error(self):
        transport = validation.Transport('/sqlcmd', 'server', 'user', 'secret', '', {}, json.loads,
            'ProdimtC1Validation_' + 'd' * 32)
        expected = (51025, validation.ROLLBACK_MARKER)
        valid_output = 'Msg 51025, Level 16\n' + validation.ROLLBACK_MARKER
        for code, output in ((0, valid_output), (1, validation.ROLLBACK_MARKER), (1, 'Msg 51025, Level 16'),
                             (1, valid_output + '\nMsg 2628, Level 16'), (1, valid_output.replace('51025', '51024'))):
            with self.subTest(code=code, output=output), patch.object(validation.subprocess, 'run',
                return_value=subprocess.CompletedProcess([], code, output, '')):
                with self.assertRaisesRegex(validation.ValidationFailure, 'validation_expected_failure_not_observed'):
                    transport.run(transport.database, 'synthetic', expected_failure=expected)
        with patch.object(validation.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1, valid_output, '')):
            self.assertEqual(transport.run(transport.database, 'synthetic', expected_failure=expected), '')

    def test_transport_timeout_and_output_parser_do_not_expose_secrets(self):
        transport = validation.Transport('/sqlcmd', 'server', 'user', 'private-canary', '', {},
            lambda value: (_ for _ in ()).throw(ValueError(value)), 'ProdimtC1Validation_' + 'e' * 32)
        with patch.object(validation.subprocess, 'run', side_effect=subprocess.TimeoutExpired('sqlcmd', 5, 'private-canary')):
            with self.assertRaisesRegex(validation.ValidationFailure, 'validation_sql_transport_failed'):
                transport.run(transport.database, 'synthetic')
        with patch.object(validation.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, 'private-canary', '')):
            with self.assertRaisesRegex(validation.ValidationFailure, 'validation_metadata_unrecognized'):
                transport.query('SELECT 1 FOR JSON PATH;')


if __name__ == '__main__':
    unittest.main()
