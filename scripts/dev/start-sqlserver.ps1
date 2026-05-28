$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$EnvFile = Join-Path $RepoRoot "infra/dev/.env"
$ComposeFile = Join-Path $RepoRoot "infra/dev/docker-compose.sqlserver.yml"

if (-not (Test-Path $EnvFile)) {
    throw "Missing infra/dev/.env. Copy infra/dev/.env.example to infra/dev/.env and set PRODIMT_SQL_SA_PASSWORD."
}

Get-Content $EnvFile | ForEach-Object {
    if ($_ -match "^\s*#" -or $_ -notmatch "=") { return }
    $name, $value = $_ -split "=", 2
    [Environment]::SetEnvironmentVariable($name.Trim(), $value.Trim(), "Process")
}

if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_SA_PASSWORD)) {
    throw "Missing PRODIMT_SQL_SA_PASSWORD in infra/dev/.env."
}

if ($env:PRODIMT_SQL_SA_PASSWORD -eq "REPLACE_WITH_STRONG_LOCAL_PASSWORD") {
    throw "Replace PRODIMT_SQL_SA_PASSWORD in infra/dev/.env with a local strong password."
}

docker compose --env-file $EnvFile -f $ComposeFile up -d
$Port = if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_PORT)) { "1433" } else { $env:PRODIMT_SQL_PORT }
Write-Host "SQL Server container requested. Port: $Port. Container: prodimt-pedidos-sqlserver."
