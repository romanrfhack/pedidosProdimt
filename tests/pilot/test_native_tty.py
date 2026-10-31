"""Exercise the native administrator prompt using a real controlling terminal."""
import errno
import json
import os
from pathlib import Path
import pty
import select
import signal
import subprocess
import sys
import termios
import time
import unittest


UPDATER = Path(__file__).parents[2] / 'scripts/pilot/update-native.py'
LOGIN = 'fictional_tty_administrator'
PASSWORD = 'fictional-tty-password-917245'
RESULT = b'PRODIMT_TTY_TEST_RESULT='
LOAD = f"""
import importlib.util, json, sys, termios
spec = importlib.util.spec_from_file_location('native_tty_fixture', {str(UPDATER)!r})
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
"""


@unittest.skipUnless(os.name == 'posix', 'Native deployment requires a POSIX terminal')
class NativeRealTerminalTests(unittest.TestCase):
    def terminal_session(self, program, replies=()):
        """Reply only after getpass has disabled echo and emitted each prompt."""
        pid, master = pty.fork()
        if pid == 0:
            os.execv(sys.executable, [sys.executable, '-c', program])
        transcript = bytearray()
        answered = 0
        child_status = None
        deadline = time.monotonic() + 5
        try:
            while time.monotonic() < deadline:
                if select.select([master], [], [], 0.05)[0]:
                    try:
                        chunk = os.read(master, 65536)
                    except OSError as error:
                        if error.errno == errno.EIO:
                            break  # The child closed the PTY slave.
                        raise
                    if not chunk:
                        break
                    transcript.extend(chunk)
                if answered < len(replies):
                    prompt, answer = replies[answered]
                    if prompt in transcript:
                        self.assertFalse(termios.tcgetattr(master)[3] & termios.ECHO,
                                         'Credentials must never be sent to an echoing terminal')
                        os.write(master, answer if isinstance(answer, bytes) else answer.encode() + b'\n')
                        answered += 1
                if child_status is None:
                    found, status = os.waitpid(pid, os.WNOHANG)
                    if found:
                        child_status = status
                if child_status is not None:
                    # Drain final output in the next iteration before the EIO.
                    if not select.select([master], [], [], 0)[0]:
                        break
            else:
                self.fail('Private terminal prompt did not finish within five seconds')
            while child_status is None and time.monotonic() < deadline:
                found, status = os.waitpid(pid, os.WNOHANG)
                if found:
                    child_status = status
                else:
                    time.sleep(0.005)
            self.assertIsNotNone(child_status, 'Terminal child did not exit')
            self.assertEqual(0, os.waitstatus_to_exitcode(child_status))
            self.assertEqual(len(replies), answered)
            self.assertIn(RESULT, transcript)
            payload = bytes(transcript).split(RESULT, 1)[1].splitlines()[0]
            return bytes(transcript), json.loads(payload)
        finally:
            os.close(master)
            if child_status is None:
                try:
                    os.kill(pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                os.waitpid(pid, 0)

    def prompt_program(self, expected_login=LOGIN):
        return LOAD + f"""
before = termios.tcgetattr(0)
try:
    credentials = module.Updater('unused-package.zip').prompt_admin()
    report = {{'accepted': credentials.login == {expected_login!r} and credentials.password == {PASSWORD!r}}}
except module.UpdateError as error:
    report = {{'issue': str(error)}}
after = termios.tcgetattr(0)
report['terminal_restored'] = before == after
report['echo_enabled'] = bool(after[3] & termios.ECHO)
print('PRODIMT_TTY_TEST_RESULT=' + json.dumps(report), flush=True)
"""

    def replies(self, login=LOGIN):
        return ((b'Usuario administrador SQL existente (oculto): ', login),
                ('Contraseña SQL (oculta; no se guarda): '.encode(), PASSWORD))

    def test_real_terminal_accepts_credentials_without_echo_and_restores_terminal(self):
        output, report = self.terminal_session(self.prompt_program(), self.replies())
        self.assertEqual({'accepted': True, 'terminal_restored': True, 'echo_enabled': True}, report)
        self.assertNotIn(LOGIN.encode(), output)
        self.assertNotIn(PASSWORD.encode(), output)

    def test_invalid_login_is_rejected_after_private_input_and_restores_terminal(self):
        output, report = self.terminal_session(self.prompt_program(), self.replies('invalid login'))
        self.assertEqual({'issue': 'sql_admin_input_invalid', 'terminal_restored': True, 'echo_enabled': True}, report)
        self.assertNotIn(b'invalid login', output)
        self.assertNotIn(PASSWORD.encode(), output)

    def test_eof_during_private_prompt_fails_closed_and_restores_terminal(self):
        output, report = self.terminal_session(
            self.prompt_program(), ((b'Usuario administrador SQL existente (oculto): ', b'\x04'),))
        self.assertEqual({'issue': 'apply_requires_interactive_tty', 'terminal_restored': True, 'echo_enabled': True}, report)
        self.assertNotIn(LOGIN.encode(), output)
        self.assertNotIn(PASSWORD.encode(), output)

    def test_buffered_read_write_open_reproduces_original_real_terminal_failure(self):
        program = """
import io, json
try:
    with open('/dev/tty', 'r+'):
        report = {'original_open_failed': False}
except io.UnsupportedOperation:
    report = {'original_open_failed': True}
print('PRODIMT_TTY_TEST_RESULT=' + json.dumps(report), flush=True)
"""
        _, report = self.terminal_session(program)
        self.assertEqual({'original_open_failed': True}, report)

    def test_without_controlling_terminal_fails_closed_without_reading_or_echoing_stdin(self):
        program = LOAD + """
try:
    module.Updater('unused-package.zip').prompt_admin()
    report = {'unexpected_success': True}
except module.UpdateError as error:
    report = {'issue': str(error)}
print('PRODIMT_TTY_TEST_RESULT=' + json.dumps(report), flush=True)
"""
        child = subprocess.run([sys.executable, '-c', program], start_new_session=True,
                               input=LOGIN + '\n' + PASSWORD + '\n', capture_output=True,
                               text=True, timeout=5, check=False)
        self.assertEqual(0, child.returncode)
        self.assertEqual({'issue': 'apply_requires_interactive_tty'},
                         json.loads(child.stdout.split(RESULT.decode(), 1)[1]))
        self.assertEqual('', child.stderr)
        self.assertNotIn(LOGIN, child.stdout)
        self.assertNotIn(PASSWORD, child.stdout)


if __name__ == '__main__':
    unittest.main()
