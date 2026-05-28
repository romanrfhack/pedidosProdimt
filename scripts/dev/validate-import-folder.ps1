param(
    [Parameter(Mandatory = $true)]
    [string]$CsvFolder
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")

node (Join-Path $RepoRoot "scripts/dev/import-folder.mjs") validate $CsvFolder
