"""Guard the C1 package boundary: exact source, binaries, SQL and no private config."""
import importlib.util
import io
import json
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


builder = load('c1_package_builder_tests', ROOT / 'scripts/pilot/build-consolidation-c1-package.py')
installer = load('c1_package_installer_tests', ROOT / 'scripts/pilot/install-consolidation-c1.py')
validation = load('c1_package_sql_tests', ROOT / 'scripts/pilot/validate-consolidation-c1-sql.py')


def upgrade():
    return ("BEGIN TRANSACTION;\nIF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + builder.MIGRATION + "')\nBEGIN\n"
            'ALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(max) NULL;\nEND;\nGO\n\n'
            "IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + builder.MIGRATION + "')\nBEGIN\n"
            "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\nVALUES (N'" + builder.MIGRATION + "', N'10.0.8');\nEND;\nGO\n\nCOMMIT;\nGO\n\n")


def downgrade():
    return ("BEGIN TRANSACTION;\nIF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + builder.MIGRATION + "')\nBEGIN\n"
            'IF EXISTS (SELECT 1 FROM [AuditLogs] WHERE DATALENGTH([MetadataJson]) > 4000)\n'
            "THROW 51024, '" + validation.DOWNGRADE_MARKER + "', 1;\n"
            'ALTER TABLE [AuditLogs] ALTER COLUMN [MetadataJson] nvarchar(2000) NULL;\nEND;\nGO\n\n'
            "DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'" + builder.MIGRATION + "';\nGO\n\nCOMMIT;\nGO\n\n")


def baseline():
    return ''.join("BEGIN TRANSACTION;\nINSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\nVALUES (N'" + value + "', N'10.0.8');\nCOMMIT;\nGO\n\n" for value in validation.BASE_IDS)


def fresh_token_sql():
    """Reproduce EF fresh output independently from its reviewed I12 fixture.

    The fixture has five idempotent guards. Removing only those guards and
    their four-space indentation yields the real non-idempotent EF layout.
    """
    original = (ROOT / 'tests/pilot/fixtures/i12-original-migration.sql').read_text(encoding='utf-8-sig')
    guard = ("IF NOT EXISTS (\n    SELECT * FROM [__EFMigrationsHistory]\n"
             "    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'\n)\nBEGIN\n")
    bodies = re.findall(re.escape(guard) + r'(.*?)\nEND;', original, re.S)
    if len(bodies) != 5:
        raise AssertionError('reviewed I12 fixture changed')
    bodies = ['\n'.join(line.removeprefix('    ') for line in body.splitlines()) for body in bodies]
    return 'BEGIN TRANSACTION;\n' + '\n\n'.join(bodies) + '\n\nCOMMIT;\nGO\n\n'


