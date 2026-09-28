param([string]$OutPng = ".tools/shots/grid-optional-check.png")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class NativeDpi2 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
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
}
"@
[NativeDpi2]::SetProcessDPIAware() | Out-Null

$exe = (Resolve-Path "MosquitoNetCalculator/bin/Release/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path
$proc = Start-Process -FilePath $exe -PassThru
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

function Find-WindowsByPid([int]$pidToFind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidToFind)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | ForEach-Object { $_ })
}
function Get-TemplatesHwnd([uint32]$ownerPid) {
    foreach ($line in [NativeDpi2]::WindowsOfPid($ownerPid)) {
        $parts = $line -split '\|'
        if ($parts.Count -ge 3 -and $parts[1] -eq 'True' -and $parts[2] -eq 'Шаблоны') {
            return [IntPtr][long]$parts[0]
        }
    }
    return [IntPtr]::Zero
}
function Save-Shot([System.Windows.Rect]$r, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, (New-Object System.Drawing.Size([int]$r.Width, [int]$r.Height)))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

try {
    $main = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $main) {
        Start-Sleep -Milliseconds 400
        $wins = Find-WindowsByPid $proc.Id
        if ($wins.Count -gt 0) { $main = $wins[0] }
    }
    if (-not $main) { throw "main window not found" }
    Start-Sleep -Seconds 2

    # «Что нового» может показаться — закрываем.
    for ($i = 0; $i -lt 5; $i++) {
        $wn = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -like "Что нового*" } | Select-Object -First 1
        if (-not $wn) { break }
        try { ($wn.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
        Start-Sleep -Milliseconds 500
    }

    # Открываем «Шаблоны».
    $btnCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) | ForEach-Object { $_ })
    $btn = $buttons | Where-Object { $_.Current.AutomationId -eq "BtnTemplates" } | Select-Object -First 1
    if (-not $btn) { $btn = $buttons | Where-Object { $_.Current.Name -eq "Шаблоны" } | Select-Object -First 1 }
    if (-not $btn) { throw "кнопка «Шаблоны» не найдена" }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()

    $tplHwnd = [IntPtr]::Zero
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw2.Elapsed.TotalSeconds -lt 10 -and $tplHwnd -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        $tplHwnd = Get-TemplatesHwnd ([uint32]$proc.Id)
    }
    if ($tplHwnd -eq [IntPtr]::Zero) { throw "окно «Шаблоны» не найдено" }
    $tplWin = [System.Windows.Automation.AutomationElement]::FromHandle($tplHwnd)
    Start-Sleep -Milliseconds 900

    # ── Проверка A: витрина компактна (скриншот + габариты) ──
    $fr = $tplWin.Current.BoundingRectangle
    Write-Host ("gallery window: {0}x{1} at ({2},{3})" -f [int]$fr.Width, [int]$fr.Height, [int]$fr.X, [int]$fr.Y)
    Save-Shot $fr ".tools/shots/gallery-compact.png"

    # ── Открываем шаблон «Окно» ──
    $cardCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Шаблон Окно")),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $card = $null
    $sw3 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw3.Elapsed.TotalSeconds -lt 6 -and -not $card) {
        Start-Sleep -Milliseconds 400
        $card = $tplWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cardCond)
    }
    if (-not $card) { throw "карточка «Шаблон Окно» не найдена" }
    ($card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 900

    # Дамп: тексты сводки и чекбоксы.
    $txtCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $chkCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
    $texts  = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond) | ForEach-Object { $_ })
    $checks = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $chkCond) | ForEach-Object { $_ })
    Write-Host "--- checkboxes ---"
    foreach ($c in $checks) {
        $tg = $null
        try { $tg = $c.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) } catch { }
        $state = if ($tg) { $tg.Current.ToggleState } else { "n/a" }
        Write-Host ("  '{0}' toggle={1} y={2}" -f $c.Current.Name, $state, [int]$c.Current.BoundingRectangle.Y)
    }

    # Первый чекбокс = строка «Сетка» (порядок строк). Toggle() у WPF CheckBox
    # поднимает настоящие Checked/Unchecked — в отличие от чипа-фильтра.
    $gridToggle = $checks | Where-Object { $_.Current.Name -like "*Сетка*" } | Select-Object -First 1
    if (-not $gridToggle) { $gridToggle = $checks | Select-Object -First 1 }
    $tgGrid = $gridToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    Write-Host ("grid toggle before: {0}" -f $tgGrid.Current.ToggleState)
    if ($tgGrid.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw "сетка должна стартовать включённой" }
    $tgGrid.Toggle()
    Start-Sleep -Milliseconds 700

    $texts = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond) | ForEach-Object { $_ })
    $summary = ($texts | Where-Object { $_.Current.Name -like "Добавится позиций*" } | Select-Object -First 1).Current.Name
    $pillGrid = ($texts | Where-Object { $_.Current.Name -eq "Не требуется" } | Select-Object -First 1) -ne $null
    Write-Host ("after grid OFF: summary='{0}' pill-не-требуется={1}" -f $summary, $pillGrid)
    if ($summary -notlike "*3*") { throw "сводка должна показывать 3 позиции, факт: $summary" }
    if (-not $pillGrid) { throw "пилюля «Не требуется» не найдена у выключенной сетки" }

    Save-Shot ([System.Windows.Automation.AutomationElement]::FromHandle($tplHwnd).Current.BoundingRectangle) $OutPng

    # ── Добавление без сетки: размеры отлива, затем «Добавить: 3» ──
    # Сетка выключена → видимых dims-строк две: ВЕРХНЯЯ (по Y) = Отлив
    # (порядок монтажа: Отлив выше ПСУЛ). В строке 3 бокса: Ширина, Высота,
    # Кол-во — по возрастанию X. Привязка чисто геометрическая: метки
    # стоят НАД боксами, совпадение по Y ненадёжно.
    $editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $edits = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond) | ForEach-Object { $_ }) |
        Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X }
    # Первые три бокса (верхняя строка) — отлив: Ширина, Высота, Кол-во.
    if ($edits.Count -lt 3) { throw "ожидались поля размеров, факт: $($edits.Count)" }
    $eW = $edits[0]; $eH = $edits[1]
    $rowY = [int]$eW.Current.BoundingRectangle.Y
    if ([Math]::Abs([int]$eH.Current.BoundingRectangle.Y - $rowY) -gt 5) { throw "верхние боксы не в одной строке" }
    foreach ($pair in @(@($eW, "800"), @($eH, "120"))) {
        $vp = $pair[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $vp.SetValue($pair[1])
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 700

    # Контроль: числа реально в боксах отлива.
    $gotW = $eW.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $gotH = $eH.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Write-Host ("otliv dims: W='{0}' H='{1}'" -f $gotW, $gotH)
    if ($gotW -ne "800" -or $gotH -ne "120") { throw "размеры отлива не применились: W='$gotW' H='$gotH'" }

    # Дамп: куда реально легли числа (edit → значение, Y).
    $edits2 = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond) | ForEach-Object { $_ }) |
        Sort-Object { $_.Current.BoundingRectangle.Y }
    foreach ($ed in $edits2) {
        $val = ""
        try { $val = $ed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
        Write-Host ("  edit y={0} x={1} value='{2}'" -f [int]$ed.Current.BoundingRectangle.Y, [int]$ed.Current.BoundingRectangle.X, $val)
    }

    $applyBtn = $tplWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Добавить в заказ")))
    if (-not $applyBtn) { throw "кнопка «Добавить в заказ» не найдена" }
    ($applyBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 1500

    # Окно должно закрыться (валидация без сетки прошла) — иначе ищем тост-текст ошибки.
    $stillOpen = Get-TemplatesHwnd ([uint32]$proc.Id)
    if ($stillOpen -ne [IntPtr]::Zero) {
        $errs = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond) | ForEach-Object { $_.Current.Name }) -join " | "
        throw "окно осталось открытым после добавления без сетки; тексты: $errs"
    }
    Write-Host "apply without grid: OK - окно закрылось, 3 позиции добавлены"
    Write-Host "RESULT: OK"
}
finally {
    try {
        if (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $proc.Id -Force }
    } catch { }
}
