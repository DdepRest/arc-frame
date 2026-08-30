# sync-version.ps1 — самоподдержание версии (CONTROL#13)
# Читает версию из MosquitoNetCalculator.csproj (единственный источник истины)
# и вставляет свежую запись в секцию «## Last verified» каждого agents/docs/*.md,
# если последняя запись ещё не содержит текущую версию.
#
# Usage:  powershell -ExecutionPolicy Bypass -File sync-version.ps1 [-DryRun]
#   -DryRun: только показать, что будет обновлено, без записи.

param([switch]$DryRun)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")).Path
$csprojPath  = Join-Path $projectRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"
$docsArcDir  = Join-Path $projectRoot "agents\docs"

# --- 1. Версия из csproj ---
if (-not (Test-Path $csprojPath)) {
    Write-Host "FAIL: csproj not found: $csprojPath" -ForegroundColor Red
    exit 1
}
$csprojContent = [System.IO.File]::ReadAllText($csprojPath)
$m = [regex]::Match($csprojContent, '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
if (-not $m.Success) {
    Write-Host "FAIL: <Version> not found in csproj" -ForegroundColor Red
    exit 1
}
$version = $m.Groups[1].Value
$today = Get-Date -Format "yyyy-MM-dd"
Write-Host "csproj version: $version  (date: $today)" -ForegroundColor Cyan

# --- 2. Обход agents/docs/*.md ---
$mdFiles = @(Get-ChildItem -Path $docsArcDir -Filter "*.md" | Where-Object {
    $_.Name -ne "DOCUMENTATION_MATRIX.md"   # генерируется из JSON
})
$updated = 0
$skipped = 0
foreach ($f in $mdFiles) {
    $content = [System.IO.File]::ReadAllText($f.FullName)   # детектит BOM
    $hadBom  = $content.StartsWith([char]0xFEFF)

    # Ищем заголовок «## Last verified» (якорь на начало строки, как в validate-docs #10)
    $headerMatch = [regex]::Match($content, '(?m)^## Last verified')
    if (-not $headerMatch.Success) {
        $skipped++
        continue
    }
    $sectionStart = $headerMatch.Index + $headerMatch.Length
    $window = $content.Substring($sectionStart, [Math]::Min(1000, $content.Length - $sectionStart))
    # Первая версия внутри окна — та, что сверяет validate-docs #10
    $verMatch = [regex]::Match($window, '\(?(v?\d+\.\d+\.\d+)')
    if ($verMatch.Success -and ($verMatch.Groups[1].Value -replace '^v','') -eq $version) {
        $skipped++
        continue   # уже актуальна
    }

    # Вставляем свежую запись сразу после заголовка (история уезжает вниз)
    $nl = "`r`n"
    $newEntry = $nl + "$today (v$version) — auto-synced from csproj (sync-version.ps1, CONTROL#13)." + $nl
    $content = $content.Substring(0, $sectionStart) + $newEntry + $content.Substring($sectionStart)

    if ($DryRun) {
        Write-Host "  [dry] $($f.Name): will add v$version entry" -ForegroundColor Yellow
    } else {
        $enc = New-Object System.Text.UTF8Encoding($hadBom)
        [System.IO.File]::WriteAllText($f.FullName, $content, $enc)
        Write-Host "  OK  : $($f.Name) -> v$version" -ForegroundColor Green
    }
    $updated++
}
Write-Host ""
Write-Host "Done: $updated updated, $skipped already current/skipped" -ForegroundColor Cyan
if ($updated -gt 0 -and -not $DryRun) {
    Write-Host "Hint: run validate-docs.ps1 to confirm check #10 passes." -ForegroundColor Gray
}
