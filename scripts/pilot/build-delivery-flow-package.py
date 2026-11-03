#!/usr/bin/env python3
"""Package RF1 reparto integration as a frontend-only update on exact I22."""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT=Path(__file__).resolve().parents[2]
PREFIX='prodimt-delivery-flow-update/'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_source(source):
    if not re.fullmatch(r'[0-9a-f]{40}',source):raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()!=source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip():
        raise ValueError('source_tree_not_clean')


def archive_bytes(files):
    stream=io.BytesIO()
    with zipfile.ZipFile(stream,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in sorted(files.items()):
            info=zipfile.ZipInfo(name,date_time=(1980,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
            archive.writestr(info,raw)
    return stream.getvalue()


def installer_support():
    spec=importlib.util.spec_from_file_location('rf1_asset_support',ROOT/'scripts/pilot/install-delivery-flow-update.py')
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result


def build(frontend,output,source):
    validate_source(source)
    if not frontend.is_dir() or frontend.is_symlink():raise ValueError('frontend_build_directory_invalid')
    files={}
    for path in sorted(frontend.rglob('*')):
        if path.is_symlink():raise ValueError('frontend_symlink_not_allowed')
        if path.is_file():
            name=path.relative_to(frontend).as_posix()
            if name.endswith('.map') or ':' in name or '\\' in name or any(part.startswith('.') for part in path.relative_to(frontend).parts):
                raise ValueError('frontend_private_or_source_map_file')
            files[name]=path.read_bytes()
    # Angular's application builder emits notices beside browser/, while only
    # browser/ belongs in the deployed web root. Include the notices in memory.
    if '3rdpartylicenses.txt' not in files and frontend.name=='browser':
        notices=frontend.parent/'3rdpartylicenses.txt'
        if notices.is_symlink():raise ValueError('frontend_symlink_not_allowed')
        if notices.is_file():files['3rdpartylicenses.txt']=notices.read_bytes()
    if not 0<len(files)<200 or sum(map(len,files.values()))>40*1024*1024 or any(len(raw)>10*1024*1024 for raw in files.values()):
        raise ValueError('frontend_build_too_large')
    support=installer_support();support.validate_asset_closure(files)
    manifest=dict(feature='delivery-flow-v1',sourceCommit=source,baseRelease=support.OLD,
                  backendRelease=support.BACKEND_RELEASE,
                  files={'frontend/'+name:dict(bytes=len(raw),sha256=digest(raw)) for name,raw in files.items()})
    payload=archive_bytes({'manifest.json':json.dumps(manifest,sort_keys=True).encode(),
                           **{'frontend/'+name:raw for name,raw in files.items()}})
    if len(payload)>20*1024*1024:raise ValueError('frontend_build_too_large')
    installer=(ROOT/'scripts/pilot/install-delivery-flow-update.py').read_text()
    assert installer.count('REPLACE_SOURCE_COMMIT')==installer.count('REPLACE_PAYLOAD_SHA256')==1
    installer=installer.replace('REPLACE_SOURCE_COMMIT',source).replace('REPLACE_PAYLOAD_SHA256',digest(payload))
    readme=f'''PRODIMT Reparto RF1 — Acceso y continuidad del flujo

Fuente: {source}
Actualización sólo del frontend sobre backend/frontend delivery-0cfe3ef511d8.
Conserva API, permisos, usuarios, configuración, claves, pedidos y registros de reparto.
No ejecuta consultas SQL, migraciones ni reinicios. Comprueba la identidad exacta
del I22 instalado y salud; no vuelve a consultar ni contar las 17 migraciones.

No aplicar después de otra actualización, incluidos Horarios u otro frontend.
El instalador rechaza releases diferentes; requiere integrar esos cambios primero.
Este parche no concede permisos ni asigna roles o viajes automáticamente.

Desde WSL, dentro de la carpeta extraída:
sha256sum -c SHA256SUMS.txt
scp PRODIMT_Instalar_Reparto_RF1.py prodimt-delivery-flow-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_Reparto_RF1.py'

Lectura esperada: planned/completed, changes_made=false, health_verified=true,
base_release_verified=true. Si falla, conservar la salida y no forzar otro release.
Con los guardados pendientes resueltos, aplicar la misma entrega:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_Reparto_RF1.py --apply'

Éxito: updated/completed, frontend_verified=true, health_verified=true.
Verifica los hashes locales y HTTPS del índice y todos los módulos/estilos, guarda
snapshot privado y activa un enlace atómico. Ante fallo restaura el enlace anterior.
Repetir comprueba already_current; no sustituye una actualización distinta.

Tras éxito: Ctrl+F5. Revisar acceso Reparto, guía de inicio, panel Pedidos y lista
de preparación en Gestión del reparto, según los permisos actuales de la cuenta.
La instalación no acredita aceptación funcional ni modifica datos para probar.
'''
    outer={'PRODIMT_Instalar_Reparto_RF1.py':installer.encode(),'prodimt-delivery-flow-payload.zip':payload,
           'LEEME.txt':readme.encode()}
    outer['SHA256SUMS.txt']=''.join(f'{digest(raw)}  {name}\n' for name,raw in sorted(outer.items())).encode()
    validate_source(source)
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX+name:raw for name,raw in outer.items()}))
    return dict(sourceCommit=source,sha256=digest(output.read_bytes()),payloadSha256=digest(payload),
                files=len(files),bytes=output.stat().st_size)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frontend',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--source',required=True)
    args=parser.parse_args()
    print(json.dumps(build(args.frontend,args.output,args.source),indent=2))


if __name__=='__main__':main()
