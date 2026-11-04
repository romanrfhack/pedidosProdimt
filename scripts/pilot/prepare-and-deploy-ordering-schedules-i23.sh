#!/usr/bin/env bash
# The exported copy lives beside source.bundle, frontend.zip and SHA256SUMS.txt.
# All compilation and unit/model checks finish before contacting production.
set -Eeuo pipefail
umask 077

i23_mode="${1:---prepare-only}"
case "$i23_mode" in
  --prepare-only|--deploy) ;;
  *) printf 'Uso: bash %s [--prepare-only|--deploy]\n' "$0" >&2; exit 2 ;;
esac
i23_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
i23_phase='verificar-descarga'
trap 'i23_rc=$?; printf "Se detuvo en %s (código %s). Conserva la carpeta y comparte la salida sin credenciales.\n" "$i23_phase" "$i23_rc" >&2; exit "$i23_rc"' ERR
for i23_tool in python3 git sha256sum; do command -v "$i23_tool" >/dev/null; done
cd "$i23_root"
sha256sum --quiet -c SHA256SUMS.txt
printf 'PRODIMT I23 revisión 3: Horarios + Reparto RF1; pruebas de autorización con fecha controlada.\n'
i23_source="$(cat SOURCE_COMMIT.txt)"
[[ "$i23_source" =~ ^[a-f0-9]{40}$ ]]
i23_output="$i23_root/salida"
i23_package="$i23_output/PRODIMT_Horarios_Reparto_I23_Instalacion.zip"
i23_native="$i23_output/prodimt-ordering-schedules-i23-update"
mkdir -p "$i23_output"

if [[ -e "$i23_package" ]]; then
  # Reuse the identical pinned payload when recovering or repeating installation.
  i23_phase='verificar-paquete-preparado'
  [[ -f "$i23_output/PAQUETE_SHA256.txt" && -f "$i23_output/SOURCE_COMMIT.txt" ]]
  [[ "$(cat "$i23_output/SOURCE_COMMIT.txt")" == "$i23_source" ]]
  (cd "$i23_output" && sha256sum --quiet -c PAQUETE_SHA256.txt)
  printf 'Reutilizando el paquete compilado y verificado de esta misma fuente.\n'
