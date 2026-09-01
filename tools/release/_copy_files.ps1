$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$publishDir = Join-Path $projectRoot "publish"

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

foreach ($sourceRelative in @(
    "MosquitoNetCalculator\prices.json",
    "MosquitoNetCalculator\Resources\app_icon.ico",
    "check-deps.bat",
    "check-deps.ps1"
)) {
    $source = Join-Path $projectRoot $sourceRelative
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required release file not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination $publishDir -Force
}

Write-Host "Release support files copied to $publishDir" -ForegroundColor Green
