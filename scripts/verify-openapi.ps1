param(
    [string]$DocumentPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\api\openapi.json")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $DocumentPath)) {
    throw "OpenAPI contract was not found at $DocumentPath."
}

$document = Get-Content -LiteralPath $DocumentPath -Raw | ConvertFrom-Json
if ($document.openapi -notlike "3.1.*") {
    throw "OpenAPI contract must declare an OpenAPI 3.1.x version."
}

$paths = @($document.paths.PSObject.Properties.Name)
$requiredPaths = @(
    "/api/v1/alerts/{alertId}/resolve",
    "/api/v1/alerts/{alertId}/cancel",
    "/api/v1/my-alerts/{alertId}/responses",
    "/api/v1/directory/imports/preview"
)
foreach ($requiredPath in $requiredPaths) {
    if ($paths -notcontains $requiredPath) {
        throw "OpenAPI contract is missing required path $requiredPath."
    }
}

$unversioned = @($paths | Where-Object { $_ -like "/api/*" -and $_ -notlike "/api/v1/*" })
if ($unversioned.Count -gt 0) {
    throw "OpenAPI contract contains unversioned API paths: $($unversioned -join ', ')"
}

Write-Output "OpenAPI 3.1 contract verified: $DocumentPath"
