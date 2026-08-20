Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$ignoredDirectoryNames = @(".git", ".next", "node_modules", "bin", "obj", "TestResults", "playwright-report", "test-results", ".playwright-browsers", ".dotnet")
$files = Get-ChildItem -LiteralPath $repositoryRoot -File -Recurse | Where-Object {
    $relative = $_.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/')
    $parts = $relative -split '[\\/]'
    @($parts | Where-Object { $_ -in $ignoredDirectoryNames }).Count -eq 0
}

$issues = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $relative = $file.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/')

    if ($file.Name -eq ".env") {
        $trackedEnv = git -C $repositoryRoot ls-files -- $relative
        if (-not [string]::IsNullOrWhiteSpace($trackedEnv)) {
            $issues.Add("tracked environment file is present: $relative")
        }

        continue
    }

    try {
        $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
    } catch {
        continue
    }

    $secretPatterns = @(
        '-----BEGIN (?:RSA|EC|OPENSSH|PRIVATE) KEY-----',
        '(?i)\b(?:AKIA|ASIA)[0-9A-Z]{16}\b',
        '(?i)\b(?:ghp|github_pat|glpat)-[A-Za-z0-9_-]{20,}\b',
        '(?i)\bsk-[A-Za-z0-9_-]{20,}\b',
        '(?i)\b(?:password|secret|token|api[_-]?key)\s*[:=]\s*["''](?!<)[^"'']{8,}["'']'
    )

    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern -and $file.Name -ne ".env.example") {
            $issues.Add("possible credential material in $relative")
            break
        }
    }

    $phoneMatches = [regex]::Matches($content, '(?<!\d)(?:\+?1[\s.-]?)?\(?([2-9]\d{2})\)?[\s.-]\d{3}[\s.-]\d{4}(?!\d)')
    foreach ($match in $phoneMatches) {
        if ($match.Groups[1].Value -ne "555") {
            $issues.Add("non-synthetic phone pattern in $relative")
            break
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Sensitive-data repository check passed. No tracked .env, credential literal, or non-555 phone pattern was found."
