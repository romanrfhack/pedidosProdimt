import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

PATH = Path(__file__).resolve().parents[2] / 'scripts/pilot/diagnose-capture-save.py'
SPEC = importlib.util.spec_from_file_location('capture_save_diagnosis', PATH)
D = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(D)
STAMP = '1793418499000000'
FRAME = '   at Prodimt.Pedidos.Application.AdminOrders.AdminOrderCaptureService.SubmitAsync()'
SECRET = 'SYNTHETIC_PRIVATE_PAYLOAD_PASSWORD_JWT'


def journal(*messages):
    return '\n'.join(json.dumps(dict(MESSAGE=m, __REALTIME_TIMESTAMP=STAMP)) for m in messages)


class CaptureSaveDiagnosisTests(unittest.TestCase):
    def test_split_journal_lines_keep_capture_exception_together(self):
        raw = journal('fail: Microsoft.AspNetCore.Server.Kestrel[13]',
                      'System.InvalidOperationException: Sales channel \'AdminManualCapture\' is not configured.', FRAME)
        result = D.summarize_journal(raw)
        self.assertTrue(result['capture_error_found'])
        self.assertEqual(['admin_sales_channel_missing'], result['capture_error_events'][0]['categories'])

    def test_multiline_sql_error_returns_only_safe_metadata(self):
        raw = journal('fail: Microsoft.AspNetCore.Server.Kestrel[13]\n'
                      'Microsoft.EntityFrameworkCore.DbUpdateException: ' + SECRET + '\n'
                      '---> Microsoft.Data.SqlClient.SqlException (0x80131904): The INSERT permission was denied on the object \'OrderOperations\', ' + SECRET + '\n'
                      'Error Number:229,State:5,Class:14\n' + FRAME)
        result = D.summarize_journal(raw)
        item = result['capture_error_events'][0]
        self.assertEqual([229], item['sql_error_numbers'])
        self.assertEqual(['OrderOperations'], item['denied_tables'])
        self.assertNotIn(SECRET, json.dumps(result))
        self.assertNotIn('INSERT', json.dumps(result))

    def test_unrelated_failure_does_not_become_capture_cause(self):
        raw = journal('fail: Other[1]\nSystem.InvalidOperationException: ' + SECRET,
                      'info: Other[2]\n' + FRAME)
        result = D.summarize_journal(raw)
        self.assertFalse(result['capture_error_found'])
        self.assertEqual(1, len(result['other_error_events']))

    def test_arbitrary_log_values_cannot_escape(self):
        raw = journal('fail: Other[1]\nSystem.InvalidOperationException: ' + SECRET + '\n'
                      '   at Prodimt.Pedidos.' + SECRET + '.Evil()\nError Number:1934,State:1,Class:16\n' + FRAME)
        result = D.summarize_journal(raw)
        self.assertNotIn(SECRET, json.dumps(result))
        self.assertEqual(['sql_session_options_incompatible'], result['capture_error_events'][0]['categories'])

    def test_unknown_denied_object_and_invalid_timestamp_hidden(self):
        result = D.classify_event(SECRET, 'Microsoft.Data.SqlClient.SqlException: '
                                 "permission was denied on the object '" + SECRET + "'\n" + FRAME)
        self.assertEqual([], result['denied_tables'])
        self.assertIsNone(result['timestamp_utc'])
        self.assertNotIn(SECRET, json.dumps(result))

    def test_invalid_journal_records_never_echo(self):
        result = D.summarize_journal(SECRET + '\n[]\n' + json.dumps(dict(MESSAGE=[SECRET])))
        self.assertEqual(3, result['invalid_records'])
        self.assertFalse(result['capture_error_found'])
        self.assertNotIn(SECRET, json.dumps(result))

    def test_only_fixed_read_commands_run_without_database_or_api(self):
        outputs = [subprocess.CompletedProcess([], 0, 'active\n', SECRET),
                   subprocess.CompletedProcess([], 0, journal('info: Other[1]'), SECRET)]
        with patch.object(D.os, 'geteuid', return_value=0), patch.object(D.subprocess, 'run', side_effect=outputs) as run:
            report = D.diagnose()
        self.assertEqual('inspected', report['status'])
        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(['systemctl', 'show', D.SERVICE, '--property=ActiveState', '--value'], commands[0])
        self.assertEqual(['journalctl', '--unit', D.SERVICE, '--since', '-6 hours', '--lines', '4000',
                          '--no-pager', '--output=json', '--output-fields=MESSAGE,__REALTIME_TIMESTAMP'], commands[1])
        for call in run.call_args_list:
            self.assertEqual(subprocess.DEVNULL, call.kwargs['stdin'])
            self.assertEqual(D.CLEAN_ENV, call.kwargs['env'])
        self.assertFalse(report['database_queries_run'])
        self.assertFalse(report['order_replay_attempted'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_failed_command_hides_both_streams(self):
        with patch.object(D.os, 'geteuid', return_value=0), patch.object(D.subprocess, 'run',
                return_value=subprocess.CompletedProcess([], 1, SECRET, SECRET)):
            report = D.diagnose()
        self.assertEqual('read_command_failed', report['issue'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_timeout_does_not_print_captured_partial_logs(self):
        with patch.object(D.os, 'geteuid', return_value=0), patch.object(D.subprocess, 'run',
                side_effect=subprocess.TimeoutExpired(SECRET, 25, output=SECRET, stderr=SECRET)):
            report = D.diagnose()
        self.assertEqual('read_command_unavailable_or_timeout', report['issue'])
        self.assertNotIn(SECRET, json.dumps(report))

    def test_empty_journal_is_not_reported_as_verified_save(self):
        result = D.summarize_journal('')
        self.assertFalse(result['capture_error_found'])
        self.assertEqual([], result['capture_error_events'])

    def test_root_and_window_guards_precede_commands(self):
        with patch.object(D.os, 'geteuid', return_value=10), patch.object(D.subprocess, 'run') as run:
            self.assertEqual('root_required_for_service_journal', D.diagnose()['issue'])
            run.assert_not_called()
        with patch.object(D.os, 'geteuid', return_value=0), patch.object(D.subprocess, 'run') as run:
            self.assertEqual('hours_out_of_range', D.diagnose(25)['issue'])
            run.assert_not_called()

    def test_cli_never_prints_unexpected_exception_text(self):
        output = io.StringIO()
        with patch.object(D.os, 'geteuid', return_value=0), patch.object(D.subprocess, 'run', side_effect=RuntimeError(SECRET)), \
                patch('sys.argv', ['diagnose-capture-save.py']), contextlib.redirect_stdout(output):
            self.assertEqual(1, D.main())
        self.assertEqual('diagnostic_local_failure', json.loads(output.getvalue())['issue'])
        self.assertNotIn(SECRET, output.getvalue())


if __name__ == '__main__':
    unittest.main()
