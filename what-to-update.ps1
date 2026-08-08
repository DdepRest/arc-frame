# what-to-update.ps1
# Принимает список изменённых файлов (git diff --name-only) и выводит,
# какие docs/arc/*.md нужно обновить согласно documentation-matrix.json.
#
# Использование:
#   what-to-update.ps1 Models/AnwisSize.cs Services/PrintService.cs
#   what-to-update.ps1 $(git diff --name-only)
#   git diff --name-only | what-to-update.ps1
#
# Флаги:
#   -RunAiTests   : после вывода сводки docs реально запустить release-flow
#                   AI-регрессии
#                   (`dotnet test --filter "FullyQualifiedName~AiGoldenCase|
#                   FullyQualifiedName~AiPlan|FullyQualifiedName~AiTelemetry"`).
#                   Требует уже собранные тесты (--no-build; сначала
#                   `dotnet build MosquitoNetCalculator.sln`, если билд
#                   устарел — скрипт НЕ будет собирать сам, чтобы не
#                   вклиниваться в обычный commit-flow).
#
#                   Если в diff есть AI-файлы, предупреждение о фильтре
#                   печатается всегда, даже без флага -RunAiTests.

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments=$true, ValueFromPipeline=$true)]
    [string[]]$ChangedFiles,

    [switch]$RunAiTests
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
    Write-Host ""
    Write-Host "Flags:"
    Write-Host "  -RunAiTests   Запустить AI Agent Mode regression tests после этой сводки"
    exit 1
}

Write-Host "=== What documentation to update ===" -ForegroundColor Cyan
Write-Host ""

$allDocs = @{}
$foundAny = $false
$aiFileTouched = $false

# Patterns that mean "this diff touched the AI Agent Mode surface".
$aiHintPatterns = @(
    'Models/Ai',
    'Services/Ai',
    'ViewModels/AiAssistantViewModel',
    'Controls/AiAssistantControl',
    'Controls/AiApiKeyDialog',
    'MainWindow.AI',
    'Tests/AI/',
    'Tests/Services/Ai',
    'Tests/Models/Ai',
    'Tests/ViewModels/AiAssistantViewModel',
    'Tests/Controls/Ai'
)

