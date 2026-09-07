param(
    [string]$DocumentPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\api\openapi.json"),
    [switch]$WriteDocument
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repositoryRoot "src\backend\CriticalAlerts.Api\CriticalAlerts.Api.csproj"
$localDotnet = Join-Path $repositoryRoot ".dotnet10\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("critical-alerts-openapi-" + [Guid]::NewGuid().ToString("N"))
$runtimeDocumentPath = Join-Path $temporaryDirectory "openapi.json"
$standardOutputPath = Join-Path $temporaryDirectory "api.stdout.log"
$standardErrorPath = Join-Path $temporaryDirectory "api.stderr.log"
$process = $null

function ConvertTo-CanonicalOpenApiValue {
    param([AllowNull()]$Value, [AllowNull()][string]$PropertyName)
    if ($null -eq $Value) { return $null }
    if ($Value -is [Management.Automation.PSCustomObject]) {
        $ordered = [ordered]@{}
        foreach ($property in @($Value.PSObject.Properties | Sort-Object Name)) {
            $ordered[$property.Name] = ConvertTo-CanonicalOpenApiValue $property.Value $property.Name
        }
        return $ordered
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        [object[]]$items = @($Value | ForEach-Object { ConvertTo-CanonicalOpenApiValue $_ $PropertyName })
        if ($PropertyName -in @("required", "enum", "type", "allOf", "anyOf", "oneOf", "tags")) {
            $items = @($items | Sort-Object { ConvertTo-Json $_ -Depth 100 -Compress })
        }
        return ,$items
    }
    return $Value
}

function ConvertTo-CanonicalOpenApiJson {
    param([string]$Path)
    $parsed = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return ConvertTo-Json (ConvertTo-CanonicalOpenApiValue $parsed $null) -Depth 100
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    & $dotnet build $apiProject --configuration Release --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "The API build failed." }

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    $url = "http://127.0.0.1:$port"
    $environment = @{
        "ASPNETCORE_ENVIRONMENT" = "Test"
        "DevelopmentAuthentication__Enabled" = "true"
        "SimulationResponses__Enabled" = "true"
        "ConnectionStrings__CriticalAlerts" = "Host=127.0.0.1;Database=openapi_unused;Username=unused;Password=unused"
        "DataProtection__Key" = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    }
    $apiAssembly = Join-Path (Split-Path -Parent $apiProject) "bin\Release\net10.0\CriticalAlerts.Api.dll"
    $startOptions = @{
        FilePath = $dotnet
        ArgumentList = @(('"' + $apiAssembly + '"'), "--urls", $url)
        WorkingDirectory = Split-Path -Parent $apiProject
        Environment = $environment
        RedirectStandardOutput = $standardOutputPath
        RedirectStandardError = $standardErrorPath
        PassThru = $true
    }
    if ($IsWindows) { $startOptions.WindowStyle = "Hidden" }
    $process = Start-Process @startOptions

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        if ($process.HasExited) {
            throw "The API exited before producing OpenAPI. $((Get-Content -LiteralPath $standardErrorPath -Raw -ErrorAction SilentlyContinue))"
        }
        try { Invoke-WebRequest -Uri "$url/openapi/v1.json" -OutFile $runtimeDocumentPath | Out-Null }
        catch { Start-Sleep -Milliseconds 200 }
    } until ((Test-Path -LiteralPath $runtimeDocumentPath) -or [DateTimeOffset]::UtcNow -ge $deadline)

    if (-not (Test-Path -LiteralPath $runtimeDocumentPath)) { throw "Timed out waiting for the runtime OpenAPI document." }
    $runtimeCanonical = ConvertTo-CanonicalOpenApiJson $runtimeDocumentPath
    if ($WriteDocument) {
        Set-Content -LiteralPath $DocumentPath -Value $runtimeCanonical -Encoding utf8
        Write-Output "Generated deterministic OpenAPI 3.1 contract: $DocumentPath"
        return
    }
    if (-not (Test-Path -LiteralPath $DocumentPath)) {
        throw "OpenAPI contract was not found at $DocumentPath. Run this script with -WriteDocument."
    }
    $committedCanonical = ConvertTo-CanonicalOpenApiJson $DocumentPath
    if ($runtimeCanonical -cne $committedCanonical) {
        throw "OpenAPI contract drift detected. Run this script with -WriteDocument and review the complete semantic diff."
    }
    Write-Output "OpenAPI 3.1 contract matches the complete runtime contract: $DocumentPath"
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        $resolvedTemporaryDirectory = [IO.Path]::GetFullPath($temporaryDirectory)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ([IO.Path]::GetDirectoryName($resolvedTemporaryDirectory) -ne $temporaryRoot -or
            [IO.Path]::GetFileName($resolvedTemporaryDirectory) -notmatch '^critical-alerts-openapi-[a-f0-9]{32}$') {
            throw "Refusing to delete an unexpected temporary directory: $resolvedTemporaryDirectory"
        }
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
