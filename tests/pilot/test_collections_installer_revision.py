"""Revisar sólo el instalador no permite cambiar ni recompilar el payload entregado."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PILOT = ROOT / 'scripts/pilot'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


R = load('collections_installer_repack', PILOT / 'repackage-collections-installer.py')
F = load('collections_installer_repack_fixtures', ROOT / 'tests/pilot/test_collections_payload.py')
REVISION_SOURCE = 'f' * 40


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


class CollectionsInstallerRevisionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.original_installer = R.git('show', R.ORIGINAL_SOURCE + ':scripts/pilot/install-collections-update.py').decode()
        self.original_wrapper = R.git('show', R.ORIGINAL_SOURCE + ':scripts/pilot/deploy-collections-from-wsl.py').decode()
        files = F.CollectionsPayloadTests().files()
        files['backend/appsettings.json'] = R.git('show', R.ORIGINAL_SOURCE + ':src/Prodimt.Pedidos.Api/appsettings.json')
        files['backend/appsettings.Production.json'] = R.git('show', R.ORIGINAL_SOURCE + ':src/Prodimt.Pedidos.Api/appsettings.Production.json')
        for name, path in R.SUPPORT_PATHS.items():
            files[name] = R.git('show', R.ORIGINAL_SOURCE + ':' + path)
        manifest = dict(feature='delivery-collections-v1', sourceCommit=R.ORIGINAL_SOURCE,
                        migration=F.I.MIGRATION, migrationsBefore=list(F.I.BASE_MIGRATIONS),
                        migrationsAfter=[*F.I.BASE_MIGRATIONS, F.I.MIGRATION],
                        basePair=[F.I.BASE_RELEASE, F.I.BASE_RELEASE],
                        files={name: dict(bytes=len(raw), sha256=sha(raw)) for name, raw in files.items()})
        self.payload = R.archive(files | {'manifest.json': json.dumps(manifest).encode()})
        # Los hashes sintéticos sólo se sustituyen en la prueba; los pines publicados no cambian.
        self.addCleanup(patch.stopall)
        patch.object(R, 'ORIGINAL_PAYLOAD_SHA256', sha(self.payload)).start()
        self.outer_files = {
            R.INSTALLER: R.pin_installer(self.original_installer),
            R.WRAPPER: self.original_wrapper.replace('REPLACE_SOURCE_COMMIT', R.ORIGINAL_SOURCE).encode(),
            R.PAYLOAD: self.payload, 'LEEME.txt': b'Synthetic installer revision test',
        }
        self.write_original()

    def write_original(self):
        files = dict(self.outer_files)
        files['SHA256SUMS.txt'] = ''.join(f'{sha(raw)}  {name}\n' for name, raw in sorted(files.items())).encode()
        raw = R.archive({R.PREFIX + name: value for name, value in files.items()})
        self.original = self.folder / 'original.zip'
        self.original.write_bytes(raw)
        patch.object(R, 'ORIGINAL_PACKAGE_SHA256', sha(raw)).start()
        return raw

    def test_production_pins_are_the_independently_verified_delivered_package(self):
        fresh = load('collections_installer_repack_pins', PILOT / 'repackage-collections-installer.py')
        self.assertEqual('ccc4193301043ec1935fb575d516319598fe94d0', fresh.ORIGINAL_SOURCE)
        self.assertEqual('848a41ded5510a0d5d63abf36f620c5bf37c65b6f69fe43d324fab295519d175', fresh.ORIGINAL_PACKAGE_SHA256)
        self.assertEqual('bbd4d3c91702ecc7249d896cf9ed1d12d687ef28e461d2caf09a55c49f7224e3', fresh.ORIGINAL_PAYLOAD_SHA256)

    def test_revision_keeps_payload_and_wrapper_exact_and_checksums_cover_revised_files(self):
        output = self.folder / 'PRODIMT_Cobranza_ccc419330104_v2.zip'
        with patch.object(R, 'validate_source') as source_gate:
            report = R.repackage(self.original, output, REVISION_SOURCE)
        self.assertEqual([((REVISION_SOURCE,),), ((REVISION_SOURCE,),)], [(call.args,) for call in source_gate.call_args_list])
        with zipfile.ZipFile(output) as archive:
            files = {name: archive.read(R.PREFIX + name) for name in R.CHECKED_FILES | {'SHA256SUMS.txt'}}
        self.assertEqual(self.payload, files[R.PAYLOAD])
        self.assertEqual(self.outer_files[R.WRAPPER], files[R.WRAPPER])
        self.assertEqual(R.pin_installer((PILOT / 'install-collections-update.py').read_text()), files[R.INSTALLER])
        self.assertIn(REVISION_SOURCE.encode(), files['LEEME.txt'])
        self.assertIn(R.ORIGINAL_SOURCE.encode(), files['LEEME.txt'])
        for line in files['SHA256SUMS.txt'].decode().splitlines():
            digest, name = line.split('  ')
            self.assertEqual(sha(files[name]), digest)
        self.assertTrue(report['payloadUnchanged'])
        self.assertFalse(report['runtimeRecompiled'])
        self.assertEqual(R.ORIGINAL_SOURCE, report['sourceCommit'])
        self.assertEqual(REVISION_SOURCE, report['installerRevision'])
        self.assertEqual(sha(self.payload), report['payloadSha256'])

    def test_original_outer_hash_is_checked_before_executing_the_loader(self):
        self.original.write_bytes(self.original.read_bytes() + b'tamper')
        with patch.object(R, 'validated_payload') as loader:
            with self.assertRaisesRegex(ValueError, '^original_package_hash_mismatch$'):
                R.read_outer(self.original.read_bytes())
        loader.assert_not_called()

    def test_missing_duplicate_and_wrong_original_checksums_are_rejected(self):
        raw = self.original.read_bytes()
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            files = {name: archive.read(name) for name in archive.namelist()}
        checksum_name = R.PREFIX + 'SHA256SUMS.txt'
        checksum = files[checksum_name]
        for replacement in (b'', checksum + checksum.splitlines(keepends=True)[0],
                            checksum.replace(checksum[:64], b'0' * 64, 1)):
            with self.subTest(checksum=replacement):
                changed = R.archive(files | {checksum_name: replacement})
                with patch.object(R, 'ORIGINAL_PACKAGE_SHA256', sha(changed)):
                    with self.assertRaises(ValueError):
                        R.read_outer(changed)

    def test_payload_tamper_is_rejected_even_with_updated_outer_checksums(self):
        self.outer_files[R.PAYLOAD] += b'tamper'
        self.write_original()
        with self.assertRaisesRegex(ValueError, '^original_payload_hash_mismatch$'):
            R.read_outer(self.original.read_bytes())

    def test_missing_active_asset_and_wrong_file_digest_are_rejected(self):
        with zipfile.ZipFile(io.BytesIO(self.payload)) as archive:
            files = {name: archive.read(name) for name in archive.namelist()}
        for changed in (files | {'frontend/lazy.js': b'import("./missing.js");'},
                        files | {'backend/Prodimt.Pedidos.Api.dll': b'changed'}):
            with self.subTest(changed=changed['frontend/lazy.js']):
                raw = R.archive(changed)
                with patch.object(R, 'ORIGINAL_PAYLOAD_SHA256', sha(raw)):
                    installer = R.pin_installer(self.original_installer)
                    with self.assertRaises(Exception):
                        R.validated_payload(installer, raw)
        # Una referencia faltante también se rechaza con el manifiesto rehasheado.
        files['frontend/lazy.js'] = b'import("./missing.js");'
        manifest = json.loads(files['manifest.json'])
        manifest['files']['frontend/lazy.js'] = dict(bytes=len(files['frontend/lazy.js']), sha256=sha(files['frontend/lazy.js']))
        files['manifest.json'] = json.dumps(manifest).encode()
        raw = R.archive(files)
        with patch.object(R, 'ORIGINAL_PAYLOAD_SHA256', sha(raw)):
            with self.assertRaisesRegex(Exception, '^frontend_asset_reference_missing$'):
                R.validated_payload(R.pin_installer(self.original_installer), raw)

    def test_wrapper_and_support_must_match_the_original_tracked_source(self):
        files = R.read_outer(self.original.read_bytes())
        for name in (R.INSTALLER, R.WRAPPER):
            with self.subTest(name=name):
                changed = dict(files)
                changed[name] += b'\n# drift\n'
                with self.assertRaisesRegex(ValueError, '^original_(installer|wrapper)_source_mismatch$'):
                    R.validate_original(changed)
        original_git = R.git
        def changed_source(*arguments):
            if arguments == ('show', R.ORIGINAL_SOURCE + ':scripts/pilot/inspect-native.py'):
                return b'unexpected support'
            return original_git(*arguments)
        with patch.object(R, 'git', side_effect=changed_source):
            with self.assertRaisesRegex(ValueError, '^payload_support_source_mismatch$'):
                R.validate_original(files)

    def source_values(self, **changes):
        values = {
            ('rev-parse', 'HEAD'): (REVISION_SOURCE + '\n').encode(),
            ('status', '--porcelain'): b'',
            ('rev-parse', 'refs/heads/main'): (REVISION_SOURCE + '\n').encode(),
            ('merge-base', R.ORIGINAL_SOURCE, REVISION_SOURCE): (R.ORIGINAL_SOURCE + '\n').encode(),
            ('diff', '--name-only', '--no-renames', '-z', R.ORIGINAL_SOURCE, REVISION_SOURCE):
                b'scripts/pilot/install-collections-update.py\0scripts/pilot/repackage-collections-installer.py\0tests/pilot/test_collections_installer_revision.py\0docs/validation/revision.md\0AGENTS.md\0.github/workflows/ci.yml\0',
        }
        values.update(changes)
        return values

    def test_revision_requires_current_clean_main_and_descendant_source(self):
        values = self.source_values()
        with patch.object(R, 'git', side_effect=lambda *arguments: values[arguments]):
            R.validate_source(REVISION_SOURCE)
        cases = [
            (('rev-parse', 'HEAD'), b'0' * 40, 'source_commit_not_current'),
            (('status', '--porcelain'), b' M file', 'source_tree_not_clean'),
            (('rev-parse', 'refs/heads/main'), b'0' * 40, 'source_commit_not_main'),
            (('merge-base', R.ORIGINAL_SOURCE, REVISION_SOURCE), b'0' * 40, 'installer_revision_not_descendant'),
        ]
        for key, value, issue in cases:
            with self.subTest(issue=issue):
                values = self.source_values(**{}) | {key: value}
                with patch.object(R, 'git', side_effect=lambda *arguments: values[arguments]):
                    with self.assertRaisesRegex(ValueError, '^' + issue + '$'):
                        R.validate_source(REVISION_SOURCE)

    def test_runtime_migration_wrapper_builder_and_vendor_changes_require_rebuild(self):
        key = ('diff', '--name-only', '--no-renames', '-z', R.ORIGINAL_SOURCE, REVISION_SOURCE)
        for path in ('src/Prodimt.Pedidos.Api/Program.cs', 'apps/prodimt-pedidos-web/src/main.ts',
                     'src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations/New.cs',
                     'scripts/pilot/inspect-native.py', 'scripts/pilot/install-quick-capture-update.py',
                     'scripts/pilot/deploy-collections-from-wsl.py', 'scripts/pilot/build-collections-package.py',
                     'vendor/runtime.dll', 'docs/../src/escape.cs'):
            with self.subTest(path=path):
                values = self.source_values() | {key: ('scripts/pilot/install-collections-update.py\0' + path + '\0').encode()}
                with patch.object(R, 'git', side_effect=lambda *arguments: values[arguments]):
                    with self.assertRaisesRegex(ValueError, '^runtime_source_changed_rebuild_required$'):
                        R.validate_source(REVISION_SOURCE)


if __name__ == '__main__':
    unittest.main()
