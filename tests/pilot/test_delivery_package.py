"""I22 package provenance, exact upgrade bases and recovery without downgrade."""
import copy
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


installer = load('i22_installer_tests', ROOT / 'scripts/pilot/install-delivery-update.py')
builder = load('i22_builder_tests', ROOT / 'scripts/pilot/build-delivery-package.py')
schema = load('i22_schema_tests', ROOT / 'scripts/pilot/delivery-schema-contract.py')
commercial = load('i22_commercial_tests', ROOT / 'scripts/pilot/install-commercial-catalog-update.py')
token = load('i22_token_tests', ROOT / 'scripts/pilot/install-access-token-management-update.py')
setup = load('i22_setup_tests', ROOT / 'scripts/pilot/install-customer-products-update.py')
exporter = load('i22_exporter_tests', ROOT / 'scripts/pilot/export-delivery-changes.py')
validation = load('i22_sql_validation_tests', ROOT / 'scripts/pilot/validate-delivery-sql.py')


def migration():
    tables = []
    for table in installer.DELIVERY_TABLES:
        columns = '    [Id] uniqueidentifier NOT NULL,\n    [Quantity] decimal(18,3) NULL,\n'
        columns += '    [PreferredTime] time NULL,\n    [Name] nvarchar(160) NOT NULL,\n'
        columns += '    [OccurredAt] datetimeoffset NOT NULL,\n'
        columns += f'    CONSTRAINT [PK_{table}] PRIMARY KEY ([Id]),\n'
        columns += f'    CONSTRAINT [CK_{table}_Quantity] CHECK ([Quantity] >= 0)\n'
        tables.append('CREATE TABLE [' + table + '] (\n' + columns + ');\n')
    return ('BEGIN TRANSACTION;\n' + ''.join(tables) +
        'CREATE UNIQUE INDEX [IX_DeliveryRoutes_Name] ON [DeliveryRoutes] ([Name]);\n' +
        ''.join("INSERT INTO [SecurityPermissions] ([Key]) VALUES(N'" + key + "');\n" for key in installer.DELIVERY_PERMISSIONS) +
        "INSERT INTO [SecurityRoles] ([Id]) VALUES('" + installer.DELIVERY_ROLE + "');\n" +
        "INSERT INTO [SecurityRolePermissions] ([RoleId]) VALUES('" + installer.DELIVERY_ROLE + "');\n" +
        'UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;\n' +
        "INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'" + installer.MIGRATION + "', N'10.0.8');\nCOMMIT;\nGO\n\n")


def supports():
    files = {name: (ROOT / 'scripts/pilot' / original).read_bytes() for name, original in (
        ('inspect-native.py', 'inspect-native.py'),
        ('native-deployment-support.py', 'install-quick-capture-update.py'),
        ('commercial-catalog-support.py', 'install-commercial-catalog-update.py'),
        ('token-management-support.py', 'install-access-token-management-update.py'),
        ('customer-products-support.py', 'install-customer-products-update.py'),
        ('security-administration-support.py', 'install-identity-update.py'),
        ('production-support.py', 'install-production-update.py'),
        ('delivery-schema-contract.py', 'delivery-schema-contract.py'),
        ('validate-delivery-sql.py', 'validate-delivery-sql.py'))}
    return files | {'migrations-i22.sql': migration().encode(),
                    'delivery-schema.json': json.dumps(schema.contract_from_sql(migration())).encode(),
                    'validation-baseline.sql': builder.normalized_validation_baseline(
                        (ROOT / 'tests/pilot/fixtures/i22-validation-baseline.sql').read_text(encoding='utf-8-sig')).encode()}


