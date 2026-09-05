Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$environment = if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    "Development"
} else {
    $env:ASPNETCORE_ENVIRONMENT
}

if ($environment -notin @("Development", "Test")) {
    throw "Database migrations are restricted to Development or Test; no database was changed."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

$envFile = Join-Path $repositoryRoot ".env"
if (Test-Path -LiteralPath $envFile) {
    Get-Content -LiteralPath $envFile | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -notmatch '=') {
            return
        }

        $parts = $_.Split('=', 2)
        Set-Item -Path ("Env:" + $parts[0].Trim()) -Value $parts[1].Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($env:CRITICAL_ALERTS_DATA_PROTECTION_KEY)) {
    throw "Set CRITICAL_ALERTS_DATA_PROTECTION_KEY in the ignored local .env file before applying protected-data migrations."
}

$env:ASPNETCORE_ENVIRONMENT = $environment
$apiProject = Join-Path $repositoryRoot "src\backend\CriticalAlerts.Api\CriticalAlerts.Api.csproj"
dotnet run --project $apiProject --no-launch-profile -- database migrate
