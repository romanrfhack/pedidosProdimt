#!/usr/bin/env python3
"""Build a pinned I22 native delivery update from a clean integrated source commit."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261103020000_AddDeliveryOperations'
FEATURE = 'delivery-operations-v1'
PREVIOUS_PAIRS = (('production-d3a11420b202', 'production-d3a11420b202'),
                  ('production-d3a11420b202', 'orders-catalog-867df56de3bb'))
PREFIX = 'prodimt-delivery-update/'
SHARED_ASSEMBLIES = ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Application.dll',
                     'Prodimt.Pedidos.Domain.dll', 'Prodimt.Pedidos.Infrastructure.dll')


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def helper(name, path):
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


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


def normalized_validation_baseline(raw):
    """Retain EF statements and apply only reviewed historical GO boundaries."""
    pilot = ROOT / 'scripts/pilot'
    validation = helper('i22_builder_baseline_validator', pilot / 'validate-delivery-sql.py')
    token = helper('i22_builder_token_normalizer', pilot / 'install-access-token-management-update.py')
    security = helper('i22_builder_security_normalizer', pilot / 'build-identity-package.py')
    validation.validate_baseline(raw)
    blocks = list(re.finditer(r'(?ms)^BEGIN TRANSACTION;\n.*?^COMMIT;\nGO\n\n', raw))
    if len(blocks) != 16:
        raise ValueError('validation_baseline_layout_unexpected')
    result = raw
    for block in reversed(blocks):
        segment = block.group()
        ids = re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", segment)
        if len(ids) != 1:
            raise ValueError('validation_baseline_increment_unexpected')
        if ids[0] == token.MIGRATION:
            segment = token.migration_sql_for_sqlcmd(segment)
        elif ids[0] == security.MIGRATION:
            segment = security.migration_sql_for_sqlcmd(segment)
        result = result[:block.start()] + segment + result[block.end():]
    validation.validate_baseline(result)
    return result


def build(backend, frontend, migration, output, source, validation, baseline_migration):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    installer = helper('i22_builder_installer', pilot / 'install-delivery-update.py')
    schema = helper('i22_builder_schema', pilot / 'delivery-schema-contract.py')
    production_builder = helper('i22_historical_builder', pilot / 'build-production-package.py')
    sql = migration.read_text(encoding='utf-8-sig')
    installer.validate_migration_sql(sql)
    files = {name: (pilot / original).read_bytes() for name, original in (
        ('inspect-native.py', 'inspect-native.py'),
        ('native-deployment-support.py', 'install-quick-capture-update.py'),
        ('commercial-catalog-support.py', 'install-commercial-catalog-update.py'),
        ('token-management-support.py', 'install-access-token-management-update.py'),
        ('customer-products-support.py', 'install-customer-products-update.py'),
        ('security-administration-support.py', 'install-identity-update.py'),
        ('production-support.py', 'install-production-update.py'),
        ('delivery-schema-contract.py', 'delivery-schema-contract.py'),
        ('validate-delivery-sql.py', 'validate-delivery-sql.py'))}
    files['migrations-i22.sql'] = sql.encode()
    files['delivery-schema.json'] = json.dumps(schema.contract_from_sql(sql), sort_keys=True).encode()
    files['validation-baseline.sql'] = normalized_validation_baseline(baseline_migration.read_text(encoding='utf-8-sig')).encode()
    for part, directory in (('backend', backend), ('frontend', frontend), ('validation', validation)):
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('build_directory_invalid')
        for path in sorted(directory.rglob('*')):
            if path.is_symlink():
                raise ValueError('build_symlink_not_allowed')
            if not path.is_file():
                continue
            relative = path.relative_to(directory)
            name = relative.as_posix()
            if any(segment.startswith('.') for segment in relative.parts) or path.suffix.lower() in (
                    '.env', '.key', '.pem', '.pfx', '.p12', '.bak', '.cs', '.ts', '.xlsx', '.csv', '.log', '.dmp', '.trace'):
                raise ValueError('build_private_or_source_file_not_allowed')
            if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
                continue
            raw = path.read_bytes()
            if part in ('backend', 'validation') and name.startswith('appsettings') and name.endswith('.json') \
                    and production_builder.contains_encryption_key(raw):
                raise ValueError('encryption_key_must_not_be_packaged')
            files[part + '/' + name] = raw
    required = {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                'backend/appsettings.json', 'frontend/index.html', 'frontend/3rdpartylicenses.txt'}
    required.update(installer.VALIDATION_REQUIRED)
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    if any('backend/' + name not in files or 'validation/' + name not in files
           or files['backend/' + name] != files['validation/' + name] for name in SHARED_ASSEMBLIES):
        raise ValueError('validation_application_binary_mismatch')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    markers = (b'apiBaseUrl:""', b'/access-tokens', b'/prices', b'/security', b'/product-setups',
               b'/additional-products', b'/operations/production', b'delivery.read.own', b'delivery.manage', b'/delivery')
    if any(marker not in scripts for marker in markers):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION,
                    migrationsBefore=16, migrationsAfter=17, previousPairs=[list(pair) for pair in PREVIOUS_PAIRS],
                    validation=dict(schema='prodimt.delivery.validation.v1', expected=3, beforeProductionChanges=True),
                    revokeTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-delivery-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    exported = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I22 — Mi reparto: Repartidor - Camioneta
REVISIÓN 3 — ESQUEMA VACÍO EXPLÍCITO Y VALIDACIÓN SQL PREVIA

La consulta de esquema siempre emite las cinco listas, incluso vacías. Conserva
la comparación estricta: no acepta objetos incompletos ni omite sus comprobaciones.

--apply primero ejecuta tres pruebas EF/HTTP portátiles y el SQL exacto del
paquete en bases temporales generadas internamente, en la instancia SQL existente.
No requiere instalar SDK, Docker ni nuevas dependencias en el VPS: usa .NET10 y
sqlcmd existentes. Las credenciales SQL sólo se reciben de forma oculta y pasan
al proceso de validación por entorno privado, nunca por argumentos o archivos.
Requiere un administrador SQL que pueda crear y eliminar bases temporales.

Las pruebas verifican reservas concurrentes, autorización/reintento entre hosts,
DDL/grants/metadata estricta, permisos del usuario DML, rollback forzado y repetición.
El runtime captura diagnósticos internos y sólo devuelve códigos/resúmenes fijos.
Cada base temporal tiene nombre UUID y se elimina/verifica antes de tocar el
despliegue. Un fallo detiene la actualización antes de snapshot/stage/marker/stop.
No se modifica una base cuyo nombre venga de configuración ni se restaura SQL.
Si falla limpieza, el JSON identifica únicamente la base temporal por revisar.

La revisión visual de tests/e2e/specs/delivery-operations.spec.ts y aceptación de
la ruta piloto se completan por separado; compilar no acredita despliegue.

Actualiza backend/frontend con reparto y conserva I16, I18, I19 e I21.
Bases admitidas exclusivamente:
  Backend production-d3a11420b202 + frontend production-d3a11420b202; o
  Backend production-d3a11420b202 + frontend orders-catalog-867df56de3bb.
Comprueba las 16 identidades de migración; aplica sólo AddDeliveryOperations (17).
Una versión concurrente desconocida se rechaza antes de cambiar archivos o datos.

Conserva pedidos originales, producción, precios/historial, cuentas, concesiones
anteriores, contraseñas, api.env y clave/tokens existentes. No incluye usuarios
reales, clientes, rutas, mapas, secretos, importaciones ni datos de demostración.
No ejecutar los instaladores históricos I16/I18/I19/I21 para obtener esta unión.

1. Extraer y verificar:
sha256sum -c SHA256SUMS.txt
scp PRODIMT_Instalar_I22.py prodimt-delivery-payload.zip root@194.238.26.70:/root/

2. Inspección de sólo lectura, sin credenciales SQL administrativas:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I22.py --package /root/prodimt-delivery-payload.zip'
Esperado planned/completed, changes_made=false, base_release_verified=true,
health_verified=true. Si falla una guarda, compartir el JSON y no aplicar.

3. Opcional: validar aisladamente sin aplicar ni reiniciar PRODIMT:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I22.py --package /root/prodimt-delivery-payload.zip --validate-only'
Esperado validated/completed, isolated_validation_verified=true,
validation_databases_cleaned=true, changes_made=false y production_changes_made=false.

4. Con los guardados abiertos resueltos, aplicar desde terminal interactiva:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I22.py --package /root/prodimt-delivery-payload.zip --apply'
Solicita las credenciales SQL existentes de forma oculta, nunca la contraseña web.
Repite primero la validación aislada. Sólo si pasa, crea snapshot privado y
BACKUP COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY antes del DDL operativo.
Migra esquema/permisos en una transacción; activa enlaces y reinicia sólo PRODIMT.
Verifica salud, assets, autorización, datos previos, clave de tokens y permisos.
Esperado updated/completed, migrations_after=17, verificaciones true y recovery_pending=false.

Si falla después de DDL, conserva aplicación detenida y el marcador privado para
recuperar hacia I22 con el MISMO paquete. No activa binarios anteriores que ignoren
la custodia de producto ni restaura SQL automáticamente. No borrar el marcador.
Un paquete distinto o cambios de configuración durante recuperación se rechazan.
Repetir instalado y saludable muestra already_current sin migrar/reotorgar roles.

5. Ctrl+F5. Seguridad: asignar el rol Repartidor - Camioneta a las cuentas elegidas.
El rol nuevo recibe delivery.read.own y delivery.operate.own. Administrador recibe
también delivery.manage. No se asigna el rol a ninguna cuenta automáticamente.
Reparto administrativo: completar rutas, chofer habitual, sustituto por fecha/vuelta,
ubicaciones de clientes y asignar cantidades por partida. Mi reparto: contar,
cargar, entregar, anotar observaciones y consultar historial con hora del servidor.
Las instrucciones del cliente y las notas de una visita se mantienen separadas.

Prueba piloto sugerida con datos ficticios: pedido50, reserva25, conteo25, carga25,
entrega20 => cliente pendiente30,25 sin asignar y5 en custodia/aclaración. Segunda
vuelta y transferencias no duplican reservas ni reescriben el pedido original.
No sumar kg con paquetes/piezas/docenas ni convertir10+2 en9+3. Cobranza/tickets y
seguimiento de envases no se contabilizan automáticamente por escribir una nota.
Instalación no acredita aceptación operativa; validar primero una ruta piloto.
'''
    outer = {'PRODIMT_Instalar_I22.py': exported.encode(), 'prodimt-delivery-payload.zip': payload,
             'LEEME.txt': readme.encode()}
    outer['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(outer.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in outer.items()}))
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'validation', 'baseline-migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source,
                          args.validation, args.baseline_migration), indent=2))


if __name__ == '__main__':
    main()
