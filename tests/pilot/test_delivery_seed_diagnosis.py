"""Aggregated seed reads never expose identities or alter security/deployment."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

ROOT=Path(__file__).resolve().parents[2]


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


diagnosis=load('i22_seed_diagnosis_tests',ROOT/'scripts/pilot/diagnose-delivery-seeds.py')
CANARY='PRIVATE-IDENTITY-OR-SECRET-MUST-NOT-APPEAR'


def rows():
    return [dict(seed=seed[0],**dict.fromkeys(diagnosis.COUNTS,0)) for seed in diagnosis.SEEDS]


class DeliverySeedDiagnosisTests(unittest.TestCase):
    def test_exact_aggregate_query_uses_real_dml_transport_and_restores_allowlist(self):
        inventory=load('i22_seed_inventory_fixture',ROOT/'scripts/pilot/inspect-native.py')
        inventory.SQL_TOOLS=('synthetic-sqlcmd',)
        inventory.QUERIES=('SELECT fixture;',)
        original=inventory.QUERIES
        inventory.permitted_connection=Mock(return_value=True)
        output='ProdimtInspectionJson\n--------------------\n'+json.dumps(rows())
        inventory.command=Mock(return_value=(0,output))
        instance=types.SimpleNamespace(inspector=inventory,sql_tool='synthetic-sqlcmd',fields={'password':CANARY},
            preflight=Mock(),state={'migrations_before':16,'health_verified':True,'secret':CANARY})
        for name in ('apply','rollback','authenticate_admin','stage','sql_run','validate_before_apply'):
            setattr(instance,name,Mock(side_effect=AssertionError('Forbidden operation')))
        installer=types.SimpleNamespace(make_installer=Mock(return_value=instance))
        report=diagnosis.read_seeds(installer,Path('/synthetic-payload'))
        instance.preflight.assert_called_once()
        inventory.command.assert_called_once()
        args,kwargs=inventory.command.call_args
        self.assertIn(diagnosis.SEED_QUERY.rstrip(';'),args[0][-1])
        self.assertNotIn(CANARY,' '.join(args[0]))
        self.assertEqual(kwargs['env']['SQLCMDUSER'],'prodimt_pedidos_app')
        self.assertEqual(inventory.QUERIES,original)
        self.assertNotIn(CANARY,json.dumps(report))
        self.assertEqual(set(report['seeds']),{seed[0] for seed in diagnosis.SEEDS})
        for name in ('apply','rollback','authenticate_admin','stage','sql_run','validate_before_apply'):
            getattr(instance,name).assert_not_called()

    def test_collisions_return_counts_without_unknown_label_or_guid(self):
        data=rows();data[0].update(label_match_count=1,different_id_same_label=1,grants_count=3,user_assignments_count=2)
        report=diagnosis.safe_counts(data)
        self.assertEqual(report['driver_role']['different_id_same_label'],1)
        self.assertEqual(report['driver_role']['user_assignments_count'],2)
        self.assertEqual(len(report),4)
        for seed in report.values():
            self.assertEqual(set(seed),set(diagnosis.COUNTS))
            self.assertTrue(all(type(value) is int and value>=0 for value in seed.values()))

    def test_unexpected_keys_seeds_types_or_negative_counts_are_rejected(self):
        bad=[]
        data=rows();data[0][CANARY]=CANARY;bad.append(data)
        data=rows();data[0]['seed']=CANARY;bad.append(data)
        data=rows();data[0]['exists_expected_id']=CANARY;bad.append(data)
        data=rows();data[0]['exists_expected_id']=True;bad.append(data)
        data=rows();data[0]['exists_expected_id']=-1;bad.append(data)
        bad.append(rows()[:3])
        data=rows();data[1]['seed']=data[0]['seed'];bad.append(data)
        for value in bad:
            with self.subTest(value_type=type(value).__name__),self.assertRaisesRegex(diagnosis.DiagnosisError,'seed_diagnosis_result_unexpected'):
                diagnosis.safe_counts(value)

    def test_query_contains_only_aggregate_selects_without_collation_override(self):
        query=diagnosis.SEED_QUERY
        for seed in diagnosis.SEEDS:
            self.assertIn(seed[3],query)
            self.assertIn(seed[4],query)
        self.assertEqual(query.count(' AS seed,'),4)
        self.assertNotIn('COLLATE',query)
        self.assertNotIn('UPDATE ',query)
        self.assertNotIn('DELETE ',query)
        self.assertNotIn('INSERT ',query)
        self.assertNotIn('GRANT ',query)
        self.assertNotIn('SELECT s.Name',query)
        self.assertNotIn('SELECT s.Id',query)

    def test_failed_select_restores_allowlist_and_private_output_is_discarded(self):
        inventory=types.SimpleNamespace(QUERIES=('fixture',),query_sql=Mock(side_effect=ValueError(CANARY)))
        instance=types.SimpleNamespace(preflight=Mock(),state={'migrations_before':16,'health_verified':True},
            inspector=inventory,sql_tool='synthetic',fields={})
        with self.assertRaises(ValueError):
            diagnosis.read_seeds(types.SimpleNamespace(make_installer=lambda _:instance),Path('/synthetic'))
        self.assertEqual(inventory.QUERIES,('fixture',))
        def run():
            print(CANARY);print(CANARY,file=os.sys.stderr)
            return dict(status='failed',issue='seed_diagnosis_read_failed')
        output,errors=io.StringIO(),io.StringIO()
        with patch.object(diagnosis,'run_diagnosis',side_effect=run),contextlib.redirect_stdout(output),contextlib.redirect_stderr(errors):
            self.assertEqual(diagnosis.main([]),1)
        self.assertNotIn(CANARY,output.getvalue()+errors.getvalue())
        self.assertEqual(json.loads(output.getvalue())['issue'],'seed_diagnosis_read_failed')

    def test_pins_and_lock_are_checked_before_import_without_writing(self):
        real_fstat=os.fstat
        def root_meta(fd):
            current=real_fstat(fd)
            return types.SimpleNamespace(st_uid=0,st_mode=current.st_mode,st_nlink=current.st_nlink)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);lock=root/'lock';script=root/'installer';payload=root/'payload'
            lock.write_bytes(b'');lock.chmod(0o600);script.write_bytes(CANARY.encode());payload.write_bytes(b'fixture')
            for changed in ('installer','payload'):
                with self.subTest(changed=changed),patch.object(diagnosis.os,'geteuid',return_value=0), \
                    patch.object(diagnosis.resource,'setrlimit'),patch.object(diagnosis.os,'fstat',side_effect=root_meta), \
                    patch.object(diagnosis.fcntl,'flock'),patch.object(diagnosis,'INSTALLER_SHA256',
                        '0'*64 if changed=='installer' else hashlib.sha256(script.read_bytes()).hexdigest()), \
                    patch.object(diagnosis,'PAYLOAD_SHA256','0'*64),patch('builtins.exec') as execute, \
                    patch.object(diagnosis.os,'open',wraps=os.open) as opened:
                    result=diagnosis.run_diagnosis(script,payload,lock)
                execute.assert_not_called()
                self.assertEqual(result['issue'],'seed_diagnosis_pinned_file_changed')
                self.assertNotIn(CANARY,json.dumps(result))
                for call in opened.call_args_list:
                    self.assertFalse(call.args[1]&(os.O_CREAT|os.O_RDWR|os.O_WRONLY))
                    self.assertTrue(call.args[1]&os.O_NOFOLLOW)


if __name__=='__main__':
    unittest.main()
