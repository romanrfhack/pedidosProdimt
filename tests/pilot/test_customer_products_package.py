"""I16 package/native guards. Synthetic files only; no VPS or credentials."""
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


I = module('i16_native_fixture', 'install-customer-products-update.py')
B = module('i16_package_fixture', 'build-customer-products-package.py')
C = module('i16_commercial_fixture', 'install-commercial-catalog-update.py')
SOURCE = 'a' * 40
KEY = base64.b64encode(bytes(range(32))).decode()
ENV = ('ASPNETCORE_ENVIRONMENT=Production\nCustomerAccessTokens__EncryptionKey=' + KEY + '\n').encode()
COUNTS = dict(customers=7, products=5, profiles=12, orders=3, lines=5, administrators=1, audits=8, order_audits=4, machines=2)
MONETARY = dict(books=5, revisions=9, consolidations=2, priced_lines=3)
TOKENS = dict(tokens=5, active_tokens=3, active_legacy_tokens=0, protected_tokens=5, active_protected_tokens=3,
              metadata_versions=9, invalid_metadata=0, upgrade_audits=4)
FINGERPRINT = dict(tokens='A' * 64, prices='B' * 64)


class CustomerProductsPackageTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.backend, self.frontend = self.root / 'backend', self.root / 'frontend'
        self.backend.mkdir(); self.frontend.mkdir()
        for name, raw in {'Prodimt.Pedidos.Api.dll': b'synthetic-assembly',
            'Prodimt.Pedidos.Api.runtimeconfig.json': b'{}', 'appsettings.json': b'{}'}.items():
            (self.backend / name).write_bytes(raw)
        (self.frontend / 'index.html').write_text('<script src="main.js"></script>')
        (self.frontend / 'main.js').write_text('apiBaseUrl:""; /access-tokens /reveal expectedVersion /prices /additional-products /product-setups Pedir otro producto useCustomerDefaultMachine')
        (self.frontend / '3rdpartylicenses.txt').write_text('Synthetic license fixture')
        self.sql = ROOT / 'tests/pilot/fixtures/i16-additional-products-migration.sql'
        self.bundle, self.payload = self.root / 'bundle.zip', self.root / 'payload.zip'
        self.addCleanup(patch.stopall)
        patch.object(B, 'validate_source').start()
        patch.object(I, 'EXPECTED_SOURCE', SOURCE).start()
        patch.object(I, 'ENV_FILE', self.root / 'api.env').start()
        patch.object(I, 'BACKUPS', self.root / 'backups').start()
        patch.object(I, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()).start()

    def build(self):
        report = B.build(self.backend, self.frontend, self.sql, self.bundle, SOURCE)
        with zipfile.ZipFile(self.bundle) as archive:
            self.payload.write_bytes(archive.read(B.PREFIX + 'prodimt-customer-products-payload.zip'))
        patch.object(I, 'EXPECTED_PAYLOAD_SHA256', report['payloadSha256']).start()
        return report

    def instance(self):
        self.build()
        instance = I.make_installer(self.payload)
        instance.inspector = types.SimpleNamespace(query_sql=lambda tool, fields, query:
            dict(C.EXPECTED_PERMISSIONS, migrations_count=14) if query == C.PERMISSION_QUERY else
            C.EXPECTED_PRICE_PERMISSIONS.copy() if query == C.PRICE_PERMISSIONS else I.EXPECTED_SETUP_PERMISSIONS.copy())
        instance.sql_tool, instance.fields = 'synthetic-sqlcmd', {}
        instance.environment, instance.token_encryption_key = ENV, KEY
        I.ENV_FILE.write_bytes(ENV); I.ENV_FILE.chmod(0o600)
        patch.object(I, 'validate_environment_file', side_effect=lambda path:
            I.require(not path.is_symlink() and stat.S_IMODE(path.stat().st_mode) == 0o600, 'environment_file_identity_unexpected')).start()
        return instance

    def migration_fixture(self, before=13):
        instance = self.instance()
        instance.state['migrations_before'] = before
        instance.verify_schema = Mock(side_effect=[before, 14])
        events = []
        def backup():
            events.append('backup-verified')
            instance.state['verified_database_backup'] = '/synthetic/verified-copy-only.bak'
        instance.database_backup = backup
        setup = dict(requests=0, links=0, open_requests=0, inherit_default=12, independent=0) if before == 13 else dict(
            requests=2, links=3, open_requests=1, inherit_default=11, independent=1)
        data = {instance.runtime.COUNTS_QUERY: COUNTS, C.PRICE_COUNTS: MONETARY,
                I.TOKEN_COUNTS: TOKENS, I.DATA_FINGERPRINT: FINGERPRINT, I.SETUP_COUNTS: setup}
        instance.sql_json = lambda sql: dict(data[sql])
        instance.sql_run = lambda sql, **kwargs: events.append(('migration', sql))
        return instance, events

    def test_deterministic_bundle_pins_source_payload_every_file_and_preserves_tokens(self):
        report = self.build(); self.assertEqual(report, self.build())
        with zipfile.ZipFile(self.bundle) as archive:
            script = archive.read(B.PREFIX + 'PRODIMT_Instalar_I16.py').decode()
            self.assertIn(SOURCE, script); self.assertIn(report['payloadSha256'], script); self.assertNotIn('REPLACE_', script)
            for row in archive.read(B.PREFIX + 'SHA256SUMS.txt').decode().splitlines():
                expected, name = row.split('  ', 1)
                self.assertEqual(expected, B.digest(archive.read(B.PREFIX + name)))
        with zipfile.ZipFile(self.payload) as archive:
            manifest = json.loads(archive.read('manifest.json'))
        self.assertEqual((13,14,I.PREVIOUS), (manifest['migrationsBefore'],manifest['migrationsAfter'],manifest['previousReleases']))
        self.assertFalse(manifest['revokeLegacyTokens']); self.assertFalse(manifest['encryptionKeyInPackage'])
        self.assertIn('migrations-i16.sql', I.load_payload(self.payload))

    def test_builder_rejects_key_debug_source_and_unrelated_sql(self):
        (self.backend / 'appsettings.json').write_text(json.dumps({'CustomerAccessTokens': {'EncryptionKey': KEY}}))
        with self.assertRaisesRegex(ValueError, 'encryption_key_must_not_be_packaged'): self.build()
        (self.backend / 'appsettings.json').write_text('{}')
        (self.backend / 'private.csv').write_text('private')
        with self.assertRaisesRegex(ValueError, 'build_private_or_source_file_not_allowed'): self.build()
        (self.backend / 'private.csv').unlink()
        for mutation in ('UPDATE dbo.CustomerAccessTokens SET IsActive=0;', 'DELETE FROM dbo.Orders;', 'EXEC unsafe;', 'DROP TABLE dbo.Products;'):
            with self.subTest(mutation=mutation), self.assertRaisesRegex(ValueError, 'incremental_migration_unexpected'):
                B.validate_migration(self.sql.read_text(encoding='utf-8-sig') + mutation)

    def test_loader_rejects_tampering_before_executing_support(self):
        self.build(); raw=self.payload.read_bytes(); self.payload.write_bytes(raw+b'changed')
        with self.assertRaisesRegex(I.InstallError,'payload_hash_mismatch'): I.load_payload(self.payload)

    def test_minimal_grants_and_ddl_commit_atomically_across_sqlcmd_batch(self):
        raw = self.sql.read_text(encoding='utf-8-sig'); fixed=I.migration_sql_for_sqlcmd(raw)
        self.assertEqual(1, fixed.count('BEGIN TRANSACTION;')); self.assertEqual(1, fixed.count('COMMIT;'))
        self.assertEqual(2, fixed.count('\nGO\n'))
        self.assertLess(fixed.index('CREATE TABLE'),fixed.index('\nGO\n'))
        self.assertLess(fixed.index('\nGO\n'),fixed.index('GRANT SELECT'))
        self.assertLess(fixed.index('DENY UPDATE, DELETE'),fixed.index('COMMIT;'))
        self.assertNotIn('UPDATE dbo.CustomerAccessTokens',fixed)
        with self.assertRaises(I.InstallError): I.migration_sql_for_sqlcmd(raw.replace('COMMIT;','COMMIT;\nGO'))

    def test_key_parser_requires_single_canonical_existing_key(self):
        self.assertEqual(KEY,I.environment_key(ENV)); self.assertIsNone(I.environment_key(b'OTHER=value\n'))
        for raw in (ENV+ENV, ENV.replace(KEY.encode(),b'wrong-key')):
            with self.assertRaises(I.InstallError): I.environment_key(raw)

    def test_application_shape_checks_do_not_require_definition_visibility_but_admin_checks_remain_strict(self):
        for sql in (I.TOKEN_SHAPE,I.SETUP_SHAPE):
            self.assertNotIn('d.definition',sql); self.assertNotIn('LOWER(definition)',sql)
        self.assertIn('d.definition',I.TOKEN_SCHEMA)
        self.assertIn('d.definition',I.SETUP_SCHEMA); self.assertIn('LOWER(definition)',I.SETUP_SCHEMA)
        self.assertNotIn('VIEW DEFINITION',I.SETUP_GRANTS)

    def test_migrate_only_after_verified_backup_preserves_every_count_and_key(self):
        instance, events = self.migration_fixture()
        instance.backup_and_migrate()
        self.assertEqual('backup-verified',events[0]); self.assertEqual(2,len(events))
        self.assertEqual(I.migration_sql_for_sqlcmd(self.sql.read_text(encoding='utf-8-sig')),events[1][1])
        self.assertTrue(instance.state['tokens_preserved']); self.assertTrue(instance.state['machine_inheritance_preserved'])
        self.assertTrue(instance.state['database_migrations_run']); self.assertEqual(14,instance.state['migrations_after'])
        self.assertEqual(ENV,I.ENV_FILE.read_bytes()); self.assertNotIn(KEY,json.dumps(instance.state))

    def test_resume_after_committed_schema_verifies_existing_requests_without_replaying_migration(self):
        instance, events = self.migration_fixture(14); instance.backup_and_migrate()
        self.assertEqual(['backup-verified'],events); self.assertFalse(instance.state['database_migrations_run'])
        self.assertTrue(instance.state['tokens_preserved']); self.assertTrue(instance.state['setup_permissions_verified'])

    def test_missing_verified_backup_blocks_migration(self):
        instance, events = self.migration_fixture(); instance.database_backup = lambda:None
        with self.assertRaisesRegex(I.InstallError,'verified_database_backup_required'): instance.backup_and_migrate()
        self.assertEqual([],events); self.assertFalse(instance.state['database_migrations_run'])

    def test_environment_drift_after_backup_blocks_sql_and_retains_file(self):
        instance, events = self.migration_fixture(); I.ENV_FILE.write_bytes(ENV+b'OPERATOR=value\n')
        with self.assertRaisesRegex(I.InstallError,'environment_or_token_key_changed'): instance.backup_and_migrate()
        self.assertEqual(['backup-verified'],events); self.assertTrue(I.ENV_FILE.read_bytes().endswith(b'OPERATOR=value\n'))

    def test_changed_token_or_price_fingerprint_blocks_success(self):
        instance, _ = self.migration_fixture(); actual=instance.sql_json; reads=0
        def query(sql):
            nonlocal reads
            if sql==I.DATA_FINGERPRINT:
                reads+=1
                if reads>1:return FINGERPRINT|{'tokens':'C'*64}
            return actual(sql)
        instance.sql_json=query
        with self.assertRaisesRegex(I.InstallError,'existing_data_changed'): instance.backup_and_migrate()
        self.assertFalse(instance.state['tokens_preserved'])

    def test_readonly_preflight_uses_real_process_config_query_allowlist_and_already_current(self):
        self.build()
        for index,(count,own,key) in enumerate(((13,False,True),(14,False,True),(14,True,True),(13,False,False))):
            with self.subTest(count=count,own=own,key=key):
                app,backups,env=(self.root/(name+str(index)) for name in ('app','backups','env'))
                backups.mkdir(); env.write_bytes(ENV if key else b'ASPNETCORE_ENVIRONMENT=Production\n');env.chmod(0o600)
                with patch.object(I,'ROOT',app),patch.object(I,'BACKUPS',backups),patch.object(I,'ENV_FILE',env):
                    instance=I.make_installer(self.payload); old={}
                    for part in ('backend','frontend'):
                        folder=app/part/'releases'/('customer-products-'+SOURCE[:12] if own else I.PREVIOUS[part])
                        folder.mkdir(parents=True);old[part]=folder;(app/part/'current').symlink_to(folder)
                        for name,raw in instance.files.items():
                            if name.startswith(part+'/'):
                                target=folder/name.removeprefix(part+'/');target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw)
                    pid,tool,private=424242,'/opt/mssql-tools/bin/sqlcmd','synthetic-sql-private'
                    argv=['/usr/bin/dotnet',str(old['backend']/'Prodimt.Pedidos.Api.dll')]
                    environment={'ASPNETCORE_ENVIRONMENT':'Production','ConnectionStrings__Pedidos':
                        'Server=127.0.0.1,14330;Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password='+private+';',
                        'Authentication__Jwt__SigningKey':'synthetic-signing-key-'+'a'*48,'Authentication__Jwt__AccessTokenMinutes':'720'}
                    if key:environment[I.KEY_ENV]=KEY
                    proc=('\0'.join(name+'='+value for name,value in environment.items())+'\0').encode()
                    real_open,real_is_file=Path.open,Path.is_file; checked=[]
                    inventory=module('i16_expected_query_inventory','inspect-native.py')
                    history=instance.runtime.MIGRATIONS+[I.PRICE_MIGRATION,I.TOKEN_MIGRATION,I.MIGRATION]
                    allowed={C.PERMISSION_QUERY:dict(C.EXPECTED_PERMISSIONS,migrations_count=count),C.NEW_SCHEMA:C.EXPECTED_SCHEMA,
                        C.PRICE_PERMISSIONS:C.EXPECTED_PRICE_PERMISSIONS,I.TOKEN_SHAPE:I.EXPECTED_TOKEN_SCHEMA,
                        I.SETUP_SHAPE:I.EXPECTED_SETUP_SCHEMA if count==14 else dict.fromkeys(I.EXPECTED_SETUP_SCHEMA,0),
                        I.SETUP_PERMISSIONS:I.EXPECTED_SETUP_PERMISSIONS,inventory.SQL_MIGRATIONS:[{'MigrationId':m} for m in history[:count]]}
                    def proc_path(value):
                        return types.SimpleNamespace(resolve=lambda strict=True:old['backend']) if str(value)==f'/proc/{pid}/cwd' else Path(value)
                    def read_file(path,limit=0):
                        return ('\0'.join(argv)+'\0').encode() if str(path)==f'/proc/{pid}/cmdline' else Path(path).read_bytes()
                    def open_file(path,*args,**kwargs):return io.BytesIO(proc) if str(path)==f'/proc/{pid}/environ' else real_open(path,*args,**kwargs)
                    def execute(args,**kwargs):
                        if args[:2]==['systemctl','show']:
                            values=dict(ActiveState='active',MainPID=str(pid),User='www-data',Group='',WorkingDirectory=str(app/'backend/current'),EnvironmentFiles=str(env)+' (ignore_errors=no)')
                            return subprocess.CompletedProcess(args,0,'\n'.join(k+'='+v for k,v in values.items()),'')
                        if args[0]==tool:
                            wrapped=args[args.index('-Q')+1]
                            query=next((q for q in allowed if wrapped.endswith('SELECT ('+q.rstrip().removesuffix(';')+') AS ProdimtInspectionJson;')),None)
                            self.assertIsNotNone(query);self.assertNotIn(private,' '.join(args))
                            return subprocess.CompletedProcess(args,0,'ProdimtInspectionJson\n---------------------\n'+json.dumps(allowed[query]),'')
                        if args[0]=='curl':
                            path=next(v for v in args if v.startswith('https://')).removeprefix('https://'+I.DOMAIN)
                            body,status=('{}',401) if path.startswith('/api/') else ('{"database":"reachable"}',200) if path=='/health/db' else ((old['frontend']/('index.html' if path.startswith('/admin/') else path.removeprefix('/'))).read_text(),200)
                            return subprocess.CompletedProcess(args,0,body+'\n'+str(status),'')
                        raise AssertionError('unexpected mutation or transport')
                    with patch.object(I,'validate_environment_file'),patch.object(instance.runtime,'Path',side_effect=proc_path), \
                        patch.object(instance.runtime,'regular',side_effect=read_file), \
                        patch.object(instance.runtime,'directory_safe',side_effect=lambda p:self.assertTrue(p.is_dir())), \
                        patch.object(instance.runtime,'prompt_secret',side_effect=AssertionError('read-only credentials prompt')), \
                        patch.object(Path,'open',new=open_file),patch.object(Path,'is_file',new=lambda p:str(p)==tool or real_is_file(p)), \
                        patch.object(subprocess,'run',side_effect=execute) as transport, \
                        patch.object(instance,'verify_stage',side_effect=lambda part:checked.append(part)):
                        if not key:
                            with self.assertRaisesRegex(I.InstallError,'token_encryption_key_missing_or_conflicting'):instance.preflight()
                        else:
                            instance.preflight();self.assertEqual('already_current' if own else 'planned',instance.state['status'])
                            self.assertEqual(['backend','frontend'] if own else [],checked)
                            before=transport.call_count
                            with self.assertRaisesRegex(instance.inspector.InspectionError,'sql_query_not_authorized'):
                                instance.inspector.query_sql(instance.sql_tool,instance.fields,'SELECT 1;')
                            self.assertEqual(before,transport.call_count)
                        self.assertFalse(instance.state['changes_made']);self.assertFalse(instance.state['service_restarted'])
                        self.assertNotIn(KEY,json.dumps(instance.state));self.assertNotIn(private,json.dumps(instance.state))

    def test_rollback_only_restores_links_service_and_health_never_database_or_key(self):
        instance=self.instance();instance.stop_requested=True
        instance.old={'backend':Path('/synthetic/old-backend'),'frontend':Path('/synthetic/old-frontend')}
        instance.switch=Mock();instance.health=Mock()
        with patch.object(instance.runtime,'command') as command:
            instance.rollback()
        self.assertEqual(['stop','start'],[call.args[0][1] for call in command.call_args_list])
        instance.switch.assert_called_once_with(instance.old);instance.health.assert_called_once_with(False)
        self.assertFalse(instance.state['database_automatically_restored']);self.assertEqual(ENV,I.ENV_FILE.read_bytes())

    def test_stage_drift_is_checked_before_stopping_application(self):
        instance=self.instance();instance.preflight=Mock();instance.authenticate_admin=Mock()
        I.BACKUPS.mkdir();instance.old={part:self.backend if part=='backend' else self.frontend for part in ('backend','frontend')}
        instance.overrides={};instance.stage=Mock();instance.links=Mock();instance.process=Mock();instance.original_dll=b'synthetic-assembly'
        instance.stage.side_effect=lambda:I.ENV_FILE.write_bytes(ENV+b'CHANGED=yes\n')
        with patch.object(instance.runtime,'command') as command:
            with self.assertRaisesRegex(I.InstallError,'configuration_or_backend_changed_during_staging'):instance.apply()
        command.assert_not_called();self.assertFalse(instance.stop_requested)


if __name__=='__main__':unittest.main()
