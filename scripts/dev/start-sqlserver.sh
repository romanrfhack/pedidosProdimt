#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
env_file="$repo_root/infra/dev/.env"
compose_file="$repo_root/infra/dev/docker-compose.sqlserver.yml"

if [[ ! -f "$env_file" ]]; then
  echo "Missing infra/dev/.env. Copy infra/dev/.env.example to infra/dev/.env and set PRODIMT_SQL_SA_PASSWORD." >&2
  exit 1
fi

set -a
source "$env_file"
set +a

if [[ -z "${PRODIMT_SQL_SA_PASSWORD:-}" ]]; then
  echo "Missing PRODIMT_SQL_SA_PASSWORD in infra/dev/.env." >&2
  exit 1
fi

if [[ "$PRODIMT_SQL_SA_PASSWORD" == "REPLACE_WITH_STRONG_LOCAL_PASSWORD" ]]; then
  echo "Replace PRODIMT_SQL_SA_PASSWORD in infra/dev/.env with a local strong password." >&2
  exit 1
fi

docker compose --env-file "$env_file" -f "$compose_file" up -d
echo "SQL Server container requested. Port: ${PRODIMT_SQL_PORT:-1433}. Container: prodimt-pedidos-sqlserver."
