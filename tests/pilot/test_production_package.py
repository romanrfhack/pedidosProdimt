"""I19 release lineage, payload pins, migration selection and safe recovery."""
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


builder = load('i19_builder_tests', ROOT / 'scripts/pilot/build-production-package.py')
installer = load('i19_installer_tests', ROOT / 'scripts/pilot/install-production-update.py')
identity_fixture = load('i19_historical_identity_test_fixture', ROOT / 'tests/pilot/test_identity_package.py')
setup = load('i19_setup_fixture', ROOT / 'scripts/pilot/install-customer-products-update.py')
security = load('i19_security_fixture', ROOT / 'scripts/pilot/install-identity-update.py')
commercial = load('i19_commercial_fixture', ROOT / 'scripts/pilot/install-commercial-catalog-update.py')
token = load('i19_token_fixture', ROOT / 'scripts/pilot/install-access-token-management-update.py')


def production_sql():
    return ('BEGIN TRANSACTION;\n' + ''.join('ALTER TABLE [' + name + '] ADD [RequiresMachine] bit NOT NULL DEFAULT CAST(1 AS bit);\n'
        for name in ('Products','CustomerFrequentProducts','OrderLines')) +
        'CREATE TABLE [ProductionBatches] ([Id] uniqueidentifier, CONSTRAINT [CK_ProductionBatch_Quantity] CHECK ([Quantity]>0), CONSTRAINT [CK_ProductionBatch_Unit] CHECK ([UnitCode]=N\'kg\'));\n'
        'CREATE TABLE [ProductionAllocations] ([Id] uniqueidentifier, CONSTRAINT [CK_ProductionAllocation_Quantity] CHECK ([Quantity]>0), CONSTRAINT [CK_ProductionAllocation_Reversal] CHECK ([ReversedAt] IS NULL));\n' +
        ''.join("INSERT INTO [SecurityPermissions] ([Key]) VALUES(N'"+key+"');\n" for key in installer.PRODUCTION_PERMISSIONS) +
        'INSERT INTO [SecurityRolePermissions] ([RoleId]) VALUES(\'18180000-0000-0000-0000-000000000001\');\n'
        'UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;\n'
        "INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'" + installer.MIGRATION + "', N'10.0.8');\nCOMMIT;\nGO\n\n")


def supports():
    return {name: (ROOT / 'scripts/pilot' / original).read_bytes() for name, original in (
        ('inspect-native.py','inspect-native.py'),
        ('native-deployment-support.py','install-quick-capture-update.py'),
        ('commercial-catalog-support.py','install-commercial-catalog-update.py'),
        ('token-management-support.py','install-access-token-management-update.py'),
        ('customer-products-support.py','install-customer-products-update.py'),
        ('security-administration-support.py','install-identity-update.py'))} | {
        'migrations-i16.sql': (ROOT / 'tests/pilot/fixtures/i16-additional-products-migration.sql').read_bytes(),
        'migrations-i18.sql': identity_fixture.builder.migration_sql_for_sqlcmd(identity_fixture.migration()).encode(),
        'migrations-i19.sql': production_sql().encode()}


