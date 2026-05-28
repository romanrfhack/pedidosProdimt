#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" != "--confirm" ]]; then
  echo "This deletes the local development database. Re-run with: bash scripts/dev/reset-database.sh --confirm" >&2
  exit 1
fi

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

if [[ "$PRODIMT_SQL_DATABASE" != "ProdimtPedidos" && "${PRODIMT_ALLOW_DATABASE_RESET:-}" != "local-dev" ]]; then
  echo "Refusing to reset database '$PRODIMT_SQL_DATABASE'. Set PRODIMT_ALLOW_DATABASE_RESET=local-dev only for local development." >&2
  exit 1
fi

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
elif [[ "${PRODIMT_ALLOW_DATABASE_RESET:-}" != "local-dev" ]]; then
  echo "ConnectionStrings__Pedidos is already set. Refusing reset unless PRODIMT_ALLOW_DATABASE_RESET=local-dev." >&2
  exit 1
fi

export ASPNETCORE_ENVIRONMENT=Development
export Persistence__Provider=SqlServer
export DevelopmentSeed__Enabled=true
export DevelopmentSeed__ApplyMigrations=false

cd "$repo_root"

dotnet tool run dotnet-ef database drop --force --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
dotnet tool run dotnet-ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api

log_file="$(mktemp)"
echo "Starting API once to apply development seed..."
dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls "$PRODIMT_API_URL" >"$log_file" 2>&1 &
api_pid=$!

cleanup() {
  if kill -0 "$api_pid" >/dev/null 2>&1; then
    kill "$api_pid" >/dev/null 2>&1 || true
    wait "$api_pid" >/dev/null 2>&1 || true
  fi
  rm -f "$log_file"
}
trap cleanup EXIT

for _ in {1..60}; do
  if curl -fsS "$PRODIMT_API_URL/health/db" >/dev/null 2>&1; then
    echo "Database reset and development seed completed."
    exit 0
  fi

  if ! kill -0 "$api_pid" >/dev/null 2>&1; then
    echo "API stopped before seed validation completed. Last API log lines:" >&2
    tail -n 80 "$log_file" >&2 || true
    exit 1
  fi

  sleep 1
done

echo "Timed out waiting for API seed validation. Last API log lines:" >&2
tail -n 80 "$log_file" >&2 || true
exit 1
