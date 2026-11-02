#!/usr/bin/env python3
"""Build the pinned I19 production and integrated I16/I18 update from a clean source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261103010000_AddProductionAllocation'
FEATURE = 'production-allocation-v1'
PREVIOUS_PAIRS = (('access-tokens-65560ca3cf98','customer-catalog-afb8f3ad9c33'),
                  ('access-tokens-65560ca3cf98','pending-customers-cdbfec3071bc'),
                  ('customer-products-b68982a14d1b','customer-products-b68982a14d1b'),
                  ('identity-c49df453de9d','identity-c49df453de9d'))
SETUP_MIGRATION = '20261102152307_AddCustomerProductSetup'
IDENTITY_MIGRATION = '20261102235000_AddSecurityAdministration'
PREFIX = 'prodimt-production-update/'


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
        helper('i19_package_validator', ROOT / 'scripts/pilot/install-production-update.py').validate_migration_sql(sql)
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


def build(backend, frontend, migration, output, source, setup_migration, identity_migration):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    identity = helper('i19_historical_identity_builder', pilot / 'build-identity-package.py')
    setup = helper('i19_historical_setup_validator', pilot / 'install-customer-products-update.py')
    setup_sql = setup_migration.read_text(encoding='utf-8-sig')
    setup.validate_migration(setup_sql)
    identity_sql = identity.migration_sql_for_sqlcmd(identity_migration.read_text(encoding='utf-8-sig'))
    production_sql = migration.read_text(encoding='utf-8-sig')
    validate_migration(production_sql)
    files = {'inspect-native.py': (pilot / 'inspect-native.py').read_bytes(),
             'native-deployment-support.py': (pilot / 'install-quick-capture-update.py').read_bytes(),
             'commercial-catalog-support.py': (pilot / 'install-commercial-catalog-update.py').read_bytes(),
             'token-management-support.py': (pilot / 'install-access-token-management-update.py').read_bytes(),
             'customer-products-support.py': (pilot / 'install-customer-products-update.py').read_bytes(),
             'security-administration-support.py': (pilot / 'install-identity-update.py').read_bytes(),
             'migrations-i16.sql': setup_sql.encode(), 'migrations-i18.sql': identity_sql.encode(),
             'migrations-i19.sql': production_sql.encode()}
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
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'/prices', b'/security', b'directPermissionIds', b'roleIds', b'/product-setups', b'/additional-products', b'/operations/production', b'requiresMachine')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsAfter=16,
                    migrationIds=[SETUP_MIGRATION, IDENTITY_MIGRATION, MIGRATION], previousPairs=[list(pair) for pair in PREVIOUS_PAIRS],
                    revokeTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-production-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = """PRODIMT I19 — Producción por máquina y asignación a pedidos

Integra productos adicionales I16, interfaz I17 y seguridad I18. Acepta sólo pares conocidos:
I12 + frontend I15/I17; I16 customer-products-b68982a14d1b; I18 identity-c49df453de9d.
Verifica IDs exactos de migraciones (13/14 conocidos), nunca sólo el conteo. Resultado:16 migraciones.
Conserva pedidos, precios e historial, cuentas/permisos existentes, contraseñas, api.env y claves/tokens I12.
No reinstalar incrementos anteriores ni ejecutar bootstrap/importaciones/revocaciones/SQL manual.

1. Desde la carpeta extraída, verificar SHA256SUMS.txt y copiar:
scp PRODIMT_Instalar_I19.py prodimt-production-payload.zip root@194.238.26.70:/root/
2. Inspección sólo lectura, sin credenciales administrativas SQL:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I19.py --package /root/prodimt-production-payload.zip'
Esperado planned/completed, changes_made=false, base_release_verified y health_verified true.
Si hay discrepancias, detenerse y compartir JSON; no repetir instaladores anteriores.
3. Resolver guardados pendientes; aplicar con terminal interactiva:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I19.py --package /root/prodimt-production-payload.zip --apply'
Solicita credenciales SQL existentes en entrada oculta, no contraseña web.
Crea snapshot privado y BACKUP COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY antes de cualquier DDL.
Aplica únicamente migraciones faltantes; DDL/historial/permisos mínimos comparten transacción por incremento.
Reinicia sólo PRODIMT y verifica backend, archivos frontend, autorización y misma clave efectiva.
Esperado updated/completed, migrations_after=16 y comprobaciones de esquema/datos/tokens/permisos/salud true.

Si falla tras cambiar SQL, conserva respaldo/snapshot y detiene API: no vuelve a binarios sin seguridad/producción.
El mismo paquete reconoce su marcador privado y muestra recovery_pending incluso con API detenida.
Repetir --apply con ese mismo paquete reanuda hacia I19: verifica esquema, no repite migraciones ni reotorga roles.
Si falla la comprobación de marcador/entorno/pines/esquema, requiere revisión manual compatible; nunca restaura SQL automáticamente.
Ya instalado y saludable: already_current sólo verifica, sin reiniciar ni modificar SQL.

4. Ctrl+F5. Catálogos/configuración: No requiere máquina excluye el producto del panel de producción.
Operación: asignar máquina diaria, registrar lotes en kg y asignar su saldo a renglones de pedidos compatibles.
No se mezclan productos, máquinas, fechas ni unidades. Recibir un aviso no registra kilos producidos.
Reversar una asignación exige motivo y conserva historial; los kilos vuelven al saldo disponible.
Permisos nuevos operations.assign / operations.produce / operations.allocate sólo se conceden al Administrador de sistema;
el administrador puede concederlos después a roles o usuarios. No cambian concesiones particulares existentes.
La entrega y las pruebas aisladas no acreditan instalación o aceptación del usuario.
"""
    bundle = {'PRODIMT_Instalar_I19.py': script.encode(), 'prodimt-production-payload.zip': payload,
              'LEEME.txt': readme.encode()}
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in bundle.items()}))
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'setup-migration', 'identity-migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source, args.setup_migration, args.identity_migration), indent=2))


if __name__ == '__main__':
    main()
