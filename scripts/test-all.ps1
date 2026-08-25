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
if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit $LASTEXITCODE" }
npm --prefix $webRoot ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw "npm ci (web) failed with exit $LASTEXITCODE" }
npm --prefix $webRoot test -- --run
if ($LASTEXITCODE -ne 0) { throw "web unit tests failed with exit $LASTEXITCODE" }
npm --prefix $webRoot run typecheck
if ($LASTEXITCODE -ne 0) { throw "web typecheck failed with exit $LASTEXITCODE" }
npm --prefix $webRoot run lint
if ($LASTEXITCODE -ne 0) { throw "web lint failed with exit $LASTEXITCODE" }
npm run web:e2e
if ($LASTEXITCODE -ne 0) { throw "web e2e failed with exit $LASTEXITCODE" }

Write-Output "Verification command completed."