class DeliveryPackageTests(unittest.TestCase):
    def instance(self):
        with patch.object(installer, 'load_payload', return_value=supports()):
            result = installer.make_installer(Path('/synthetic-payload.zip'))
        result.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        return result

    def base(self, instance):
        return instance.runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION,
            installer.SETUP_MIGRATION, installer.IDENTITY_MIGRATION, installer.PRODUCTION_MIGRATION]

    def test_contract_preserves_decimal_scale_time_and_column_nullability(self):
        contract = schema.contract_from_sql(migration())
        quantity = next(row for row in contract['columns'] if row['table'] == 'DeliveryAssignments' and row['name'] == 'Quantity')
        self.assertEqual((quantity['precision'], quantity['scale'], quantity['nullable']), (18, 3, 1))
        time = next(row for row in contract['columns'] if row['table'] == 'DeliveryAssignments' and row['name'] == 'PreferredTime')
        self.assertEqual((time['length'], time['precision'], time['scale']), (5, 16, 7))
        self.assertEqual(len(contract['primaryKeys']), 5)
        self.assertEqual(len(contract['indexes']), 1)

    def test_real_ef_incremental_sql_is_accepted_without_modifying_old_tables(self):
        sql = (ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text(encoding='utf-8-sig')
        installer.validate_migration_sql(sql)
        contract = schema.contract_from_sql(sql)
        self.assertEqual({row['table'] for row in contract['columns']}, set(installer.DELIVERY_TABLES))
        requested = next(row for row in contract['columns'] if row['table'] == 'DeliveryAssignments' and row['name'] == 'RequestedQuantity')
        self.assertEqual((requested['type'], requested['precision'], requested['scale']), ('decimal', 18, 3))
        self.assertTrue(any(row['table'] == 'DeliveryTrips' and row['unique'] == 1 for row in contract['indexes']))
        self.assertTrue(schema.matches(contract, contract))

    def test_schema_check_rejects_drift_and_untrusted_constraint(self):
        contract = schema.contract_from_sql(migration())
        self.assertTrue(schema.matches(contract, contract))
        for part, key, value in [('columns', 'scale', 2), ('checks', 'untrusted', 1), ('indexes', 'unique', 0),
                                  ('primaryKeys', 'column', 'AnotherId')]:
            changed = copy.deepcopy(contract)
            changed[part][0][key] = value
            self.assertFalse(schema.matches(changed, contract))
        extra = copy.deepcopy(contract)
        extra['columns'].append(dict(extra['columns'][0], name='Unexpected'))
        self.assertFalse(schema.matches(extra, contract))

    def test_dml_principal_checks_shape_but_admin_still_checks_predicate(self):
        contract = schema.contract_from_sql(migration())
        visible = copy.deepcopy(contract)
        for row in visible['checks']:
            row.pop('definition')
        self.assertTrue(schema.matches(visible, contract, strict=False))
        self.assertFalse(schema.matches(visible, contract, strict=True))
        changed = copy.deepcopy(contract)
        changed['checks'][0]['definition'] = '[Quantity] > 100'
        self.assertFalse(schema.matches(changed, contract))

    def test_status_check_supports_sqlserver_canonical_or_without_widening_values(self):
        expected = schema.normalized("[Status] IN ('Open', 'Closed')")
        self.assertEqual(expected, schema.normalized("([Status]='Closed' OR [Status]='Open')"))
        self.assertNotEqual(expected, schema.normalized("[Status] IN ('Open','Closed','Unknown')"))
        self.assertNotEqual(expected, schema.normalized("[OtherStatus]='Closed' OR [OtherStatus]='Open'"))

    def test_check_normalization_retains_arithmetic_and_boolean_grouping(self):
        self.assertNotEqual(schema.normalized('(A-B)+C>=0'), schema.normalized('A-(B+C)>=0'))
        self.assertNotEqual(schema.normalized('A=1 AND (B=2 OR C=3)'), schema.normalized('(A=1 AND B=2) OR C=3'))
        self.assertEqual(schema.normalized('((A)-(B))+(C)>=(0)'), schema.normalized('(A-B)+C>=0'))
        self.assertEqual(schema.normalized('(A=1 AND B=2) AND C=3'), schema.normalized('C=3 AND (B=2 AND A=1)'))

    def test_sql_validator_refuses_existing_data_mutation_or_role_target_change(self):
        installer.validate_migration_sql(migration())
        for sql in (migration() + 'DROP TABLE dbo.Orders;',
                    migration().replace('WHERE [Id] = 1;', ';'),
                    migration().replace('INSERT INTO [SecurityRoles]', 'INSERT INTO [AdminUsers]'),
                    migration().replace('CREATE TABLE [DeliveryEvents]', 'CREATE TABLE [OtherEvents]')):
            with self.subTest(sql=sql[-60:]), self.assertRaises(installer.InstallError):
                installer.validate_migration_sql(sql)

    def test_sql_grants_commit_with_ddl_and_preserve_events_append_only(self):
        sql = installer.migration_sql_for_sqlcmd(supports())
        self.assertEqual(sql.count('BEGIN TRANSACTION;'), 1)
        self.assertEqual(sql.count('COMMIT;'), 1)
        self.assertLess(sql.index('CREATE TABLE'), sql.index('GRANT SELECT'))
        self.assertLess(sql.index('DENY UPDATE,DELETE ON dbo.DeliveryEvents'), sql.index('COMMIT;'))
        self.assertIn('\nGO\n\nGRANT SELECT', sql)
        self.assertEqual(installer.EXPECTED_DELIVERY_PERMISSIONS['DeliveryEvents_UPDATE'], 0)
        self.assertEqual(installer.EXPECTED_DELIVERY_PERMISSIONS['DeliveryAssignments_UPDATE'], 1)
        self.assertTrue(all(value == 0 for key, value in installer.EXPECTED_DELIVERY_PERMISSIONS.items() if key.endswith('_DELETE')))

    def test_history_accepts_only_exact_i19_or_i22_not_same_count_unknown(self):
        instance = self.instance()
        base = self.base(instance)
        for ids in (base, base + [installer.MIGRATION]):
            self.assertEqual(instance.history(lambda _: [{'MigrationId': value} for value in ids]), ids)
        for ids in (base[:-1] + ['unknown'], base + ['unknown'], base[:-2] + base[-2:][::-1]):
            with self.subTest(ids=ids), self.assertRaises(installer.InstallError):
                instance.history(lambda _: [{'MigrationId': value} for value in ids])

    def test_release_guard_accepts_only_i19_with_i19_or_exact_i21(self):
        cases = [(pair, True) for pair in installer.PREVIOUS_PAIRS] + [
            (('identity-c49df453de9d', 'identity-c49df453de9d'), False),
            ((installer.BASE_BACKEND, 'orders-catalog-unreviewed'), False)]
        for pair, accepted in cases:
            with self.subTest(pair=pair), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for part, name in zip(('backend', 'frontend'), pair):
                    target = root / part / 'releases' / name
                    target.mkdir(parents=True)
                    (root / part / 'current').symlink_to(target)
                with patch.object(installer, 'ROOT', root), patch.object(installer, 'BACKUPS', root / 'backups'):
                    instance = self.instance()
                    instance.runtime.directory_safe = Mock()
                    instance.process = Mock(side_effect=installer.InstallError('accepted_base'))
                    with self.assertRaises(installer.InstallError) as error:
                        instance.preflight()
                self.assertEqual(str(error.exception), 'accepted_base' if accepted else 'previous_release_not_supported')
                self.assertFalse(instance.state['changes_made'])
                self.assertFalse(instance.stop_requested)

    def test_unfinished_i19_update_cannot_be_taken_over(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(installer, 'BACKUPS', Path(folder)):
            (Path(folder) / 'production-pending.json').write_text('{}')
            instance = self.instance()
            instance.runtime.directory_safe = Mock()
            instance.process = Mock(side_effect=AssertionError('must fail before touching service'))
            with self.assertRaisesRegex(installer.InstallError, 'previous_update_recovery_pending'):
                instance.preflight()
            instance.process.assert_not_called()

    def simulate_upgrade(self, already_migrated=False, backup_verified=True):
        instance = self.instance()
        ids = self.base(instance) + ([installer.MIGRATION] if already_migrated else [])
        instance.state['migration_ids_before'] = ids.copy()
        instance.schemas = Mock(return_value=ids.copy())
        instance.verify_schema = Mock(return_value=17)
        instance.verify_security = Mock()
        instance.app_permissions = Mock()
        raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
        instance.environment = raw
        instance.token_encryption_key = token.environment_key(raw)
        counts = dict.fromkeys(('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits', 'profiles', 'products', 'machines'), 1)
        mapping = {instance.runtime.COUNTS_QUERY: counts, commercial.PRICE_COUNTS: {}, token.TOKEN_COUNTS: {},
            setup.DATA_FINGERPRINT: {}, setup.SETUP_COUNTS: {}, installer.SECURITY_FINGERPRINT: {},
            installer.DELIVERY_COUNTS: dict.fromkeys(installer.DELIVERY_TABLES, 0),
            installer.DELIVERY_ROLE_DATA: dict(driver_roles=1, driver_grants=2, driver_total_grants=2)}
        instance.sql_json = lambda query: [{'MigrationId': value} for value in ids] if query == 'history' else mapping[query]
        events = []
        def backup():
            events.append('backup')
            if backup_verified:
                instance.state['verified_database_backup'] = '/synthetic-verified-backup'
        instance.database_backup = backup
        def execute(sql, timeout):
            self.assertEqual(events, ['backup'])
            events.append('migration')
            ids.append(installer.MIGRATION)
        instance.sql_run = Mock(side_effect=execute)
        with patch.object(installer, 'regular', return_value=raw):
            if backup_verified:
                instance.backup_and_migrate()
            else:
                with self.assertRaisesRegex(installer.InstallError, 'verified_database_backup_required'):
                    instance.backup_and_migrate()
        return instance, events

    def test_verified_backup_precedes_the_only_new_migration(self):
        instance, events = self.simulate_upgrade()
        self.assertEqual(events, ['backup', 'migration'])
        self.assertEqual(instance.state['migrations_after'], 17)
        self.assertTrue(instance.state['tokens_preserved'])
        self.assertTrue(instance.state['delivery_permissions_verified'])

    def test_missing_verified_backup_blocks_ddl(self):
        instance, events = self.simulate_upgrade(backup_verified=False)
        self.assertEqual(events, ['backup'])
        instance.sql_run.assert_not_called()

    def test_forward_recovery_does_not_reexecute_ddl_or_regrant_role(self):
        instance, events = self.simulate_upgrade(already_migrated=True)
        self.assertEqual(events, ['backup'])
        instance.sql_run.assert_not_called()

    def test_rollback_with_new_schema_or_unknown_history_never_starts_old_binary(self):
        for uncertain in (False, True):
            with self.subTest(uncertain=uncertain):
                instance = self.instance()
                instance.stop_requested = True
                instance.history_before = self.base(instance)
                instance.runtime.command = Mock()
                instance.sql_json = Mock(side_effect=OSError('synthetic failure')) if uncertain else Mock(
                    return_value=[{'MigrationId': value} for value in instance.history_before + [installer.MIGRATION]])
                instance.runtime.Installer.rollback = Mock(side_effect=AssertionError('unsafe downgrade'))
                instance.rollback()
                instance.runtime.Installer.rollback.assert_not_called()
                self.assertTrue(instance.state['application_stopped_for_security'])
                self.assertTrue(instance.state['recovery_pending'])
                self.assertFalse(instance.state['database_automatically_restored'])

    def test_failed_service_stop_does_not_claim_application_stopped(self):
        instance = self.instance()
        instance.stop_requested = True
        instance.runtime.command = Mock(side_effect=installer.InstallError('stop failed'))
        instance.sql_json = Mock(side_effect=AssertionError('must not query SQL'))
        instance.rollback()
        instance.sql_json.assert_not_called()
        self.assertTrue(instance.state['rollback_blocked_service_stop'])
        self.assertNotIn('application_stopped_for_security', instance.state)

    def test_builder_export_hashes_and_pins_match_installer(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backend, frontend = root / 'backend', root / 'frontend'
            backend.mkdir(); frontend.mkdir()
            (backend / 'Prodimt.Pedidos.Api.dll').write_bytes(b'synthetic-backend')
            (backend / 'Prodimt.Pedidos.Api.runtimeconfig.json').write_text('{}')
            (backend / 'appsettings.json').write_text('{}')
            (frontend / 'index.html').write_text('<html></html>')
            (frontend / '3rdpartylicenses.txt').write_text('synthetic')
            (frontend / 'main.js').write_text('apiBaseUrl:"" /access-tokens /prices /security /product-setups /additional-products /operations/production delivery.read.own delivery.manage /delivery')
            validator = root / 'validator'; validator.mkdir()
            for name in installer.VALIDATION_REQUIRED:
                (validator / Path(name).name).write_text('{}')
            for name in builder.SHARED_ASSEMBLIES:
                (backend / name).write_bytes(b'synthetic-shared-assembly')
                (validator / name).write_bytes(b'synthetic-shared-assembly')
            sql = ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql'
            output = root / 'package.zip'
            with patch.object(builder, 'validate_source'):
                result = builder.build(backend, frontend, sql, output, 'a' * 40, validator,
                    ROOT / 'tests/pilot/fixtures/i22-validation-baseline.sql')
            with zipfile.ZipFile(output) as outer:
                exported = root / 'exported.py'; exported.write_bytes(outer.read(builder.PREFIX + 'PRODIMT_Instalar_I22.py'))
                payload = root / 'payload.zip'; payload.write_bytes(outer.read(builder.PREFIX + 'prodimt-delivery-payload.zip'))
                with zipfile.ZipFile(io.BytesIO(payload.read_bytes())) as inner:
                    manifest = json.loads(inner.read('manifest.json'))
                    self.assertEqual(manifest['previousPairs'], [list(pair) for pair in installer.PREVIOUS_PAIRS])
                    self.assertEqual(manifest['migrationsBefore'], 16)
                    self.assertEqual(manifest['migrationsAfter'], 17)
                    self.assertEqual(manifest['validation']['expected'], 3)
            module = load('i22_exported_tests', exported)
            self.assertEqual(module.EXPECTED_SOURCE, 'a' * 40)
            self.assertEqual(module.EXPECTED_PAYLOAD_SHA256, result['payloadSha256'])
            # The fixture runs under an unprivileged test user; production file
            # owner/permission checks remain unchanged in the exported script.
            with patch.object(module, 'regular', side_effect=lambda path: path.read_bytes()):
                self.assertEqual(len(module.load_payload(payload)), result['files'])
                payload.write_bytes(payload.read_bytes() + b'corruption')
                with self.assertRaisesRegex(module.InstallError, 'payload_hash_mismatch'):
                    module.load_payload(payload)
            (validator / 'Prodimt.Pedidos.Api.dll').write_bytes(b'outdated-api')
            with patch.object(builder, 'validate_source'), self.assertRaisesRegex(ValueError, 'validation_application_binary_mismatch'):
                builder.build(backend, frontend, sql, output, 'a' * 40, validator,
                    ROOT / 'tests/pilot/fixtures/i22-validation-baseline.sql')

    def test_builder_refuses_wrong_or_dirty_source(self):
        with patch.object(builder.subprocess, 'check_output', return_value='b' * 40):
            with self.assertRaisesRegex(ValueError, 'source_commit_not_current'):
                builder.validate_source('a' * 40)
        with patch.object(builder.subprocess, 'check_output', side_effect=['a' * 40, ' M source.cs']):
            with self.assertRaisesRegex(ValueError, 'source_tree_not_clean'):
                builder.validate_source('a' * 40)

    def test_source_bundle_restores_without_prerequisites_and_patch_uses_exact_base(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            repo = root / 'repo'; repo.mkdir()
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=repo, text=True).strip()
            git('init', '--quiet', '--initial-branch=codex/i22-driver-van-deliveries')
            git('config', 'user.name', 'Fictional test')
            git('config', 'user.email', 'fixture@example.invalid')
            (repo / 'example.txt').write_text('baseline\n')
            git('add', 'example.txt'); git('commit', '--quiet', '-m', 'baseline fixture')
            base = git('rev-parse', 'HEAD')
            (repo / 'example.txt').write_text('delivery fixture\n')
            git('add', 'example.txt'); git('commit', '--quiet', '-m', 'delivery fixture')
            source = git('rev-parse', 'HEAD')
            output = root / 'changes.zip'
            with patch.object(exporter, 'ROOT', repo), patch.object(builder, 'ROOT', repo), \
                    patch.object(exporter, 'helper', return_value=builder):
                result = exporter.export(output, source, base)
            with zipfile.ZipFile(output) as archive:
                bundle = root / 'source.bundle'
                bundle.write_bytes(archive.read(exporter.PREFIX + 'PRODIMT_I22.bundle'))
                difference = archive.read(exporter.PREFIX + 'PRODIMT_I22.patch').decode()
                self.assertIn('-baseline', difference)
                self.assertIn('+delivery fixture', difference)
            subprocess.run(['git', 'clone', '--quiet', '--branch', 'codex/i22-driver-van-deliveries',
                            str(bundle), str(root / 'restored')], check=True)
            restored = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root / 'restored', text=True).strip()
            self.assertEqual(restored, source)
            self.assertTrue(result['selfContainedBundle'])


if __name__ == '__main__':
    unittest.main()
