"""Native I05 package integrity and activation rollback with fictional files."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile


ROOT = Path(__file__).parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


installer = module('password_install_fixture', 'install-password-update.py')
builder = module('password_build_fixture', 'build-password-package.py')
SOURCE = 'a' * 40


class PasswordPackageTests(unittest.TestCase):
    def fixture(self, folder):
        backend, frontend = folder / 'backend', folder / 'frontend'
        backend.mkdir()
        frontend.mkdir()
        (backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'fictional-assembly')
        (backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
        (backend / 'appsettings.json').write_text('{}')
        (frontend / 'index.html').write_text('<script src="main.js"></script>')
        (frontend / 'main.js').write_text('var e={apiBaseUrl:""};var path="/api/admin/account/change-password";')
        bundle = folder / 'bundle.zip'
        result = builder.build(backend, frontend, bundle, SOURCE)
        with zipfile.ZipFile(bundle) as archive:
            payload = folder / 'payload.zip'
            payload.write_bytes(archive.read('prodimt-password-update/payload.zip'))
        return result, payload

    def test_bundle_pins_installer_and_carries_exact_hashed_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            result, payload = self.fixture(root)
            with zipfile.ZipFile(root / 'bundle.zip') as archive:
                script = archive.read('prodimt-password-update/install.py').decode()
                self.assertIn("EXPECTED_PACKAGE_SHA256 = '" + result['payload_sha256'] + "'", script)
                self.assertIn("EXPECTED_SOURCE = '" + SOURCE + "'", script)
            original = installer.regular
            with patch.object(installer, 'EXPECTED_PACKAGE_SHA256', result['payload_sha256']), \
                    patch.object(installer, 'EXPECTED_SOURCE', SOURCE), \
                    patch.object(installer, 'regular', side_effect=lambda path, limit: Path(path).read_bytes()):
                files = installer.load_package(payload)
            self.assertEqual(b'fictional-assembly', files['backend/Prodimt.Pedidos.Api.dll'])
            self.assertIn('inspect-native.py', files)

    def test_package_tamper_is_rejected_before_execution(self):
        with tempfile.TemporaryDirectory() as tmp:
            result, payload = self.fixture(Path(tmp))
            payload.write_bytes(payload.read_bytes() + b'changed')
            with patch.object(installer, 'EXPECTED_PACKAGE_SHA256', result['payload_sha256']), \
                    patch.object(installer, 'EXPECTED_SOURCE', SOURCE), \
                    patch.object(installer, 'regular', side_effect=lambda path, limit: Path(path).read_bytes()):
                with self.assertRaisesRegex(installer.InstallError, '^package_hash_mismatch$'):
                    installer.load_package(payload)

    def test_paths_refuse_traversal_absolute_and_noncanonical_names(self):
        for path in ('../bad', '/absolute', '.', '', 'backend/../bad', 'backend//bad', 'C:bad', 'backend\\bad'):
            with self.subTest(path=path): self.assertFalse(installer.safe_name(path))
        self.assertTrue(installer.safe_name('backend/es/messages.dll'))

    def test_failed_activation_restores_old_links_and_health_in_order(self):
        instance = installer.Installer('fictional.zip')
        instance.stop_requested = True
        events = []
        with patch.object(installer, 'command', side_effect=lambda args, **kw: events.append(args[1]) or ''), \
                patch.object(instance, 'switch', side_effect=lambda target: events.append('old-links') if target == instance.old else None), \
                patch.object(instance, 'health', side_effect=lambda new: events.append('old-health') if not new else None):
            instance.rollback()
        self.assertEqual(['stop', 'old-links', 'start', 'old-health'], events)
        self.assertTrue(instance.state['previous_health_verified'])

    def test_readonly_failure_never_stops_or_switches(self):
        instance = installer.Installer('fictional.zip')
        with patch.object(instance, 'preflight', side_effect=installer.InstallError('guarded')), \
                patch.object(installer, 'command') as command, patch.object(instance, 'stage') as stage:
            with self.assertRaises(installer.InstallError): instance.apply()
            instance.rollback()
        command.assert_not_called()
        stage.assert_not_called()

    def test_switch_refuses_to_replace_regular_current(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'backend').mkdir()
            (root / 'backend/current').write_text('unrelated-file')
            with patch.object(installer, 'ROOT', root):
                instance = installer.Installer('fictional.zip')
                with self.assertRaisesRegex(installer.InstallError, '^refuse_to_replace_unexpected_current$'):
                    instance.switch(instance.new)
            self.assertEqual('unrelated-file', (root / 'backend/current').read_text())


if __name__ == '__main__':
    unittest.main()
