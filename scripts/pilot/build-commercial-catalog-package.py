#!/usr/bin/env python3
"""Build the pinned native I11 update from a clean, reviewed source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261101154618_AddSharedProductsAndCustomerPrices'
FEATURE = 'shared-products-customer-prices-v1'
PREFIX = 'prodimt-commercial-catalog-update/'


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


def build(backend, frontend, migration, output, source):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    files = {'inspect-native.py': (pilot / 'inspect-native.py').read_bytes(),
             'native-deployment-support.py': (pilot / 'install-quick-capture-update.py').read_bytes(),
             'migrations-i11.sql': migration.read_bytes()}
    sql = files['migrations-i11.sql'].decode('utf-8-sig')
    if MIGRATION not in sql or 'CustomerProductPriceRevisions' not in sql \
            or re.search(r'(?im)^\s*(UPDATE|DELETE|TRUNCATE|DROP)\s', sql) \
            or re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) != [MIGRATION]:
        raise ValueError('incremental_migration_unexpected')
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
                    or path.suffix.lower() in ('.env', '.key', '.pem', '.pfx', '.p12', '.bak', '.cs', '.ts', '.xlsx', '.csv'):
                raise ValueError('build_private_or_source_file_not_allowed')
            if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
                continue
            files[part + '/' + name] = path.read_bytes()
    required = {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                'backend/appsettings.json', 'frontend/index.html', 'frontend/3rdpartylicenses.txt'}
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/consolidation/', b'/prices', b'effectiveFrom', b'/api/customer-orders/')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsBefore=11, migrationsAfter=12,
                    productsConsolidated=False, pricesPreloaded=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-commercial-catalog-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I11 — Productos compartidos y precios por cliente

Actualiza backend/frontend nativos I07/I08 o I09 (con o sin modal I10) y migra de once a doce versiones.
No consolida productos ni registra precios automáticamente. No cambia contraseñas ni Nginx.
No crea cobros, saldos, impuestos o facturas. Los precios se capturan en MXN por unidad de venta.

1. Verificar SHA256SUMS.txt en WSL, desde esta carpeta.
2. Copiar:
scp PRODIMT_Instalar_I11.py prodimt-commercial-catalog-payload.zip root@194.238.26.70:/root/
3. Comprobar, sin modificar aplicación o base:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I11.py --package /root/prodimt-commercial-catalog-payload.zip'
Resultado esperado: planned, phase completed. Resolver cualquier database_mismatches primero.
Si hay discrepancias, conservar el resultado para resolverlas antes de aplicar. No ejecutar bootstrap.
4. Resolver capturas/borradores con guardado incierto; después instalar:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I11.py --package /root/prodimt-commercial-catalog-payload.zip --apply'
Solicita usuario/contraseña del administrador SQL existente, ocultos. No es la cuenta web.
Respalda y verifica SQL antes de migrar; reinicia sólo PRODIMT y conserva api.env/appsettings actuales.
Resultado esperado: updated, phase completed, migrations_after 12, health_verified/frontend_verified true,
existing_data_counts_preserved true y price_history_append_only_verified true.
Si falla la activación, restaura los enlaces de aplicación anteriores; nunca restaura SQL automáticamente.
El mismo paquete puede repetirse: already_current verifica archivos, permisos y salud sin reiniciar.

Después de instalar:
- Recargar con Ctrl+F5. En Catálogos > Productos > Revisar duplicados, revisar cada grupo y producto destino.
- Confirmar sólo productos físicos equivalentes y de la misma unidad. No se convierten kg, piezas o paquetes.
- Verificar nombre/impacto y motivo; los anteriores quedan archivados como equivalencias.
- En cada cliente revisar y aprobar nuevamente las posiciones afectadas para habilitar captura rápida.
- En Configuración del cliente > Productos > Precio, registrar importe, vigencia y motivo reales.
- Precio sin definir queda pendiente; cero es un precio gratuito explícito, no un valor predeterminado.
- Revisar historial: precio, vigencia, fecha/hora de registro, persona y motivo.
- Capturar un pedido de prueba acordado y revisar el detalle: importe por partida y total, o precio pendiente.
- Un cambio posterior de precio no modifica ese pedido; corregir su cantidad mantiene su precio original.

Pedidos anteriores sin precio conservan precio pendiente: no se calculan cobros históricos automáticamente.
El portal, enlaces y corte de las 18:00 CDMX de I09 y el modal de tokens I10 se conservan.
No se envían mensajes automáticamente. Este paquete incluye el modal: no reinstalar después un frontend anterior.
'''
    bundle = {'PRODIMT_Instalar_I11.py': script.encode(), 'prodimt-commercial-catalog-payload.zip': payload,
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
