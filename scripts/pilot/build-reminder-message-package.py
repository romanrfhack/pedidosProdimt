#!/usr/bin/env python3
"""Package the built I14 reminder frontend and pinned native installer for I13."""
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
    manifest = dict(feature='reminder-message-template-v1', sourceCommit=source,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json', json.dumps(manifest, sort_keys=True))
        for name, raw in files.items():
            archive.writestr(name, raw)
    payload = stream.getvalue()
    installer = (ROOT / 'scripts/pilot/install-reminder-message-update.py').read_text()
    assert installer.count('REPLACE_SOURCE_COMMIT') == installer.count('REPLACE_PAYLOAD_SHA256') == 1
    installer = installer.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I14 — Plantilla simplificada del recordatorio

Actualización sólo del frontend sobre I13 instalado y saludable.
Frontend requerido: product-catalog-2345c7f4c27f.
Backend conservado: access-tokens-65560ca3cf98.

El mensaje conserva saludo, descripción editable, enlace y vencimiento CDMX.
Quita la entrega prevista y la leyenda del corte de las 18:00 del mensaje.
Agrega «Haz tu pedido aquí:» seguido de la URL completa en otra línea.
Vista previa, Copiar mensaje, copia manual y WhatsApp usan la misma plantilla.
La URL conserva /cliente#token= y el token de cada acceso; no crea un acortador.
WhatsApp sigue mostrando la URL: no se simulan botones ni enlaces Markdown.
La fecha de entrega se sigue mostrando y validando dentro del portal del cliente.
Sin SQL directo, migraciones, revocaciones, claves, contraseñas ni reinicios.
No modifica pedidos, perfiles, productos, agrupación I13, precios o historial.
Abrir WhatsApp sólo prepara el mensaje; la persona operadora confirma el envío.

Desde WSL, dentro de la carpeta extraída:
sha256sum -c SHA256SUMS.txt
scp PRODIMT_Instalar_I14.py prodimt-reminder-message-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I14.py --package /root/prodimt-reminder-message-payload.zip'

La comprobación de lectura debe terminar planned/completed, changes_made=false,
base_release_verified=true y health_verified=true. Si falla, no aplicar.
Después, con cualquier guardado pendiente ya resuelto:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I14.py --package /root/prodimt-reminder-message-payload.zip --apply'

Resultado esperado: updated/completed, frontend_verified=true y health_verified=true.
Crea snapshot privado, conserva el release anterior y activa por enlace atómico.
Si falla después de activar, intenta restaurar sólo el enlace del frontend anterior.
Repetir este mismo paquete devuelve already_current tras verificarlo.
El instalador rechaza I11/I12 u otros frontends para conservar I13.
No ejecutar instaladores anteriores después de I14.

Tras éxito: Ctrl+F5. Configuración de cliente → Tokens de acceso.
Editar un acceso activo y revisar vista previa; Copiar mensaje y Abrir WhatsApp.
Comprobar saludo, descripción, texto «Haz tu pedido aquí:», URL y vencimiento.
Comprobar que no aparecen las dos leyendas retiradas y que el mismo enlace abre
el portal del cliente con fecha de entrega visible. No crear pedidos de prueba.
Revisar también Catálogos → Productos para confirmar que conserva I13.
'''
    outer = {'PRODIMT_Instalar_I14.py': installer.encode(), 'prodimt-reminder-message-payload.zip': payload,
             'LEEME.txt': readme.encode()}
    outer['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(outer.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in outer.items():
            archive.writestr('prodimt-reminder-message-update/' + name, raw)
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
