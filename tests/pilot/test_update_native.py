import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import types
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile


SPEC = importlib.util.spec_from_file_location('update_native', Path(__file__).parents[2] / 'scripts/pilot/update-native.py')
update = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(update)
SECRET = 'fictional-administrator-secret-3865417290'


class NativeEnvironmentFileTests(unittest.TestCase):
    def test_plain_quoted_empty_and_comment_values_preserve_literal_content(self):
        values = update.parse_env_file(
            '# Private environment\n'
            'ASPNETCORE_ENVIRONMENT=Production\n'
            'ConnectionStrings__Pedidos="Server=127.0.0.1,14330;Password=literal#value;"\n'
            "SINGLE='literal value'\n"
            'EMPTY=""\n'
        )
        self.assertEqual('Production', values['ASPNETCORE_ENVIRONMENT'])
        self.assertEqual('Server=127.0.0.1,14330;Password=literal#value;', values['ConnectionStrings__Pedidos'])
        self.assertEqual('literal value', values['SINGLE'])
        self.assertEqual('', values['EMPTY'])

    # Expected values follow systemd 255, the Ubuntu 24.04 target version.
    SYSTEMD_CASES = (
        ('bare note\n# comment\\\nKEY=kept\n; comment\n', {'KEY': 'kept'}),
        ('KEY=first\nKEY=last\n', {'KEY': 'last'}),
        ("KEY='first\nsecond'\n", {'KEY': 'first\nsecond'}),
        ('KEY="first\nsecond"\n', {'KEY': 'first\nsecond'}),
        ('KEY=first\\\nsecond\n', {'KEY': 'firstsecond'}),
        ('KEY="first\\\nsecond"\n', {'KEY': 'firstsecond'}),
        ("KEY='first\\\nsecond'\n", {'KEY': 'first\\\nsecond'}),
        ("KEY='one' \"two\" tail\n", {'KEY': 'onetwotail'}),
        ('KEY=one"two" \'three\'\n', {'KEY': 'one"two" \'three\''}),
        ('KEY=  inner  spaces \t\r\n', {'KEY': 'inner  spaces'}),
        ('KEY=\\ leading\\ \n', {'KEY': ' leading '}),
        ('KEY="literal\\q\\$\\`\\\"\\\\end"\n', {'KEY': 'literal\\q$`"\\end'}),
        ('KEY=literal#hash;semicolon\n', {'KEY': 'literal#hash;semicolon'}),
        ('KEY=first\vsecond\fthird\u2028fourth\n', {'KEY': 'first\vsecond\fthird\u2028fourth'}),
        ('KEY=value\rNEXT=other\r\n', {'KEY': 'value', 'NEXT': 'other'}),
        ('KEY=first\\\r\nNEXT=other\n', {'KEY': 'first', 'NEXT': 'other'}),
        ('KEY="first\\\r\nsecond"\n', {'KEY': 'first\\\r\nsecond'}),
        ('KEY="unterminated', {'KEY': 'unterminated'}),
        ("KEY='unterminated", {'KEY': 'unterminated'}),
        ('KEY=value\\', {'KEY': 'value'}),
        ('KEY="value\\', {'KEY': 'value'}),
        ('KEY=\nEMPTY=""\nSINGLE=\'\'\n', {'KEY': '', 'EMPTY': '', 'SINGLE': ''}),
    )

    def test_systemd_multiline_continuations_quotes_and_whitespace(self):
        for index, (text, expected) in enumerate(self.SYSTEMD_CASES):
            with self.subTest(case=index):
                self.assertEqual(expected, update.parse_env_file(text))

    def test_parser_matches_real_systemd_255_when_oracle_is_available(self):
        # Only fictitious fixture bytes enter the real parser. No service runs.
        import ctypes
        library_path = Path('/usr/lib/x86_64-linux-gnu/systemd/libsystemd-shared-255.so')
        if not library_path.is_file():
            self.skipTest('Optional systemd 255 parser oracle is unavailable')
        library = ctypes.CDLL(str(library_path))
        vector_type = ctypes.POINTER(ctypes.c_char_p)
        library.load_env_file.argtypes = (ctypes.c_void_p, ctypes.c_char_p, ctypes.POINTER(vector_type))
        library.load_env_file.restype = ctypes.c_int
        library.strv_free.argtypes = (vector_type,)
        library.strv_free.restype = ctypes.c_void_p
        with tempfile.TemporaryDirectory() as directory:
            fixture = Path(directory) / 'fictional.env'
            for index, (text, expected) in enumerate(self.SYSTEMD_CASES):
                with self.subTest(case=index):
                    fixture.write_bytes(text.encode('utf-8'))
                    entries = vector_type()
                    code = library.load_env_file(None, os.fsencode(fixture), ctypes.byref(entries))
                    self.assertEqual(0, code)
                    try:
                        actual, offset = {}, 0
                        while entries[offset] is not None:
                            name, value = entries[offset].decode('utf-8').split('=', 1)
                            actual[name] = value
                            offset += 1
                        self.assertEqual(expected, actual)
                        self.assertEqual(actual, update.parse_env_file(text))
                    finally:
                        library.strv_free(entries)

    def test_ignored_invalid_names_and_overrides_report_only_fixed_metadata(self):
        diagnostics = {}
        values = update.parse_env_file(
            'export KEY=' + SECRET + '\nINVALID KEY=' + SECRET + '\n'
            '9KEY=' + SECRET + '\nKEY=first\nKEY=last\nplain note\n',
            diagnostics=diagnostics,
        )
        self.assertEqual({'KEY': 'last'}, values)
        self.assertEqual(3, diagnostics['ignored_invalid_names'])
        self.assertEqual(1, diagnostics['overwritten_assignments'])
        self.assertEqual(1, diagnostics['ignored_without_assignment'])
        self.assertNotIn(SECRET, json.dumps(diagnostics))
        self.assertNotIn('KEY', json.dumps(diagnostics))

    def test_invalid_unicode_is_rejected_with_fixed_reason_and_line_without_secret(self):
        for char in ('\0', '\ufeff', '\ud800', '\ufdd0', '\ufffe', '\U0001ffff'):
            with self.subTest(codepoint=ord(char)):
                diagnostics = {}
                with self.assertRaises(update.UpdateError) as error:
                    update.parse_env_file('KEY=' + SECRET + '\nOTHER=' + char, diagnostics=diagnostics)
                self.assertEqual('environment_file_invalid_character', str(error.exception))
                self.assertEqual('invalid_unicode_character', diagnostics['failure_kind'])
                self.assertEqual(2, diagnostics['failure_line'])
                self.assertNotIn(SECRET, json.dumps(diagnostics) + str(error.exception))

    def test_environment_file_is_never_executed_as_shell(self):
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / 'must-not-exist'
            text = 'KEY=$(touch ' + str(marker) + ')\nLITERAL=${KEY}`id`\n'
            values = update.parse_env_file(text)
            self.assertEqual('$(touch ' + str(marker) + ')', values['KEY'])
            self.assertEqual('${KEY}`id`', values['LITERAL'])
            self.assertFalse(marker.exists())


