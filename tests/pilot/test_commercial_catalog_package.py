"""Native commercial update: package integrity, verified backup and recovery boundaries."""
from contextlib import contextmanager
import importlib.util
import io
import json
from pathlib import Path
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


installer = module('commercial_installer_fixture', 'install-commercial-catalog-update.py')
builder = module('commercial_builder_fixture', 'build-commercial-catalog-package.py')
SOURCE = 'a' * 40
COUNTS = dict(customers=23, products=7, profiles=26, orders=9, lines=15,
              administrators=2, audits=12, order_audits=9, machines=2)
MONETARY = dict(books=0, revisions=0, consolidations=0, priced_lines=0)


class CommercialPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.backend, self.frontend = self.root / 'backend', self.root / 'frontend'
        self.backend.mkdir(); self.frontend.mkdir()
        for name, raw in {'Prodimt.Pedidos.Api.dll': b'synthetic-assembly',
                          'Prodimt.Pedidos.Api.runtimeconfig.json': b'{}', 'appsettings.json': b'{}'}.items():
            (self.backend / name).write_bytes(raw)
        (self.frontend / 'index.html').write_text('<script src="main.js"></script>')
        (self.frontend / 'main.js').write_text('apiBaseUrl:""; /consolidation/candidates /prices effectiveFrom /api/customer-orders/')
        (self.frontend / '3rdpartylicenses.txt').write_text('Synthetic fixture license notice')
        self.migration = self.root / 'migration.sql'
        self.migration.write_text("-- CustomerProductPriceRevisions\nINSERT INTO [__EFMigrationsHistory] VALUES (N'" + builder.MIGRATION + "', N'10.0.8');\n")
        self.bundle, self.payload = self.root / 'bundle.zip', self.root / 'payload.zip'
        self.addCleanup(patch.stopall)
        patch.object(builder, 'validate_source').start()
        patch.object(installer, 'EXPECTED_SOURCE', SOURCE).start()

    def build(self):
        result = builder.build(self.backend, self.frontend, self.migration, self.bundle, SOURCE)
        with zipfile.ZipFile(self.bundle) as archive:
            self.payload.write_bytes(archive.read(builder.PREFIX + 'prodimt-commercial-catalog-payload.zip'))
        patch.object(installer, 'EXPECTED_PAYLOAD_SHA256', result['payloadSha256']).start()
        patch.object(installer, 'regular', side_effect=lambda path, limit=0: Path(path).read_bytes()).start()
        return result

    def instance(self, count=11):
        self.build()
        result = installer.make_installer(self.payload)
        result.state['migrations_before'] = count
        result.sql_tool, result.fields = 'synthetic-sqlcmd', {}
        def query(tool, fields, sql):
            if sql == installer.PERMISSION_QUERY:
                return dict(installer.EXPECTED_PERMISSIONS, migrations_count=12)
            if sql == installer.PRICE_PERMISSIONS:
                return installer.EXPECTED_PRICE_PERMISSIONS.copy()
            raise AssertionError('unexpected application query')
        result.inspector = types.SimpleNamespace(SQL_MIGRATIONS='history', query_sql=query)
        return result

    def support(self, instance):
        return instance.__class__.__mro__[1].stage.__globals__

    @contextmanager
    def real_preflight(self, current=False, sql_failure=False):
        """Keep the real process/configuration/query policy; fake only OS and transports."""
        self.build()
        app, backups, env_file = self.root/'app', self.root/'backups', self.root/'api.env'
        backups.mkdir(); env_file.write_bytes(b'synthetic-private-environment-file')
        with patch.object(installer, 'ROOT', app), patch.object(installer, 'BACKUPS', backups), \
                patch.object(installer, 'ENV_FILE', env_file):
            instance = installer.make_installer(self.payload)
            old = {}
            for part in ('backend', 'frontend'):
                name = 'prices-'+SOURCE[:12] if current else (installer.I09 if part=='backend' else installer.I10_FRONTEND)
                folder = app/part/'releases'/name
                folder.mkdir(parents=True)
                old[part] = folder
                (app/part/'current').symlink_to(folder)
                for path, raw in instance.files.items():
                    if path.startswith(part+'/'):
                        target = folder/path.removeprefix(part+'/')
                        target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(raw)
            pid = 424242
            argv = ['/usr/bin/dotnet',str(old['backend']/'Prodimt.Pedidos.Api.dll'),'--urls','http://127.0.0.1:5080']
            secret = 'synthetic-password-never-print'
            environment = {
                'ASPNETCORE_ENVIRONMENT':'Production',
                'ConnectionStrings__Pedidos':'Server=127.0.0.1,14330;Database=ProdimtPedidos;User Id=prodimt_pedidos_app;Password='+secret+';',
                'Authentication__Jwt__SigningKey':'synthetic-test-signing-key-'+'a'*48,
                'Authentication__Jwt__AccessTokenMinutes':'720',
                'DevelopmentSeed__Enabled':'false','DevelopmentSeed__ApplyMigrations':'false',
                'Hosting__UseHttpsRedirection':'true','UNRELATED_PRIVATE_VALUE':secret}
            environment_raw = ('\0'.join(key+'='+value for key,value in environment.items())+'\0').encode()
            real_open, real_is_file = Path.open, Path.is_file
            tool = '/opt/mssql-tools/bin/sqlcmd'
            queries, checked_stages = [], []
            def proc_path(value):
                if str(value)==f'/proc/{pid}/cwd': return types.SimpleNamespace(resolve=lambda strict=True: old['backend'])
                return Path(value)
            def read_file(path, limit=0):
                if str(path)==f'/proc/{pid}/cmdline': return ('\0'.join(argv)+'\0').encode()
                return Path(path).read_bytes()
            def open_file(path, *args, **kwargs):
                if str(path)==f'/proc/{pid}/environ': return io.BytesIO(environment_raw)
                return real_open(path,*args,**kwargs)
            def execute(args, **kwargs):
                if args[:2]==['systemctl','show']:
                    self.assertEqual(installer.SERVICE,args[2])
                    values=dict(ActiveState='active',MainPID=str(pid),User='www-data',Group='',
                        WorkingDirectory=str(app/'backend/current'),EnvironmentFiles=str(env_file)+' (ignore_errors=no)')
                    return subprocess.CompletedProcess(args,0,'\n'.join(k+'='+v for k,v in values.items()),'')
                if args[0]==tool:
                    self.assertIn('-Q',args)
                    wrapped=args[args.index('-Q')+1]
                    query=next((q for q in (installer.PERMISSION_QUERY,installer.NEW_SCHEMA,installer.PRICE_PERMISSIONS)
                        if wrapped.endswith('SELECT ('+q.rstrip().removesuffix(';')+') AS ProdimtInspectionJson;')),None)
                    self.assertIsNotNone(query)
                    self.assertEqual({'PATH','LANG','LC_ALL','SQLCMDUSER','SQLCMDPASSWORD'},set(kwargs['env']))
                    self.assertEqual(secret,kwargs['env']['SQLCMDPASSWORD'])
                    self.assertNotIn(secret,' '.join(args))
                    queries.append(query)
                    if sql_failure: return subprocess.CompletedProcess(args,1,secret+' private SQL error','')
                    data=(dict(installer.EXPECTED_PERMISSIONS,migrations_count=12 if current else 11)
                        if query==installer.PERMISSION_QUERY else installer.EXPECTED_SCHEMA
                        if query==installer.NEW_SCHEMA else installer.EXPECTED_PRICE_PERMISSIONS)
                    return subprocess.CompletedProcess(args,0,'ProdimtInspectionJson\n---------------------\n'+json.dumps(data),'')
                if args[0]=='curl':
                    url=next(value for value in args if value.startswith('https://'))
                    path=url.removeprefix('https://'+installer.DOMAIN)
                    if path=='/health/db': body,status=json.dumps({'database':'reachable'}),200
                    elif path.startswith('/api/'): body,status='{}',401
                    else:
                        name='index.html' if path.startswith('/admin/') else path.removeprefix('/')
                        self.assertIn('frontend/'+name,instance.files)
                        body,status=(old['frontend']/name).read_text(),200
                    return subprocess.CompletedProcess(args,0,body+'\n'+str(status),'')
                raise AssertionError('unexpected OS mutation or transport: '+args[0])
            def verify_stage(part):
                checked_stages.append(part)
                self.assertEqual(instance.expected(part),{str(p.relative_to(instance.new[part])):p.read_bytes()
                    for p in instance.new[part].rglob('*') if p.is_file()})
            with patch.dict(self.support(instance),Path=proc_path,regular=read_file,directory_safe=lambda path: self.assertTrue(path.is_dir()),
                    prompt_secret=Mock(side_effect=AssertionError('read-only requested credentials'))), \
                    patch.object(Path,'open',open_file), \
                    patch.object(Path,'is_file',lambda path: str(path)==tool or real_is_file(path)), \
                    patch.object(subprocess,'run',side_effect=execute) as transport, \
                    patch.object(instance,'verify_stage',side_effect=verify_stage):
                yield instance, queries, checked_stages, transport, secret

    def test_real_preflight_process_configuration_and_query_guard_accept_i09_with_i10_without_writes(self):
        with self.real_preflight() as (instance,queries,checked,transport,secret):
            instance.preflight()
            self.assertEqual([installer.PERMISSION_QUERY],queries)
            self.assertEqual([],checked)
            self.assertEqual([],instance.state['database_mismatches'])
            self.assertFalse(instance.state['changes_made'])
            self.assertFalse(instance.state['service_restarted'])
            self.assertNotIn(secret,json.dumps(instance.state))

    def test_real_already_current_path_authorizes_all_three_selects_and_rejects_foreign_sql(self):
        with self.real_preflight(current=True) as (instance,queries,checked,transport,secret):
            instance.preflight()
            self.assertEqual([installer.PERMISSION_QUERY,installer.NEW_SCHEMA,installer.PRICE_PERMISSIONS],queries)
            self.assertEqual(['backend','frontend'],checked)
            self.assertEqual('already_current',instance.state['status'])
            self.assertFalse(instance.state['changes_made'])
            self.assertFalse(instance.state['service_restarted'])
            calls=transport.call_count
            for sql in ('SELECT 1;','UPDATE dbo.Products SET IsActive=0;'):
                with self.assertRaisesRegex(instance.inspector.InspectionError,'^sql_query_not_authorized$'):
                    instance.inspector.query_sql(instance.sql_tool,instance.fields,sql)
            self.assertEqual(calls,transport.call_count)

    def test_real_sql_read_failure_reports_only_its_fixed_issue_without_private_streams(self):
        with self.real_preflight(sql_failure=True) as (instance,queries,checked,transport,secret):
            with self.assertRaises(Exception) as caught:
                instance.preflight()
            self.assertIsInstance(caught.exception,instance.inspector.InspectionError)
            issue=installer.error_issue(caught.exception,instance.inspector)
            self.assertEqual('sql_read_failed',issue)
            self.assertEqual('application-sql-read',instance.state['preflight_step'])
            self.assertEqual([installer.PERMISSION_QUERY],queries)
            self.assertNotIn(secret,json.dumps(dict(instance.state,issue=issue)))
            self.assertFalse(instance.state['changes_made'])
            self.assertFalse(instance.state['service_restarted'])

    def test_unknown_exception_messages_remain_private(self):
        inspector=types.SimpleNamespace(InspectionError=type('InspectionError',(Exception,),{}))
        for error in (ValueError('synthetic-private-value'),OSError('synthetic-private-value')):
            self.assertEqual('local_operation_failed',installer.error_issue(error,inspector))
            self.assertEqual('local_operation_failed',installer.error_issue(error))
        self.assertEqual('known_guard',installer.error_issue(installer.InstallError('known_guard'),inspector))

    def metadata(self, query):
        if 'AS priced_lines' in query: return MONETARY.copy()
        return COUNTS.copy()

    def backup(self, instance, events):
        events.append('verified-backup')
        instance.state['verified_database_backup'] = '/synthetic/private.bak'

    def test_bundle_pins_exact_source_payload_support_and_license(self):
        result = self.build()
        files = installer.load_payload(self.payload)
        self.assertEqual((ROOT / 'scripts/pilot/install-quick-capture-update.py').read_bytes(), files['native-deployment-support.py'])
        self.assertIn('frontend/3rdpartylicenses.txt', files)
        with zipfile.ZipFile(self.bundle) as archive:
            script = archive.read(builder.PREFIX + 'PRODIMT_Instalar_I11.py').decode()
            self.assertIn("EXPECTED_SOURCE = '" + SOURCE + "'", script)
            self.assertIn("EXPECTED_PAYLOAD_SHA256 = '" + result['payloadSha256'] + "'", script)
            hashes = archive.read(builder.PREFIX + 'SHA256SUMS.txt').decode().splitlines()
            for line in hashes:
                digest, name = line.split('  ')
                self.assertEqual(digest, builder.digest(archive.read(builder.PREFIX + name)))

    def test_build_is_reproducible_and_excludes_debug_configuration(self):
        (self.backend / 'appsettings.Development.json').write_text('synthetic-development')
        (self.backend / 'unused.pdb').write_bytes(b'debug')
        (self.frontend / 'unused.map').write_bytes(b'map')
        first = self.build(); second = self.build()
        self.assertEqual(first['sha256'], second['sha256'])
        self.assertFalse(any(name.endswith(('.map', '.pdb', 'appsettings.Development.json')) for name in installer.load_payload(self.payload)))

    def test_tampering_is_rejected_before_loading_support(self):
        self.build(); self.payload.write_bytes(self.payload.read_bytes() + b'changed')
        with self.assertRaisesRegex(installer.InstallError, '^payload_hash_mismatch$'):
            installer.make_installer(self.payload)

    def test_manifest_cannot_authorize_an_extra_script_or_changed_file(self):
        self.build()
        with zipfile.ZipFile(self.payload) as archive: original = {name: archive.read(name) for name in archive.namelist()}
        for name, raw in [('arbitrary.py', b'pass'), ('backend/Prodimt.Pedidos.Api.dll', b'changed')]:
            with self.subTest(name=name):
                files = original | {name: raw}
                payload = builder.archive_bytes(files); self.payload.write_bytes(payload)
                with patch.object(installer, 'EXPECTED_PAYLOAD_SHA256', installer.sha(payload)):
                    with self.assertRaises(installer.InstallError): installer.load_payload(self.payload)

    def test_source_migration_and_same_origin_are_required(self):
        original = self.migration.read_text()
        for sql in (original + 'UPDATE Products SET Name=Name;', original.replace(builder.MIGRATION, 'unexpected')):
            self.migration.write_text(sql)
            with self.assertRaisesRegex(ValueError, '^incremental_migration_unexpected$'): self.build()
        self.migration.write_text(original)
        (self.frontend / 'main.js').write_text('apiBaseUrl:"https://synthetic.invalid"')
        with self.assertRaisesRegex(ValueError, '^frontend_feature_or_same_origin_missing$'): self.build()

    def test_private_files_symlinks_and_unsafe_names_are_rejected(self):
        for name in ('../bad', '/bad', '', '.', 'backend//bad', 'backend/../bad', 'C:bad', 'backend\\bad'):
            with self.subTest(name=name): self.assertFalse(installer.safe_name(name))
        self.assertTrue(installer.safe_name('backend/es/message.dll'))
        private = self.backend / 'private.env'; private.write_text('synthetic')
        with self.assertRaisesRegex(ValueError, '^build_private_or_source_file_not_allowed$'): self.build()
        private.unlink(); (self.frontend / 'linked.js').symlink_to(self.frontend / 'main.js')
        with self.assertRaisesRegex(ValueError, '^build_symlink_not_allowed$'): self.build()

    def test_application_guards_report_each_missing_capability_and_channel(self):
        valid = dict(installer.EXPECTED_PERMISSIONS, migrations_count=11)
        self.assertEqual([], installer.permission_mismatches(valid))
        for key in installer.EXPECTED_PERMISSIONS:
            with self.subTest(key=key):
                data = valid.copy(); data.pop(key)
                self.assertEqual([key], installer.permission_mismatches(data))
        self.assertEqual(['migrations_count'], installer.permission_mismatches(valid, (12,)))

    def test_verified_backup_precedes_migration_and_append_only_grants(self):
        instance = self.instance(); events = []
        with patch.object(instance, 'verify_schema', side_effect=[11, 12]), \
                patch.object(instance, 'sql_json', side_effect=self.metadata), \
                patch.object(instance, 'database_backup', side_effect=lambda: self.backup(instance, events)), \
                patch.object(instance, 'sql_run', side_effect=lambda sql, **kw: events.append('grants' if sql == installer.PRICE_GRANTS else 'migration')):
            instance.backup_and_migrate()
        self.assertEqual(['verified-backup', 'migration', 'grants'], events)
        self.assertTrue(instance.state['price_history_append_only_verified'])
        self.assertTrue(instance.state['existing_data_counts_preserved'])

    def test_backup_failure_prevents_migration_or_permission_changes(self):
        instance = self.instance()
        with patch.object(instance, 'verify_schema', return_value=11), \
                patch.object(instance, 'sql_json', side_effect=self.metadata), \
                patch.object(instance, 'database_backup', side_effect=installer.InstallError('backup_failed')), \
                patch.object(instance, 'sql_run') as sql:
            with self.assertRaisesRegex(installer.InstallError, '^backup_failed$'): instance.backup_and_migrate()
        sql.assert_not_called(); self.assertFalse(instance.state['migrations_may_have_changed'])

    def test_recovery_from_completed_migration_preserves_existing_prices(self):
        instance = self.instance(12); events = []
        monetary = dict(books=4, revisions=9, consolidations=1, priced_lines=20)
        with patch.object(instance, 'verify_schema', return_value=12), \
                patch.object(instance, 'sql_json', side_effect=lambda q: monetary if q == installer.PRICE_COUNTS else COUNTS), \
                patch.object(instance, 'database_backup', side_effect=lambda: self.backup(instance, events)), \
                patch.object(instance, 'sql_run', side_effect=lambda sql, **kw: events.append('grants')) as sql:
            instance.backup_and_migrate()
        sql.assert_called_once_with(installer.PRICE_GRANTS)
        self.assertEqual(['verified-backup', 'grants'], events)
        self.assertFalse(instance.state['database_migrations_run'])

    def test_count_drift_or_unknown_count_stops_activation(self):
        instance = self.instance(); events = []
        for value in ({}, dict(COUNTS, customers=-1)):
            with patch.object(instance, 'verify_schema', return_value=11), patch.object(instance, 'sql_json', return_value=value), \
                    patch.object(instance, 'database_backup') as backup:
                with self.assertRaisesRegex(installer.InstallError, '^database_counts_unrecognized$'): instance.backup_and_migrate()
                backup.assert_not_called()
        with patch.object(instance, 'verify_schema', side_effect=[11, 12]), \
                patch.object(instance, 'sql_json', side_effect=[COUNTS, dict(COUNTS, orders=10)]), \
                patch.object(instance, 'database_backup', side_effect=lambda: self.backup(instance, events)), patch.object(instance, 'sql_run'):
            with self.assertRaisesRegex(installer.InstallError, '^existing_data_counts_changed$'): instance.backup_and_migrate()

    def test_partial_schema_unknown_history_and_invalid_alias_data_fail_closed(self):
        instance = self.instance(); support = self.support(instance)
        history = [{'MigrationId': x} for x in support['MIGRATIONS']]
        rest = dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1)
        alias = dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1)
        for values in ([[]], [history, rest, alias, {'invalid': 0}, {'invalid': 1}],
                       [history, rest, alias, {'invalid': 0}, {'invalid': 0}, {'tables_count': 1, 'new_columns': 0}]):
            with self.subTest(values=values), patch.object(instance, 'sql_json', side_effect=values):
                with self.assertRaises(installer.InstallError): instance.verify_schema()

    def test_complete_schema_is_required_and_valid_history_is_accepted(self):
        instance = self.instance(); support = self.support(instance)
        history = [{'MigrationId': x} for x in support['MIGRATIONS'] + [installer.MIGRATION]]
        prefix = [history, dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1),
                  dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1), {'invalid': 0}, {'invalid': 0}]
        with patch.object(instance, 'sql_json', side_effect=prefix + [installer.EXPECTED_SCHEMA]):
            self.assertEqual(12, instance.verify_schema())
        for field in installer.EXPECTED_SCHEMA:
            with self.subTest(field=field), patch.object(instance, 'sql_json', side_effect=prefix + [dict(installer.EXPECTED_SCHEMA, **{field: 0})]):
                with self.assertRaisesRegex(installer.InstallError, '^commercial_schema_unexpected$'): instance.verify_schema()

    def test_read_only_failure_does_not_stage_stop_or_restore(self):
        instance = self.instance(); command = Mock()
        with patch.object(instance, 'preflight', side_effect=installer.InstallError('guarded')), \
                patch.object(instance, 'stage') as stage, patch.dict(self.support(instance), command=command):
            with self.assertRaisesRegex(installer.InstallError, '^guarded$'): instance.apply()
            instance.rollback()
        stage.assert_not_called(); command.assert_not_called()

    def test_already_current_does_not_authenticate_backup_or_restart(self):
        instance = self.instance(); command = Mock()
        def preflight(): instance.state.update(status='already_current', phase='completed')
        with patch.object(instance, 'preflight', side_effect=preflight), \
                patch.object(instance, 'authenticate_admin') as auth, patch.object(instance, 'database_backup') as backup, \
                patch.dict(self.support(instance), command=command):
            instance.apply()
        auth.assert_not_called(); backup.assert_not_called(); command.assert_not_called()

    def test_staging_interruption_leaves_no_partial_release_and_can_retry(self):
        instance = self.instance(); instance.overrides = {}
        instance.new = {part: self.root / ('release-' + part) for part in ('backend', 'frontend')}
        with patch.object(instance, 'expected', side_effect=installer.InstallError('synthetic-interruption')), \
                patch.object(instance, 'verify_stage') as verify:
            with self.assertRaisesRegex(installer.InstallError, '^synthetic-interruption$'): instance.stage()
            verify.assert_not_called()
        self.assertFalse(any(path.exists() for path in instance.new.values()))
        self.assertFalse(list(self.root.glob('.commercial-stage-*')))
        with patch.object(installer.os, 'chown'), patch.object(instance, 'verify_stage') as verify:
            instance.stage()
        self.assertEqual(2, verify.call_count)
        self.assertEqual(b'synthetic-assembly', (instance.new['backend'] / 'Prodimt.Pedidos.Api.dll').read_bytes())

    def test_apply_snapshots_configuration_stops_only_its_service_and_verifies_before_success(self):
        instance = self.instance(); events = []
        backups = self.root / 'backups'; backups.mkdir()
        env = self.root / 'api.env'; env.write_bytes(b'synthetic-environment')
        def preflight():
            events.append('preflight')
            instance.old = dict(backend=self.backend, frontend=self.frontend)
            instance.environment = env.read_bytes()
            instance.original_dll = (self.backend / 'Prodimt.Pedidos.Api.dll').read_bytes()
            instance.overrides = {'backend/appsettings.json': b'{}'}
        with patch.object(installer, 'BACKUPS', backups), patch.object(installer, 'ENV_FILE', env), \
                patch.object(instance, 'preflight', side_effect=preflight), \
                patch.object(instance, 'authenticate_admin', side_effect=lambda: events.append('authenticate')), \
                patch.object(instance, 'stage', side_effect=lambda: events.append('stage')), \
                patch.object(instance, 'links'), patch.object(instance, 'process'), \
                patch.object(instance, 'backup_and_migrate', side_effect=lambda: events.append('backup-and-migrate')), \
                patch.object(instance, 'switch', side_effect=lambda target: events.append('activate')), \
                patch.object(instance, 'health', side_effect=lambda new: events.append('new-health')), \
                patch.dict(self.support(instance), command=lambda args, **kw: events.append(args[1]) or ''):
            instance.apply()
        self.assertEqual(['preflight','authenticate','stage','stop','backup-and-migrate','activate','start','new-health'], events)
        self.assertEqual('updated', instance.state['status'])
        self.assertTrue(instance.state['health_verified'])
        snapshot = Path(instance.state['private_snapshot'])
        self.assertEqual(0o700, snapshot.stat().st_mode & 0o777)
        self.assertEqual(0o600, (snapshot / 'api.env').stat().st_mode & 0o777)
        self.assertEqual(env.read_bytes(), (snapshot / 'api.env').read_bytes())

    def test_activation_failure_restores_previous_application_without_database_restore(self):
        instance = self.instance(); instance.stop_requested = True; events = []
        with patch.object(instance, 'switch', side_effect=lambda target: events.append('old-links')), \
                patch.object(instance, 'health', side_effect=lambda new: events.append('old-health')), \
                patch.dict(self.support(instance), command=lambda args, **kw: events.append(args[1]) or ''):
            instance.rollback()
        self.assertEqual(['stop', 'old-links', 'start', 'old-health'], events)
        self.assertTrue(instance.state['previous_health_verified'])
        self.assertFalse(instance.state['database_automatically_restored'])

    def test_environment_and_production_json_override_remain_separate_from_payload(self):
        instance = self.instance()
        instance.overrides = {'backend/appsettings.json': b'old-default', 'backend/appsettings.Production.json': b'private-config'}
        expected = instance.expected('backend')
        self.assertEqual(b'old-default', expected['appsettings.json'])
        self.assertEqual(b'private-config', expected['appsettings.Production.json'])
        self.assertNotIn('backend/api.env', instance.files)


if __name__ == '__main__': unittest.main()
