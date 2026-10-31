"""Generated web-account recovery: reuse, scope, secrecy and failure ordering."""
import copy
from contextlib import ExitStack
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).parents[2]
SPEC = importlib.util.spec_from_file_location('new_admin_fixture', ROOT / 'scripts/pilot/create-native-admin.py')
creator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(creator)
BASE_RAW = (ROOT / 'scripts/pilot/reset-native-admin.py').read_bytes()
base = creator.module_from_bytes(BASE_RAW)


def data(users):
    return {'database_name': base.DATABASE, 'migrations_count': 9, 'i04_migration_present': 1, 'users': users}


class NativeAdminCreationTests(unittest.TestCase):
    def test_source_pin_and_generated_credentials_are_unique_and_not_embedded(self):
        with self.assertRaisesRegex(creator.CreationError, '^previous_recovery_module_mismatch$'):
            creator.module_from_bytes(BASE_RAW + b'\n# changed')
        first, second = creator.new_record(base), creator.new_record(base)
        self.assertNotEqual(first['password'], second['password'])
        self.assertNotEqual(first['user_id'], second['user_id'])
        self.assertEqual(32, len(first['password']))
        self.assertEqual(first, creator.validate_record(base, first))
        self.assertNotIn(first['password'], (ROOT / 'scripts/pilot/create-native-admin.py').read_text())

    def test_credential_record_binds_target_hash_and_password(self):
        record = creator.new_record(base)
        for changed in ({'web_user': 'admin'}, {'database': 'other'}, {'server': 'remote'},
                        {'password': 'A' * 32}, {'password_hash': 'invalid'},
                        {'operation_id': 'invalid'}, {'extra': 'untrusted'}):
            with self.subTest(changed=changed), self.assertRaises(creator.CreationError):
                creator.validate_record(base, dict(record, **changed))

    def test_sql_creates_only_new_account_and_requires_matching_audit_for_reuse(self):
        record = creator.new_record(base)
        sql = creator.creation_sql(base, record)
        self.assertNotIn(record['password'], sql)
        self.assertNotIn(record['password_hash'], sql)
        self.assertNotIn('UPDATE dbo.AdminUsers', sql)
        self.assertNotIn('ALTER LOGIN', sql)
        self.assertIn('WITH (UPDLOCK,HOLDLOCK)', sql)
        self.assertIn('THROW 51021', sql)
        self.assertIn('AND ActorType=2 AND MetadataJson=@metadata', sql)
        self.assertLess(sql.index('INSERT INTO dbo.AuditLogs'), sql.index('COMMIT TRANSACTION'))
        self.assertIn('SET @created=1', sql)

    def test_private_record_survives_retry_without_password_rotation(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp) / 'account'
            original_fstat = os.fstat
            original_lstat = Path.lstat

            def fixture_stat(st):
                values = list(st)
                values[4] = 0  # Translate only CI-owned temporary fixture identity.
                return os.stat_result(values)

            with patch.object(creator, 'DIRECTORY', directory), \
                    patch.object(creator.os, 'fstat', side_effect=lambda fd: fixture_stat(original_fstat(fd))), \
                    patch.object(Path, 'lstat', side_effect=lambda self: fixture_stat(original_lstat(self)), autospec=True):
                first = creator.stored_record(base)
                second = creator.stored_record(base)
            self.assertEqual(first, second)
            self.assertEqual(0o700, directory.stat().st_mode & 0o777)
            self.assertEqual(0o600, (directory / creator.RECORD).stat().st_mode & 0o777)
            self.assertEqual(first, json.loads((directory / creator.RECORD).read_text()))

    def test_private_reader_rejects_symlinks_and_public_credential_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'value.json'
            target.write_text('{}')
            target.chmod(0o644)
            link = Path(tmp) / 'link.json'
            link.symlink_to(target)
            with self.assertRaises(OSError):
                creator.checked_read(link, mode=0o600)
            with self.assertRaisesRegex(creator.CreationError, '^private_file_permissions_unexpected$'):
                creator.checked_read(target, mode=0o600)

    def test_baseline_rejects_wrong_database_and_duplicate_user(self):
        self.assertEqual([], creator.baseline(base, data([])))
        empty = data([])
        del empty['users']
        self.assertEqual([], creator.baseline(base, empty))
        for invalid in (dict(data([]), database_name='other'), dict(data([]), migrations_count=5),
                        dict(data([]), users=None), data([{}, {}])):
            with self.subTest(invalid=invalid), self.assertRaises(creator.CreationError):
                creator.baseline(base, invalid)

    def run_apply(self, *, existing=None, login_failure=False, record_exists=False):
        record = creator.new_record(base)
        row = {'id': record['user_id'], 'user_name': creator.USER, 'password_hash': record['password_hash'],
               'is_active': 1, 'display_name': 'Fictional admin', 'created_at': '2026-10-26T00:00:00+00:00'}
        instance = base.Recovery(creator.USER)
        instance.state = {'changes_made': False, 'login_verified': False}
        events = []
        replies = [data(existing or []), {'account_created': 1, 'account_verified': 1, 'audit_verified': 1}, data([row])]

        def sql(_):
            events.append('sql')
            return replies.pop(0)

        def stored(_):
            events.append('stored')
            return record

        def login(password):
            events.append('login')
            self.assertEqual(record['password'], password)
            if login_failure:
                raise base.RecoveryError('web_login_failed_after_reset')
            instance.state['login_verified'] = True

        with ExitStack() as stack:
            stack.enter_context(patch.object(instance, 'service_ready'))
            stack.enter_context(patch.object(Path, 'is_file', return_value=True))
            stack.enter_context(patch.object(Path, 'exists', return_value=record_exists))
            stack.enter_context(patch.object(instance, 'http', return_value=(401, '{}')))
            stack.enter_context(patch.object(instance, 'sql', side_effect=sql))
            stack.enter_context(patch.object(base, 'prompt_secret', side_effect=['fictional_sa', 'fictional_secret']))
            stack.enter_context(patch.object(creator, 'stored_record', side_effect=stored))
            stack.enter_context(patch.object(instance, 'verify_login', side_effect=login))
            try:
                returned = creator.apply(base, instance)
                failure = None
            except (creator.CreationError, base.RecoveryError) as error:
                returned, failure = None, str(error)
        return instance, record, events, returned, failure

    def test_apply_saves_credentials_before_sql_then_verifies_account_and_https(self):
        instance, record, events, returned, failure = self.run_apply()
        self.assertIsNone(failure)
        self.assertEqual(['sql', 'stored', 'sql', 'sql', 'login'], events)
        self.assertEqual(record, returned)
        self.assertTrue(instance.state['account_created'])
        self.assertTrue(instance.state['account_verified'])
        self.assertTrue(instance.state['login_verified'])
        self.assertNotIn(record['password'], json.dumps(instance.state))
        self.assertNotIn(record['password_hash'], json.dumps(instance.state))

    def test_existing_user_without_private_provenance_is_not_adopted(self):
        instance, _, events, returned, failure = self.run_apply(existing=[{'id': 'another_user'}])
        self.assertEqual('existing_web_user_not_owned_by_operation', failure)
        self.assertEqual(['sql'], events)
        self.assertIsNone(returned)
        self.assertFalse(instance.state['changes_made'])

    def test_failed_login_preserves_that_account_creation_already_committed(self):
        instance, record, events, returned, failure = self.run_apply(login_failure=True)
        self.assertEqual('web_login_failed_after_reset', failure)
        self.assertTrue(instance.state['changes_made'])
        self.assertTrue(instance.state['account_created'])
        self.assertFalse(instance.state['login_verified'])
        self.assertIn('private_credential_file', instance.state)
        self.assertIsNone(returned)
        self.assertNotIn(record['password'], json.dumps(instance.state))


if __name__ == '__main__':
    unittest.main()
