import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT=Path(__file__).resolve().parents[2]


def module(name,file):
    spec=importlib.util.spec_from_file_location(name,ROOT/'scripts/pilot'/file)
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result


I=module('configuration_installer','install-client-configuration-update.py')
B=module('configuration_builder','build-client-configuration-package.py')
SOURCE='a'*40


class ClientConfigurationPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.path=Path(self.temp.name)
        self.files={'index.html':b'<script src="main-demo.js"></script>','main-demo.js':b'public-application'}

    def payload(self,extra=None):
        files={'frontend/'+name:raw for name,raw in self.files.items()}
        if extra:files.update(extra)
        manifest=dict(feature='customer-configuration-v1',sourceCommit=SOURCE,
                      files={name:dict(bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest()) for name,raw in files.items()})
        path=self.path/'payload.zip'
        with zipfile.ZipFile(path,'w') as archive:
            archive.writestr('manifest.json',json.dumps(manifest))
            for name,raw in files.items():archive.writestr(name,raw)
        return path,hashlib.sha256(path.read_bytes()).hexdigest()

    def fixture(self):
        root=self.path/'app';backups=self.path/'backups';backups.mkdir()
        for part in ('backend','frontend'):(root/part/'releases').mkdir(parents=True)
        old=root/'frontend/releases'/I.OLD;old.mkdir();(old/'index.html').write_bytes(b'old-ui')
        (root/'frontend/current').symlink_to(old)
        with patch.object(I,'ROOT',root):instance=I.Installer(self.path/'unused.zip')
        instance.files=dict(self.files);instance.previous=old;instance.identity=(100,'unchanged-backend')
        instance.preflight=lambda:None
        instance.backend_identity=lambda:instance.identity
        instance.api_health=lambda:None
        def http(path):
            current=instance.current.resolve()
            return 200,(current/('index.html' if path=='/' else path.lstrip('/'))).read_bytes()
        instance.http=http
        return instance,backups

    def test_pin_and_manifest_return_only_the_expected_frontend(self):
        path,pin=self.payload()
        with patch.object(I,'EXPECTED_SOURCE',SOURCE),patch.object(I,'EXPECTED_PAYLOAD_SHA256',pin):
            self.assertEqual(self.files,I.load_payload(path))
        with patch.object(I,'EXPECTED_SOURCE',SOURCE),patch.object(I,'EXPECTED_PAYLOAD_SHA256','b'*64):
            with self.assertRaisesRegex(I.InstallError,'payload_hash_mismatch'):I.load_payload(path)

    def test_traversal_and_non_frontend_files_are_rejected(self):
        for name in ('frontend/../../outside','backend/Prodimt.Pedidos.Api.dll','frontend/debug.map'):
            with self.subTest(name=name):
                path,pin=self.payload({name:b'forbidden'})
                with patch.object(I,'EXPECTED_SOURCE',SOURCE),patch.object(I,'EXPECTED_PAYLOAD_SHA256',pin):
                    with self.assertRaises(I.InstallError):I.load_payload(path)

    def test_apply_switches_only_frontend_and_second_apply_is_idempotent(self):
        instance,backups=self.fixture()
        with patch.object(I,'BACKUPS',backups):
            result=instance.apply()
            self.assertEqual('updated',result['status']);self.assertTrue(result['frontend_verified'])
            self.assertEqual(instance.new,instance.current.resolve())
            self.assertFalse(result['database_migrations_run']);self.assertFalse(result['service_restarted'])
            self.assertFalse(result['backend_changed'])
            self.assertEqual(b'old-ui',(instance.old/'index.html').read_bytes())
            instance.previous=instance.new
            result=instance.apply();self.assertEqual('already_current',result['status'])
            self.assertEqual(1,len(list(backups.iterdir())))

    def test_failed_new_assets_restore_previous_link(self):
        instance,backups=self.fixture()
        http=instance.http
        def failed(path):
            if instance.current.resolve()==instance.new:return 500,b'SYNTHETIC_PRIVATE_ERROR'
            return http(path)
        instance.http=failed
        with patch.object(I,'BACKUPS',backups):
            with self.assertRaisesRegex(I.InstallError,'new_frontend_asset_not_served'):instance.apply()
        self.assertEqual(instance.old,instance.current.resolve())
        self.assertTrue(instance.state['previous_frontend_restored'])
        self.assertNotIn('SYNTHETIC_PRIVATE_ERROR',json.dumps(instance.state))

    def test_changed_backend_stops_before_frontend_activation(self):
        instance,backups=self.fixture();instance.backend_identity=lambda:(101,'different')
        with patch.object(I,'BACKUPS',backups):
            with self.assertRaisesRegex(I.InstallError,'backend_changed_during_update'):instance.apply()
        self.assertEqual(instance.old,instance.current.resolve())

    def test_interruption_immediately_after_link_activation_also_restores_previous(self):
        instance,backups=self.fixture();switch=instance.switch
        def interrupted(target):
            switch(target)
            if target==instance.new:raise I.InstallError('installation_interrupted')
        instance.switch=interrupted
        with patch.object(I,'BACKUPS',backups):
            with self.assertRaisesRegex(I.InstallError,'installation_interrupted'):instance.apply()
        self.assertEqual(instance.old,instance.current.resolve())
        self.assertTrue(instance.state['previous_frontend_restored'])

    def test_unexpected_current_link_is_never_replaced(self):
        instance,_=self.fixture();other=self.path/'other';other.mkdir()
        instance.current.unlink();instance.current.symlink_to(other)
        with self.assertRaisesRegex(I.InstallError,'frontend_changed_during_update'):instance.switch(instance.new)
        self.assertEqual(other,instance.current.resolve())

    def test_builder_pins_installer_and_never_packages_backend_or_source_files(self):
        frontend=self.path/'dist';frontend.mkdir()
        for name,raw in self.files.items():(frontend/name).write_bytes(raw)
        output=self.path/'package.zip'
        with patch.object(B.subprocess,'check_output',side_effect=[SOURCE+'\n','']):
            report=B.build(frontend,output,SOURCE)
        with zipfile.ZipFile(output) as archive:
            installer=archive.read('prodimt-client-configuration-update/PRODIMT_Instalar_I08.py').decode()
            self.assertIn(report['payloadSha256'],installer);self.assertIn(SOURCE,installer)
            self.assertNotIn('REPLACE_',installer)
            inner=archive.read('prodimt-client-configuration-update/prodimt-client-configuration-payload.zip')
            with zipfile.ZipFile(io.BytesIO(inner)) as payload:
                self.assertEqual({'manifest.json','frontend/index.html','frontend/main-demo.js'},set(payload.namelist()))

    def test_dirty_source_blocks_packaging_before_output(self):
        with patch.object(B.subprocess,'check_output',side_effect=[SOURCE+'\n',' M source.ts']):
            with self.assertRaisesRegex(ValueError,'source_tree_not_clean'):B.build(self.path,self.path/'out.zip',SOURCE)


if __name__=='__main__':unittest.main()
