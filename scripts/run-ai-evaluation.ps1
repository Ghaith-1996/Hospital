Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousReport = $env:ASSISTANCE_EVALUATION_REPORT
try {
    $env:ASSISTANCE_EVALUATION_REPORT = 'true'
    dotnet test (Join-Path $repositoryRoot 'tests/CriticalAlerts.Infrastructure.Tests') --configuration Release --no-restore --filter 'FullyQualifiedName~AssistanceEvaluation' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Fictional evaluation failed.' }
    Get-Content -LiteralPath (Join-Path $repositoryRoot 'artifacts/ai-evaluation/summary.json') -Raw
} finally { $env:ASSISTANCE_EVALUATION_REPORT = $previousReport }
