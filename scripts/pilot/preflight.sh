#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
for command in docker python3 curl; do command -v "$command" >/dev/null || { printf 'Missing required command: %s\n' "$command" >&2; exit 1; }; done
docker info >/dev/null
docker compose version >/dev/null
require_pilot_config
pilot_compose config --quiet
# SQL Server Linux images require x86-64; Docker reports its actual VM resources.
pilot_arch="$(docker info --format '{{.Architecture}}')"
[[ "$pilot_arch" == x86_64 || "$pilot_arch" == amd64 ]] || { printf 'Docker must run on x86-64 for SQL Server 2022.\n' >&2; exit 1; }
pilot_memory="$(docker info --format '{{.MemTotal}}')"
[[ "$pilot_memory" -ge 5368709120 ]] || { printf 'Docker needs at least 5 GiB RAM for building and running this pilot.\n' >&2; exit 1; }
pilot_running_app="$(pilot_compose ps --status running -q app)"
python3 - "$pilot_http_port" "$pilot_repo_dir" "$pilot_running_app" <<'PY'
import shutil,socket,sys
if shutil.disk_usage(sys.argv[2]).free < 10 * 1024**3:
    sys.exit('At least 10 GiB free space is required for the initial image build and database.')
with socket.socket() as probe:
    try: probe.bind(('127.0.0.1', int(sys.argv[1])))
    except OSError:
        if not sys.argv[3]:
            sys.exit('HTTP port is occupied by another service. Choose a free port in private .env; no service was changed.')
PY
printf 'Preflight passed. Project=%s; HTTP=127.0.0.1:%s; SQL has no published port.\n' "$pilot_project" "$pilot_http_port"
