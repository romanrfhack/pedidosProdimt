#!/usr/bin/env python3
"""Read-only shape diagnosis of the exact exported I22 revision 2 preflight."""
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

INSTALLER = Path('/root/PRODIMT_Instalar_I22.py')
PAYLOAD = Path('/root/prodimt-delivery-payload.zip')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
INSTALLER_SHA256 = '706c78a0fb134f4d3ece20229d217edc72c540161ce56b5e3ab55840ec622b34'
PAYLOAD_SHA256 = 'fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d'
SOURCE = '0cfe3ef511d80e4af032a3e64abd97391e86df08'
SECTIONS = ('columns', 'primaryKeys', 'foreignKeys', 'indexes', 'checks')
TABLES = ('CustomerDeliveryLocations', 'DeliveryAssignments', 'DeliveryEvents', 'DeliveryRoutes', 'DeliveryTrips')
FIELDS = {
    'columns': ('table','name','type','length','precision','scale','nullable','defaultBinding'),
    'primaryKeys': ('table','name','column','ordinal','disabled'),
    'foreignKeys': ('table','name','column','target','targetColumn','ordinal','disabled','untrusted','deleteAction','updateAction'),
    'indexes': ('table','name','column','ordinal','unique','disabled','filtered','descending'),
    'checks': ('table','name','disabled','untrusted','definition'),
}
KINDS = ('null','boolean','integer','number','string','array','object','other')
ALLOWED_ISSUES = frozenset((
    'delivery_schema_unexpected','migration_history_unexpected','previous_release_not_supported',
    'commercial_schema_unexpected','token_management_schema_unexpected','product_setup_schema_unexpected',
    'security_schema_unexpected','production_schema_unexpected','sql_read_failed',
    'sql_query_not_authorized','sql_metadata_format_unrecognized','sqlcmd_required',
    'previous_update_recovery_pending','different_update_recovery_pending','current_release_link_required',
    'service_identity_unexpected','production_configuration_unexpected','sql_target_unexpected',
    'file_identity_unexpected','payload_hash_mismatch','payload_file_hash_mismatch',
))


class DiagnosisError(Exception):
    pass


def require(condition, code):
    if not condition:
        raise DiagnosisError(code)


def kind(value):
    return {type(None):'null', bool:'boolean', int:'integer', float:'number', str:'string',
            list:'array', dict:'object'}.get(type(value), 'other')


def shape(value):
    """Names and definitions from SQL never enter the output."""
    result = dict(root_kind=kind(value), known_sections_present=[], unknown_root_fields_count=0, sections={})
    if type(value) is not dict:
        return result
    result['unknown_root_fields_count'] = sum(key not in SECTIONS for key in value)
    for section in SECTIONS:
        present = section in value
        if present:
            result['known_sections_present'].append(section)
        rows = value.get(section)
        data = dict(present=present, kind=kind(rows), count=len(rows) if type(rows) is list else None)
        if type(rows) is not list or not rows:
            result['sections'][section] = data
            continue
        data.update(row_kinds=dict.fromkeys(KINDS,0), known_field_presence=dict.fromkeys(FIELDS[section],0),
            known_field_kinds={name:dict.fromkeys(KINDS,0) for name in FIELDS[section]},
            unknown_row_fields_count=0, rows_by_allowed_table=dict.fromkeys(TABLES,0), unknown_table_rows_count=0)
        if type(rows) is list:
            for row in rows:
                data['row_kinds'][kind(row)] += 1
                if type(row) is not dict:
                    continue
                data['unknown_row_fields_count'] += sum(key not in FIELDS[section] for key in row)
                for name in FIELDS[section]:
                    if name in row:
                        data['known_field_presence'][name] += 1
                        data['known_field_kinds'][name][kind(row[name])] += 1
                table = row.get('table')
                if type(table) is str and table in TABLES:
                    data['rows_by_allowed_table'][table] += 1
                else:
                    data['unknown_table_rows_count'] += 1
        data['row_kinds'] = {key: count for key, count in data['row_kinds'].items() if count}
        data['known_field_presence'] = {key: count for key, count in data['known_field_presence'].items() if count}
        data['known_field_kinds'] = {key: {name: count for name, count in counts.items() if count}
            for key, counts in data['known_field_kinds'].items() if key in data['known_field_presence']}
        data['rows_by_allowed_table'] = {key: count for key, count in data['rows_by_allowed_table'].items() if count}
        result['sections'][section] = data
    return result


