[CmdletBinding()]
param(
    [string]$PublishDir = "",
    [string]$ZipName = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $projectRoot "publish"
}
$PublishDir = (Resolve-Path $PublishDir).Path

$exe = Join-Path $PublishDir "MosquitoNetCalculator.exe"
if (Test-Path -LiteralPath $exe) {
    $exeItem = Get-Item -LiteralPath $exe
    Write-Host "EXE SHA256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant())"
    Write-Host "EXE Size: $($exeItem.Length)"
}

if (-not [string]::IsNullOrWhiteSpace($ZipName)) {
    $zip = Join-Path $PublishDir $ZipName
    if (-not (Test-Path -LiteralPath $zip)) {
        throw "ZIP not found: $zip"
    }
    $zipItem = Get-Item -LiteralPath $zip
    Write-Host "ZIP SHA256: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
    Write-Host "ZIP Size: $($zipItem.Length)"
} else {
    Write-Host "No ZipName supplied; EXE metadata only." -ForegroundColor Gray
}
