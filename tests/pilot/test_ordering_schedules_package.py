import importlib.util
from pathlib import Path
import tempfile
import io
import json
import zipfile
import unittest
from unittest.mock import Mock,patch
import test_production_package as previous

ROOT=Path(__file__).resolve().parents[2]
def load(name,path):
 spec=importlib.util.spec_from_file_location(name,path);value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value);return value
installer=load('ordering_schedules_installer',ROOT/'scripts/pilot/install-ordering-schedules-update.py')
builder=load('ordering_schedules_builder',ROOT/'scripts/pilot/build-ordering-schedules-package.py')
def sql():return (ROOT/'tests/pilot/fixtures/i22-ordering-schedules-migration.sql').read_text(encoding='utf-8-sig')+'\n'
def supports():return previous.supports()|{'migrations-i22.sql':sql().encode()}

class OrderingSchedulesPackageTests(unittest.TestCase):
 def instance(self):
  with patch.object(installer,'load_payload',return_value=supports()): instance=installer.make_installer(Path('/synthetic-package'))
  instance.inspector=type('Inventory',(),{'SQL_MIGRATIONS':'history'})
  return instance
 def history(self,instance):return instance.runtime.MIGRATIONS+[previous.token.PRICE_MIGRATION,previous.token.MIGRATION,installer.SETUP_MIGRATION,installer.IDENTITY_MIGRATION,installer.PRODUCTION_MIGRATION]
 def test_generated_sql_is_additive_and_grants_share_its_transaction(self):
  installer.validate_migration_sql(sql());native=installer.migration_sql_for_sqlcmd(supports(),installer.MIGRATION)
  self.assertEqual(native.count('BEGIN TRANSACTION;'),1);self.assertEqual(native.count('COMMIT;'),1)
  self.assertLess(native.index(installer.SCHEDULE_GRANTS),native.index('COMMIT;'))
  for value in (sql().replace('ADD [OrderingScheduleVersion] int','ADD [Other] int'),sql().replace('COMMIT;','DELETE FROM dbo.Orders; COMMIT;'),sql().replace(installer.MIGRATION,'20261103030000_Unknown')):
   with self.assertRaises(installer.InstallError):installer.validate_migration_sql(value)
 def test_only_exact_i19_history_and_i22_are_recognized(self):
  i=self.instance();base=self.history(i)
  for ids in (base,base+[installer.MIGRATION]):self.assertEqual(ids,i.history(lambda _:[{'MigrationId':x} for x in ids]))
  for ids in (base[:-1],base[:-1]+['same-count-other-migration'],base+[installer.MIGRATION,'unknown']):
   with self.assertRaises(installer.InstallError):i.history(lambda _:[{'MigrationId':x} for x in ids])
 def test_new_table_cannot_be_deleted_by_application_account(self):
  self.assertEqual(installer.EXPECTED_SCHEDULE_PERMISSIONS,dict(SELECT=1,INSERT=1,UPDATE=1,DELETE=0))
 def test_unknown_or_parallel_release_pair_stops_before_changes(self):
  cases=[(pair,True) for pair in installer.PREVIOUS_PAIRS]+[(('identity-c49df453de9d','orders-catalog-867df56de3bb'),False)]
  for pair,accepted in cases:
   with tempfile.TemporaryDirectory() as folder:
    root=Path(folder)
    for part,name in zip(('backend','frontend'),pair):
     target=root/part/'releases'/name;target.mkdir(parents=True);(root/part/'current').symlink_to(target)
    with patch.object(installer,'ROOT',root):
     i=self.instance();i.runtime.directory_safe=Mock();i.process=Mock(side_effect=installer.InstallError('accepted'))
     with self.assertRaises(installer.InstallError) as error:i.preflight()
     self.assertEqual(str(error.exception),'accepted' if accepted else 'previous_release_not_supported');self.assertFalse(i.stop_requested);self.assertFalse(i.state['changes_made'])
 def test_verified_backup_precedes_only_new_migration_and_preserves_tokens_security_prices(self):
  i=self.instance();ids=self.history(i);i.state['migration_ids_before']=ids.copy();i.schemas=Mock(return_value=ids.copy());i.verify_schema=Mock(return_value=17);i.verify_security=Mock();i.app_permissions=Mock()
  raw=b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
  i.environment=raw;i.token_encryption_key=previous.token.environment_key(raw);events=[]
  def backup():events.append('verified-backup');i.state['verified_database_backup']='/synthetic-verified-backup'
  i.database_backup=backup
  mapping={i.runtime.COUNTS_QUERY:dict.fromkeys(('customers','orders','lines','administrators','audits','order_audits','profiles','products','machines'),1),previous.commercial.PRICE_COUNTS:{},previous.token.TOKEN_COUNTS:{},previous.setup.DATA_FINGERPRINT:{},previous.setup.SETUP_COUNTS:{},installer.SECURITY_FINGERPRINT:{}}
  i.sql_json=lambda query:[{'MigrationId':x} for x in ids] if query=='history' else mapping[query]
  def execute(text,timeout):
   self.assertEqual(events,['verified-backup']);self.assertIn('CREATE TABLE [RouteOrderSchedules]',text);self.assertNotIn('CREATE TABLE [ProductionBatches]',text);events.append(installer.MIGRATION);ids.append(installer.MIGRATION)
  i.sql_run=Mock(side_effect=execute)
  with patch.object(installer,'regular',return_value=raw):i.backup_and_migrate()
  self.assertEqual(events,['verified-backup',installer.MIGRATION]);self.assertTrue(i.state['tokens_preserved']);self.assertEqual(i.state['migrations_after'],17)
 def test_no_verified_backup_means_no_ddl(self):
  i=self.instance();ids=self.history(i);i.schemas=Mock(return_value=ids);i.state['migration_ids_before']=ids;i.database_backup=Mock();i.sql_json=Mock(return_value=dict.fromkeys(('customers','orders','lines','administrators','audits','order_audits','profiles','products','machines'),1));i.sql_run=Mock()
  with self.assertRaises(installer.InstallError):i.backup_and_migrate()
  i.sql_run.assert_not_called()
 def test_failure_after_ddl_preserves_marker_and_stops_instead_of_rolling_back_to_incompatible_binary(self):
  i=self.instance();i.stop_requested=True;i.history_before=self.history(i);i.history=Mock(return_value=i.history_before+[installer.MIGRATION]);i.sql_json=Mock();i.runtime.command=Mock()
  i.rollback();self.assertTrue(i.state['recovery_pending']);self.assertTrue(i.state['application_stopped_for_security']);self.assertFalse(i.state['database_automatically_restored'])

 def test_exported_package_pins_source_payload_and_exact_single_increment(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);backend=root/'backend';frontend=root/'frontend';backend.mkdir();frontend.mkdir()
   for name in ('Prodimt.Pedidos.Api.dll','Prodimt.Pedidos.Api.runtimeconfig.json','appsettings.json'):(backend/name).write_text('{}')
   (frontend/'index.html').write_text('<html></html>');(frontend/'3rdpartylicenses.txt').write_text('Synthetic notices')
   (frontend/'main.js').write_text('apiBaseUrl:"" /access-tokens /reveal /prices /security directPermissionIds roleIds /product-setups /additional-products /operations/production requiresMachine /ordering-schedules orderingWindow')
   production=root/'production.sql';production.write_text(previous.production_sql());identity=root/'identity.sql';identity.write_text(previous.identity_fixture.migration());schedule=root/'schedule.sql';schedule.write_text(sql())
   source='a'*40;output=root/'delivery.zip'
   with patch.object(builder,'validate_source'):
    result=builder.build(backend,frontend,schedule,output,source,ROOT/'tests/pilot/fixtures/i16-additional-products-migration.sql',identity,production)
   with zipfile.ZipFile(output) as outer:
    exported=root/'exported.py';exported.write_bytes(outer.read(builder.PREFIX+'PRODIMT_Instalar_I22.py'))
    payload=root/'payload.zip';payload.write_bytes(outer.read(builder.PREFIX+'prodimt-ordering-schedules-payload.zip'))
   module=load('i22_exported_fixture',exported)
   self.assertEqual(module.EXPECTED_SOURCE,source);self.assertEqual(module.EXPECTED_PAYLOAD_SHA256,result['payloadSha256'])
   # This test validates content/pins under a normal user; production separately requires root-owned files.
   with patch.object(module,'regular',side_effect=lambda path,limit=0:Path(path).read_bytes()):
    self.assertEqual(len(module.load_payload(payload)),result['files'])
    payload.write_bytes(payload.read_bytes()+b'corruption')
    with self.assertRaisesRegex(module.InstallError,'payload_hash_mismatch'):module.load_payload(payload)

if __name__=='__main__':unittest.main()
