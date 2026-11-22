#!/usr/bin/env python3
"""Build the pinned backend/frontend and SQL 21→22 collections update."""
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
PREFIX = 'prodimt-collections-update/'
FEATURE = 'delivery-collections-v1'
BASE_RELEASE = 'machine-workspace-0d64f5bd2aef'
MIGRATION = '20261122201338_AddDeliveryCollections'


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
    if subprocess.check_output(['git', 'rev-parse', 'refs/heads/main'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_main')


# Los recursos compilados deben conservar cobranza y el módulo de Máquina instalado.
FRONTEND_MARKERS = (b'/api/admin/collections', b'/cashier', b'/collection-preview', b'collectedAmount',
                    b'collectionVersion', b'operations.collections.receive', b'/machine-workspace')


def collect_runtime(files, part, directory, installer):
    if not directory.is_dir() or directory.is_symlink():
        raise ValueError('build_directory_invalid')
    settings = {name: (ROOT / 'src/Prodimt.Pedidos.Api' / name).read_bytes()
                for name in ('appsettings.json', 'appsettings.Production.json')}
    for path in sorted(directory.rglob('*')):
        if path.is_symlink():
            raise ValueError('build_symlink_not_allowed')
        if not path.is_file():
            continue
        name = path.relative_to(directory).as_posix()
        suffix = path.suffix.lower()
        if suffix in ('.pdb', '.map') or name == 'appsettings.Development.json':
            continue
        if not installer.safe_name(name) or suffix in (
                '.env', '.pem', '.key', '.pfx', '.p12', '.log', '.bak', '.cs', '.ts',
                '.xlsx', '.xlsm', '.csv', '.dmp', '.trace', '.zip', '.sql'):
            raise ValueError('build_private_or_source_file_not_allowed')
        if path.stat().st_size > 40 * 1024 * 1024:
            raise ValueError('build_file_too_large')
        raw = path.read_bytes()
        if path.name.lower().startswith('appsettings') and suffix == '.json':
            if part != 'backend' or name not in settings or raw != settings[name]:
                raise ValueError('runtime_settings_not_source_template')
        files[part + '/' + name] = raw


def build(backend, frontend, migration, output, source, base_backend=BASE_RELEASE, base_frontend=BASE_RELEASE):
    validate_source(source)
    installer = helper('install-collections-update.py')
    if (base_backend, base_frontend) != (BASE_RELEASE, BASE_RELEASE):
        raise ValueError('base_release_unexpected')
    before = sorted(path.stem for path in (ROOT / 'src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations')
                    .glob('*.cs') if not path.stem.endswith('.Designer') and re.fullmatch(r'\d{14}_[A-Za-z0-9_]+', path.stem)
                    and path.stem != MIGRATION)
    if tuple(before) != installer.BASE_MIGRATIONS:
        raise ValueError('base_migration_history_unexpected')
    files = {
        'inspect-native.py': (ROOT / 'scripts/pilot/inspect-native.py').read_bytes(),
        'native-deployment-support.py': (ROOT / 'scripts/pilot/install-quick-capture-update.py').read_bytes(),
        'frontend-asset-support.py': (ROOT / 'scripts/pilot/install-delivery-flow-update.py').read_bytes(),
        'migration.sql': migration.read_bytes(),
    }
    if set(installer.SUPPORT) != {'inspect-native.py', 'native-deployment-support.py', 'frontend-asset-support.py'}:
        raise ValueError('installer_support_contract_mismatch')
    if installer.MIGRATION != MIGRATION:
        raise ValueError('installer_migration_contract_mismatch')
    if installer.BASE_RELEASE != BASE_RELEASE:
        raise ValueError('installer_base_contract_mismatch')
    installer.validate_migration(files['migration.sql'].decode('utf-8-sig'))
    for part, directory in (('backend', backend), ('frontend', frontend)):
        collect_runtime(files, part, directory, installer)
    notices = frontend.parent / '3rdpartylicenses.txt'
    if 'frontend/3rdpartylicenses.txt' not in files and frontend.name == 'browser' and notices.is_file() and not notices.is_symlink():
        files['frontend/3rdpartylicenses.txt'] = notices.read_bytes()
    if any(not files.get('backend/' + name) for name in (*installer.ASSEMBLIES,
            'appsettings.json', 'appsettings.Production.json',
            'Prodimt.Pedidos.Api.runtimeconfig.json', 'Prodimt.Pedidos.Api.deps.json')):
        raise ValueError('backend_build_missing')
    assets = {name[9:]: raw for name, raw in files.items() if name.startswith('frontend/')}
    helper('install-delivery-flow-update.py').validate_asset_closure(assets)
    scripts = b'\n'.join(raw for name, raw in assets.items() if name.endswith('.js'))
    if not all(marker in scripts for marker in FRONTEND_MARKERS):
        raise ValueError('frontend_feature_missing')
    if len(files) > 1200 or sum(map(len, files.values())) > 250 * 1024 * 1024:
        raise ValueError('package_too_large')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION,
                    migrationsBefore=before, migrationsAfter=before + [MIGRATION],
                    basePair=[base_backend, base_frontend],
                    files={name: dict(bytes=len(raw), sha256=sha(raw)) for name, raw in files.items()})
    payload = archive({'manifest.json': json.dumps(manifest, ensure_ascii=False, sort_keys=True).encode(), **files})
    pinned = (ROOT / 'scripts/pilot/install-collections-update.py').read_text()
    if pinned.count('REPLACE_SOURCE_COMMIT') != 1 or pinned.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    pinned = pinned.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', sha(payload))
    frontend_arg = '--base-frontend ' + base_frontend
    wrapper = (ROOT / 'scripts/pilot/deploy-collections-from-wsl.py').read_text()
    if wrapper.count('REPLACE_SOURCE_COMMIT') != 1:
        raise ValueError('wrapper_template_invalid')
    wrapper = wrapper.replace('REPLACE_SOURCE_COMMIT', source)
    readme = f'''PRODIMT · Cobranza de reparto y Mostrador
Fuente {source}
Base esperada · backend {base_backend} / frontend {base_frontend}
Migración incremental · 21→22 {MIGRATION}

Desde WSL, en la carpeta extraída, ejecuta el despliegue autorizado.
Si descargaste en Windows, ubica el ZIP en /mnt/c/Users/TUF/Downloads.
Puedes extraer desde Windows Downloads con estos comandos de WSL:
python3 -m zipfile -e /mnt/c/Users/TUF/Downloads/PRODIMT_Cobranza_{source[:12]}.zip ~/prodimt-cobranza-{source[:12]}
cd ~/prodimt-cobranza-{source[:12]}/prodimt-collections-update
No ejecutes el instalador histórico de Máquina para aplicar este incremento.
python3 PRODIMT_Desplegar_Cobranza_WSL.py --apply

El script comprueba SHA256, copia a una ruta nueva, guarda INVENTARIO_COBRANZA.json,
comprueba las dos bases exactas, valida el plan y abre SSH con terminal para introducir
las credenciales SQL existentes. Sin --apply prepara inventario y plan.
Para ejecutar los pasos manualmente, sigue los comandos siguientes.
sha256sum -c SHA256SUMS.txt

Copia esta carpeta a una ruta nueva del VPS.
ssh root@194.238.26.70 'install -d -m 0700 /root/prodimt-cobranza-{source[:12]}'
scp PRODIMT_Instalar_Cobranza.py prodimt-collections-payload.zip root@194.238.26.70:/root/prodimt-cobranza-{source[:12]}/

Obtén el inventario sin modificar releases o SQL.
ssh root@194.238.26.70 'cd /root/prodimt-cobranza-{source[:12]} && python3 PRODIMT_Instalar_Cobranza.py --package prodimt-collections-payload.zip --inventory'

Confirma que releases y migraciones corresponden a la base, después ejecuta el plan.
ssh root@194.238.26.70 'cd /root/prodimt-cobranza-{source[:12]} && python3 PRODIMT_Instalar_Cobranza.py --package prodimt-collections-payload.zip {frontend_arg}'

Aplica con terminal para introducir las credenciales SQL existentes en prompts ocultos.
ssh -t root@194.238.26.70 'cd /root/prodimt-cobranza-{source[:12]} && python3 PRODIMT_Instalar_Cobranza.py --package prodimt-collections-payload.zip {frontend_arg} --apply'

El éxito emite updated/completed, migrations_after=22 y recovery_pending=false.
La instalación conserva appsettings y api.env. Antes del DDL genera COPY_ONLY con CHECKSUM y RESTORE VERIFYONLY.
Una interrupción después del DDL se recupera ejecutando el mismo paquete. El marcador privado conserva la fase y la base.
Las operaciones abiertas se deben guardar antes del reinicio de PRODIMT.
'''
    outer = {'PRODIMT_Instalar_Cobranza.py': pinned.encode(), 'prodimt-collections-payload.zip': payload,
             'LEEME.txt': readme.encode(), 'PRODIMT_Desplegar_Cobranza_WSL.py': wrapper.encode()}
    outer['SHA256SUMS.txt'] = ''.join(f'{sha(raw)}  {name}\n' for name, raw in sorted(outer.items())).encode()
    validate_source(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive({PREFIX + name: raw for name, raw in outer.items()}))
    return dict(sourceCommit=source, sha256=sha(output.read_bytes()), bytes=output.stat().st_size,
                payloadSha256=sha(payload), migrationsBefore=21, migrationsAfter=22,
                basePair=[base_backend, base_frontend])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--base-backend', default=BASE_RELEASE)
    parser.add_argument('--base-frontend', default=BASE_RELEASE)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source,
                           args.base_backend, args.base_frontend), indent=2))


if __name__ == '__main__':
    main()
