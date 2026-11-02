#!/usr/bin/env python3
"""Build I22 ordering schedules from a clean source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PRODUCTION_MIGRATION = '20261103010000_AddProductionAllocation'
MIGRATION = '20261103030000_AddOrderingSchedules'
FEATURE = 'ordering-schedules-v1'
PREVIOUS_PAIRS = (('production-d3a11420b202','production-d3a11420b202'),
                  ('production-d3a11420b202','orders-catalog-867df56de3bb'))
SETUP_MIGRATION = '20261102152307_AddCustomerProductSetup'
IDENTITY_MIGRATION = '20261102235000_AddSecurityAdministration'
PREFIX = 'prodimt-ordering-schedules-update/'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_source(source):
    if not re.fullmatch(r'[a-f0-9]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')


def archive_bytes(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, raw)
    return stream.getvalue()


def helper(name, path):
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def validate_migration(sql):
    try:
        helper('i19_package_validator', ROOT / 'scripts/pilot/install-ordering-schedules-update.py').validate_migration_sql(sql)
    except Exception as error:
        raise ValueError('incremental_production_migration_unexpected') from error


def contains_encryption_key(raw):
    def visit(value):
        if not isinstance(value, dict):
            return False
        for key, child in value.items():
            if key.casefold() in ('customeraccesstokens:encryptionkey', 'customeraccesstokens__encryptionkey') and child:
                return True
            if key.casefold() == 'customeraccesstokens' and isinstance(child, dict) \
                    and any(name.casefold() == 'encryptionkey' and data for name, data in child.items()):
                return True
            if visit(child):
                return True
        return False
    return visit(json.loads(raw))


def build(backend, frontend, migration, output, source, setup_migration, identity_migration, production_migration):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    identity = helper('i19_historical_identity_builder', pilot / 'build-identity-package.py')
    setup = helper('i19_historical_setup_validator', pilot / 'install-customer-products-update.py')
    setup_sql = setup_migration.read_text(encoding='utf-8-sig')
    setup.validate_migration(setup_sql)
    identity_sql = identity.migration_sql_for_sqlcmd(identity_migration.read_text(encoding='utf-8-sig'))
    production_sql = production_migration.read_text(encoding='utf-8-sig')
    helper('historical_production_validator', pilot / 'install-production-update.py').validate_migration_sql(production_sql)
    schedule_sql = migration.read_text(encoding='utf-8-sig')
    validate_migration(schedule_sql)
    files = {'inspect-native.py': (pilot / 'inspect-native.py').read_bytes(),
             'native-deployment-support.py': (pilot / 'install-quick-capture-update.py').read_bytes(),
             'commercial-catalog-support.py': (pilot / 'install-commercial-catalog-update.py').read_bytes(),
             'token-management-support.py': (pilot / 'install-access-token-management-update.py').read_bytes(),
             'customer-products-support.py': (pilot / 'install-customer-products-update.py').read_bytes(),
             'security-administration-support.py': (pilot / 'install-identity-update.py').read_bytes(),
             'migrations-i16.sql': setup_sql.encode(), 'migrations-i18.sql': identity_sql.encode(),
             'migrations-i19.sql': production_sql.encode(), 'migrations-i22.sql': schedule_sql.encode()}
    for part, directory in (('backend', backend), ('frontend', frontend)):
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('build_directory_invalid')
        for path in sorted(directory.rglob('*')):
            if path.is_symlink():
                raise ValueError('build_symlink_not_allowed')
            if not path.is_file():
                continue
            name = path.relative_to(directory).as_posix()
            if any(segment.startswith('.') for segment in path.relative_to(directory).parts) \
                    or path.suffix.lower() in ('.env', '.key', '.pem', '.pfx', '.p12', '.bak', '.cs', '.ts', '.xlsx', '.csv', '.log', '.dmp', '.trace'):
                raise ValueError('build_private_or_source_file_not_allowed')
            if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
                continue
            raw = path.read_bytes()
            if part == 'backend' and name.startswith('appsettings') and name.endswith('.json') and contains_encryption_key(raw):
                raise ValueError('encryption_key_must_not_be_packaged')
            files[part + '/' + name] = raw
    required = {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                'backend/appsettings.json', 'frontend/index.html', 'frontend/3rdpartylicenses.txt'}
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'/prices', b'/security', b'directPermissionIds', b'roleIds', b'/product-setups', b'/additional-products', b'/operations/production', b'requiresMachine', b'/ordering-schedules', b'orderingWindow')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsAfter=17,
                    migrationIds=[SETUP_MIGRATION, IDENTITY_MIGRATION, PRODUCTION_MIGRATION, MIGRATION], previousPairs=[list(pair) for pair in PREVIOUS_PAIRS],
                    revokeTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-ordering-schedules-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = """PRODIMT I22 — Horarios por ruta/cliente y pedidos sujetos a confirmación

