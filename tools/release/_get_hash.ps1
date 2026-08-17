$exe = Join-Path $PSScriptRoot "publish" "MosquitoNetCalculator.exe"
$zip = Join-Path $PSScriptRoot "publish" "ARC-Frame-3.34.4-full.zip"

$exeSize = (Get-Item $exe).Length
$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "EXE SHA256: $exeHash"
Write-Host "EXE Size: $exeSize"

if (Test-Path $zip) {
    $zipSize = (Get-Item $zip).Length
    $zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Write-Host "ZIP SHA256: $zipHash"
    Write-Host "ZIP Size: $zipSize"
} else {
    Write-Host "ZIP not found at $zip"
}