class NativeRestartConfigurationTests(unittest.TestCase):
    def configuration_fixture(self, stack, *, mismatch=False):
        directory = stack.enter_context(tempfile.TemporaryDirectory())
        root = Path(directory)
        old = root / 'backend/releases/previous'
        old.mkdir(parents=True)
        (root / 'backend/current').mkdir()
        env_path = root / 'api.env'
        jwt = SECRET + '\nfictional signing material'
        connection = 'Server=127.0.0.1,14330;Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password=' + SECRET
        env_text = (
            'ASPNETCORE_ENVIRONMENT=Production\nASPNETCORE_URLS=http://127.0.0.1:5080\n'
            'Authentication__Jwt__SigningKey=shadowed\n'
            "Authentication__Jwt__SigningKey='" + jwt + "'\n"
            'ConnectionStrings__Pedidos="Server=127.0.0.1,14330;\\\n'
            'Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password=' + SECRET + '"\n'
        )
        running = {'ASPNETCORE_ENVIRONMENT': 'Production', 'ASPNETCORE_URLS': 'http://127.0.0.1:5080',
                   'Authentication__Jwt__SigningKey': jwt if not mismatch else 'different-' + jwt,
                   'ConnectionStrings__Pedidos': connection}
        spec = importlib.util.spec_from_file_location('restart_fixture_inspector', Path(__file__).parents[2] / 'scripts/pilot/inspect-native.py')
        inspector = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inspector)
        argv = ['/usr/bin/dotnet', str(root / 'backend/current/Prodimt.Pedidos.Api.dll')]
        metadata = {'MainPID': '101', 'ActiveState': 'active', 'User': 'www-data', 'Group': '',
                    'WorkingDirectory': str(root / 'backend/current'), 'EnvironmentFiles': str(env_path) + ' (ignore_errors=no)',
                    'KillMode': 'control-group', 'ControlGroup': '/system.slice/' + update.SERVICE,
                    'ExecStart': '{ argv[]=' + ' '.join(argv) + ' ; ignore_errors=no ; }'}
        original_lstat, original_resolve = Path.lstat, Path.resolve
        def lstat(path, *args, **kwargs):
            if path == env_path:
                return types.SimpleNamespace(st_mode=stat.S_IFREG | 0o640, st_nlink=1, st_uid=0, st_gid=33)
            return original_lstat(path, *args, **kwargs)
        def resolve(path, *args, **kwargs):
            return old if path == Path('/proc/101/cwd') else original_resolve(path, *args, **kwargs)
        for name, value in (('ROOT', root), ('OLD_BACKEND', old), ('ENV_FILE', env_path)):
            stack.enter_context(patch.object(update, name, value))
        stack.enter_context(patch.object(Path, 'lstat', lstat))
        stack.enter_context(patch.object(Path, 'resolve', resolve))
        stack.enter_context(patch.object(update, 'read_file', return_value=env_text.encode()))
        stack.enter_context(patch.object(inspector, 'process_identity', return_value='fictional-identity'))
        stack.enter_context(patch.object(inspector, 'process_environment', return_value=running))
        stack.enter_context(patch.object(inspector, 'read_limited', return_value='\0'.join(argv)))
        stack.enter_context(patch.object(inspector, 'assert_same_process'))
        updater = update.Updater('unused-package.zip')
        updater.inspector = inspector
        updater.payload = {'files': {'backend/appsettings.json': b'{}', 'backend/appsettings.Production.json': b'{}'}}
        stack.enter_context(patch.object(updater, 'systemd_values', return_value=metadata))
        return updater

    def test_multiline_and_last_assignment_reach_real_configuration_and_candidate_guards(self):
        with contextlib.ExitStack() as stack:
            updater = self.configuration_fixture(stack)
            updater.inspect_restart_configuration()
            self.assertEqual(SECRET, updater.app_credentials.password)
            self.assertEqual(1, updater.state['environment_file_syntax']['overwritten_assignments'])
            self.assertNotIn(SECRET, json.dumps(updater.state))
            self.assertEqual('Production', updater.snapshot['source_environment'])

    def test_restart_configuration_mismatch_blocks_before_prompt_or_mutation(self):
        with contextlib.ExitStack() as stack:
            updater = self.configuration_fixture(stack, mismatch=True)
            stack.enter_context(patch.object(update, 'own_lock', contextlib.nullcontext))
            stack.enter_context(patch.object(update, 'interruption_guard', contextlib.nullcontext))
            stack.enter_context(patch.object(updater, 'readonly_preflight', updater.inspect_restart_configuration))
            forbidden = [stack.enter_context(patch.object(updater, name)) for name in ('prompt_admin', 'stage', 'stop', 'migrate', 'switch_links')]
            report = updater.run(apply=True)
            self.assertEqual('environment_file_differs_from_running_configuration', report['issue'])
            self.assertFalse(report['changes_made'])
            self.assertFalse(report['migrations_may_have_changed'])
            self.assertNotIn(SECRET, json.dumps(report))
            for action in forbidden:
                action.assert_not_called()


class NativePackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def package(self, *, extra_files=None, extras=None, checksum_overrides=None, omit_checksums=(), manifest_changes=None):
        sql = b'SELECT 1;\n'
        manifest = {'application_source_commit': update.SOURCE_COMMIT,
                    'application_build': {'migrations': {'migrationIds': list(update.MIGRATIONS),
                                                         'sha256': hashlib.sha256(sql).hexdigest()}}}
        if manifest_changes:
            manifest.update(manifest_changes)
        files = {
            'inspect-native.py': b'raise RuntimeError("verification must not execute inspector")\n',
            'backend/Prodimt.Pedidos.Api.dll': b'fictional binary',
            'backend/Prodimt.Pedidos.Api.runtimeconfig.json': b'{"runtimeOptions":{"tfm":"net10.0"}}',
            'backend/appsettings.json': b'{}',
            'backend/appsettings.Production.json': b'{}',
            'frontend/index.html': b'<html>test</html>',
            'migrations-idempotent.sql': sql,
            'MANIFIESTO.json': json.dumps(manifest).encode(),
        }
        files.update(extra_files or {})
        checksums = {name: hashlib.sha256(data).hexdigest() for name, data in files.items() if name not in omit_checksums}
        checksums.update(checksum_overrides or {})
        files['SHA256SUMS.txt'] = ''.join(digest + '  ' + name + '\n' for name, digest in sorted(checksums.items())).encode()
        destination = self.root / 'fixture.zip'
        with zipfile.ZipFile(destination, 'w') as archive:
            for name, data in files.items():
                archive.writestr(update.PACKAGE_ROOT + name, data)
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                for entry, data in extras or ():
                    archive.writestr(entry, data)
        return destination

    def verify_fixture(self, path):
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        with patch.object(update, 'EXPECTED_PACKAGE_SHA', digest):
            return update.verify_package(path)

    def test_valid_fixture_is_verified_in_memory_without_extraction_or_execution(self):
        package = self.package()
        before = sorted(p.name for p in self.root.iterdir())
        result = self.verify_fixture(package)
        self.assertEqual(before, sorted(p.name for p in self.root.iterdir()))
        self.assertEqual(b'fictional binary', result['files']['backend/Prodimt.Pedidos.Api.dll'])
        self.assertEqual(hashlib.sha256(package.read_bytes()).hexdigest(), result['sha256'])

    def test_unapproved_package_hash_is_rejected_before_zip_contents(self):
        package = self.package()
        with patch.object(update, 'EXPECTED_PACKAGE_SHA', '0' * 64):
            with self.assertRaisesRegex(update.UpdateError, 'package_sha_mismatch'):
                update.verify_package(package)

    def test_traversal_foreign_roots_duplicate_entries_and_symlinks_are_rejected(self):
        link = zipfile.ZipInfo(update.PACKAGE_ROOT + 'backend/link')
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        cases = (
            [(update.PACKAGE_ROOT + '../outside', b'outside')],
            [('different-root/outside', b'outside')],
            [(update.PACKAGE_ROOT + 'backend/Prodimt.Pedidos.Api.dll', b'duplicate')],
            [(link, b'/etc/passwd')],
        )
        for entries in cases:
            with self.subTest(entry=str(entries[0][0])):
                package = self.package(extras=entries)
                with self.assertRaises(update.UpdateError):
                    self.verify_fixture(package)
                self.assertFalse((self.root / 'outside').exists())

    def test_checksum_coverage_and_payload_hashes_are_required(self):
        for options in (
            {'omit_checksums': ('frontend/index.html',)},
            {'checksum_overrides': {'frontend/index.html': '0' * 64}},
            {'extras': [(update.PACKAGE_ROOT + 'unlisted.txt', b'unlisted')]},
        ):
            with self.subTest(options=options):
                with self.assertRaises(update.UpdateError):
                    self.verify_fixture(self.package(**options))

    def test_wrong_source_or_private_payload_is_rejected(self):
        for options in (
            {'manifest_changes': {'application_source_commit': 'different-commit'}},
            {'extra_files': {'credentials.env': SECRET.encode()}},
            {'extra_files': {'backend/appsettings.Development.json': b'{}'}},
        ):
            with self.subTest(options_kind=next(iter(options))):
                with self.assertRaises(update.UpdateError) as error:
                    self.verify_fixture(self.package(**options))
                self.assertNotIn(SECRET, str(error.exception))


