$src = "C:\Users\Asus\Desktop\A.R.C. Frame\gwga\publish"
$zip = Join-Path $src "ARC-Frame-3.47.3-full.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Compressing publish files..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Exclude existing ZIPs, locale folders, fonts
$exclude = @("ARC-Frame-*.zip", "ARC-Frame-*.txt", "LatoFont", "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant")

Get-ChildItem $src -Exclude $exclude | Compress-Archive -DestinationPath $zip -CompressionLevel Optimal

$sw.Stop()
$fi = Get-Item $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Write-Host "DONE: $($fi.Length) bytes in $($sw.Elapsed.TotalSeconds.ToString('N1'))s"
Write-Host "SHA256: $hash"
