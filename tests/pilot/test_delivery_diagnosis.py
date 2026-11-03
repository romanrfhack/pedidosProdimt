"""Read-only I22 diagnosis preserves preflight and never emits metadata values."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


diagnosis = load('i22_diagnosis_tests', ROOT / 'scripts/pilot/diagnose-delivery-schema.py')
fixtures = load('i22_diagnosis_fixture_support', ROOT / 'tests/pilot/test_delivery_package.py')
security = load('i22_diagnosis_security_fixture', ROOT / 'scripts/pilot/install-identity-update.py')
production = load('i22_diagnosis_production_fixture', ROOT / 'scripts/pilot/install-production-update.py')
CANARY = 'PRIVATE-CANARY-MUST-NOT-APPEAR'


class DeliveryDiagnosisTests(unittest.TestCase):
    def composition(self, observed):
        """Real make_installer/preflight/schema guard, simulated read transport."""
        installer = fixtures.installer
        original_factory = installer.module
        instances = []
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            backups = root / 'backups'; backups.mkdir()
            env = root / 'api.env'
            raw = b'CustomerAccessTokens__EncryptionKey=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n'
            env.write_bytes(raw)
            for part in ('backend','frontend'):
                target = root / part / 'releases' / installer.BASE_BACKEND
                target.mkdir(parents=True)
                (root / part / 'current').symlink_to(target)
                if part == 'backend':
                    (target / 'Prodimt.Pedidos.Api.dll').write_bytes(b'synthetic')
                    (target / 'appsettings.json').write_text('{}')
                else:
                    (target / 'index.html').write_bytes(b'synthetic-index')
            key = fixtures.token.environment_key(raw)
            history = fixtures.DeliveryPackageTests().base(fixtures.DeliveryPackageTests().instance())
            mapping = {
                fixtures.commercial.NEW_SCHEMA: fixtures.commercial.EXPECTED_SCHEMA,
                fixtures.setup.TOKEN_SHAPE: fixtures.setup.EXPECTED_TOKEN_SCHEMA,
                fixtures.setup.SETUP_SHAPE: fixtures.setup.EXPECTED_SETUP_SCHEMA,
                security.SECURITY_SCHEMA: security.EXPECTED_SECURITY_SCHEMA,
                production.PRODUCTION_SHAPE: production.EXPECTED_PRODUCTION_SCHEMA,
                fixtures.schema.schema_query(False): observed,
                fixtures.commercial.PERMISSION_QUERY: fixtures.commercial.EXPECTED_PERMISSIONS | {'migrations_count':16},
                fixtures.commercial.PRICE_PERMISSIONS: fixtures.commercial.EXPECTED_PRICE_PERMISSIONS,
                fixtures.setup.SETUP_PERMISSIONS: fixtures.setup.EXPECTED_SETUP_PERMISSIONS,
                security.SECURITY_PERMISSIONS: security.EXPECTED_SECURITY_PERMISSIONS,
                production.PRODUCTION_SQL_PERMISSIONS: production.EXPECTED_PRODUCTION_PERMISSIONS,
                'history': [{'MigrationId': value} for value in history],
            }
            reader = Mock(side_effect=lambda tool, fields, query: mapping[query])
            command = Mock(side_effect=AssertionError('No system command may run in this fixture'))
            def factory(name, raw_code):
                module = original_factory(name, raw_code)
                if name == 'reviewed_i18_native_support':
                    module.directory_safe = Mock()
                    module.command = command
                    module.shutil = types.SimpleNamespace(disk_usage=lambda _:types.SimpleNamespace(free=1024**3))
                elif name == 'reviewed_i18_token_support':
                    module.validate_environment_file = Mock()
                elif name == 'i19_inventory':
                    module.SQL_MIGRATIONS = 'history'
                    module.SQL_TOOLS = (os.sys.executable,)
                    module.process_environment = Mock(return_value={})
                    module.configuration = Mock(return_value=({fixtures.token.KEY_CONFIG:key,
                        'ConnectionStrings:Pedidos':CANARY}, {'environment':'Production'}))
                    module.first = lambda config,*keys,default=None: config.get(keys[0],default)
                    module.policy_flags = Mock(return_value=dict(provider_is_sqlserver=True,jwt_compatible_with_production_policy=True,
                        jwt_lifetime_compatible=True,connection_compatible_with_production_policy=True,
                        development_seed_enabled=False,automatic_migrations_enabled=False,https_redirection_enabled=True))
                    module.connection_fields = Mock(return_value={})
                    module.permitted_connection = Mock(return_value=True)
                    module.query_sql = reader
                return module
            real_make = installer.make_installer
            def make(payload):
                instance = real_make(payload)
                instance.unit_configuration = {}
                instance.process = Mock(return_value=(1, root/'backend/releases'/installer.BASE_BACKEND,
                    ['/synthetic/dotnet','Prodimt.Pedidos.Api.dll']))
                instance.http = Mock(side_effect=lambda path: (200,b'{"database":"reachable"}') if path=='/health/db'
                    else (200,b'synthetic-index') if path=='/admin/reparto' else (401,b''))
                for name in ('apply','rollback','authenticate_admin','stage','sql_run','validate_before_apply'):
                    setattr(instance,name,Mock(side_effect=AssertionError('Forbidden method: '+name)))
                instances.append(instance)
                return instance
            with patch.object(installer,'ROOT',root), patch.object(installer,'BACKUPS',backups), \
                    patch.object(installer,'ENV_FILE',env), patch.object(installer,'regular',side_effect=lambda path:path.read_bytes()), \
                    patch.object(installer,'load_payload',return_value=fixtures.supports()), \
                    patch.object(installer,'module',side_effect=factory), patch.object(installer,'make_installer',side_effect=make):
                result, captures = diagnosis.observe_preflight(installer, Path('/synthetic-payload.zip'))
            for instance in instances:
                for name in ('apply','rollback','authenticate_admin','stage','sql_run','validate_before_apply'):
                    getattr(instance,name).assert_not_called()
                self.assertFalse(instance.stop_requested)
                self.assertFalse(instance.state['changes_made'])
            command.assert_not_called()
            self.assertTrue(reader.called)
            return result, captures

    def test_original_preflight_mismatch_is_observed_without_changing_false(self):
        result, captures = self.composition({key:None for key in diagnosis.SECTIONS})
        self.assertEqual(result, dict(status='failed',issue='delivery_schema_unexpected'))
        self.assertEqual(len(captures),1)
        self.assertIs(captures[0]['comparison_result'],False)
        self.assertTrue(captures[0]['comparison_false'])
        self.assertFalse(captures[0]['strict'])
        self.assertFalse(captures[0]['migrated_schema_expected'])
        self.assertEqual(captures[0]['observed']['sections']['columns'],dict(present=True,kind='null',count=None))

    def test_original_preflight_success_is_observed_without_changing_true(self):
        result, captures = self.composition({key:[] for key in diagnosis.SECTIONS})
        self.assertEqual(result,dict(status='passed',issue=None))
        self.assertIs(captures[0]['comparison_result'],True)
        self.assertFalse(captures[0]['comparison_false'])
        self.assertLess(len(json.dumps(captures)),1800)

    def test_unknown_keys_names_definitions_values_and_tables_never_leave_shape(self):
        raw = {key:[] for key in diagnosis.SECTIONS}
        raw[CANARY] = CANARY
        raw['columns'] = [dict(table='DeliveryTrips',name=CANARY,type=CANARY,length=123),
                          dict(table=CANARY,name=CANARY,**{CANARY:CANARY}),CANARY]
        raw['checks'] = [dict(table='DeliveryTrips',name=CANARY,definition=CANARY)]
        result, captures = self.composition(raw)
        output = json.dumps(dict(result=result,captures=captures))
        self.assertNotIn(CANARY,output)
        summary = captures[0]['observed']
        self.assertEqual(summary['unknown_root_fields_count'],1)
        self.assertEqual(summary['sections']['columns']['unknown_row_fields_count'],1)
        self.assertEqual(summary['sections']['columns']['unknown_table_rows_count'],1)
        self.assertEqual(summary['sections']['columns']['rows_by_allowed_table'],{'DeliveryTrips':1})
        self.assertEqual(summary['sections']['checks']['known_field_presence']['definition'],1)

    def test_capture_failure_cannot_turn_an_original_false_into_success(self):
        installer = types.SimpleNamespace(InstallError=RuntimeError)
        returned = []
        module = types.SimpleNamespace(matches=lambda data,contract,strict:False)
        installer.module = lambda name,raw:module
        def make(_):
            wrapped = installer.module('i22_delivery_schema_support',b'').matches
            return types.SimpleNamespace(preflight=lambda:returned.append(wrapped(None,{},False)))
        installer.make_installer = make
        with patch.object(diagnosis,'shape',side_effect=ValueError(CANARY)):
            _, captures = diagnosis.observe_preflight(installer,Path('/synthetic'))
        self.assertEqual(returned,[False])
        self.assertEqual(captures,[{'capture_issue':'shape_summary_failed'}])

    def test_error_allowlist_and_main_discard_internal_output(self):
        installer = types.SimpleNamespace(InstallError=RuntimeError,module=lambda *args:None)
        def make(_):
            def preflight():
                print(CANARY)
                print(CANARY,file=os.sys.stderr)
                raise RuntimeError(CANARY)
            return types.SimpleNamespace(preflight=preflight)
        installer.make_installer=make
        def run():
            preflight,captures=diagnosis.observe_preflight(installer,Path('/synthetic'))
            return dict(schema='prodimt.delivery.schema.diagnosis.v1',status='observed',preflight=preflight,captures=captures)
        output, errors = io.StringIO(),io.StringIO()
        with patch.object(diagnosis,'run_diagnosis',side_effect=run),contextlib.redirect_stdout(output),contextlib.redirect_stderr(errors):
            self.assertEqual(diagnosis.main([]),0)
        self.assertNotIn(CANARY,output.getvalue()+errors.getvalue())
        self.assertEqual(json.loads(output.getvalue())['preflight']['issue'],'preflight_failed_unclassified')

    def test_pin_change_is_refused_before_import_and_lock_is_read_only(self):
        real_fstat=os.fstat
        def root_metadata(fd):
            current=real_fstat(fd)
            return types.SimpleNamespace(st_uid=0,st_mode=current.st_mode,st_nlink=current.st_nlink)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder); lock=root/'lock'; script=root/'installer.py'; payload=root/'payload.zip'
            lock.write_bytes(b''); lock.chmod(0o600)
            script.write_bytes(b'print("'+CANARY.encode()+b'")\n'); payload.write_bytes(b'synthetic')
            for changed in ('installer','payload'):
                with self.subTest(changed=changed),patch.object(diagnosis.os,'geteuid',return_value=0), \
                        patch.object(diagnosis.os,'fstat',side_effect=root_metadata),patch.object(diagnosis.resource,'setrlimit'), \
                        patch.object(diagnosis.fcntl,'flock'),patch.object(diagnosis,'INSTALLER_SHA256',
                            '0'*64 if changed=='installer' else hashlib.sha256(script.read_bytes()).hexdigest()), \
                        patch.object(diagnosis,'PAYLOAD_SHA256','0'*64),patch('builtins.exec') as execute, \
                        patch.object(diagnosis.os,'open',wraps=os.open) as opened:
                    report=diagnosis.run_diagnosis(script,payload,lock)
                execute.assert_not_called()
                self.assertEqual(report['issue'],'diagnosis_pinned_file_changed')
                self.assertNotIn(CANARY,json.dumps(report))
                for call in opened.call_args_list:
                    self.assertFalse(call.args[1] & (os.O_CREAT|os.O_WRONLY|os.O_RDWR))
                    self.assertTrue(call.args[1] & os.O_NOFOLLOW)
            self.assertEqual(lock.read_bytes(),b'')

    def test_lock_busy_and_missing_do_not_read_or_import_installer(self):
        with patch.object(diagnosis.os,'geteuid',return_value=0),patch.object(diagnosis.resource,'setrlimit'), \
                patch.object(diagnosis.os,'open',side_effect=FileNotFoundError),patch.object(diagnosis,'pinned_bytes') as read:
            report=diagnosis.run_diagnosis()
        read.assert_not_called()
        self.assertEqual(report['issue'],'diagnosis_file_or_lock_unavailable')
        with patch.object(diagnosis.os,'geteuid',return_value=0),patch.object(diagnosis.resource,'setrlimit'), \
                patch.object(diagnosis.os,'open',return_value=123),patch.object(diagnosis.os,'close'), \
                patch.object(diagnosis.os,'fstat',return_value=types.SimpleNamespace(st_uid=0,st_nlink=1,st_mode=stat.S_IFREG|0o600)), \
                patch.object(diagnosis.fcntl,'flock',side_effect=BlockingIOError),patch.object(diagnosis,'pinned_bytes') as read:
            report=diagnosis.run_diagnosis()
        read.assert_not_called()
        self.assertEqual(report['issue'],'diagnosis_lock_busy')


if __name__=='__main__':
    unittest.main()
