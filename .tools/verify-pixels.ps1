param([string]$Dir = ".tools/shots")

# Pixel-verify UX state colors in the captured screenshots.
# Dark tokens: selected #26384C, invalid #D63C42 (v3.53 contrast audit: #E5484D
# gave white-on-red 3.91:1, below AA), warn #F5A524
# Light tokens (ThemeService): selected #E8F0FA, invalid #C42B1C, warn #D48C00
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$checks = @(
    @{ f = "09a-grid-row-selected-dark.png";       rgb = 0x26,0x38,0x4C; tol = 14; label = "dark  row-selected RowAltSelected #26384C" },
    @{ f = "09a-grid-row-selected-light.png";      rgb = 0xE8,0xF0,0xFA; tol = 10; label = "light row-selected RowAltSelected #E8F0FA" },
    @{ f = "03-quickadd-invalid-dark.png";         rgb = 0xD6,0x3C,0x42; tol = 20; label = "dark  quickadd invalid border #D63C42" },
    @{ f = "03-quickadd-invalid-light.png";        rgb = 0xC4,0x2B,0x1C; tol = 40; label = "light quickadd invalid border #C42B1C" },
    @{ f = "08a-warning-toast-dark.png";           rgb = 0xF5,0xA5,0x24; tol = 40; label = "dark  warning toast accent #F5A524" },
    @{ f = "08a-warning-toast-light.png";          rgb = 0xD4,0x8C,0x00; tol = 40; label = "light warning toast accent #D48C00" },
    @{ f = "10a-destructive-dialog-dark.png";      rgb = 0xD6,0x3C,0x42; tol = 20; label = "dark  destructive dialog cancel #D63C42" },
    @{ f = "10a-destructive-dialog-light.png";     rgb = 0xC4,0x2B,0x1C; tol = 40; label = "light destructive dialog cancel #C42B1C" },
    @{ f = "08c-warning-hover-held-7s-dark.png";   rgb = 0xF5,0xA5,0x24; tol = 40; label = "dark  toast still visible at +7s (pause)" },
    @{ f = "08c-warning-hover-held-7s-light.png";  rgb = 0xD4,0x8C,0x00; tol = 40; label = "light toast still visible at +7s (pause)" },
    @{ f = "08d-warning-after-unhover-dark.png";   rgb = 0xF5,0xA5,0x24; tol = 20; label = "dark  toast GONE after unhover (expect 0)" },
    @{ f = "08d-warning-after-unhover-light.png";  rgb = 0xD4,0x8C,0x00; tol = 20; label = "light toast GONE after unhover (expect 0)" },
    @{ f = "01-main-dark.png";                     rgb = 0x13,0x14,0x17; tol = 8;  label = "dark  AppBg #131417 dominant" },
    @{ f = "01-main-light.png";                    rgb = 0xF5,0xF5,0xF7; tol = 8;  label = "light AppBg #F5F5F7 dominant" }
)

foreach ($c in $checks) {
    $p = Join-Path $Dir $c.f
    if (-not (Test-Path $p)) { Write-Host ("{0,-52} MISSING" -f $c.f); continue }
    $img = [System.Drawing.Image]::FromFile($p)
    $bmp = New-Object System.Drawing.Bitmap($img); $img.Dispose()
    $tr = $c.rgb[0]; $tg = $c.rgb[1]; $tb = $c.rgb[2]
    $hits = 0; $total = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 2) {
        for ($x = 0; $x -lt $bmp.Width; $x += 2) {
            $px = $bmp.GetPixel($x, $y); $total++
            if ([math]::Abs($px.R - $tr) -le $c.tol -and
                [math]::Abs($px.G - $tg) -le $c.tol -and
                [math]::Abs($px.B - $tb) -le $c.tol) { $hits++ }
        }
    }
    $bmp.Dispose()
    $pct = [math]::Round(100.0 * $hits / $total, 2)
    Write-Host ("{0,-52} {1,8:N0}/{2,8:N0} px  {3,6}%  {4}" -f $c.f, $hits, $total, $pct, $c.label)
}