class ProductionPackageTests(unittest.TestCase):
    def instance(self):
        with patch.object(installer, 'load_payload', return_value=supports()):
            instance = installer.make_installer(Path('/unused-synthetic-payload.zip'))
        instance.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        return instance

    def test_production_sql_rejects_destructive_changes_and_unscoped_revision_update(self):
        installer.validate_migration_sql(production_sql())
        for changed in (production_sql()+'DROP TABLE dbo.Orders;',
                        production_sql().replace('WHERE [Id] = 1;', ';'),
                        production_sql().replace('DEFAULT CAST(1 AS bit)', 'DEFAULT CAST(0 AS bit)', 1),
                        production_sql().replace('INSERT INTO [SecurityPermissions]', 'INSERT INTO [AdminUsers]', 1)):
            with self.subTest(changed=changed[-70:]), self.assertRaises(installer.InstallError):
                installer.validate_migration_sql(changed)

    def test_exact_migration_ids_distinguish_parallel_fourteenth_migration(self):
        instance = self.instance()
        base = instance.runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION]
        for suffix in ([], [installer.SETUP_MIGRATION], [installer.IDENTITY_MIGRATION],
                       [installer.SETUP_MIGRATION, installer.IDENTITY_MIGRATION],
                       [installer.SETUP_MIGRATION, installer.IDENTITY_MIGRATION, installer.MIGRATION]):
            ids = base + suffix
            self.assertEqual(instance.history(lambda _: [{'MigrationId': value} for value in ids]), ids)
        for suffix in (['same-count-unknown-id'], [installer.MIGRATION], [installer.IDENTITY_MIGRATION, installer.SETUP_MIGRATION]):
            with self.subTest(suffix=suffix), self.assertRaises(installer.InstallError):
                instance.history(lambda _: [{'MigrationId': value} for value in base+suffix])

    def test_release_pair_guard_does_not_mix_parallel_frontend_and_backend(self):
        cases = [(pair, True) for pair in installer.PREVIOUS_PAIRS] + [
            ((installer.I16_RELEASE, installer.I18_RELEASE), False),
            ((installer.I18_RELEASE, 'pending-customers-cdbfec3071bc'), False),
            (('access-tokens-unreviewed', 'customer-catalog-afb8f3ad9c33'), False)]
        for pair, accepted in cases:
            with self.subTest(pair=pair), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for part, name in zip(('backend','frontend'), pair):
                    target = root / part / 'releases' / name
                    target.mkdir(parents=True)
                    (root / part / 'current').symlink_to(target)
                with patch.object(installer, 'ROOT', root):
                    instance = self.instance()
                    instance.runtime.directory_safe = Mock()
                    instance.process = Mock(side_effect=installer.InstallError('release_accepted'))
                    with self.assertRaises(installer.InstallError) as error:
                        instance.preflight()
                self.assertEqual(str(error.exception), 'release_accepted' if accepted else 'previous_release_not_supported')
                self.assertFalse(instance.stop_requested)
                self.assertFalse(instance.state['changes_made'])

    def test_least_privilege_prevents_batch_mutation_and_ledger_deletion(self):
        self.assertEqual(installer.EXPECTED_PRODUCTION_PERMISSIONS['ProductionBatches_UPDATE'], 0)
        self.assertEqual(installer.EXPECTED_PRODUCTION_PERMISSIONS['ProductionBatches_DELETE'], 0)
        self.assertEqual(installer.EXPECTED_PRODUCTION_PERMISSIONS['ProductionAllocations_UPDATE'], 1)
        self.assertEqual(installer.EXPECTED_PRODUCTION_PERMISSIONS['ProductionAllocations_DELETE'], 0)

    def test_strict_unit_check_accepts_only_ascii_or_unicode_exact_kg_literal(self):
        # SQL Server canonicalizes this ASCII-compatible CHECK by removing N.
        # Keep both complete expressions explicit; do not bypass its definition.
        self.assertIn(" IN (N'unitcode=''kg''',N'unitcode=n''kg''')",installer.PRODUCTION_SCHEMA)
        self.assertIn("name=N'CK_ProductionBatch_Unit'",installer.PRODUCTION_SCHEMA)
        self.assertEqual(installer.EXPECTED_PRODUCTION_SCHEMA['trusted_checks'],4)
        self.assertNotIn("unitcode=''piece''",installer.PRODUCTION_SCHEMA)

    def simulate_upgrade(self, initial_suffix):
        instance = self.instance()
        base = instance.runtime.MIGRATIONS + [token.PRICE_MIGRATION,token.MIGRATION]
        ids = base + initial_suffix
        instance.state['migration_ids_before'] = ids.copy()
        instance.schemas = Mock(return_value=ids.copy())
        instance.verify_schema = Mock(return_value=16)
        instance.verify_security = Mock()
        instance.app_permissions = Mock()
        events = []
        def backup():
            events.append('backup-verified')
            instance.state['verified_database_backup'] = '/synthetic-backup'
        instance.database_backup = backup
        raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
        instance.environment = raw
        instance.token_encryption_key = token.environment_key(raw)
        counts = dict.fromkeys(('customers','orders','lines','administrators','audits','order_audits','profiles','products','machines'), 1)
        mapping = {instance.runtime.COUNTS_QUERY: counts, commercial.PRICE_COUNTS: {}, token.TOKEN_COUNTS: {},
                   setup.DATA_FINGERPRINT: {}, setup.SETUP_COUNTS: {}, installer.SECURITY_FINGERPRINT: {},
                   security.ADMIN_ACCESS_PRESERVED: {'missing':0}}
        instance.sql_json = lambda query: [{'MigrationId': value} for value in ids] if query=='history' else mapping[query]
        def execute(sql, timeout):
            self.assertEqual(events[0], 'backup-verified')
            self.assertEqual(sql.count('BEGIN TRANSACTION;'), 1)
            self.assertEqual(sql.count('COMMIT;'), 1)
            value = next(value for value in (installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.MIGRATION) if value in sql)
            events.append(value)
            ids.append(value)
            ids.sort()
            self.assertIn('GRANT ', sql)
        instance.sql_run = Mock(side_effect=execute)
        with patch.object(installer, 'regular', return_value=raw):
            instance.backup_and_migrate()
        return instance, events

    def test_each_supported_base_applies_only_missing_migrations_after_verified_backup(self):
        for existing in ([], [installer.SETUP_MIGRATION], [installer.IDENTITY_MIGRATION]):
            with self.subTest(existing=existing):
                instance, events = self.simulate_upgrade(existing)
                expected = [value for value in (installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.MIGRATION) if value not in existing]
                self.assertEqual(events, ['backup-verified'] + expected)
                self.assertTrue(instance.state['tokens_preserved'])
                self.assertEqual(instance.state['migrations_after'], 16)

    def test_forward_recovery_at_complete_schema_never_reexecutes_sql_or_regrants_roles(self):
        instance, events = self.simulate_upgrade([installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.MIGRATION])
        instance.sql_run.assert_not_called()
        self.assertEqual(events, ['backup-verified'])

    def test_rollback_after_ddl_or_uncertain_history_never_starts_old_binary(self):
        for uncertain in (False,True):
            with self.subTest(uncertain=uncertain):
                instance = self.instance()
                instance.stop_requested = True
                instance.history_before = instance.runtime.MIGRATIONS + [token.PRICE_MIGRATION,token.MIGRATION]
                instance.runtime.command = Mock()
                instance.sql_json = Mock(side_effect=OSError('synthetic_read_failure')) if uncertain else Mock(return_value=[{'MigrationId':value} for value in instance.history_before+[installer.SETUP_MIGRATION]])
                instance.runtime.Installer.rollback = Mock(side_effect=AssertionError('must never downgrade'))
                instance.rollback()
                instance.runtime.Installer.rollback.assert_not_called()
                self.assertTrue(instance.state['recovery_pending'])
                self.assertTrue(instance.state['application_stopped_for_security'])

    def test_rollback_before_stop_does_not_query_sql_or_change_service(self):
        instance = self.instance()
        instance.sql_json = Mock(side_effect=AssertionError('no SQL'))
        instance.runtime.command = Mock(side_effect=AssertionError('no service'))
        instance.rollback()
        instance.sql_json.assert_not_called()
        instance.runtime.command.assert_not_called()

    def test_failed_service_stop_never_claims_stopped_or_attempts_old_binary(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.runtime.command = Mock(side_effect=installer.InstallError('synthetic_stop_failure'))
        instance.sql_json = Mock(side_effect=AssertionError('must not query after failed stop'))
        instance.rollback()
        instance.sql_json.assert_not_called()
        self.assertTrue(instance.state['rollback_blocked_service_stop'])
        self.assertNotIn('application_stopped_for_security',instance.state)

    def test_recovery_marker_pins_source_payload_and_private_environment(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(installer,'BACKUPS',Path(folder)):
            instance = self.instance()
            instance.marker.write_text(json.dumps({'source':'another-source','payload':'another-payload'}))
            instance.marker.chmod(0o600)
            with self.assertRaisesRegex(installer.InstallError,'different_update_recovery_pending'):
                instance.read_marker()

    def test_readonly_recovery_accepts_stopped_service_and_apply_activates_only_i19(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)/'app'
            backups = Path(folder)/'backups'; backups.mkdir()
            env = Path(folder)/'api.env'
            raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
            env.write_bytes(raw); env.chmod(0o600)
            pair = installer.PREVIOUS_PAIRS[1]
            for part,name in zip(('backend','frontend'),pair):
                target = root/part/'releases'/name
                target.mkdir(parents=True)
                (root/part/'current').symlink_to(target)
                if part=='backend':
                    (target/'Prodimt.Pedidos.Api.dll').write_bytes(b'synthetic-old-backend')
                    (target/'appsettings.json').write_text('{}')
            snapshot = backups/'production-update-20261103T010000Z-aabbccddeeff'
            snapshot.mkdir(mode=0o700)
            (snapshot/'api.env').write_bytes(raw)
            (snapshot/'appsettings.json').write_text('{}')
            unit = dict(User='www-data',WorkingDirectory=str(root/'backend/current'),EnvironmentFiles=str(env)+' (ignore_errors=no)')
            (snapshot/'process-input.json').write_text(json.dumps(dict(environment={},argv=['dotnet','Prodimt.Pedidos.Api.dll'],unit=unit)))
            for path in snapshot.iterdir(): path.chmod(0o600)
            with patch.object(installer,'ROOT',root), patch.object(installer,'BACKUPS',backups), patch.object(installer,'ENV_FILE',env):
                instance = self.instance()
                base = instance.runtime.MIGRATIONS+[token.PRICE_MIGRATION,token.MIGRATION]
                complete = base+[installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.MIGRATION]
                instance.marker.write_text(json.dumps(dict(source=installer.EXPECTED_SOURCE,payload=installer.EXPECTED_PAYLOAD_SHA256,
                    snapshot=str(snapshot),previous=list(pair),historyBefore=base)))
                instance.marker.chmod(0o600)
                mapping = {'history':[{'MigrationId':value} for value in complete],commercial.NEW_SCHEMA:commercial.EXPECTED_SCHEMA,
                    setup.TOKEN_SHAPE:setup.EXPECTED_TOKEN_SCHEMA,setup.SETUP_SHAPE:setup.EXPECTED_SETUP_SCHEMA,
                    security.SECURITY_SCHEMA:security.EXPECTED_SECURITY_SCHEMA,installer.PRODUCTION_SHAPE:installer.EXPECTED_PRODUCTION_SCHEMA,
                    commercial.PERMISSION_QUERY:commercial.EXPECTED_PERMISSIONS|{'migrations_count':16},
                    commercial.PRICE_PERMISSIONS:commercial.EXPECTED_PRICE_PERMISSIONS,setup.SETUP_PERMISSIONS:setup.EXPECTED_SETUP_PERMISSIONS,
                    security.SECURITY_PERMISSIONS:security.EXPECTED_SECURITY_PERMISSIONS,
                    installer.PRODUCTION_SQL_PERMISSIONS:installer.EXPECTED_PRODUCTION_PERMISSIONS}
                configuration = {token.KEY_CONFIG:token.environment_key(raw),'ConnectionStrings:Pedidos':'synthetic-connection'}
                inventory = types.SimpleNamespace(SQL_MIGRATIONS='history',SQL_TOOLS=('/bin/true',),
                    configuration=lambda *args:(configuration,{'environment':'Production'}),
                    first=lambda cfg,*keys,default=None:cfg.get(keys[0],default),
                    policy_flags=lambda cfg:dict(provider_is_sqlserver=True,jwt_compatible_with_production_policy=True,
                        jwt_lifetime_compatible=True,connection_compatible_with_production_policy=True,
                        development_seed_enabled=False,automatic_migrations_enabled=False,https_redirection_enabled=True),
                    connection_fields=lambda _: {},permitted_connection=lambda _:True,
                    query_sql=lambda tool,fields,query:mapping[query])
                original_module = installer.module
                def module(name, data):
                    return inventory if name=='i19_inventory' else original_module(name,data)
                instance.runtime.command = Mock(return_value='ActiveState=inactive\nMainPID=0\n'+''.join(key+'='+value+'\n' for key,value in unit.items()))
                instance.process = Mock(side_effect=AssertionError('stopped API must not require a live PID'))
                instance.http = Mock(side_effect=AssertionError('stopped API has no health endpoint'))
                with patch.object(installer,'module',side_effect=module):
                    instance.preflight()
                self.assertEqual(instance.state['status'],'recovery_pending')
                self.assertFalse(instance.state['changes_made'])
                self.assertEqual(instance.state['migration_ids_before'],complete)
                self.assertEqual(env.read_bytes(),raw)
                instance.preflight = Mock()
                instance.authenticate_admin = Mock()
                instance.stage = Mock()
                instance.backup_and_migrate = Mock()
                instance.switch = Mock()
                instance.health = Mock()
                instance.runtime.command.reset_mock()
                instance.apply()
                self.assertEqual(instance.state['status'],'updated')
                instance.switch.assert_called_once_with(instance.new)
                self.assertTrue(all(path.name.startswith('production-') for path in instance.new.values()))
                self.assertFalse(instance.marker.exists())
                self.assertEqual(env.read_bytes(),raw)

    def test_build_and_exported_installer_agree_on_pins_hashes_and_all_three_migrations(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backend, frontend = root/'backend', root/'frontend'
            backend.mkdir(); frontend.mkdir()
            (backend/'Prodimt.Pedidos.Api.dll').write_bytes(b'synthetic-backend')
            (backend/'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
            (backend/'appsettings.json').write_text('{}')
            (frontend/'index.html').write_text('<html></html>')
            (frontend/'3rdpartylicenses.txt').write_text('synthetic')
            (frontend/'main.js').write_text('apiBaseUrl:"" /access-tokens /reveal /prices /security directPermissionIds roleIds /product-setups /additional-products /operations/production requiresMachine')
            production = root/'production.sql'; production.write_text(production_sql())
            identity = root/'identity.sql'; identity.write_text(identity_fixture.migration())
            output = root/'delivery.zip'
            source = 'a'*40
            with patch.object(builder,'validate_source'):
                result = builder.build(backend,frontend,production,output,source,ROOT/'tests/pilot/fixtures/i16-additional-products-migration.sql',identity)
            with zipfile.ZipFile(output) as outer:
                exported = root/'exported.py'
                exported.write_bytes(outer.read(builder.PREFIX+'PRODIMT_Instalar_I19.py'))
                payload = root/'payload.zip'
                payload.write_bytes(outer.read(builder.PREFIX+'prodimt-production-payload.zip'))
                with zipfile.ZipFile(io.BytesIO(payload.read_bytes())) as inner:
                    manifest = json.loads(inner.read('manifest.json'))
                    self.assertEqual(manifest['feature'],'production-allocation-v1')
                    self.assertEqual(manifest['migrationsAfter'],16)
                    self.assertEqual(manifest['migrationIds'],[installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.MIGRATION])
            exported_module = load('i19_exported_test',exported)
            self.assertEqual(exported_module.EXPECTED_SOURCE,source)
            self.assertEqual(exported_module.EXPECTED_PAYLOAD_SHA256,result['payloadSha256'])
            self.assertEqual(len(exported_module.load_payload(payload)),result['files'])
            payload.write_bytes(payload.read_bytes()+b'corruption')
            with self.assertRaisesRegex(exported_module.InstallError,'payload_hash_mismatch'):
                exported_module.load_payload(payload)

    def test_builder_rejects_dirty_source_and_wrong_revision(self):
        with patch.object(builder.subprocess,'check_output',return_value='b'*40):
            with self.assertRaisesRegex(ValueError,'source_commit_not_current'):
                builder.validate_source('a'*40)
        with patch.object(builder.subprocess,'check_output',side_effect=['a'*40,' M changed.cs']):
            with self.assertRaisesRegex(ValueError,'source_tree_not_clean'):
                builder.validate_source('a'*40)


if __name__ == '__main__':
    unittest.main()
