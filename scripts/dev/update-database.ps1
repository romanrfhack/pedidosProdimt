$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$EnvFile = Join-Path $RepoRoot "infra/dev/.env"

if (Test-Path $EnvFile) {
    Get-Content $EnvFile | ForEach-Object {
        if ($_ -match "^\s*#" -or $_ -notmatch "=") { return }
        $name, $value = $_ -split "=", 2
        [Environment]::SetEnvironmentVariable($name.Trim(), $value.Trim(), "Process")
    }
}

if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_HOST)) { $env:PRODIMT_SQL_HOST = "localhost" }
if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_PORT)) { $env:PRODIMT_SQL_PORT = "1433" }
if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_DATABASE)) { $env:PRODIMT_SQL_DATABASE = "ProdimtPedidos" }

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Pedidos)) {
    if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_SA_PASSWORD)) {
        throw "Missing SQL configuration. Set ConnectionStrings__Pedidos or create infra/dev/.env with PRODIMT_SQL_SA_PASSWORD."
    }

    if ($env:PRODIMT_SQL_SA_PASSWORD -eq "REPLACE_WITH_STRONG_LOCAL_PASSWORD") {
        throw "Replace PRODIMT_SQL_SA_PASSWORD in infra/dev/.env with a local strong password."
    }

    $env:ConnectionStrings__Pedidos = "Server=$($env:PRODIMT_SQL_HOST),$($env:PRODIMT_SQL_PORT);Database=$($env:PRODIMT_SQL_DATABASE);User Id=sa;Password=$($env:PRODIMT_SQL_SA_PASSWORD);TrustServerCertificate=True"
}

$env:Persistence__Provider = "SqlServer"

Set-Location $RepoRoot
dotnet ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
