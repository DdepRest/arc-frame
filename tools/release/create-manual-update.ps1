[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$PublishDir = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $projectRoot "publish"
} else {
    $PublishDir = (Resolve-Path $PublishDir).Path
}

$exePath = Join-Path $PublishDir "MosquitoNetCalculator.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "MosquitoNetCalculator.exe not found in $PublishDir. Run build/publish first."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $csprojPath = Join-Path $projectRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"
    $Version = (& dotnet msbuild $csprojPath -getProperty:Version -nologo 2>$null | Select-Object -Last 1).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Could not resolve a valid version; got '$Version'."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PublishDir "ARC-Frame-$Version-manual-update.zip"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$readmePath = Join-Path $PublishDir "README_ОБНОВЛЕНИЕ.txt"
$readmeContent = @"
============================================================
  РУЧНОЕ ОБНОВЛЕНИЕ MosquitoNetCalculator (ARC-Frame)
  Версия: $Version
============================================================

1. Закройте MosquitoNetCalculator.
2. Распакуйте содержимое этого ZIP-архива в папку программы
   с заменой файлов.
3. Запустите MosquitoNetCalculator.exe.

Ваши заказы, настройки и цены сохраняются в
%AppData%\MosquitoNetCalculator\ и не входят в этот архив.

Если программа не запускается, запустите check-deps.bat
для диагностики системных зависимостей.
============================================================
"@
[System.IO.File]::WriteAllText($readmePath, $readmeContent, (New-Object System.Text.UTF8Encoding($true)))

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "arc-frame-manual-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stage -Force | Out-Null

try {
    foreach ($name in @("MosquitoNetCalculator.exe", "README_ОБНОВЛЕНИЕ.txt", "check-deps.bat", "check-deps.ps1")) {
        $source = Join-Path $PublishDir $name
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name) -Force
        }
    }

    foreach ($dll in (Get-ChildItem -LiteralPath $PublishDir -Filter "*.dll" -File)) {
        Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $stage $dll.Name) -Force
    }

    foreach ($directoryName in @("tessdata", "x64", "x86", "runtimes")) {
        $source = Join-Path $PublishDir $directoryName
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $stage $directoryName) -Recurse -Force
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage,
        $OutputPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $item = Get-Item -LiteralPath $OutputPath
    $hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Created: $OutputPath" -ForegroundColor Green
    Write-Host "Size: $($item.Length) bytes"
    Write-Host "SHA256: $hash"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
