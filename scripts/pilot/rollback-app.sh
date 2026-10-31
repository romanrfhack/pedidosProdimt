#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_pilot_config
pilot_previous_image="$pilot_project-app:previous"
docker image inspect "$pilot_previous_image" >/dev/null
# Roll back only the app; retain the SQL container, volume and current schema.
export PRODIMT_APP_IMAGE="$pilot_previous_image"
pilot_compose up -d --no-deps --no-build app
for attempt in $(seq 1 60); do
  if curl --fail --silent --max-time 3 "http://127.0.0.1:$pilot_http_port/health/db" >/dev/null; then
    printf 'Previous application image is healthy. Data and schema were retained.\n'
    exit 0
  fi
  sleep 2
done
printf 'Previous image did not become healthy. Check schema compatibility with the backup before any data restore.\n' >&2
exit 1
