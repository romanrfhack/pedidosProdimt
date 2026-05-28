#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ $# -ne 1 ]]; then
  echo "Usage: bash scripts/dev/validate-import-folder.sh <csv-folder>" >&2
  exit 1
fi

node "$repo_root/scripts/dev/import-folder.mjs" validate "$1"
