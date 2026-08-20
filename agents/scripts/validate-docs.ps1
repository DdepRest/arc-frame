# validate-docs.ps1
# A.R.C. Documentation Validator
# Usage: powershell -ExecutionPolicy Bypass -File validate-docs.ps1

$ErrorActionPreference = "Continue"
$issues = 0
$warnings = 0
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

Write-Host "=== A.R.C. Documentation Validator ===" -ForegroundColor Cyan
Write-Host ""

# 1. Version consistency: CURRENT_STATE.md vs .csproj
Write-Host "[1] Version consistency" -ForegroundColor Yellow

$csprojPath = Join-Path $projectRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"
$currentStatePath = Join-Path $projectRoot "agents\docs\CURRENT_STATE.md"

if (-not (Test-Path $csprojPath)) {
    Write-Host "  FAIL: MosquitoNetCalculator.csproj not found" -ForegroundColor Red
    $issues++
} else {
    $csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8
    if ($csprojContent -match '<Version>(.+?)</Version>') {
        $csprojVersion = $Matches[1]
        Write-Host "  .csproj version: $csprojVersion" -ForegroundColor Gray
    } else {
        Write-Host "  FAIL: Could not extract version from .csproj" -ForegroundColor Red
        $csprojVersion = $null
        $issues++
    }
}

if (-not (Test-Path $currentStatePath)) {
    Write-Host "  FAIL: CURRENT_STATE.md not found" -ForegroundColor Red
    $issues++
} elseif ($csprojVersion) {
    $currentStateContent = Get-Content $currentStatePath -Raw -Encoding UTF8
    if ($currentStateContent -match '[вВ]ерсия[:\s]+\*?\*?(?<ver>\d+\.\d+\.\d+)\*?\*?') {
        $docVersion = $Matches['ver']
        Write-Host "  CURRENT_STATE.md version: $docVersion" -ForegroundColor Gray
        if ($docVersion -eq $csprojVersion) {
            Write-Host "  PASS: Versions match" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: Version mismatch! .csproj=$csprojVersion, CURRENT_STATE.md=$docVersion" -ForegroundColor Red
            $issues++
        }
    } else {
        Write-Host "  WARN: Could not extract version from CURRENT_STATE.md" -ForegroundColor Yellow
        $warnings++
    }
}

Write-Host ""

# 2. MODULES.md file references exist on disk
Write-Host "[2] MODULES.md file references" -ForegroundColor Yellow

$modulesPath = Join-Path $projectRoot "agents\docs\MODULES.md"
if (-not (Test-Path $modulesPath)) {
    Write-Host "  WARN: MODULES.md not found (skipping)" -ForegroundColor Yellow
    $warnings++
} else {
    $modulesContent = Get-Content $modulesPath -Raw -Encoding UTF8
    $pattern = '`(MosquitoNetCalculator[^`]+)`'
    $matches = [regex]::Matches($modulesContent, $pattern)
    $checked = 0
    $missing = 0
    foreach ($m in $matches) {
        $relPath = $m.Groups[1].Value
        $fullPath = Join-Path $projectRoot $relPath
        $checked++
        if (-not (Test-Path $fullPath)) {
            Write-Host "  MISSING: $relPath" -ForegroundColor Red
            $missing++
        }
    }
    if ($missing -eq 0) {
        Write-Host "  PASS: All $checked referenced files exist" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $missing/$checked files missing" -ForegroundColor Red
        $issues += $missing
    }
}

Write-Host ""

# 3. CHEATSHEET.md cross-references
Write-Host "[3] CHEATSHEET.md cross-references" -ForegroundColor Yellow

