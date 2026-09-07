Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot "src\web"
$activePaths = @(
    (Join-Path $webRoot "app"),
    (Join-Path $webRoot "features\connected"),
    (Join-Path $webRoot "features\session"),
    (Join-Path $webRoot "components\layout"),
    (Join-Path $webRoot "lib")
)
$patterns = @(
    '\blocalStorage\b',
    '\bsessionStorage\b',
    'PrototypeProvider',
    'usePrototype',
    'prototype-store'
)
$violations = [Collections.Generic.List[string]]::new()

foreach ($path in $activePaths) {
    Get-ChildItem -LiteralPath $path -File -Recurse -Include *.ts,*.tsx,*.js,*.mjs | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName)
        foreach ($pattern in $patterns) {
            $matches = Select-String -LiteralPath $_.FullName -Pattern $pattern
            foreach ($match in $matches) {
                $violations.Add("${relative}:$($match.LineNumber): active workflow contains '$($match.Matches[0].Value)'")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "STORAGE_SAFETY_VIOLATION $_" }
    throw "Active web workflow storage safety check failed with $($violations.Count) violation(s)."
}

Write-Output "Active web workflow storage safety check passed: no domain local/session storage or prototype-store dependency."
