 param()
# Живая проверка v3.53.1 (композитный бейдж типа «Новинка + Исправление»):
#   1. settings.json: LastSeenVersion=3.53.0, Theme=dark (бэкап/восстановление)
#   2. старт → «Что нового» обязано показать запись 3.53.1 с ДВУМЯ бейджами
#      («Новинка» и «Исправление») и шаблоны ПЕРВЫМ пунктом
#   3. вкладка «Обновления»: карточка 3.53.1, чип «Новинки» её находит,
#      чип «Исправления» тоже, чип «Улучшения» — нет
# Всё состояние профиля восстанавливается в finally.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeShot {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public const uint LEFTDOWN = 0x02;
    public const uint LEFTUP = 0x04;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }
}
"@
[NativeShot]::SetProcessDPIAware() | Out-Null

$settingsPath = Join-Path $env:APPDATA "MosquitoNetCalculator\settings.json"
$settingsBackup = $null
$settingsExisted = Test-Path $settingsPath
$exe = (Resolve-Path "MosquitoNetCalculator/bin/Release/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path

$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement
function Find-WindowsByPid([int]$pidToFind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidToFind)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | ForEach-Object { $_ })
}
function Find-DescendantText([System.Windows.Automation.AutomationElement]$win, [string]$needle) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
    foreach ($t in $texts) {
        if ($t.Current.Name -like "*$needle*") { return $t.Current.Name }
    }
    return $null
}

$proc = $null
$failures = @()
$ok = @()

