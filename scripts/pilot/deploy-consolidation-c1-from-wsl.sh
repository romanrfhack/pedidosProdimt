#!/usr/bin/env bash
# Validates the exact bundled source before invoking the independently pinned installer.
set -euo pipefail
umask 077

usage() {
  cat <<'TXT'
Uso: bash PRODIMT_Desplegar_C1_WSL.sh [--validate-only | --prepare-only | --resume-from-transfer DIRECTORIO_RESULTADOS]

Sin opciones: navegador local completo y, si pasa, preflight/SQL/respaldo/despliegue.
--validate-only: navegador local y pruebas SQL aisladas del VPS; sin despliegue.
--prepare-only: sólo navegador local; no conecta al VPS.
--resume-from-transfer: reutiliza únicamente el gate completo del MISMO paquete;
  verifica reporte, fuente y hashes antes de repetir SCP y los controles del VPS.
Requiere WSL/Linux, git, python3, curl, tar, sha256sum, ssh y scp.
Node 24 se utiliza si existe; de lo contrario se descarga una copia local verificada.
Chromium requiere sus bibliotecas del sistema instaladas. No se ejecuta sudo.
El destino predeterminado es root@194.238.26.70.
Puede definirse PRODIMT_SSH_TARGET=usuario@servidor antes de ejecutar.
TXT
}

