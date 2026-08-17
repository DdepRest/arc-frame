# Release cleanup + ZIP rebuild for v3.47.2
# Pure ASCII body to avoid PowerShell 5.1 reading Cyrillic
# as Windows-1251 (ParserError). Cyrillic README filename
# is referenced via Get-ChildItem glob.

Set-Location 'C:\Users\Asus\Desktop\A.R.C. Frame\gwga'

Write-Host '[1/9] killing background processes'
Get-Process dotnet,MosquitoNetCalculator,MosquitoNetCalculator.Tests -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 12

Write-Host '[2/9] hard-nuke staging subdirs (by name)'
$stagingNames = @(
    '.full-staging',
    '.manual-staging',
    '.full-tmp',
    '.manual-tmp',
    '.full-z',
    '.manual-z',
    '.staging',
    '.tmp',
    '.tmp-z',
    '.tmp-stage'
)
foreach ($n in $stagingNames) {
    Remove-Item -Path (Join-Path 'publish' $n) -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '[3/9] nuke stale ARC-Frame-*.zip in publish'
$existingZips = Get-ChildItem -Path 'publish' -Filter 'ARC-Frame-*.zip' -ErrorAction SilentlyContinue
foreach ($z in $existingZips) {
    Remove-Item -Path $z.FullName -Force -ErrorAction SilentlyContinue
}

Write-Host '[4/9] nuke *.zip.old'
$oldFiles = Get-ChildItem -Path 'publish' -Filter '*.zip.old' -ErrorAction SilentlyContinue
foreach ($o in $oldFiles) {
    Remove-Item -Path $o.FullName -Force -ErrorAction SilentlyContinue
}

Write-Host '[5/9] verify publish tree recovered (expect ~487 files)'
$fileCount = (Get-ChildItem -Path 'publish' -Recurse -File | Measure-Object).Count
Write-Host ("publish file count: " + $fileCount)

Write-Host '[6/9] check essential files'
Write-Host ("exe present: " + (Test-Path 'publish\MosquitoNetCalculator.exe'))
Write-Host ("prices.json present: " + (Test-Path 'publish\prices.json'))
Write-Host ("check-deps.bat present: " + (Test-Path 'publish\check-deps.bat'))
$readmeFound = Get-ChildItem -Path 'publish' -Filter 'README*.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
Write-Host ("README present: " + ($null -ne $readmeFound))
if ($readmeFound) {
    $bytes = [System.IO.File]::ReadAllBytes($readmeFound.FullName)[0..2]
    Write-Host ("README BOM bytes: " + ($bytes -join ','))
    Write-Host ("README path: " + $readmeFound.FullName)
}

Write-Host '[7/9] build fresh staging dirs + full ZIP'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fullStage = Join-Path 'publish' '.full-staging'
if (Test-Path $fullStage) { Remove-Item $fullStage -Recurse -Force }
New-Item -ItemType Directory -Path $fullStage | Out-Null

foreach ($item in (Get-ChildItem -Path 'publish')) {
    if ($item.Name[0] -eq '.') { continue }
    if ($item.Name.StartsWith('ARC-Frame-')) { continue }
    if ($item.Name -like 'README*.txt') { continue }
    Copy-Item -Path $item.FullName -Destination (Join-Path $fullStage $item.Name) -Recurse -Force -ErrorAction SilentlyContinue
}

$fullStageCount = (Get-ChildItem -Path $fullStage -Recurse -File | Measure-Object).Count
Write-Host ("FULL STAGE files: " + $fullStageCount)

$fullZip = Join-Path 'publish' 'ARC-Frame-3.47.2-full.zip'
if (Test-Path $fullZip) { Remove-Item $fullZip -Force -ErrorAction SilentlyContinue }
[System.IO.Compression.ZipFile]::CreateFromDirectory($fullStage, $fullZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("FULL ZIP SIZE: " + (Get-Item $fullZip).Length + ' bytes')

Write-Host '[8/9] build manual-update staging + ZIP'
$manualStage = Join-Path 'publish' '.manual-staging'
if (Test-Path $manualStage) { Remove-Item $manualStage -Recurse -Force }
New-Item -ItemType Directory -Path $manualStage | Out-Null
Get-ChildItem -Path $fullStage | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $manualStage $_.Name) -Recurse -Force -ErrorAction SilentlyContinue
}
if ($readmeFound) {
    Copy-Item -Path $readmeFound.FullName -Destination (Join-Path $manualStage $readmeFound.Name) -Force -ErrorAction SilentlyContinue
}

$manualStageCount = (Get-ChildItem -Path $manualStage -Recurse -File | Measure-Object).Count
Write-Host ("MANUAL STAGE files: " + $manualStageCount)

$manualZip = Join-Path 'publish' 'ARC-Frame-3.47.2-manual-update.zip'
if (Test-Path $manualZip) { Remove-Item $manualZip -Force -ErrorAction SilentlyContinue }
[System.IO.Compression.ZipFile]::CreateFromDirectory($manualStage, $manualZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("MANUAL ZIP SIZE: " + (Get-Item $manualZip).Length + ' bytes')

Write-Host '[9/9] cleanup staging'
Remove-Item -Path $fullStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $manualStage -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '---FINAL---'
Get-ChildItem -Path 'publish' -Filter 'ARC-Frame-3.47.2-*.zip' | Select-Object Name, Length | Format-Table -AutoSize
Write-Host ("publish file count: " + (Get-ChildItem -Path 'publish' -Recurse -File | Measure-Object).Count)