foreach ($changed in $ChangedFiles) {
    # Нормализуем путь
    $normalized = $changed -replace '^MosquitoNetCalculator[\\/]', '' -replace '^MosquitoNetCalculator\.Tests[\\/]', ''

    $matched = $false

    foreach ($mapping in $matrix.mappings) {
        $pattern = $mapping.file
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

    foreach ($hint in $aiHintPatterns) {
        if ($normalized -like "*$hint*" -or $changed -like "*$hint*") {
            $aiFileTouched = $true
            break
        }
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

# ============================================================
# AI Agent Mode regression suite (release-flow integration)
# ============================================================
$aiFilter = 'FullyQualifiedName~AiGoldenCase|FullyQualifiedName~AiPlan|FullyQualifiedName~AiTelemetry'
$aiTestCmd = "dotnet test --no-build --nologo --filter `"$aiFilter`""

Write-Host ""
Write-Host "=== AI Agent Mode — regression suite ===" -ForegroundColor Cyan
if ($aiFileTouched) {
    Write-Host "Обнаружены AI-файлы в diff — рекомендуется прогнать AI-фильтр." -ForegroundColor Yellow
} else {
    Write-Host "AI-файлов в diff не обнаружено — фильтр всё равно полезен перед релизом." -ForegroundColor Gray
}
Write-Host ""
Write-Host "Рекомендованная команда (копируй вручную или используй -RunAiTests):" -ForegroundColor Cyan
Write-Host "  $aiTestCmd" -ForegroundColor White
Write-Host ""

if (-not $RunAiTests) {
    Write-Host "Флаг -RunAiTests не указан — реального запуска dotnet test не будет." -ForegroundColor DarkGray
    Write-Host "  what-to-update.ps1 -RunAiTests `$changedFiles" -ForegroundColor DarkGray
    exit 0
}

# Реальный запуск. Требует уже собранный Debug-билд.
$slnPath = Join-Path $projectRoot "MosquitoNetCalculator.sln"
if (-not (Test-Path $slnPath)) {
    Write-Host "FAIL: $slnPath not found" -ForegroundColor Red
    exit 1
}

Write-Host "=== Запуск AI-регрессий (~30-60 секунд) ===" -ForegroundColor Cyan
Write-Host "Filter: $aiFilter" -ForegroundColor Gray
Write-Host ""

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "test --no-build --nologo --filter `"$aiFilter`""
$psi.WorkingDirectory = $projectRoot
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$p = [System.Diagnostics.Process]::Start($psi)
# Block the main thread until both output streams are fully drained.
# .GetAwaiter().GetResult() returns the actual String, not a Task, so we
# don't have to worry about Task.Result being null.
$stdoutText = $p.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult()
$stderrText = $p.StandardError.ReadToEndAsync().GetAwaiter().GetResult()
$p.WaitForExit()

if ($null -eq $stdoutText) { $stdoutText = "" }
if ($null -eq $stderrText) { $stderrText = "" }
$stdoutText = [string]$stdoutText
$stderrText = [string]$stderrText

$combined = $stdoutText + "`n" + $stderrText

# Показываем весь stdout
Write-Host $stdoutText
if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
    Write-Host $stderrText -ForegroundColor DarkYellow
}

# Несколько исходов, на которые оператор должен реагировать:
# (1) dotnet test не нашёл тестов по фильтру — обычно значит, что
#     apply-locally не донёс AI Agent Mode test files до main checkout.
#     Считаем warning (exit 0), чтобы не блокировать обычный commit-flow.
# (2) dotnet test нашёл и прогнал — но упал один или несколько кейсов.
#     Тогда ExitCode != 0 — это серьёзный FAIL.
# If dotnet test finds 0 tests for our filter, the very same filter string
# is echoed back in the failure message. If tests ran, the filter string
# does NOT reappear in output (only summary lines do). This is the most
# reliable indicator because Cyrillic in dotnet output may be re-encoded
# differently across terminals, but the filter token byte sequence is stable.
$filterToken = 'FullyQualifiedName~AiGoldenCase'
$noTestsMatch = $combined.Contains($filterToken)

# summaryLine — последняя строка, в которой есть dotnet test summary keywords
$summaryCandidates = $combined -split "`n" | Where-Object {
    $_ -match 'Пройдено|Провалено|Всего тестов|Total tests|Passed!|Failed!|Не пройден|^Пройдено|^Failed'
}
$summaryLine = $null
if ($summaryCandidates -and $summaryCandidates.Count -gt 0) {
    $summaryLine = ($summaryCandidates | Select-Object -Last 1).Trim()
}

# Дополнительно: чёткий pass/fail сигнал для release-flow.
$hasPassed = $combined -match 'Пройдено!|Passed!|Пройден тест'
$hasFailed = $combined -match 'Провалено!|Failed!|Тесты не пройдены|Не пройден' -or ($p.ExitCode -ne 0)

Write-Host ""
if ($noTestsMatch) {
    Write-Host "WARNING: dotnet test нашёл 0 тестов по фильтру AiGoldenCase|AiPlan|AiTelemetry." -ForegroundColor Yellow
    Write-Host "  Возможные причины:" -ForegroundColor Yellow
    Write-Host "    — AI Agent Mode test files (AiGoldenCaseTests, AiPlan*, AiTelemetry*) не доехали до main checkout" -ForegroundColor Yellow
    Write-Host "    — Решение: примените worktree через apply-locally или соберите Debug ('dotnet build MosquitoNetCalculator.sln')" -ForegroundColor Yellow
    if ($summaryLine) {
        Write-Host "  dotnet summary: $summaryLine" -ForegroundColor DarkYellow
    }
    exit 0
}

if ($p.ExitCode -ne 0) {
    Write-Host "FAIL: AI regression tests exited with code $($p.ExitCode)" -ForegroundColor Red
    if ($summaryLine) {
        Write-Host "  dotnet summary: $summaryLine" -ForegroundColor DarkYellow
    }
    exit $p.ExitCode
}

Write-Host ""
Write-Host "OK: AI regression tests passed." -ForegroundColor Green
if ($summaryLine) {
    Write-Host "  dotnet summary: $summaryLine" -ForegroundColor Gray
}
exit 0
