import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import types
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location('resume_native_fixture', Path(__file__).parents[2] / 'scripts/pilot/update-native.py')
update = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(update)
SECRET = 'fictional-original-private-value-772319'


class NativeResumeTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        temporary = self.stack.enter_context(tempfile.TemporaryDirectory())
        self.base = Path(temporary)
        root, private, sql = self.base / 'app', self.base / 'private', self.base / 'sql'
        source = private / 'reviewed-attempt'
        backup = sql / 'reviewed-backup' / 'ProdimtPedidos-copy-only.bak'
        for name, value in (('ROOT', root), ('PRIVATE_ROOT', private), ('SQL_BACKUP_ROOT', sql),
                            ('RECOVERY_SOURCE', source), ('RECOVERY_BACKUP', backup),
                            ('OLD_BACKEND', root / 'backend/releases/old-backend'),
                            ('OLD_FRONTEND', root / 'frontend/releases/old-frontend')):
            self.stack.enter_context(patch.object(update, name, value))
        self.updater = update.Updater('unused-package.zip', resume_from=source)
        for path in update.structural_directories():
            path.mkdir(parents=True, exist_ok=True, mode=0o755)
        for part, old in self.updater.old.items():
            old.mkdir(mode=0o755)
            (root / part / 'current').symlink_to(old)
        private.mkdir(mode=0o700)
        source.mkdir(mode=0o700)
        backup.parent.mkdir(parents=True, mode=0o700)
        with backup.open('wb') as stream:
            stream.truncate(4872704)
        backup.chmod(0o600)
        self.updater.env_bytes = ('PRIVATE=' + SECRET + '\n').encode()
        self.updater.old_links = {part: str(old) for part, old in self.updater.old.items()}
        self.updater.unit_values = self.unit('200', 'new-start')
        self.updater.payload = {'files': {
            'migrations-idempotent.sql': b'-- fictional exact migration payload\n',
            'backend/Prodimt.Pedidos.Api.dll': b'fictional-dll',
            'backend/appsettings.Production.json': b'{}',
            'backend/nested/resource.dat': b'fixture-resource',
            'frontend/index.html': b'<script src="main.js"></script>',
            'frontend/main.js': b'fixture-asset',
        }}
        self.previous = {'updater_revision': '20261025-tty3', 'mode': 'apply', 'status': 'failed',
                         'phase': 'database-migrations', 'issue': 'sql_operation_failed',
                         'migrations_may_have_changed': True, 'database_automatically_restored': False,
                         'changes_made': True, 'package_sha256': update.EXPECTED_PACKAGE_SHA,
                         'database': update.DATABASE, 'private_backup_directory': str(source),
                         'verified_database_backup': str(backup),
                         'old_releases': {k: str(v) for k, v in self.updater.old.items()},
                         'new_releases': {k: str(v) for k, v in self.updater.new.items()},
                         'migrations_before': 5, 'migrations_expected': 9, 'env_preserved': True,
                         'structure_adopted': True, 'rollback_errors': [], 'stop_completed': True,
                         'restore_links_completed': True, 'start_previous_completed': True,
                         'previous_application_health_verified': True, 'previous_release_start_requested': True}
        records = {'api.env': self.updater.env_bytes, 'unit-private.json': json.dumps(self.unit('100', 'old-start')).encode(),
                   'old-links.json': json.dumps(self.updater.old_links).encode(), 'structure-before.json': b'[]',
                   'migrations-idempotent.sql': self.updater.payload['files']['migrations-idempotent.sql'],
                   'database-backup.json': json.dumps({'path': str(backup), 'bytes': 4872704,
                                                      'copy_only': True, 'checksum': True, 'verifyonly_completed': True}).encode(),
                   'result-0123abcd.json': json.dumps(self.previous).encode()}
        for name, data in records.items():
            (source / name).write_bytes(data)
            (source / name).chmod(0o600)
        for part, stage in self.updater.new.items():
            stage.mkdir(mode=0o755)
            for name, data in self.updater.payload['files'].items():
                if name.startswith(part + '/'):
                    target = stage / name[len(part) + 1:]
                    target.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                    target.write_bytes(data)
                    target.chmod(0o644)
        # Keep modes, inode/link counts and bytes real. Translate only fixture
        # ownership so these tests also exercise guards on unprivileged CI.
        original_lstat, original_fstat = Path.lstat, os.fstat
        def translated(st, path=None):
            values = {name: getattr(st, name) for name in ('st_mode', 'st_uid', 'st_gid', 'st_size',
                      'st_nlink', 'st_dev', 'st_ino', 'st_mtime_ns')}
            values.update(st_uid=0, st_gid=33 if path in update.structural_directories() else 0)
            if path in (backup, backup.parent):
                values.update(st_uid=999, st_gid=988)
            return types.SimpleNamespace(**values)
        self.stack.enter_context(patch.object(Path, 'lstat', lambda path: translated(original_lstat(path), path)))
        self.stack.enter_context(patch.object(os, 'fstat', lambda fd: translated(original_fstat(fd))))
        self.stack.enter_context(patch.object(os, 'chown'))

    def unit(self, pid, timestamp):
        return {'MainPID': pid, 'ActiveState': 'active', 'Environment': 'PRIVATE=' + SECRET,
                'ExecStart': '{ path=/usr/bin/dotnet ; argv[]=/usr/bin/dotnet '
                '/var/www/prodimt-pedidos/backend/current/Prodimt.Pedidos.Api.dll ; ignore_errors=no ; '
                'start_time=' + timestamp + ' ; pid=' + pid + ' ; code=0 ; status=0 ; }'}

    def source_bytes(self):
        return {p.name: p.read_bytes() for p in update.RECOVERY_SOURCE.iterdir()}

    def test_reviewed_snapshot_and_complete_stage_are_verified_without_modification(self):
        before = self.source_bytes()
        self.updater.verify_resume_source()
        self.updater.verify_staged_releases()
        self.assertEqual(before, self.source_bytes())
        self.assertTrue(self.updater.state['resume_source_verified'])
        self.assertTrue(self.updater.state['staged_releases_verified'])
        self.assertNotIn(SECRET, json.dumps(self.updater.state))

    def test_unit_runtime_changes_are_allowed_but_static_execution_changes_are_rejected(self):
        self.assertEqual(update.unit_restart_signature(self.unit('1', 'first')),
                         update.unit_restart_signature(self.unit('2', 'second')))
        for fragment in ('path=/other/dotnet', 'ignore_errors=yes'):
            with self.subTest(fragment=fragment):
                changed = self.unit('2', 'second')
                original = 'path=/usr/bin/dotnet' if fragment.startswith('path=') else 'ignore_errors=no'
                changed['ExecStart'] = changed['ExecStart'].replace(original, fragment)
                self.assertNotEqual(update.unit_restart_signature(changed), update.unit_restart_signature(self.updater.unit_values))

    def test_unreviewed_source_and_changed_result_are_rejected(self):
        self.updater.resume_from = update.RECOVERY_SOURCE.parent
        with self.assertRaisesRegex(update.UpdateError, 'resume_source_not_reviewed'):
            self.updater.verify_resume_source()
        self.updater.resume_from = update.RECOVERY_SOURCE
        self.previous['previous_application_health_verified'] = False
        (update.RECOVERY_SOURCE / 'result-0123abcd.json').write_text(json.dumps(self.previous))
        with self.assertRaisesRegex(update.UpdateError, 'resume_previous_result_unexpected'):
            self.updater.verify_resume_source()

    def test_private_snapshot_permissions_symlinks_and_hardlinks_are_rejected(self):
        path = update.RECOVERY_SOURCE / 'api.env'
        original = path.read_bytes()
        path.chmod(0o644)
        with self.assertRaisesRegex(update.UpdateError, 'resume_file_metadata_unexpected'):
            self.updater.verify_resume_source()
        path.chmod(0o600)
        outside = self.base / 'outside-private'
        outside.write_bytes(original)
        outside.chmod(0o600)
        path.unlink()
        path.symlink_to(outside)
        with self.assertRaisesRegex(update.UpdateError, 'resume_file_unreadable'):
            self.updater.verify_resume_source()
        path.unlink()
        os.link(outside, path)
        with self.assertRaisesRegex(update.UpdateError, 'resume_file_metadata_unexpected'):
            self.updater.verify_resume_source()

    def test_stage_rejects_changed_bytes_extra_files_missing_files_and_bad_modes(self):
        path = self.updater.new['frontend'] / 'main.js'
        original = path.read_bytes()
        for kind in ('changed', 'extra', 'missing', 'mode'):
            with self.subTest(kind=kind):
                extra = path.parent / 'unexpected.txt'
                if kind == 'changed': path.write_bytes(b'changed')
                elif kind == 'extra': extra.write_text('extra')
                elif kind == 'missing': path.unlink()
                else: path.chmod(0o600)
                with self.assertRaises(update.UpdateError):
                    self.updater.verify_staged_releases()
                if extra.exists(): extra.unlink()
                path.write_bytes(original)
                path.chmod(0o644)

    def test_stage_rejects_symlink_or_shared_inode(self):
        path = self.updater.new['frontend'] / 'main.js'
        outside = self.base / 'outside-staged'
        outside.write_bytes(path.read_bytes())
        outside.chmod(0o644)
        path.unlink()
        path.symlink_to(outside)
        with self.assertRaises(update.UpdateError): self.updater.verify_staged_releases()
        path.unlink()
        os.link(outside, path)
        with self.assertRaises(update.UpdateError): self.updater.verify_staged_releases()

    def test_new_preflight_without_resume_still_rejects_existing_stage(self):
        updater = update.Updater('unused-package.zip')
        with patch.object(update.os, 'geteuid', return_value=0), \
                patch.object(update, 'verify_package', return_value=self.updater.payload), patch.object(updater, 'load_inspector'):
            with self.assertRaisesRegex(update.UpdateError, 'new_release_already_exists'):
                updater.readonly_preflight()

    def test_resume_requires_already_secured_structural_directories(self):
        entry = {'path': str(update.ROOT), 'uid': 33, 'gid': 33, 'mode': 0o755}
        with patch.object(update.os, 'geteuid', return_value=0), \
                patch.object(update, 'verify_package', return_value=self.updater.payload), \
                patch.object(self.updater, 'load_inspector'), patch.object(update, 'structure_metadata', return_value=entry):
            with self.assertRaisesRegex(update.UpdateError, 'resume_structure_no_longer_secured'):
                self.updater.readonly_preflight()

    def test_source_or_backup_changes_after_validation_block_reuse(self):
        self.updater.verify_resume_source()
        (update.RECOVERY_SOURCE / 'structure-before.json').write_bytes(b'[ ]')
        with self.assertRaisesRegex(update.UpdateError, 'resume_source_changed_during_update'):
            self.updater.verify_resume_source()

    def test_missing_or_truncated_original_backup_blocks_reuse(self):
        with update.RECOVERY_BACKUP.open('wb') as stream:
            stream.truncate(1024)
        with self.assertRaisesRegex(update.UpdateError, 'resume_backup_metadata_unexpected'):
            self.updater.verify_resume_source()

    def baseline(self):
        return {'database_name': update.DATABASE, 'can_view_database_definition': 1, 'orders_count': 0,
                'core_tables': 13, 'new_columns': 0, 'new_tables': 0, 'new_indexes': 0,
                'new_foreign_keys': 0, 'original_order_index': 1, 'original_audit_metadata': 1}

    def test_admin_baseline_rejects_partial_unknown_or_invisible_schema_with_safe_diagnostics(self):
        self.updater.inspector = types.SimpleNamespace(SQL_MIGRATIONS='history')
        history = [{'MigrationId': name} for name in update.MIGRATIONS[:5]]
        for key, value in (('can_view_database_definition', 0), ('new_columns', 1), ('new_tables', 1),
                           ('original_order_index', 0), ('original_audit_metadata', None), ('orders_count', 1),
                           ('database_name', SECRET)):
            with self.subTest(key=key), patch.object(self.updater, 'sql_json', side_effect=[history, dict(self.baseline(), **{key: value})]):
                with self.assertRaisesRegex(update.UpdateError, 'resume_database_schema_baseline_unexpected'):
                    self.updater.verify_resume_schema_baseline(None)
                self.assertIn(key, self.updater.state['resume_schema_failed_checks'])
                self.assertNotIn(SECRET, json.dumps(self.updater.state))
        with patch.object(self.updater, 'sql_json', return_value=history + [{'MigrationId': SECRET}]) as read:
            with self.assertRaisesRegex(update.UpdateError, 'resume_database_history_unexpected'):
                self.updater.verify_resume_schema_baseline(None)
            self.assertEqual(1, read.call_count)
        with patch.object(self.updater, 'sql_json', side_effect=[history, self.baseline()]):
            self.updater.verify_resume_schema_baseline(None)
        self.assertNotIn('resume_schema_failed_checks', self.updater.state)

    def lifecycle(self, fail=None):
        events = []
        credentials = update.Credentials('fictional-admin', SECRET)
        before = self.source_bytes()
        def readonly():
            self.updater.verify_resume_source()
            self.updater.verify_staged_releases()
        def action(name):
            def perform(*args, **kwargs):
                events.append(name)
                if name == fail: raise update.UpdateError('fixture_failed')
            return perform
        def backup(*args):
            events.append('fresh_backup')
            self.assertNotEqual(update.RECOVERY_BACKUP, self.updater.backup_file)
            self.updater.verify_resume_schema_baseline(credentials)
            self.updater.state['verified_database_backup'] = str(self.updater.backup_file)
        def sql_run(*args, **kwargs):
            self.assertEqual("RESTORE VERIFYONLY FROM DISK=N'" + str(update.RECOVERY_BACKUP) + "' WITH CHECKSUM;", kwargs['query'])
            events.append('verify_previous_backup')
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(update, 'own_lock', contextlib.nullcontext))
            stack.enter_context(patch.object(update, 'interruption_guard', return_value=contextlib.nullcontext()))
            stack.enter_context(patch.object(self.updater, 'readonly_preflight', side_effect=readonly))
            stack.enter_context(patch.object(self.updater, 'prompt_admin', return_value=credentials))
            stack.enter_context(patch.object(self.updater, 'sql_run', side_effect=sql_run))
            stack.enter_context(patch.object(self.updater, 'backup_database', side_effect=backup))
            for name in ('preflight_admin', 'verify_resume_schema_baseline', 'unchanged_before_stop', 'stop', 'migrate',
                         'verify_history', 'switch_links', 'start', 'verify_health', 'restore_links', 'verify_previous_health'):
                stack.enter_context(patch.object(self.updater, name, side_effect=action(name)))
            adopt = stack.enter_context(patch.object(self.updater, 'adopt_structure'))
            stage = stack.enter_context(patch.object(self.updater, 'stage'))
            stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
            report = self.updater.run(apply=True)
            adopt.assert_not_called()
            stage.assert_not_called()
        self.assertEqual(before, self.source_bytes())
        self.assertNotIn(SECRET, json.dumps(report))
        return report, events

    def test_resume_revalidates_prior_backup_and_creates_fresh_snapshot_before_new_backup_and_ddl(self):
        report, events = self.lifecycle()
        self.assertEqual('updated', report['status'])
        self.assertTrue(report['changes_made'])
        self.assertTrue(report['previous_database_backup_reverified'])
        self.assertLess(events.index('verify_previous_backup'), events.index('stop'))
        self.assertLess(events.index('fresh_backup'), events.index('migrate'))
        self.assertEqual(3, events.count('verify_resume_schema_baseline'))
        self.assertNotEqual(str(update.RECOVERY_SOURCE), report['private_backup_directory'])
        self.assertNotEqual(str(update.RECOVERY_BACKUP), report['verified_database_backup'])

    def test_admin_schema_failure_never_stops_or_writes_fresh_snapshot(self):
        report, events = self.lifecycle(fail='verify_resume_schema_baseline')
        self.assertFalse(report['changes_made'])
        self.assertFalse(report['migrations_may_have_changed'])
        self.assertNotIn('stop', events)
        self.assertFalse(self.updater.private_dir.exists())

    def test_migration_failure_recovers_previous_links_and_preserves_both_original_artifacts(self):
        report, events = self.lifecycle(fail='migrate')
        self.assertTrue(report['changes_made'])
        self.assertTrue(report['migrations_may_have_changed'])
        self.assertEqual([], report['rollback_errors'])
        self.assertEqual(['stop', 'restore_links', 'start', 'verify_previous_health'], events[-4:])
        self.assertFalse(report['database_automatically_restored'])


if __name__ == '__main__':
    unittest.main()