$cheatsheetPath = Join-Path $projectRoot "agents\docs\CHEATSHEET.md"
if (-not (Test-Path $cheatsheetPath)) {
    Write-Host "  WARN: CHEATSHEET.md not found (skipping)" -ForegroundColor Yellow
    $warnings++
} else {
    $cheatsheetContent = Get-Content $cheatsheetPath -Raw -Encoding UTF8
    $refPattern = '(CALCULATION_LOGIC|CALCULATION_TEST_CASES|GOTCHAS|RELEASE_PROCESS|AUTO_UPDATE|DECISIONS|MODULES|PROJECT_OVERVIEW|DOCUMENTATION_MATRIX|CURRENT_STATE|PROMPTS)\.md'
    $refs = [regex]::Matches($cheatsheetContent, $refPattern) | ForEach-Object { $_.Groups[1].Value + ".md" } | Sort-Object -Unique
    $missing = 0
    foreach ($ref in $refs) {
        $refPath = Join-Path $projectRoot "agents\docs\$ref"
        if (-not (Test-Path $refPath)) {
            Write-Host "  MISSING: $ref (referenced in CHEATSHEET)" -ForegroundColor Red
            $missing++
        }
    }
    if ($missing -eq 0) {
        Write-Host "  PASS: All cross-references in CHEATSHEET.md are valid" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $missing cross-references broken" -ForegroundColor Red
        $issues += $missing
    }
}

Write-Host ""

# 4. DOCUMENTATION_MATRIX.md source file references
Write-Host "[4] DOCUMENTATION_MATRIX.md source file references" -ForegroundColor Yellow

$matrixMdPath = Join-Path $projectRoot "agents\docs\DOCUMENTATION_MATRIX.md"
if (-not (Test-Path $matrixMdPath)) {
    Write-Host "  WARN: DOCUMENTATION_MATRIX.md not found (skipping)" -ForegroundColor Yellow
    $warnings++
} else {
    # Read from JSON instead of regex-parsing MD
    $matrixJsonPath = Join-Path $projectRoot "agents\docs\documentation-matrix.json"
    if (Test-Path $matrixJsonPath) {
        $matrix = Get-Content $matrixJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $checkedSrc = 0
        $missingSrc = 0
        foreach ($mapping in $matrix.mappings) {
            $relPath = $mapping.file
            if ($relPath -match '\*') { continue }  # skip glob patterns
            # Convert JSON path to filesystem path
            if ($relPath -match '^(Models|ViewModels|Services|Controls|Themes|Resources)/') {
                $fullPath = Join-Path $projectRoot "MosquitoNetCalculator\$relPath"
            } elseif ($relPath -match '^MosquitoNetCalculator\.') {
                $fullPath = Join-Path $projectRoot "MosquitoNetCalculator\$relPath"
            } else {
                $fullPath = Join-Path $projectRoot $relPath
            }
            $checkedSrc++
            if (-not (Test-Path $fullPath)) {
                Write-Host "  MISSING: $relPath" -ForegroundColor Red
                $missingSrc++
            }
        }
        if ($missingSrc -eq 0) {
            Write-Host "  PASS: All $checkedSrc source files from matrix JSON exist" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: $missingSrc/$checkedSrc files missing" -ForegroundColor Red
            $issues += $missingSrc
        }
    } else {
        Write-Host "  WARN: documentation-matrix.json not found, falling back to regex" -ForegroundColor Yellow
        $matrixContent = Get-Content $matrixMdPath -Raw -Encoding UTF8
        $srcPattern = '`([a-zA-Z][^`]+\.(cs|xaml|html|json|bat|iss|ps1))`'
        $srcMatches = [regex]::Matches($matrixContent, $srcPattern)
        $checkedSrc = 0
        $missingSrc = 0
        foreach ($m in $srcMatches) {
            $relPath = $m.Groups[1].Value
            if ($relPath -match '^(Models|ViewModels|Services|Controls|Themes|Resources)/') {
                $fullPath = Join-Path $projectRoot "MosquitoNetCalculator\$relPath"
            } elseif ($relPath -match '^MosquitoNetCalculator\.') {
                $fullPath = Join-Path $projectRoot "MosquitoNetCalculator\$relPath"
            } else {
                $fullPath = Join-Path $projectRoot $relPath
            }
            $checkedSrc++
            if (-not (Test-Path $fullPath)) {
                Write-Host "  MISSING: $relPath" -ForegroundColor Red
                $missingSrc++
            }
        }
        if ($missingSrc -eq 0) {
            Write-Host "  PASS: All $checkedSrc source files exist" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: $missingSrc/$checkedSrc files missing" -ForegroundColor Red
            $issues += $missingSrc
        }
    }
}

