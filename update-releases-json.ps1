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

# Extract release notes from the version's UpdateItem entry (if any)
$updateLogPath = Join-Path $PSScriptRoot "MosquitoNetCalculator\Services\UpdateLog.cs"
$changes = @()
$type = "Улучшение"
$title = "ARC-Frame $Version"

# Fetch changes from UpdateLog.cs for this version
if (Test-Path $updateLogPath) {
    $updateLogText = Get-Content $updateLogPath -Raw -Encoding UTF8
    # Find the UpdateItem block for this version
    $pattern = '(?s)new UpdateItem\s*\{[^}]*?\bVersion\s*=\s*"' + [regex]::Escape($Version) + '"[^}]*?\}'
    $match = [regex]::Match($updateLogText, $pattern)
    if ($match.Success) {
        $block = $match.Value
        # Extract Type
        if ($block -match 'Type\s*=\s*"([^"]+)"') { $type = $matches[1] }
        # Extract Title
        if ($block -match 'Title\s*=\s*"([^"]+)"') { $title = $matches[1] }
        # Extract Changes list
        $changesPattern = '(?s)Changes\s*=\s*new List<string>\s*\{([^}]+)\}'
        if ($block -match $changesPattern) {
            $changesRaw = $matches[1]
            $changeMatches = [regex]::Matches($changesRaw, '"([^"]+)"')
            foreach ($cm in $changeMatches) {
                $changes += $cm.Groups[1].Value
            }
        }
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
