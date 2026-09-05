param(
    [switch]$SkipWebBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ($IsWindows) { Join-Path $repositoryRoot ".dotnet10\dotnet.exe" } else { "dotnet" }
$nodeRoot = Split-Path -Parent $repositoryRoot
$npm = if ($IsWindows) { Join-Path $nodeRoot ".node-v24.16.0\npm.cmd" } else { "npm" }
$npx = if ($IsWindows) { Join-Path $nodeRoot ".node-v24.16.0\npx.cmd" } else { "npx" }
$apiProject = Join-Path $repositoryRoot "src\backend\CriticalAlerts.Api\CriticalAlerts.Api.csproj"
$workerProject = Join-Path $repositoryRoot "src\backend\CriticalAlerts.Worker\CriticalAlerts.Worker.csproj"
$webRoot = Join-Path $repositoryRoot "src\web"
$postgresImage = "postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636"
$runId = [Guid]::NewGuid().ToString("N")
$containerName = "critical-alerts-system-$runId"
$database = "critical_alerts_test_system"
$username = "system_$($runId.Substring(0, 12))"
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))
$dataProtectionKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$logRoot = Join-Path ([IO.Path]::GetTempPath()) "critical-alerts-system-$runId"
$ownedProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()

function Get-EphemeralPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}

function Wait-Http([string]$Url, [Diagnostics.Process]$Process, [int]$Seconds = 120) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) { throw "Process $($Process.Id) exited before $Url became ready (exit $($Process.ExitCode))." }
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec 2 -SkipHttpErrorCheck
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $Url."
}

function Start-OwnedProcess([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [string]$Name) {
    $stdout = Join-Path $logRoot "$Name.stdout.log"
    $stderr = Join-Path $logRoot "$Name.stderr.log"
    $parameters = @{ FilePath = $FilePath; ArgumentList = $Arguments; WorkingDirectory = $WorkingDirectory; PassThru = $true; RedirectStandardOutput = $stdout; RedirectStandardError = $stderr }
    if ($IsWindows) { $parameters.WindowStyle = "Hidden" }
    $process = Start-Process @parameters
    $ownedProcesses.Add($process)
    return $process
}

function Stop-OwnedProcess([Diagnostics.Process]$Process) {
    if ($Process.HasExited) { return }
    if ($IsWindows) {
        taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
    } else {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
    try { Wait-Process -Id $Process.Id -Timeout 15 -ErrorAction SilentlyContinue } catch { }
}

function Test-PortClosed([int]$Port) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync([Net.IPAddress]::Loopback, $Port)
        return -not $task.Wait(500)
    } catch { return $true } finally { $client.Dispose() }
}

$postgresPort = Get-EphemeralPort
$apiPort = Get-EphemeralPort
$webPort = Get-EphemeralPort
$connectionString = "Host=127.0.0.1;Port=$postgresPort;Database=$database;Username=$username;Password=$password"
$exitCode = 1

New-Item -ItemType Directory -Path $logRoot | Out-Null
Set-Location -LiteralPath $repositoryRoot
if ([string]::IsNullOrWhiteSpace($env:PLAYWRIGHT_BROWSERS_PATH)) {
    $localBrowsers = Join-Path $repositoryRoot ".playwright-browsers"
    if (Test-Path -LiteralPath $localBrowsers) { $env:PLAYWRIGHT_BROWSERS_PATH = $localBrowsers }
}

try {
    docker run --detach --name $containerName --publish "127.0.0.1:${postgresPort}:5432" --env "POSTGRES_DB=$database" --env "POSTGRES_USER=$username" --env "POSTGRES_PASSWORD=$password" --health-cmd "pg_isready -U $username -d $database" --health-interval 1s --health-timeout 3s --health-retries 60 $postgresImage | Out-Null
    $deadline = (Get-Date).AddSeconds(90)
    do {
        $health = docker inspect --format "{{.State.Health.Status}}" $containerName
        if ($health -eq "healthy") { break }
        if ((Get-Date) -ge $deadline) { throw "PostgreSQL 18 did not become healthy." }
        Start-Sleep -Seconds 1
    } while ($true)

    $env:ASPNETCORE_ENVIRONMENT = "Test"
    $env:DOTNET_ENVIRONMENT = "Test"
    $env:ConnectionStrings__CriticalAlerts = $connectionString
    $env:DataProtection__Key = $dataProtectionKey
    $env:CRITICAL_ALERTS_DATA_PROTECTION_KEY = $dataProtectionKey
    $env:DevelopmentAuthentication__Enabled = "true"
    $env:SimulationResponses__Enabled = "true"
    $env:SimulationDispatch__Enabled = "true"

    & $dotnet run --project $apiProject --no-launch-profile -- database migrate
    & $dotnet run --project $apiProject --no-launch-profile -- database reset-demo --confirm-demo-reset

    $env:ASPNETCORE_URLS = "http://127.0.0.1:$apiPort"
    $api = Start-OwnedProcess $dotnet @("run", "--project", $apiProject, "--no-launch-profile") $repositoryRoot "api"
    Wait-Http "http://127.0.0.1:$apiPort/health/ready" $api

    $worker = Start-OwnedProcess $dotnet @("run", "--project", $workerProject, "--no-launch-profile") $repositoryRoot "worker"
    $env:CRITICAL_ALERTS_API_URL = "http://127.0.0.1:$apiPort"
    if (-not $SkipWebBuild) {
        & $npm --prefix $webRoot run build
    }

    $web = Start-OwnedProcess $npm @("--prefix", $webRoot, "run", "start", "--", "--hostname", "127.0.0.1", "--port", "$webPort") $repositoryRoot "web"
    Wait-Http "http://127.0.0.1:$webPort" $web

    $env:SYSTEM_E2E_API_URL = "http://127.0.0.1:$apiPort"
    $env:SYSTEM_E2E_WEB_PORT = "$webPort"
    $env:SYSTEM_E2E_POSTGRES_CONTAINER = $containerName
    $env:SYSTEM_E2E_POSTGRES_DATABASE = $database
    $env:SYSTEM_E2E_POSTGRES_USER = $username
    & $npx playwright test --config playwright.system.config.ts
    $exitCode = $LASTEXITCODE
} finally {
    for ($index = $ownedProcesses.Count - 1; $index -ge 0; $index--) {
        $process = $ownedProcesses[$index]
        Stop-OwnedProcess $process
    }
    docker rm --force $containerName 2>$null | Out-Null
    $containerRemaining = docker ps --all --quiet --filter "name=^/${containerName}$"
    $liveOwnedProcesses = @($ownedProcesses | Where-Object { -not $_.HasExited }).Count
    $portsClosed = [int]((Test-PortClosed $postgresPort) -and (Test-PortClosed $apiPort) -and (Test-PortClosed $webPort))
    Write-Output "SYSTEM_E2E_TEARDOWN container_remaining=$([int](-not [string]::IsNullOrWhiteSpace($containerRemaining))) live_owned_processes=$liveOwnedProcesses ports_closed=$portsClosed logs=$logRoot"
}

if ($exitCode -ne 0) { throw "System E2E failed with exit $exitCode." }
Write-Output "System E2E completed."
