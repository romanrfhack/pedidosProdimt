#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_pilot_config
bash "$pilot_script_dir/preflight.sh"
# Preserve the running image before replacing its tag. Database migrations are not reversed.
pilot_current_container="$(pilot_compose ps --all -q app)"
if [[ -n "$pilot_current_container" ]]; then
  pilot_current_image="$(docker inspect --format '{{.Image}}' "$pilot_current_container")"
  docker image tag "$pilot_current_image" "$pilot_project-app:previous"
fi
# Build before starting SQL to avoid competing with the compiler for memory.
pilot_compose build app
pilot_compose up -d --wait --wait-timeout 180 sqlserver
pilot_compose stop app
# Existing databases are backed up before an explicit schema/bootstrap update.
pilot_database_exists="$(printf "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'ProdimtPilot') IS NULL THEN 0 ELSE 1 END;\n" | pilot_sql)"
if [[ "$pilot_database_exists" =~ 1 ]]; then
  bash "$pilot_script_dir/backup.sh"
fi
# Password SQL is carried over stdin, never shell arguments or stdout.
python3 "$pilot_script_dir/config.py" provision-sql "$pilot_env_file" | pilot_sql >/dev/null
pilot_compose run --rm --no-deps bootstrap
pilot_compose up -d --no-deps app
for attempt in $(seq 1 60); do
  if curl --fail --silent --max-time 3 "http://127.0.0.1:$pilot_http_port/health/db" >/dev/null; then
    printf 'Pilot ready: http://127.0.0.1:%s/admin/login\n' "$pilot_http_port"
    printf 'Private access: %s/ACCESO-PRIVADO.txt\n' "$pilot_config_dir"
    exit 0
  fi
  sleep 2
done
printf 'Application health did not become ready. Inspect docker compose logs without publishing secrets.\n' >&2
exit 1
