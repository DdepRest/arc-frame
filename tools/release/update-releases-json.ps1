[CmdletBinding()]
param(
    [string]$Repository = "DdepRest/arc-frame",
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$Size,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$Sha256,
    [string]$MirrorUrl = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$manifestPath = Join-Path $projectRoot "releases.json"
$updateLogPath = Join-Path $projectRoot "MosquitoNetCalculator\Resources\update-log.json"
$manifestUrl = "https://github.com/$Repository/releases/download/v$Version/ARC-Frame-$Version-full.zip"
$today = Get-Date -Format "yyyy-MM-dd"

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "releases.json not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $manifest.releases -or @($manifest.releases).Count -eq 0) {
    throw "releases.json has no release entries to update. Add the hand-written release entry first."
}

$entry = @($manifest.releases | Where-Object { $_.version -eq $Version }) | Select-Object -First 1
if ($null -eq $entry) {
    throw "No hand-written release entry for v$Version. Add version/title/type/changes before publishing."
}

if (Test-Path -LiteralPath $updateLogPath) {
    $allEntries = @(Get-Content -LiteralPath $updateLogPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $logEntry = $allEntries | Where-Object { $_.version -eq $Version } | Select-Object -First 1
    if ($null -ne $logEntry) {
        # The generated log is a consistency check only; committed manifest text
        # remains authoritative so CI never overwrites editorial release notes.
        Write-Host "Found matching update-log entry for v$Version" -ForegroundColor Gray
    }
}

$entry.url = $manifestUrl
$entry.size = $Size
$entry.sha256 = $Sha256.ToLowerInvariant()
$entry.date = $today
if (-not $entry.PSObject.Properties['mirrorUrl']) {
    $entry | Add-Member -NotePropertyName mirrorUrl -NotePropertyValue $MirrorUrl
} else {
    $entry.mirrorUrl = $MirrorUrl
}

$manifest.latest = $Version
$json = $manifest | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $json, $utf8NoBom)

Write-Host "Updated releases.json manifest for v$Version" -ForegroundColor Green
Write-Host "  URL: $manifestUrl"
Write-Host "  Size: $Size bytes"
Write-Host "  SHA256: $($Sha256.ToLowerInvariant())"
Write-Host "  Mirror: $(if ($MirrorUrl) { $MirrorUrl } else { 'none' })"
