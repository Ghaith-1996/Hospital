Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot ".env"))) {
    throw "Create the ignored local .env from .env.example before starting PostgreSQL."
}

docker compose up --detach postgres | Out-Null

$deadline = (Get-Date).AddSeconds(90)
do {
    $status = docker inspect --format "{{.State.Health.Status}}" critical-alerts-postgres 2>$null
    if ($status -eq "healthy") {
        Write-Output "PostgreSQL local service is healthy."
        exit 0
    }

    if ((Get-Date) -ge $deadline) {
        throw "PostgreSQL local service did not become healthy within 90 seconds."
    }

    Start-Sleep -Seconds 2
} while ($true)
