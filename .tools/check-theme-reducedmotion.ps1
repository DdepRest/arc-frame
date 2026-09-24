param()
# Живая проверка фикса v3.53.0 (тема при выключенных анимациях):
#   1. SPI_SETCLIENTAREAANIMATION=off — то самое «отключить анимации» Windows
#   2. settings.json → Theme=dark (бэкап и восстановление оригинала)
#   3. запуск приложения → пиксельная проба контента
# БАГ (до фикса): контент оставался светлым (#FFFFFF/#F3F3F3) — дефолт
# Brushes.xaml, у тёмных пользователей светлый контент + тёмный сайдбар.
# ФИКС: контент тёмный (#131417/#1E2025).
# Всё состояние системы восстанавливается в finally.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeTheme {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref uint value, uint ini);
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;
    public static bool GetClientAreaAnimation() { uint v = 1; SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref v, 0); return v != 0; }
    public static void SetClientAreaAnimation(bool on) { uint v = on ? 1u : 0u; SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, ref v, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE); }
}
"@
[NativeTheme]::SetProcessDPIAware() | Out-Null

$settingsPath = Join-Path $env:APPDATA "MosquitoNetCalculator\settings.json"
$settingsBackup = $null
$settingsExisted = Test-Path $settingsPath
$animOriginal = [NativeTheme]::GetClientAreaAnimation()
$exe = (Resolve-Path "MosquitoNetCalculator/bin/Release/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path

$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement
function Find-WindowsByPid([int]$pidToFind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidToFind)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | ForEach-Object { $_ })
}

$proc = $null
$failures = @()
try {
    Write-Host ("animations: {0} -> OFF" -f $animOriginal)
    [NativeTheme]::SetClientAreaAnimation($false)
    if ([NativeTheme]::GetClientAreaAnimation()) { throw "не удалось выключить системные анимации" }

    # ── settings.json: бэкап → Theme=dark, без приветствия/«Что нового» ──
    if ($settingsExisted) {
        $settingsBackup = [IO.File]::ReadAllBytes($settingsPath)
        $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
        $json.Theme = "dark"
        $json.FirstRunComplete = $true
        if ($json.PSObject.Properties["LastSeenVersion"]) { $json.LastSeenVersion = "3.53.0" }
        else { $json | Add-Member -NotePropertyName LastSeenVersion -NotePropertyValue "3.53.0" }
        [IO.File]::WriteAllText($settingsPath, ($json | ConvertTo-Json -Depth 10))
    } else {
        [IO.File]::WriteAllText($settingsPath, '{"Theme":"dark","FirstRunComplete":true,"LastSeenVersion":"3.53.0"}')
    }
    Write-Host "settings: Theme=dark (оригинал в памяти)"

    # ── Запуск (свежий процесс прочитает ClientAreaAnimation=false и Theme=dark) ──
    $proc = Start-Process -FilePath $exe -PassThru
    $main = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $main) {
        Start-Sleep -Milliseconds 400
        $wins = Find-WindowsByPid $proc.Id
        if ($wins.Count -gt 0) { $main = $wins[0] }
    }
    if (-not $main) { throw "главное окно не найдено (pid $($proc.Id))" }
    Start-Sleep -Seconds 2
    for ($i = 0; $i -lt 5; $i++) {
        $wn = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -like "Что нового*" } | Select-Object -First 1
        if (-not $wn) { break }
        try { ($wn.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
        Start-Sleep -Milliseconds 400
    }
    Start-Sleep -Milliseconds 800

    # ── Пиксельные пробы ──
    $r = $main.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save(".tools/shots/theme-reducedmotion.png", [System.Drawing.Imaging.ImageFormat]::Png)

    function Sample([double]$fx, [double]$fy) {
        $c = $bmp.GetPixel([int]($r.Width * $fx), [int]($r.Height * $fy))
        return $c
    }
    $contentA = Sample 0.15 0.50   # пустая зона карточки контента (Surface)
    $contentB = Sample 0.70 0.235  # правая часть карточки «Добавить позицию»
    $sidebar  = Sample 0.05 0.45   # сайдбар (информационно)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ("content A (0.15,0.50): #{0:X2}{1:X2}{2:X2}" -f $contentA.R, $contentA.G, $contentA.B)
    Write-Host ("content B (0.70,0.235): #{0:X2}{1:X2}{2:X2}" -f $contentB.R, $contentB.G, $contentB.B)
    Write-Host ("sidebar  (0.05,0.45): #{0:X2}{1:X2}{2:X2}" -f $sidebar.R, $sidebar.G, $sidebar.B)

    # ЯДРО ПРОВЕРКИ: контент обязан быть ТЁМНЫМ (до фикса оставался светлым).
    $darkA = ($contentA.R -lt 70 -and $contentA.G -lt 70 -and $contentA.B -lt 70)
    $darkB = ($contentB.R -lt 70 -and $contentB.G -lt 70 -and $contentB.B -lt 70)
    if (-not ($darkA -or $darkB)) {
        $failures += "контент светлый при Theme=dark и выключенных анимациях (дефолт Brushes.xaml — баг v3.53.0)"
    }
    if ($darkA -and $darkB) { Write-Host "theme-check: контент тёмный в обеих пробах" }
}
catch {
    $failures += $_.Exception.Message
}
finally {
    if ($proc) {
        try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch { }
        if (-not $proc.HasExited) { $proc.Kill() }
    }
    # ── Восстановление состояния машины ──
    if ($settingsExisted -and $settingsBackup -ne $null) {
        [IO.File]::WriteAllBytes($settingsPath, $settingsBackup)
        Write-Host "settings.json восстановлен"
    } elseif (-not $settingsExisted -and (Test-Path $settingsPath)) {
        Remove-Item $settingsPath -Force
        Write-Host "синтетический settings.json удалён"
    }
    [NativeTheme]::SetClientAreaAnimation($animOriginal)
    Write-Host ("animations восстановлены: {0}" -f [NativeTheme]::GetClientAreaAnimation())
}

if ($failures.Count -gt 0) { throw "ПРОВЕРКИ ПРОВАЛЕНЫ:`n" + ($failures -join "`n") }
Write-Host "RESULT: OK — тёмная тема применилась при выключенных системных анимациях"
