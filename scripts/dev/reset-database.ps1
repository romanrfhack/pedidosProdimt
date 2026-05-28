$ErrorActionPreference = "Stop"

if ($args.Count -eq 0 -or $args[0] -ne "--confirm") {
    throw "This deletes the local development database. Re-run with: pwsh scripts/dev/reset-database.ps1 --confirm"
}

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
if ([string]::IsNullOrWhiteSpace($env:PRODIMT_API_URL)) { $env:PRODIMT_API_URL = "http://127.0.0.1:5088" }

if ($env:PRODIMT_SQL_DATABASE -ne "ProdimtPedidos" -and $env:PRODIMT_ALLOW_DATABASE_RESET -ne "local-dev") {
    throw "Refusing to reset database '$($env:PRODIMT_SQL_DATABASE)'. Set PRODIMT_ALLOW_DATABASE_RESET=local-dev only for local development."
}

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Pedidos)) {
    if ([string]::IsNullOrWhiteSpace($env:PRODIMT_SQL_SA_PASSWORD)) {
        throw "Missing SQL configuration. Set ConnectionStrings__Pedidos or create infra/dev/.env with PRODIMT_SQL_SA_PASSWORD."
    }

    if ($env:PRODIMT_SQL_SA_PASSWORD -eq "REPLACE_WITH_STRONG_LOCAL_PASSWORD") {
        throw "Replace PRODIMT_SQL_SA_PASSWORD in infra/dev/.env with a local strong password."
    }

    $env:ConnectionStrings__Pedidos = "Server=$($env:PRODIMT_SQL_HOST),$($env:PRODIMT_SQL_PORT);Database=$($env:PRODIMT_SQL_DATABASE);User Id=sa;Password=$($env:PRODIMT_SQL_SA_PASSWORD);TrustServerCertificate=True"
}
elseif ($env:PRODIMT_ALLOW_DATABASE_RESET -ne "local-dev") {
    throw "ConnectionStrings__Pedidos is already set. Refusing reset unless PRODIMT_ALLOW_DATABASE_RESET=local-dev."
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Persistence__Provider = "SqlServer"
$env:DevelopmentSeed__Enabled = "true"
$env:DevelopmentSeed__ApplyMigrations = "false"

Set-Location $RepoRoot

dotnet tool run dotnet-ef database drop --force --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
dotnet tool run dotnet-ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api

$stdoutLogFile = [System.IO.Path]::GetTempFileName()
$stderrLogFile = [System.IO.Path]::GetTempFileName()
Write-Host "Starting API once to apply development seed..."
$process = Start-Process dotnet -ArgumentList @(
    "run",
    "--project", "src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj",
    "--urls", $env:PRODIMT_API_URL
) -RedirectStandardOutput $stdoutLogFile -RedirectStandardError $stderrLogFile -PassThru -NoNewWindow

try {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            Invoke-RestMethod "$($env:PRODIMT_API_URL)/health/db" | Out-Null
            Write-Host "Database reset and development seed completed."
            return
        }
        catch {
            if ($process.HasExited) {
                Write-Error "API stopped before seed validation completed. Stdout: $(Get-Content $stdoutLogFile -Raw) Stderr: $(Get-Content $stderrLogFile -Raw)"
            }

            Start-Sleep -Seconds 1
        }
    }

    throw "Timed out waiting for API seed validation. Stdout: $(Get-Content $stdoutLogFile -Raw) Stderr: $(Get-Content $stderrLogFile -Raw)"
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    }

    Remove-Item $stdoutLogFile -ErrorAction SilentlyContinue
    Remove-Item $stderrLogFile -ErrorAction SilentlyContinue
}
