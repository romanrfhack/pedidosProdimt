#!/usr/bin/env python3
"""Package the already-built I10 token-modal frontend and pinned native installer."""
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
    files={}
    for path in sorted(frontend.rglob('*')):
        if path.is_symlink():raise ValueError('frontend_symlink_not_allowed')
        if path.is_file():
            name=path.relative_to(frontend).as_posix()
            if name.endswith('.map') or any(part.startswith('.') for part in Path(name).parts):
                raise ValueError('frontend_private_or_source_map_file')
            files['frontend/'+name]=path.read_bytes()
    if 'frontend/index.html' not in files or not any(name.endswith('.js') for name in files):
        raise ValueError('frontend_build_missing')
    manifest=dict(feature='customer-token-modal-v1',sourceCommit=source,
                  files={name:dict(bytes=len(raw),sha256=digest(raw)) for name,raw in files.items()})
    stream=io.BytesIO()
    with zipfile.ZipFile(stream,'w',zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest,sort_keys=True))
        for name,raw in files.items():archive.writestr(name,raw)
    payload=stream.getvalue()
    installer=(ROOT/'scripts/pilot/install-token-modal-update.py').read_text()
    assert installer.count('REPLACE_SOURCE_COMMIT')==installer.count('REPLACE_PAYLOAD_SHA256')==1
    installer=installer.replace('REPLACE_SOURCE_COMMIT',source).replace('REPLACE_PAYLOAD_SHA256',digest(payload))
    readme=('PRODIMT I10 — Tokens de acceso en modal amplio\n\n'
        'Actualización sólo del frontend nativo I09. Sin SQL, migraciones, contraseñas o reinicio.\n'
        'Requiere backend/frontend customer-access-6cfdc7ff366a y conserva sus funciones y datos.\n'
        'Extraer este ZIP y verificar SHA256SUMS.txt antes de copiar los dos archivos al VPS.\n'
        'sha256sum -c SHA256SUMS.txt\n'
        'scp PRODIMT_Instalar_I10.py prodimt-token-modal-payload.zip root@194.238.26.70:/root/\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I10.py --package /root/prodimt-token-modal-payload.zip'\n"
        'Sin --apply ejecuta sólo comprobaciones de lectura y muestra planned/completed.\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I10.py --package /root/prodimt-token-modal-payload.zip --apply'\n\n"
        'Resultado esperado: updated, phase completed, frontend_verified y health_verified true.\n'
        'Conserva backend, base, perfiles, tokens, pedidos y reparaciones de canales existentes.\n'
        'Ante fallo posterior a activación, restaura sólo el enlace del frontend anterior.\n'
        'Repetir el mismo paquete verifica already_current sin volver a activar o reiniciar.\n'
        'Guardar o resolver cualquier borrador abierto antes de recargar el navegador.\n'
        'Tras éxito recargar con Ctrl+F5; abrir Configuración de cliente → Tokens de acceso.\n'
        'El modal se adapta a PC, tableta y teléfono; mantiene creación, copia y WhatsApp individuales.\n')
    outer={'PRODIMT_Instalar_I10.py':installer.encode(),'prodimt-token-modal-payload.zip':payload,'LEEME.txt':readme.encode()}
    outer['SHA256SUMS.txt']=''.join(f'{digest(raw)}  {name}\n' for name,raw in sorted(outer.items())).encode()
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,raw in outer.items():archive.writestr('prodimt-token-modal-update/'+name,raw)
    return dict(sourceCommit=source,sha256=digest(output.read_bytes()),payloadSha256=digest(payload),files=len(files),bytes=output.stat().st_size)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frontend',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--source',required=True)
    args=parser.parse_args()
    print(json.dumps(build(args.frontend,args.output,args.source),indent=2))


if __name__=='__main__':main()
