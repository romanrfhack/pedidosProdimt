"""Recovery security boundaries without touching a real server or credentials."""
import base64
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import test_native_tty as tty_fixture


SCRIPT = Path(__file__).parents[2] / 'scripts/pilot/reset-native-admin.py'
spec = importlib.util.spec_from_file_location('admin_recovery_fixture', SCRIPT)
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)
PASSWORD = 'Ficticia-ñ-漢字-123!$(printf nope)'
ROW = {'id': '91c8c3c3-319a-4bed-8dd9-d4118deef85f', 'user_name': 'admin',
       'password_hash': 'fictional-old-hash', 'is_active': 1,
       'display_name': 'Fictional administrator', 'created_at': '2026-10-24T12:00:00+00:00'}
OPERATION = '49e9a0cf-c622-41bd-b7e0-fc89874e5a21'


class NativeAdminRecoveryTests(unittest.TestCase):
    def test_hash_format_and_random_salt(self):
        first, second = recovery.identity_hash(PASSWORD), recovery.identity_hash(PASSWORD)
        self.assertNotEqual(first, second)
        raw = base64.b64decode(first, validate=True)
        self.assertEqual(61, len(raw))
        self.assertEqual(1, raw[0])
        self.assertEqual((2, 100000, 16), struct.unpack('>III', raw[1:13]))
        self.assertNotIn(PASSWORD, first)

    def test_sql_transport_hides_password_and_hash_from_argv_and_rejects_untrusted_env(self):
        tool = recovery.Recovery('admin')
        tool.sql_tool = recovery.SQL_TOOLS[0]
        tool.sql_login, tool.sql_password = 'fictional_sql_admin', 'fictional_sql_secret_1'
        sensitive = recovery.reset_sql(ROW, recovery.identity_hash(PASSWORD), OPERATION)
        result = subprocess.CompletedProcess([], 0, '{"ok":1}', '')
        with patch.dict(os.environ, {'SQLCMDINI': 'untrusted.sql', 'SQLCMDSERVER': 'other-server'}), \
                patch.object(recovery, 'command', return_value=result) as run:
            self.assertEqual({'ok': 1}, tool.sql(sensitive))
        args = run.call_args.args[0]
        options = run.call_args.kwargs
        self.assertNotIn(tool.sql_password, '\0'.join(args))
        self.assertNotIn(ROW['password_hash'], '\0'.join(args))
        self.assertNotIn('-P', args)
        self.assertNotIn('-Q', args)
        self.assertTrue({'-I', '-b', '-x'} <= set(args))
        self.assertEqual(set(recovery.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'}, set(options['env']))
        self.assertEqual(recovery.OPTIONS + sensitive, options['data'])

    def test_sql_and_json_never_echo_free_error_messages(self):
        tool = recovery.Recovery('admin')
        tool.sql_tool = recovery.SQL_TOOLS[0]
        result = subprocess.CompletedProcess([], 1, 'Msg 51001, Level 16, State 1, Server secret\n', PASSWORD)
        with patch.object(recovery, 'command', return_value=result):
            with self.assertRaisesRegex(recovery.RecoveryError, '^sql_operation_failed$'):
                tool.sql('unused')
        self.assertEqual([51001], tool.state['sql_error_numbers'])
        self.assertNotIn(PASSWORD, json.dumps(tool.state))
        self.assertEqual({'ok': 1}, recovery.parse_json('ProdimtRecoveryJson\n--------------------\n{"ok":1}\n'))
        with self.assertRaisesRegex(recovery.RecoveryError, '^sql_response_invalid$'):
            recovery.parse_json('private unexpected output')

    def test_snapshot_requires_exact_target_and_active_single_account(self):
        valid = {'database_name': recovery.DATABASE, 'migrations_count': 9,
                 'i04_migration_present': 1, 'users': [ROW]}
        self.assertEqual(ROW, recovery.validate_snapshot(valid, 'admin'))
        for modified in ({'database_name': 'another_database'}, {'migrations_count': 5},
                         {'users': []}, {'users': [ROW, ROW]},
                         {'users': [dict(ROW, is_active=0)]},
                         {'users': [dict(ROW, id='invalid')]},
                         {'users': [dict(ROW, user_name='other')]}):
            with self.subTest(modified=modified), self.assertRaises(recovery.RecoveryError):
                recovery.validate_snapshot(dict(valid, **modified), 'admin')

    def test_reset_only_changes_hash_with_concurrency_guard_and_atomic_audit(self):
        script = recovery.reset_sql(ROW, recovery.identity_hash(PASSWORD), OPERATION)
        self.assertIn('SET PasswordHash=', script)
        self.assertIn('AND IsActive=1', script)
        self.assertIn('PasswordHash COLLATE Latin1_General_100_BIN2=', script)
        self.assertLess(script.index('BEGIN TRANSACTION'), script.index('UPDATE dbo.AdminUsers'))
        self.assertLess(script.index('INSERT INTO dbo.AuditLogs'), script.index('COMMIT TRANSACTION'))
        self.assertNotIn('SET IsActive', script)
        self.assertNotIn('INSERT INTO dbo.AdminUsers', script)
        self.assertNotIn('ALTER LOGIN', script)
        self.assertNotIn(PASSWORD, script)
        self.assertNotIn(ROW['password_hash'], script)
        injected = recovery.sql_text("x';\nGO\n:r /tmp/secret\n$(SQLCMDPASSWORD) ñ")
        self.assertRegex(injected, r'^CONVERT\(nvarchar\(max\),0x[0-9a-f]+\)$')

    def test_password_rejection_happens_before_backup_or_sql_update(self):
        for values in (('Fictional valid pass 123', 'mismatch'), ('short', 'short')):
            with self.subTest(values=values), patch.object(recovery, 'prompt_secret', side_effect=values):
                with self.assertRaises(recovery.RecoveryError):
                    recovery.new_password()

    def test_private_backup_has_only_old_account_hash_and_private_modes(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            original = Path.lstat

            def fixture_stat(path):
                st = original(path)
                if path == parent:
                    # CI owns its temp fixture, while the real parent is root.
                    fields = list(st)
                    fields[4] = 0
                    return os.stat_result(fields)
                return st

            tool = recovery.Recovery('admin')
            with patch.object(recovery, 'BACKUPS', parent), patch.object(Path, 'lstat', fixture_stat):
                tool.backup(ROW, OPERATION)
            target = Path(tool.state['private_account_backup'])
            self.assertEqual(0o600, target.stat().st_mode & 0o777)
            self.assertEqual(0o700, target.parent.stat().st_mode & 0o777)
            self.assertEqual(ROW, json.loads(target.read_text())['administrator'])
            self.assertNotIn(PASSWORD, target.read_text())

    def test_https_login_and_protected_query_keep_password_and_token_out_of_argv(self):
        token = 'a' * 30 + '.bbbb.cccc'
        replies = [subprocess.CompletedProcess([], 0, json.dumps({'accessToken': token, 'tokenType': 'Bearer'}) + '\n200', ''),
                   subprocess.CompletedProcess([], 0, '[{"userName":"admin","isActive":true}]\n200', '')]
        tool = recovery.Recovery('admin')
        with patch.object(recovery, 'command', side_effect=replies) as run:
            tool.verify_login(PASSWORD)
        self.assertTrue(tool.state['login_verified'])
        self.assertEqual(200, tool.state['admin_http_status'])
        for call in run.call_args_list:
            argv = '\0'.join(call.args[0])
            self.assertNotIn(PASSWORD, argv)
            self.assertNotIn(token, argv)
            self.assertNotIn('--insecure', argv)
            self.assertIn(recovery.DOMAIN + ':443:127.0.0.1', call.args[0])
        self.assertEqual(PASSWORD, json.loads(run.call_args_list[0].kwargs['data'])['password'])
        self.assertIn('Authorization: Bearer ' + token, run.call_args_list[1].kwargs['data'])

    def test_login_401_and_admin_403_are_not_reported_as_success(self):
        for replies in ([ (401, '{}') ], [ (200, json.dumps({'accessToken': 'a' * 40, 'tokenType': 'Bearer'})), (403, '{}') ]):
            tool = recovery.Recovery('admin')
            with patch.object(tool, 'http', side_effect=replies), self.assertRaises(recovery.RecoveryError):
                tool.verify_login(PASSWORD)
            self.assertFalse(tool.state['login_verified'])

    def test_apply_backs_up_before_write_and_preserves_reset_state_if_login_fails(self):
        tool = recovery.Recovery('admin')
        events = []
        snapshot = {'database_name': recovery.DATABASE, 'migrations_count': 9,
                    'i04_migration_present': 1, 'users': [ROW]}

        def sql(script):
            if 'UPDATE dbo.AdminUsers' in script:
                events.append('reset')
                return {'password_reset': 1, 'audit_recorded': 1}
            events.append('inspect')
            return snapshot

        def login(password):
            events.append('login')
            self.assertEqual(PASSWORD, password)
            raise recovery.RecoveryError('web_login_failed_after_reset')

        with patch.object(recovery.os, 'geteuid', return_value=0), \
                patch.object(tool, 'service_ready'), patch.object(Path, 'is_file', return_value=True), \
                patch.object(tool, 'http', return_value=(401, '{}')), \
                patch.object(recovery, 'prompt_secret', side_effect=['fictional_sa', 'fictional_sql_password']), \
                patch.object(recovery, 'new_password', return_value=PASSWORD), \
                patch.object(tool, 'sql', side_effect=sql), \
                patch.object(tool, 'backup', side_effect=lambda *_: events.append('backup')), \
                patch.object(tool, 'verify_login', side_effect=login):
            with self.assertRaisesRegex(recovery.RecoveryError, '^web_login_failed_after_reset$'):
                tool.apply()
        self.assertEqual(['inspect', 'backup', 'reset', 'login'], events)
        self.assertTrue(tool.state['password_reset'])
        self.assertTrue(tool.state['changes_made'])
        self.assertFalse(tool.state['login_verified'])
        self.assertNotIn(PASSWORD, json.dumps(tool.state))

    def test_preflight_failure_never_requests_credentials_or_changes_account(self):
        tool = recovery.Recovery('admin')
        with patch.object(recovery.os, 'geteuid', return_value=0), \
                patch.object(tool, 'service_ready', side_effect=recovery.RecoveryError('prodimt_service_not_i04')), \
                patch.object(recovery, 'prompt_secret') as prompt, patch.object(tool, 'sql') as sql, \
                patch.object(tool, 'backup') as backup:
            with self.assertRaises(recovery.RecoveryError):
                tool.apply()
        prompt.assert_not_called()
        sql.assert_not_called()
        backup.assert_not_called()
        self.assertFalse(tool.state['changes_made'])

    def test_real_tty_password_confirmation_without_echo_and_no_tty_fails_closed(self):
        load = ("import importlib.util,json,termios\n"
                f"s=importlib.util.spec_from_file_location('recovery_tty',{str(SCRIPT)!r})\n"
                "m=importlib.util.module_from_spec(s);s.loader.exec_module(m)\n")
        program = load + ("before=termios.tcgetattr(0)\n"
                          "p=m.new_password()\n"
                          f"print('PRODIMT_TTY_TEST_RESULT='+json.dumps({{'accepted':p=={PASSWORD!r},'restored':before==termios.tcgetattr(0)}}))")
        transcript, report = tty_fixture.NativeRealTerminalTests().terminal_session(program, (
            ('Nueva contraseña WEB (mínimo 16 caracteres; oculta): '.encode(), PASSWORD),
            ('Repite la nueva contraseña WEB (oculta): '.encode(), PASSWORD)))
        self.assertEqual({'accepted': True, 'restored': True}, report)
        self.assertNotIn(PASSWORD.encode(), transcript)
        program = load + "try:\n m.prompt_secret('Hidden: ')\nexcept m.RecoveryError as e:\n print(str(e))\n"
        child = subprocess.run([sys.executable, '-c', program], start_new_session=True,
                               input=PASSWORD, text=True, capture_output=True, timeout=5)
        self.assertEqual('interactive_tty_required', child.stdout.strip())
        self.assertEqual('', child.stderr)


if __name__ == '__main__':
    unittest.main()
