#!/usr/bin/env python3
"""Verify, inventory and deploy one pinned Responsable de Máquina package from WSL."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shlex
import subprocess
import sys

SOURCE = 'REPLACE_SOURCE_COMMIT'
SERVER = 'root@194.238.26.70'
ROOT = Path(__file__).resolve().parent
REMOTE = '/root/prodimt-maquina-' + SOURCE[:12]


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
        ['python3', 'PRODIMT_Instalar_Maquina.py', '--package', 'prodimt-machine-workspace-payload.zip', *arguments])


def read_report(output):
    value = json.loads(output)
    if not isinstance(value, dict):
        raise RuntimeError('invalid_inventory_report')
    return value


def verify_files():
    for line in (ROOT / 'SHA256SUMS.txt').read_text().splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9_.-]+)', line)
        if match is None:
            raise RuntimeError('invalid_checksums')
        path = ROOT / match[2]
        if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != match[1]:
            raise RuntimeError('file_checksum_mismatch')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if re.fullmatch('[0-9a-f]{40}', SOURCE) is None:
        raise RuntimeError('wrapper_not_pinned')
    verify_files()
    run(['ssh', SERVER, 'install -d -m 0700 ' + shlex.quote(REMOTE)])
    run(['scp', str(ROOT / 'PRODIMT_Instalar_Maquina.py'), str(ROOT / 'prodimt-machine-workspace-payload.zip'),
         SERVER + ':' + REMOTE + '/'])
    inventory = read_report(run(['ssh', SERVER, remote_command(['--inventory'])], capture=True))
    (ROOT / 'INVENTARIO_MAQUINA.json').write_text(json.dumps(inventory, ensure_ascii=False, indent=2))
    if inventory.get('status') != 'inventoried':
        print(json.dumps(inventory, ensure_ascii=False, indent=2))
        raise RuntimeError('inventory_failed')
    front = inventory.get('releases', {}).get('frontend')
    if not isinstance(front, str) or re.fullmatch('[A-Za-z0-9][A-Za-z0-9._-]{0,119}', front) is None:
        raise RuntimeError('invalid_frontend_release')
    plan = read_report(run(['ssh', SERVER, remote_command(['--base-frontend', front])], capture=True))
    (ROOT / 'PLAN_MAQUINA.json').write_text(json.dumps(plan, ensure_ascii=False, indent=2))
    print(json.dumps(plan, ensure_ascii=False, indent=2))
    if plan.get('status') not in ('planned', 'already_current'):
        raise RuntimeError('plan_failed')
    if args.apply and plan.get('status') != 'already_current':
        run(['ssh', '-t', SERVER, remote_command(['--base-frontend', front, '--apply'])])


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt):
        print('El despliegue se detuvo. Revisa INVENTARIO_MAQUINA.json y PLAN_MAQUINA.json si se generaron. Repite el mismo paquete para recuperar una aplicación interrumpida.', file=sys.stderr)
        sys.exit(1)
