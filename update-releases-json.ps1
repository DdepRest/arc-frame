param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [long] $Size,
    [Parameter(Mandatory)] [string] $Sha256
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $PSScriptRoot "releases.json"
$manifestUrl = "https://github.com/$Repository/releases/download/v$Version/ARC-Frame-$Version-full.zip"
$today = Get-Date -Format "yyyy-MM-dd"

# Load existing manifest or create a new one
if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $manifest = [PSCustomObject]@{
        latest = ""
        minRequired = ""
        releases = @()
    }
}

# Remove existing entry for this version (idempotent re-run)
$manifest.releases = @($manifest.releases | Where-Object { $_.version -ne $Version })

# Extract release notes from update-log.json (single source of truth)
$updateLogPath = Join-Path $PSScriptRoot "MosquitoNetCalculator\Resources\update-log.json"
$changes = @()
$type = "Улучшение"
$title = "ARC-Frame $Version"

# Read release info from the JSON file
if (Test-Path $updateLogPath) {
    $allEntries = Get-Content $updateLogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $entry = $allEntries | Where-Object { $_.version -eq $Version } | Select-Object -First 1
    if ($entry) {
        $type   = $entry.type
        $title  = $entry.title
        $changes = @($entry.changes)
    }
}

# Create new release entry
$newRelease = [PSCustomObject]@{
    version = $Version
    date    = $today
    type    = $type
    title   = $title
    changes = $changes
    url     = $manifestUrl
    size    = $Size
    sha256  = $Sha256.ToLowerInvariant()
}

# Prepend new release (newest first)
$manifest.releases = @($newRelease) + @($manifest.releases)
$manifest.latest = $Version

# Save with UTF-8 encoding (no BOM for raw.githubusercontent.com compatibility)
$json = $manifest | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))

Write-Host "Updated releases.json manifest for v$Version"
Write-Host "  Type: $type"
Write-Host "  Title: $title"
Write-Host "  Changes: $($changes.Count) items"
Write-Host "  Size: $Size bytes"
Write-Host "  SHA256: $Sha256"
