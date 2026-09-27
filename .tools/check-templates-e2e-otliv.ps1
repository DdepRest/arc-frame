param()
# E2E-проверка шаблона «Окно» с включённым отливом (v3.54):
#   1. открыть «Шаблоны» → «Окно»
#   2. размеры сетки 500×1500×1 (списки НЕ трогаем — регрессия v3.54-fix2)
#   3. включить строку «Отлив», размеры 800×120×1
#   4. «Добавить в заказ» → в таблице расчёта 4 позиции: Anwis/ПСУЛ/Доставка/Отлив
#   5. Ctrl+Z → одним шагом откатываются ВСЕ 4 позиции
# Скриншоты: .tools/shots/templates-e2e-*.png
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class NativeE2E {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtra);
    public static List<string> WindowsOfPid(uint pid) {
        var list = new List<string>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid) {
                var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256);
                list.Add(h + "|" + IsWindowVisible(h) + "|" + sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static void SendCtrlZ() {
        const uint KEYUP = 0x0002;
        keybd_event(0x11, 0, 0, UIntPtr.Zero);            // Ctrl down
        keybd_event(0x5A, 0, 0, UIntPtr.Zero);            // Z down
        keybd_event(0x5A, 0, KEYUP, UIntPtr.Zero);        // Z up
        keybd_event(0x11, 0, KEYUP, UIntPtr.Zero);        // Ctrl up
    }
}
"@
[NativeE2E]::SetProcessDPIAware() | Out-Null

$exe = (Resolve-Path "MosquitoNetCalculator/bin/Release/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path
$proc = Start-Process -FilePath $exe -PassThru
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

function Find-WindowsByPid([int]$pidToFind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidToFind)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | ForEach-Object { $_ })
}

# Дамп видимых элементов окна: Type|Name|X|Y|W|H.
function Dump-Visible($ae) {
    $rows = @()
    foreach ($el in $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $r = $el.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { continue }
        $rows += [pscustomobject]@{
            El = $el
            Type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType.', ''
            Name = ($el.Current.Name -replace "`r`n", " ")
            X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height
        }
    }
    return $rows
}

# Edit-поле размерного поля: подпись и бокс в одной колонке (близкие X), бокс ниже подписи.
function Find-DimEdit($rows, $labelName, $minY) {
    $lbl = $rows | Where-Object { $_.Type -eq "Text" -and $_.Name -eq $labelName -and $_.Y -ge $minY } |
        Sort-Object Y | Select-Object -First 1
    if (-not $lbl) { return $null }
    return $rows | Where-Object {
        $_.Type -eq "Edit" -and
        [math]::Abs($_.X - $lbl.X) -lt 10 -and
        $_.Y -gt $lbl.Y -and ($_.Y - $lbl.Y) -lt 60
    } | Sort-Object Y | Select-Object -First 1
}

function Set-Dim($rows, $labelName, $value, $minY) {
    $box = Find-DimEdit $rows $labelName $minY
    if (-not $box) { throw "поле «$labelName» (minY=$minY) не найдено" }
    ($box.El.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue([string]$value)
    Start-Sleep -Milliseconds 150
}

function Invoke-Button([System.Windows.Automation.AutomationElement]$el) {
    ($el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}

# Строки таблицы расчёта: тексты внутри OrderGrid, сгруппированные по Y.
function Get-GridRows($main) {
    $gridCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "OrderGrid")
    $grid = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $gridCond)
    if (-not $grid) { throw "таблица расчёта (OrderGrid) не найдена" }
    $gr = $grid.Current.BoundingRectangle
    $map = @{}
    foreach ($el in $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $r = $el.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { continue }
        if ($el.Current.ControlType.ProgrammaticName -notmatch 'Text') { continue }
        $key = [math]::Floor(($r.Y - $gr.Y) / 20)
        if (-not $map.ContainsKey($key)) { $map[$key] = "" }
        $map[$key] = ($map[$key] + " " + $el.Current.Name)
    }
    return @($map.Values | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
}

$main = $null
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $main) {
    Start-Sleep -Milliseconds 400
    $wins = Find-WindowsByPid $proc.Id
    if ($wins.Count -gt 0) { $main = $wins[0] }
}
if (-not $main) { throw "main window not found for pid $($proc.Id)" }
Write-Host "main window: '$($main.Current.Name)'"
Start-Sleep -Seconds 2

