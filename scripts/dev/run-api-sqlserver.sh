#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
env_file="$repo_root/infra/dev/.env"

if [[ -f "$env_file" ]]; then
  set -a
  source "$env_file"
  set +a
fi

PRODIMT_SQL_HOST="${PRODIMT_SQL_HOST:-localhost}"
PRODIMT_SQL_PORT="${PRODIMT_SQL_PORT:-1433}"
PRODIMT_SQL_DATABASE="${PRODIMT_SQL_DATABASE:-ProdimtPedidos}"
PRODIMT_API_URL="${PRODIMT_API_URL:-http://127.0.0.1:5088}"

if [[ -z "${ConnectionStrings__Pedidos:-}" ]]; then
  if [[ -z "${PRODIMT_SQL_SA_PASSWORD:-}" ]]; then
    echo "Missing SQL configuration. Set ConnectionStrings__Pedidos or create infra/dev/.env with PRODIMT_SQL_SA_PASSWORD." >&2
    exit 1
  fi

  if [[ "$PRODIMT_SQL_SA_PASSWORD" == "REPLACE_WITH_STRONG_LOCAL_PASSWORD" ]]; then
    echo "Replace PRODIMT_SQL_SA_PASSWORD in infra/dev/.env with a local strong password." >&2
    exit 1
  fi

  export ConnectionStrings__Pedidos="Server=${PRODIMT_SQL_HOST},${PRODIMT_SQL_PORT};Database=${PRODIMT_SQL_DATABASE};User Id=sa;Password=${PRODIMT_SQL_SA_PASSWORD};TrustServerCertificate=True"
fi

export ASPNETCORE_ENVIRONMENT=Development
export Persistence__Provider=SqlServer
export DevelopmentSeed__Enabled="${DevelopmentSeed__Enabled:-true}"
export DevelopmentSeed__ApplyMigrations="${DevelopmentSeed__ApplyMigrations:-true}"

cd "$repo_root"
dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls "$PRODIMT_API_URL"
