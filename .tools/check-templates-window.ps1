param([string]$OutPng = ".tools/shots/templates-window-check.png")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class NativeDpi {
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
[NativeDpi]::SetProcessDPIAware() | Out-Null

$exe = (Resolve-Path "MosquitoNetCalculator/bin/Release/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path
$proc = Start-Process -FilePath $exe -PassThru
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

function Find-WindowsByPid([int]$pidToFind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidToFind)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | ForEach-Object { $_ })
}

try {
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

    # «Что нового» может показаться — закрываем WindowPattern.Close.
    for ($i = 0; $i -lt 5; $i++) {
        $wn = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -like "Что нового*" } | Select-Object -First 1
        if (-not $wn) { break }
        try { ($wn.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
        Start-Sleep -Milliseconds 500
    }

    # Все кнопки окна — для диагностики.
    $btnTypeCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnTypeCond) | ForEach-Object { $_ })
    Write-Host "--- buttons in main window ---"
    foreach ($b in $buttons) { Write-Host ("  [{0}] '{1}'" -f $b.Current.AutomationId, $b.Current.Name) }

    # Ищем кнопку «Шаблоны» по AutomationId, затем по имени.
    $btn = $buttons | Where-Object { $_.Current.AutomationId -eq "BtnTemplates" } | Select-Object -First 1
    if (-not $btn) { $btn = $buttons | Where-Object { $_.Current.Name -eq "Шаблоны" } | Select-Object -First 1 }
    if (-not $btn) { throw "кнопка «Шаблоны» не найдена среди $($buttons.Count) кнопок" }
    Write-Host "clicking: '$($btn.Current.Name)' (AutomationId=$($btn.Current.AutomationId))"
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()

    # Окно может появиться не мгновенно (StoryBoard, ShowDialog) и не сразу
    # попасть в кэш UIA — ждём с повторными опросами до 10 с.
    $tplWin = $null
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw2.Elapsed.TotalSeconds -lt 10 -and -not $tplWin) {
        Start-Sleep -Milliseconds 500
        $tplWin = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -eq "Шаблоны" } | Select-Object -First 1
    }

    Write-Host "--- win32 windows after click ---"
    $tplHwnd = [IntPtr]::Zero
    $scriptLiteral = [int[]][char[]]'Шаблоны'
    Write-Host ("  script literal codes: " + (($scriptLiteral | ForEach-Object { $_ }) -join ','))
    foreach ($line in [NativeDpi]::WindowsOfPid([uint32]$proc.Id)) {
        $parts = $line -split '\|'
        if ($parts.Count -ge 3) {
            $h = [IntPtr][long]$parts[0]; $vis = $parts[1] -eq 'True'; $title = $parts[2]
            $codes = ([int[]][char[]]$title | ForEach-Object { $_ }) -join ','
            Write-Host ("  hwnd={0} vis={1} len={2} codes=[{3}]" -f $parts[0], $parts[1], $title.Length, $codes)
            if ($vis -and $title -eq 'Шаблоны') { $tplHwnd = $h }
        }
    }
    if ($tplHwnd -eq [IntPtr]::Zero) { throw "win32-окно «Шаблоны» не найдено" }
    $tplWin = [System.Windows.Automation.AutomationElement]::FromHandle($tplHwnd)
    Write-Host "templates window opened (hwnd=$tplHwnd)"
    Write-Host "templates window opened"

    # Скриншот ВИТРИНЫ (до открытия чек-листа): там пиктограммы-товары —
    # регрессия «иконки ни о чём» проверяется на кадре.
    Start-Sleep -Milliseconds 700
    $galR = $tplWin.Current.BoundingRectangle
    $galPng = $OutPng -replace '\.png$', '-gallery.png'
    New-Item -ItemType Directory -Force -Path (Split-Path $galPng) | Out-Null
    $gbmp = New-Object System.Drawing.Bitmap([int]$galR.Width, [int]$galR.Height)
    $gg = [System.Drawing.Graphics]::FromImage($gbmp)
    $gg.CopyFromScreen([int]$galR.X, [int]$galR.Y, 0, 0, $gbmp.Size)
    $gbmp.Save($galPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $gg.Dispose(); $gbmp.Dispose()
    Write-Host "gallery screenshot: $galPng"

    # Карточка шаблона «Окно». WPF строит UIA-дерево лениво — ищем с ретраями.
    $cardCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Шаблон Окно")),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $card = $null
    $sw4 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw4.Elapsed.TotalSeconds -lt 6 -and -not $card) {
        Start-Sleep -Milliseconds 400
        $card = $tplWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cardCond)
    }
    if (-not $card) { throw "карточка «Шаблон Окно» не найдена" }
    ($card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 900

    # Дамп видимых элементов с координатами.
    $all = $tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $rows = @()
    foreach ($el in $all) {
        $r = $el.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { continue }
        $rows += [pscustomobject]@{
            Type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType.', ''
            Name = ($el.Current.Name -replace "`r`n", " ")
            X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height
        }
    }
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

    # Проверка 1: подписи полей сетки на РАЗНЫХ строках (наложение = баг).
    $labels = $rows | Where-Object { $_.Type -eq "Text" -and $_.Name -in @("Тип", "Цвет", "Режим", "Антикошка") }
    $failures = @()
    $seenY = @{}
    foreach ($l in $labels) {
        $key = [math]::Round($l.Y / 4)
        if ($seenY.ContainsKey($key)) { $failures += "НАЛОЖЕНИЕ: «$($l.Name)» и «$($seenY[$key])» на Y=$($l.Y)" }
        else { $seenY[$key] = $l.Name }
    }
    if ($labels.Count -lt 3) { $failures += "подписи не найдены (найдено $($labels.Count): $($labels.Name -join ', '))" }

    # Проверка 2: статусы «Добавится»/«Не требуется» у правого края окна.
    $winR = $tplWin.Current.BoundingRectangle
    $statuses = $rows | Where-Object { $_.Type -eq "Text" -and $_.Name -in @("Добавится", "Не требуется") }
    foreach ($s in $statuses) {
        $rightEdge = $s.X + $s.W
        if ($rightEdge -lt $winR.X + $winR.Width * 0.75) { $failures += "статус «$($s.Name)» не у правого края (правый край X=$rightEdge, окно до $($winR.X + $winR.Width))" }
    }
    if ($statuses.Count -lt 3) { $failures += "статусов строк меньше трёх (найдено $($statuses.Count))" }

    # Скриншот окна для просмотра человеком.
    New-Item -ItemType Directory -Force -Path (Split-Path $OutPng) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap([int]$winR.Width, [int]$winR.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$winR.X, [int]$winR.Y, 0, 0, $bmp.Size)
    $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "screenshot: $OutPng"

    # ── Проверка 3 (регрессия v3.54-fix2): «Добавить в заказ» сразу после
    # открытия чек-листа, БЕЗ касания выпадающих списков. Раньше падала с
    # тостом «Выберите тип сетки.» — начальная установка SelectedIndex до
    # подписки на SelectionChanged не синхронизировала GridProductIndex.
    $editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $textboxes = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond) | ForEach-Object { $_ })
    $dimValues = @{ "Ширина, мм" = "500"; "Высота, мм" = "1500"; "Кол-во" = "1" }
    foreach ($lbl in ($rows | Where-Object { $_.Type -eq "Text" -and $dimValues.ContainsKey($_.Name) })) {
        # TextBox того же поля: начинается у левого края подписи, ниже неё.
        $box = $textboxes | Where-Object {
            [math]::Abs($_.Current.BoundingRectangle.X - $lbl.X) -lt 10 -and
            $_.Current.BoundingRectangle.Y -gt $lbl.Y -and
            ($_.Current.BoundingRectangle.Y - $lbl.Y) -lt 60
        } | Select-Object -First 1
        if ($box) {
            ($box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($dimValues[$lbl.Name])
            Start-Sleep -Milliseconds 150
        }
    }

    $btnCond2 = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $tplButtons = @($tplWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond2) | ForEach-Object { $_ })
    # Имя кнопки в UIA может быть пустым (текст во вложенных TextBlock) —
    # ищем по собственному имени ИЛИ по имени вложенного текстового потомка.
    $apply = $tplButtons | Where-Object {
        $_.Current.Name -match "Добавить в заказ" -or
        (@($_.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Добавить в заказ")))) -ne $null)
    } | Select-Object -First 1
    if (-not $apply) {
        $failures += "кнопка «Добавить в заказ» не найдена (кнопки: $(($tplButtons | ForEach-Object { $_.Current.Name }) -join ', '))"
    }
    else {
        ($apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $applied = $false
        $sw3 = [Diagnostics.Stopwatch]::StartNew()
        while ($sw3.Elapsed.TotalSeconds -lt 6) {
            Start-Sleep -Milliseconds 400
            $still = Find-WindowsByPid $proc.Id | Where-Object { $_.Current.Name -eq "Шаблоны" } | Select-Object -First 1
            if (-not $still) { $applied = $true; break }
        }
        if (-not $applied) {
            # Окно живо → валидация не пропустила. Собираем тексты тостов для диагностики.
            $toastNames = @()
            foreach ($el in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                $n = $el.Current.Name
                if ($n -and $n -match "Выберите|Укажите|должно быть") { $toastNames += $n }
            }
            $failures += "REGRESSION v3.54-fix2: «Добавить в заказ» не закрыл окно (валидация не пропустила). Тосты: $($toastNames -join ' | ')"
        }
        else { Write-Host "apply-check: OK — валидация прошла, окно закрылось без касания списков" }
    }

    if ($failures.Count -gt 0) { throw "ПРОВЕРКИ ПРОВАЛЕНЫ:`n" + ($failures -join "`n") }
    Write-Host "RESULT: OK — наложений нет, статусы у правого края, добавление без error-тоста"
}
finally {
    try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch { }
    if (-not $proc.HasExited) { $proc.Kill() }
}
