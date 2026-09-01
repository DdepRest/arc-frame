[CmdletBinding()]
param(
    [string]$ZipPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    throw "Pass -ZipPath with the archive to hash. Example: -ZipPath publish\ARC-Frame-3.48.7-full.zip"
}
$ZipPath = (Resolve-Path $ZipPath).Path
$item = Get-Item -LiteralPath $ZipPath
$hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "ZIP_SHA256=$hash"
Write-Host "ZIP_SIZE=$($item.Length)"
