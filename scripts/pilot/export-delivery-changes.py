#!/usr/bin/env python3
"""Export I22 source as a self-contained Git bundle and a reviewable I21 patch."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

import importlib.util

ROOT = Path(__file__).resolve().parents[2]
BASE = '496eb7094c05e3269f941ca6f87af6a5a88e4c98'
PREFIX = 'prodimt-delivery-changes/'


def helper():
    spec = importlib.util.spec_from_file_location('i22_export_package_support', ROOT / 'scripts/pilot/build-delivery-package.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def export(output, source, base=BASE):
    package = helper()
    package.validate_source(source)
    if not re.fullmatch(r'[a-f0-9]{40}', base):
        raise ValueError('source_base_invalid')
    subprocess.run(['git', 'merge-base', '--is-ancestor', base, source], cwd=ROOT, check=True)
    branch = subprocess.check_output(['git', 'symbolic-ref', '-q', 'HEAD'], cwd=ROOT, text=True).strip()
    if not branch.startswith('refs/heads/codex/'):
        raise ValueError('source_branch_unexpected')
    with tempfile.TemporaryDirectory(prefix='prodimt-i22-source-') as folder:
        bundle = Path(folder) / 'PRODIMT_I22.bundle'
        # Full reachable history: no missing I16/I18/I19/I21 prerequisites when
        # recovering on another computer. This does not publish to any remote.
        subprocess.run(['git', 'bundle', 'create', str(bundle), branch], cwd=ROOT, check=True)
        heads = subprocess.check_output(['git', 'bundle', 'list-heads', str(bundle)], cwd=ROOT, text=True).splitlines()
        if heads != [source + ' ' + branch]:
            raise ValueError('source_bundle_revision_changed')
        patch = subprocess.check_output(['git', 'diff', '--binary', '--full-index', '--no-ext-diff', base, source], cwd=ROOT)
        package.validate_source(source)
        readme = f'''PRODIMT I22 — Fuente y cambios revisables

Fuente: {source}
Base del parche I21: {base}
Rama: {branch.removeprefix('refs/heads/')}

El bundle conserva la historia completa alcanzable y no requiere descargas ni
prerrequisitos de versiones anteriores. No publica archivos en GitHub.
Verificar SHA256SUMS.txt antes de usar.

Crear un checkout independiente para revisión:
git clone --branch {branch.removeprefix('refs/heads/')} PRODIMT_I22.bundle pedidosProdimt-I22

PRODIMT_I22.patch permite revisar la diferencia desde I21; incluye los arreglos
posteriores del instalador I18 conservados en la integración. No aplicar sobre
una base diferente ni sobre una carpeta con cambios locales sin revisarlos.
Este ZIP contiene fuente, no instaladores listos para ejecutar en producción.
Usar el paquete PRODIMT_Reparto_I22.zip para la actualización nativa revisada.
'''
        files = {'PRODIMT_I22.bundle': bundle.read_bytes(), 'PRODIMT_I22.patch': patch,
                 'LEEME.txt': readme.encode(),
                 'source.json': json.dumps(dict(sourceCommit=source, patchBase=base, branch=branch,
                     selfContainedBundle=True, published=False), sort_keys=True, indent=2).encode()}
        files['SHA256SUMS.txt'] = ''.join(f'{package.digest(raw)}  {name}\n' for name, raw in sorted(files.items())).encode()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(package.archive_bytes({PREFIX + name: raw for name, raw in files.items()}))
    return dict(sourceCommit=source, baseCommit=base, bytes=output.stat().st_size,
                sha256=package.digest(output.read_bytes()), selfContainedBundle=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--base', default=BASE)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(export(args.output, args.source, args.base), indent=2))


if __name__ == '__main__':
    main()
