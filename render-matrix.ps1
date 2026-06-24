# render-matrix.ps1
# Generates DOCUMENTATION_MATRIX.md from documentation-matrix.json.
# Run after editing the JSON file.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File render-matrix.ps1

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot

$jsonPath = Join-Path $projectRoot "docs\arc\documentation-matrix.json"
$mdPath = Join-Path $projectRoot "docs\arc\DOCUMENTATION_MATRIX.md"

if (-not (Test-Path $jsonPath)) {
    Write-Host "FAIL: documentation-matrix.json not found" -ForegroundColor Red
    exit 1
}

$matrix = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Get-Category($file) {
    if ($file -match '^Models/')       { return "Models" }
    if ($file -match '^ViewModels/')   { return "ViewModels" }
    if ($file -match '^Services/')     { return "Services" }
    if ($file -match '^Controls/')     { return "Controls (WPF UI)" }
    if ($file -match '^Themes/')       { return "Themes" }
    if ($file -match '^Resources/')    { return "Resources" }
    if ($file -match '\.csproj$|releases\.json|build\.bat|installer\.iss') { return "Project / Config" }
    if ($file -match 'Tests\.cs$')     { return "Tests" }
    return "Other"
}

$categories = [ordered]@{
    "Models" = @()
    "ViewModels" = @()
    "Services" = @()
    "Controls (WPF UI)" = @()
    "Themes" = @()
    "Resources" = @()
    "Project / Config" = @()
    "Tests" = @()
}

foreach ($m in $matrix.mappings) {
    $cat = Get-Category $m.file
    if (-not $categories[$cat]) { $categories[$cat] = @() }
    $categories[$cat] += $m
}

$mdLines = @()
$mdLines += "# DOCUMENTATION_MATRIX.md"
$mdLines += ""
$mdLines += "> Auto-generated from documentation-matrix.json. Edit the JSON, then run render-matrix.ps1."
$mdLines += ""
$mdLines += "## File -> Docs mapping"
$mdLines += ""
$mdLines += "Use on the **Document** phase. If you changed a file in the left column, update all files in the right column."
$mdLines += ""
$mdLines += "Or run: what-to-update.ps1 (git diff --name-only) -- the script reads documentation-matrix.json."
$mdLines += ""

foreach ($cat in $categories.GetEnumerator()) {
    if ($cat.Value.Count -eq 0) { continue }
    $mdLines += "### $($cat.Key)"
    $mdLines += ""
    $mdLines += "| Changed file | Update docs |"
    $mdLines += "|---|---|"
    foreach ($m in $cat.Value) {
        $file = "``$($m.file)``"
        $docs = ($m.docs | ForEach-Object { "``$_``" }) -join ", "
        if ($m.note) {
            $docs += " ($($m.note))"
        }
        $mdLines += "| $file | $docs |"
    }
    $mdLines += ""
}

$date = Get-Date -Format "yyyy-MM-dd"
$mdLines += "---"
$mdLines += ""
$mdLines += "## Auto-update of 'Last verified'"
$mdLines += ""
$mdLines += "Use what-to-update.ps1 to get the list of docs to update - the script reads documentation-matrix.json directly."
$mdLines += ""
$mdLines += "## Source files"
$mdLines += ""
$mdLines += "- docs/arc/documentation-matrix.json -- machine-readable source (edit this!)"
$mdLines += "- render-matrix.ps1 -- generates this file from JSON"
$mdLines += ""
$mdLines += "## Last verified"
$mdLines += ""
$mdLines += "$date (generated from JSON)"

$md = $mdLines -join "`n"
[System.IO.File]::WriteAllText($mdPath, $md, [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated: $mdPath" -ForegroundColor Green
Write-Host "Entries: $($matrix.mappings.Count) mappings" -ForegroundColor Gray
