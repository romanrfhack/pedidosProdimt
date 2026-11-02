#!/usr/bin/env python3
"""Package the already-built I13 grouped-products frontend and pinned native installer."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT=Path(__file__).resolve().parents[2]


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def build(frontend,output,source):
    if not re.fullmatch(r'[0-9a-f]{40}',source):raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()!=source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip():
        raise ValueError('source_tree_not_clean')
    if not frontend.is_dir() or frontend.is_symlink():raise ValueError('frontend_build_directory_invalid')
    files={}
    for path in sorted(frontend.rglob('*')):
        if path.is_symlink():raise ValueError('frontend_symlink_not_allowed')
        if path.is_file():
            name=path.relative_to(frontend).as_posix()
            if name.endswith('.map') or any(part.startswith('.') for part in Path(name).parts):
                raise ValueError('frontend_private_or_source_map_file')
            files['frontend/'+name]=path.read_bytes()
    if not {'frontend/index.html','frontend/3rdpartylicenses.txt'}.issubset(files) or not any(name.endswith('.js') for name in files):
        raise ValueError('frontend_build_missing')
    manifest=dict(feature='grouped-products-v1',sourceCommit=source,
                  files={name:dict(bytes=len(raw),sha256=digest(raw)) for name,raw in files.items()})
    stream=io.BytesIO()
    with zipfile.ZipFile(stream,'w',zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest,sort_keys=True))
        for name,raw in files.items():archive.writestr(name,raw)
    payload=stream.getvalue()
    installer=(ROOT/'scripts/pilot/install-product-catalog-update.py').read_text()
    assert installer.count('REPLACE_SOURCE_COMMIT')==installer.count('REPLACE_PAYLOAD_SHA256')==1
    installer=installer.replace('REPLACE_SOURCE_COMMIT',source).replace('REPLACE_PAYLOAD_SHA256',digest(payload))
    readme=('PRODIMT I13 — Catálogo de productos por tipo\n\n'
        'Actualización del frontend: tarjetas, grupos, búsqueda y editor modal.\n'
        'Requiere I12 instalado y saludable: backend/frontend access-tokens-65560ca3cf98.\n'
        'Si I12 sigue pendiente, completar su instalación con la revisión 2 antes de I13.\n'
        'El preflight rechaza I11 sin cambiar nada; no ejecutar instaladores antiguos después de I13.\n'
        'Sin consultas SQL, migraciones, contraseñas, claves, cambios de backend o reinicios.\n'
        'Verificar SHA256SUMS.txt y copiar sólo el instalador y el payload al VPS.\n'
        'sha256sum -c SHA256SUMS.txt\n'
        'scp PRODIMT_Instalar_I13.py prodimt-product-catalog-payload.zip root@194.238.26.70:/root/\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I13.py --package /root/prodimt-product-catalog-payload.zip'\n"
        'Primero sólo lectura: resultado planned/completed, base_release_verified y health_verified true.\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I13.py --package /root/prodimt-product-catalog-payload.zip --apply'\n\n"
        'Resultado: updated/completed, frontend_verified y health_verified true.\n'
        'Conserva pedidos, perfiles, posiciones de captura, precios e historial y accesos I12.\n'
        'Crea snapshot privado y conserva el frontend anterior. Ante fallo tras activar, revierte su enlace.\n'
        'Repetir este mismo paquete devuelve already_current tras verificarlo.\n'
        'Resolver cualquier guardado pendiente antes de recargar. Tras éxito: Ctrl+F5.\n'
        'Abrir Catálogos → Productos: Por número, Harina, Sancochados, Vapor y Otros productos.\n'
        'Los grupos usan el nombre explícito: no cambian datos ni deducen ingredientes de Burrera o Flauta.\n'
        'Buscar por nombre, descripción, tipo o unidad; filtrar estado o tipo.\n'
        'Nuevo producto/Editar abren un modal. El precio sigue en la configuración de cada cliente.\n'
        'Archivados conservan equivalencias e historial y no se pueden reactivar.\n')
    outer={'PRODIMT_Instalar_I13.py':installer.encode(),'prodimt-product-catalog-payload.zip':payload,'LEEME.txt':readme.encode()}
    outer['SHA256SUMS.txt']=''.join(f'{digest(raw)}  {name}\n' for name,raw in sorted(outer.items())).encode()
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in outer.items():archive.writestr('prodimt-product-catalog-update/'+name,raw)
    return dict(sourceCommit=source,sha256=digest(output.read_bytes()),payloadSha256=digest(payload),files=len(files),bytes=output.stat().st_size)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frontend',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--source',required=True)
    args=parser.parse_args()
    print(json.dumps(build(args.frontend,args.output,args.source),indent=2))


if __name__=='__main__':main()
