param(
    [Parameter(Mandatory)][uri]$LocalApiUrl,
    [Parameter(Mandatory)][string]$FictionalWavePath,
    [Parameter(Mandatory)][string]$FictionalReferencePath,
    [Parameter(Mandatory)][switch]$ConfirmFictionalAudio
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:CI -or -not $ConfirmFictionalAudio -or $LocalApiUrl.Scheme -notin @('http', 'https') -or
    $LocalApiUrl.Host -notin @('127.0.0.1', 'localhost', '[::1]') -or $LocalApiUrl.UserInfo -or $LocalApiUrl.Query) {
    throw 'Live speech evaluation is an explicit local-only fictional-data operation; CI is prohibited.'
}
try {
    $audioFile = Get-Item -LiteralPath $FictionalWavePath
    $referenceFile = Get-Item -LiteralPath $FictionalReferencePath
    if ($audioFile.Extension -ne '.wav' -or $audioFile.Length -lt 44 -or $audioFile.Length -gt 2097152 -or $referenceFile.Length -gt 64000) { throw 'Input bounds rejected.' }
    $reference = Get-Content -LiteralPath $referenceFile.FullName -Raw
    if (-not $reference.StartsWith('SIMULATION:') -or $reference.Length -gt 16000) { throw 'Fictional reference required.' }
    $base = $LocalApiUrl.GetLeftPart([UriPartial]::Authority)
    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    Invoke-RestMethod -Uri "$base/api/v1/dev/session" -Method Post -WebSession $session -ContentType 'application/json' -Body '{"simulationHandle":"sim-operator-jordan"}' | Out-Null
    $caps = Invoke-RestMethod -Uri "$base/api/v1/capabilities" -WebSession $session
    if (-not $caps.simulationOnly -or -not $caps.speechTranscription -or $caps.speechProvider -ne 'AzureSpeech') { throw 'Explicit Azure capability is unavailable.' }
    $inputDraft = @{
        siteId = '11111111-1111-4111-8111-111111111201'; departmentId = '11111111-1111-4111-8111-111111110301'
        simulationPatientReference = 'SIM-PAT-SPEECH-EVALUATION'; location = 'Simulation room'; urgencyLabel = 'DEMO'
        sourceText = 'SIMULATION: local speech evaluation source'; criticalFields = @()
        sbar = @{ situation = 'SIMULATION: pending review'; background = 'SIMULATION: pending review'; assessment = 'SIMULATION: pending review'; recommendation = 'SIMULATION: pending review' }
    }
    $draft = Invoke-RestMethod -Uri "$base/api/v1/alerts/drafts" -Method Post -WebSession $session -ContentType 'application/json' -Body ($inputDraft | ConvertTo-Json -Depth 4)
    $result = Invoke-RestMethod -Uri "$base/api/v1/alerts/$($draft.alertId)/transcriptions" -Method Post -WebSession $session -ContentType 'audio/wav' -InFile $audioFile.FullName -TimeoutSec 35 -Headers @{
        'Idempotency-Key' = [Guid]::NewGuid().ToString('N'); 'X-Alert-Draft-Version' = '1'; 'X-Audio-Language-Hint' = 'en-CA'
    }
    $expected = @([regex]::Matches($reference, '(?<![\p{L}\d])[-+]?\d+(?:[.,:/-]\d+)*') | ForEach-Object Value)
    $observed = [Collections.Generic.List[string]]::new()
    [regex]::Matches($result.transcription.transcript, '(?<![\p{L}\d])[-+]?\d+(?:[.,:/-]\d+)*') | ForEach-Object { $observed.Add($_.Value) }
    $exact = 0
    foreach ($value in $expected) { if ($observed.Remove($value)) { $exact++ } }
    @{ simulationOnly = $true; provider = 'AzureSpeech'; cases = 1; numberExactMatches = $exact; expectedNumbers = $expected.Count
       numberExactMatchRate = $(if ($expected.Count -eq 0) { $null } else { $exact / $expected.Count }); extraNumbers = $observed.Count
       applied = $false } | ConvertTo-Json
} catch {
    throw 'Local speech evaluation failed. Check local configuration and bounded fictional WAV input; no payload or provider diagnostics are printed.'
}

