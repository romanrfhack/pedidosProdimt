#!/usr/bin/env python3
"""Read-only aggregate diagnosis of the four fixed I22 security seeds."""
import sys
sys.dont_write_bytecode = True
import contextlib
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import resource
import stat
import types

INSTALLER = Path('/root/PRODIMT_Instalar_I22_v3.py')
PAYLOAD = Path('/root/prodimt-delivery-payload.zip')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
INSTALLER_SHA256 = '1c5dec8f3bd590f109ce76a6fb356af33b6e5eca78209fd2d2a62f7528cf5806'
PAYLOAD_SHA256 = 'fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d'
SOURCE = '0cfe3ef511d80e4af032a3e64abd97391e86df08'
SEEDS = (
    ('driver_role','SecurityRoles','Name','22220000-0000-0000-0000-000000000001','Repartidor - Camioneta','RoleId','SecurityUserRoles'),
    ('read_own','SecurityPermissions','Key','18180000-0000-0000-0001-000000000019','delivery.read.own','PermissionId','SecurityUserPermissions'),
    ('operate_own','SecurityPermissions','Key','18180000-0000-0000-0001-000000000020','delivery.operate.own','PermissionId','SecurityUserPermissions'),
    ('manage','SecurityPermissions','Key','18180000-0000-0000-0001-000000000021','delivery.manage','PermissionId','SecurityUserPermissions'),
)
COUNTS = ('exists_expected_id','label_match_count','same_id_and_label','different_id_same_label',
          'same_id_different_label','label_active','label_system','grants_count','user_assignments_count')
ALLOWED_ISSUES = frozenset(('delivery_schema_unexpected','migration_history_unexpected','previous_release_not_supported',
    'sql_read_failed','sql_query_not_authorized','sql_metadata_format_unrecognized','previous_update_recovery_pending',
    'different_update_recovery_pending','payload_hash_mismatch','file_identity_unexpected'))


def seed_query():
    rows = []
    for key,table,label_column,identity,label,foreign,user_table in SEEDS:
        ident = "s.Id='" + identity + "'"
        match = 's.[' + label_column + "]=N'" + label + "'"
        def count(predicate):
            return '(SELECT COUNT(*) FROM dbo.['+table+'] s WHERE '+predicate+')'
        expressions = [count(ident),count(match),count(ident+' AND '+match),
            count('NOT ('+ident+') AND '+match),count(ident+' AND NOT ('+match+')'),
            count(match+' AND s.IsActive=1'),count(match+' AND s.IsSystem=1')]
        for association in ('SecurityRolePermissions',user_table):
            expressions.append('(SELECT COUNT(*) FROM dbo.['+association+'] a WHERE EXISTS '
                '(SELECT 1 FROM dbo.['+table+'] s WHERE s.Id=a.['+foreign+'] AND ('+ident+' OR '+match+')))')
        rows.append("SELECT N'"+key+"' AS seed,"+','.join(value+' AS ['+name+']' for name,value in zip(COUNTS,expressions)))
    return 'SELECT * FROM ('+' UNION ALL '.join(rows)+') q FOR JSON PATH;'


SEED_QUERY = seed_query()


class DiagnosisError(Exception):
    pass


def require(condition, code):
    if not condition:
        raise DiagnosisError(code)


def safe_counts(rows):
    require(type(rows) is list and len(rows)==4,'seed_diagnosis_result_unexpected')
    result = {}
    for row in rows:
        require(type(row) is dict and set(row)=={'seed',*COUNTS},'seed_diagnosis_result_unexpected')
        key = row['seed']
        require(type(key) is str and key in {item[0] for item in SEEDS} and key not in result,
                'seed_diagnosis_result_unexpected')
        require(all(type(row[name]) is int and row[name]>=0 for name in COUNTS),'seed_diagnosis_result_unexpected')
        result[key] = {name:row[name] for name in COUNTS}
    return result


