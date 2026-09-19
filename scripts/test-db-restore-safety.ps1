param([switch]$ExerciseCleanup)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "db-restore-test.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) { throw "Restore exercise implementation is missing." }
$previous = $env:ASPNETCORE_ENVIRONMENT
try {
    foreach ($environment in @("Production", "Staging", "Unknown", "")) {
        $env:ASPNETCORE_ENVIRONMENT = $environment
        $rejected = $false
        try { & $scriptPath -ConfirmRestoreTest *> $null } catch { $rejected = $true }
        if (-not $rejected) { throw "Unsafe restore environment was accepted." }
    }
    $env:ASPNETCORE_ENVIRONMENT = "Test"
    $rejected = $false
    try { & $scriptPath *> $null } catch { $rejected = $true }
    if (-not $rejected) { throw "Restore confirmation guard failed." }
    foreach ($name in @("postgres", "hospital", "critical_alerts_test;unsafe", "critical_alerts_test_remote")) {
        $rejected = $false
        try { & $scriptPath -ConfirmRestoreTest -SourceDatabase $name *> $null } catch { $rejected = $true }
        if (-not $rejected) { throw "Unsafe restore source was accepted." }
    }
    if ($ExerciseCleanup) {
        foreach ($point in @("AfterBackup", "AfterRestore")) {
            $rejected = $false
            $evidence = [System.Collections.Generic.List[string]]::new()
            try { & $scriptPath -ConfirmRestoreTest -TestFailurePoint $point | ForEach-Object { $evidence.Add([string]$_) } } catch { $rejected = $true }
            if (-not $rejected) { throw "Injected restore failure did not fail." }
            $requiredStage = if ($point -eq "AfterBackup") { "RESTORE_TEST backup_created=true" } else { "RESTORE_TEST audit_append_only_verified=true" }
            if (-not $evidence.Contains($requiredStage)) { throw "Restore failure test did not reach its intended injection stage." }
            if (-not $evidence.Contains("RESTORE_TEST temporary_database_removed=true temporary_dump_removed=true owned_container_removed=true")) {
                throw "Restore failure cleanup evidence is incomplete."
            }
            $remaining = docker ps --all --quiet --filter "label=criticalalerts.restore-test=true"
            if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace($remaining)) { throw "Restore failure cleanup left a container." }
            Write-Output "RESTORE_FAILURE_TEST stage=$point reached=true cleanup_verified=true"
        }
    }
    Write-Output "Restore safety guards passed; failure cleanup exercised=$([bool]$ExerciseCleanup)."
} finally { $env:ASPNETCORE_ENVIRONMENT = $previous }
