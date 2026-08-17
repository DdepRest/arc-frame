# generate-update-log.ps1
# Генерирует MosquitoNetCalculator/Resources/update-log.json из CHANGELOG.md.
# Запускать при релизе.
#
# Использование:
#   powershell -ExecutionPolicy Bypass -File generate-update-log.ps1

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot

$changelogPath = Join-Path $projectRoot "CHANGELOG.md"
$updateLogPath = Join-Path $projectRoot "MosquitoNetCalculator\Resources\update-log.json"

if (-not (Test-Path $changelogPath)) {
    Write-Host "FAIL: CHANGELOG.md not found" -ForegroundColor Red
    exit 1
}

$content = Get-Content $changelogPath -Raw -Encoding UTF8

# Парсим секции версий: ## X.Y.Z — YYYY-MM-DD
$versionPattern = '##\s+(\d+\.\d+\.\d+)\s*[—–-]\s*(\d{4}-\d{2}-\d{2})'
$versionMatches = [regex]::Matches($content, $versionPattern)

$updates = @()

for ($i = 0; $i -lt $versionMatches.Count; $i++) {
    $v = $versionMatches[$i]
    $version = $v.Groups[1].Value
    $date = $v.Groups[2].Value

    # Определяем границы текста этой версии
    $startIndex = $v.Index + $v.Length
    if ($i -lt $versionMatches.Count - 1) {
        $endIndex = $versionMatches[$i + 1].Index
    } else {
        $endIndex = $content.Length
    }

    $section = $content.Substring($startIndex, $endIndex - $startIndex)

    # Извлекаем заголовок (первая непустая строка после ### или текст до первого ###)
    $title = ""
    if ($section -match '###\s+(.+?)[\r\n]') {
        $title = $Matches[1].Trim()
    } elseif ($section -match '^\s*\n\s*\*?\*?\*(.+?)\*?\*?\*') {
        $title = $Matches[1].Trim()
    }

    # Собираем пункты изменений
    $changes = @()
    $lines = $section -split "`n"
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        # Skip ### subsection headers
        if ($trimmed -match '^###\s') {
            continue
        }
        # Bold list item: - **Text:** rest or - **Text** rest
        if ($trimmed -match '^[-*]\s+\*\*(.+?)\*\*[:\s]*(.*)') {
            $text = $Matches[1].Trim()
            $rest = $Matches[2].Trim()
            # Strip trailing colon from bold text if present (CHANGELOG already has it)
            $text = $text -replace '\s*:\s*$', ''
            if ($rest) {
                $changes += "$text`: $rest"
            } else {
                $changes += $text
            }
        }
        # Plain list item (no bold): - Text
        elseif ($trimmed -match '^[-*]\s+(?!\*\*)(.+)') {
            $text = $Matches[1].Trim()
            if ($text) { $changes += $text }
        }
    }

    # Определяем тип релиза
    $type = "Исправление"
    if ($section -match 'исправлен|фикс|fix|bug') { $type = "Исправление" }
    if ($section -match 'добавлен|новая|новый|feat|feature') { $type = "Новая функция" }

    $update = @{
        version = $version
        date = $date
        type = $type
        title = if ($title) { $title } else { "Версия $version" }
        changes = $changes
    }
    $updates += $update
}

# Ограничиваем последними 15 версиями
$updates = $updates | Select-Object -First 15

if ($updates.Count -eq 0) {
    Write-Host "FAIL: No version entries found in CHANGELOG.md. Check format." -ForegroundColor Red
    Write-Host "Expected: ## X.Y.Z - YYYY-MM-DD" -ForegroundColor Gray
    exit 1
}

$json = $updates | ConvertTo-Json -Depth 3
Set-Content -Path $updateLogPath -Value $json -Encoding UTF8

Write-Host "Generated: $updateLogPath" -ForegroundColor Green
Write-Host "Versions: $($updates.Count)" -ForegroundColor Gray
if ($updates.Count -gt 0) {
    Write-Host "Latest: $($updates[0].version) ($($updates[0].date))" -ForegroundColor Gray
}
