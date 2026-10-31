#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${PRODIMT_SQL_TEST_CONNECTION:-}" ]]; then
  printf 'Set PRODIMT_SQL_TEST_CONNECTION to an isolated SQL Server test instance.\n' >&2
  printf 'The login must create/drop databases. Only generated ProdimtI02Test_<guid> databases are changed.\n' >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$script_dir/../.." && pwd)"
dotnet test "$repo_dir/tests/Prodimt.Pedidos.SqlServer.Tests/Prodimt.Pedidos.SqlServer.Tests.csproj" \
  --configuration Release -m:1 -nr:false -p:UseSharedCompilation=false