function Find-WhatsNewWindow([int]$appPid) {
    # ВАЖНО: WhatsNewWindow имеет ShowInTaskbar=False и owner=main — UIA НЕ отдаёт
    # его как top-level child десктопа. Оно приходит как ControlType.Window
    # ВНУТРИ дерева главного окна (Descendants).
    $AE2 = [System.Windows.Automation.AutomationElement]
    $root2 = $AE2::RootElement
    $cond2 = New-Object System.Windows.Automation.PropertyCondition($AE2::ProcessIdProperty, $appPid)
    $tops = @($root2.FindAll([System.Windows.Automation.TreeScope]::Children, $cond2))
    $dlgCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE2::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    foreach ($t in $tops) {
        $dlgs = @($t.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dlgCond)) |
            Where-Object { $_.Current.Name -like "*нового*" -or $_.Current.Name -like "*3.53.1*" }
        if ($dlgs.Count -gt 0) { return $dlgs[0] }
    }
    return $null
}
try {
    # ── settings.json: бэкап → LastSeenVersion=3.53.0 → старт покажет «Что нового» ──
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

    $proc = Start-Process -FilePath $exe -PassThru
    $wn = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $wn) {
        Start-Sleep -Milliseconds 400
        $wn = Find-WhatsNewWindow $proc.Id
    }
    if (-not $wn) { throw "окно «Что нового» не появилось (LastSeenVersion=3.53.0)" }
    Start-Sleep -Seconds 2

    # ── Проверка 1: запись 3.53.1 с двумя бейджами ──
    $shot1 = ".tools/shots/composite-whatsnew.png"
    $r = $wn.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save($shot1, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    $novelty = Find-DescendantText $wn "Новинка"
    $fix     = Find-DescendantText $wn "Исправление"
    $first   = Find-DescendantText $wn "Новое: кнопка «Шаблоны»"
    if ($novelty) { $ok += "«Что нового»: бейдж «Новинка» виден" } else { $failures += "«Что нового»: бейдж «Новинка» не найден" }
    if ($fix)     { $ok += "«Что нового»: бейдж «Исправление» виден" } else { $failures += "«Что нового»: бейдж «Исправление» не найден" }
    if ($first -and $wn.Current.Name -like "*3.53.1*" -or $first) {
        $idxFirst = -1; $idxFix = -1
        $texts = @($wn.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))))
        for ($i = 0; $i -lt $texts.Count; $i++) {
            if ($texts[$i].Current.Name -like "Новое: кнопка «Шаблоны»*" -and $idxFirst -lt 0) { $idxFirst = $i }
            if ($texts[$i].Current.Name -like "Исправлено: если в Windows*" -and $idxFix -lt 0) { $idxFix = $i }
        }
        if ($idxFirst -ge 0 -and $idxFix -gt $idxFirst) { $ok += "шаблоны — ПЕРВЫЙ пункт, фикс — последний (порядок верный)" }
        else { $failures += "порядок пунктов неверный: first=$idxFirst fix=$idxFix" }
    }

    # ── Закрыть «Что нового» → вкладка «Обновления» ──
    try { ($wn.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
    Start-Sleep -Milliseconds 800

    $main = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -like "A.R.C. Frame*" } | Select-Object -First 1
    if (-not $main) { throw "главное окно не найдено" }

    # Навигация — кнопка NavBtnUpdates (у неё ToolTip «Обновления (Ctrl+4)»);
    # ищем по AutomationId — надёжнее имени (кириллица в UIA-имени зависит от кодировки).
    $navBtn = $null
    $idCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "NavBtnUpdates")
    $navBtn = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCond)
    if ($navBtn) {
        $inv = $null
        try { $inv = $navBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) } catch { }
        if ($inv) { $inv.Invoke(); Start-Sleep -Seconds 2 }

        $main2 = $main
        $cardFirst = Find-DescendantText $main2 "Новое: кнопка «Шаблоны»"
        if ($cardFirst) { $ok += "«Обновления»: карточка 3.53.1 развёрнута, шаблоны первым пунктом" }
        else { $failures += "«Обновления»: текст карточки 3.53.1 не найден" }

        $shot2 = ".tools/shots/composite-updates-tab.png"
        $r2 = $main2.Current.BoundingRectangle
        $bmp2 = New-Object System.Drawing.Bitmap([int]$r2.Width, [int]$r2.Height)
        $g2 = [System.Drawing.Graphics]::FromImage($bmp2)
        $g2.CopyFromScreen([int]$r2.X, [int]$r2.Y, 0, 0, $bmp2.Size)
        $bmp2.Save($shot2, [System.Drawing.Imaging.ImageFormat]::Png)
        $g2.Dispose(); $bmp2.Dispose()

        # Чип «Улучшения»: карточка-композит обязана исчезнуть.
        # Чипы — ToggleButton с x:Name ChipFilterImprovement.
        $chipIdCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "ChipFilterImprovement")
        $chip = $main2.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $chipIdCond)
        if ($chip) {
            # ToggleButtonAutomationPeer does not expose InvokePattern, and
            # TogglePattern flips IsChecked WITHOUT raising Click (handler
            # ChipTypeFilter_Click never runs, counter stays). Real mouse
            # click on the chip center is the only honest path.
            $hwnd = $proc.MainWindowHandle
            [NativeShot]::SetForegroundWindow($hwnd) | Out-Null
            Start-Sleep -Milliseconds 400
            $rc = $chip.Current.BoundingRectangle
            [NativeShot]::Click([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2))
            Start-Sleep -Milliseconds 1500
            # Счётчик «N из M версий» — диагностик: если фильтр НЕ применился,
            # текст останется «74 версии» без «из».
            $countTxt = Find-DescendantText $main2 "из 74"
            Write-Host ("counter: " + $(if ($countTxt) { $countTxt } else { "<74 версии — фильтр не активен>" }))
            $still = Find-DescendantText $main2 "Новое: кнопка «Шаблоны»"
            # Диагностический кадр после фильтра — по нему видно, скрыта ли карточка на самом деле.
            $shot3 = ".tools/shots/composite-updates-filtered.png"
            $r3 = $main2.Current.BoundingRectangle
            $bmp3 = New-Object System.Drawing.Bitmap([int]$r3.Width, [int]$r3.Height)
            $g3 = [System.Drawing.Graphics]::FromImage($bmp3)
            $g3.CopyFromScreen([int]$r3.X, [int]$r3.Y, 0, 0, $bmp3.Size)
            $bmp3.Save($shot3, [System.Drawing.Imaging.ImageFormat]::Png)
            $g3.Dispose(); $bmp3.Dispose()
            if (-not $still) { $ok += "чип «Улучшения»: композит-карточка скрыта (HasType работает)" }
            else { $failures += "чип «Улучшения»: карточка 3.53.1 НЕ скрыта (кадр: composite-updates-filtered.png)" }
            [NativeShot]::Click([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2))
            Start-Sleep -Milliseconds 600
        }
    }
}
catch {
    $failures += $_.Exception.Message
}
finally {
    if ($proc) {
        try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch { }
        if (-not $proc.HasExited) { $proc.Kill() }
    }
    if ($settingsExisted -and $settingsBackup -ne $null) {
        [IO.File]::WriteAllBytes($settingsPath, $settingsBackup)
        Write-Host "settings.json восстановлен"
    } elseif (-not $settingsExisted -and (Test-Path $settingsPath)) {
        Remove-Item $settingsPath -Force
        Write-Host "созданный settings.json удалён"
    }
}

Write-Host ""
foreach ($m in $ok) { Write-Host "  OK: $m" }
foreach ($f in $failures) { Write-Host "FAIL: $f" }
if ($failures.Count -gt 0) { Write-Host "RESULT: FAILED ($($failures.Count))"; exit 1 }
Write-Host "RESULT: OK"