Actualización exclusiva sobre backend I19 production-d3a11420b202 con frontend I19
production-d3a11420b202 o I21 orders-catalog-867df56de3bb. Agrega sólo la migración
20261103030000_AddOrderingSchedules: dos columnas de Customers y RouteOrderSchedules.
Final: 17 migraciones. Mantiene tokens, claves, precios, seguridad y producción.

1. Verificar SHA256SUMS.txt desde esta carpeta.
2. Copiar PRODIMT_Instalar_I22.py y prodimt-ordering-schedules-payload.zip a /root del VPS.
3. Inspección sin cambios:
python3 /root/PRODIMT_Instalar_I22.py --package /root/prodimt-ordering-schedules-payload.zip
Esperado planned/completed. Si la base difiere, detenerse; no reinstalar paquetes viejos.
4. Aplicación con terminal interactiva, usando las credenciales SQL existentes en entrada oculta:
python3 /root/PRODIMT_Instalar_I22.py --package /root/prodimt-ordering-schedules-payload.zip --apply
Crea snapshot privado y respaldo SQL COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY antes del DDL.
DDL, permisos mínimos e historial se confirman juntos. Reinicia sólo PRODIMT y valida salud,
frontend y preservación de datos, permisos y clave. No envía WhatsApp ni modifica Nginx.
Si falla después del DDL, deja API detenida y marcador recovery_pending; repetir este mismo
paquete con --apply para recuperar hacia adelante. Nunca restaura SQL automáticamente.
Si ya quedó aplicado, already_current verifica sin alterar horarios ni reiniciar.

5. Ctrl+F5. Menú Horarios (permiso customers.manage): ruta o cliente. Cliente prevalece;
quitar su hora restaura ruta; ruta vacía usa 10:00. Horas menores a 18:00, CDMX.
Menú Por confirmar (permiso orders.review): aceptar/rechazar/ver detalle/ajustar.
Sólo tras aceptar se incorpora a producción y reparto. El aviso abre WhatsApp para revisión
manual, sin enviar. Una propuesta de otra fecha requiere respuesta y captura posterior.

Antes de las 18:00 se reciben solicitudes para ese día; desde las 18:00 y durante martes,
se propone el siguiente día hábil y se exige aceptar la fecha antes de capturar.
Un enlace vencido aún identificable y no revocado funciona sólo para solicitudes pendientes,
hasta las 18:00 del siguiente día hábil posterior a su vencimiento. Nunca se restablece el
acceso normal ni se habilitan enlaces revocados/inactivos. Revocar bloquea cada petición.
Pedidos sujetos a revisión no se incluyen en producción hasta su aceptación.
La instalación y las pruebas locales no acreditan la aceptación de producción.
"""
    bundle = {'PRODIMT_Instalar_I22.py': script.encode(), 'prodimt-ordering-schedules-payload.zip': payload,
              'LEEME.txt': readme.encode()}
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in bundle.items()}))
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'setup-migration', 'identity-migration', 'production-migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source, args.setup_migration, args.identity_migration, args.production_migration), indent=2))


if __name__ == '__main__':
    main()
