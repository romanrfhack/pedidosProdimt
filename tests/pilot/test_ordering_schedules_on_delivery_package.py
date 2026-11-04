"""Regression guards for installing schedules after an already deployed delivery release."""
import importlib.util
import json
import tempfile
import types
import unittest
import zipfile
from pathlib import Path
from unittest.mock import Mock, patch
import test_ordering_schedules_package as previous
import test_production_package as historical

ROOT = Path(__file__).resolve().parents[2]
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result

installer = load('i23_native_installer', ROOT / 'scripts/pilot/install-ordering-schedules-on-delivery-update.py')
builder = load('i23_native_builder', ROOT / 'scripts/pilot/build-ordering-schedules-on-delivery-package.py')
validation = load('i23_exact_sql_validator', ROOT / 'scripts/pilot/validate-ordering-schedules-sql.py')
schema = load('i23_schema_contract', ROOT / 'scripts/pilot/delivery-schema-contract.py')
def supports():
    files = historical.supports()
    files.update({
        'migrations-i23.sql': previous.sql().encode(),
        'delivery-support.py': (ROOT / 'scripts/pilot/install-delivery-update.py').read_bytes(),
        'delivery-schema-contract.py': (ROOT / 'scripts/pilot/delivery-schema-contract.py').read_bytes(),
        'validate-ordering-schedules-sql.py': (ROOT / 'scripts/pilot/validate-ordering-schedules-sql.py').read_bytes(),
        'delivery-schema.json': json.dumps(schema.contract_from_sql((ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text())).encode(),
        'validation-baseline.sql': baseline().encode(),
    })
    return files

def baseline():
    return ''.join("BEGIN TRANSACTION;\nINSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\nVALUES (N'" + value + "', N'10.0.8');\nCOMMIT;\nGO\n\n" for value in validation.BASE_IDS)

class SchedulesOnDeliveryTests(unittest.TestCase):
    def instance(self):
        with patch.object(installer, 'load_payload', return_value=supports()):
            value = installer.make_installer(Path('/synthetic-package'))
        value.inspector = type('Inventory', (), {'SQL_MIGRATIONS': 'history'})
        return value

    def test_only_known_delivery_and_rf1_pairs_are_supported(self):
        self.assertEqual(installer.PREVIOUS_PAIRS, (('delivery-0cfe3ef511d8', 'delivery-0cfe3ef511d8'), ('delivery-0cfe3ef511d8', 'delivery-flow-426c360e1365')))
        pairs = list(installer.PREVIOUS_PAIRS) + [('production-d3a11420b202', 'orders-catalog-867df56de3bb'), ('delivery-unknown', 'delivery-unknown'), ('delivery-0cfe3ef511d8', 'delivery-flow-unknown')]
        for pair in pairs:
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for part, name in zip(('backend', 'frontend'), pair):
                    release = root / part / 'releases' / name
                    release.mkdir(parents=True)
                    (root / part / 'current').symlink_to(release)
                with patch.object(installer, 'ROOT', root), patch.object(installer, 'BACKUPS', root / 'backups'):
                    i = self.instance()
                    i.runtime.directory_safe = Mock()
                    i.process = Mock(side_effect=installer.InstallError('accepted'))
                    with self.assertRaises(installer.InstallError) as error:
                        i.preflight()
                    self.assertEqual(str(error.exception), 'accepted' if pair in installer.PREVIOUS_PAIRS else 'previous_release_not_supported')
                    self.assertFalse(i.stop_requested)
                    self.assertFalse(i.state['changes_made'])

    def test_history_rejects_same_length_alternative_i22_and_unknown_migrations(self):
        i = self.instance()
        ids = validation.BASE_IDS
        for values in (ids, ids + [installer.MIGRATION]):
            self.assertEqual(i.history(lambda _: [{'MigrationId': value} for value in values]), values)
        for values in (ids[:-1], ids[:-1] + [installer.MIGRATION], ids + ['unknown'], ids + [installer.MIGRATION, 'unknown']):
            with self.assertRaises(installer.InstallError):
                i.history(lambda _: [{'MigrationId': value} for value in values])

    def test_only_additive_schedule_delta_and_its_grants_are_executed(self):
        text = installer.migration_sql_for_sqlcmd(supports(), installer.MIGRATION)
        self.assertIn('CREATE TABLE [RouteOrderSchedules]', text)
        self.assertNotIn('CREATE TABLE [Delivery', text)
        self.assertNotIn('SecurityRoles', text)
        self.assertIn("IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory", text)
        self.assertIn("EXEC(N'ALTER TABLE [Customers]", text)
        self.assertEqual(text.count("EXEC(N'"), 5)
        self.assertEqual(text.count('BEGIN TRANSACTION;'), 1)
        self.assertEqual(text.count('COMMIT;'), 1)
        import re
        executed = [part.replace("''", "'") for part in re.findall(r"EXEC\(N'((?:''|[^'])*)'\);", text)]
        original = previous.sql().removeprefix('BEGIN TRANSACTION;\n').removesuffix('COMMIT;\nGO\n\n')
        self.assertEqual(executed, [part.strip()+';' for part in original.split(';') if part.strip()])
        self.assertLess(text.index(installer.SCHEDULE_GRANTS), text.index('COMMIT;'))
        for text in (previous.sql().replace('COMMIT;', 'DROP TABLE dbo.DeliveryEvents; COMMIT;'), previous.sql().replace(installer.MIGRATION, installer.DELIVERY_MIGRATION)):
            with self.assertRaises(installer.InstallError):
                installer.validate_migration_sql(text)

    def migration_fixture(self, changed_delivery=False):
        i = self.instance()
        ids = validation.BASE_IDS.copy()
        i.state['migration_ids_before'] = ids.copy()
        i.schemas = Mock(return_value=ids.copy())
        i.verify_schema = Mock(return_value=18)
        i.verify_security = Mock()
        i.app_permissions = Mock()
        raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
        i.environment = raw
        i.token_encryption_key = historical.token.environment_key(raw)
        events = []
        mapping = {i.runtime.COUNTS_QUERY: dict.fromkeys(('customers','orders','lines','administrators','audits','order_audits','profiles','products','machines'), 1),
            historical.commercial.PRICE_COUNTS: {}, historical.token.TOKEN_COUNTS: {},
            historical.setup.DATA_FINGERPRINT: {}, historical.setup.SETUP_COUNTS: {}, installer.SECURITY_FINGERPRINT: {'all_security': 'original'}}
        def query(text):
            if text == 'history':
                return [{'MigrationId': value} for value in ids]
            if text == installer.DELIVERY_FINGERPRINT:
                return {'ledger': 'modified' if changed_delivery and installer.MIGRATION in ids else 'original'}
            return mapping[text]
        i.sql_json = query
        def backup():
            events.append('verified-backup')
            i.state['verified_database_backup'] = '/synthetic-verified-backup'
        i.database_backup = backup
        def execute(text, timeout):
            self.assertEqual(events, ['verified-backup'])
            self.assertIn('CREATE TABLE [RouteOrderSchedules]', text)
            self.assertNotIn('CREATE TABLE [Delivery', text)
            ids.append(installer.MIGRATION)
            events.append('schedule-delta')
        i.sql_run = Mock(side_effect=execute)
        return i, raw, events

    def test_backup_precedes_17_to_18_and_delivery_is_preserved(self):
        i, raw, events = self.migration_fixture()
        with patch.object(installer, 'regular', return_value=raw):
            i.backup_and_migrate()
        self.assertEqual(events, ['verified-backup', 'schedule-delta'])
        self.assertEqual(i.state['migrations_after'], 18)
        self.assertTrue(i.state['existing_delivery_preserved'])
        self.assertTrue(i.state['tokens_preserved'])

    def test_any_unexpected_delivery_mutation_blocks_activation(self):
        i, raw, _ = self.migration_fixture(changed_delivery=True)
        with patch.object(installer, 'regular', return_value=raw):
            with self.assertRaisesRegex(installer.InstallError, 'existing_delivery_changed'):
                i.backup_and_migrate()
        self.assertNotIn('migrated_schema_verified', i.state)

    def test_missing_verified_backup_prevents_ddl(self):
        i, raw, _ = self.migration_fixture()
        i.database_backup = Mock()
        with patch.object(installer, 'regular', return_value=raw):
            with self.assertRaisesRegex(installer.InstallError, 'verified_database_backup_required'):
                i.backup_and_migrate()
        i.sql_run.assert_not_called()

    def test_sql_isolated_failure_precedes_snapshot_stage_marker_and_stop(self):
        i = self.instance()
        i.preflight = Mock()
        i.authenticate_admin = Mock()
        i.validate_before_apply = Mock(side_effect=installer.InstallError('validation_failed'))
        i.stage, i.write_marker = Mock(), Mock()
        with self.assertRaisesRegex(installer.InstallError, 'validation_failed'):
            i.apply()
        i.stage.assert_not_called()
        i.write_marker.assert_not_called()
        self.assertFalse(i.stop_requested)
        self.assertFalse(i.state['changes_made'])

    def test_post_ddl_failure_keeps_forward_recovery_instead_of_old_binary(self):
        i = self.instance()
        i.stop_requested = True
        i.history_before = validation.BASE_IDS
        i.history = Mock(return_value=validation.BASE_IDS + [installer.MIGRATION])
        i.sql_json, i.runtime.command = Mock(), Mock()
        i.rollback()
        self.assertTrue(i.state['recovery_pending'])
        self.assertTrue(i.state['application_stopped_for_security'])
        self.assertFalse(i.state['database_automatically_restored'])

    def test_security_fingerprint_includes_delivery_grants_and_revision(self):
        self.assertNotIn('NOT IN', installer.SECURITY_FINGERPRINT)
        self.assertIn('SELECT * FROM dbo.SecurityStates', installer.SECURITY_FINGERPRINT)
        self.assertIn('SELECT * FROM dbo.SecurityRolePermissions', installer.SECURITY_FINGERPRINT)
        for table in installer.DELIVERY_TABLES:
            self.assertIn('SELECT * FROM dbo.' + table, installer.DELIVERY_FINGERPRINT)
        self.assertIn('CustomerDeliveryLocations ORDER BY CustomerId', installer.DELIVERY_FINGERPRINT)
        self.assertIn('DeliveryRoutes ORDER BY Code', installer.DELIVERY_FINGERPRINT)

    def test_disposable_sql_target_cannot_be_production_or_user_selected_name(self):
        for value in ('ProdimtPedidos', 'master', 'ProdimtI23Validation_user', 'ProdimtI23Validation_' + 'a' * 32 + ';DROP DATABASE x'):
            self.assertFalse(validation.generated_name(value))
            with self.assertRaises(validation.ValidationFailure):
                validation.Transport('/tool', 'server', 'login', 'password', '', {}, lambda _: {}, value)
        self.assertTrue(validation.generated_name('ProdimtI23Validation_' + 'a' * 32))

    def test_exported_builder_pins_merged_payload_and_rejects_corruption(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backend, frontend, verification = root/'backend', root/'frontend', root/'validation'
            backend.mkdir(); frontend.mkdir(); verification.mkdir()
            for name in ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Api.runtimeconfig.json', 'appsettings.json'):
                (backend/name).write_text('{}')
            for name in installer.VALIDATED_ASSEMBLIES:
                (backend/name).write_text('{}'); (verification/name).write_text('{}')
            for name in installer.VALIDATION_REQUIRED:
                (verification/Path(name).name).write_text('{}')
            (frontend/'index.html').write_text('<html></html>')
            (frontend/'3rdpartylicenses.txt').write_text('Synthetic notices')
            (frontend/'main.js').write_text('apiBaseUrl:"" /access-tokens /reveal /prices /security directPermissionIds roleIds /product-setups /additional-products /operations/production requiresMachine /ordering-schedules orderingWindow delivery.read.own delivery.manage /delivery /personal/reparto Preparar reparto')
            schedule, identity, production, base = [root/name for name in ('schedule.sql','identity.sql','production.sql','baseline.sql')]
            schedule.write_text(previous.sql()); identity.write_text(historical.identity_fixture.migration())
            production.write_text(historical.production_sql()); base.write_text(baseline())
            output = root/'package.zip'
            with patch.object(builder, 'validate_source'), patch.object(builder, 'normalize_baseline', side_effect=lambda value: value):
                result = builder.build(backend, frontend, schedule, output, 'a'*40, ROOT/'tests/pilot/fixtures/i16-additional-products-migration.sql', identity, production, base, verification)
            with zipfile.ZipFile(output) as outer:
                exported = root/'exported.py'; exported.write_bytes(outer.read(builder.PREFIX+'PRODIMT_Instalar_I23.py'))
                payload = root/'payload.zip'; payload.write_bytes(outer.read(builder.PREFIX+'prodimt-ordering-schedules-i23-payload.zip'))
            module = load('i23_exported', exported)
            with patch.object(module, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()):
                self.assertEqual(len(module.load_payload(payload)), result['files'])
                payload.write_bytes(payload.read_bytes()+b'corrupt')
                with self.assertRaisesRegex(module.InstallError, 'payload_hash_mismatch'):
                    module.load_payload(payload)

    def test_corrupt_validation_assembly_is_rejected_before_runtime_start(self):
        # The source-pinned builder must reject testing binaries different from
        # those it would install, rather than validating an older application.
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backend, frontend, verification = [root/name for name in ('backend','frontend','validation')]
            for directory in (backend,frontend,verification): directory.mkdir()
            for name in installer.VALIDATED_ASSEMBLIES:
                (backend/name).write_text('new'); (verification/name).write_text('older')
            for name in ('Prodimt.Pedidos.Api.runtimeconfig.json','appsettings.json'):
                (backend/name).write_text('{}')
            for name in installer.VALIDATION_REQUIRED:
                (verification/Path(name).name).write_text('{}')
            (frontend/'index.html').write_text('<html></html>'); (frontend/'3rdpartylicenses.txt').write_text('fixture')
            sql_files = [root/name for name in ('schedule.sql','identity.sql','production.sql','baseline.sql')]
            for path, text in zip(sql_files, (previous.sql(),historical.identity_fixture.migration(),historical.production_sql(),baseline())):
                path.write_text(text)
            with patch.object(builder,'validate_source'), patch.object(builder,'normalize_baseline',side_effect=lambda value:value):
                with self.assertRaisesRegex(ValueError,'validation_application_binary_mismatch'):
                    builder.build(backend,frontend,sql_files[0],root/'result.zip','a'*40,
                        ROOT/'tests/pilot/fixtures/i16-additional-products-migration.sql',sql_files[1],sql_files[2],sql_files[3],verification)

    def test_cleanup_is_mandatory_even_when_isolated_baseline_fails(self):
        events = []
        class BrokenBaseline:
            def __init__(self,*args): self.database=args[-1]
            def run(self,database,sql,**kwargs):
                events.append(sql)
                if database != 'master': raise validation.ValidationFailure('validation_sql_operation_failed')
                return '{}'
        operations = type('Operations',(),dict(
            migration_sql_for_sqlcmd=staticmethod(lambda _: installer.migration_sql_for_sqlcmd(supports(),installer.MIGRATION)),
            EXPECTED_SCHEDULE_SHAPE=installer.EXPECTED_SCHEDULE_SHAPE))
        with patch.object(validation,'cleanup_generated_database',return_value=True) as cleanup:
            with self.assertRaisesRegex(validation.ValidationFailure,'validation_sql_operation_failed'):
                validation.validate_exact_sql(supports(),operations,schema,'tool','server','user','password','',{},lambda _: {},BrokenBaseline)
            cleanup.assert_called_once()
            self.assertRegex(cleanup.call_args.args[7],r'^ProdimtI23Validation_[a-f0-9]{32}$')
        with patch.object(validation,'cleanup_generated_database',return_value=False):
            with self.assertRaisesRegex(validation.ValidationFailure,'validation_database_cleanup_failed') as error:
                validation.validate_exact_sql(supports(),operations,schema,'tool','server','user','password','',{},lambda _: {},BrokenBaseline)
            self.assertTrue(validation.generated_name(error.exception.cleanup_database))

    def test_overlay_accepts_only_the_reviewed_old_or_current_validation_helper(self):
        import subprocess
        legacy = subprocess.check_output(['git','show','aa802e2:scripts/pilot/validate-ordering-schedules-sql.py'],cwd=ROOT)
        current = (ROOT/'scripts/pilot/validate-ordering-schedules-sql.py').read_bytes()
        self.assertEqual(installer.adapt_ordering_validation_source(legacy), current)
        self.assertEqual(installer.adapt_ordering_validation_source(current), current)
        with self.assertRaisesRegex(installer.InstallError,'ordering_validation_helper_unexpected'):
            installer.adapt_ordering_validation_source(legacy+b'unknown edit')

    def probe(self, stdout, stderr, code=1):
        transport = validation.Transport('/tool','server','user','private-credential','',{},lambda _: {},'ProdimtI23Validation_'+'a'*32)
        transport.phase = 'rollback-probe'
        response = types.SimpleNamespace(returncode=code,stdout=stdout,stderr=stderr)
        with patch.object(validation.subprocess,'run',return_value=response):
            return transport.run(transport.database,'synthetic rollback probe',expected_failure=True)

    def test_expected_sql_error_and_marker_can_arrive_on_either_output_stream(self):
        message = 'Msg 51023, Level 16, State 1, Line 1\n'+validation.ROLLBACK_MARKER+'\n'
        for stdout,stderr in ((message,''),('',message),(validation.ROLLBACK_MARKER+'\n','Msg 51023, Level 16, State 1\n')):
            with self.subTest(stderr=bool(stderr)):
                self.assertEqual(self.probe(stdout,stderr),'')

    def test_marker_alone_or_success_exit_does_not_prove_expected_rollback(self):
        for output,code in ((validation.ROLLBACK_MARKER,1),('Msg 51023, Level 16\n'+validation.ROLLBACK_MARKER,0)):
            with self.assertRaisesRegex(validation.ValidationFailure,'validation_expected_rollback_not_observed'):
                self.probe(output,'',code)

    def test_earlier_sql_failure_is_reported_without_private_messages(self):
        with self.assertRaises(validation.ValidationFailure) as result:
            self.probe('',"Msg 207, Level 16, State 1\nprivate-table-or-user-value\n")
        self.assertEqual(result.exception.sql_error_numbers,(207,))
        self.assertEqual(result.exception.phase,'rollback-probe')
        self.assertNotIn('private-table-or-user-value',str(result.exception))

    def test_unrelated_failure_cannot_hide_behind_the_expected_error_and_marker(self):
        with self.assertRaises(validation.ValidationFailure):
            self.probe('Msg 51023, Level 16\n'+validation.ROLLBACK_MARKER,'Msg 207, Level 16\n')

if __name__ == '__main__':
    unittest.main()
