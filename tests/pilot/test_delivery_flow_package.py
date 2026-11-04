import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SOURCE = 'a' * 40


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


I = module('delivery_flow_installer_test', 'install-delivery-flow-update.py')


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


class DeliveryFlowPackageTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name)
        self.root = self.path / 'app'
        self.backups = self.path / 'backups'
        self.backups.mkdir()
        self.environment = self.path / 'api.env'
        self.environment.write_bytes(b'SYNTHETIC_CONFIGURATION=keep-this-value\n')
        self.base = {
            'index.html': b'<link rel="stylesheet" href="styles-base.css"><script type="module" src="main-base.js"></script>',
            'main-base.js': b'import "./chunk-base.js";',
            'chunk-base.js': b'export const previous = true;',
            'styles-base.css': b'body { color: black; }',
            '3rdpartylicenses.txt': b'Synthetic previous notices',
        }
        self.files = {
            'index.html': b'<link rel="stylesheet" href="styles-demo.css"><script type="module" src="main-demo.js"></script>',
            'main-demo.js': b'import "./chunk-demo.js"; import("./chunk-lazy.js");',
            'chunk-demo.js': b'export const application = true;',
            'chunk-lazy.js': b'export const lazy = true;',
            'styles-demo.css': b'body { background: url("assets/logo.svg"); }',
            'assets/logo.svg': b'<svg xmlns="http://www.w3.org/2000/svg"/>',
            '3rdpartylicenses.txt': b'Synthetic third-party notices',
        }
        self.backend_files = {
            'Prodimt.Pedidos.Api.dll': b'synthetic-exact-installed-i22-dll',
            'Prodimt.Pedidos.Application.dll': b'synthetic-exact-application-dll',
            'Prodimt.Pedidos.Domain.dll': b'synthetic-exact-domain-dll',
            'Prodimt.Pedidos.Infrastructure.dll': b'synthetic-exact-infrastructure-dll',
            'appsettings.json': b'{"SyntheticSetting":"keep-base"}',
            'appsettings.Production.json': b'{"SyntheticProduction":"keep-base"}',
        }
        for name, value in (
            ('EXPECTED_SOURCE', SOURCE), ('ROOT', self.root), ('BACKUPS', self.backups),
            ('ENV_FILE', self.environment), ('BASE_FRONTEND_HASHES', {name: digest(raw) for name, raw in self.base.items()}),
            ('BASE_API_SHA256', digest(self.backend_files['Prodimt.Pedidos.Api.dll'])),
            ('BASE_BACKEND_HASHES', {name: digest(raw) for name, raw in self.backend_files.items() if name.endswith('.dll')}),
        ):
            replacement = patch.object(I, name, value)
            replacement.start()
            self.addCleanup(replacement.stop)
        # Native files are root-owned; CI fixtures belong to its unprivileged
        # runner. Mock only that filesystem boundary, retaining the real link,
        # hash, public-asset, authorization and activation checks below.
        filesystem = patch.object(I, 'safe_directory')
        filesystem.start()
        self.addCleanup(filesystem.stop)
        reads = patch.object(I, 'regular', side_effect=lambda path, limit=1024*1024: path.read_bytes())
        reads.start()
        self.addCleanup(reads.stop)

    def payload(self, files=None, extra=None, manifest_changes=None):
        contents = {'frontend/' + name: raw for name, raw in (self.files if files is None else files).items()}
        contents.update(extra or {})
        manifest = dict(feature='delivery-flow-v1', sourceCommit=SOURCE, baseRelease=I.OLD,
                        backendRelease=I.BACKEND_RELEASE,
                        files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in contents.items()})
        manifest.update(manifest_changes or {})
        path = self.path / 'payload.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            archive.writestr('manifest.json', json.dumps(manifest))
            for name, raw in contents.items():
                archive.writestr(name, raw)
        return path, digest(path.read_bytes())

    def instance(self):
        for part in ('backend', 'frontend'):
            (self.root / part / 'releases').mkdir(parents=True)
        instance = I.Installer(self.path / 'unused.zip')
        for directory, files in ((instance.old, self.base), (instance.backend, self.backend_files)):
            directory.mkdir()
            for name, raw in files.items():
                path = directory / name
                path.parent.mkdir(exist_ok=True, parents=True)
                path.write_bytes(raw)
        instance.current.symlink_to(instance.old)
        (self.root / 'backend/current').symlink_to(instance.backend)
        instance.package, pin = self.payload()
        pin_patch = patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin)
        pin_patch.start()
        self.addCleanup(pin_patch.stop)
        instance.identity_value = (1234, I.BASE_API_SHA256, digest(self.environment.read_bytes()), ('settings-base', 'settings-production'))
        instance.backend_identity = Mock(side_effect=lambda: instance.identity_value)
        instance.public_requests = []

        def http(path):
            instance.public_requests.append(path)
            if path == '/health/db':
                return 200, b'{"database":"reachable"}'
            if path.startswith('/api/'):
                return 401, b''
            current = instance.current.resolve()
            target = current / ('index.html' if path == '/' else path.lstrip('/'))
            return (200, target.read_bytes()) if target.is_file() else (404, b'')

        instance.http = http
        return instance

    def assert_unchanged(self, instance):
        self.assertEqual(instance.old, instance.current.resolve())
        self.assertFalse(instance.new.exists())
        self.assertEqual([], list(self.backups.iterdir()))
        self.assertFalse(instance.state['changes_made'])

    def test_payload_pin_manifest_and_bytes_are_bound_to_one_verified_archive(self):
        path, pin = self.payload()
        with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin):
            self.assertEqual(self.files, I.load_payload(path))
            original = path.read_bytes()
            with patch.object(I, 'regular', side_effect=lambda current, limit: (current.write_bytes(b'replaced'), original)[1]):
                self.assertEqual(self.files, I.load_payload(path))
        with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', 'b' * 64):
            with self.assertRaisesRegex(I.InstallError, 'payload_hash_mismatch'):
                I.load_payload(path)

    def test_unknown_manifest_base_or_source_is_rejected(self):
        for changes in ({'feature': 'other-feature'}, {'sourceCommit': 'b' * 40},
                        {'baseRelease': 'production-d3a11420b202'}, {'backendRelease': 'other-backend'}):
            with self.subTest(changes=changes):
                path, pin = self.payload(manifest_changes=changes)
                with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin):
                    with self.assertRaisesRegex(I.InstallError, 'payload_manifest_unexpected'):
                        I.load_payload(path)

    def test_payload_rejects_backend_source_maps_private_files_and_traversal(self):
        for name in ('backend/Prodimt.Pedidos.Api.dll', 'frontend/source.map', 'frontend/.env',
                     'frontend/../../outside', 'frontend/assets/.private'):
            with self.subTest(name=name):
                path, pin = self.payload(extra={name: b'forbidden'})
                with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin):
                    with self.assertRaises(I.InstallError):
                        I.load_payload(path)

    def test_payload_requires_transitive_js_and_css_asset_closure(self):
        for absent in ('main-demo.js', 'chunk-demo.js', 'chunk-lazy.js', 'styles-demo.css', 'assets/logo.svg'):
            with self.subTest(absent=absent):
                files = dict(self.files)
                del files[absent]
                path, pin = self.payload(files=files)
                with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin):
                    with self.assertRaisesRegex(I.InstallError, 'frontend_asset_reference_missing'):
                        I.load_payload(path)
        for reference in ('https://elsewhere.invalid/main.js', '//elsewhere.invalid/main.js', '../outside.js', 'main-demo.js?other'):
            with self.subTest(reference=reference):
                files = dict(self.files, **{'index.html': ('<script src="' + reference + '"></script>').encode()})
                path, pin = self.payload(files=files)
                with patch.object(I, 'EXPECTED_PAYLOAD_SHA256', pin):
                    with self.assertRaises(I.InstallError):
                        I.load_payload(path)

    def test_unknown_frontend_release_blocks_before_backend_or_http_and_writes(self):
        instance = self.instance()
        other = instance.old.parent / 'delivery-future-change'
        other.mkdir()
        instance.current.unlink()
        instance.current.symlink_to(other)
        instance.http = Mock()
        with self.assertRaisesRegex(I.InstallError, 'frontend_release_not_expected'):
            instance.apply()
        instance.backend_identity.assert_not_called()
        instance.http.assert_not_called()
        self.assertEqual(other, instance.current.resolve())
        self.assertFalse(instance.new.exists())
        self.assertEqual([], list(self.backups.iterdir()))

    def test_changed_base_assets_and_extra_files_block_before_snapshot_or_stage(self):
        for change in ('changed-chunk', 'extra-file'):
            with self.subTest(change=change):
                # Each subcase needs fresh link/release directories.
                if self.root.exists():
                    shutil.rmtree(self.root)
                instance = self.instance()
                if change == 'changed-chunk':
                    (instance.old / 'chunk-base.js').write_bytes(b'unrecognized-concurrent-frontend')
                else:
                    (instance.old / 'unrecognized.js').write_bytes(b'extra-file')
                with self.assertRaisesRegex(I.InstallError, 'frontend_(file_hash_mismatch|files_unexpected)'):
                    instance.apply()
                self.assert_unchanged(instance)

    def test_every_current_entrypoint_and_js_css_asset_must_be_served_exactly(self):
        instance = self.instance()
        original = instance.http
        instance.http = lambda path: (200, b'other-live-chunk') if path == '/chunk-base.js' else original(path)
        with self.assertRaisesRegex(I.InstallError, 'current_frontend_not_served'):
            instance.apply()
        self.assert_unchanged(instance)

    def test_existing_or_dangling_pending_recovery_blocks_before_health_or_writes(self):
        instance = self.instance()
        instance.http = Mock()
        marker = self.backups / 'delivery-other-pending.json'
        for dangling in (False, True):
            with self.subTest(dangling=dangling):
                if dangling:
                    marker.symlink_to(self.path / 'missing-private-journal')
                else:
                    marker.write_text('{"synthetic":"pending"}')
                with self.assertRaisesRegex(I.InstallError, 'pending_recovery_requires_original_installer'):
                    instance.apply()
                instance.http.assert_not_called()
                instance.backend_identity.assert_not_called()
                self.assertEqual(instance.old, instance.current.resolve())
                self.assertFalse(instance.new.exists())
                self.assertFalse(instance.state['changes_made'])
                self.assertEqual([marker], list(self.backups.iterdir()))
                marker.unlink()

    def test_recovery_marker_created_during_stage_prevents_activation(self):
        instance = self.instance()
        original_stage = instance.stage

        def stage_with_concurrent_pending_recovery():
            original_stage()
            (self.backups / 'delivery-other-pending.json').write_text('{"synthetic":"pending"}')

        instance.stage = stage_with_concurrent_pending_recovery
        with self.assertRaisesRegex(I.InstallError, 'pending_recovery_requires_original_installer'):
            instance.apply()
        self.assertEqual(instance.old, instance.current.resolve())
        self.assertFalse(instance.switched)

    def test_exact_base_preflight_is_read_only_and_checks_public_asset_set(self):
        instance = self.instance()
        instance.preflight()
        self.assert_unchanged(instance)
        self.assertTrue(instance.state['base_release_verified'])
        self.assertTrue(instance.state['health_verified'])
        self.assertTrue({'/', '/main-base.js', '/chunk-base.js', '/styles-base.css'}.issubset(instance.public_requests))

    def test_apply_is_atomic_frontend_only_and_second_apply_is_idempotent(self):
        instance = self.instance()
        backend_before = {path.name: path.read_bytes() for path in instance.backend.iterdir()}
        configuration_before = self.environment.read_bytes()
        with patch.object(I, 'command', side_effect=AssertionError('unexpected-process-or-SQL-command')):
            result = instance.apply()
            self.assertEqual('updated', result['status'])
            self.assertTrue(result['frontend_verified'])
            self.assertEqual(instance.new, instance.current.resolve())
            for key in ('database_migrations_run', 'database_queries_run', 'backend_changed', 'service_restarted', 'password_changed'):
                self.assertFalse(result[key], key)
            self.assertEqual({name: (instance.old / name).read_bytes() for name in self.base}, self.base)
            self.assertEqual('already_current', instance.apply()['status'])
        self.assertEqual(1, len(list(self.backups.iterdir())))
        self.assertEqual(backend_before, {path.name: path.read_bytes() for path in instance.backend.iterdir()})
        self.assertEqual(configuration_before, self.environment.read_bytes())

    def test_failed_new_lazy_asset_restores_previous_frontend_without_private_output(self):
        instance = self.instance()
        http = instance.http
        instance.http = lambda path: (500, b'SYNTHETIC_PRIVATE_RESPONSE') if instance.current.resolve() == instance.new and path == '/chunk-lazy.js' else http(path)
        with self.assertRaisesRegex(I.InstallError, 'new_frontend_asset_not_served'):
            instance.apply()
        self.assertEqual(instance.old, instance.current.resolve())
        self.assertTrue(instance.state['previous_frontend_restored'])
        self.assertNotIn('SYNTHETIC_PRIVATE_RESPONSE', json.dumps(instance.state))

    def test_interruption_after_atomic_link_replacement_restores_base(self):
        instance = self.instance()
        switch = instance.switch

        def interrupted(target):
            switch(target)
            if target == instance.new:
                raise I.InstallError('installation_interrupted')

        instance.switch = interrupted
        with self.assertRaisesRegex(I.InstallError, 'installation_interrupted'):
            instance.apply()
        self.assertEqual(instance.old, instance.current.resolve())
        self.assertTrue(instance.state['previous_frontend_restored'])

    def test_concurrent_backend_configuration_change_blocks_activation(self):
        instance = self.instance()
        identity = instance.identity_value
        instance.backend_identity.side_effect = [identity, (*identity[:2], 'changed-environment', identity[3])]
        with self.assertRaisesRegex(I.InstallError, 'backend_changed_during_update'):
            instance.apply()
        self.assertEqual(instance.old, instance.current.resolve())

    def test_concurrent_unknown_frontend_link_is_preserved(self):
        instance = self.instance()
        other = instance.old.parent / 'other-concurrent-update'
        other.mkdir()
        original_stage = instance.stage

        def stage_then_competing_update():
            original_stage()
            instance.current.unlink()
            instance.current.symlink_to(other)

        instance.stage = stage_then_competing_update
        with self.assertRaisesRegex(I.InstallError, 'frontend_changed_during_update'):
            instance.apply()
        self.assertEqual(other, instance.current.resolve())
        self.assertFalse(instance.state.get('previous_frontend_restored', False))

    def test_customer_admin_and_both_delivery_endpoints_remain_protected(self):
        instance = self.instance()
        protected = ('/api/admin/customers',
                     '/api/customer-orders/11111111-1111-1111-1111-111111111111/next',
                     '/api/delivery', '/api/admin/delivery')
        http = instance.http
        I.Installer.api_health(instance)
        self.assertTrue(set(protected).issubset(instance.public_requests))
        for endpoint in protected:
            with self.subTest(endpoint=endpoint):
                instance.http = lambda path, endpoint=endpoint: (404, b'') if path == endpoint else http(path)
                with self.assertRaises(I.InstallError):
                    I.Installer.api_health(instance)

    def test_backend_identity_requires_pinned_i22_dll_and_detects_configuration_changes(self):
        instance = self.instance()
        output = f'ActiveState=active\nMainPID=1234\nUser=www-data\nWorkingDirectory={self.root}/backend/current\n'.encode()
        original_resolve = Path.resolve

        def resolve(path, *args, **kwargs):
            if str(path) == '/proc/1234/cwd':
                return instance.backend
            return original_resolve(path, *args, **kwargs)

        with patch.object(I, 'command', return_value=output) as command, patch.object(Path, 'resolve', resolve):
            identity = I.Installer.backend_identity(instance)
            self.environment.write_bytes(b'SYNTHETIC_CONFIGURATION=changed\n')
            self.assertNotEqual(identity, I.Installer.backend_identity(instance))
            (instance.backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'other-dll-same-release-name')
            with self.assertRaisesRegex(I.InstallError, 'backend_binary_not_expected'):
                I.Installer.backend_identity(instance)
        self.assertTrue(all(call.args[0][:2] == ['systemctl', 'show'] for call in command.call_args_list))

    def test_builder_pins_installer_manifest_and_sha256s_without_backend_or_source(self):
        builder = module('delivery_flow_builder_test', 'build-delivery-flow-package.py')
        frontend = self.path / 'dist'
        frontend.mkdir()
        for name, raw in self.files.items():
            path = frontend / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        output = self.path / 'package.zip'
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', '', SOURCE + '\n', '']):
            report = builder.build(frontend, output, SOURCE)
        with zipfile.ZipFile(output) as outer:
            names = outer.namelist()
            installer_name = next(name for name in names if name.endswith('.py'))
            payload_name = next(name for name in names if name.endswith('payload.zip'))
            installer = outer.read(installer_name).decode()
            payload_raw = outer.read(payload_name)
            self.assertIn(report['payloadSha256'], installer)
            self.assertIn(SOURCE, installer)
            self.assertNotIn('REPLACE_', installer)
            self.assertEqual(report['payloadSha256'], digest(payload_raw))
            with zipfile.ZipFile(io.BytesIO(payload_raw)) as payload:
                self.assertEqual({'manifest.json'} | {'frontend/' + name for name in self.files}, set(payload.namelist()))
                manifest = json.loads(payload.read('manifest.json'))
                self.assertEqual('delivery-flow-v1', manifest['feature'])
                self.assertEqual(I.OLD, manifest['baseRelease'])
                self.assertEqual(I.BACKEND_RELEASE, manifest['backendRelease'])
                for name, raw in self.files.items():
                    self.assertEqual({'bytes': len(raw), 'sha256': digest(raw)}, manifest['files']['frontend/' + name])
            sums_name = next(name for name in names if name.endswith('/SHA256SUMS.txt'))
            prefix = sums_name.removesuffix('SHA256SUMS.txt')
            for row in outer.read(sums_name).decode().splitlines():
                expected, name = row.split('  ', 1)
                self.assertEqual(expected, digest(outer.read(prefix + name)))

    def test_builder_dirty_source_or_missing_lazy_chunk_produces_no_package(self):
        builder = module('delivery_flow_builder_guard_test', 'build-delivery-flow-package.py')
        frontend = self.path / 'dist'
        frontend.mkdir()
        for name, raw in self.files.items():
            path = frontend / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        output = self.path / 'package.zip'
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', ' M source.ts']):
            with self.assertRaisesRegex(ValueError, 'source_tree_not_clean'):
                builder.build(frontend, output, SOURCE)
        self.assertFalse(output.exists())
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', '', SOURCE + '\n', ' M source.ts']):
            with self.assertRaisesRegex(ValueError, 'source_tree_not_clean'):
                builder.build(frontend, output, SOURCE)
        self.assertFalse(output.exists())
        (frontend / 'chunk-lazy.js').unlink()
        support = builder.installer_support()
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', '']), \
                patch.object(builder, 'installer_support', return_value=support):
            with self.assertRaisesRegex(support.InstallError, 'frontend_asset_reference_missing'):
                builder.build(frontend, output, SOURCE)
        self.assertFalse(output.exists())

    def test_builder_adds_angular_sibling_license_in_memory_and_rejects_symlink(self):
        builder = module('delivery_flow_builder_angular_license_test', 'build-delivery-flow-package.py')
        dist = self.path / 'angular-dist'
        frontend = dist / 'browser'
        frontend.mkdir(parents=True)
        for name, raw in self.files.items():
            if name == '3rdpartylicenses.txt':
                continue
            path = frontend / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        notices = dist / '3rdpartylicenses.txt'
        notices.write_bytes(self.files['3rdpartylicenses.txt'])
        (dist / 'prerendered-routes.json').write_bytes(b'{"synthetic":"not-a-web-asset"}')
        before = {path.relative_to(dist).as_posix(): path.read_bytes() for path in dist.rglob('*') if path.is_file()}
        output = self.path / 'angular-package.zip'
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', '', SOURCE + '\n', '']):
            builder.build(frontend, output, SOURCE)
        with zipfile.ZipFile(output) as outer:
            payload_name = next(name for name in outer.namelist() if name.endswith('payload.zip'))
            with zipfile.ZipFile(io.BytesIO(outer.read(payload_name))) as payload:
                self.assertEqual({'manifest.json'} | {'frontend/' + name for name in self.files}, set(payload.namelist()))
                self.assertEqual(self.files['3rdpartylicenses.txt'], payload.read('frontend/3rdpartylicenses.txt'))
        self.assertEqual(before, {path.relative_to(dist).as_posix(): path.read_bytes() for path in dist.rglob('*') if path.is_file()})
        self.assertFalse((frontend / '3rdpartylicenses.txt').exists())

        notices.unlink()
        external_notices = self.path / 'external-notices.txt'
        external_notices.write_bytes(b'outside-the-Angular-build')
        notices.symlink_to(external_notices)
        rejected_output = self.path / 'rejected-angular-package.zip'
        with patch.object(builder.subprocess, 'check_output', side_effect=[SOURCE + '\n', '']):
            with self.assertRaisesRegex(ValueError, 'frontend_symlink_not_allowed'):
                builder.build(frontend, rejected_output, SOURCE)
        self.assertFalse(rejected_output.exists())
        self.assertTrue(notices.is_symlink())
        self.assertFalse((frontend / '3rdpartylicenses.txt').exists())


if __name__ == '__main__':
    unittest.main()
