# generate-update-log.ps1
# Генерирует MosquitoNetCalculator/Resources/update-log.json из CHANGELOG.md.
# Запускать из любого каталога при подготовке релиза.
#
# Использование:
#   powershell -ExecutionPolicy Bypass -File tools/release/generate-update-log.ps1
#
# ПОЛИТИКА ТЕКСТА (почему так, а не иначе — GOTCHAS §36 и §39):
#   * CHANGELOG.md — технический журнал для разработчика (имена файлов, замеры,
#     ссылки на GOTCHAS, счётчики тестов).
#   * update-log.json читает ПОЛЬЗОВАТЕЛЬ: окно «Что нового» после обновления и
#     вкладка «Обновления». Формулировки там — курируемые (написаны человеком
#     для человека), и скрипт НЕ имеет права затирать их техническим текстом
#     из CHANGELOG (регрессия 3.53.0: в «Что нового» уехал весь технический разбор).
#   * Поэтому: версия и ДАТА всегда из CHANGELOG (там финализируется дата релиза),
#     а title/changes/type — из json, если запись этой версии в json уже есть.
#   * Запись, которой в json нет, создаётся из CHANGELOG как ЧЕРНОВИК, и её
#     обязан вычитать человек: техжаргон в пользовательской записи роняет страж
#     MosquitoNetCalculator.Tests/Services/UpdateLogVoiceTests.cs.

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

$changelogPath = Join-Path $projectRoot "CHANGELOG.md"
$updateLogPath = Join-Path $projectRoot "MosquitoNetCalculator\Resources\update-log.json"

if (-not (Test-Path -LiteralPath $changelogPath)) {
    Write-Error "CHANGELOG.md not found: $changelogPath"
    exit 1
}

$content = [System.IO.File]::ReadAllText($changelogPath, [System.Text.Encoding]::UTF8)

# Парсим секции версий: ## X.Y.Z — YYYY-MM-DD
$versionPattern = '##\s+(\d+\.\d+\.\d+)\s*[—–-]\s*(\d{4}-\d{2}-\d{2})'
$versionMatches = [regex]::Matches($content, $versionPattern)
$updates = @()

for ($i = 0; $i -lt $versionMatches.Count; $i++) {
    $v = $versionMatches[$i]
    $version = $v.Groups[1].Value
    $date = $v.Groups[2].Value

    $startIndex = $v.Index + $v.Length
    $endIndex = if ($i -lt $versionMatches.Count - 1) {
        $versionMatches[$i + 1].Index
    } else {
        $content.Length
    }
    $section = $content.Substring($startIndex, $endIndex - $startIndex)

    $title = ""
    if ($section -match '###\s+(.+?)[\r\n]') {
        $title = $Matches[1].Trim()
    } elseif ($section -match '^\s*\n\s*\*?\*?\*(.+?)\*?\*?\*') {
        $title = $Matches[1].Trim()
    }

    $changes = @()
    foreach ($line in ($section -split "`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^###\s') { continue }

        if ($trimmed -match '^[-*]\s+\*\*(.+?)\*\*[:\s]*(.*)') {
            $text = $Matches[1].Trim() -replace '\s*:\s*$', ''
            $rest = $Matches[2].Trim()
            $changes += if ($rest) { "$text`: $rest" } else { $text }
        } elseif ($trimmed -match '^[-*]\s+(?!\*\*)(.+)') {
            $text = $Matches[1].Trim()
            if ($text) { $changes += $text }
        }
    }

    # Инвариант приложения: у каждой записи непустой список пунктов
    # (ManualChecklistTests). Секции-однострочники без буллетов берём
    # первым абзацем текста.
    if ($changes.Count -eq 0) {
        foreach ($line in ($section -split "`n")) {
            $t = $line.Trim()
            if ($t -and -not $t.StartsWith('#') -and $t -ne '---') { $changes += $t; break }
        }
    }

    $type = "Исправление"
    if ($section -match 'добавлен|новая|новый|feat|feature') {
        $type = "Новинка"
    }

    $updates += [ordered]@{
        version = $version
        date = $date
        type = $type
        title = if ($title) { $title } else { "Версия $version" }
        changes = @($changes)
    }
}

$updates = @($updates)
if ($updates.Count -eq 0) {
    Write-Error "No version entries found in CHANGELOG.md. Expected: ## X.Y.Z - YYYY-MM-DD"
    exit 1
}

# Мержим с существующим json по политике текста из шапки скрипта:
#   * version/date — из CHANGELOG (финализированная дата секции релиза);
#   * title/changes — курированный пользовательский текст из json, если запись
#     уже есть (технический текст CHANGELOG не подменяет его);
#   * type — из json (таксономию чипов «Новинка»/«Улучшение»/«Исправление»/
#     «Техническое» эвристика по тексту не восстанавливает);
#   * записи нет в json — берём секцию CHANGELOG как ЧЕРНОВИК (warning ниже).
# Версии, которых в CHANGELOG нет (история до введения дат), переносятся как есть.
$drafts = @()
$curated = 0
$existingByVersion = @{}
if (Test-Path -LiteralPath $updateLogPath) {
    try {
        $existing = ConvertFrom-Json ([System.IO.File]::ReadAllText($updateLogPath, [System.Text.Encoding]::UTF8))
        foreach ($e in @($existing)) { $existingByVersion[[string]$e.version] = $e }
        foreach ($u in $updates) {
            $v = [string]$u.version
            if (-not $existingByVersion.ContainsKey($v)) {
                $drafts += $v
                continue
            }
            $e = $existingByVersion[$v]
            if ($e.type) { $u.type = [string]$e.type }
            if ($e.title) { $u.title = [string]$e.title }
            if ($e.changes) { $u.changes = @($e.changes) }
            $curated++
        }
    } catch {
        Write-Warning "Существующий $updateLogPath не разобран — перезаписываю только из CHANGELOG: $($_.Exception.Message)"
        $existing = @()
    }
} else {
    $existing = @()
}
$datedVersions = @{}
foreach ($u in $updates) { $datedVersions[[string]$u.version] = $true }
$updates = @($updates + @($existing | Where-Object { -not $datedVersions.ContainsKey([string]$_.version) }))

$json = ConvertTo-Json -InputObject $updates -Depth 5
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($updateLogPath, $json, $utf8NoBom)

Write-Host "Generated: $updateLogPath" -ForegroundColor Green
Write-Host "Versions: $($updates.Count)" -ForegroundColor Gray
Write-Host "Latest: $($updates[0].version) ($($updates[0].date))" -ForegroundColor Gray
Write-Host "Curated user text kept from json: $curated" -ForegroundColor Gray
if ($drafts.Count -gt 0) {
    Write-Warning ("Взято из CHANGELOG как ЧЕРНОВИК (нет курированной записи в json): " +
                   ($drafts -join ', ') +
                   ". Вычитайте текст для пользователя — технический текст CHANGELOG" +
                   " в «Что нового» попадать не должен (страж UpdateLogVoiceTests).")
}
