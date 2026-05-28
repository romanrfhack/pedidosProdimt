$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
node (Join-Path $RepoRoot "scripts/dev/smoke-fase1.mjs")
