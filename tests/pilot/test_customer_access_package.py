"""I09 payload trust boundary, preserved configuration and two-link rollback."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


I = module('customer_access_installer_fixture', 'install-customer-access-update.py')
B = module('customer_access_builder_fixture', 'build-customer-access-package.py')
SOURCE = 'a' * 40


class CustomerAccessPackageTests(unittest.TestCase):
    def test_database_report_identifies_only_failed_allowlisted_checks_without_raw_values(self):
        checks = {**I.EXPECTED_DATABASE, 'database_name': 'never-print-this-name',
                  'update_customers': 0, 'read_tokens': None, 'unknown-secret': 'never-print-this-secret'}
        report = I.database_check_report(checks)
        self.assertEqual(['database_name', 'update_customers', 'read_tokens'], report['mismatched_fields'])
        self.assertEqual(0, report['checks']['update_customers']['observed'])
        self.assertIsNone(report['checks']['read_tokens']['observed'])
        self.assertEqual(1, report['unexpected_field_count'])
        encoded = json.dumps(report)
        for private in ('never-print-this-name', 'never-print-this-secret', 'unknown-secret'):
            self.assertNotIn(private, encoded)

    def test_database_report_handles_missing_and_invalid_values_without_weakening_equality(self):
        for value in (None, [], 'never-print-this-secret'):
            report = I.database_check_report(value)
            self.assertFalse(report['result_is_object'])
            self.assertEqual(list(I.EXPECTED_DATABASE), report['mismatched_fields'])
            self.assertNotIn('never-print-this-secret', json.dumps(report))
        self.assertEqual([], I.database_check_report(I.EXPECTED_DATABASE)['mismatched_fields'])

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        self.backend, self.frontend = self.folder / 'publish', self.folder / 'browser'
        self.backend.mkdir()
        self.frontend.mkdir()
        (self.backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'fictional-assembly')
        (self.backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
        (self.backend / 'appsettings.json').write_text('{}')
        (self.frontend / 'index.html').write_text('<script src="main.js"></script>')
        (self.frontend / 'main.js').write_text('var e={apiBaseUrl:""};var c="/api/customer-orders/";var p="/capture";')
        self.bundle = self.folder / 'bundle.zip'
        with patch.object(B, 'validate_sources'):
            self.report = B.build(self.backend, self.frontend, self.bundle, SOURCE)
        with zipfile.ZipFile(self.bundle) as archive:
            self.payload = self.folder / 'payload.zip'
            self.payload.write_bytes(archive.read('prodimt-customer-access-update/prodimt-customer-access-payload.zip'))
        for name, value in (('EXPECTED_SOURCE', SOURCE), ('EXPECTED_PAYLOAD_SHA256', self.report['payloadSha256']),
                            ('ROOT', self.folder / 'app'), ('BACKUPS', self.folder / 'backups'),
                            ('ENV_FILE', self.folder / 'api.env'), ('LOCK', self.folder / 'update.lock')):
            patcher = patch.object(I, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        patcher = patch.object(I, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes())
        patcher.start()
        self.addCleanup(patcher.stop)

    def instance(self):
        instance = I.make_installer(self.payload)
        I.BACKUPS.mkdir(exist_ok=True)
        I.ENV_FILE.write_bytes(b'private-environment-fixture')
        old = {}
        for part in ('backend', 'frontend'):
            root = I.ROOT / part / 'releases'
            root.mkdir(parents=True)
            old[part] = root / (I.I07 if part == 'backend' else 'client-config-' + B.I08_SOURCE[:12])
            old[part].mkdir()
            (I.ROOT / part / 'current').symlink_to(old[part])
        (old['frontend'] / 'index.html').write_bytes(b'old-index')
        (old['backend'] / 'appsettings.json').write_bytes(b'private-existing-configuration')
        def preflight():
            instance.old = {part: (I.ROOT / part / 'current').resolve() for part in old}
            instance.environment = I.ENV_FILE.read_bytes()
            instance.overrides = {'backend/appsettings.json': (instance.old['backend'] / 'appsettings.json').read_bytes()}
        def stage():
            for part, target in instance.new.items():
                target.mkdir(exist_ok=True)
                for name, raw in instance.expected(part).items():
                    path = target / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(raw)
        instance.preflight = preflight
        instance.stage = stage
        instance.process = lambda expected: None
        instance.health = lambda new: instance.state.update(frontend_verified=new)
        instance.verify_stage = lambda part: None
        instance.runtime.regular = lambda path, limit=0: Path(path).read_bytes()
        instance.events = []
        instance.runtime.command = lambda args, **kwargs: instance.events.append(args[1]) or ''
        return instance, old

    def test_builder_pins_installer_and_payload_without_new_sql_or_runtime_dependency(self):
        with zipfile.ZipFile(self.bundle) as archive:
            script = archive.read('prodimt-customer-access-update/PRODIMT_Instalar_I09.py').decode()
        self.assertIn(self.report['payloadSha256'], script)
        self.assertIn(SOURCE, script)
        self.assertNotIn('REPLACE_', script)
        files, manifest = I.load_payload(self.payload)
        self.assertEqual(11, manifest['migrationsRequired'])
        self.assertEqual('18:00', manifest['deliveryCutoff'])
        self.assertEqual({'inspect-native.py', 'install-password-update.py'},
                         {name for name in files if '/' not in name})
        self.assertFalse(any(name.endswith('.sql') for name in files))
        self.assertEqual([I.I07, 'client-config-' + B.I08_SOURCE[:12]], manifest['previousFrontendReleases'])

    def test_tampered_payload_never_executes_activation_dependency(self):
        self.payload.write_bytes(self.payload.read_bytes() + b'changed')
        with patch.object(I, 'exec', create=True) as execute:
            with self.assertRaisesRegex(I.InstallError, '^payload_hash_mismatch$'):
                I.make_installer(self.payload)
        execute.assert_not_called()

    def test_unlisted_frontend_release_cannot_pass_preflight(self):
        instance = I.make_installer(self.payload)
        I.BACKUPS.mkdir()
        for part in ('backend', 'frontend'):
            releases = I.ROOT / part / 'releases'
            releases.mkdir(parents=True)
            target = releases / (I.I07 if part == 'backend' else 'client-config-bbbbbbbbbbbb')
            target.mkdir()
            (I.ROOT / part / 'current').symlink_to(target)
        with patch.object(instance.runtime, 'directory_safe'), patch.object(instance, 'process') as process:
            with self.assertRaisesRegex(I.InstallError, '^previous_release_not_pinned$'):
                instance.preflight()
        process.assert_not_called()

    def test_apply_preserves_configuration_and_repeat_does_not_restart(self):
        instance, old = self.instance()
        instance.apply()
        self.assertEqual('updated', instance.state['status'])
        self.assertEqual(['stop', 'start'], instance.events)
        for part, path in instance.new.items():
            self.assertEqual(path, (I.ROOT / part / 'current').resolve())
        self.assertEqual(b'private-existing-configuration', (instance.new['backend'] / 'appsettings.json').read_bytes())
        self.assertEqual(b'private-existing-configuration', (old['backend'] / 'appsettings.json').read_bytes())
        self.assertFalse(instance.state['database_migrations_run'])
        self.assertFalse(instance.state['database_writes_run'])
        snapshot = Path(instance.state['private_snapshot'])
        self.assertEqual(0o700, stat.S_IMODE(snapshot.stat().st_mode))
        self.assertTrue(all(stat.S_IMODE(path.stat().st_mode) == 0o600 for path in snapshot.iterdir()))
        instance.events.clear()
        instance.apply()
        self.assertEqual('already_current', instance.state['status'])
        self.assertEqual([], instance.events)
        self.assertEqual(1, len(list(I.BACKUPS.iterdir())))

    def test_failure_after_first_link_switch_restores_both_old_links(self):
        instance, old = self.instance()
        switch = instance.switch
        def interrupted(target):
            if target == instance.new:
                current = I.ROOT / 'backend/current'
                current.unlink()
                current.symlink_to(instance.new['backend'])
                raise I.InstallError('installation_interrupted')
            switch(target)
        instance.switch = interrupted
        with self.assertRaisesRegex(I.InstallError, 'installation_interrupted'):
            instance.apply()
        instance.rollback()
        self.assertEqual(['stop', 'stop', 'start'], instance.events)
        for part, path in old.items():
            self.assertEqual(path, (I.ROOT / part / 'current').resolve())
        self.assertTrue(instance.state['previous_health_verified'])

    def test_failed_preflight_never_stops_service_or_stages(self):
        instance, old = self.instance()
        instance.preflight = lambda: (_ for _ in ()).throw(I.InstallError('guarded'))
        with self.assertRaisesRegex(I.InstallError, 'guarded'):
            instance.apply()
        instance.rollback()
        self.assertEqual([], instance.events)
        self.assertFalse(any(path.exists() for path in instance.new.values()))
        for part, path in old.items():
            self.assertEqual(path, (I.ROOT / part / 'current').resolve())

    def test_changed_configuration_blocks_activation(self):
        instance, old = self.instance()
        stage = instance.stage
        def drift():
            stage()
            I.ENV_FILE.write_bytes(b'changed-during-stage')
        instance.stage = drift
        with self.assertRaisesRegex(I.InstallError, 'configuration_changed_during_staging'):
            instance.apply()
        self.assertEqual([], instance.events)
        for part, path in old.items():
            self.assertEqual(path, (I.ROOT / part / 'current').resolve())

    def test_readonly_mode_does_not_create_lock_stop_service_or_stage(self):
        instance, _ = self.instance()
        with patch.object(I, 'make_installer', return_value=instance), patch.object(I.os, 'geteuid', return_value=0), \
                patch('sys.argv', ['installer', '--package', str(self.payload)]), patch('sys.stdout', new_callable=io.StringIO) as out:
            self.assertEqual(0, I.main())
        state = json.loads(out.getvalue())
        self.assertEqual('planned', state['status'])
        self.assertEqual('read-only', state['mode'])
        self.assertFalse(I.LOCK.exists())
        self.assertEqual([], instance.events)
        self.assertFalse(any(path.exists() for path in instance.new.values()))

    def test_interrupted_extraction_never_publishes_partial_release(self):
        instance, _ = self.instance()
        actual_stage = type(instance).stage
        instance.preflight()
        instance.expected = lambda part: {'first.dll': b'valid-fixture', 'invalid.dll': None}
        with patch.object(I.os, 'chown'):
            with self.assertRaises(TypeError):
                actual_stage(instance)
        self.assertFalse(any(path.exists() for path in instance.new.values()))
        self.assertFalse(list(I.ROOT.rglob('.customer-access-stage-*')))

    def test_builder_refuses_secrets_symlinks_and_wrong_origin(self):
        for kind in ('secret', 'symlink', 'origin'):
            with self.subTest(kind=kind), patch.object(B, 'validate_sources'):
                if kind == 'secret':
                    path = self.backend / 'credentials.pfx'
                    path.write_bytes(b'fictional-private')
                elif kind == 'symlink':
                    path = self.frontend / 'linked.js'
                    path.symlink_to(self.frontend / 'main.js')
                else:
                    path = self.frontend / 'main.js'
                    path.write_bytes(b'var e={apiBaseUrl:"https://unrelated.invalid"};')
                with self.assertRaises(ValueError):
                    B.build(self.backend, self.frontend, self.folder / 'forbidden.zip', SOURCE)
                if kind != 'origin':
                    path.unlink()

    def test_dirty_source_is_rejected_before_output(self):
        with patch.object(B.subprocess, 'check_output', side_effect=[SOURCE + '\n', ' M source.cs\n']):
            with self.assertRaisesRegex(ValueError, 'source_tree_not_clean'):
                B.build(self.backend, self.frontend, self.folder / 'dirty.zip', SOURCE)
        self.assertFalse((self.folder / 'dirty.zip').exists())


if __name__ == '__main__':
    unittest.main()
