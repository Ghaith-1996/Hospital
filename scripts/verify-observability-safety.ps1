Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$log = Join-Path ([IO.Path]::GetTempPath()) ("critical-alerts-observability-" + [Guid]::NewGuid().ToString("N") + ".log")
try {
    $checks = @(
        @("tests/CriticalAlerts.Api.IntegrationTests", "FullyQualifiedName~Observability|FullyQualifiedName~AuditQueryTests|FullyQualifiedName~HealthEndpointsTests|FullyQualifiedName~OperationalWarning|FullyQualifiedName~LatestSyncStatus"),
        @("tests/CriticalAlerts.Infrastructure.Tests", "FullyQualifiedName~OperationalLoggingTests|FullyQualifiedName~PlatformMetricsTests|FullyQualifiedName~AuditStorageTests|FullyQualifiedName~OperationalWarningProjectionTests")
    )
    foreach ($check in $checks) {
        & dotnet test (Join-Path $repositoryRoot $check[0]) --configuration Release --no-restore --nologo --filter $check[1] *> $log
        $result = $LASTEXITCODE
        $summary = Get-Content -LiteralPath $log | Where-Object { $_ -match '^(Passed!|Failed!)\s+- Failed:' }
        $summary | Write-Output
        if ($result -ne 0) { throw "Focused observability verification failed. Inspect locally without publishing raw test payloads." }
    }
    $tracked = & git -c "safe.directory=$($repositoryRoot.Replace('\', '/'))" -C $repositoryRoot ls-files
    if ($LASTEXITCODE -ne 0) { throw "Tracked artifact verification failed." }
    $unsafe = @($tracked | Where-Object { $_ -match '(^|/)(\.env|TestResults|test-results|playwright-report)(/|$)|\.(dump|backup|log|trx)$' })
    if ($unsafe.Count -gt 0) { throw "An excluded operational artifact is tracked." }
    $systemConfig = Get-Content (Join-Path $repositoryRoot "playwright.system.config.ts") -Raw
    if ($systemConfig -match 'retain-on-failure|on-first-retry') { throw "Payload-bearing browser traces must be disabled for connected workflows." }
    Write-Output "Observability safety passed: runtime logs, metric tags, audit projection, health, correlation and tracked artifacts."
} finally {
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
}
