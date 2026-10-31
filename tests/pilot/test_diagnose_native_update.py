import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile


SPEC = importlib.util.spec_from_file_location('native_update_diagnosis', Path(__file__).parents[2] / 'scripts/pilot/diagnose-native-update.py')
diagnosis = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(diagnosis)
SECRET = 'fictional-private-value-must-not-escape-998315'


class DiagnosisMigrationTests(unittest.TestCase):
    def test_every_partial_committed_prefix_is_reported_without_authorizing_replay(self):
        for count in range(5, 10):
            with self.subTest(count=count):
                report = diagnosis.migration_summary([{'MigrationId': value} for value in diagnosis.MIGRATIONS[:count]])
                self.assertEqual(count, report['known_count'])
                self.assertTrue(report['is_expected_prefix'])
                self.assertEqual(9 - count, report['pending_known_count'])
                self.assertFalse(report['automatic_retry_authorized'])

    def test_unknown_missing_and_duplicate_history_fail_prefix_without_echoing_unknown_id(self):
        cases = ([{'MigrationId': SECRET}],
                 [{'MigrationId': diagnosis.MIGRATIONS[1]}],
                 [{'MigrationId': diagnosis.MIGRATIONS[0]}] * 2)
        for rows in cases:
            with self.subTest(kind=len(rows)):
                report = diagnosis.migration_summary(rows)
                self.assertFalse(report['is_expected_prefix'])
                self.assertNotIn(SECRET, json.dumps(report))

    def test_malformed_history_uses_fixed_issue(self):
        with self.assertRaisesRegex(diagnosis.DiagnosisError, '^migration_metadata_unrecognized$'):
            diagnosis.migration_summary([{'unexpected': SECRET}])

    def test_schema_and_session_output_only_allowlisted_numeric_values(self):
        raw = {'database_name': diagnosis.DATABASE, 'server_version': '16.0.4236.2',
               'quoted_identifier': 0, 'audit_metadata_bytes': -1, 'orders_count': 0,
               'compatibility_level': 160, 'can_view_database_definition': 0,
               'password': SECRET, 'orders_source_bytes': SECRET,
               'schema_description': SECRET}
        report = diagnosis.state_summary(raw)
        self.assertEqual(0, report['session_without_quoted_identifier_flag']['quoted_identifier'])
        self.assertEqual(-1, report['schema_metadata']['audit_metadata_bytes'])
        self.assertIsNone(report['schema_metadata']['orders_source_bytes'])
        self.assertTrue(report['null_schema_metadata_may_mean_not_visible'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_wrong_database_never_echoes_its_unexpected_name(self):
        with self.assertRaises(diagnosis.DiagnosisError) as error:
            diagnosis.state_summary({'database_name': SECRET})
        self.assertEqual('database_identity_mismatch', str(error.exception))


class DiagnosisProtocolTests(unittest.TestCase):
    def inspector(self):
        return types.SimpleNamespace(permitted_connection=lambda values: values.get('password') == SECRET,
                                     parse_sql_output=json.loads, InspectionError=ValueError)

    def test_only_fixed_reads_use_clean_environment_and_reproduce_original_no_I_protocol(self):
        tool = diagnosis.SQL_TOOLS[0]
        for query in diagnosis.QUERIES:
            with self.subTest(query_kind='history' if query == diagnosis.SQL_HISTORY else 'state'):
                with patch.object(diagnosis, 'command', return_value=(0, '{}')) as execute:
                    diagnosis.query_sql(self.inspector(), tool, {'password': SECRET}, query)
                args = execute.call_args.args[0]
                env = execute.call_args.kwargs['env']
                self.assertEqual(tool, args[0])
                self.assertNotIn('-I', args)
                self.assertNotIn('-P', args)
                self.assertNotIn(SECRET, ' '.join(args))
                self.assertEqual(SECRET, env['SQLCMDPASSWORD'])
                self.assertEqual(diagnosis.LOGIN, env['SQLCMDUSER'])
                self.assertEqual(set(diagnosis.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'}, set(env))
                self.assertIn('SESSIONPROPERTY', diagnosis.SQL_STATE)
                self.assertNotRegex(args[-1].upper(), r'\b(?:INSERT|UPDATE|DELETE|ALTER|CREATE|DROP|BACKUP|RESTORE|EXEC)\b')

    def test_arbitrary_statement_tool_or_connection_never_runs(self):
        for tool, query, connection in ((diagnosis.SQL_TOOLS[0], 'DROP DATABASE anything', {'password': SECRET}),
                                         ('/tmp/sqlcmd', diagnosis.SQL_STATE, {'password': SECRET}),
                                         (diagnosis.SQL_TOOLS[0], diagnosis.SQL_STATE, {'password': 'wrong'})):
            with self.subTest(tool=tool):
                with patch.object(diagnosis, 'command') as execute:
                    with self.assertRaisesRegex(diagnosis.DiagnosisError, '^sql_query_not_authorized$'):
                        diagnosis.query_sql(self.inspector(), tool, connection, query)
                    execute.assert_not_called()

    def test_sql_failure_output_is_discarded(self):
        with patch.object(diagnosis, 'command', return_value=(1, SECRET)):
            with self.assertRaisesRegex(diagnosis.DiagnosisError, '^sql_read_failed$') as error:
                diagnosis.query_sql(self.inspector(), diagnosis.SQL_TOOLS[0], {'password': SECRET}, diagnosis.SQL_STATE)
        self.assertNotIn(SECRET, str(error.exception))

    def test_command_never_reads_stdin_or_emits_subprocess_stderr(self):
        child = types.SimpleNamespace(returncode=1, stdout='safe', stderr=SECRET)
        with patch.object(diagnosis.subprocess, 'run', return_value=child) as run:
            self.assertEqual((1, 'safe'), diagnosis.command(['systemctl', 'show', diagnosis.SERVICE]))
        self.assertEqual(diagnosis.subprocess.DEVNULL, run.call_args.kwargs['stdin'])
        self.assertEqual(diagnosis.CLEAN_ENV, run.call_args.kwargs['env'])


class DiagnosisPayloadTests(unittest.TestCase):
    def test_partial_metadata_survives_later_fixed_read_failure(self):
        def fail_after_session(package, result):
            result['sql'] = {'session_without_quoted_identifier_flag': {'quoted_identifier': 0}}
            raise diagnosis.DiagnosisError('sql_read_failed')
        with patch.object(diagnosis, 'collect', side_effect=fail_after_session):
            report = diagnosis.inspect('unused-package')
        self.assertEqual('sql_read_failed', report['issue'])
        self.assertEqual(0, report['sql']['session_without_quoted_identifier_flag']['quoted_identifier'])
        self.assertFalse(report['changes_made'])

    def test_inspector_is_loaded_only_after_whole_package_hash_matches_without_extracting(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fictional.zip'
            with zipfile.ZipFile(path, 'w') as archive:
                for name, data in (('inspect-native.py', b'raise RuntimeError("must not execute while reading")'),
                                   ('backend/Prodimt.Pedidos.Api.dll', b'fictional-dll'),
                                   ('frontend/index.html', b'fictional-index')):
                    archive.writestr(diagnosis.PACKAGE_ROOT + name, data)
            with self.assertRaisesRegex(diagnosis.DiagnosisError, '^package_sha_mismatch$'):
                diagnosis.trusted_payload(path)
            with patch.object(diagnosis, 'PACKAGE_SHA', hashlib.sha256(path.read_bytes()).hexdigest()):
                result = diagnosis.trusted_payload(path)
            self.assertEqual(b'fictional-dll', result['backend/Prodimt.Pedidos.Api.dll'])
            self.assertEqual(['fictional.zip'], [file.name for file in Path(directory).iterdir()])

    def test_main_redacts_unexpected_exception_and_does_not_write_report(self):
        with patch.object(diagnosis.sys, 'argv', ['diagnose', '--package', 'unused']), \
                patch.object(diagnosis, 'inspect', side_effect=RuntimeError(SECRET)), \
                patch('builtins.print') as output:
            self.assertEqual(1, diagnosis.main())
        report = json.loads(output.call_args.args[0])
        self.assertEqual('diagnosis_incomplete', report['issue'])
        self.assertFalse(report['changes_made'])
        self.assertNotIn(SECRET, json.dumps(report))


if __name__ == '__main__':
    unittest.main()
