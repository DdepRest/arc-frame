# rotate-current-state.ps1
# A.R.C. — Rolling Window Policy for CURRENT_STATE.md
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File rotate-current-state.ps1 [-Check] [-MaxVersions 10] [-KeepVersions 5]
#
# Description:
#   Поддерживает компактный размер CURRENT_STATE.md (не более 10 версий/разделов).
#   При превышении лимита старые записи автоматически архивируются в
#   agents/docs/history/CURRENT_STATE_ARCHIVE.md.

[CmdletBinding()]
param(
    [switch]$Check,
    [int]$MaxVersions = 10,
    [int]$KeepVersions = 5
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$currentStatePath = Join-Path $projectRoot "agents\docs\CURRENT_STATE.md"
$archivePath = Join-Path $projectRoot "agents\docs\history\CURRENT_STATE_ARCHIVE.md"

if (-not (Test-Path $currentStatePath)) {
    Write-Host "FAIL: CURRENT_STATE.md not found at $currentStatePath" -ForegroundColor Red
    exit 1
}

$content = Get-Content $currentStatePath -Raw -Encoding UTF8
$lines = $content -split "`r?`n"

# Search for bullet items that represent versions/features
$versionIndices = @()
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^- \*\*(.+?)\*\*') {
        $versionIndices += $i
    }
}

$count = $versionIndices.Count
Write-Host "=== CURRENT_STATE.md Rolling Window Check ===" -ForegroundColor Cyan
Write-Host "Found $count active version/feature entries (Max: $MaxVersions, Target: $KeepVersions)" -ForegroundColor Gray

if ($count -le $MaxVersions) {
    Write-Host "PASS: Current entries ($count) within limit of $MaxVersions. No rotation needed." -ForegroundColor Green
    exit 0
}

if ($Check) {
    Write-Host "WARN: Entries count ($count) exceeds $MaxVersions. Run rotate-current-state.ps1 to archive." -ForegroundColor Yellow
    exit 0
}

Write-Host "Rotating oldest $($count - $KeepVersions) entries to archive..." -ForegroundColor Yellow

# Ensure history directory exists
$historyDir = Split-Path $archivePath -Parent
if (-not (Test-Path $historyDir)) {
    New-Item -ItemType Directory -Path $historyDir -Force | Out-Null
}

# The split index: keep the first $KeepVersions
$splitLine = $versionIndices[$KeepVersions]

# Find the end of the feature list (starts at ## Статус A.R.C. or similar)
$endFeatureIndex = $lines.Length
for ($i = $splitLine; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^##\s+') {
        $endFeatureIndex = $i
        break
    }
}

$keepLines = $lines[0..($splitLine - 1)]
$archiveLines = $lines[$splitLine..($endFeatureIndex - 1)]
$trailingLines = $lines[$endFeatureIndex..($lines.Length - 1)]

# Append to archive
$archiveText = "`r`n`r`n## Archived on $(Get-Date -Format 'yyyy-MM-dd')`r`n`r`n" + ($archiveLines -join "`r`n")
Add-Content -Path $archivePath -Value $archiveText -Encoding UTF8

# Rewrite CURRENT_STATE
$newCurrentState = ($keepLines + $trailingLines) -join "`r`n"
[System.IO.File]::WriteAllText($currentStatePath, $newCurrentState, [System.Text.Encoding]::UTF8)

Write-Host "SUCCESS: Rotated $($count - $KeepVersions) entries to $archivePath" -ForegroundColor Green
