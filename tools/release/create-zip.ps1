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

if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "MosquitoNetCalculator.exe"))) {
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
    $OutputPath = Join-Path $PublishDir "ARC-Frame-$Version-full.zip"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "arc-frame-full-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stage -Force | Out-Null

try {
    $requiredFiles = @("MosquitoNetCalculator.exe")
    foreach ($name in $requiredFiles) {
        Copy-Item -LiteralPath (Join-Path $PublishDir $name) -Destination (Join-Path $stage $name) -Force
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
