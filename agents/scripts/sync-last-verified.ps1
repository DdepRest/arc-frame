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
#                          в индекс кладётся ТОЛЬКО строка записи, границы
#                          индекс/рабочее дерево не трогаются — GOTCHAS §41)
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

function Test-Utf8Bom {
    # Не «$text.StartsWith([char]0xFEFF)»: в Windows PowerShell 5.1 этот вызов врёт —
    # на строке БЕЗ BOM возвращает True (замерено: первый символ '#', а ответ True,
    # потому что char приводится к пустой строке и StartsWith("") истинно для всего).
    # Цена ошибки не космическая, но видимая: файл без BOM получал его при записи,
    # и в коммит уезжала лишняя правка первой строки (так BOM появился в agents/README.md
    # в коммите 9bcf1b7 — GOTCHAS §41).
    param([string]$Text)
    return ($Text.Length -gt 0 -and ([int]$Text[0]) -eq 0xFEFF)
}

# --- 2.5 Вставка записи и точная правка ИНДЕКСА (для режима -Staged) ---------
# Вставка строки живёт в ОДНОЙ функции на файл и на индекс: если правило разъедется,
# рабочее дерево и индекс разойдутся молча.
function Add-LastVerifiedStamp {
    param([string]$Text, [string]$Date, [string]$Stamp)
    # Status: ok — текст изменён; same — первая запись уже с этой датой; nosection.
    $m = [regex]::Match($Text, '(?m)^## Last verified')
    if (-not $m.Success) { return @{ Status = "nosection"; Text = $Text; FirstDate = $null } }
    $start = $m.Index + $m.Length
    $window = $Text.Substring($start, [Math]::Min(400, $Text.Length - $start))
    $dm = [regex]::Match($window, '(\d{4}-\d{2}-\d{2})')
    $firstDate = if ($dm.Success) { $dm.Groups[1].Value } else { $null }
    if ($firstDate -eq $Date) { return @{ Status = "same"; Text = $Text; FirstDate = $firstDate } }
    # Концы строк — как в самом тексте: часть agents/docs живёт на LF, часть на CRLF,
    # и вставка чужих переводов строк дала бы смешанные окончания (git это правит
    # на коммите, но diff и текстовые инструменты — нет).
    $nl = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    return @{ Status = "ok"; FirstDate = $firstDate
              Text = $Text.Substring(0, $start) + $nl + $Stamp + $nl + $Text.Substring($start) }
}

# Индекс — отдельная версия документа, и она может отличаться от рабочей. Поэтому
# раньше здесь стоял «git add -- $rel», и он клам в индекс ВСЁ содержимое рабочего
# файла: незастейдженные правки уезжали в чужой коммит, а после коммита рабочее
# дерево оказывалось чистым (замерено сценарием staged A + unstaged B — GOTCHAS §41).
# Теперь в индекс попадает РОВНО строка записи: берём содержимое индекса, вставляем
# строку, кладём blob через plumbing — границы индекс/рабочее дерево не трогаются.
function Invoke-GitRaw {
    # Содержимое документа — UTF-8 (с BOM), и через строку PowerShell оно прошло бы
    # через кодовую страницу консоли: кириллица превратилась бы в мусор, а хеш — в чужой blob.
    param([string]$Arguments, [byte[]]$Stdin)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = $Arguments
    if ($null -ne $Stdin) { $psi.RedirectStandardInput = $true }
    $p = [System.Diagnostics.Process]::Start($psi)
    if ($null -ne $Stdin) {
        $p.StandardInput.BaseStream.Write($Stdin, 0, $Stdin.Length)
        $p.StandardInput.BaseStream.Flush()
        $p.StandardInput.Close()
    }
    $ms = New-Object System.IO.MemoryStream
    $p.StandardOutput.BaseStream.CopyTo($ms)
    $null = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { return $null }
    ,$ms.ToArray()
}

