#!/usr/bin/env python3
"""Build the pinned I16 additional-products update from a clean source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261102152307_AddCustomerProductSetup'
FEATURE = 'customer-additional-products-v1'
PREVIOUS = dict(backend='access-tokens-65560ca3cf98', frontend='customer-catalog-afb8f3ad9c33')
PREFIX = 'prodimt-customer-products-update/'


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


def validate_migration(sql):
    import importlib.util
    path = ROOT / 'scripts/pilot/install-customer-products-update.py'
    spec = importlib.util.spec_from_file_location('i16_reviewed_migration_validator', path)
    installer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(installer)
    try:
        installer.validate_migration(sql)
    except installer.InstallError:
        raise ValueError('incremental_migration_unexpected') from None


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


def build(backend, frontend, migration, output, source):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    files = {'inspect-native.py': (pilot / 'inspect-native.py').read_bytes(),
             'native-deployment-support.py': (pilot / 'install-quick-capture-update.py').read_bytes(),
             'commercial-catalog-support.py': (pilot / 'install-commercial-catalog-update.py').read_bytes(),
             'migrations-i16.sql': migration.read_bytes()}
    validate_migration(files['migrations-i16.sql'].decode('utf-8-sig'))
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
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'expectedVersion', b'/prices',
        b'/additional-products', b'/product-setups', b'Pedir otro producto', b'useCustomerDefaultMachine')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsBefore=13, migrationsAfter=14,
                    previousReleases=PREVIOUS, revokeLegacyTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-customer-products-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I16 — Pedir otro producto y completar configuración

Requiere frontend I15 customer-catalog-afb8f3ad9c33 y backend I12 access-tokens-65560ca3cf98 saludables, trece migraciones.
Conserva I14/I15, clientes, productos consolidados, perfiles, pedidos, precios e historial y todos los tokens actuales.
Mantiene exactamente api.env y su clave de cifrado. No revoca tokens ni crea o cambia claves/contraseñas/Nginx.

1. Verificar SHA256SUMS.txt dentro de la carpeta extraída.
2. Copiar:
scp PRODIMT_Instalar_I16.py prodimt-customer-products-payload.zip root@194.238.26.70:/root/
3. Comprobar sólo lectura:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I16.py --package /root/prodimt-customer-products-payload.zip'
Esperado: planned/completed, sin cambios. No aplicar si informa discrepancias.
4. Terminar los borradores abiertos antes de aplicar (el navegador debe actualizarse después):
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I16.py --package /root/prodimt-customer-products-payload.zip --apply'
Solicita usuario y contraseña SQL existentes en entrada oculta; son las credenciales SQL, no las web.
Crea snapshot privado del entorno y configuraciones y COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY.
Detiene brevemente sólo PRODIMT, migra 13→14 y concede permisos mínimos en la misma transacción.
Verifica esquema, herencia de máquina, datos/precios/tokens y activa ambos releases.
No restaura SQL automáticamente: si falla, recupera los enlaces anteriores con salud y conserva el esquema aditivo.
Si falló después del commit SQL, repetir el mismo instalador verifica 14 y retoma sin repetir migración.
Si está activo I16, already_current comprueba archivos/salud/permisos/clave sin reiniciar ni escribir en SQL.

Esperado al aplicar: updated/completed, migrations_after 14, migrated_schema_verified,
existing_data_counts_preserved, price_history_append_only_verified, tokens_preserved,
machine_inheritance_preserved, setup_permissions_verified, encryption_key_effective_verified,
health_verified y frontend_verified true.

5. Ctrl+F5 en administración y portal. Captura manual → Pedir otro producto → elegir del catálogo y cantidad.
Agregar sólo modifica el borrador; Confirmar pedido/Guardar pedido registra pedido y pendiente atómicamente.
El pedido continúa con precio pendiente; no se convierte en precio cero ni se hereda máquina.
Administración → Configurar productos → Configurar producto: agregar a productos del cliente, guardar frecuentes,
definir Precio e historial con fecha de vigencia (incluidas las entregas anteriores pendientes) y motivo.
Revisar vista previa, indicar motivo, marcar revisión y Completar configuración.
Sólo completa precios null de las partidas vinculadas; conserva precios previos, incluso cero explícito.
La máquina es opcional; no aprobar posiciones o abreviaturas sin verificarlas.
La asignación diaria de producción, reventa, cobranza y roles completos siguen fuera de este incremento.
'''
    bundle = {'PRODIMT_Instalar_I16.py': script.encode(), 'prodimt-customer-products-payload.zip': payload,
              'LEEME.txt': readme.encode()}
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in bundle.items()}))
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source), indent=2))


if __name__ == '__main__':
    main()