class NativeLifecycleTests(unittest.TestCase):
    methods = ('readonly_preflight', 'prompt_admin', 'preflight_admin', 'stage', 'save_private_backup', 'adopt_structure',
               'unchanged_before_stop', 'stop', 'backup_database', 'migrate', 'verify_history',
               'switch_links', 'start', 'verify_health', 'restore_links', 'verify_previous_health')

    def execute(self, *, apply=True, failures=None, watch_lock=False):
        updater = update.Updater('/not-read/mock-package.zip')
        credentials = update.Credentials('fictional_admin', SECRET)
        app_credentials = update.Credentials(update.APP_LOGIN, 'fictional-app-password')
        events, calls = [], {}
        locked = [False]
        failures = failures or {}

        @contextlib.contextmanager
        def watched_lock():
            locked[0] = True
            try:
                yield
            finally:
                locked[0] = False

        def action(name):
            def perform(*args, **kwargs):
                if watch_lock and apply:
                    self.assertTrue(locked[0], 'Apply and rollback must share the same lock')
                events.append(name)
                calls[name] = calls.get(name, 0) + 1
                error = failures.get((name, calls[name]))
                if error is not None:
                    raise error
                if name == 'readonly_preflight':
                    updater.app_credentials = app_credentials
                if name == 'prompt_admin':
                    return credentials
                return None
            return perform

        output = io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(update, 'own_lock', side_effect=watched_lock if watch_lock else contextlib.nullcontext))
            stack.enter_context(patch.object(update, 'interruption_guard', return_value=contextlib.nullcontext()))
            for method in self.methods:
                stack.enter_context(patch.object(updater, method, side_effect=action(method)))
            stack.enter_context(contextlib.redirect_stdout(output))
            report = updater.run(apply=apply)
        self.assertFalse(locked[0])
        self.assertNotIn(SECRET, json.dumps(report) + output.getvalue())
        if 'readonly_preflight' in events and not failures.get(('readonly_preflight', 1)):
            self.assertEqual('', app_credentials.password)
        if 'prompt_admin' in events and not failures.get(('prompt_admin', 1)):
            self.assertEqual('', credentials.password)
        return report, events

    def test_plan_default_reads_without_credentials_or_mutations(self):
        report, events = self.execute(apply=False)
        self.assertEqual(['readonly_preflight'], events)
        self.assertEqual('ready', report['status'])
        self.assertEqual('20261025-sql4-protocol', report['updater_revision'])
        self.assertFalse(report['changes_made'])
        self.assertFalse(report['migrations_may_have_changed'])

    def test_successful_apply_has_required_order_and_no_rollback(self):
        report, events = self.execute()
        self.assertEqual(['readonly_preflight', 'prompt_admin', 'preflight_admin', 'save_private_backup',
                          'adopt_structure', 'stage', 'unchanged_before_stop', 'stop', 'backup_database',
                          'migrate', 'verify_history', 'switch_links', 'start', 'verify_health'], events)
        self.assertEqual('updated', report['status'])
        self.assertTrue(report['migrations_may_have_changed'])
        self.assertFalse(report['database_automatically_restored'])

    def test_guards_and_bad_admin_abort_before_stop_or_staging(self):
        for step in ('readonly_preflight', 'prompt_admin', 'preflight_admin'):
            with self.subTest(step=step):
                report, events = self.execute(failures={(step, 1): update.UpdateError('guard_failed')})
                self.assertEqual('failed', report['status'])
                self.assertFalse(report['changes_made'])
                self.assertNotIn('stage', events)
                self.assertNotIn('stop', events)
                self.assertNotIn('backup_database', events)

    def test_stale_configuration_after_staging_never_stops_api(self):
        report, events = self.execute(failures={('unchanged_before_stop', 1): update.UpdateError('configuration_changed')})
        self.assertEqual('failed', report['status'])
        self.assertNotIn('stop', events)
        self.assertNotIn('migrate', events)
        self.assertFalse(report['migrations_may_have_changed'])

    def test_backup_failure_recovers_previous_service_without_migration(self):
        report, events = self.execute(failures={('backup_database', 1): update.UpdateError('backup_failed')})
        self.assertNotIn('migrate', events)
        self.assertEqual(['stop', 'restore_links', 'start', 'verify_previous_health'], events[-4:])
        self.assertTrue(report['previous_release_start_requested'])
        self.assertFalse(report['migrations_may_have_changed'])
        self.assertFalse(report['database_automatically_restored'])

    def test_a_failed_stop_still_attempts_recovery(self):
        report, events = self.execute(failures={('stop', 1): update.UpdateError('stop_failed')})
        self.assertEqual(['stop', 'stop', 'restore_links', 'start', 'verify_previous_health'], events[-5:])
        self.assertNotIn('migrate', events)
        self.assertTrue(report['previous_release_start_requested'])

    def test_migration_failure_is_marked_partial_before_the_call_returns(self):
        report, events = self.execute(failures={('migrate', 1): update.UpdateError('migration_failed')})
        self.assertTrue(report['migrations_may_have_changed'])
        self.assertFalse(report['database_automatically_restored'])
        self.assertNotIn('verify_history', events)
        self.assertNotIn('switch_links', events)
        self.assertEqual(['stop', 'restore_links', 'start', 'verify_previous_health'], events[-4:])

    def test_history_or_partial_switch_failure_restores_previous_links(self):
        for step in ('verify_history', 'switch_links'):
            with self.subTest(step=step):
                report, events = self.execute(failures={(step, 1): update.UpdateError('activation_failed')})
                self.assertTrue(report['migrations_may_have_changed'])
                self.assertEqual(['stop', 'restore_links', 'start', 'verify_previous_health'], events[-4:])
                self.assertTrue(report['previous_release_start_requested'])

    def test_health_failure_stops_new_process_before_reverting_and_starting_old(self):
        report, events = self.execute(failures={('verify_health', 1): update.UpdateError('health_failed')})
        self.assertEqual(['start', 'verify_health', 'stop', 'restore_links', 'start', 'verify_previous_health'], events[-6:])
        self.assertTrue(report['previous_release_start_requested'])
        self.assertFalse(report['database_automatically_restored'])

    def test_rollback_link_failure_leaves_service_stopped_for_manual_recovery(self):
        report, events = self.execute(failures={('verify_health', 1): update.UpdateError('health_failed'),
                                               ('restore_links', 1): RuntimeError(SECRET)})
        self.assertEqual(['restore_links_failed'], report['rollback_errors'])
        self.assertFalse(report['previous_release_start_requested'])
        self.assertTrue(report['manual_recovery_required'])
        self.assertEqual('restore_links', events[-1])
        self.assertEqual(1, events.count('start'))
        self.assertNotIn('verify_previous_health', events)

    def test_rollback_stop_failure_never_switches_links_or_starts_again(self):
        report, events = self.execute(failures={('verify_health', 1): update.UpdateError('health_failed'),
                                               ('stop', 2): RuntimeError(SECRET)})
        self.assertEqual(['stop_failed'], report['rollback_errors'])
        self.assertFalse(report['previous_release_start_requested'])
        self.assertTrue(report['manual_recovery_required'])
        self.assertNotIn('restore_links', events)
        self.assertEqual(1, events.count('start'))

    def test_rollback_start_or_health_failure_is_reported_without_claiming_success(self):
        cases = ((('start', 2), 'start_previous_failed', False),
                 (('verify_previous_health', 1), 'previous_health_failed', True))
        for failed_call, expected_error, started in cases:
            with self.subTest(step=failed_call):
                report, events = self.execute(failures={('verify_health', 1): update.UpdateError('health_failed'),
                                                       failed_call: RuntimeError(SECRET)})
                self.assertEqual([expected_error], report['rollback_errors'])
                self.assertEqual(started, report['previous_release_start_requested'])
                self.assertTrue(report['manual_recovery_required'])
                self.assertFalse(report.get('previous_application_health_verified', False))

    def test_rollback_holds_the_original_update_lock_until_recovery_finishes(self):
        report, events = self.execute(watch_lock=True,
                                     failures={('verify_health', 1): update.UpdateError('health_failed')})
        self.assertEqual([], report['rollback_errors'])
        self.assertEqual('verify_previous_health', events[-1])

    def test_interrupt_during_migration_runs_recovery_with_partial_state(self):
        report, events = self.execute(failures={('migrate', 1): KeyboardInterrupt()})
        self.assertTrue(report['migrations_may_have_changed'])
        self.assertFalse(report['database_automatically_restored'])
        self.assertEqual(['stop', 'restore_links', 'start', 'verify_previous_health'], events[-4:])

    def test_unexpected_exception_does_not_reveal_private_details(self):
        report, events = self.execute(failures={('preflight_admin', 1): RuntimeError(SECRET)})
        self.assertEqual('update_step_failed', report['issue'])
        self.assertNotIn('stop', events)


