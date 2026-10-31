#!/usr/bin/env python3
"""Package already-built I07 binaries and pin their native installer."""
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
             'migrations-i07.sql': migration.read_bytes()}
    sql = files['migrations-i07.sql'].decode('utf-8-sig')
    if ('20261028202031_AddCustomerCaptureAliases' not in sql or 'ADD [CaptureAlias] nvarchar(16) NULL' not in sql
            or 'IX_CustomerFrequentProducts_CustomerId_CaptureAlias' not in sql
            or any(token in sql.upper() for token in ('CREATE TABLE', 'DROP TABLE', 'DROP COLUMN', 'RESTDAYSMASK'))):
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
            or b'/orders/preview' not in scripts or b'/orders/capture' not in scripts
            or b'Pedido del cliente' not in scripts or b'Captura manual' not in scripts):
        raise ValueError('frontend_feature_or_origin_missing')
    manifest = {'sourceCommit': source, 'feature': 'admin-quick-capture-v1', 'files': {
        name: {'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()} for name, raw in files.items()}}
    with tempfile.TemporaryDirectory() as temp:
        payload = Path(temp) / 'payload.zip'
        with zipfile.ZipFile(payload, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('manifest.json', json.dumps(manifest, sort_keys=True))
            for name, raw in sorted(files.items()): archive.writestr(name, raw)
        digest = hashlib.sha256(payload.read_bytes()).hexdigest()
        installer = (root / 'scripts/pilot/install-quick-capture-update.py').read_text()
        installer = installer.replace("EXPECTED_PACKAGE_SHA256 = 'REPLACE_PACKAGE_SHA256'", "EXPECTED_PACKAGE_SHA256 = '" + digest + "'")
        installer = installer.replace("EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'", "EXPECTED_SOURCE = '" + source + "'")
        readme = ("PRODIMT I07 - Captura rápida de pedidos\n\n"
                  "Requiere I06 nativo activo (catalog-965dc65d82d2). No reimportar catálogos.\n"
                  "Verifica SHA256SUMS.txt antes de copiar instalador y payload al VPS.\n"
                  "Ejecuta desde una terminal interactiva con SSH -t:\n"
                  "python3 /root/PRODIMT_Instalar_I07.py --package /root/prodimt-quick-capture-payload.zip --apply\n"
                  "Solicita usuario y contraseña del administrador SQL existente, ocultos. No son los del acceso web.\n"
                  "Conserva configuración, cuentas, clientes, productos, posiciones, máquinas y pedidos.\n"
                  "Detiene brevemente sólo la API de PRODIMT y verifica respaldo COPY_ONLY/CHECKSUM antes de migrar 10→11.\n"
                  "Agrega CaptureAlias nullable y un índice único filtrado por cliente. No precarga abreviaturas.\n"
                  "Si falla intenta recuperar los releases anteriores; no restaura SQL automáticamente.\n"
                  "Una migración completa con aplicación I06 puede reintentarse con este mismo paquete.\n"
                  "Si ya está instalado I07, no ejecutar otra vez.\n\n"
                  "Después de status updated, migrations_after 11 y health_verified true: Ctrl+F5.\n"
                  "Pendientes > Capturar pedido: escribe 16 o 13+7 y sal del campo para ver el desglose.\n"
                  "16 usa posición 1; 13+7 usa posiciones 1 y 2 del cliente. Guardar es una acción separada.\n"
                  "Si el perfil importado está pendiente, revisa el orden y pulsa Confirmar orden y productos una vez.\n"
                  "Para 1+5+2HN, asigna HN explícitamente al producto correcto de ese cliente en Configurar cliente.\n"
                  "HN no tiene un significado supuesto. Las unidades proceden de cada producto.\n"
                  "La hora habitual es informativa y se cambia en Configurar cliente > Editar datos y entrega.\n"
                  "Captura manual permite escribir cantidades por producto; notas quedan contraídas.\n"
                  "Si falta una unidad, confírmala en configuración. No conviertas paquetes o piezas en kilos.\n"
                  "Si hay pedido previo, ábrelo para corregir el total con nota; otra entrega debe confirmarse.\n"
                  "Si se pierde la respuesta de guardado, pulsa Reintentar guardado para recuperar el mismo pedido.\n")
        bundle = {'prodimt-quick-capture-payload.zip': payload.read_bytes(), 'PRODIMT_Instalar_I07.py': installer.encode(), 'LEEME.txt': readme.encode()}
        bundle['SHA256SUMS.txt'] = ''.join(hashlib.sha256(raw).hexdigest() + '  ' + name + '\n'
                                         for name, raw in sorted(bundle.items())).encode()
        destination.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, raw in bundle.items(): archive.writestr('prodimt-quick-capture-update/' + name, raw)
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
