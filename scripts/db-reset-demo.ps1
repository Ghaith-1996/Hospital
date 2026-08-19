Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$environment = if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    "Development"
} else {
    $env:ASPNETCORE_ENVIRONMENT
}

if ($environment -notin @("Development", "Test")) {
    throw "Demo reset is restricted to Development or Test; no database was changed."
}

throw "Phase 1 has no demo reset implementation. No database was changed."
