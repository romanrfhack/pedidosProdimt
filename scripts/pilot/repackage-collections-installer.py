#!/usr/bin/env python3
"""Revise only the collections installer while preserving the delivered CCC payload."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import stat
import subprocess
import types
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ORIGINAL_SOURCE = 'ccc4193301043ec1935fb575d516319598fe94d0'
ORIGINAL_PACKAGE_SHA256 = '848a41ded5510a0d5d63abf36f620c5bf37c65b6f69fe43d324fab295519d175'
ORIGINAL_PAYLOAD_SHA256 = 'bbd4d3c91702ecc7249d896cf9ed1d12d687ef28e461d2caf09a55c49f7224e3'
PREFIX = 'prodimt-collections-update/'
INSTALLER = 'PRODIMT_Instalar_Cobranza.py'
WRAPPER = 'PRODIMT_Desplegar_Cobranza_WSL.py'
PAYLOAD = 'prodimt-collections-payload.zip'
CHECKED_FILES = frozenset((INSTALLER, WRAPPER, PAYLOAD, 'LEEME.txt'))
SUPPORT_PATHS = {
    'inspect-native.py': 'scripts/pilot/inspect-native.py',
    'native-deployment-support.py': 'scripts/pilot/install-quick-capture-update.py',
    'frontend-asset-support.py': 'scripts/pilot/install-delivery-flow-update.py',
}
REVISION_FILES = frozenset(('scripts/pilot/install-collections-update.py',
                            'scripts/pilot/repackage-collections-installer.py',
                            'AGENTS.md', 'CONTINUAR.md', 'README.md'))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=ROOT)


def allowed_revision_path(name):
    # Una revisión del instalador no autoriza reutilizar binarios tras cambiar la aplicación.
    if not name or name.startswith('/') or '\\' in name or ':' in name \
            or any(part in ('', '.', '..') for part in name.split('/')):
        return False
    return name in REVISION_FILES or name.startswith(('tests/', 'docs/', '.github/'))


def validate_source(source):
    if not re.fullmatch('[0-9a-f]{40}', source) or source == ORIGINAL_SOURCE:
        raise ValueError('installer_revision_source_invalid')
    if git('rev-parse', 'HEAD').decode().strip() != source:
        raise ValueError('source_commit_not_current')
    if git('status', '--porcelain').strip():
        raise ValueError('source_tree_not_clean')
    if git('rev-parse', 'refs/heads/main').decode().strip() != source:
        raise ValueError('source_commit_not_main')
    if git('merge-base', ORIGINAL_SOURCE, source).decode().strip() != ORIGINAL_SOURCE:
        raise ValueError('installer_revision_not_descendant')
    changed = git('diff', '--name-only', '--no-renames', '-z', ORIGINAL_SOURCE, source).decode().split('\0')
    if any(name and not allowed_revision_path(name) for name in changed):
        raise ValueError('runtime_source_changed_rebuild_required')
    if 'scripts/pilot/install-collections-update.py' not in changed:
        raise ValueError('installer_revision_missing')


def archive(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as output:
        for name, raw in sorted(files.items()):
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            output.writestr(entry, raw)
    return stream.getvalue()


def read_outer(raw):
    if sha(raw) != ORIGINAL_PACKAGE_SHA256:
        raise ValueError('original_package_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as package:
        entries = package.infolist()
        expected = {PREFIX + name for name in CHECKED_FILES | {'SHA256SUMS.txt'}}
        if len(entries) != len(expected) or {row.filename for row in entries} != expected \
                or any(row.is_dir() or row.flag_bits & 1 or stat.S_IFMT(row.external_attr >> 16)
                       not in (0, stat.S_IFREG) for row in entries) \
                or sum(row.file_size for row in entries) > 100 * 1024 * 1024:
            raise ValueError('original_package_layout_invalid')
        files = {name: package.read(PREFIX + name) for name in CHECKED_FILES | {'SHA256SUMS.txt'}}
    seen = set()
    for line in files['SHA256SUMS.txt'].decode('utf-8').splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9_.-]+)', line)
        if match is None or match[2] not in CHECKED_FILES or match[2] in seen:
            raise ValueError('original_checksums_invalid')
        seen.add(match[2])
        if sha(files[match[2]]) != match[1]:
            raise ValueError('original_file_hash_mismatch')
    if seen != CHECKED_FILES:
        raise ValueError('original_checksums_incomplete')
    if sha(files[PAYLOAD]) != ORIGINAL_PAYLOAD_SHA256:
        raise ValueError('original_payload_hash_mismatch')
    return files


def pin_installer(template):
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    return template.replace('REPLACE_SOURCE_COMMIT', ORIGINAL_SOURCE).replace(
        'REPLACE_PAYLOAD_SHA256', ORIGINAL_PAYLOAD_SHA256).encode('utf-8')


def validated_payload(installer_raw, payload):
    # El ZIP exterior y el payload ya están fijados por hashes independientes.
    # Este adaptador permite al loader validar bytes sin exigir identidad de archivo de VPS.
    loader = types.ModuleType('collections_installer_revision_validation')
    exec(compile(installer_raw, '<collections-installer-revision>', 'exec'), loader.__dict__)
    if loader.EXPECTED_SOURCE != ORIGINAL_SOURCE or loader.EXPECTED_PAYLOAD_SHA256 != ORIGINAL_PAYLOAD_SHA256:
        raise ValueError('installer_application_pin_changed')
    loader.regular = lambda path, limit=100 * 1024 * 1024: payload
    files, manifest = loader.load_payload(None)
    if manifest.get('sourceCommit') != ORIGINAL_SOURCE:
        raise ValueError('payload_application_source_changed')
    return files, manifest


def validate_original(files):
    payload_files, manifest = validated_payload(files[INSTALLER], files[PAYLOAD])
    original_installer = git('show', ORIGINAL_SOURCE + ':scripts/pilot/install-collections-update.py').decode('utf-8')
    if files[INSTALLER] != pin_installer(original_installer):
        raise ValueError('original_installer_source_mismatch')
    original_wrapper = git('show', ORIGINAL_SOURCE + ':scripts/pilot/deploy-collections-from-wsl.py').decode('utf-8')
    if original_wrapper.count('REPLACE_SOURCE_COMMIT') != 1 or files[WRAPPER] != original_wrapper.replace(
            'REPLACE_SOURCE_COMMIT', ORIGINAL_SOURCE).encode('utf-8'):
        raise ValueError('original_wrapper_source_mismatch')
    for name, path in SUPPORT_PATHS.items():
        if payload_files[name] != git('show', ORIGINAL_SOURCE + ':' + path):
            raise ValueError('payload_support_source_mismatch')
    for name in ('appsettings.json', 'appsettings.Production.json'):
        if payload_files['backend/' + name] != git('show', ORIGINAL_SOURCE + ':src/Prodimt.Pedidos.Api/' + name):
            raise ValueError('payload_configuration_source_mismatch')
    return manifest


def repackage(original_package, output, source):
    validate_source(source)
    if original_package.is_symlink() or not original_package.is_file() or output.is_symlink() \
            or original_package.resolve() == output.resolve():
        raise ValueError('package_path_invalid')
    files = read_outer(original_package.read_bytes())
    manifest = validate_original(files)
    installer_template = (ROOT / 'scripts/pilot/install-collections-update.py').read_text(encoding='utf-8')
    revised = pin_installer(installer_template)
    # El instalador corregido también debe aceptar el mismo manifiesto y cierre de recursos.
    validated_payload(revised, files[PAYLOAD])
    files[INSTALLER] = revised
    files['LEEME.txt'] = f'''PRODIMT · Cobranza de reparto y Mostrador · Instalador revisión 2
Fuente de aplicación y binarios: {ORIGINAL_SOURCE}
Fuente de la revisión del instalador: {source}
SHA256 del instalador: {sha(revised)}
SHA256 del payload conservado sin cambios: {ORIGINAL_PAYLOAD_SHA256}
Paquete original verificado: {ORIGINAL_PACKAGE_SHA256}
Base requerida: {manifest['basePair'][0]} en backend y frontend, 21 migraciones.

Este paquete corrige la inspección de preservación del instalador.
No recompila ni cambia la aplicación, los recursos frontend o la migración 21→22.
El identificador de release sigue siendo collections-{ORIGINAL_SOURCE[:12]}.

Desde WSL puedes extraer el ZIP descargado en Windows a una carpeta NUEVA:
python3 -m zipfile -e /mnt/c/Users/TUF/Downloads/{output.name} ~/prodimt-cobranza-{ORIGINAL_SOURCE[:12]}-v2
cd ~/prodimt-cobranza-{ORIGINAL_SOURCE[:12]}-v2/prodimt-collections-update
python3 PRODIMT_Desplegar_Cobranza_WSL.py --apply

El wrapper verifica SHA256, guarda INVENTARIO_COBRANZA.json y PLAN_COBRANZA.json,
y conserva la terminal para los prompts ocultos del administrador SQL existente.
Sin --apply sólo copia, inventaría y valida el plan; no modifica releases ni SQL.
No cambies credenciales, destino SQL ni uses el instalador histórico de Máquina.
Antes del DDL exige respaldo COPY_ONLY con CHECKSUM y RESTORE VERIFYONLY.
Si se interrumpe después del DDL, recupera ejecutando exactamente este paquete.
El éxito requiere updated/completed, migrations_after=22 y recovery_pending=false.
'''.encode('utf-8')
    files['SHA256SUMS.txt'] = ''.join(f'{sha(files[name])}  {name}\n' for name in sorted(CHECKED_FILES)).encode('utf-8')
    validate_source(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive({PREFIX + name: raw for name, raw in files.items()}))
    return dict(sourceCommit=ORIGINAL_SOURCE, installerRevision=source, installerSha256=sha(revised),
                sha256=sha(output.read_bytes()), bytes=output.stat().st_size,
                payloadSha256=sha(files[PAYLOAD]), payloadUnchanged=True, runtimeRecompiled=False,
                originalPackageSha256=ORIGINAL_PACKAGE_SHA256,
                migrationsBefore=21, migrationsAfter=22, basePair=manifest['basePair'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original-package', type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(repackage(args.original_package, args.output, args.source), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