function Add-StampToIndex {
    param([string]$Rel)
    $blob = Invoke-GitRaw "show `":$Rel`""
    if ($null -eq $blob) { Write-Warning "  индекс: не читается $Rel"; return $false }
    $indexText = [System.Text.Encoding]::UTF8.GetString($blob)
    $hadBom = Test-Utf8Bom $indexText

    $res = Add-LastVerifiedStamp -Text $indexText -Date $Date -Stamp $stamp
    if ($res.Status -eq "same") { return $true }   # индекс уже с этой датой
    if ($res.Status -eq "nosection") {
        Write-Host "  SKIP: $Rel — в индексе нет секции «## Last verified»" -ForegroundColor Yellow
        return $true
    }

    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($res.Text)
    if ($hadBom) { $bytes = [byte[]]([byte[]]@(0xEF, 0xBB, 0xBF) + $bytes) }   # GetBytes BOM не пишет

    $shaBytes = Invoke-GitRaw "hash-object -w --stdin" $bytes
    if ($null -eq $shaBytes) { Write-Warning "  индекс: git hash-object не сработал ($Rel)"; return $false }
    $sha = ([System.Text.Encoding]::ASCII.GetString($shaBytes)).Trim()
    if ($sha -notmatch '^[0-9a-f]{40,64}$') { Write-Warning "  индекс: неожиданный ответ git ($Rel)"; return $false }

    $lsLine = @(& git -C $projectRoot ls-files -s -- $Rel 2>$null | Select-Object -First 1)
    if (-not $lsLine) { Write-Warning "  индекс: $Rel не найден"; return $false }
    $mode = ($lsLine -split '\s+')[0]

    & git -C $projectRoot update-index --cacheinfo "$mode,$sha,$Rel" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning "  индекс: update-index не сработал ($Rel)"; return $false }
    return $true
}

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
$stagedCount = 0   # не $staged: PowerShell не различает регистр и $Staged — это сам switch
$alreadyToday = 0
$noSection = 0
$stale = @()

foreach ($rel in $targets) {
    $path = Join-Path $projectRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) { Write-Warning "нет файла: $rel"; continue }

    $content = [System.IO.File]::ReadAllText($path)          # с BOM, если он есть
    $hadBom = Test-Utf8Bom $content

    $wt = Add-LastVerifiedStamp -Text $content -Date $Date -Stamp $stamp
    if ($wt.Status -eq "nosection") {
        $noSection++
        if ($Check) { $stale += "$rel — нет секции «## Last verified»" }
        else { Write-Host "  SKIP: $rel — нет секции «## Last verified»" -ForegroundColor Yellow }
        continue
    }

    if ($Check) {
        $gitDate = (@(& git -C $projectRoot log -1 --format=%as -- $rel 2>$null) -join '').Trim()
        if ($wt.FirstDate -and $gitDate -and $wt.FirstDate -lt $gitDate) {
            $stale += "$rel — документ говорит $($wt.FirstDate), последний коммит $gitDate"
        }
        continue
    }

    if ($wt.Status -eq "same") { $alreadyToday++; continue }   # идемпотентно

    if ($DryRun) {
        Write-Host "  [dry] $rel — добавит «$stamp»" -ForegroundColor Yellow
        $updated++
        continue
    }

    [System.IO.File]::WriteAllText($path, $wt.Text, (New-Object System.Text.UTF8Encoding($hadBom)))
    Write-Host "  OK  : $rel -> $Date" -ForegroundColor Green

    # Коммит видит правку: в индекс уходит ТОЛЬКО строка записи (см. Add-StampToIndex).
    if ($Staged) {
        if (Add-StampToIndex -Rel $rel) { $stagedCount++ }
        else { Write-Warning "  индекс: строку записи не удалось застейджить ($rel)" }
    }
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
if ($Staged) {
    Write-Host "Индекс: строка записи застейджена в $stagedCount файлах; прочие правки не тронуты." -ForegroundColor Gray
}
if ($updated -gt 0 -and -not $DryRun) {
    Write-Host "Hint: validate-docs.ps1 #7 должен пройти (дата = день коммита)." -ForegroundColor Gray
}
