# sync-last-verified.ps1 — «Last verified» обновляется вместе с правкой документа (CONTROL#13)
#
# Боль, которую закрывает: правишь agents/docs/*.md — validate-docs.ps1 #7 ругается
# «STALE: MODULES.md - doc says 2026-09-14, git says 2026-09-21», и дату приходится
# проставлять руками. sync-version.ps1 закрывает только релизный случай (версия в
# csproj сменилась): при правке текста версия та же, и документ молча пропускается.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File sync-last-verified.ps1
#     Изменённые (staged/unstaged/untracked) agents/docs/*.md — вставить сегодняшнюю
#     дату первой записью в секцию «## Last verified».
#   ... -Staged          — только файлы из индекса (этот режим ставит git pre-commit:
#                          после правки файл заново кладётся в индекс)
#   ... -All             — все agents/docs/*.md (кроме генерируемых DOCUMENTATION_MATRIX.md
#                          и SYMBOL_INDEX.md — у них секция «## Last generated», а не Last verified)
#   ... -Files a.md,b.md — явный список
#   ... -Note "текст"    — текст записи (по умолчанию — generic-запись скрипта)
#   ... -Date 2026-09-21 — дата записи (по умолчанию сегодня)
#   ... -DryRun          — показать, что будет сделано, без записи
#   ... -Check           — ничего не писать: exit 1, если дата документа старее
#                          последнего коммита файла (семантика validate-docs.ps1 #7;
#                          сравнение с датой КОММИТА, а не правки — см. GOTCHAS §40)
#
# Идемпотентен: если первая запись секции уже с нужной датой, файл не трогается.

param(
    [switch]$Staged,
    [switch]$All,
    [string[]]$Files,
    [string]$Note,
    [string]$Date,
    [switch]$DryRun,
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$docsArcDir = Join-Path $projectRoot "agents\docs"
$csprojPath = Join-Path $projectRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"

# --- 1. Версия из csproj (единственный источник истины) ---
$version = ""
if (Test-Path -LiteralPath $csprojPath) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($csprojPath), '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
    if ($m.Success) { $version = $m.Groups[1].Value }
}
if (-not $version) { Write-Warning "Версия не найдена в csproj — запись будет без версии." }

# --- 2. Дата и текст записи ---
if (-not $Date) { $Date = Get-Date -Format "yyyy-MM-dd" }
if (-not $Note) { $Note = "обновлено содержимое (sync-last-verified.ps1, CONTROL#13)." }
$stamp = if ($version) { "$Date (v$version) — $Note" } else { "$Date — $Note" }

# --- 3. Набор файлов ---
function Get-DocFiles {
    if ($Files) {
        # "-Files a.md,b.md" приходит ОДНОЙ строкой, "-Files a.md, b.md" — массивом:
        # принимаем обе формы.
        return @($Files | ForEach-Object { $_ -split ',' } |
                 ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    if ($All) {
        return @(Get-ChildItem -Path $docsArcDir -Filter "*.md" |
                 Where-Object { $_.Name -notin @("DOCUMENTATION_MATRIX.md", "SYMBOL_INDEX.md") } |
                 ForEach-Object { "agents/docs/$($_.Name)" })
    }

    if ($Staged) {
        $raw = @(& git -C $projectRoot diff --cached --name-only --diff-filter=ACM 2>$null)
    } else {
        # Изменённые, но ещё не закоммиченные (включая новые): X/Y <path>
        $raw = @(& git -C $projectRoot status --porcelain 2>$null | ForEach-Object {
            $p = $_.Substring(3)
            if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1] }   # переименование
            $p.Trim('"')
        })
    }

    return @($raw | Where-Object { $_ } |
             Where-Object { $_ -match '^agents/docs/[^/]+\.md$' } |
             Where-Object { $_ -notmatch '(DOCUMENTATION_MATRIX|SYMBOL_INDEX)\.md$' } |
             Sort-Object -Unique)
}

$targets = Get-DocFiles
if ($targets.Count -eq 0) {
    if ($Check) { Write-Host "Нет изменённых документов — проверять нечего." -ForegroundColor Gray; exit 0 }
    Write-Host "Нет изменённых agents/docs/*.md — даты в порядке." -ForegroundColor Gray
    exit 0
}

$updated = 0
$alreadyToday = 0
$noSection = 0
$stale = @()

foreach ($rel in $targets) {
    $path = Join-Path $projectRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) { Write-Warning "нет файла: $rel"; continue }

    $content = [System.IO.File]::ReadAllText($path)          # с BOM, если он есть
    $hadBom = $content.StartsWith([char]0xFEFF)

    $headerMatch = [regex]::Match($content, '(?m)^## Last verified')
    if (-not $headerMatch.Success) {
        $noSection++
        if ($Check) { $stale += "$rel — нет секции «## Last verified»" }
        else { Write-Host "  SKIP: $rel — нет секции «## Last verified»" -ForegroundColor Yellow }
        continue
    }

    $sectionStart = $headerMatch.Index + $headerMatch.Length
    $window = $content.Substring($sectionStart, [Math]::Min(400, $content.Length - $sectionStart))
    $dateMatch = [regex]::Match($window, '(\d{4}-\d{2}-\d{2})')
    $firstDate = if ($dateMatch.Success) { $dateMatch.Groups[1].Value } else { $null }

    if ($Check) {
        $gitDate = (@(& git -C $projectRoot log -1 --format=%as -- $rel 2>$null) -join '').Trim()
        if ($firstDate -and $gitDate -and $firstDate -lt $gitDate) {
            $stale += "$rel — документ говорит $firstDate, последний коммит $gitDate"
        }
        continue
    }

    if ($firstDate -eq $Date) { $alreadyToday++; continue }   # идемпотентно

    if ($DryRun) {
        Write-Host "  [dry] $rel — добавит «$stamp»" -ForegroundColor Yellow
        $updated++
        continue
    }

    # Концы строк — как в самом файле: часть agents/docs живёт на LF, часть на CRLF,
    # и вставка чужих переводов строк дала бы смешанные окончания (git это правит
    # на коммите, но diff и текстовые инструменты — нет).
    $nl = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
    $content = $content.Substring(0, $sectionStart) + $nl + $stamp + $nl + $content.Substring($sectionStart)
    [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($hadBom)))
    Write-Host "  OK  : $rel -> $Date" -ForegroundColor Green
    if ($Staged) { & git -C $projectRoot add -- $rel | Out-Null }   # коммит видит правку
    $updated++
}

if ($Check) {
    if ($stale.Count -eq 0) {
        Write-Host "PASS: даты «Last verified» не отстают от коммитов ($($targets.Count) файлов)." -ForegroundColor Green
        exit 0
    }
    Write-Host "STALE: дату надо поставить (запусти скрипт без -Check):" -ForegroundColor Yellow
    $stale | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host ""
Write-Host "Готово: обновлено $updated, уже с датой $Date — $alreadyToday, без секции — $noSection." -ForegroundColor Cyan
if ($updated -gt 0 -and -not $DryRun) {
    Write-Host "Hint: validate-docs.ps1 #7 должен пройти (дата = день коммита)." -ForegroundColor Gray
}
