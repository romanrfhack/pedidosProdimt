"""I12 native update: persistent private key, backed-up revocation and rollback."""
import base64
import importlib.util
import io
import json
from pathlib import Path
import stat
import subprocess
import tempfile
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/pilot' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


I = module('token_management_installer_fixture', 'install-access-token-management-update.py')
B = module('token_management_builder_fixture', 'build-access-token-management-package.py')
C = module('token_management_price_fixture', 'install-commercial-catalog-update.py')
SOURCE = 'a' * 40
KEY = base64.b64encode(bytes(range(32))).decode('ascii')
OLD_ENV = b'ASPNETCORE_ENVIRONMENT=Production\nPRODIMT_JWT_SIGNING_KEY=Fictional_JWT_key_only\nOTHER_SETTING=unchanged'
COUNTS = dict(customers=23, products=7, profiles=26, orders=9, lines=15,
              administrators=2, audits=12, order_audits=9, machines=2)
MONETARY = dict(books=5, revisions=9, consolidations=2, priced_lines=7)


class AccessTokenManagementPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.backend, self.frontend = self.root / 'backend', self.root / 'frontend'
        self.backend.mkdir()
        self.frontend.mkdir()
        for name, raw in {'Prodimt.Pedidos.Api.dll': b'synthetic-assembly',
                          'Prodimt.Pedidos.Api.runtimeconfig.json': b'{}', 'appsettings.json': b'{}'}.items():
            (self.backend / name).write_bytes(raw)
        (self.frontend / 'index.html').write_text('<script src="main.js"></script>')
        (self.frontend / 'main.js').write_text('apiBaseUrl:""; /access-tokens /reveal expectedVersion /prices')
        (self.frontend / '3rdpartylicenses.txt').write_text('Synthetic fixture license notice')
        self.migration = self.root / 'migration.sql'
        self.migration.write_text("""-- ProtectedToken MetadataVersion IsReminderLink
UPDATE dbo.CustomerAccessTokens
 SET IsActive=0,MetadataVersion=MetadataVersion+1 OUTPUT inserted.Id INTO @revoked(Id)
 WHERE IsActive=1 AND ProtectedToken IS NULL;
INSERT INTO dbo.AuditLogs SELECT N'CustomerAccessTokenRevokedForRecoveryUpgrade';
INSERT INTO [__EFMigrationsHistory] VALUES (N'""" + B.MIGRATION + "', N'10.0.8');\n")
        self.bundle, self.payload = self.root / 'bundle.zip', self.root / 'payload.zip'
        self.addCleanup(patch.stopall)
        patch.object(B, 'validate_source').start()
        patch.object(I, 'EXPECTED_SOURCE', SOURCE).start()
        patch.object(I, 'ENV_FILE', self.root / 'api.env').start()
        patch.object(I, 'BACKUPS', self.root / 'backups').start()
        patch.object(I, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()).start()

    def build(self):
        result = B.build(self.backend, self.frontend, self.migration, self.bundle, SOURCE)
        with zipfile.ZipFile(self.bundle) as archive:
            self.payload.write_bytes(archive.read(B.PREFIX + 'prodimt-access-token-management-payload.zip'))
        patch.object(I, 'EXPECTED_PAYLOAD_SHA256', result['payloadSha256']).start()
        return result

    def instance(self):
        self.build()
        result = I.make_installer(self.payload)
        result.sql_tool, result.fields = 'synthetic-sqlcmd', {}
        result.inspector = types.SimpleNamespace(parse_sql_output=json.loads,
            query_sql=lambda tool, fields, query: dict(C.EXPECTED_PERMISSIONS, migrations_count=13)
                if query == C.PERMISSION_QUERY else C.EXPECTED_PRICE_PERMISSIONS.copy())
        return result

    def key_fixture(self, existing=False):
        result = self.instance()
        result.environment = OLD_ENV + (b'\n' + (I.KEY_ENV + '=' + KEY + '\n').encode() if existing else b'')
        I.ENV_FILE.write_bytes(result.environment)
        I.ENV_FILE.chmod(0o600)
        result.token_encryption_key = KEY if existing else None
        result.snapshot = self.root / 'snapshot'
        result.snapshot.mkdir(mode=0o700)
        result.state['verified_database_backup'] = '/synthetic/verified-backup.bak'
        patch.object(I, 'validate_environment_file', side_effect=lambda path:
            I.require(stat.S_IMODE(path.stat().st_mode) == 0o600 and not path.is_symlink(), 'environment_file_identity_unexpected')).start()
        patch.object(result.runtime, 'directory_safe').start()
        patch.object(I.os, 'chown').start()
        return result

    def test_package_pins_all_files_authorized_revocation_and_deterministic_bundle(self):
        report = self.build()
        self.assertEqual(report, self.build())
        with zipfile.ZipFile(self.bundle) as archive:
            script = archive.read(B.PREFIX + 'PRODIMT_Instalar_I12.py').decode()
            self.assertIn(report['payloadSha256'], script)
            self.assertIn(SOURCE, script)
            self.assertNotIn('REPLACE_', script)
            for row in archive.read(B.PREFIX + 'SHA256SUMS.txt').decode().splitlines():
                expected, name = row.split('  ', 1)
                self.assertEqual(expected, B.digest(archive.read(B.PREFIX + name)))
        files = I.load_payload(self.payload)
        self.assertIn('commercial-catalog-support.py', files)
        self.assertIn('migrations-i12.sql', files)
        self.assertFalse(any(name.endswith(('.env', '.key', '.pdb', '.map')) for name in files))
        with zipfile.ZipFile(self.payload) as archive:
            manifest = json.loads(archive.read('manifest.json'))
        self.assertTrue(manifest['revokeLegacyTokens'])
        self.assertFalse(manifest['encryptionKeyInPackage'])
        self.assertEqual((12, 13, I.PREVIOUS), (manifest['migrationsBefore'], manifest['migrationsAfter'], manifest['previousRelease']))

    def test_builder_rejects_packaged_key_and_unrelated_sql_writes(self):
        (self.backend / 'appsettings.json').write_text(json.dumps({'CustomerAccessTokens': {'EncryptionKey': KEY}}))
        with self.assertRaisesRegex(ValueError, 'encryption_key_must_not_be_packaged'):
            self.build()
        (self.backend / 'appsettings.json').write_text('{}')
        for mutation in ('UPDATE dbo.Customers SET Name=N\'unrelated\';', 'DELETE FROM dbo.AuditLogs;',
                         'EXEC unsafe;', 'TRUNCATE TABLE dbo.Orders;'):
            with self.subTest(mutation=mutation):
                with self.assertRaisesRegex(ValueError, 'incremental_migration_unexpected'):
                    B.validate_migration(self.migration.read_text() + '\n' + mutation)

    def test_payload_tamper_is_rejected_before_support_execution(self):
        self.build()
        self.payload.write_bytes(self.payload.read_bytes() + b'changed')
        with patch.object(I, 'exec', create=True) as execute:
            with self.assertRaisesRegex(I.InstallError, 'payload_hash_mismatch'):
                I.make_installer(self.payload)
        execute.assert_not_called()

    def test_existing_quoted_key_is_reused_and_invalid_or_duplicate_keys_are_rejected(self):
        self.assertEqual(KEY, I.environment_key(('OTHER=value\n' + I.KEY_ENV + '="' + KEY + '"\n').encode()))
        for value in ('', 'not-base64', base64.b64encode(b'short').decode(), KEY + ' '):
            with self.subTest(value=value), self.assertRaises(I.InstallError):
                I.canonical_key(value)
        with self.assertRaisesRegex(I.InstallError, 'duplicate_token_encryption_key'):
            I.environment_key(((I.KEY_ENV + '=' + KEY + '\n') * 2).encode())

    def test_private_environment_guard_rejects_symlink_and_non_private_mode(self):
        path = Mock()
        path.resolve.return_value = path
        for mode, owner in ((stat.S_IFLNK | 0o600, 0), (stat.S_IFREG | 0o640, 0), (stat.S_IFREG | 0o600, 33)):
            path.lstat.return_value = types.SimpleNamespace(st_mode=mode, st_uid=owner, st_nlink=1)
            with self.assertRaises(I.InstallError):
                I.validate_environment_file(path)
        path.lstat.return_value = types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_uid=0, st_nlink=1)
        I.validate_environment_file(path)

    def test_key_creation_requires_verified_backup_and_never_reuses_jwt_or_prints_secret(self):
        result = self.key_fixture()
        result.state.pop('verified_database_backup')
        with self.assertRaisesRegex(I.InstallError, 'verified_database_backup_required'):
            result.prepare_encryption_key()
        self.assertEqual(OLD_ENV, I.ENV_FILE.read_bytes())
        result.state['verified_database_backup'] = '/synthetic/verified-backup.bak'
        result.prepare_encryption_key()
        current = I.ENV_FILE.read_bytes()
        self.assertTrue(current.startswith(OLD_ENV + b'\n'))
        created = I.environment_key(current)
        self.assertEqual(32, len(base64.b64decode(created)))
        self.assertTrue(result.state['encryption_key_created'])
        self.assertFalse(result.state['encryption_key_rotated'])
        self.assertEqual(0o600, stat.S_IMODE(I.ENV_FILE.stat().st_mode))
        self.assertEqual(current, (result.snapshot / 'api.env.with-token-key').read_bytes())
        self.assertEqual(created + '\n', (result.snapshot / 'customer-access-token-encryption.key').read_text())
        self.assertTrue(all(stat.S_IMODE(path.stat().st_mode) == 0o600 for path in result.snapshot.iterdir()))
        self.assertNotIn(created, json.dumps(result.state))
        self.assertFalse(list(I.ENV_FILE.parent.glob('.api.env.i12-*')))

    def test_existing_key_is_never_rotated_or_rewritten(self):
        result = self.key_fixture(existing=True)
        previous = I.ENV_FILE.read_bytes()
        with patch.object(I.secrets, 'token_bytes', side_effect=AssertionError('must not generate')), patch.object(I.os, 'replace') as replace:
            result.prepare_encryption_key()
        replace.assert_not_called()
        self.assertEqual(previous, I.ENV_FILE.read_bytes())
        self.assertFalse(result.state['encryption_key_created'])
        self.assertNotIn(KEY, json.dumps(result.state))

    def test_environment_drift_fails_without_replacing_existing_contents(self):
        result = self.key_fixture()
        I.ENV_FILE.write_bytes(OLD_ENV + b'\noperator-change=value\n')
        with self.assertRaisesRegex(I.InstallError, 'environment_changed_before_key_persistence'):
            result.prepare_encryption_key()
        self.assertIsNone(I.environment_key(I.ENV_FILE.read_bytes()))
        self.assertFalse(list(result.snapshot.iterdir()))

    def test_effective_key_mismatch_blocks_success_even_if_environment_file_is_correct(self):
        result = self.key_fixture(existing=True)
        result.process = Mock(return_value=(123, Path('/synthetic/release'), ['dotnet', 'Prodimt.Pedidos.Api.dll']))
        result.inspector = types.SimpleNamespace(process_environment=lambda pid: {}, first=lambda cfg, key, default: cfg.get(key, default),
            configuration=lambda *args: ({I.KEY_CONFIG: 'different-private-key'}, {'environment': 'Production'}))
        with self.assertRaisesRegex(I.InstallError, 'token_encryption_key_not_effective_in_running_process'):
            result.verify_effective_key()
        self.assertFalse(result.state['encryption_key_effective_verified'])
        result.inspector.configuration = lambda *args: ({I.KEY_CONFIG: KEY}, {'environment': 'Production'})
        result.verify_effective_key()
        self.assertTrue(result.state['encryption_key_effective_verified'])
        self.assertNotIn(KEY, json.dumps(result.state))

    def migration_fixture(self, before):
        result = self.instance()
        result.state['migrations_before'] = before
        tokens = dict(tokens=5, active_tokens=3, upgrade_audits=4)
        if before == 13:
            tokens.update(active_legacy_tokens=1, protected_tokens=3, active_protected_tokens=2,
                          metadata_versions=9, invalid_metadata=0)
        revoked = 3 if before == 12 else 1
        result.verify_schema = Mock(side_effect=[before, 13])
        events = []
        def backup():
            events.append('backup-verified')
            result.state['verified_database_backup'] = '/synthetic/backup.bak'
        result.database_backup = backup
        result.prepare_encryption_key = lambda: events.append('key-persisted')
        count_calls = 0
        def query(sql):
            nonlocal count_calls
            if sql == result.runtime.COUNTS_QUERY:
                count_calls += 1
                return dict(COUNTS, audits=COUNTS['audits'] + (revoked if count_calls == 2 else 0))
            if sql == C.PRICE_COUNTS:
                return MONETARY.copy()
            if sql == (I.TOKEN_BASE_COUNTS if before == 12 else I.TOKEN_COUNTS) and count_calls == 1:
                return tokens.copy()
            if sql == I.TOKEN_COUNTS:
                return dict(tokens=5, active_tokens=3 - revoked, active_legacy_tokens=0,
                            protected_tokens=0 if before == 12 else 3, active_protected_tokens=0 if before == 12 else 2,
                            metadata_versions=(5 if before == 12 else 9) + revoked, invalid_metadata=0, upgrade_audits=4 + revoked)
            raise AssertionError('unexpected metadata query')
        result.sql_json = query
        def execute(sql, **kwargs):
            if sql == I.CLEANUP_LEGACY_SQL:
                events.append('legacy-cleanup')
                return json.dumps(dict(revoked=0 if before == 12 else revoked, transaction_committed=1))
            self.assertEqual(result.files['migrations-i12.sql'].decode('utf-8-sig'), sql)
            events.append('migration')
            return ''
        result.sql_run = execute
        return result, events

    def test_migration_follows_verified_backup_and_key_persistence_and_accounts_for_revocation_audits(self):
        result, events = self.migration_fixture(12)
        result.backup_and_migrate()
        self.assertEqual(['backup-verified', 'key-persisted', 'migration', 'legacy-cleanup'], events)
        self.assertEqual(3, result.state['legacy_tokens_revoked'])
        self.assertTrue(result.state['existing_data_counts_preserved'])
        self.assertTrue(result.state['legacy_tokens_verified'])
        self.assertTrue(result.state['database_migrations_run'])

    def test_retry_after_schema_upgrade_revokes_only_new_hash_only_accesses_and_preserves_protected_tokens(self):
        result, events = self.migration_fixture(13)
        result.backup_and_migrate()
        self.assertEqual(['backup-verified', 'key-persisted', 'legacy-cleanup'], events)
        self.assertEqual(1, result.state['legacy_tokens_revoked'])
        self.assertFalse(result.state['database_migrations_run'])
        self.assertTrue(result.state['legacy_tokens_verified'])

    def test_unverified_backup_stops_before_key_or_migration(self):
        result, events = self.migration_fixture(12)
        result.database_backup = lambda: (_ for _ in ()).throw(I.InstallError('backup_verification_failed'))
        with self.assertRaisesRegex(I.InstallError, 'backup_verification_failed'):
            result.backup_and_migrate()
        self.assertEqual([], events)

    def test_application_rollback_keeps_dedicated_key_and_does_not_restore_sql(self):
        result = self.key_fixture()
        result.prepare_encryption_key()
        retained = I.ENV_FILE.read_bytes()
        result.stop_requested = True
        result.old = dict(backend=Path('/synthetic/previous-backend'), frontend=Path('/synthetic/previous-frontend'))
        result.switch, result.health = Mock(), Mock()
        with patch.object(result.runtime, 'command') as command, patch.object(result, 'sql_run') as sql:
            result.rollback()
        self.assertEqual(['stop', 'start'], [row.args[0][1] for row in command.call_args_list])
        sql.assert_not_called()
        self.assertEqual(retained, I.ENV_FILE.read_bytes())
        self.assertTrue(result.state['encryption_key_retained_after_rollback'])
        self.assertFalse(result.state['database_automatically_restored'])

    def test_cleanup_sql_is_atomic_and_targets_only_active_hash_only_rows(self):
        self.assertIn('BEGIN TRANSACTION', I.CLEANUP_LEGACY_SQL)
        self.assertIn('ROLLBACK TRANSACTION', I.CLEANUP_LEGACY_SQL)
        self.assertIn('WHERE IsActive=1 AND ProtectedToken IS NULL', I.CLEANUP_LEGACY_SQL)
        self.assertIn('MetadataVersion=MetadataVersion+1', I.CLEANUP_LEGACY_SQL)
        self.assertIn('CustomerAccessTokenRevokedForRecoveryUpgrade', I.CLEANUP_LEGACY_SQL)
        self.assertNotIn('UPDATE dbo.AdminUsers', I.CLEANUP_LEGACY_SQL)
        self.assertNotIn('TokenHash', I.CLEANUP_LEGACY_SQL)

    def test_real_readonly_preflight_composes_exact_query_allowlist_key_checks_and_already_current(self):
        # Only process files and transport are fictional. Keep the original
        # process/configuration/SQL-parser/query-allowlist composition intact.
        self.build()
        cases = ((12, False, False, 0), (13, True, True, 0), (13, True, True, 1), (13, False, False, 0))
        for index, (count, own, has_key, legacy) in enumerate(cases):
            with self.subTest(count=count, own=own, has_key=has_key, legacy=legacy):
                app, backups, env = self.root / ('app' + str(index)), self.root / ('backups' + str(index)), self.root / ('env' + str(index))
                backups.mkdir()
                raw_env = OLD_ENV + (b'\n' + (I.KEY_ENV + '=' + KEY + '\n').encode() if has_key else b'')
                env.write_bytes(raw_env)
                env.chmod(0o600)
                with patch.object(I, 'ROOT', app), patch.object(I, 'BACKUPS', backups), patch.object(I, 'ENV_FILE', env):
                    instance = I.make_installer(self.payload)
                    old = {}
                    for part in ('backend', 'frontend'):
                        folder = app / part / 'releases' / ('access-tokens-' + SOURCE[:12] if own else I.PREVIOUS)
                        folder.mkdir(parents=True)
                        old[part] = folder
                        (app / part / 'current').symlink_to(folder)
                        for name, raw in instance.files.items():
                            if name.startswith(part + '/'):
                                target = folder / name.removeprefix(part + '/')
                                target.parent.mkdir(parents=True, exist_ok=True)
                                target.write_bytes(raw)
                    pid, tool, private = 424242, '/opt/mssql-tools/bin/sqlcmd', 'fictional-private-sql-value'
                    argv = ['/usr/bin/dotnet', str(old['backend'] / 'Prodimt.Pedidos.Api.dll')]
                    environment = {'ASPNETCORE_ENVIRONMENT': 'Production',
                        'ConnectionStrings__Pedidos': 'Server=127.0.0.1,14330;Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password=' + private + ';',
                        'Authentication__Jwt__SigningKey': 'fictional-signing-key-' + 'a' * 48,
                        'Authentication__Jwt__AccessTokenMinutes': '720'}
                    if has_key:
                        environment[I.KEY_ENV] = KEY
                    proc_raw = ('\0'.join(name + '=' + value for name, value in environment.items()) + '\0').encode()
                    actual_open, actual_is_file = Path.open, Path.is_file
                    queries, checked = [], []
                    allowed = (C.PERMISSION_QUERY, C.NEW_SCHEMA, C.PRICE_PERMISSIONS, I.TOKEN_SCHEMA, I.TOKEN_COUNTS, I.TOKEN_BASE_COUNTS)
                    def proc_path(value):
                        return types.SimpleNamespace(resolve=lambda strict=True: old['backend']) if str(value) == f'/proc/{pid}/cwd' else Path(value)
                    def read_file(path, limit=0):
                        return ('\0'.join(argv) + '\0').encode() if str(path) == f'/proc/{pid}/cmdline' else Path(path).read_bytes()
                    def open_file(path, *args, **kwargs):
                        return io.BytesIO(proc_raw) if str(path) == f'/proc/{pid}/environ' else actual_open(path, *args, **kwargs)
                    def execute(args, **kwargs):
                        if args[:2] == ['systemctl', 'show']:
                            values = dict(ActiveState='active', MainPID=str(pid), User='www-data', Group='',
                                WorkingDirectory=str(app / 'backend/current'), EnvironmentFiles=str(env) + ' (ignore_errors=no)')
                            return subprocess.CompletedProcess(args, 0, '\n'.join(name + '=' + value for name, value in values.items()), '')
                        if args[0] == tool:
                            self.assertIn('-Q', args)
                            wrapped = args[args.index('-Q') + 1]
                            query = next((query for query in allowed if wrapped.endswith('SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;')), None)
                            self.assertIsNotNone(query)
                            self.assertNotIn(private, ' '.join(args))
                            queries.append(query)
                            if query == C.PERMISSION_QUERY:
                                data = dict(C.EXPECTED_PERMISSIONS, migrations_count=count)
                            elif query == C.NEW_SCHEMA:
                                data = C.EXPECTED_SCHEMA
                            elif query == C.PRICE_PERMISSIONS:
                                data = C.EXPECTED_PRICE_PERMISSIONS
                            elif query == I.TOKEN_SCHEMA:
                                data = I.EXPECTED_TOKEN_SCHEMA if count == 13 else dict.fromkeys(I.EXPECTED_TOKEN_SCHEMA, 0)
                            elif query == I.TOKEN_BASE_COUNTS:
                                data = dict(tokens=2, active_tokens=2, upgrade_audits=0)
                            else:
                                data = dict(tokens=2, active_tokens=2, active_legacy_tokens=legacy, protected_tokens=2 - legacy,
                                    active_protected_tokens=2 - legacy, metadata_versions=3, invalid_metadata=0, upgrade_audits=4)
                            return subprocess.CompletedProcess(args, 0, 'ProdimtInspectionJson\n---------------------\n' + json.dumps(data), '')
                        if args[0] == 'curl':
                            url = next(value for value in args if value.startswith('https://'))
                            path = url.removeprefix('https://' + I.DOMAIN)
                            if path == '/health/db':
                                body, status = '{"database":"reachable"}', 200
                            elif path.startswith('/api/'):
                                body, status = '{}', 401
                            else:
                                name = 'index.html' if path.startswith('/admin/') else path.removeprefix('/')
                                body, status = (old['frontend'] / name).read_text(), 200
                            return subprocess.CompletedProcess(args, 0, body + '\n' + str(status), '')
                        raise AssertionError('unexpected mutation or transport')
                    with patch.object(I, 'validate_environment_file'), patch.object(instance.runtime, 'Path', side_effect=proc_path), \
                            patch.object(instance.runtime, 'regular', side_effect=read_file), \
                            patch.object(instance.runtime, 'directory_safe', side_effect=lambda path: self.assertTrue(path.is_dir())), \
                            patch.object(instance.runtime, 'prompt_secret', side_effect=AssertionError('read-only requested credentials')), \
                            patch.object(Path, 'open', new=open_file), \
                            patch.object(Path, 'is_file', new=lambda path: str(path) == tool or actual_is_file(path)), \
                            patch.object(subprocess, 'run', side_effect=execute) as transport, \
                            patch.object(instance, 'verify_stage', side_effect=lambda part: checked.append(part)):
                        if count == 13 and not has_key:
                            with self.assertRaisesRegex(I.InstallError, 'token_encryption_key_missing_for_migrated_database'):
                                instance.preflight()
                        else:
                            instance.preflight()
                            self.assertEqual('already_current' if own and not legacy else 'planned', instance.state['status'])
                            self.assertEqual(['backend', 'frontend'] if own and not legacy else [], checked)
                            self.assertEqual(own and not legacy, instance.state['encryption_key_effective_verified'])
                            before_calls = transport.call_count
                            with self.assertRaisesRegex(instance.inspector.InspectionError, 'sql_query_not_authorized'):
                                instance.inspector.query_sql(instance.sql_tool, instance.fields, 'SELECT 1;')
                            self.assertEqual(before_calls, transport.call_count)
                        self.assertFalse(instance.state['changes_made'])
                        self.assertFalse(instance.state['service_restarted'])
                        self.assertNotIn(private, json.dumps(instance.state))
                        self.assertNotIn(KEY, json.dumps(instance.state))


if __name__ == '__main__':
    unittest.main()
