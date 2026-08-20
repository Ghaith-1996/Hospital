Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot "src\web"
$solution = Join-Path $repositoryRoot "src\backend\CriticalAlerts.sln"

Set-Location -LiteralPath $repositoryRoot

& (Join-Path $repositoryRoot "scripts\verify-no-sensitive-data.ps1")
dotnet build $solution --configuration Release --nologo
dotnet test $solution --configuration Release --no-build --nologo

npm ci --no-audit --no-fund
npm --prefix $webRoot ci --no-audit --no-fund
npm --prefix $webRoot test -- --run
npm --prefix $webRoot run typecheck
npm --prefix $webRoot run lint
npm run web:e2e

Write-Output "Phase 1 verification command completed."
