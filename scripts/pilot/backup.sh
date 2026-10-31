#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_pilot_config
umask 077
mkdir -p "$pilot_config_dir/backups"
pilot_backup_name="ProdimtPilot-$(date -u +%Y%m%dT%H%M%SZ)-$$.bak"
printf "BACKUP DATABASE [ProdimtPilot] TO DISK=N'/var/opt/mssql/data/%s' WITH COPY_ONLY, CHECKSUM;\nRESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/data/%s' WITH CHECKSUM;\n" "$pilot_backup_name" "$pilot_backup_name" | pilot_sql >/dev/null
pilot_compose cp "sqlserver:/var/opt/mssql/data/$pilot_backup_name" "$pilot_config_dir/backups/$pilot_backup_name"
chmod 600 "$pilot_config_dir/backups/$pilot_backup_name"
printf 'Verified backup saved: %s/backups/%s\n' "$pilot_config_dir" "$pilot_backup_name"
