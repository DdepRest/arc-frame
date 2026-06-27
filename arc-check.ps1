# arc-check.ps1
# A.R.C. v4 — Pre-commit documentation sync check
# Usage: powershell -ExecutionPolicy Bypass -File arc-check.ps1
# Verifies that docs/arc files are up-to-date with the current codebase.

$ErrorActionPreference = "Continue"
$issues = 0

Write-Host "=== A.R.C. v4 — Documentation Sync Check ===" -ForegroundColor Cyan
Write-Host ""

# 1. Run validate-docs.ps1
Write-Host "[1] Running validate-docs.ps1..." -ForegroundColor Yellow
$validateOutput = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "validate-docs.ps1") 2>&1
$validateText = $validateOutput -join "`n"

if ($validateText -match "RESULT: ALL CHECKS PASSED") {
    Write-Host "  PASS: validate-docs.ps1 all clear" -ForegroundColor Green
} else {
    Write-Host "  ISSUES in validate-docs.ps1:" -ForegroundColor Red
    Write-Host $validateText -ForegroundColor Red
    $issues++
}

Write-Host ""

# 2. Check SYMBOL_INDEX.md is not stale vs source files
Write-Host "[2] Checking SYMBOL_INDEX.md staleness..." -ForegroundColor Yellow
$symbolIndexPath = Join-Path $PSScriptRoot "docs\arc\SYMBOL_INDEX.md"
if (Test-Path $symbolIndexPath) {
    $indexDate = (Get-Item $symbolIndexPath).LastWriteTime
    $srcFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot "MosquitoNetCalculator") -Filter "*.cs" -Recurse |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }
    $newestSrc = ($srcFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    
    if ($newestSrc.LastWriteTime -gt $indexDate) {
        Write-Host "  STALE: SYMBOL_INDEX.md ($indexDate) is older than $($newestSrc.Name) ($($newestSrc.LastWriteTime))" -ForegroundColor Yellow
        Write-Host "  Run: powershell -File gensymbols.ps1" -ForegroundColor Yellow
        $issues++
    } else {
        Write-Host "  PASS: SYMBOL_INDEX.md is up-to-date" -ForegroundColor Green
    }
} else {
    Write-Host "  SKIP: SYMBOL_INDEX.md not found" -ForegroundColor Gray
}

Write-Host ""

# 3. Check for uncommitted changes to source files without doc updates
Write-Host "[3] Checking doc sync for uncommitted changes..." -ForegroundColor Yellow
$changedFiles = & git -C $PSScriptRoot diff --name-only 2>$null
$changedFiles = @($changedFiles | Where-Object { $_ })

if ($changedFiles.Count -gt 0) {
    $srcChanged = @($changedFiles | Where-Object { $_ -match '\.(cs|xaml|html|json|css)$' -and $_ -notmatch '^(docs/arc/|publish/)' })
    $docsChanged = @($changedFiles | Where-Object { $_ -match '^docs/arc/' -or $_ -match '^CHANGELOG.md$' })
    
    if ($srcChanged.Count -gt 0) {
        Write-Host "  INFO: $($srcChanged.Count) source files changed" -ForegroundColor Gray
        
        # Run what-to-update
        $whatUpdateOutput = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "what-to-update.ps1") @srcChanged 2>&1
        $whatUpdateText = ($whatUpdateOutput -join "`n")
        Write-Host "  $whatUpdateText" -ForegroundColor Gray
        
        if ($docsChanged.Count -eq 0) {
            Write-Host "  WARN: Source files changed but no docs/arc or CHANGELOG.md updated" -ForegroundColor Yellow
            Write-Host "  Run: what-to-update.ps1 your-changed-files" -ForegroundColor Yellow
            $issues++
        } else {
            Write-Host "  PASS: $($docsChanged.Count) doc files updated for $($srcChanged.Count) source changes" -ForegroundColor Green
        }
    } else {
        Write-Host "  PASS: No source files changed (only docs)" -ForegroundColor Green
    }
} else {
    Write-Host "  PASS: No uncommitted changes" -ForegroundColor Green
}

Write-Host ""

# Summary
Write-Host "====================================" -ForegroundColor Cyan
if ($issues -eq 0) {
    Write-Host "RESULT: ALL CHECKS PASSED" -ForegroundColor Green
    Write-Host "  Issues: 0" -ForegroundColor Green
} else {
    Write-Host "RESULT: $issues ISSUE(S) FOUND" -ForegroundColor Red
    Write-Host "  Issues: $issues" -ForegroundColor Red
    exit 1
}
