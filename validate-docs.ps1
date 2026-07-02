# validate-docs.ps1
# A.R.C. Documentation Validator
# Usage: powershell -ExecutionPolicy Bypass -File validate-docs.ps1

$ErrorActionPreference = "Continue"
$issues = 0
$warnings = 0
$projectRoot = $PSScriptRoot

Write-Host "=== A.R.C. Documentation Validator ===" -ForegroundColor Cyan
Write-Host ""

# 1. Version consistency: CURRENT_STATE.md vs .csproj
Write-Host "[1] Version consistency" -ForegroundColor Yellow

$csprojPath = Join-Path $projectRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"
$currentStatePath = Join-Path $projectRoot "docs\arc\CURRENT_STATE.md"

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

$modulesPath = Join-Path $projectRoot "docs\arc\MODULES.md"
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

$cheatsheetPath = Join-Path $projectRoot "docs\arc\CHEATSHEET.md"
if (-not (Test-Path $cheatsheetPath)) {
    Write-Host "  WARN: CHEATSHEET.md not found (skipping)" -ForegroundColor Yellow
    $warnings++
} else {
    $cheatsheetContent = Get-Content $cheatsheetPath -Raw -Encoding UTF8
    $refPattern = '(CALCULATION_LOGIC|CALCULATION_TEST_CASES|GOTCHAS|RELEASE_PROCESS|AUTO_UPDATE|DECISIONS|MODULES|PROJECT_OVERVIEW|DOCUMENTATION_MATRIX|CURRENT_STATE|PROMPTS)\.md'
    $refs = [regex]::Matches($cheatsheetContent, $refPattern) | ForEach-Object { $_.Groups[1].Value + ".md" } | Sort-Object -Unique
    $missing = 0
    foreach ($ref in $refs) {
        $refPath = Join-Path $projectRoot "docs\arc\$ref"
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

$matrixMdPath = Join-Path $projectRoot "docs\arc\DOCUMENTATION_MATRIX.md"
if (-not (Test-Path $matrixMdPath)) {
    Write-Host "  WARN: DOCUMENTATION_MATRIX.md not found (skipping)" -ForegroundColor Yellow
    $warnings++
} else {
    # Read from JSON instead of regex-parsing MD
    $matrixJsonPath = Join-Path $projectRoot "docs\arc\documentation-matrix.json"
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

$controlPath = Join-Path $projectRoot "docs\arc\MULTI_AGENT_ARC_CALC_CONTROL.md"
if (-not (Test-Path $controlPath)) {
    Write-Host "  FAIL: MULTI_AGENT_ARC_CALC_CONTROL.md not found!" -ForegroundColor Red
    $issues++
} else {
    $controlContent = Get-Content $controlPath -Raw -Encoding UTF8
    $controlRefs = [regex]::Matches($controlContent, $refPattern) | ForEach-Object { $_.Groups[1].Value + ".md" } | Sort-Object -Unique
    $ctrlMissing = 0
    foreach ($ref in $controlRefs) {
        $refPath = Join-Path $projectRoot "docs\arc\$ref"
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

# 6. docs/arc/ completeness
Write-Host "[6] docs/arc/ completeness" -ForegroundColor Yellow

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

$docsDir = Join-Path $projectRoot "docs\arc"
$missingDocs = 0
foreach ($doc in $expectedDocs) {
    $docPath = Join-Path $docsDir $doc
    if (-not (Test-Path $docPath)) {
        Write-Host "  MISSING: docs/arc/$doc" -ForegroundColor Red
        $missingDocs++
    }
}
if ($missingDocs -eq 0) {
    Write-Host "  PASS: All $($expectedDocs.Count) expected docs/arc files present" -ForegroundColor Green
} else {
    Write-Host "  FAIL: $missingDocs/$($expectedDocs.Count) files missing" -ForegroundColor Red
    $issues += $missingDocs
}

Write-Host ""

# 7. Last verified dates vs git
Write-Host "[7] Last verified dates vs git" -ForegroundColor Yellow

$docsArcDir = Join-Path $projectRoot "docs\arc"
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
        if ($gitDate -ne $docDate -and $gitDate -ne "unknown") {
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

# 8. Staleness: docs/arc files not changed in recent releases
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
            Write-Host "  PASS: All docs/arc files updated since last release" -ForegroundColor Green
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
