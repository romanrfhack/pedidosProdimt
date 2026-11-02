#!/usr/bin/env python3
"""Package the built I15 customer catalog frontend and pinned installer for I14."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def build(frontend, output, source):
    if not re.fullmatch(r'[0-9a-f]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')
    if not frontend.is_dir() or frontend.is_symlink():
        raise ValueError('frontend_build_directory_invalid')
    files = {}
    for path in sorted(frontend.rglob('*')):
        if path.is_symlink():
            raise ValueError('frontend_symlink_not_allowed')
        if path.is_file():
            name = path.relative_to(frontend).as_posix()
            if name.endswith('.map') or any(part.startswith('.') for part in Path(name).parts):
                raise ValueError('frontend_private_or_source_map_file')
            files['frontend/' + name] = path.read_bytes()
    if not {'frontend/index.html', 'frontend/3rdpartylicenses.txt'}.issubset(files) or not any(name.endswith('.js') for name in files):
        raise ValueError('frontend_build_missing')
    manifest = dict(feature='responsive-customers-v1', sourceCommit=source,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json', json.dumps(manifest, sort_keys=True))
        for name, raw in files.items():
            archive.writestr(name, raw)
    payload = stream.getvalue()
    installer = (ROOT / 'scripts/pilot/install-customer-catalog-layout-update.py').read_text()
    assert installer.count('REPLACE_SOURCE_COMMIT') == installer.count('REPLACE_PAYLOAD_SHA256') == 1
    installer = installer.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I15 — Catálogo de clientes responsivo

Actualización sólo frontend sobre I14 instalado y saludable.
Frontend requerido: reminder-message-768230826a3e.
Backend conservado: access-tokens-65560ca3cf98.

Clientes aprovecha el ancho con márgenes y una rejilla automática de tarjetas.
Una columna en móvil; el número aumenta según el ancho disponible, sin ocultar
nombre, ruta, teléfono, entrega, llave/cobro, estado o acciones. Conserva estados
y orden del servidor, descansos/no pedidos al final y modales de alta/edición.
Búsqueda por nombre/teléfono/ruta/código, limpiar búsqueda y filtros, y contador
visible de resultados. Los filtros reacomodan las tarjetas manteniendo su ancho.
No cambia reglas de pedidos, clientes, productos, perfiles, precios o tokens.
Conserva el catálogo de productos I13 y el mensaje de acceso I14.
Sin SQL directo, migraciones, cambios de backend/contraseñas/claves o reinicios.

Desde WSL, dentro de la carpeta extraída:
sha256sum -c SHA256SUMS.txt
scp PRODIMT_Instalar_I15.py prodimt-customer-catalog-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I15.py --package /root/prodimt-customer-catalog-payload.zip'

Lectura esperada: planned/completed, changes_made=false, base_release_verified=true,
health_verified=true. Si falla, no aplicar ni repetir instaladores anteriores.
Con cualquier guardado pendiente resuelto:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I15.py --package /root/prodimt-customer-catalog-payload.zip --apply'

Aplicación esperada: updated/completed, frontend_verified=true, health_verified=true.
Crea snapshot privado y conserva el frontend anterior. Activación atómica y reversión
del enlace ante fallo posterior. Repetir el mismo paquete verifica already_current.
El preflight rechaza frontends previos a I14 para conservar el mensaje vigente.

Tras éxito: Ctrl+F5 → Catálogos → Clientes. Cambiar el ancho de la ventana y revisar
las columnas; buscar/filtrar/limpiar, abrir Nuevo cliente/Editar y cancelar, comprobar
Configurar y conservación de Productos y Tokens de acceso. No crear pedidos o
cambiar datos reales sólo para probar el diseño. Instalación no implica aceptación
funcional del usuario ni prueba de operación real.
'''
    outer = {'PRODIMT_Instalar_I15.py': installer.encode(), 'prodimt-customer-catalog-payload.zip': payload,
             'LEEME.txt': readme.encode()}
    outer['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(outer.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in outer.items():
            archive.writestr('prodimt-customer-catalog-layout-update/' + name, raw)
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                files=len(files), bytes=output.stat().st_size)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frontend', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.frontend, args.output, args.source), indent=2))


if __name__ == '__main__':
    main()