mode=apply
resume_results=''
case "${1:-}" in
  '') ;;
  --validate-only) mode=validate ;;
  --prepare-only) mode=prepare ;;
  --resume-from-transfer)
    [[ $# == 2 ]] || { usage >&2; exit 2; }
    resume_results=$(cd -- "$2" && pwd -P) ;;
  --help|-h) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac
if (( $# > 1 )) && [[ -z "$resume_results" ]]; then usage >&2; exit 2; fi
for tool in git python3 curl tar sha256sum ssh scp cmp; do
  command -v "$tool" >/dev/null || { printf 'Falta la herramienta: %s\n' "$tool" >&2; exit 2; }
done
package_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
cd -- "$package_dir"
sha256sum --quiet -c SHA256SUMS.txt
source_commit=$(python3 - <<'PY'
import json,re
data=json.load(open('source.json',encoding='utf-8'))
source=data.get('sourceCommit','')
if not re.fullmatch('[a-f0-9]{40}',source):
    raise SystemExit('source.json no contiene un commit válido')
print(source)
PY
)
ssh_target=${PRODIMT_SSH_TARGET:-root@194.238.26.70}
if [[ ! "$ssh_target" =~ ^[a-zA-Z0-9_][a-zA-Z0-9_.-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$ ]]; then
  printf 'PRODIMT_SSH_TARGET no es usuario@servidor válido.\n' >&2
  exit 2
fi
if [[ -n "$resume_results" ]]; then
  [[ "$resume_results" == "$package_dir/resultados-c1/"* ]] || {
    printf 'Los resultados deben pertenecer a este paquete.\n' >&2; exit 2;
  }
  results=$resume_results
  cmp --silent SHA256SUMS.txt "$results/package-SHA256SUMS.txt" || {
    printf 'El paquete difiere del validado en esos resultados.\n' >&2; exit 2;
  }
  gate="$results/prodimt-c1-browser-gate.json"
else
work=$(mktemp -d "${TMPDIR:-/tmp}/prodimt-c1-XXXXXXXX")
results="$package_dir/resultados-c1/$(date -u +%Y%m%dT%H%M%SZ)-$$"
mkdir -p -- "$results"
cp -- SHA256SUMS.txt "$results/package-SHA256SUMS.txt"
gate="$results/prodimt-c1-browser-gate.json"
printf 'Fuentes temporales: %s\nResultados: %s\n' "$work" "$results"
git clone --quiet --branch codex/consolidacion-c1 "$package_dir/PRODIMT_C1.bundle" "$work/fuente"
[[ $(git -C "$work/fuente" rev-parse HEAD) == "$source_commit" ]] || {
  printf 'El bundle no corresponde al paquete.\n' >&2; exit 2;
}

# Use the supported Node major without changing the user's shell or global packages.
if ! command -v node >/dev/null || [[ $(node -p 'process.versions.node.split(".")[0]') != 24 ]]; then
  case $(uname -m) in
    x86_64) node_arch=x64; node_sha=6c172a9ab6718f7b801fb5a7bd486af2ee5e32758086a04b84fcce2aa77095e6 ;;
    aarch64|arm64) node_arch=arm64; node_sha=dbabf7a1cbf947e6059ffad2c27b80041db97dea1e553b62afdee7f13fca336e ;;
    *) printf 'Arquitectura de Node no soportada.\n' >&2; exit 2 ;;
  esac
  node_archive="node-v24.22.1-linux-$node_arch.tar.xz"
  curl --fail --location --connect-timeout 20 --max-time 300 \
    "https://nodejs.org/dist/v24.22.1/$node_archive" -o "$work/$node_archive"
  printf '%s  %s\n' "$node_sha" "$work/$node_archive" | sha256sum --check --status
  tar -xJf "$work/$node_archive" -C "$work"
  export PATH="$work/${node_archive%.tar.xz}/bin:$PATH"
fi

printf 'Ejecutando navegador sobre el commit %s.\n' "$source_commit"
BUILD_C1_BROWSER_GATE_JSON="$gate" \
  bash "$work/fuente/tests/e2e/run-c1-browser-gate.sh" --source "$source_commit" \
  2>&1 | tee "$results/navegador.log"
fi
# Do not trust only the child exit status: bind its successful evidence to this source.
python3 - "$gate" "$source_commit" "$results/playwright.json" <<'PY'
import hashlib,json,sys
from pathlib import Path
with open(sys.argv[1],encoding='utf-8') as stream:
    gate=json.load(stream)
expected={'schema':'prodimt.c1.browser-gate.v1','status':'passed',
          'sourceCommit':sys.argv[2],'api':'mock','passed':240,'failed':0,
          'skipped':0,'flaky':0,'collected':240,'suite':'all','expectedCases':240,
          'exitCode':0,'sqlServerIncluded':False,'humanAcceptance':False}
if any(type(gate.get(key)) is not type(value) or gate[key]!=value for key,value in expected.items()):
    raise SystemExit('El gate de navegador no cumple la validación completa de esta fuente.')
raw=Path(sys.argv[3]).read_bytes()
report=json.loads(raw)
def count(suites):
    return sum(sum(len(spec.get('tests',[])) for spec in suite.get('specs',[]))
               + count(suite.get('suites',[])) for suite in suites)
if (gate.get('reportSha256')!=hashlib.sha256(raw).hexdigest() or count(report.get('suites',[]))!=240
    or any(type(report.get('stats',{}).get(key)) is not int or report['stats'][key]!=value
           for key,value in {'expected':240,'unexpected':0,'skipped':0,'flaky':0}.items())
    or report.get('errors',[])):
    raise SystemExit('El reporte completo no corresponde al gate de este paquete.')
print('Navegador completo verificado; evidencia ligada a la fuente del paquete.')
PY
if [[ "$mode" == prepare ]]; then
  printf 'Validación local terminada. No se conectó al VPS.\n'
  exit 0
fi
printf 'Copiando el paquete validado a %s.\n' "$ssh_target"
scp "$package_dir/PRODIMT_Instalar_C1.py" \
  "$package_dir/prodimt-consolidation-c1-payload.zip" "$gate" "$ssh_target:/root/"
if [[ "$mode" == validate ]]; then
  ssh -t "$ssh_target" \
    'python3 /root/PRODIMT_Instalar_C1.py && python3 /root/PRODIMT_Instalar_C1.py --validate-only' \
    2>&1 | tee "$results/servidor-validacion.log"
else
  ssh -t "$ssh_target" \
    'python3 /root/PRODIMT_Instalar_C1.py && python3 /root/PRODIMT_Instalar_C1.py --apply --browser-gate /root/prodimt-c1-browser-gate.json' \
    2>&1 | tee "$results/servidor-despliegue.log"
fi
printf 'Ejecución terminada; revisa el JSON final. Resultados: %s\n' "$results"
