#!/usr/bin/env python3
"""Package already-built I06 binaries and pin their native installer."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile


def build(backend, frontend, migration, destination, source):
    if not re.fullmatch(r'[a-f0-9]{40}', source):
        raise ValueError('source_commit_invalid')
    root = Path(__file__).parents[2]
    files = {'inspect-native.py': (root / 'scripts/pilot/inspect-native.py').read_bytes(),
             'migrations-i06.sql': migration.read_bytes()}
    sql = files['migrations-i06.sql'].decode('utf-8-sig')
    if ('20261028142346_AddCustomerRestDays' not in sql or 'ADD [RestDaysMask]' not in sql
            or 'CK_Customers_RestDaysMask' not in sql or 'CREATE TABLE' in sql or 'DROP TABLE' in sql):
        raise ValueError('incremental_migration_unexpected')
    for part, folder in (('backend', backend), ('frontend', frontend)):
        for path in folder.rglob('*'):
            if path.is_symlink(): raise ValueError('input_symlink_invalid')
            if path.is_file() and path.suffix not in ('.pdb', '.map'):
                files[part + '/' + path.relative_to(folder).as_posix()] = path.read_bytes()
    if not {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json', 'frontend/index.html'} <= set(files):
        raise ValueError('required_files_missing')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if (b'apiBaseUrl:""' not in scripts or b'/api/admin/account/change-password' not in scripts
            or b'/api/admin/customers/day-status' not in scripts or b'Descanso programado' not in scripts):
        raise ValueError('frontend_feature_or_origin_missing')
    manifest = {'sourceCommit': source, 'feature': 'admin-customer-cards-v1', 'files': {
        name: {'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()} for name, raw in files.items()}}
    with tempfile.TemporaryDirectory() as temp:
        payload = Path(temp) / 'payload.zip'
        with zipfile.ZipFile(payload, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('manifest.json', json.dumps(manifest, sort_keys=True))
            for name, raw in sorted(files.items()): archive.writestr(name, raw)
        digest = hashlib.sha256(payload.read_bytes()).hexdigest()
        installer = (root / 'scripts/pilot/install-catalog-update.py').read_text()
        installer = installer.replace("EXPECTED_PACKAGE_SHA256 = 'REPLACE_PACKAGE_SHA256'", "EXPECTED_PACKAGE_SHA256 = '" + digest + "'")
        installer = installer.replace("EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'", "EXPECTED_SOURCE = '" + source + "'")
        readme = ('PRODIMT I06 - Catálogo de clientes\n\n'
                  'Verifica SHA256SUMS.txt antes de copiar los dos archivos PRODIMT al VPS.\n'
                  'Ejecuta con SSH -t desde una terminal interactiva:\n'
                  'python3 /root/PRODIMT_Instalar_I06.py --package /root/prodimt-catalog-payload.zip --apply\n'
                  'Solicita usuario y contraseña del administrador SQL existente, ocultos. No son los del acceso web.\n'
                  'Comprueba I05 activo y configuración, respalda SQL con COPY_ONLY/CHECKSUM y verifica el respaldo.\n'
                  'Después agrega RestDaysMask y su restricción (historial 9 a 10), sin reemplazar clientes ni pedidos.\n'
                  'Conserva cuentas, contraseñas y configuración. Reinicia brevemente sólo la API de PRODIMT.\n'
                  'Si falla, intenta volver a la aplicación anterior; no restaura SQL automáticamente.\n'
                  'Una migración completa con aplicación anterior puede reintentarse con este mismo paquete.\n'
                  'Si ya está instalado I06, no vuelvas a ejecutar el instalador.\n\n'
                  'Tras status updated, migrations_after 10 y health_verified true, recarga con Ctrl+F5.\n'
                  'Catálogos > Clientes: buscar, filtrar y Nuevo cliente. Editar abre el modal y días de descanso.\n'
                  'Ámbar: por confirmar; verde: con pedido; gris: no pide o descanso. Estos últimos van al final.\n'
                  'Los descansos se configuran por cliente; no se infieren de pedidos históricos.\n'
                  'Un descanso no desactiva al cliente ni crea un pedido NoOrder. Un pedido vigente prevalece.\n'
                  'Los estados usan la fecha operativa de Ciudad de México y se actualizan cada 30 segundos.\n')
        bundle = {'prodimt-catalog-payload.zip': payload.read_bytes(), 'PRODIMT_Instalar_I06.py': installer.encode(), 'LEEME.txt': readme.encode()}
        bundle['SHA256SUMS.txt'] = ''.join(hashlib.sha256(raw).hexdigest() + '  ' + name + '\n'
                                         for name, raw in sorted(bundle.items())).encode()
        destination.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, raw in bundle.items(): archive.writestr('prodimt-catalog-update/' + name, raw)
    return {'source_commit': source, 'files': len(files), 'payload_sha256': digest,
            'bundle_sha256': hashlib.sha256(destination.read_bytes()).hexdigest(), 'bundle_bytes': destination.stat().st_size}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', type=Path, required=True)
    parser.add_argument('--frontend', type=Path, required=True)
    parser.add_argument('--migration', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).parents[2]
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip():
        raise ValueError('source_tree_not_clean')
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, source), indent=2))


if __name__ == '__main__':
    main()
