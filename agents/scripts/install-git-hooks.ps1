# install-git-hooks.ps1 — локальные git-хуки репозитория
#
# Ставит pre-commit, который вызывает sync-last-verified.ps1 -Staged: при коммите
# дата «Last verified» в тронутых agents/docs проставляется САМА (днём коммита), а
# в индекс уходит только эта строка (незастейдженные правки в коммит не попадают —
# GOTCHAS §41). Ручной шаг «правка документа → не забыть дату» больше не нужен,
# и validate-docs.ps1 #7 не ругается (GOTCHAS §40).
#
# Usage: powershell -ExecutionPolicy Bypass -File install-git-hooks.ps1 [-Force] [-DryRun]
#   -Force   — перезаписать чужой pre-commit (по умолчанию скрипт откажется)
#   -DryRun  — показать путь и содержимое, ничего не писать
#
# Удалить хук: удалить файл <git-dir>/hooks/pre-commit.
# Хуки НЕ версионируются: после клона или в новом worktree запусти этот скрипт заново.

param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

$hookRel = (@(& git -C $projectRoot rev-parse --git-path hooks/pre-commit 2>$null) -join '').Trim()
if (-not $hookRel) { Write-Error "Не удалось определить путь хуков (git rev-parse не сработал)."; exit 1 }
$hookPath = if ([System.IO.Path]::IsPathRooted($hookRel)) { $hookRel } else { Join-Path $projectRoot $hookRel }

$marker = "A.R.C. pre-commit: sync-last-verified.ps1"
$script = @"
#!/bin/sh
# $marker (установлен agents/scripts/install-git-hooks.ps1)
# Дату «Last verified» в agents/docs ставит сам скрипт (днём коммита) и кладёт в
# индекс только эту строку — незастейдженные правки документа не трогая.
if command -v powershell >/dev/null 2>&1; then
    powershell -NoProfile -ExecutionPolicy Bypass -File "agents/scripts/sync-last-verified.ps1" -Staged || exit 1
fi
exit 0
"@
$script = $script -replace "`r`n", "`n"   # хук исполняет sh — только LF

if ((Test-Path -LiteralPath $hookPath) -and -not $Force) {
    $existing = [System.IO.File]::ReadAllText($hookPath)
    if ($existing -notmatch [regex]::Escape($marker)) {
        Write-Host "PRE-EXISTING: $hookPath уже есть и поставлен не этим скриптом." -ForegroundColor Yellow
        Write-Host "  Посмотри его и перезапусти с -Force, если он не нужен." -ForegroundColor Yellow
        exit 1
    }
}

if ($DryRun) {
    Write-Host "[dry] $hookPath" -ForegroundColor Yellow
    Write-Host $script -ForegroundColor Gray
    exit 0
}

$hookDir = Split-Path -Parent $hookPath
if (-not (Test-Path -LiteralPath $hookDir)) { New-Item -ItemType Directory -Force -Path $hookDir | Out-Null }
[System.IO.File]::WriteAllText($hookPath, $script, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "OK: установлен $hookPath" -ForegroundColor Green
Write-Host "Теперь при коммите дата «Last verified» обновляется автоматически." -ForegroundColor Gray
