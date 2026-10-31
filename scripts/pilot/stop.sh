#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_pilot_config
pilot_compose stop app sqlserver
printf 'Pilot stopped. SQL volume and private configuration preserved.\n'