for ($i = 0; $i -lt 5; $i++) {
    $wn = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -like "Что нового*" } | Select-Object -First 1
    if (-not $wn) { break }
    try { ($wn.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
    Start-Sleep -Milliseconds 500
}

$failures = @()

try {
    # ── 1. Открыть «Шаблоны» → карточку «Окно» ──
    $btnCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $btn = $null
    $swB = [Diagnostics.Stopwatch]::StartNew()
    while ($swB.Elapsed.TotalSeconds -lt 8 -and -not $btn) {
        Start-Sleep -Milliseconds 400
        $btn = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.AndCondition($btnCond,
                (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "BtnTemplates")))))
    }
    if (-not $btn) { throw "кнопка «Шаблоны» не найдена" }
    Invoke-Button $btn

    # Win32-заголовок надёжнее UIA-дерева: диалог — не ребёнок главного окна,
    # а отдельное hwnd; GetWindowTextW с CharSet.Unicode (см. ловушку v3.53).
    $tplHwnd = [IntPtr]::Zero
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw2.Elapsed.TotalSeconds -lt 10 -and $tplHwnd -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        foreach ($line in [NativeE2E]::WindowsOfPid([uint32]$proc.Id)) {
            $parts = $line -split '\|'
            if ($parts.Count -ge 3 -and $parts[1] -eq 'True' -and $parts[2] -eq 'Шаблоны') { $tplHwnd = [IntPtr][long]$parts[0]; break }
        }
    }
    if ($tplHwnd -eq [IntPtr]::Zero) { throw "окно «Шаблоны» не открылось" }
    $tplWin = [System.Windows.Automation.AutomationElement]::FromHandle($tplHwnd)

    $cardCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Шаблон Окно")),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $card = $null
    $swC = [Diagnostics.Stopwatch]::StartNew()
    while ($swC.Elapsed.TotalSeconds -lt 6 -and -not $card) {
        Start-Sleep -Milliseconds 400
        $card = $tplWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cardCond)
    }
    if (-not $card) { throw "карточка «Шаблон Окно» не найдена" }
    Invoke-Button $card
    Start-Sleep -Milliseconds 900
    Write-Host "checklist opened"

    # ── 2. Размеры сетки 500×1500×1 (первая строка «Сетка»; списки не трогаем) ──
    $rows = Dump-Visible $tplWin
    $gridTitle = $rows | Where-Object { $_.Type -eq "Text" -and $_.Name -eq "Сетка" } | Sort-Object Y | Select-Object -First 1
    if (-not $gridTitle) { throw "строка «Сетка» не найдена" }
    $minY = $gridTitle.Y - 10
    Set-Dim $rows "Ширина, мм" 500 $minY
    Set-Dim $rows "Высота, мм" 1500 $minY
    Set-Dim $rows "Кол-во" 1 $minY

    # ── 3. «Отлив» включён ПО УМОЛЧАНИЮ (v3.53.1, решение владельца):
    # проверяем состояние переключателя и НЕ трогаем его. Поля строки
    # уже раскрыты — размеры вводим сразу.
    $rows = Dump-Visible $tplWin
    $otliv = $rows | Where-Object { $_.Type -eq "Text" -and $_.Name -eq "Отлив" } | Sort-Object Y | Select-Object -First 1
    if (-not $otliv) { throw "строка «Отлив» не найдена" }
    $otlivToggle = $rows | Where-Object {
        $_.Type -eq "CheckBox" -and [math]::Abs($_.Y - $otliv.Y) -lt 25 -and $_.X -lt $otliv.X
    } | Sort-Object X | Select-Object -First 1
    if (-not $otlivToggle) { throw "переключатель строки «Отлив» не найден" }
    $tp = $otlivToggle.El.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
        throw "отлив должен быть включён по умолчанию (ToggleState=$($tp.Current.ToggleState))"
    }
    Write-Host "отлив включён по умолчанию — OK"

    # Размеры отлива 800×120×1 — подписи ниже заголовка строки «Отлив».
    $rows = Dump-Visible $tplWin
    $otlivY = $otliv.Y
    Set-Dim $rows "Ширина, мм" 800 $otlivY
    Set-Dim $rows "Высота, мм" 120 $otlivY
    Set-Dim $rows "Кол-во" 1 $otlivY

    # ── 4. «Добавить в заказ» (имя кнопки может быть пустым — ищем по тексту-потомку) ──
    $tplButtons = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) | ForEach-Object { $_ })
    $apply = $tplButtons | Where-Object {
        $_.Current.Name -match "Добавить в заказ" -or
        ($_.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Добавить в заказ"))) -ne $null)
    } | Select-Object -First 1
    if (-not $apply) { throw "кнопка «Добавить в заказ» не найдена" }
    Invoke-Button $apply

    $closed = $false
    $sw3 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw3.Elapsed.TotalSeconds -lt 6) {
        Start-Sleep -Milliseconds 400
        $still = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -eq "Шаблоны" } | Select-Object -First 1
        if (-not $still) { $closed = $true; break }
    }
    if (-not $closed) { $failures += "«Добавить в заказ» не закрыл окно (валидация не пропустила)" }
    Write-Host "apply done (closed=$closed)"

    # ── Состав таблицы расчёта ──
    Start-Sleep -Milliseconds 800
    $gridRows = Get-GridRows $main
    $all = ($gridRows -join " `n ")
    Write-Host "--- строки таблицы после добавления ---"
    foreach ($r in $gridRows) { Write-Host ("  · " + $r) }

    foreach ($p in @("Anwis", "ПСУЛ", "Доставка", "Отлив")) {
        if (-not ($gridRows | Where-Object { $_ -match [regex]::Escape($p) })) { $failures += "позиция «$p» не появилась в таблице" }
    }
    $expected = 4
    if ($gridRows.Count -lt $expected) { $failures += "строк в таблице $($gridRows.Count), ожидалось ≥ $expected" }

    # Скриншот главного окна с заполненной таблицей.
    $winR = $main.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$winR.Width, [int]$winR.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$winR.X, [int]$winR.Y, 0, 0, $bmp.Size)
    $bmp.Save(".tools/shots/templates-e2e-added.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    # ── 5. Ctrl+Z — один шаг откатывает весь шаблон ──
    $mainHwnd = [IntPtr]::Zero
    foreach ($line in [NativeE2E]::WindowsOfPid([uint32]$proc.Id)) {
        $parts = $line -split '\|'
        if ($parts.Count -ge 3 -and $parts[1] -eq 'True' -and $parts[2] -eq $main.Current.Name) { $mainHwnd = [IntPtr][long]$parts[0]; break }
    }
    if ($mainHwnd -eq [IntPtr]::Zero) { throw "hwnd главного окна не найден" }
    [NativeE2E]::SetForegroundWindow($mainHwnd) | Out-Null
    Start-Sleep -Milliseconds 300
    [NativeE2E]::SendCtrlZ()
    Write-Host "Ctrl+Z sent"

    $undone = $false
    $sw4 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw4.Elapsed.TotalSeconds -lt 6) {
        Start-Sleep -Milliseconds 400
        $gridRows2 = Get-GridRows $main
        $left = @($gridRows2 | Where-Object { $_ -match "Anwis|ПСУЛ|Доставка|Отлив" })
        if ($left.Count -eq 0) { $undone = $true; break }
    }
    if (-not $undone) {
        $left = @(Get-GridRows $main | Where-Object { $_ -match "Anwis|ПСУЛ|Доставка|Отлив" })
        $failures += "Ctrl+Z не откатил шаблон одним шагом (осталось строк: $($left.Count))"
    }

    $bmp2 = New-Object System.Drawing.Bitmap([int]$winR.Width, [int]$winR.Height)
    $g2 = [System.Drawing.Graphics]::FromImage($bmp2)
    $g2.CopyFromScreen([int]$winR.X, [int]$winR.Y, 0, 0, $bmp2.Size)
    $bmp2.Save(".tools/shots/templates-e2e-undone.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g2.Dispose(); $bmp2.Dispose()
    Write-Host "screenshots: templates-e2e-added.png / templates-e2e-undone.png"

    if ($failures.Count -gt 0) { throw "ПРОВЕРКИ ПРОВАЛЕНЫ:`n" + ($failures -join "`n") }
    Write-Host "RESULT: OK — 4 позиции добавлены (Anwis/ПСУЛ/Доставка/Отлив), Ctrl+Z откатил всё одним шагом"
}
finally {
    try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch { }
    if (-not $proc.HasExited) { $proc.Kill() }
}