Write-Host ""

# 5. MULTI_AGENT_ARC_CALC_CONTROL.md cross-references
Write-Host "[5] MULTI_AGENT_ARC_CALC_CONTROL.md cross-references" -ForegroundColor Yellow

$controlPath = Join-Path $projectRoot "agents\docs\MULTI_AGENT_ARC_CALC_CONTROL.md"
if (-not (Test-Path $controlPath)) {
    Write-Host "  FAIL: MULTI_AGENT_ARC_CALC_CONTROL.md not found!" -ForegroundColor Red
    $issues++
} else {
    $controlContent = Get-Content $controlPath -Raw -Encoding UTF8
    $controlRefs = [regex]::Matches($controlContent, $refPattern) | ForEach-Object { $_.Groups[1].Value + ".md" } | Sort-Object -Unique
    $ctrlMissing = 0
    foreach ($ref in $controlRefs) {
        $refPath = Join-Path $projectRoot "agents\docs\$ref"
        if (-not (Test-Path $refPath)) {
            Write-Host "  MISSING: $ref (referenced in CONTROL)" -ForegroundColor Red
            $ctrlMissing++
        }
    }
    if ($ctrlMissing -eq 0) {
        Write-Host "  PASS: All cross-references in CONTROL are valid" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $ctrlMissing cross-references broken" -ForegroundColor Red
        $issues += $ctrlMissing
    }
}

Write-Host ""

# 6. agents/docs/ completeness
Write-Host "[6] agents/docs/ completeness" -ForegroundColor Yellow

$expectedDocs = @(
    "MULTI_AGENT_ARC_CALC_CONTROL.md",
    "CHEATSHEET.md",
    "DOCUMENTATION_MATRIX.md",
    "PROMPTS.md",
    "CURRENT_STATE.md",
    "CALCULATION_LOGIC.md",
    "CALCULATION_TEST_CASES.md",
    "GOTCHAS.md",
    "MODULES.md",
    "DECISIONS.md",
    "PROJECT_OVERVIEW.md",
    "RELEASE_PROCESS.md",
    "AUTO_UPDATE.md"
)

$docsDir = Join-Path $projectRoot "agents\docs"
$missingDocs = 0
foreach ($doc in $expectedDocs) {
    $docPath = Join-Path $docsDir $doc
    if (-not (Test-Path $docPath)) {
        Write-Host "  MISSING: agents/docs/$doc" -ForegroundColor Red
        $missingDocs++
    }
}
if ($missingDocs -eq 0) {
    Write-Host "  PASS: All $($expectedDocs.Count) expected agents/docs files present" -ForegroundColor Green
} else {
    Write-Host "  FAIL: $missingDocs/$($expectedDocs.Count) files missing" -ForegroundColor Red
    $issues += $missingDocs
}

Write-Host ""

# 7. Last verified dates vs git
Write-Host "[7] Last verified dates vs git" -ForegroundColor Yellow

$docsArcDir = Join-Path $projectRoot "agents\docs"
$docsFiles = @(Get-ChildItem -Path $docsArcDir -Filter "*.md" | Where-Object { $_.Name -ne "DOCUMENTATION_MATRIX.md" })
$dateChecked = 0
$dateStale = 0

foreach ($docFile in $docsFiles) {
    $content = Get-Content $docFile.FullName -Raw -Encoding UTF8
    if ($content -match 'Last verified[:\s]*(\d{4}-\d{2}-\d{2})') {
        $docDate = $Matches[1]
        $gitDate = git -C $projectRoot log -1 --format="%as" -- $docFile.FullName 2>$null
        if (-not $gitDate) { $gitDate = "unknown" }
        $dateChecked++
        if ($gitDate -ne "unknown" -and $docDate -lt $gitDate) {
            Write-Host "  STALE: $($docFile.Name) - doc says $docDate, git says $gitDate" -ForegroundColor Yellow
            $dateStale++
        }
    }
}

