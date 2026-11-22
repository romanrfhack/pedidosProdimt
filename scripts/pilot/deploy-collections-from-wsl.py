#!/usr/bin/env python3
"""Verify, inventory and deploy one pinned collections package from WSL."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import uuid

SOURCE = 'REPLACE_SOURCE_COMMIT'
SERVER = 'root@194.238.26.70'
BASE_RELEASE = 'machine-workspace-0d64f5bd2aef'
ROOT = Path(__file__).resolve().parent
REMOTE = '/root/prodimt-cobranza-' + SOURCE[:12]
PACKAGE_FILES = frozenset(('PRODIMT_Instalar_Cobranza.py', 'prodimt-collections-payload.zip',
                           'PRODIMT_Desplegar_Cobranza_WSL.py', 'LEEME.txt'))


def run(args, capture=False):
    result = subprocess.run(args, check=False, capture_output=capture, text=capture)
    if result.returncode:
        if capture:
            try:
                report = json.loads(result.stdout)
                if isinstance(report, dict) and report.get('status') == 'failed':
                    return result.stdout
            except (ValueError, TypeError):
                pass
            print('La conexión remota no terminó correctamente.', file=sys.stderr)
        raise RuntimeError('remote_step_failed')
    return result.stdout if capture else None


def remote_command(arguments):
    return 'cd ' + shlex.quote(REMOTE) + ' && ' + shlex.join(
        ['python3', 'PRODIMT_Instalar_Cobranza.py', '--package', 'prodimt-collections-payload.zip', *arguments])


def read_report(output):
    value = json.loads(output)
    if not isinstance(value, dict):
        raise RuntimeError('invalid_inventory_report')
    return value


def verify_files():
    checksums = ROOT / 'SHA256SUMS.txt'
    if checksums.is_symlink() or not checksums.is_file():
        raise RuntimeError('invalid_checksums')
    names = set()
    for line in checksums.read_text().splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9_.-]+)', line)
        if match is None or match[2] in names or match[2] not in PACKAGE_FILES:
            raise RuntimeError('invalid_checksums')
        names.add(match[2])
        path = ROOT / match[2]
        if path.is_symlink() or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != match[1]:
            raise RuntimeError('file_checksum_mismatch')
    if names != PACKAGE_FILES:
        raise RuntimeError('incomplete_checksums')


def write_report(name, report):
    # Escritura atómica: no seguir enlaces locales al guardar el inventario o plan.
    path = ROOT / name
    if path.is_symlink():
        raise RuntimeError('report_symlink_not_allowed')
    temporary = ROOT / ('.collections-report-' + uuid.uuid4().hex)
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(descriptor, 'w') as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if re.fullmatch('[0-9a-f]{40}', SOURCE) is None:
        raise RuntimeError('wrapper_not_pinned')
    verify_files()
    run(['ssh', SERVER, 'install -d -m 0700 ' + shlex.quote(REMOTE)])
    run(['scp', str(ROOT / 'PRODIMT_Instalar_Cobranza.py'), str(ROOT / 'prodimt-collections-payload.zip'),
         SERVER + ':' + REMOTE + '/'])
    inventory = read_report(run(['ssh', SERVER, remote_command(['--inventory'])], capture=True))
    write_report('INVENTARIO_COBRANZA.json', inventory)
    if inventory.get('status') != 'inventoried':
        print(json.dumps(inventory, ensure_ascii=False, indent=2))
        raise RuntimeError('inventory_failed')
    expected_new = 'collections-' + SOURCE[:12]
    releases = inventory.get('releases', {})
    if not isinstance(releases, dict) or any(releases.get(part) not in (BASE_RELEASE, expected_new)
                                            for part in ('backend', 'frontend')):
        raise RuntimeError('base_release_unexpected')
    # La base no se deriva del inventario: el paquete sólo admite Máquina instalada.
    plan = read_report(run(['ssh', SERVER, remote_command(['--base-frontend', BASE_RELEASE])], capture=True))
    write_report('PLAN_COBRANZA.json', plan)
    print(json.dumps(plan, ensure_ascii=False, indent=2))
    if plan.get('status') not in ('planned', 'already_current'):
        raise RuntimeError('plan_failed')
    if args.apply and plan.get('status') != 'already_current':
        # La terminal conserva los prompts ocultos y la verificación de respaldo del instalador.
        run(['ssh', '-t', SERVER, remote_command(['--base-frontend', BASE_RELEASE, '--apply'])])


if __name__ == '__main__':
    os.umask(0o077)
    try:
        main()
    except (Exception, KeyboardInterrupt):
        print('El despliegue se detuvo. Revisa INVENTARIO_COBRANZA.json y PLAN_COBRANZA.json si se generaron. Repite el mismo paquete para recuperar una aplicación interrumpida.', file=sys.stderr)
        sys.exit(1)