def observe_preflight(installer, payload):
    captures = []
    original_factory = installer.module
    def factory(name, raw):
        module = original_factory(name, raw)
        if name == 'i22_delivery_schema_support':
            original_matches = module.matches
            def matches(data, contract, strict=True):
                result = original_matches(data, contract, strict)
                try:
                    migrated = type(contract) is dict and any(type(contract.get(key)) is list and len(contract[key]) > 0 for key in SECTIONS)
                    captures.append(dict(strict=strict if type(strict) is bool else None,
                        migrated_schema_expected=migrated, comparison_false=result is False,
                        comparison_result=result if type(result) is bool else None,
                        observed=shape(data), expected=shape(contract)))
                except Exception:
                    captures.append(dict(capture_issue='shape_summary_failed'))
                return result
            module.matches = matches
        return module
    installer.module = factory
    try:
        instance = installer.make_installer(payload)
        instance.preflight()
        return dict(status='passed', issue=None), captures
    except Exception as error:
        # Only fixed installer codes cross the diagnostic boundary.
        code = str(error) if isinstance(error, installer.InstallError) else None
        return dict(status='failed', issue=code if code in ALLOWED_ISSUES else 'preflight_failed_unclassified'), captures
    finally:
        installer.module = original_factory


def pinned_bytes(path, expected, limit):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'diagnosis_file_identity_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'diagnosis_file_too_large')
    require(hashlib.sha256(raw).hexdigest() == expected, 'diagnosis_pinned_file_changed')
    return raw


def run_diagnosis(installer_path=INSTALLER, payload_path=PAYLOAD, lock_path=LOCK):
    report = dict(schema='prodimt.delivery.schema.diagnosis.v1', status='failed', read_only=True,
        installer_hash_verified=False, payload_hash_verified=False, lock_acquired=False,
        preflight=None, preflight_completed=False, capture_count=0, captures=[], production_changes_made=False,
        database_migrations_run=False, sql_admin_requested=False, service_changed=False)
    fd = None
    try:
        require(os.geteuid() == 0, 'diagnosis_root_required')
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
        fd = os.open(lock_path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        identity = os.fstat(fd)
        require(stat.S_ISREG(identity.st_mode) and identity.st_uid == 0 and identity.st_nlink == 1
                and stat.S_IMODE(identity.st_mode) == 0o600, 'diagnosis_lock_identity_unexpected')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        report['lock_acquired'] = True
        script = pinned_bytes(installer_path, INSTALLER_SHA256, 1024 * 1024)
        report['installer_hash_verified'] = True
        pinned_bytes(payload_path, PAYLOAD_SHA256, 40 * 1024 * 1024)
        report['payload_hash_verified'] = True
        installer = types.ModuleType('pinned_i22_diagnosis_installer')
        installer.__file__ = str(installer_path)
        exec(compile(script, '<pinned-i22-installer>', 'exec'), installer.__dict__)
        require(installer.EXPECTED_SOURCE == SOURCE and installer.EXPECTED_PAYLOAD_SHA256 == PAYLOAD_SHA256
                and installer.LOCK == LOCK, 'diagnosis_installer_identity_unexpected')
        report['preflight'], report['captures'] = observe_preflight(installer, payload_path)
        report.update(status='observed', preflight_completed=report['preflight']['status']=='passed',
                      capture_count=len(report['captures']))
    except DiagnosisError as error:
        report['issue'] = str(error)
    except BlockingIOError:
        report['issue'] = 'diagnosis_lock_busy'
    except OSError:
        report['issue'] = 'diagnosis_file_or_lock_unavailable'
    except Exception:
        report['issue'] = 'diagnosis_failed_unclassified'
    finally:
        if fd is not None:
            os.close(fd)
    return report


def main(arguments=None):
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        report = run_diagnosis() if not (sys.argv[1:] if arguments is None else arguments) else dict(
            schema='prodimt.delivery.schema.diagnosis.v1', status='failed', issue='diagnosis_arguments_not_supported', read_only=True)
    print(json.dumps(report, ensure_ascii=True, sort_keys=True))
    return 0 if report['status'] == 'observed' else 1


if __name__ == '__main__':
    sys.exit(main())
