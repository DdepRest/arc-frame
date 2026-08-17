$zipPath = Join-Path $PSScriptRoot "publish" "ARC-Frame-3.34.4-full.zip"
$zipItem = Get-Item $zipPath
$hash = (Get-FileHash $zipItem.FullName -Algorithm SHA256).Hash
Write-Host "ZIP_SHA256=$hash"
Write-Host "ZIP_SIZE=$($zipItem.Length)"
