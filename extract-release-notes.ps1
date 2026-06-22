# =====================================================================
# extract-release-notes.ps1
# Parses MosquitoNetCalculator/Services/UpdateLog.cs and writes the
# LATEST UpdateItem (newest version) as a Markdown file. Target
# consumers: `vpk pack --releaseNotes publish\release-notes.md` which
# embeds the markdown into the GitHub Release notes.
#
# Project convention (per file comment in UpdateLog.cs):
#   "To add a new version, append a new UpdateItem to the END of _entries."
# So the LAST `new UpdateItem { ... }` block in the file is the version
# being shipped.
#
# Uses (?s) singleline regex mode so "." matches "\n". Pattern matches
# the block regardless of whether `{` is on the same line as `new
# UpdateItem` or on the next line (current file uses 2-line format).
# =====================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$SourceFile,
    [Parameter(Mandatory = $true)] [string]$OutputFile,
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SourceFile)) {
    Write-Error "Source file not found: $SourceFile"
    exit 1
}

Write-Host "  Reading $SourceFile..."

# Read source as UTF-8 (no BOM). Keep newlines so (?s) regex picks them up.
$content = [System.IO.File]::ReadAllText(
    (Resolve-Path -LiteralPath $SourceFile).Path,
    [System.Text.Encoding]::UTF8)

# Find every `new UpdateItem { ... }` block.
# (?s) -> singleline so . matches \n.
# Non-greedy .*? stops at the first `},?` followed by either another
# `new UpdateItem` or the closing `};` of the _entries list.
$pattern = '(?s)new UpdateItem\s*\{(.*?)\}\s*,?\s*(?=new UpdateItem|\};)'
$matches = [regex]::Matches($content, $pattern)

if ($matches.Count -eq 0) {
    Write-Error "No UpdateItem blocks found in $SourceFile"
    exit 2
}

$latest = $matches[$matches.Count - 1].Value
Write-Host "  Found $($matches.Count) UpdateItem blocks; using the last one"

# Helper: extract `Name = "value"` where value may contain `\"` escapes.
# Single-quoted PowerShell string -> no escape processing. In regex:
#   (?:[^"\\]|\\.)*    -> any char except " and \, OR \ followed by any char
function Get-Attr([string]$name, [string]$src) {
    $p = '(?s)' + [regex]::Escape($name) + '\s*=\s*"((?:[^"\\]|\\.)*)"'
    $m = [regex]::Match($src, $p)
    if (-not $m.Success) { return $null }
    $raw = $m.Groups[1].Value
    # Unescape \"  -> "    and    \\ -> \
    $raw = $raw -replace '\\"', '"'
    $raw = $raw -replace '\\\\', '\'
    return $raw
}

$version = Get-Attr "Version" $latest
$type    = Get-Attr "Type"    $latest
$title   = Get-Attr "Title"   $latest

if (-not $version -or -not $title) {
    Write-Error "Could not extract Version or Title from last UpdateItem block"
    exit 3
}

# Safety: if caller passed -ExpectedVersion (build.bat reads from csproj),
# verify that what we just extracted is what build.bat thinks is shipping.
# If not, the "latest = last block" rule has been broken by reformatting
# the file - fail loudly instead of silently shipping the wrong changelog.
if ($ExpectedVersion -and $ExpectedVersion -ne $version) {
    Write-Error ("Extracted version '$version' does not match expected " +
        "'$ExpectedVersion'. Services\UpdateLog.cs may have been " +
        "reformatted; latest-update detection is no longer reliable.")
    exit 5
}

# Extract the Changes list = new List<string> { "..." , "..." }
$changesMatch = [regex]::Match($latest,
    '(?s)Changes\s*=\s*new\s+List<string>\s*\{(.*?)\}\s*,?')
if (-not $changesMatch.Success) {
    Write-Error "Could not find Changes list in last UpdateItem block"
    exit 4
}

$changesBody = $changesMatch.Groups[1].Value
$changeLines = @()
foreach ($m in [regex]::Matches($changesBody, '"((?:[^"\\]|\\.)*)"')) {
    $line = $m.Groups[1].Value
    $line = $line -replace '\\"', '"'
    $line = $line -replace '\\\\', '\'
    $changeLines += $line
}

Write-Host "  Version: $version  Type: $type  Title: $title  Changes: $($changeLines.Count) items"

# Compose Markdown via here-string. Cleanest way to handle multi-line output
# without -join tricks. The here-string expands $version, $type, $title and
# the embedded expressions but all literal " and \ pass through unchanged.
$bulletList = ($changeLines | ForEach-Object { "- $_" }) -join "`n"

$md = @"
# v$version - $title

**Тип:** $type

**Что нового:**

$bulletList

_Сгенерировано автоматически из Services\UpdateLog.cs extract-release-notes.ps1_
"@

# Resolve OutputFile to absolute path; ensure parent exists.
$resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputFile)
$outDir = Split-Path -Parent $resolved
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Set-Content -Encoding UTF8 handles BOM/CRLF plumbing reliably.
Set-Content -LiteralPath $resolved -Value $md -Encoding UTF8

$bytes = (Get-Item -LiteralPath $resolved).Length
Write-Host "  Wrote: $resolved ($bytes bytes)"
exit 0
