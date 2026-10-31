#!/usr/bin/env python3
"""Package reviewed I09 native binaries without a database migration."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
FEATURE = 'customer-access-links'
I07 = 'capture-46fed604732e'
I08_SOURCE = '42f6eb4c225a6781475893151a28d3e9b19fae2a'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_sources(source, previous_sources):
    if not re.fullmatch(r'[0-9a-f]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')
    for previous in previous_sources:
        if not re.fullmatch(r'[0-9a-f]{40}', previous):
            raise ValueError('previous_frontend_source_invalid')
        if subprocess.run(['git', 'merge-base', '--is-ancestor', previous, source], cwd=ROOT,
                          capture_output=True, check=False).returncode:
            raise ValueError('previous_frontend_source_not_ancestor')


def build(backend, frontend, output, source, previous_frontend_sources=()):
    previous = sorted(set((I08_SOURCE, *previous_frontend_sources)))
    validate_sources(source, previous)
    files = {name: (ROOT / 'scripts/pilot' / name).read_bytes()
             for name in ('inspect-native.py', 'install-password-update.py')}
    for part, directory in (('backend', backend), ('frontend', frontend)):
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('build_directory_invalid')
        for path in sorted(directory.rglob('*')):
            if path.is_symlink():
                raise ValueError('build_symlink_not_allowed')
            if not path.is_file():
                continue
            name = path.relative_to(directory).as_posix()
            if any(segment.startswith('.') for segment in Path(name).parts):
                raise ValueError('build_private_file_not_allowed')
            if path.suffix.lower() in ('.pfx', '.p12', '.pem', '.key', '.bak', '.env', '.cs', '.ts'):
                raise ValueError('build_secret_or_source_file_not_allowed')
            if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
                continue
            files[part + '/' + name] = path.read_bytes()
    required = {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                'backend/appsettings.json', 'frontend/index.html'}
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if b'apiBaseUrl:""' not in scripts or b'/api/customer-orders/' not in scripts or b'/capture' not in scripts:
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, deliveryCutoff='18:00', businessTimeZone='America/Mexico_City',
                    previousBackendRelease=I07,
                    previousFrontendReleases=[I07, *('client-config-' + item[:12] for item in previous)],
                    migrationsRequired=11,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json', json.dumps(manifest, sort_keys=True))
        for name, raw in sorted(files.items()):
            archive.writestr(name, raw)
    payload = stream.getvalue()
    installer = (ROOT / 'scripts/pilot/install-customer-access-update.py').read_text()
    if installer.count('REPLACE_SOURCE_COMMIT') != 1 or installer.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    installer = installer.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = (
        'PRODIMT I09 — Acceso de clientes y próximo pedido\n\n'
        'Actualiza backend y frontend nativos I07/I08. Conserva cuentas, configuración, catálogos y once migraciones.\n'
        'No migra ni restaura SQL, no cambia contraseñas y no envía mensajes automáticamente.\n'
        'Verifica SHA256SUMS.txt antes de copiar los dos archivos PRODIMT al VPS.\n'
        'scp PRODIMT_Instalar_I09.py prodimt-customer-access-payload.zip root@194.238.26.70:/root/\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I09.py --package /root/prodimt-customer-access-payload.zip'\n"
        'Sin --apply ejecuta sólo comprobaciones de lectura y muestra planned/completed.\n'
        "ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I09.py --package /root/prodimt-customer-access-payload.zip --apply'\n\n"
        'Resolver primero cualquier guardado o borrador incierto. Habrá un reinicio breve sólo de PRODIMT.\n'
        'El instalador valida paquete, proceso/configuración, esquema/permisos SQL y canales existentes.\n'
        'Guarda un snapshot privado y revierte los enlaces de aplicación si falla la activación. No restaura SQL.\n'
        'Resultado esperado: updated, phase completed, health_verified y frontend_verified true.\n'
        'Una repetición del mismo paquete verifica already_current sin nuevo reinicio.\n'
        'Tras éxito recarga e inicia sesión si se solicita; los enlaces de cliente existentes se conservan.\n'
        'Tokens de acceso: guarda teléfono, crea enlace y usa Abrir WhatsApp para preparar un mensaje individual.\n'
        'Antes de las 18:00 CDMX la entrega es hoy; desde las 18:00 es mañana. Revisa la fecha antes de confirmar.\n'
        'Comprobar captura rápida/manual con un cliente de prueba acordado; la instalación no registra pedidos.\n')
    bundle = {'PRODIMT_Instalar_I09.py': installer.encode(), 'prodimt-customer-access-payload.zip': payload,
              'LEEME.txt': readme.encode()}
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in bundle.items():
            archive.writestr('prodimt-customer-access-update/' + name, raw)
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                files=len(files), bytes=output.stat().st_size, previousFrontendReleases=manifest['previousFrontendReleases'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', type=Path, required=True)
    parser.add_argument('--frontend', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--previous-frontend-source', action='append', default=[])
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.output, args.source, args.previous_frontend_source), indent=2))


if __name__ == '__main__':
    main()