class NativeSqlAndFilesystemTests(unittest.TestCase):
    def test_prompt_requires_private_tty_and_rejects_application_login(self):
        updater = update.Updater('/not-read/mock-package.zip')
        with patch('builtins.open', side_effect=OSError(SECRET)), patch.object(update.getpass, 'getpass') as prompt:
            with self.assertRaisesRegex(update.UpdateError, 'interactive_tty'):
                updater.prompt_admin()
            prompt.assert_not_called()
        tty = io.StringIO()
        tty.isatty = lambda: True
        with patch('builtins.open', return_value=tty), \
                patch.object(update.getpass, 'getpass', side_effect=[update.APP_LOGIN, SECRET]):
            with self.assertRaisesRegex(update.UpdateError, 'sql_admin_input_invalid'):
                updater.prompt_admin()

    def test_signal_guard_uses_interrupt_handler_then_ignores_during_rollback(self):
        signals = (update.signal.SIGINT, update.signal.SIGTERM, update.signal.SIGHUP)
        previous = object()
        for ignore, expected in ((False, update.interrupt_update), (True, update.signal.SIG_IGN)):
            with patch.object(update.signal, 'getsignal', return_value=previous), \
                    patch.object(update.signal, 'signal') as setter:
                with update.interruption_guard(ignore=ignore):
                    self.assertEqual([(number, expected) for number in signals],
                                     [call.args for call in setter.call_args_list])
                self.assertEqual([(number, previous) for number in signals],
                                 [call.args for call in setter.call_args_list][-3:])
        with self.assertRaisesRegex(update.UpdateError, 'update_interrupted'):
            update.interrupt_update(update.signal.SIGTERM, None)

    def test_structure_adoption_rejects_out_of_scope_or_changed_snapshot_before_chown(self):
        updater = update.Updater('/not-read/mock-package.zip')
        with patch.object(update.os, 'fchown') as chown:
            updater.structure = [{'path': '/unrelated/application'}]
            with self.assertRaisesRegex(update.UpdateError, 'structure_path_outside_scope'):
                updater.adopt_structure()
            entry = {'path': str(update.ROOT), 'device': 1, 'inode': 10, 'uid': 33, 'gid': 33, 'mode': 0o755}
            updater.structure = [entry]
            with patch.object(update, 'structure_metadata', return_value=dict(entry, inode=11)):
                with self.assertRaisesRegex(update.UpdateError, 'structure_changed_since_preflight'):
                    updater.adopt_structure()
            chown.assert_not_called()

    def test_admin_needs_backup_and_verification_capability_on_expected_database(self):
        updater = update.Updater('/not-read/mock-package.zip')
        credentials = update.Credentials('fictional_admin', SECRET)
        valid = {'database_name': update.DATABASE, 'orders_count': 0, 'sysadmin': 0,
                 'db_owner': 1, 'backup_permission': 1, 'verify_permission': 1, 'dbcreator': 0}
        with patch.object(updater, 'sql_json', return_value=valid), patch.object(updater, 'sql_run') as writer:
            updater.preflight_admin(credentials)
            writer.assert_not_called()
        for changed in ({'backup_permission': 0}, {'verify_permission': 0}, {'db_owner': 0},
                        {'database_name': 'OtherDatabase'}, {'orders_count': 1}):
            with self.subTest(changed=changed), patch.object(updater, 'sql_json', return_value=dict(valid, **changed)):
                with self.assertRaises(update.UpdateError):
                    updater.preflight_admin(credentials)

    def test_new_table_grants_are_only_missing_dml_on_two_named_objects(self):
        updater = update.Updater('/not-read/mock-package.zip')
        updater.inspector = types.SimpleNamespace(SQL_MIGRATIONS='READ_HISTORY', SQL_METADATA='READ_METADATA')
        updater.app_credentials = update.Credentials(update.APP_LOGIN, 'fictional-app-password')
        credentials = update.Credentials('fictional_admin', SECRET)
        complete = {prefix + '_' + verb: 1 for prefix in ('operations', 'notices')
                    for verb in ('select', 'insert', 'update', 'delete')}
        missing = dict(complete, operations_insert=0, notices_select=0)
        responses = [[{'MigrationId': name} for name in update.MIGRATIONS],
                     {'database_name': update.DATABASE, 'order_sequence_index_unique': 1}, missing, complete]
        with patch.object(updater, 'sql_json', side_effect=responses), patch.object(updater, 'sql_run') as writer:
            updater.verify_history(credentials)
        statements = [call.kwargs['query'] for call in writer.call_args_list]
        self.assertEqual([
            'GRANT INSERT ON OBJECT::[dbo].[OrderOperations] TO [prodimt_pedidos_app];',
            'GRANT SELECT ON OBJECT::[dbo].[OrderChangeNotices] TO [prodimt_pedidos_app];',
        ], statements)
        self.assertFalse(any(word in ' '.join(statements).upper() for word in ('ALTER ROLE', 'DB_OWNER', 'CREATE TABLE')))

    def test_private_backup_preserves_original_env_and_uses_restrictive_modes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            private_root = root / 'backups'
            private_root.mkdir(mode=0o700)
            original = root / 'api.env'
            original.write_text('PRIVATE_VALUE=' + SECRET + '\n')
            original.chmod(0o640)
            original_bytes = original.read_bytes()
            with patch.object(update, 'PRIVATE_ROOT', private_root), \
                    patch.object(update, 'plain_owned_directory'), patch.object(update.os, 'chown'):
                updater = update.Updater('/not-read/mock-package.zip')
                updater.env_bytes = original_bytes
                updater.unit_values = {'Environment': 'PRIVATE_VALUE=' + SECRET}
                updater.old_links = {'backend': 'old-backend', 'frontend': 'old-frontend'}
                updater.payload = {'files': {'migrations-idempotent.sql': b'SELECT 1;'}}
                updater.save_private_backup()
            self.assertEqual(original_bytes, original.read_bytes())
            self.assertEqual(0o640, original.stat().st_mode & 0o777)
            self.assertEqual(0o700, updater.private_dir.stat().st_mode & 0o777)
            for saved in updater.private_dir.iterdir():
                self.assertEqual(0o600, saved.stat().st_mode & 0o777)
            self.assertEqual(original_bytes, (updater.private_dir / 'api.env').read_bytes())
            self.assertNotIn(SECRET, json.dumps(updater.state))

    def test_staging_applies_reviewed_config_override_and_readable_modes_only_to_new_releases(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(update, 'ROOT', Path(directory)), \
                patch.object(update.os, 'chown'):
            updater = update.Updater('/not-read/mock-package.zip')
            for part in ('backend', 'frontend'):
                updater.new[part].parent.mkdir(parents=True)
            old_file = updater.new['backend'].parent / 'old-private-marker'
            old_file.write_text('preserved')
            old_file.chmod(0o600)
            packaged = b'{"Authentication":{"Jwt":{"AccessTokenMinutes":120}}}'
            preserved = b'{"Authentication":{"Jwt":{"AccessTokenMinutes":720}}}'
            updater.payload = {'files': {'backend/appsettings.Production.json': packaged,
                                         'backend/nested/resources.dll': b'fake binary',
                                         'frontend/index.html': b'<html>verified frontend</html>'}}
            updater.config_overrides = {'backend/appsettings.Production.json': preserved}
            previous_mask = os.umask(0o077)
            try:
                updater.stage()
            finally:
                os.umask(previous_mask)
            self.assertEqual(preserved, (updater.new['backend'] / 'appsettings.Production.json').read_bytes())
            self.assertEqual(packaged, updater.payload['files']['backend/appsettings.Production.json'])
            for release in updater.new.values():
                self.assertEqual(0o755, release.stat().st_mode & 0o777)
                for path in release.rglob('*'):
                    self.assertEqual(0o755 if path.is_dir() else 0o644, path.stat().st_mode & 0o777)
            self.assertEqual('preserved', old_file.read_text())
            self.assertEqual(0o600, old_file.stat().st_mode & 0o777)

    def test_sql_admin_credentials_only_reach_clean_environment(self):
        updater = update.Updater('/not-read/mock-package.zip')
        updater.inspector = types.SimpleNamespace(SQL_TOOLS=('/opt/mssql-tools/bin/sqlcmd',))
        updater.sql_tool = updater.inspector.SQL_TOOLS[0]
        credentials = update.Credentials('fictional_admin', SECRET)
        with patch.dict(os.environ, {'SQLCMDINI': '/private/unsafe-init.sql', 'SQLCMDSERVER': 'outside.example',
                                    'OTHER_SECRET': SECRET}), patch.object(update, 'command', return_value=(0, '{}')) as command:
            updater.sql_run(credentials, query='SELECT 1;')
        args = command.call_args.args[0]
        env = command.call_args.kwargs['env']
        self.assertNotIn(SECRET, repr(args))
        self.assertNotIn(credentials.login, args)
        self.assertNotIn('-P', args)
        self.assertNotIn('-U', args)
        self.assertNotIn('-h', args)
        self.assertNotIn('-W', args)
        self.assertIn('-x', args)
        self.assertIn('-I', args)
        self.assertEqual(update.SERVER, args[args.index('-S') + 1])
        self.assertEqual(update.DATABASE, args[args.index('-d') + 1])
        self.assertEqual(set(update.CLEAN_ENV) | {'SQLCMDUSER', 'SQLCMDPASSWORD'}, set(env))
        self.assertEqual(SECRET, env['SQLCMDPASSWORD'])
        self.assertEqual(credentials.login, env['SQLCMDUSER'])
        self.assertNotIn(SECRET, repr(credentials))

    def test_sql_failures_do_not_expose_command_output(self):
        updater = update.Updater('/not-read/mock-package.zip')
        updater.inspector = types.SimpleNamespace(SQL_TOOLS=('/opt/mssql-tools/bin/sqlcmd',))
        updater.sql_tool = updater.inspector.SQL_TOOLS[0]
        with patch.object(update, 'command', return_value=(1, SECRET)):
            with self.assertRaises(update.UpdateError) as error:
                updater.sql_run(update.Credentials('fictional_admin', SECRET), query='SELECT 1;')
        self.assertNotIn(SECRET, str(error.exception))

    def test_sql_failure_retains_only_numeric_headers_without_message_or_credentials(self):
        updater = update.Updater('/not-read/mock-package.zip')
        updater.inspector = types.SimpleNamespace(SQL_TOOLS=('/opt/mssql-tools/bin/sqlcmd',))
        updater.sql_tool = updater.inspector.SQL_TOOLS[0]
        raw = ('Msg 1934, Level 16, State 1, Server private-host, Line 22\n'
               + SECRET + '\nMsg 1934, Level 16, State 1, Server private-host, Line 23\n')
        with patch.object(update, 'command', return_value=(1, raw)):
            with self.assertRaisesRegex(update.UpdateError, '^sql_operation_failed$'):
                updater.sql_run(update.Credentials('fictional_admin', SECRET), script='SELECT 1;')
        self.assertEqual({'exit_code': 1, 'error_numbers': [1934]}, updater.state['sql_failure'])
        self.assertNotIn(SECRET, json.dumps(updater.state))
        self.assertNotIn('private-host', json.dumps(updater.state))

    def test_partial_link_switch_can_restore_both_original_targets(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(update, 'ROOT', Path(directory)):
            updater = update.Updater('/not-read/mock-package.zip')
            for part in ('backend', 'frontend'):
                old = Path(directory) / part / 'releases' / 'old'
                old.mkdir(parents=True)
                updater.old[part] = old
                updater.new[part].mkdir()
                current = Path(directory) / part / 'current'
                current.symlink_to(old)
                updater.old_links[part] = str(old)
            original_replace = update.os.replace
            def replace(source, destination):
                if Path(destination).parent.name == 'frontend':
                    raise OSError('simulated atomic replace failure')
                return original_replace(source, destination)
            with patch.object(update.os, 'replace', side_effect=replace):
                with self.assertRaises(OSError):
                    updater.switch_links()
            self.assertEqual(updater.new['backend'], (Path(directory) / 'backend/current').resolve())
            self.assertEqual(updater.old['frontend'], (Path(directory) / 'frontend/current').resolve())
            updater.restore_links()
            for part in ('backend', 'frontend'):
                self.assertEqual(updater.old[part], (Path(directory) / part / 'current').resolve())
                self.assertFalse(list((Path(directory) / part).glob('.prodimt-i04-link-*')))

    def test_restore_attempts_other_link_even_when_one_current_link_has_changed(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(update, 'ROOT', Path(directory)):
            updater = update.Updater('/not-read/mock-package.zip')
            for part in ('backend', 'frontend'):
                old = Path(directory) / part / 'releases' / 'old'
                old.mkdir(parents=True)
                updater.old[part] = old
                updater.new[part].mkdir()
                (Path(directory) / part / 'current').symlink_to(updater.new[part])
                updater.old_links[part] = str(old)
            foreign = Path(directory) / 'unrelated'
            foreign.mkdir()
            backend = Path(directory) / 'backend/current'
            backend.unlink()
            backend.symlink_to(foreign)
            with self.assertRaisesRegex(update.UpdateError, 'previous_links_not_fully_restored'):
                updater.restore_links()
            self.assertEqual(foreign, backend.resolve())
            self.assertEqual(updater.old['frontend'], (Path(directory) / 'frontend/current').resolve())

    def test_foreign_current_link_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(update, 'ROOT', Path(directory)):
            updater = update.Updater('/not-read/mock-package.zip')
            foreign = Path(directory) / 'unrelated'
            foreign.mkdir()
            current = Path(directory) / 'backend/current'
            current.parent.mkdir()
            current.symlink_to(foreign)
            with self.assertRaisesRegex(update.UpdateError, 'current_link_conflict'):
                updater.replace_link('backend', str(updater.new['backend']))
            self.assertEqual(foreign, current.resolve())


class NativeHealthVerificationTests(unittest.TestCase):
    def setUp(self):
        self.updater = update.Updater('/not-read/mock-package.zip')
        self.index = b'<html><head><link rel="stylesheet" href="styles.css"></head><body><script src="main.js"></script></body></html>'
        self.updater.payload = {'files': {
            'frontend/index.html': self.index,
            'frontend/main.js': b'console.log("verified production asset");',
            'frontend/styles.css': b'body{color:#123456}',
        }}
        self.responses = {
            ('/health/db', False): (200, '{"database":"reachable"}'),
            ('/health/db', True): (200, '{"database":"reachable"}'),
            ('/', True): (200, self.index.decode()),
            ('/main.js', True): (200, self.updater.payload['files']['frontend/main.js'].decode()),
            ('/styles.css', True): (200, self.updater.payload['files']['frontend/styles.css'].decode()),
        }
        for path in ('/api/admin/operations/changes', '/api/admin/operations/plan'):
            for https in (False, True):
                self.responses[(path, https)] = (401, '')
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.http = self.stack.enter_context(patch.object(
            self.updater, 'http_get', side_effect=lambda path, https=False: self.responses[(path, https)]))
        self.process = self.stack.enter_context(patch.object(self.updater, 'verify_process_release'))
        self.sleep = self.stack.enter_context(patch.object(update.time, 'sleep'))

    def test_success_requires_new_process_protected_api_and_exact_https_frontend_assets(self):
        self.updater.verify_health()
        self.assertTrue(self.updater.state['health_verified'])
        self.assertFalse(self.updater.state['health_used_https_proxy'])
        self.process.assert_called_once_with(self.updater.new['backend'])
        for path in ('/', '/main.js', '/styles.css', '/api/admin/operations/changes', '/api/admin/operations/plan'):
            self.http.assert_any_call(path, https=True)
        for path in ('/api/admin/operations/changes', '/api/admin/operations/plan'):
            self.http.assert_any_call(path, https=False)
        self.sleep.assert_not_called()

    def test_local_redirect_still_requires_valid_https_database_and_assets(self):
        self.responses[('/health/db', False)] = (307, '')
        self.updater.verify_health()
        self.assertTrue(self.updater.state['health_verified'])
        self.assertTrue(self.updater.state['health_used_https_proxy'])
        self.http.assert_any_call('/health/db', https=True)
        self.http.assert_any_call('/main.js', https=True)
        self.http.assert_any_call('/styles.css', https=True)
        self.process.assert_called_once_with(self.updater.new['backend'])

    def test_previous_process_cannot_pass_even_when_database_responds(self):
        self.process.side_effect = update.UpdateError('application_running_release_mismatch')
        with self.assertRaisesRegex(update.UpdateError, 'application_running_release_mismatch'):
            self.updater.verify_health()
        self.process.assert_called_once_with(self.updater.new['backend'])
        self.assertFalse(self.updater.state.get('health_verified', False))
        self.assertNotIn((('/',), {'https': True}), [(call.args, call.kwargs) for call in self.http.call_args_list])

    def test_different_https_index_or_assets_cannot_pass(self):
        for path, expected_issue in (('/', 'https_frontend_index_mismatch'),
                                     ('/main.js', 'https_frontend_asset_mismatch'),
                                     ('/styles.css', 'https_frontend_asset_mismatch')):
            with self.subTest(path=path):
                original = self.responses[(path, True)]
                self.responses[(path, True)] = (200, 'different deployment content')
                try:
                    with self.assertRaisesRegex(update.UpdateError, expected_issue):
                        self.updater.verify_health()
                    self.assertFalse(self.updater.state.get('health_verified', False))
                finally:
                    self.responses[(path, True)] = original

    def test_missing_401_on_local_or_https_new_endpoints_cannot_pass(self):
        for path in ('/api/admin/operations/changes', '/api/admin/operations/plan'):
            for https in (False, True):
                with self.subTest(path=path, https=https):
                    original = self.responses[(path, https)]
                    self.responses[(path, https)] = (404, '')
                    try:
                        with self.assertRaisesRegex(update.UpdateError, 'endpoints_not_ready'):
                            self.updater.verify_health()
                        self.assertFalse(self.updater.state.get('health_verified', False))
                    finally:
                        self.responses[(path, https)] = original

    def test_unreachable_database_exhausts_bounded_checks_without_claiming_health(self):
        self.responses[('/health/db', False)] = (503, '{"database":"unreachable"}')
        with self.assertRaisesRegex(update.UpdateError, 'updated_application_health_failed'):
            self.updater.verify_health()
        self.assertEqual(20, self.http.call_count)
        self.assertEqual(19, self.sleep.call_count)
        self.process.assert_not_called()
        self.assertFalse(self.updater.state.get('health_verified', False))


if __name__ == '__main__':
    unittest.main()
