param(
    [Parameter(Mandatory = $true)]
    [string]$CsvFolder,

    [Parameter(Mandatory = $true)]
    [string]$Confirm
)

$ErrorActionPreference = "Stop"

if ($Confirm -ne "--confirm") {
    Write-Error "Usage: pwsh scripts/dev/apply-import-folder.ps1 <csv-folder> --confirm"
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")

node (Join-Path $RepoRoot "scripts/dev/import-folder.mjs") apply $CsvFolder --confirm
