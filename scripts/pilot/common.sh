#!/usr/bin/env bash
set -euo pipefail
pilot_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
pilot_repo_dir="$(cd -- "$pilot_script_dir/../.." && pwd)"
pilot_config_dir="${PRODIMT_PILOT_CONFIG_DIR:-$pilot_repo_dir/infra/pilot}"
pilot_env_file="$pilot_config_dir/.env"

require_pilot_config() {
  [[ -f "$pilot_env_file" ]] || { printf 'Run bash scripts/pilot/initialize.sh first.\n' >&2; return 1; }
  python3 "$pilot_script_dir/config.py" check "$pilot_env_file" >/dev/null
  pilot_project="$(python3 "$pilot_script_dir/config.py" get "$pilot_env_file" PRODIMT_PROJECT)"
  pilot_http_port="$(python3 "$pilot_script_dir/config.py" get "$pilot_env_file" PRODIMT_HTTP_PORT)"
}

pilot_compose() {
  docker compose --project-name "$pilot_project" --env-file "$pilot_env_file" -f "$pilot_repo_dir/infra/pilot/compose.yml" "$@"
}

pilot_sql() {
  pilot_compose exec -T sqlserver bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -r1 -i /dev/stdin'
}
