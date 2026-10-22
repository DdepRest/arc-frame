# arc-finish.ps1 — One-click task finalizer for AI agents & developers (CONTROL#13)
#
# Performs complete self-maintenance in one step:
#   1. Runs sync-last-verified.ps1 to stamp today's date in modified agents/docs/*.md
#   2. Regenerates SYMBOL_INDEX.md via gensymbols.ps1
#   3. Checks CURRENT_STATE.md and rotates older versions if count >= 10
#   4. Checks CHANGELOG.md reminder for code changes
#   5. Runs validate-docs.ps1 (12 validation checks)
#   6. Runs arc-check.ps1 (final gatekeeper)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File agents/scripts/arc-finish.ps1
#   powershell -ExecutionPolicy Bypass -File agents/scripts/arc-finish.ps1 -DryRun

param(
    [switch]$DryRun
)

$ErrorActionPreference = "Continue"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   A.R.C. Task Finalizer (arc-finish)   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Sync Last Verified
Write-Host "[1/6] Syncing 'Last verified' in modified docs..." -ForegroundColor Yellow
$syncScript = Join-Path $PSScriptRoot "sync-last-verified.ps1"
if (Test-Path $syncScript) {
    if ($DryRun) {
        & powershell -ExecutionPolicy Bypass -File $syncScript -DryRun
    } else {
        & powershell -ExecutionPolicy Bypass -File $syncScript
    }
}

# 2. Regenerate Symbol Index
Write-Host ""
Write-Host "[2/6] Updating SYMBOL_INDEX.md..." -ForegroundColor Yellow
$genSymbols = Join-Path $PSScriptRoot "gensymbols.ps1"
if (Test-Path $genSymbols) {
    if (-not $DryRun) {
        & powershell -ExecutionPolicy Bypass -File $genSymbols
    } else {
        Write-Host "  [DryRun] Would run gensymbols.ps1" -ForegroundColor Gray
    }
}

# 3. Rolling Window Check for CURRENT_STATE.md
Write-Host ""
Write-Host "[3/6] Checking CURRENT_STATE.md rolling window..." -ForegroundColor Yellow
$rotateScript = Join-Path $PSScriptRoot "rotate-current-state.ps1"
if (Test-Path $rotateScript) {
    if ($DryRun) {
        & powershell -ExecutionPolicy Bypass -File $rotateScript -Check
    } else {
        $null = & powershell -ExecutionPolicy Bypass -File $rotateScript -Check 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Entries >= 10: Running auto-rotation..." -ForegroundColor Yellow
            & powershell -ExecutionPolicy Bypass -File $rotateScript
        } else {
            Write-Host "  PASS: CURRENT_STATE.md entries within limit." -ForegroundColor Green
        }
    }
}

# 4. Check CHANGELOG.md & Documentation Matrix
Write-Host ""
Write-Host "[4/6] Checking CHANGELOG.md reminder..." -ForegroundColor Yellow
$st = @(& git -C $projectRoot status --porcelain 2>$null)
$changedCode = @($st | Where-Object { $_ -match '\.(cs|xaml)$' })
$changelogTouched = @($st | Where-Object { $_ -match 'CHANGELOG\.md' })

if ($changedCode.Count -gt 0 -and $changelogTouched.Count -eq 0) {
    Write-Host "  WARN: Code files changed ($($changedCode.Count)), but CHANGELOG.md was not updated!" -ForegroundColor Magenta
    Write-Host "        Remember to add a brief entry to agents/docs/CHANGELOG.md" -ForegroundColor Magenta
} else {
    Write-Host "  PASS: CHANGELOG.md check OK" -ForegroundColor Green
}

# 5. Validate Documentation
Write-Host ""
Write-Host "[5/6] Running validate-docs.ps1..." -ForegroundColor Yellow
$valScript = Join-Path $PSScriptRoot "validate-docs.ps1"
$valExit = 0
if (Test-Path $valScript) {
    & powershell -ExecutionPolicy Bypass -File $valScript
    $valExit = $LASTEXITCODE
}

# 6. Run arc-check.ps1
Write-Host ""
Write-Host "[6/6] Running arc-check.ps1 gatekeeper..." -ForegroundColor Yellow
$checkScript = Join-Path $PSScriptRoot "arc-check.ps1"
$checkExit = 0
if (Test-Path $checkScript) {
    & powershell -ExecutionPolicy Bypass -File $checkScript
    $checkExit = $LASTEXITCODE
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($valExit -eq 0 -and $checkExit -eq 0) {
    Write-Host "   SUCCESS: ALL CHECKS PASSED!        " -ForegroundColor Green
    Write-Host "   Ready to commit.                   " -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "   ATTENTION: Some checks had issues.  " -ForegroundColor Red
    Write-Host "   Please review the output above.     " -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    exit 1
}
