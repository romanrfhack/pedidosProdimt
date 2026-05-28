#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ $# -ne 2 || "$2" != "--confirm" ]]; then
  echo "Usage: bash scripts/dev/apply-import-folder.sh <csv-folder> --confirm" >&2
  exit 1
fi

node "$repo_root/scripts/dev/import-folder.mjs" apply "$1" --confirm