else
  i23_phase='preparar-compilacion-local'
  [[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]]
  command -v curl >/dev/null
  command -v tar >/dev/null
  i23_work="$(mktemp -d "${TMPDIR:-/tmp}/prodimt-i23.XXXXXXXX")"
  # Keep logs and build products available after failure; this path has no secrets.
  printf 'Carpeta de compilación: %s\n' "$i23_work"
  i23_sdk=''
  if command -v dotnet >/dev/null 2>&1; then
    i23_sdk="$(dotnet --list-sdks | awk '/^10\./ {print $1}' | sort -V | tail -1)"
  fi
  if [[ -z "$i23_sdk" ]]; then
    i23_phase='obtener-sdk-dotnet-en-carpeta-local'
    i23_runtime="$i23_root/.runtime/dotnet-10.0"
    if [[ ! -x "$i23_runtime/dotnet" ]]; then
      curl --fail --show-error --silent --location --retry 2 \
        https://dot.net/v1/dotnet-install.sh --output "$i23_work/dotnet-install.sh"
      bash "$i23_work/dotnet-install.sh" --channel 10.0 --quality GA \
        --install-dir "$i23_runtime" --no-path
    fi
    export DOTNET_ROOT="$i23_runtime"
    export PATH="$i23_runtime:$PATH"
    i23_sdk="$(dotnet --list-sdks | awk '/^10\./ {print $1}' | sort -V | tail -1)"
    [[ -n "$i23_sdk" ]]
  fi
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1
  export DOTNET_CLI_HOME="$i23_work/dotnet-home"
  unset PRODIMT_SQL_TEST_CONNECTION
  dotnet new globaljson --sdk-version "$i23_sdk" --roll-forward latestPatch --output "$i23_work"
  git clone --quiet --branch codex/i23-schedules-on-delivery "$i23_root/source.bundle" "$i23_work/source"
  i23_repo="$i23_work/source"
  cd "$i23_repo"
  [[ "$(git rev-parse HEAD)" == "$i23_source" ]]
  git bundle verify "$i23_root/source.bundle"
  python3 -m zipfile -e "$i23_root/frontend.zip" "$i23_work/frontend"

  i23_phase='pruebas-python'
  python3 -m unittest discover -s tests/pilot
  i23_phase='restaurar-y-compilar-backend'
  dotnet restore src/Prodimt.Pedidos.sln --disable-parallel -m:1 -nr:false
  dotnet build src/Prodimt.Pedidos.sln --configuration Release --no-restore \
    -m:1 -nr:false -p:UseSharedCompilation=false
  i23_phase='pruebas-backend'
  dotnet test tests/Prodimt.Pedidos.Domain.Tests --configuration Release --no-build --no-restore -m:1 -nr:false
  dotnet test tests/Prodimt.Pedidos.Application.Tests --configuration Release --no-build --no-restore -m:1 -nr:false
  i23_phase='validar-modelo-y-generar-sql'
  dotnet tool restore
  i23_ef=(--project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --configuration Release --no-build)
  dotnet tool run dotnet-ef migrations has-pending-model-changes "${i23_ef[@]}"
  i23_sql="$i23_work/sql"
  mkdir "$i23_sql"
  i23_generate_sql() {
    dotnet tool run dotnet-ef migrations script "$1" "$2" --idempotent "${i23_ef[@]}" --output "$i23_sql/$3.sql"
  }
  i23_generate_sql 20261101190000_AddRecoverableCustomerAccessTokens 20261102152307_AddCustomerProductSetup i16
  i23_generate_sql 20261102152307_AddCustomerProductSetup 20261102235000_AddSecurityAdministration i18
  i23_generate_sql 20261102235000_AddSecurityAdministration 20261103010000_AddProductionAllocation i19
  i23_generate_sql 0 20261103020000_AddDeliveryOperations baseline
  dotnet tool run dotnet-ef migrations script 20261103020000_AddDeliveryOperations \
    20261103030000_AddOrderingSchedules "${i23_ef[@]}" --output "$i23_sql/i23.sql"

  i23_phase='publicar-binarios-linux-y-validacion'
  i23_publish=(-c Release -r linux-x64 --self-contained false -m:1 -nr:false -p:UseSharedCompilation=false)
  dotnet publish src/Prodimt.Pedidos.Api "${i23_publish[@]}" --output "$i23_work/backend"
  dotnet publish tests/Prodimt.Pedidos.DeploymentValidation "${i23_publish[@]}" --output "$i23_work/validation"
  i23_phase='ensamblar-paquete-con-hashes'
  python3 scripts/pilot/build-ordering-schedules-on-delivery-package.py \
    --backend "$i23_work/backend" --frontend "$i23_work/frontend" \
    --migration "$i23_sql/i23.sql" --setup-migration "$i23_sql/i16.sql" \
    --identity-migration "$i23_sql/i18.sql" --production-migration "$i23_sql/i19.sql" \
    --baseline-migration "$i23_sql/baseline.sql" --validation "$i23_work/validation" \
    --output "$i23_package" --source "$i23_source" | tee "$i23_output/compilacion.json"
  cp "$i23_root/SOURCE_COMMIT.txt" "$i23_output/SOURCE_COMMIT.txt"
  (cd "$i23_output" && sha256sum PRODIMT_Horarios_Reparto_I23_Instalacion.zip > PAQUETE_SHA256.txt)
fi

i23_phase='verificar-artefactos-de-despliegue'
python3 -m zipfile -e "$i23_package" "$i23_output"
(cd "$i23_native" && sha256sum --quiet -c SHA256SUMS.txt)
printf '\nPaquete preparado: %s\n' "$i23_package"
if [[ "$i23_mode" == --prepare-only ]]; then
  printf 'Preparación terminada. Para aplicar ejecuta este mismo script con --deploy.\n'
  exit 0
fi

i23_phase='copiar-paquete-al-vps'
command -v scp >/dev/null
command -v ssh >/dev/null
scp "$i23_native/PRODIMT_Instalar_I23.py" "$i23_native/prodimt-ordering-schedules-i23-payload.zip" \
  root@194.238.26.70:/root/
i23_phase='inspeccionar-validar-y-aplicar-i23'
ssh -t root@194.238.26.70 \
  'python3 /root/PRODIMT_Instalar_I23.py --package /root/prodimt-ordering-schedules-i23-payload.zip &&
   python3 /root/PRODIMT_Instalar_I23.py --package /root/prodimt-ordering-schedules-i23-payload.zip --apply'
printf 'Comando remoto finalizado. Revisa el JSON: updated/completed, migrations_after=18, health_verified=true.\n'
