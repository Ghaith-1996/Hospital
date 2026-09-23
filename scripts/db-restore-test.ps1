param(
    [switch]$ConfirmRestoreTest,
    [string]$SourceDatabase = "critical_alerts_test_restore_source",
    [ValidateSet("None", "AfterBackup", "AfterRestore")][string]$TestFailurePoint = "None"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
# Native output is captured; never expose provider exceptions, commands, credentials, or rows.
$PSNativeCommandUseErrorActionPreference = $false
if (-not $ConfirmRestoreTest) { throw "Restore exercise requires -ConfirmRestoreTest; no database was changed." }
if ($env:ASPNETCORE_ENVIRONMENT -cnotin @("Development", "Test")) { throw "Restore exercise requires an explicit Development or Test environment." }
if ($SourceDatabase -cne "critical_alerts_test_restore_source") { throw "Restore exercise accepts only its isolated fictional source database." }
if ($TestFailurePoint -ne "None" -and $env:ASPNETCORE_ENVIRONMENT -cne "Test") { throw "Failure injection requires Test." }
if ($env:DOCKER_HOST -and $env:DOCKER_HOST -notmatch '^(npipe|unix)://') { throw "Restore exercise requires a local Docker engine." }
$contextEndpoint = & docker context inspect --format '{{.Endpoints.docker.Host}}' 2>$null
if ($LASTEXITCODE -ne 0 -or $contextEndpoint -notmatch '^(npipe|unix)://') { throw "Restore exercise requires a local Docker engine." }

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString("N")
$container = "critical-alerts-restore-$runId"
$restoreDatabase = "critical_alerts_test_restore_$runId"
if ($restoreDatabase -eq $SourceDatabase -or $restoreDatabase -notmatch '^critical_alerts_test_restore_[a-f0-9]{32}$') { throw "Unsafe restore target." }
$dump = "/tmp/phase10-$runId.dump"
$image = "postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636"
$user = "restore_simulation"
$apiProject = Join-Path $repositoryRoot "src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj"
$apiDll = Join-Path $repositoryRoot "src/backend/CriticalAlerts.Api/bin/Release/net10.0/CriticalAlerts.Api.dll"
$saved = @{}
foreach ($name in @("POSTGRES_PASSWORD", "ConnectionStrings__CriticalAlerts", "CRITICAL_ALERTS_DATA_PROTECTION_KEY")) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$owned = $false
$restoreCreated = $false
$dumpCreated = $false
$cleanupPassed = $false
$passed = $false
$total = [Diagnostics.Stopwatch]::StartNew()
$backupMs = 0L
$restoreMs = 0L
$validationMs = 0L

function Invoke-SafeDocker([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "A PostgreSQL restore exercise operation failed." }
    return $result
}
function Read-Validation {
    $result = & dotnet $apiDll database validate-restore 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Application restore validation failed." }
    # The command emits structural counts only. Keep the values in memory.
    try { return (($result -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable) }
    catch { throw "Application restore validation returned an invalid result." }
}

try {
    & dotnet build $apiProject --configuration Release --no-restore --nologo *> $null
    if ($LASTEXITCODE -ne 0) { throw "Restore validation host build failed." }
    $env:POSTGRES_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))
    $env:CRITICAL_ALERTS_DATA_PROTECTION_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $owned = $true
    Invoke-SafeDocker @("run", "--detach", "--name", $container, "--label", "criticalalerts.restore-test=true",
        "--publish", "127.0.0.1::5432", "--env", "POSTGRES_PASSWORD", "--env", "POSTGRES_USER=$user",
        "--env", "POSTGRES_DB=$SourceDatabase", $image) | Out-Null
    $portResult = (Invoke-SafeDocker @("port", $container, "5432/tcp") | Select-Object -First 1).ToString()
    if ($portResult -notmatch '^127\.0\.0\.1:([0-9]+)$') { throw "Restore database is not loopback-bound." }
    $port = [int]$Matches[1]
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        & docker exec $container pg_isready --username $user --dbname $SourceDatabase *> $null
        if ($LASTEXITCODE -eq 0) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Fictional source database did not become healthy." }
        Start-Sleep -Milliseconds 250
    } while ($true)
    $sourceConnection = "Host=127.0.0.1;Port=$port;Database=$SourceDatabase;Username=$user;Password=$($env:POSTGRES_PASSWORD)"
    $env:ConnectionStrings__CriticalAlerts = $sourceConnection
    & dotnet $apiDll database reset-demo --confirm-demo-reset *> $null
    if ($LASTEXITCODE -ne 0) { throw "Isolated fictional source initialization failed." }
    $source = Read-Validation
    Write-Output "RESTORE_TEST source_database_validated=true"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    # Claim artifact ownership before execution so partial dumps are removed on failure.
    $dumpCreated = $true
    Invoke-SafeDocker @("exec", $container, "pg_dump", "--username", $user, "--dbname", $SourceDatabase,
        "--format=custom", "--no-owner", "--no-acl", "--file", $dump) | Out-Null
    $backupMs = $timer.ElapsedMilliseconds
    Write-Output "RESTORE_TEST backup_created=true"
    if ($TestFailurePoint -eq "AfterBackup") { throw "Injected simulation restore failure." }
    $restoreCreated = $true
    Invoke-SafeDocker @("exec", $container, "createdb", "--username", $user, "--template=template0", $restoreDatabase) | Out-Null
    Write-Output "RESTORE_TEST temporary_database_created=true"
    $timer.Restart()
    Invoke-SafeDocker @("exec", $container, "pg_restore", "--username", $user, "--dbname", $restoreDatabase,
        "--no-owner", "--no-acl", "--exit-on-error", $dump) | Out-Null
    $restoreMs = $timer.ElapsedMilliseconds
    Write-Output "RESTORE_TEST restore_succeeded=true"
    $mutation = & docker exec $container psql --username $user --dbname $restoreDatabase --set ON_ERROR_STOP=1 --set VERBOSITY=sqlstate --command "UPDATE audit_events SET action = action WHERE false" 2>&1
    if ($LASTEXITCODE -eq 0 -or ($mutation -join ' ') -notmatch '23514') { throw "Restored append-only audit protection failed." }
    Write-Output "RESTORE_TEST audit_append_only_verified=true"
    $assistanceMutation = & docker exec $container psql --username $user --dbname $restoreDatabase --set ON_ERROR_STOP=1 --set VERBOSITY=sqlstate --command "UPDATE alert_assistance_results SET kind = kind WHERE false" 2>&1
    if ($LASTEXITCODE -eq 0 -or ($assistanceMutation -join ' ') -notmatch '23514') { throw "Restored assistance evidence immutability failed." }
    Write-Output "RESTORE_TEST assistance_immutability_verified=true"
    if ($TestFailurePoint -eq "AfterRestore") { throw "Injected simulation restore failure." }
    $timer.Restart()
    $env:ConnectionStrings__CriticalAlerts = "Host=127.0.0.1;Port=$port;Database=$restoreDatabase;Username=$user;Password=$($env:POSTGRES_PASSWORD)"
    $restored = Read-Validation
    if (($source.Migrations -join "|") -cne ($restored.Migrations -join "|")) { throw "Restored migration history differs." }
    if ($source.Counts.Count -ne $restored.Counts.Count) { throw "Restored table inventory differs." }
    foreach ($table in $source.Counts.Keys) {
        if (-not $restored.Counts.ContainsKey($table) -or $source.Counts[$table] -ne $restored.Counts[$table]) { throw "Restored safe counts differ." }
    }
    # Revalidate the untouched source, without running any workflow writes.
    $env:ConnectionStrings__CriticalAlerts = $sourceConnection
    $unchanged = Read-Validation
    foreach ($table in $source.Counts.Keys) {
        if ($source.Counts[$table] -ne $unchanged.Counts[$table]) { throw "Source changed during isolated restore exercise." }
    }
    $validationMs = $timer.ElapsedMilliseconds
    Write-Output "RESTORE_TEST schema_verified=true relational_invariants_matched=true safe_counts_matched=true restored_database_readable=true source_unchanged=true"
    $passed = $true
} catch {
    # Do not forward a native error or inner exception into test output.
    Write-Output "RESTORE_TEST result=failed category=exercise-failed"
} finally {
    if ($owned) {
        $databaseRemoved = -not $restoreCreated
        $dumpRemoved = -not $dumpCreated
        if ($restoreCreated) {
            & docker exec $container dropdb --username $user --if-exists --force $restoreDatabase *> $null
            $databaseRemoved = $LASTEXITCODE -eq 0
        }
        if ($dumpCreated) {
            & docker exec $container rm -f -- $dump *> $null
            & docker exec $container test ! -e $dump *> $null
            $dumpRemoved = $LASTEXITCODE -eq 0
        }
        & docker rm --force --volumes $container *> $null
        $removed = $LASTEXITCODE -eq 0
        $remaining = & docker ps --all --quiet --filter "name=^/$container$" 2>$null
        $cleanupPassed = $removed -and $LASTEXITCODE -eq 0 -and [string]::IsNullOrWhiteSpace($remaining)
        Write-Output "RESTORE_TEST temporary_database_removed=$($databaseRemoved.ToString().ToLowerInvariant()) temporary_dump_removed=$($dumpRemoved.ToString().ToLowerInvariant()) owned_container_removed=$($cleanupPassed.ToString().ToLowerInvariant())"
        $cleanupPassed = $cleanupPassed -and $databaseRemoved -and $dumpRemoved
    } else { $cleanupPassed = $true }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
if (-not $cleanupPassed) { throw "Restore exercise cleanup failed." }
if (-not $passed) { throw "Restore exercise failed; owned resources were cleaned." }
Write-Output "RESTORE_TEST result=passed backup_ms=$backupMs restore_ms=$restoreMs verification_ms=$validationMs total_ms=$($total.ElapsedMilliseconds)"
Write-Output "Measured simulation exercise durations are not approved production recovery objectives. RPO/RTO: REQUIRES_HOSPITAL_DECISION."