def read_seeds(installer, payload):
    instance = installer.make_installer(payload)
    instance.preflight()
    count = instance.state.get('migrations_before')
    require(type(count) is int and count in (16,17),'seed_diagnosis_history_unexpected')
    require(instance.state.get('health_verified') is True,'seed_diagnosis_health_not_verified')
    inventory = instance.inspector
    previous = inventory.QUERIES
    try:
        inventory.QUERIES = (*previous, SEED_QUERY)
        values = inventory.query_sql(instance.sql_tool, instance.fields, SEED_QUERY)
        return dict(history_count=count,health_verified=True,seeds=safe_counts(values))
    finally:
        inventory.QUERIES = previous


def pinned_bytes(path, expected, limit):
    fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
    with os.fdopen(fd,'rb') as stream:
        meta=os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid==0 and meta.st_nlink==1
                and not meta.st_mode&0o022,'seed_diagnosis_file_identity_unexpected')
        raw=stream.read(limit+1)
    require(len(raw)<=limit,'seed_diagnosis_file_too_large')
    require(hashlib.sha256(raw).hexdigest()==expected,'seed_diagnosis_pinned_file_changed')
    return raw


def run_diagnosis(installer_path=INSTALLER,payload_path=PAYLOAD,lock_path=LOCK):
    report=dict(schema='prodimt.delivery.seed.diagnosis.v1',status='failed',read_only=True,
        installer_hash_verified=False,payload_hash_verified=False,lock_acquired=False,
        history_count=None,health_verified=False,seeds=None,production_changes_made=False,
        database_migrations_run=False,sql_admin_requested=False,service_changed=False)
    fd=None
    try:
        require(os.geteuid()==0,'seed_diagnosis_root_required')
        resource.setrlimit(resource.RLIMIT_CORE,(0,0))
        fd=os.open(lock_path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
        meta=os.fstat(fd)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid==0 and meta.st_nlink==1
                and stat.S_IMODE(meta.st_mode)==0o600,'seed_diagnosis_lock_identity_unexpected')
        fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        report['lock_acquired']=True
        script=pinned_bytes(installer_path,INSTALLER_SHA256,1024*1024)
        report['installer_hash_verified']=True
        pinned_bytes(payload_path,PAYLOAD_SHA256,40*1024*1024)
        report['payload_hash_verified']=True
        installer=types.ModuleType('pinned_i22_seed_diagnosis')
        installer.__file__=str(installer_path)
        exec(compile(script,'<pinned-i22-installer>','exec'),installer.__dict__)
        require(installer.EXPECTED_SOURCE==SOURCE and installer.EXPECTED_PAYLOAD_SHA256==PAYLOAD_SHA256
                and installer.LOCK==LOCK,'seed_diagnosis_installer_identity_unexpected')
        try:
            report.update(read_seeds(installer,payload_path))
        except DiagnosisError:
            raise
        except Exception as error:
            code=str(error) if isinstance(error,installer.InstallError) else None
            raise DiagnosisError(code if code in ALLOWED_ISSUES else 'seed_diagnosis_read_failed') from None
        report['status']='observed'
    except DiagnosisError as error:
        report['issue']=str(error)
    except BlockingIOError:
        report['issue']='seed_diagnosis_lock_busy'
    except OSError:
        report['issue']='seed_diagnosis_file_or_lock_unavailable'
    except Exception:
        report['issue']='seed_diagnosis_failed_unclassified'
    finally:
        if fd is not None:
            os.close(fd)
    return report


def main(arguments=None):
    with contextlib.redirect_stdout(io.StringIO()),contextlib.redirect_stderr(io.StringIO()):
        report=run_diagnosis() if not (sys.argv[1:] if arguments is None else arguments) else dict(
            schema='prodimt.delivery.seed.diagnosis.v1',status='failed',issue='seed_diagnosis_arguments_not_supported',read_only=True)
    print(json.dumps(report,ensure_ascii=True,sort_keys=True))
    return 0 if report['status']=='observed' else 1


if __name__=='__main__':
    sys.exit(main())
