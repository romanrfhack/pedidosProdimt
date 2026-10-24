#!/usr/bin/env bash
set -euo pipefail
# This is the only script allowed to remove a volume: it creates a random,
# disposable CI project itself and never reads an operator's private .env.
pilot_smoke_dir="$(mktemp -d -t prodimt-smoke-XXXXXXXX)"
export PRODIMT_PILOT_CONFIG_DIR="$pilot_smoke_dir"
export PRODIMT_PROJECT="prodimt-smoke-$(python3 -c 'import secrets; print(secrets.token_hex(6))')"
export PRODIMT_HTTP_PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
pilot_project="$PRODIMT_PROJECT"
cleanup() {
  local result=$?
  trap - EXIT INT TERM
  if [[ "$pilot_project" == prodimt-smoke-* && "$pilot_config_dir" == "$pilot_smoke_dir" && -f "$pilot_env_file" ]]; then
    if [[ "$result" -ne 0 ]]; then
      pilot_compose ps -a || true
      # No docker inspect/config or raw application logs: keep generated secrets private.
    fi
    pilot_compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  fi
  rm -rf -- "$pilot_smoke_dir"
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
bash "$pilot_script_dir/initialize.sh"
require_pilot_config
pilot_original_env_hash="$(sha256sum "$pilot_env_file" | cut -d' ' -f1)"
bash "$pilot_script_dir/initialize.sh"
[[ "$(sha256sum "$pilot_env_file" | cut -d' ' -f1)" == "$pilot_original_env_hash" ]]
bash "$pilot_script_dir/start.sh"
python3 "$pilot_script_dir/smoke-http.py" "$pilot_env_file" initial
if PRODIMT_PILOT_CONFIG_DIR="$pilot_smoke_dir/missing" bash "$pilot_script_dir/initialize.sh"; then
  printf 'Missing configuration with an existing volume must not generate new secrets.\n' >&2
  exit 1
fi
[[ ! -e "$pilot_smoke_dir/missing/.env" ]]
pilot_app_id="$(pilot_compose ps -q app)"
pilot_sql_id="$(pilot_compose ps -q sqlserver)"
python3 - "$pilot_app_id" "$pilot_sql_id" <<'PY'
import json,subprocess,sys
def ports(container):
    return json.loads(subprocess.check_output(['docker','inspect','--format','{{json .HostConfig.PortBindings}}',container]))
assert ports(sys.argv[2]) in ({},None), 'SQL port is published.'
bindings=ports(sys.argv[1])
assert set(bindings)=={'8080/tcp'} and all(binding['HostIp']=='127.0.0.1' for binding in bindings['8080/tcp']), 'HTTP must bind only loopback.'
print('Docker port isolation verified.')
PY
cat <<'SQL' | pilot_sql >/dev/null
USE [ProdimtPilot];
IF (SELECT COUNT(*) FROM [SalesChannels]) <> 3 THROW 51000, 'Expected three base channels.', 1;
IF (SELECT COUNT(*) FROM [CustomerAccessTokens]) <> 0 THROW 51000, 'Unexpected customer token.', 1;
IF SUSER_SID(N'prodimt_app') <> (SELECT sid FROM sys.database_principals WHERE name = N'prodimt_app') THROW 51000, 'Orphaned application user.', 1;
EXECUTE AS LOGIN = 'prodimt_app';
IF IS_SRVROLEMEMBER('sysadmin') <> 0 THROW 51000, 'Application must not be sysadmin.', 1;
IF IS_MEMBER('db_owner') <> 0 THROW 51000, 'Application must not be database owner.', 1;
IF HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') <> 0 THROW 51000, 'Application must not migrate schema.', 1;
IF HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'SELECT') <> 1 THROW 51000, 'Application must read products.', 1;
IF HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'INSERT') <> 1 THROW 51000, 'Application must write products.', 1;
REVERT;
SQL
# Repeat SQL provisioning/bootstrap without rotating credentials or seeding rows.
python3 "$pilot_script_dir/config.py" provision-sql "$pilot_env_file" | pilot_sql >/dev/null
pilot_compose run --rm --no-deps -e PilotBootstrap__AdminPassword=DifferentFictionalPassword123! bootstrap
bash "$pilot_script_dir/backup.sh"
[[ -n "$(find "$pilot_config_dir/backups" -name '*.bak' -size +0c -print -quit)" ]]
# Recreate only the application; the same isolated volume retains its data.
pilot_compose up -d --no-deps --force-recreate app
for attempt in $(seq 1 60); do
  if curl --fail --silent --max-time 3 "http://127.0.0.1:$pilot_http_port/health/db" >/dev/null; then break; fi
  sleep 2
done
python3 "$pilot_script_dir/smoke-http.py" "$pilot_env_file" persisted
printf 'Disposable Production Compose smoke passed. Test volume and private files will now be removed.\n'
