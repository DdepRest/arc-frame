# find-task.ps1 — Умный агентский навигатор по кодовой базе и документации A.R.C.
# Usage:
#   powershell -ExecutionPolicy Bypass -File agents/scripts/find-task.ps1 "anwis"
#   .\find.bat "скидка"

param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Query
)

$ErrorActionPreference = "Continue"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$docsDir = Join-Path $projectRoot "agents\docs"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   A.R.C. Task Locator: `"$Query`"" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$qLower = $Query.ToLower().Trim()
$foundFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$foundSymbols = @()
$foundGotchas = @()
$foundIntents = @()
$foundCalc = @()

# 1. Search INTENTS.md
$intentsPath = Join-Path $docsDir "INTENTS.md"
if (Test-Path $intentsPath) {
    $lines = Get-Content $intentsPath -Encoding UTF8
    foreach ($line in $lines) {
        if ($line.ToLower().Contains($qLower) -and $line.Contains("|")) {
            $parts = $line.Split("|") | ForEach-Object { $_.Trim() }
            if ($parts.Count -ge 4 -and $parts[1] -notmatch '^---' -and $parts[1] -ne "Что сделать / Намерение") {
                $intent = $parts[1]
                $files = $parts[2]
                $foundIntents += "$intent -> $files"
                $matches = [regex]::Matches($files, '`([^`]+)`')
                foreach ($m in $matches) {
                    $foundFiles.Add($m.Groups[1].Value) | Out-Null
                }
            }
        }
    }
}

# 2. Search SYMBOL_INDEX.md
# Format: | Name | Kind | File | Props | Methods | Static |
$symbolPath = Join-Path $docsDir "SYMBOL_INDEX.md"
if (Test-Path $symbolPath) {
    $lines = Get-Content $symbolPath -Encoding UTF8
    $currModule = ""
    foreach ($line in $lines) {
        if ($line -match '^###\s+(.+)') {
            $currModule = $Matches[1].Trim()
        } elseif ($line.ToLower().Contains($qLower) -and $line.Contains("|")) {
            $parts = $line.Split("|") | ForEach-Object { $_.Trim() }
            if ($parts.Count -ge 4 -and $parts[1] -notmatch '^---' -and $parts[1] -ne "Name" -and $parts[1] -ne "Класс") {
                $className = $parts[1] -replace '[`\*]', ''
                $kind = $parts[2] -replace '[`\*]', ''
                $file = if ($parts.Count -ge 4) { $parts[3] -replace '[`\*]', '' } else { "" }
                $foundSymbols += "$kind $className ($currModule) -> $file"
                if ($file) { $foundFiles.Add($file) | Out-Null }
            }
        }
    }
}

# 3. Search GOTCHAS.md (Quick Index table)
$gotchasPath = Join-Path $docsDir "GOTCHAS.md"
if (Test-Path $gotchasPath) {
    $lines = Get-Content $gotchasPath -Encoding UTF8
    foreach ($line in $lines) {
        if ($line.ToLower().Contains($qLower) -and $line.Contains("|")) {
            $parts = $line.Split("|") | ForEach-Object { $_.Trim() }
            if ($parts.Count -ge 5 -and $parts[1] -match '^\d+$') {
                $num = $parts[1]
                $domain = $parts[2]
                $files = $parts[3] -replace '`', ''
                $desc = $parts[4]
                $foundGotchas += "GOTCHAS#$num [$domain]: $desc"
                if ($files) {
                    foreach ($f in ($files -split ',\s*')) {
                        if ($f -match '\.(cs|xaml|md|json)$') { $foundFiles.Add($f.Trim()) | Out-Null }
                    }
                }
            }
        }
    }
}

# 4. Search CALCULATION_LOGIC.md (Quick Index table)
$calcPath = Join-Path $docsDir "CALCULATION_LOGIC.md"
if (Test-Path $calcPath) {
    $lines = Get-Content $calcPath -Encoding UTF8
    foreach ($line in $lines) {
        if ($line.ToLower().Contains($qLower) -and $line.Contains("|")) {
            $parts = $line.Split("|") | ForEach-Object { $_.Trim() }
            if ($parts.Count -ge 5 -and $parts[2] -match '\[(.*?)\]\((.*?)\)') {
                $secName = $Matches[1]
                $anchor = $Matches[2]
                $files = $parts[3] -replace '`', ''
                $desc = $parts[4]
                $foundCalc += "CALCULATION_LOGIC.md$anchor ($secName) -> $desc"
                if ($files) {
                    foreach ($f in ($files -split ',\s*')) {
                        if ($f -match '\.(cs|xaml|md|json)$') { $foundFiles.Add($f.Trim()) | Out-Null }
                    }
                }
            }
        }
    }
}

# Output Results
Write-Host ""
if ($foundFiles.Count -gt 0) {
    Write-Host "[Relevant Files]" -ForegroundColor Green
    foreach ($f in ($foundFiles | Select-Object -First 8)) {
        Write-Host "  - $f" -ForegroundColor White
    }
    Write-Host ""
}

if ($foundSymbols.Count -gt 0) {
    Write-Host "[Symbols & Classes]" -ForegroundColor Yellow
    foreach ($s in ($foundSymbols | Select-Object -First 5)) {
        Write-Host "  - $s" -ForegroundColor White
    }
    Write-Host ""
}

if ($foundGotchas.Count -gt 0) {
    Write-Host "[Relevant Gotchas]" -ForegroundColor Red
    foreach ($g in ($foundGotchas | Select-Object -First 4)) {
        Write-Host "  - $g" -ForegroundColor White
    }
    Write-Host ""
}

if ($foundCalc.Count -gt 0) {
    Write-Host "[Calculation Logic]" -ForegroundColor Magenta
    foreach ($c in ($foundCalc | Select-Object -First 3)) {
        Write-Host "  - $c" -ForegroundColor White
    }
    Write-Host ""
}

if ($foundIntents.Count -gt 0) {
    Write-Host "[Matched User Intents]" -ForegroundColor Cyan
    foreach ($i in ($foundIntents | Select-Object -First 3)) {
        Write-Host "  - $i" -ForegroundColor White
    }
    Write-Host ""
}

if ($foundFiles.Count -eq 0 -and $foundSymbols.Count -eq 0 -and $foundGotchas.Count -eq 0 -and $foundCalc.Count -eq 0) {
    Write-Host "Ничего не найдено по запросу `"$Query`". Попробуйте корень слова или имя класса." -ForegroundColor Gray
}

Write-Host "============================================================" -ForegroundColor Cyan