if ($dateStale -eq 0) {
    Write-Host "  PASS: All $dateChecked Last verified dates match git" -ForegroundColor Green
} else {
    Write-Host "  WARN: $dateStale/$dateChecked dates are stale" -ForegroundColor Yellow
    $warnings += $dateStale
}

Write-Host ""

# 8. Staleness: agents/docs files not changed in recent releases
Write-Host "[8] Documentation staleness" -ForegroundColor Yellow

# Redefine $docsFiles in case check 7 was skipped or failed
if (-not $docsFiles) {
    $docsFiles = @(Get-ChildItem -Path $docsArcDir -Filter "*.md")
}

$lastReleaseTag = git -C $projectRoot tag --sort=-creatordate --merged HEAD 2>$null | Select-Object -First 1
if (-not $lastReleaseTag) {
    Write-Host "  SKIP: No release tags found in git" -ForegroundColor Gray
} else {
    $lastReleaseDate = git -C $projectRoot log -1 --format="%as" $lastReleaseTag 2>$null
    if (-not $lastReleaseDate) {
        Write-Host "  SKIP: Could not determine last release date" -ForegroundColor Gray
    } else {
        Write-Host "  Last release: $lastReleaseTag ($lastReleaseDate)" -ForegroundColor Gray
        $staleCount = 0
        foreach ($docFile in $docsFiles) {
            $gitDate = git -C $projectRoot log -1 --format="%as" -- $docFile.FullName 2>$null
            if ($gitDate -and $gitDate -lt $lastReleaseDate) {
                Write-Host "  STALE: $($docFile.Name) - last change $gitDate (before release $lastReleaseDate)" -ForegroundColor Yellow
                $staleCount++
            }
        }
        if ($staleCount -eq 0) {
            Write-Host "  PASS: All agents/docs files updated since last release" -ForegroundColor Green
        } else {
            Write-Host "  WARN: $staleCount files not changed since last release" -ForegroundColor Yellow
            $warnings += $staleCount
        }
    }
}

Write-Host ""

# 9. releases.json JSON validity and schema
Write-Host "[9] releases.json validity" -ForegroundColor Yellow

$releasesJsonPath = Join-Path $projectRoot "releases.json"
if (-not (Test-Path $releasesJsonPath)) {
    Write-Host "  FAIL: releases.json not found" -ForegroundColor Red
    $issues++
} else {
    try {
        $releasesContent = Get-Content $releasesJsonPath -Raw -Encoding UTF8
        $releasesData = $releasesContent | ConvertFrom-Json -ErrorAction Stop
        Write-Host "  PASS: Valid JSON" -ForegroundColor Green

        # Check required top-level fields
        $schemaIssues = 0
        if ([string]::IsNullOrWhiteSpace($releasesData.latest)) {
            Write-Host "  FAIL: Missing or empty 'latest' field" -ForegroundColor Red
            $schemaIssues++
        }
        if (-not $releasesData.releases -or $releasesData.releases.Count -eq 0) {
            Write-Host "  FAIL: Missing or empty 'releases' array" -ForegroundColor Red
            $schemaIssues++
        }

        # Check each release has required fields
        $releaseIndex = 0
        foreach ($rel in $releasesData.releases) {
            $requiredFields = @('version', 'date', 'type', 'title', 'changes', 'url', 'size', 'sha256')
            foreach ($field in $requiredFields) {
                if (-not (Get-Member -InputObject $rel -Name $field -MemberType Properties)) {
                    Write-Host "  FAIL: Release[$releaseIndex] missing field '$field'" -ForegroundColor Red
                    $schemaIssues++
                }
            }
            if ($rel.changes -and -not ($rel.changes -is [array])) {
                Write-Host "  FAIL: Release[$releaseIndex] ($($rel.version)) 'changes' is not an array" -ForegroundColor Red
                $schemaIssues++
            }
            # Validate sha256 is non-empty for non-placeholder entries
            # (placeholder entries like v3.40.4 have size=0 and sha256="")
            if ($rel.size -gt 0 -and [string]::IsNullOrWhiteSpace($rel.sha256)) {
                Write-Host "  FAIL: Release[$releaseIndex] ($($rel.version)) has size>0 but empty sha256" -ForegroundColor Red
                $schemaIssues++
            }
            $releaseIndex++
        }

        # Check latest matches first release version (newest-first ordering)
        if ($releasesData.releases -and $releasesData.releases.Count -gt 0) {
            $firstVersion = $releasesData.releases[0].version
            if ($releasesData.latest -ne $firstVersion) {
                Write-Host "  FAIL: 'latest' ($($releasesData.latest)) != first release version ($firstVersion)" -ForegroundColor Red
                $schemaIssues++
            } else {
                Write-Host "  PASS: 'latest' matches first release version" -ForegroundColor Green
            }
        }

        $releaseCount = if ($releasesData.releases) { $releasesData.releases.Count } else { 0 }
        if ($schemaIssues -eq 0) {
            Write-Host "  PASS: Schema valid ($releaseCount releases)" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: $schemaIssues schema issue(s)" -ForegroundColor Red
            $issues += $schemaIssues
        }
    } catch {
        Write-Host "  FAIL: Invalid JSON — $($_.Exception.Message)" -ForegroundColor Red
        $issues++
    }
}

