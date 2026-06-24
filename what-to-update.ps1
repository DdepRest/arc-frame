# what-to-update.ps1
# Принимает список изменённых файлов (git diff --name-only) и выводит,
# какие docs/arc/*.md нужно обновить согласно documentation-matrix.json.
#
# Использование:
#   what-to-update.ps1 Models/AnwisSize.cs Services/PrintService.cs
#   what-to-update.ps1 $(git diff --name-only)
#   git diff --name-only | what-to-update.ps1

param(
    [Parameter(ValueFromRemainingArguments=$true, ValueFromPipeline=$true)]
    [string[]]$ChangedFiles
)

$ErrorActionPreference = "Continue"
$projectRoot = $PSScriptRoot

$matrixPath = Join-Path $projectRoot "docs\arc\documentation-matrix.json"
if (-not (Test-Path $matrixPath)) {
    Write-Host "FAIL: documentation-matrix.json not found at $matrixPath" -ForegroundColor Red
    exit 1
}

$matrix = Get-Content $matrixPath -Raw -Encoding UTF8 | ConvertFrom-Json

if (@($ChangedFiles).Count -eq 0) {
    Write-Host "Usage: what-to-update.ps1 <changed-file> [changed-file ...]"
    Write-Host "       what-to-update.ps1 `$(git diff --name-only)"
    Write-Host "       git diff --name-only | what-to-update.ps1"
    exit 1
}

Write-Host "=== What documentation to update ===" -ForegroundColor Cyan
Write-Host ""

$allDocs = @{}
$foundAny = $false

foreach ($changed in $ChangedFiles) {
    # Нормализуем путь: убираем возможный префикс MosquitoNetCalculator/ или MosquitoNetCalculator.Tests/
    $normalized = $changed -replace '^MosquitoNetCalculator[\\/]', '' -replace '^MosquitoNetCalculator\.Tests[\\/]', ''

    $matched = $false

    foreach ($mapping in $matrix.mappings) {
        $pattern = $mapping.file
        # Поддержка glob-паттернов (*.Tests.cs, *.xaml, Controls/*.*)
        if ($pattern.Contains('*')) {
            $regex = '^' + [regex]::Escape($pattern).Replace('\*', '.*') + '$'
            if ($normalized -match $regex) {
                $matched = $true
                $foundAny = $true
                Write-Host "Changed: $changed" -ForegroundColor Yellow
                Write-Host "  Matched pattern: $pattern" -ForegroundColor Gray
                if ($mapping.note) { Write-Host "  Note: $($mapping.note)" -ForegroundColor Gray }
                Write-Host "  → Update:" -ForegroundColor Green
                foreach ($doc in $mapping.docs) {
                    Write-Host "      $doc" -ForegroundColor Green
                    $allDocs[$doc] = $true
                }
                Write-Host ""
            }
        } else {
            # Точное совпадение или suffix match
            if ($normalized -eq $pattern -or $normalized -like "*\$pattern") {
                $matched = $true
                $foundAny = $true
                Write-Host "Changed: $changed" -ForegroundColor Yellow
                if ($mapping.note) { Write-Host "  Note: $($mapping.note)" -ForegroundColor Gray }
                Write-Host "  → Update:" -ForegroundColor Green
                foreach ($doc in $mapping.docs) {
                    Write-Host "      $doc" -ForegroundColor Green
                    $allDocs[$doc] = $true
                }
                Write-Host ""
            }
        }
    }

    if (-not $matched) {
        Write-Host "Changed: $changed" -ForegroundColor DarkGray
        Write-Host "  → No docs mapping found (may be safe to skip)" -ForegroundColor DarkGray
        Write-Host ""
    }
}

if (-not $foundAny) {
    Write-Host "No documentation updates required for the given files." -ForegroundColor Gray
} else {
    Write-Host "=== Summary: all docs to update ===" -ForegroundColor Cyan
    $sorted = $allDocs.Keys | Sort-Object
    foreach ($doc in $sorted) {
        Write-Host "  docs/arc/$doc" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "Also always update: CHANGELOG.md (if this is a user-facing change)" -ForegroundColor Cyan
}

exit 0
