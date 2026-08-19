Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$environment = if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    "Development"
} else {
    $env:ASPNETCORE_ENVIRONMENT
}

if ($environment -notin @("Development", "Test")) {
    throw "Database migrations are restricted to Development or Test; no database was changed."
}

Write-Output "Phase 1: migrations are not available. No database was changed."