class ConsolidationC1PackageTests(unittest.TestCase):
    def test_source_production_settings_are_allowed_without_accepting_local_overrides(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            template = (ROOT / 'src/Prodimt.Pedidos.Api/appsettings.Production.json').read_bytes()
            (directory / 'appsettings.Production.json').write_bytes(template)
            files = {}
            builder.collect_runtime(files, 'backend', directory)
            self.assertEqual(files, {'backend/appsettings.Production.json': template})
            (directory / 'appsettings.Production.json').write_bytes(b'{"ConnectionStrings":{"db":"local-only"}}')
            with self.assertRaisesRegex(ValueError, 'runtime_settings_not_source_template'):
                builder.collect_runtime({}, 'backend', directory)

    def test_fresh_ef_baseline_splits_token_and_schedule_ddl_without_changing_statements(self):
        raw = (ROOT / 'tests/pilot/fixtures/i22-validation-baseline.sql').read_text(encoding='utf-8-sig')
        token_blocks = [match for match in re.finditer(r'(?ms)^BEGIN TRANSACTION;\n.*?^COMMIT;\nGO\n\n', raw)
                        if re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", match.group()) ==
                        ['20261101190000_AddRecoverableCustomerAccessTokens']]
        self.assertEqual(len(token_blocks), 1)
        token = token_blocks[0]
        raw = raw[:token.start()] + fresh_token_sql() + raw[token.end():]
        # Reproduce fresh EF's other ADD-column operations independently from
        # the normalizer, including ExternalCode before its filtered index.
        column_guard = (r"IF NOT EXISTS \(\n    SELECT \* FROM \[__EFMigrationsHistory\]\n"
                        r"    WHERE \[MigrationId\] = N'[^']+'\n\)\nBEGIN\n    "
                        r'(ALTER TABLE \[[^]]+\] ADD \[[^]]+\] [^;\n]+;)\nEND;')
        raw = re.sub(column_guard, r'\1', raw)
        raw += (ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text(encoding='utf-8-sig')
        raw += (ROOT / 'tests/pilot/fixtures/i22-ordering-schedules-migration.sql').read_text(encoding='utf-8-sig').rstrip() + '\n\n'
        normalized = builder.normalize_baseline(raw)
        validation.validate_baseline(normalized)
        self.assertEqual(normalized.count('BEGIN TRANSACTION;'), 18)
        self.assertEqual(normalized.count('COMMIT;'), 18)
        self.assertIn('GO\n\nDECLARE @revoked TABLE', normalized)
        self.assertIn('ALTER TABLE [Products] ADD [ExternalCode] nvarchar(80) NULL;\nGO\n', normalized)
        self.assertIn('ALTER TABLE [Customers] ADD [RestDaysMask] int NOT NULL DEFAULT 0;\nGO\n', normalized)
        self.assertIn('ALTER TABLE [CustomerFrequentProducts] ADD [CaptureAlias] nvarchar(16) NULL;\nGO\n', normalized)
        self.assertIn('GO\n\nALTER TABLE [Customers] ADD CONSTRAINT [CK_Customers_AutomaticConfirmationCutoff]', normalized)
        def canonical(sql):
            return re.sub(r'\s+', ' ', re.sub(r'(?m)^GO\s*$', '', sql)).strip()
        self.assertEqual(canonical(normalized), canonical(raw))

    def test_column_batch_split_refuses_a_missing_or_unreviewed_addition(self):
        sql = ''.join('ALTER TABLE [' + table + '] ADD [' + column + '] int NULL;\n'
                      for table, column in builder.BASELINE_ADDED_COLUMNS)
        self.assertEqual(builder.split_raw_column_additions(sql).count('GO\n'), 44)
        for changed in (sql.replace('[Products] ADD [ExternalCode]', '[Products] ADD [UnknownColumn]'),
                        sql.removesuffix('ALTER TABLE [Customers] ADD [OrderingScheduleVersion] int NULL;\n')):
            with self.assertRaisesRegex(ValueError, 'validation_baseline_columns_unexpected'):
                builder.split_raw_column_additions(changed)

    def test_raw_token_baseline_refuses_unreviewed_column_or_revocation_changes(self):
        raw = fresh_token_sql()
        for changed in (raw.replace('DEFAULT 1;', 'DEFAULT 2;'),
                        raw.replace('SET IsActive=0', 'SET IsActive=1'),
                        raw.replace('COMMIT;', 'COMMIT;\nDROP TABLE [Orders];')):
            with self.subTest(changed=changed[-100:]), self.assertRaisesRegex(ValueError, 'validation_raw_token_layout_unexpected'):
                builder.normalize_raw_token_baseline(changed)

    def inputs(self, root):
        backend, frontend, portable = [root / name for name in ('backend', 'frontend', 'validation')]
        for directory in (backend, frontend, portable):
            directory.mkdir()
        for name in installer.VALIDATED_ASSEMBLIES:
            (backend / name).write_bytes(b'same-reviewed-assembly-' + name.encode())
            (portable / name).write_bytes((backend / name).read_bytes())
        (backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
        (backend / 'appsettings.json').write_bytes((ROOT / 'src/Prodimt.Pedidos.Api/appsettings.json').read_bytes())
        for name in installer.VALIDATION_REQUIRED:
            (portable / Path(name).name).write_text('{}')
        (frontend / 'index.html').write_text('<html></html>')
        (frontend / '3rdpartylicenses.txt').write_text('Synthetic notices')
        (frontend / 'main.js').write_bytes(b' '.join(builder.FRONTEND_MARKERS))
        up, down, base = [root / name for name in ('up.sql', 'down.sql', 'base.sql')]
        for path, text in ((up, upgrade()), (down, downgrade()), (base, baseline())):
            path.write_text(text)
        return backend, frontend, portable, up, down, base

    def build_fixture(self, root, inputs=None, output='package.zip', browser_evidence=None):
        inputs = inputs or self.inputs(root)
        with patch.object(builder, 'validate_source'), patch.object(builder, 'source_bundle', return_value=b'synthetic-git-bundle'), \
             patch.object(builder, 'normalize_baseline', side_effect=lambda value: value):
            result = builder.build(*inputs, root / output, 'a' * 40, browser_evidence)
        return result, root / output

    def browser_evidence(self, **changes):
        gate = dict(schema='prodimt.c1.browser-gate.v1', status='passed', sourceCommit='a' * 40,
                    api='mock', sqlServerIncluded=False, humanAcceptance=False, suite='all',
                    expectedCases=240, collected=240, passed=240, failed=0, skipped=0, flaky=0,
                    exitCode=0, reportSha256='b' * 64,
                    recordedAtUtc='2026-11-04T20:00:00.000Z', nodeVersion='v24.0.0',
                    completedStages=['npm-web', 'npm-e2e', 'angular-build', 'components', 'typecheck', 'browser-suite'],
                    lastStage='completed')
        return gate | changes

    def test_same_source_pass_evidence_is_identical_outer_documentation_without_payload_changes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            evidence = root / 'gate.json'
            raw = (json.dumps(self.browser_evidence(), indent=2) + '\n').encode()
            evidence.write_bytes(raw)
            plain_result, plain_path = self.build_fixture(root, inputs)
            documented_result, documented_path = self.build_fixture(root, inputs, 'documented.zip', evidence)
            self.assertFalse(plain_result['browserEvidenceIncluded'])
            self.assertTrue(documented_result['browserEvidenceIncluded'])
            self.assertFalse(documented_result['deployed'])
            self.assertTrue(documented_result['browserGateRequired'])
            self.assertEqual(plain_result['payloadSha256'], documented_result['payloadSha256'])
            with zipfile.ZipFile(plain_path) as plain, zipfile.ZipFile(documented_path) as documented:
                self.assertEqual(plain.read(builder.PREFIX + 'prodimt-consolidation-c1-payload.zip'),
                                 documented.read(builder.PREFIX + 'prodimt-consolidation-c1-payload.zip'))
                self.assertEqual(plain.read(builder.PREFIX + 'PRODIMT_Instalar_C1.py'),
                                 documented.read(builder.PREFIX + 'PRODIMT_Instalar_C1.py'))
                self.assertEqual(plain.read(builder.PREFIX + 'PRODIMT_Desplegar_C1_WSL.sh'),
                                 documented.read(builder.PREFIX + 'PRODIMT_Desplegar_C1_WSL.sh'))
                self.assertEqual(documented.read(builder.PREFIX + 'BROWSER_C1_VALIDACION.json'), raw)
                summary = documented.read(builder.PREFIX + 'BROWSER_C1_VALIDACION.md').decode()
                self.assertIn('a' * 40, summary)
                self.assertIn('240 de 240', summary)
                self.assertIn('no incluye SQL Server, aceptación humana ni despliegue', summary)
                sums = documented.read(builder.PREFIX + 'SHA256SUMS.txt').decode().splitlines()
                for name in ('BROWSER_C1_VALIDACION.json', 'BROWSER_C1_VALIDACION.md'):
                    self.assertIn(builder.digest(documented.read(builder.PREFIX + name)) + '  ' + name, sums)

    def test_browser_evidence_rejects_other_sources_failed_partial_or_different_persistence(self):
        changes = (
            dict(sourceCommit='c' * 40), dict(schema='other.v1'), dict(status='failed'),
            dict(api='real'), dict(suite='focal'), dict(expectedCases=239), dict(collected=239),
            dict(passed=239), dict(failed=1), dict(skipped=1), dict(flaky=1), dict(exitCode=1),
            dict(sqlServerIncluded=True), dict(humanAcceptance=True), dict(reportSha256='bad'),
            dict(failed=False), dict(exitCode=False), dict(passed=240.0), dict(skipped='0'),
            dict(unexpectedPrivateField='must-not-be-packaged'),
        )
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'gate.json'
            for change in changes:
                path.write_text(json.dumps(self.browser_evidence(**change)))
                with self.subTest(change=change), self.assertRaisesRegex(ValueError, 'browser_evidence_incomplete_or_source_mismatch'):
                    builder.read_browser_evidence(path, 'a' * 40)

    def test_browser_evidence_rejects_duplicate_fields_malformed_json_and_symlinks(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / 'gate.json'
            for raw in (b'{"status":"failed","status":"passed"}', b'{"unknown":NaN}', b'not-json', b'\xff'):
                path.write_bytes(raw)
                with self.subTest(raw=raw), self.assertRaisesRegex(ValueError, 'browser_evidence_unrecognized'):
                    builder.read_browser_evidence(path, 'a' * 40)
            path.write_text(json.dumps(self.browser_evidence()))
            linked = root / 'linked.json'
            linked.symlink_to(path)
            with self.assertRaisesRegex(ValueError, 'browser_evidence_file_unexpected'):
                builder.read_browser_evidence(linked, 'a' * 40)

    def test_invalid_browser_evidence_cannot_produce_a_package(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            evidence = root / 'gate.json'
            evidence.write_text(json.dumps(self.browser_evidence(skipped=1)))
            with self.assertRaisesRegex(ValueError, 'browser_evidence_incomplete_or_source_mismatch'):
                self.build_fixture(root, inputs, browser_evidence=evidence)
            self.assertFalse((root / 'package.zip').exists())

    def test_export_pins_own_payload_exact_baseline_and_source_and_is_deterministic(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            first, first_path = self.build_fixture(root, inputs)
            second, second_path = self.build_fixture(root, inputs, 'second.zip')
            self.assertEqual(first_path.read_bytes(), second_path.read_bytes())
            self.assertEqual(first['sha256'], second['sha256'])
            self.assertFalse(first['deployed'])
            with zipfile.ZipFile(first_path) as outer:
                names = {name.removeprefix(builder.PREFIX) for name in outer.namelist()}
                self.assertEqual(names, {'PRODIMT_Instalar_C1.py', 'prodimt-consolidation-c1-payload.zip',
                    'PRODIMT_Desplegar_C1_WSL.sh', 'PRODIMT_C1.bundle', 'source.json', 'LEEME.txt', 'SHA256SUMS.txt'})
                script = outer.read(builder.PREFIX + 'PRODIMT_Instalar_C1.py')
                payload = outer.read(builder.PREFIX + 'prodimt-consolidation-c1-payload.zip')
                source = json.loads(outer.read(builder.PREFIX + 'source.json'))
                self.assertEqual(source['sourceCommit'], 'a' * 40)
                self.assertEqual(source['baselineSource'], builder.BASELINE_SOURCE)
                self.assertNotIn(b'REPLACE_SOURCE_COMMIT', script)
                self.assertIn(builder.digest(payload).encode(), script)
                sums = outer.read(builder.PREFIX + 'SHA256SUMS.txt').decode().splitlines()
                for row in sums:
                    sha, name = row.split('  ', 1)
                    self.assertEqual(sha, builder.digest(outer.read(builder.PREFIX + name)))
            with zipfile.ZipFile(io.BytesIO(payload)) as inner:
                manifest = json.loads(inner.read('manifest.json'))
                self.assertEqual(manifest['migrationIds'], validation.BASE_IDS + [builder.MIGRATION])
                self.assertEqual(manifest['previousPairs'], [list(pair) for pair in builder.PREVIOUS_PAIRS])
                self.assertFalse(manifest['revokeTokens'])
                self.assertFalse(manifest['encryptionKeyInPackage'])

    def test_exported_installer_accepts_only_the_hash_pinned_payload(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            result, path = self.build_fixture(root)
            with zipfile.ZipFile(path) as archive:
                exported = root / 'exported.py'
                exported.write_bytes(archive.read(builder.PREFIX + 'PRODIMT_Instalar_C1.py'))
                payload = root / 'payload.zip'
                payload.write_bytes(archive.read(builder.PREFIX + 'prodimt-consolidation-c1-payload.zip'))
            exported_module = load('c1_exported_installer_test', exported)
            with patch.object(exported_module, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()):
                self.assertEqual(len(exported_module.load_payload(payload)), result['files'])
                payload.write_bytes(payload.read_bytes() + b'corrupted')
                with self.assertRaisesRegex(exported_module.InstallError, 'payload_hash_mismatch'):
                    exported_module.load_payload(payload)

    def test_different_application_binary_cannot_be_validated_then_installed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            (inputs[2] / installer.VALIDATED_ASSEMBLIES[0]).write_bytes(b'older-binary')
            with self.assertRaisesRegex(ValueError, 'validation_application_binary_mismatch'):
                self.build_fixture(root, inputs)
            self.assertFalse((root / 'package.zip').exists())

    def test_missing_browser_or_same_origin_feature_marker_blocks_package(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            main = inputs[1] / 'main.js'
            for marker in (b'apiBaseUrl:""', b'prodimt.production.pending.v1.', b'/ordering-schedules', b'Preparar reparto'):
                main.write_bytes(b' '.join(value for value in builder.FRONTEND_MARKERS if value != marker))
                with self.subTest(marker=marker), self.assertRaisesRegex(ValueError, 'frontend_feature_or_same_origin_missing'):
                    self.build_fixture(root, inputs)

    def test_runtime_configuration_must_equal_the_source_template(self):
        for name, raw in (('appsettings.json', b'{"ConnectionStrings":{"db":"private-value"}}'),
                          ('appsettings.Production.json', b'{}'),
                          ('nested/appsettings.json', b'{}')):
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                inputs = self.inputs(root)
                target = inputs[0] / name
                target.parent.mkdir(exist_ok=True)
                target.write_bytes(raw)
                with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'runtime_settings_not_source_template'):
                    self.build_fixture(root, inputs)

    def test_private_source_files_and_symlinks_are_never_packaged(self):
        for name in ('api.env', 'private.pem', 'backup.bak', 'source.cs', '.private.json'):
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                inputs = self.inputs(root)
                (inputs[0] / name).write_text('synthetic-private')
                with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'build_private_or_source_file_not_allowed'):
                    self.build_fixture(root, inputs)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            (inputs[0] / 'linked.json').symlink_to(inputs[0] / 'appsettings.json')
            with self.assertRaisesRegex(ValueError, 'build_symlink_not_allowed'):
                self.build_fixture(root, inputs)

    def test_missing_portable_validation_runtime_is_not_a_deployable_package(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            (inputs[2] / Path(installer.VALIDATION_REQUIRED[0]).name).unlink()
            with self.assertRaisesRegex(ValueError, 'required_build_files_missing'):
                self.build_fixture(root, inputs)

    def test_wrong_sql_target_is_rejected_before_any_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inputs = self.inputs(root)
            inputs[3].write_text(upgrade().replace('[AuditLogs]', '[Orders]'))
            with self.assertRaisesRegex(Exception, 'validation_delta_targets_unexpected'):
                self.build_fixture(root, inputs)
            self.assertFalse((root / 'package.zip').exists())

    def test_source_mismatch_dirty_tree_and_wrong_branch_are_rejected(self):
        for replies, issue in ((['b' * 40], 'source_commit_not_current'),
                               (['a' * 40, ' M dirty.py'], 'source_tree_not_clean'),
                               (['a' * 40, '', 'unrelated'], 'source_branch_unexpected')):
            with patch.object(builder.subprocess, 'check_output', side_effect=replies):
                with self.subTest(issue=issue), self.assertRaisesRegex(ValueError, issue):
                    builder.validate_source('a' * 40)

    def test_source_bundle_is_a_real_recoverable_exact_revision(self):
        source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        raw = builder.source_bundle(source)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            bundle = root / 'source.bundle'
            bundle.write_bytes(raw)
            subprocess.run(['git', 'clone', '--quiet', '--branch', builder.SOURCE_BRANCH, str(bundle), str(root / 'recovered')],
                           check=True, capture_output=True)
            head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root / 'recovered', text=True).strip()
            self.assertEqual(head, source)
        with self.assertRaisesRegex(ValueError, 'bundle_source_not_current'):
            builder.source_bundle('0' * 40)


if __name__ == '__main__':
    unittest.main()
