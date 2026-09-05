Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot "src\web"
$solution = Join-Path $repositoryRoot "src\backend\CriticalAlerts.sln"

Set-Location -LiteralPath $repositoryRoot

& (Join-Path $repositoryRoot "scripts\verify-no-sensitive-data.ps1")
& (Join-Path $repositoryRoot "scripts\verify-web-storage-safety.ps1")
& (Join-Path $repositoryRoot "scripts\verify-openapi.ps1")
dotnet restore $solution --locked-mode --nologo
dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
dotnet list $solution package --vulnerable --include-transitive --no-restore
dotnet build $solution --configuration Release --no-restore --nologo
dotnet test $solution --configuration Release --no-build --nologo

npm.cmd ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit $LASTEXITCODE" }
npm.cmd --prefix $webRoot ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw "npm ci (web) failed with exit $LASTEXITCODE" }
npm.cmd audit --prefix $webRoot --audit-level=high --omit=dev
if ($LASTEXITCODE -ne 0) { throw "web dependency audit failed with exit $LASTEXITCODE" }
npm.cmd --prefix $webRoot test -- --run
if ($LASTEXITCODE -ne 0) { throw "web unit tests failed with exit $LASTEXITCODE" }
npm.cmd --prefix $webRoot run typecheck
if ($LASTEXITCODE -ne 0) { throw "web typecheck failed with exit $LASTEXITCODE" }
npm.cmd --prefix $webRoot run lint
if ($LASTEXITCODE -ne 0) { throw "web lint failed with exit $LASTEXITCODE" }
npm.cmd --prefix $webRoot run build
if ($LASTEXITCODE -ne 0) { throw "web production build failed with exit $LASTEXITCODE" }
npm.cmd run web:e2e
if ($LASTEXITCODE -ne 0) { throw "web e2e failed with exit $LASTEXITCODE" }
& (Join-Path $repositoryRoot "scripts\system-e2e.ps1") -SkipWebBuild

docker build --file (Join-Path $repositoryRoot "src\backend\CriticalAlerts.Api\Dockerfile") --tag critical-alerts-api:verification $repositoryRoot
if ($LASTEXITCODE -ne 0) { throw "API container build failed with exit $LASTEXITCODE" }
docker build --file (Join-Path $repositoryRoot "src\backend\CriticalAlerts.Worker\Dockerfile") --tag critical-alerts-worker:verification $repositoryRoot
if ($LASTEXITCODE -ne 0) { throw "worker container build failed with exit $LASTEXITCODE" }
docker build --file (Join-Path $repositoryRoot "src\web\Dockerfile") --tag critical-alerts-web:verification $repositoryRoot
if ($LASTEXITCODE -ne 0) { throw "web container build failed with exit $LASTEXITCODE" }

& (Join-Path $repositoryRoot "scripts\verify-web-container.ps1")

Write-Output "Verification command completed."