Write-Host ""

# 10. Self-maintenance: stale version in agents/docs Last verified (soft, CONTROL#13)
# Мягкая проверка: НЕ полное покрытие — «видит» только файлы, где версия стоит
# внутри секции Last verified в пределах 300 символов. GOTCHAS/MODULES/etc. без
# версии в Last verified молча пропускаются. Это осознанно (soft level, CONTROL#13).
Write-Host "[10] Self-maintenance: version staleness in agents/docs (soft)" -ForegroundColor Yellow

if ($csprojVersion) {
    if (-not $docsFiles) {
        $docsFiles = @(Get-ChildItem -Path $docsArcDir -Filter "*.md" | Where-Object { $_.Name -ne "DOCUMENTATION_MATRIX.md" })
    }
    $verChecked = 0
    $verStale = 0
    foreach ($docFile in $docsFiles) {
        $content = Get-Content $docFile.FullName -Raw -Encoding UTF8
        # Find a version like v3.42.0 inside the Last verified section
        if ($content -match '(?m)^## Last verified[\s\S]{0,1000}?\(?(v?\d+\.\d+\.\d+)') {
            $docVer = $Matches[1] -replace '^v',''
            $verChecked++
            if ($docVer -ne $csprojVersion) {
                Write-Host "  STALE VER: $($docFile.Name) - Last verified says v$docVer, csproj=$csprojVersion" -ForegroundColor Yellow
                $verStale++
            }
        }
    }
    if ($verStale -eq 0) {
        Write-Host "  PASS: All $verChecked agents/docs Last verified versions match csproj" -ForegroundColor Green
    } else {
        Write-Host "  WARN: $verStale/$verChecked docs reference an older version (soft — не блокирует, обнови при случае)" -ForegroundColor Yellow
        $warnings += $verStale
    }
} else {
    Write-Host "  SKIP: csproj version unavailable" -ForegroundColor Gray
}

# 11. CONTROL#N references resolve to real sections (hard)
Write-Host "[11] CONTROL#N references resolve to real sections" -ForegroundColor Yellow

