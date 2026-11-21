#!/usr/bin/env python3
"""Build the pinned backend/frontend and SQL 20→21 machine-workspace update."""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PREFIX = 'prodimt-machine-workspace-update/'
MIGRATION = '20261117145405_AddMachineWorkspace'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def helper(name):
    spec = importlib.util.spec_from_file_location('build_' + name.replace('-', '_'), ROOT / 'scripts/pilot' / name)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def archive(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as output:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            output.writestr(info, raw)
    return stream.getvalue()


def validate_source(source):
    if not re.fullmatch('[0-9a-f]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')


def build(backend, frontend, migration, output, source, base_backend, base_frontend):
    validate_source(source)
    installer = helper('install-machine-workspace-update.py')
    for release in (base_backend, base_frontend):
        if release is None:
            continue
        if not re.fullmatch('[A-Za-z0-9][A-Za-z0-9._-]{0,119}', release):
            raise ValueError('base_release_invalid')
    before = sorted(path.stem for path in (ROOT / 'src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations')
                    .glob('*.cs') if not path.stem.endswith('.Designer') and re.fullmatch(r'\d{14}_[A-Za-z0-9_]+', path.stem)
                    and path.stem != MIGRATION)
    if len(before) != 20 or before[-1] != '20261108143413_AddDeliveryTripDepartureAndDayDrivers':
        raise ValueError('base_migration_history_unexpected')
    files = {
        'inspect-native.py': (ROOT / 'scripts/pilot/inspect-native.py').read_bytes(),
        'native-deployment-support.py': (ROOT / 'scripts/pilot/install-quick-capture-update.py').read_bytes(),
        'frontend-asset-support.py': (ROOT / 'scripts/pilot/install-delivery-flow-update.py').read_bytes(),
        'migration.sql': migration.read_bytes(),
    }
    installer.validate_migration(files['migration.sql'].decode('utf-8-sig'))
    for part, directory in (('backend', backend), ('frontend', frontend)):
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('build_directory_invalid')
        for path in sorted(directory.rglob('*')):
            if path.is_symlink():
                raise ValueError('build_symlink_not_allowed')
            if not path.is_file():
                continue
            name = path.relative_to(directory).as_posix()
            if name.endswith(('.pdb', '.map')) or name == 'appsettings.Development.json':
                continue
            if not installer.safe_name(name) or name.endswith(('.env', '.pem', '.key', '.pfx', '.log')):
                raise ValueError('build_private_file')
            if path.stat().st_size > 40 * 1024 * 1024:
                raise ValueError('build_file_too_large')
            files[part + '/' + name] = path.read_bytes()
    notices = frontend.parent / '3rdpartylicenses.txt'
    if 'frontend/3rdpartylicenses.txt' not in files and frontend.name == 'browser' and notices.is_file() and not notices.is_symlink():
        files['frontend/3rdpartylicenses.txt'] = notices.read_bytes()
    if any('backend/' + name not in files for name in (*installer.ASSEMBLIES,
            'Prodimt.Pedidos.Api.runtimeconfig.json', 'Prodimt.Pedidos.Api.deps.json')):
        raise ValueError('backend_build_missing')
    for name, raw in files.items():
        if name.startswith('backend/appsettings') and name.endswith('.json'):
            value = json.loads(raw)
            flattened = helper('inspect-native.py').flatten_json(value)
            if any((key.endswith(('password', 'signingkey', 'encryptionkey', 'connectionstrings:pedidos'))
                    or 'tokenprotection' in key and key.endswith(':key'))
                   and text and not str(text).startswith('development-only-') for key, text in flattened.items()):
                raise ValueError('package_configuration_contains_secret')
    helper('install-delivery-flow-update.py').validate_asset_closure(
        {name[9:]: raw for name, raw in files.items() if name.startswith('frontend/')})
    if len(files) > 1200 or sum(map(len, files.values())) > 250 * 1024 * 1024:
        raise ValueError('package_too_large')
    manifest = dict(feature='machine-workspace-v1', sourceCommit=source, migration=MIGRATION,
                    migrationsBefore=before, migrationsAfter=before + [MIGRATION],
                    basePair=[base_backend, base_frontend],
                    files={name: dict(bytes=len(raw), sha256=sha(raw)) for name, raw in files.items()})
    payload = archive({'manifest.json': json.dumps(manifest, ensure_ascii=False, sort_keys=True).encode(), **files})
    pinned = (ROOT / 'scripts/pilot/install-machine-workspace-update.py').read_text()
    if pinned.count('REPLACE_SOURCE_COMMIT') != 1 or pinned.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    pinned = pinned.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', sha(payload))
    frontend_arg = ('--base-frontend ' + base_frontend) if base_frontend else '--base-frontend NOMBRE_OBTENIDO_EN_INVENTARIO'
    wrapper = (ROOT / 'scripts/pilot/deploy-machine-workspace-from-wsl.py').read_text()
    if wrapper.count('REPLACE_SOURCE_COMMIT') != 1:
        raise ValueError('wrapper_template_invalid')
    wrapper = wrapper.replace('REPLACE_SOURCE_COMMIT', source)
    readme = f'''PRODIMT · Responsable de Máquina
Fuente {source}
Base esperada · backend {base_backend} / frontend {base_frontend or 'indicar nombre exacto tras inventario'}
Migración incremental · 20→21 {MIGRATION}

Desde WSL, en la carpeta extraída, ejecuta el despliegue autorizado.
python3 PRODIMT_Desplegar_Maquina_WSL.py --apply

El script comprueba SHA256, copia a una ruta nueva, guarda INVENTARIO_MAQUINA.json,
obtiene el frontend real, valida el plan y abre SSH con terminal para introducir
las credenciales SQL existentes. Sin --apply prepara inventario y plan.
Para ejecutar los pasos manualmente, sigue los comandos siguientes.
sha256sum -c SHA256SUMS.txt

Copia esta carpeta a una ruta nueva del VPS.
ssh root@194.238.26.70 'install -d -m 0700 /root/prodimt-maquina-{source[:12]}'
scp PRODIMT_Instalar_Maquina.py prodimt-machine-workspace-payload.zip root@194.238.26.70:/root/prodimt-maquina-{source[:12]}/

Obtén el inventario sin modificar releases o SQL.
ssh root@194.238.26.70 'cd /root/prodimt-maquina-{source[:12]} && python3 PRODIMT_Instalar_Maquina.py --package prodimt-machine-workspace-payload.zip --inventory'

Confirma que releases y migraciones corresponden a la base, después ejecuta el plan.
ssh root@194.238.26.70 'cd /root/prodimt-maquina-{source[:12]} && python3 PRODIMT_Instalar_Maquina.py --package prodimt-machine-workspace-payload.zip {frontend_arg}'

Aplica con terminal para introducir las credenciales SQL existentes en prompts ocultos.
ssh -t root@194.238.26.70 'cd /root/prodimt-maquina-{source[:12]} && python3 PRODIMT_Instalar_Maquina.py --package prodimt-machine-workspace-payload.zip {frontend_arg} --apply'

El éxito emite updated/completed, migrations_after=21 y recovery_pending=false.
La instalación conserva appsettings y api.env. Antes del DDL genera COPY_ONLY con CHECKSUM y RESTORE VERIFYONLY.
Una interrupción después del DDL se recupera ejecutando el mismo paquete. El marcador privado conserva la fase y la base.
Las operaciones abiertas se deben guardar antes del reinicio de PRODIMT.
'''
    outer = {'PRODIMT_Instalar_Maquina.py': pinned.encode(), 'prodimt-machine-workspace-payload.zip': payload,
             'LEEME.txt': readme.encode(), 'PRODIMT_Desplegar_Maquina_WSL.py': wrapper.encode()}
    outer['SHA256SUMS.txt'] = ''.join(f'{sha(raw)}  {name}\n' for name, raw in sorted(outer.items())).encode()
    validate_source(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive({PREFIX + name: raw for name, raw in outer.items()}))
    return dict(sourceCommit=source, sha256=sha(output.read_bytes()), bytes=output.stat().st_size,
                payloadSha256=sha(payload), migrationsBefore=20, migrationsAfter=21,
                basePair=[base_backend, base_frontend])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--base-backend', default='delivery-auto-89ed72778f30')
    parser.add_argument('--base-frontend')
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source,
                           args.base_backend, args.base_frontend), indent=2))


if __name__ == '__main__':
    main()
