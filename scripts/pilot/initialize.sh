#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
command -v python3 >/dev/null
docker info >/dev/null
docker compose version >/dev/null

if [[ -f "$pilot_env_file" ]]; then
  require_pilot_config
  printf 'Existing configuration preserved for project %s.\n' "$pilot_project"
else
  pilot_project="${PRODIMT_PROJECT:-prodimt-pilot}"
  pilot_http_port="${PRODIMT_HTTP_PORT:-5098}"
  if docker volume inspect "${pilot_project}_sql-data" >/dev/null 2>&1; then
    printf 'SQL data already exists but private configuration is missing. Restore .env; no new secrets or database will be created.\n' >&2
    exit 1
  fi
  python3 "$pilot_script_dir/config.py" create "$pilot_env_file" "$pilot_project" "$pilot_http_port"
  require_pilot_config
fi
printf 'Next: bash scripts/pilot/preflight.sh && bash scripts/pilot/start.sh\n'