$controlPath11 = Join-Path $projectRoot "agents\docs\MULTI_AGENT_ARC_CALC_CONTROL.md"
if (Test-Path $controlPath11) {
    $ctrlContent11 = Get-Content $controlPath11 -Raw -Encoding UTF8
    # Собираем номера реальных секций: заголовки вида "## N. ..." (в т.ч. "### N.N." для вложенных)
    $sectionNums = @{}
    foreach ($m in [regex]::Matches($ctrlContent11, '(?m)^#{2,3}\s+(\d+(?:\.\d+)*)\s*\.')) {
        $sectionNums[$m.Groups[1].Value] = $true
    }
    # Ищем все ссылки CONTROL#N во всех agents/docs/*.md и AGENTS.md
    $refFiles = @(Get-ChildItem -Path $docsArcDir -Filter "*.md")
    $refFiles += @(Get-Item (Join-Path $projectRoot "AGENTS.md") -ErrorAction SilentlyContinue)
    $brokenRefs = 0
    $checkedRefs = 0
    foreach ($rf in $refFiles) {
        $rc = Get-Content $rf.FullName -Raw -Encoding UTF8
        foreach ($m in [regex]::Matches($rc, 'CONTROL#(\d+(?:\.\d+)*)')) {
            $num = $m.Groups[1].Value
            $checkedRefs++
            if (-not $sectionNums.ContainsKey($num)) {
                Write-Host "  BROKEN REF: $($rf.Name) -> CONTROL#$num (no such section)" -ForegroundColor Red
                $brokenRefs++
            }
        }
    }
    if ($brokenRefs -eq 0) {
        Write-Host "  PASS: All $checkedRefs CONTROL#N references resolve to real sections" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $brokenRefs broken CONTROL#N references" -ForegroundColor Red
        $issues += $brokenRefs
    }
} else {
    Write-Host "  SKIP: CONTROL file not found" -ForegroundColor Gray
}

Write-Host ""

# 12. GOTCHAS.md numbering — soft check (CONTROL#13, hardening stage 6)
Write-Host "[12] GOTCHAS.md numbering consistency (soft)" -ForegroundColor Yellow
$gotchasPath = Join-Path $projectRoot "agents\docs\GOTCHAS.md"
if (-not (Test-Path $gotchasPath)) {
    Write-Host "  SKIP: GOTCHAS.md not found" -ForegroundColor Gray
} else {
    $gotchasContent = Get-Content $gotchasPath -Raw -Encoding UTF8
    # Collect all `### N.` markers (numbering of dangerous topics).
    $gotchaMatches = [regex]::Matches($gotchasContent, '^### (\d+)\. ', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $gotchaNumbers = @()
    foreach ($m in $gotchaMatches) { $gotchaNumbers += [int]$m.Groups[1].Value }
    if ($gotchaNumbers.Count -eq 0) {
        Write-Host "  WARN: no numbered gotchas found" -ForegroundColor Yellow
        $warnings++
    } else {
        # Detect gaps and duplicates. Soft = warning, not error.
        $sorted = $gotchaNumbers | Sort-Object -Unique
        $expected = 1..$sorted[-1]
        $missing = $expected | Where-Object { $_ -notin $sorted }
        $duplicates = $gotchaNumbers | Group-Object | Where-Object { $_.Count -gt 1 }
        $allSeqOk = $true
        for ($i = 1; $i -lt $sorted.Count; $i++) {
            if ($sorted[$i] -ne $sorted[$i - 1] + 1) {
                $allSeqOk = $false
                break
            }
        }
        if ($missing.Count -eq 0 -and $duplicates.Count -eq 0 -and $allSeqOk) {
            Write-Host "  PASS: gotchas 1..$($sorted[-1]) are sequential with no gaps/duplicates" -ForegroundColor Green
        } else {
            $msg = "gotchas not sequential: "
            if ($missing.Count -gt 0) { $msg += "missing [$($missing -join ',')] " }
            if ($duplicates.Count -gt 0) { $msg += "duplicates [$($duplicates.Name -join ',')] " }
            if (-not $allSeqOk) { $msg += "out-of-order" }
            Write-Host "  WARN: $msg" -ForegroundColor Yellow
            $warnings++
        }
    }
}

Write-Host ""

# Summary
Write-Host "====================================" -ForegroundColor Cyan
if ($issues -eq 0 -and $warnings -eq 0) {
    Write-Host "RESULT: ALL CHECKS PASSED" -ForegroundColor Green
    Write-Host "  Issues:  0" -ForegroundColor Green
    Write-Host "  Warnings: 0" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: ISSUES FOUND" -ForegroundColor Red
    Write-Host "  Issues:  $issues" -ForegroundColor $(if ($issues -gt 0) { "Red" } else { "Green" })
    Write-Host "  Warnings: $warnings" -ForegroundColor $(if ($warnings -gt 0) { "Yellow" } else { "Green" })
    if ($issues -gt 0) {
        exit 1
    } else {
        exit 0
    }
}
