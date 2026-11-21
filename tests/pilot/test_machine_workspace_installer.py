"""Machine-workspace package boundaries and incremental migration contracts."""
import hashlib
import importlib.util
import io
import inspect
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PILOT = ROOT / 'scripts/pilot'
spec = importlib.util.spec_from_file_location('machine_installer_contract', PILOT / 'install-machine-workspace-update.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)
SOURCE = 'a' * 40


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def migration_sql():
    return ('BEGIN TRANSACTION;\n' +
            ''.join('CREATE TABLE [' + table + '] ([Id] int);\n' for table in installer.TABLES) +
            'ALTER TABLE [ProductionAllocations] ADD [ReleasedQuantity] decimal(18,2) NOT NULL DEFAULT 0;\n' +
            'INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N\'' + installer.MIGRATION + "', N'10.0.8');\nCOMMIT;\n")


def archive_bytes(files, special=None):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in files.items():
            entry = zipfile.ZipInfo(name)
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = (special if special is not None else stat.S_IFREG | 0o644) << 16
            archive.writestr(entry, raw)
    return stream.getvalue()


class MachineWorkspaceInstallerTests(unittest.TestCase):
    def files(self):
        files = {
            'inspect-native.py': (PILOT / 'inspect-native.py').read_bytes(),
            'native-deployment-support.py': (PILOT / 'install-quick-capture-update.py').read_bytes(),
            'frontend-asset-support.py': (PILOT / 'install-delivery-flow-update.py').read_bytes(),
            'migration.sql': migration_sql().encode(),
            'backend/appsettings.json': b'{}',
            'backend/Prodimt.Pedidos.Api.runtimeconfig.json': b'{}',
            'backend/Prodimt.Pedidos.Api.deps.json': b'{}',
            'frontend/index.html': b'<script src="main.js"></script>',
            'frontend/main.js': b'import("./lazy.js");',
            'frontend/lazy.js': b'console.log("fixture");',
            'frontend/3rdpartylicenses.txt': b'fixture licenses',
        }
        files.update({'backend/' + name: b'fixture binary' for name in installer.ASSEMBLIES})
        return files

    def package(self, folder, changes=None, metadata_changes=None, special=None):
        files = self.files()
        files.update(changes or {})
        before = ['20260101%06d_Test%02d' % (index, index) for index in range(20)]
        manifest = dict(feature='machine-workspace-v1', sourceCommit=SOURCE, migration=installer.MIGRATION,
                        migrationsBefore=before, migrationsAfter=before + [installer.MIGRATION],
                        basePair=['delivery-auto-89ed72778f30', 'frontend-759c68f32198'],
                        files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
        manifest.update(metadata_changes or {})
        raw = archive_bytes(files | {'manifest.json': json.dumps(manifest).encode()}, special)
        payload = folder / 'payload.zip'
        payload.write_bytes(raw)
        return payload, digest(raw)

    def load(self, path, payload_hash):
        with patch.object(installer, 'EXPECTED_SOURCE', SOURCE), \
                patch.object(installer, 'EXPECTED_PAYLOAD_SHA256', payload_hash), \
                patch.object(installer, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()):
            return installer.load_payload(path)

    def test_valid_package_checks_pinned_contents_and_asset_closure(self):
        with tempfile.TemporaryDirectory() as folder:
            payload, payload_hash = self.package(Path(folder))
            files, manifest = self.load(payload, payload_hash)
        self.assertEqual(self.files(), files)
        self.assertEqual(SOURCE, manifest['sourceCommit'])
        self.assertEqual(21, len(manifest['migrationsAfter']))

    def test_tamper_is_rejected_before_support_code_runs(self):
        with tempfile.TemporaryDirectory() as folder:
            payload, payload_hash = self.package(Path(folder))
            payload.write_bytes(payload.read_bytes() + b'tampered')
            with patch.object(installer, 'module') as execute_support:
                with self.assertRaisesRegex(installer.InstallError, '^payload_hash_mismatch$'):
                    self.load(payload, payload_hash)
                execute_support.assert_not_called()

    def test_private_payload_and_unsafe_archive_entries_are_rejected(self):
        for name in ('../escape', '/absolute', 'backend/../escape', 'backend//double',
                     'backend/.env', 'backend/private.key', 'backend/appsettings.Development.json'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as folder:
                payload, payload_hash = self.package(Path(folder), {name: b'fixture'})
                with self.assertRaises(installer.InstallError):
                    self.load(payload, payload_hash)
        with tempfile.TemporaryDirectory() as folder:
            payload, payload_hash = self.package(Path(folder), special=stat.S_IFLNK | 0o777)
            with self.assertRaisesRegex(installer.InstallError, '^payload_path_invalid$'):
                self.load(payload, payload_hash)

    def test_manifest_source_migration_and_file_digests_must_match(self):
        for changes in ({'sourceCommit': 'b' * 40}, {'migration': 'wrong'},
                        {'migrationsBefore': []}, {'migrationsAfter': []}, {'files': {}}):
            with self.subTest(changes=changes), tempfile.TemporaryDirectory() as folder:
                payload, payload_hash = self.package(Path(folder), metadata_changes=changes)
                with self.assertRaises(installer.InstallError):
                    self.load(payload, payload_hash)

    def test_missing_lazy_module_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            payload, payload_hash = self.package(Path(folder), {'frontend/lazy.js': b'import("./missing.js");'})
            with self.assertRaisesRegex(Exception, '^frontend_asset_reference_missing$'):
                self.load(payload, payload_hash)

    def test_only_one_transaction_for_the_requested_additive_migration_is_accepted(self):
        installer.validate_migration(migration_sql())
        sql = migration_sql()
        for invalid in (sql.replace('BEGIN TRANSACTION;', ''), sql + '\nCOMMIT;',
                        sql.replace(installer.MIGRATION, 'WrongMigration'),
                        sql.replace('CREATE TABLE [MachineWorkers]', 'CREATE TABLE [WrongWorkers]'),
                        sql + '\nDROP TABLE [Orders];', sql + '\nDELETE FROM [Orders];',
                        sql + '\nALTER LOGIN [app] DISABLE;'):
            with self.subTest(sql=invalid):
                with self.assertRaises(installer.InstallError):
                    installer.validate_migration(invalid)

    def test_schema_constraint_name_matches_the_actual_ef_migration(self):
        migration = ROOT / 'src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations' / (installer.MIGRATION + '.cs')
        expected = 'CK_ProductionAllocation_Release'
        self.assertIn('name: "' + expected + '"', migration.read_text(encoding='utf-8-sig'))
        self.assertIn(expected, installer.schema_query())

    def test_permissions_preserve_append_only_machine_journals(self):
        permissions = installer.expected_permissions()
        self.assertEqual(1, permissions['MachineOperators_DELETE'])
        self.assertEqual(1, permissions['MachineTeamTemplates_UPDATE'])
        self.assertEqual(0, permissions['MachineTeamTemplates_DELETE'])
        for table in ('MachineWorkers', 'MachineTeamChanges', 'ProductionLotHandoffs', 'MachineProductionNotices'):
            self.assertEqual(0, permissions[table + '_UPDATE'])
            self.assertEqual(0, permissions[table + '_DELETE'])

    def prepared_installer(self):
        files = self.files()
        manifest = dict(basePair=['old-backend', 'old-frontend'], migrationsBefore=['baseline'],
                        migrationsAfter=['baseline', installer.MIGRATION])
        with patch.object(installer, 'load_payload', return_value=(files, manifest)):
            instance = installer.make_installer(Path('fixture.zip'))
        runtime = inspect.getclosurevars(instance.apply).nonlocals['runtime']
        return instance, runtime, manifest

    def test_preflight_rejection_never_stops_stages_or_switches(self):
        instance, runtime, _ = self.prepared_installer()
        with patch.object(instance, 'preflight', side_effect=installer.InstallError('base_release_unexpected')), \
                patch.object(runtime, 'command') as command, patch.object(instance, 'stage') as stage, \
                patch.object(instance, 'switch') as switch:
            with self.assertRaisesRegex(installer.InstallError, '^base_release_unexpected$'):
                instance.apply()
            instance.rollback()
        command.assert_not_called()
        stage.assert_not_called()
        switch.assert_not_called()

    def test_verified_migration_recovery_checks_backup_and_skips_ddl(self):
        instance, runtime, manifest = self.prepared_installer()
        events = []
        instance.recovery = {'fixture': True}
        instance.migration_verified = True
        instance.environment = b'fixture environment'
        instance.configuration = {}
        instance.process_environment = {}
        instance.argv = ['/usr/bin/dotnet', 'Prodimt.Pedidos.Api.dll']
        instance.old = {part: Path('/fixture/old') / part for part in instance.new}
        with tempfile.TemporaryDirectory() as folder:
            instance.snapshot = Path(folder)
            instance.marker = Path(folder) / 'pending.json'
            instance.marker.write_text('{}')
            with patch.object(instance, 'preflight'), \
                    patch.object(runtime, 'prompt_secret', side_effect=['fixture-admin', 'fixture-password']), \
                    patch.object(instance, 'sql_json', return_value=dict(database_name='ProdimtPedidos', view_definition=1,
                        sysadmin=1, files_mib=32)), \
                    patch.object(instance, 'history', return_value=manifest['migrationsAfter']), \
                    patch.object(instance, 'verify_backup', side_effect=lambda: events.append('verify-backup')), \
                    patch.object(instance, 'database_backup') as create_backup, \
                    patch.object(instance, 'stage', side_effect=lambda: events.append('stage')), \
                    patch.object(instance, 'verify_stage'), \
                    patch.object(installer, 'regular', return_value=instance.environment), \
                    patch.object(instance.inspector, 'configuration', return_value=({}, {'content_root': str(instance.new['backend'])})), \
                    patch.object(instance, 'write_marker', side_effect=lambda: events.append('marker')), \
                    patch.object(runtime, 'command', side_effect=lambda args, **kwargs: events.append(args[1])), \
                    patch.object(instance, 'sql_run') as ddl, \
                    patch.object(instance, 'switch', side_effect=lambda _: events.append('activate')), \
                    patch.object(instance, 'health', side_effect=lambda _: events.append('health')):
                instance.apply()
            create_backup.assert_not_called()
            ddl.assert_not_called()
            self.assertEqual(['verify-backup', 'stage', 'marker', 'stop', 'activate', 'start', 'health'], events)
            self.assertEqual('updated', instance.state['status'])
            self.assertFalse(instance.state['recovery_pending'])
            self.assertFalse(instance.marker.exists())

    def test_failure_after_ddl_keeps_recovery_and_does_not_restore_old_application(self):
        instance, runtime, manifest = self.prepared_installer()
        instance.stop_requested = True
        with patch.object(runtime, 'command') as command, \
                patch.object(instance, 'history', return_value=manifest['migrationsAfter']), \
                patch.object(instance, 'switch') as switch, patch.object(instance, 'health') as health:
            instance.rollback()
        command.assert_called_once()
        switch.assert_not_called()
        health.assert_not_called()
        self.assertTrue(instance.state['recovery_pending'])
        self.assertTrue(instance.state['application_stopped_for_recovery'])

    def test_interrupted_before_ddl_reuses_verified_backup_metadata(self):
        instance, runtime, _ = self.prepared_installer()
        instance.database_mib = 1
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            sql_root = root / 'sql-backups'
            sql_root.mkdir()
            instance.snapshot = root / 'snapshot'
            instance.snapshot.mkdir()
            metadata = instance.snapshot / 'database-backup.json'
            metadata.write_text('{"fixture":true}')
            original_stat = Path.stat

            def observed_owner(path, *args, **kwargs):
                value = original_stat(path, *args, **kwargs)
                if path == sql_root:
                    fields = list(value)
                    fields[4], fields[5] = 999, 988
                    return os.stat_result(fields)
                return value

            with patch.object(runtime, 'SQL_BACKUPS', sql_root), \
                    patch.object(Path, 'stat', observed_owner), \
                    patch.object(instance, 'verify_backup') as verify_existing, \
                    patch.object(instance, 'sql_run') as sql:
                instance.database_backup()
            verify_existing.assert_called_once_with()
            sql.assert_not_called()
            self.assertEqual([], list(sql_root.iterdir()))
            self.assertEqual('{"fixture":true}', metadata.read_text())


if __name__ == '__main__':
    unittest.main()
