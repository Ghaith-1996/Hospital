Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

docker compose stop postgres | Out-Null
Write-Output "PostgreSQL local service stopped. Volumes were retained."
