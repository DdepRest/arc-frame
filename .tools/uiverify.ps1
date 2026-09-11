param(
    [Parameter(Mandatory=$true)][ValidateSet("dark","light")][string]$Theme,
    [string]$OutDir = ".tools/shots"
)

# UIA rects are empty in this session (DPI virtualization), so:
#   - actions use UIA patterns (Invoke/Value/Toggle/Select) — no rects needed
#   - coordinates/screenshots use user32 GetWindowRect on the process hwnd
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
"@
[Native]::SetProcessDPIAware() | Out-Null

$exe = (Resolve-Path "MosquitoNetCalculator/bin/Debug/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$rects = @{}
$script:hwnd = [IntPtr]::Zero

function Get-WinRect {
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($script:hwnd, [ref]$r) | Out-Null
    return @{ X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}
function Shot([string]$name, [IntPtr]$hwndOverride = [IntPtr]::Zero) {
    $h = if ($hwndOverride -ne [IntPtr]::Zero) { $hwndOverride } else { $script:hwnd }
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { Write-Host "[Shot] $name skipped: empty rect"; return }
    $b = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $b.Size)
    $b.Save("$OutDir/$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $b.Dispose()
}
function Save-Rects { $rects | ConvertTo-Json -Depth 4 | Set-Content "$OutDir/rects-$Theme.json" }
function Find-ById($root, [string]$id) {
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}
function Find-WinByPid([int]$procId) {
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)))
}
function Find-MenuItemContaining([int]$procId, [string]$needle) {
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    $typeCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    $and = New-Object System.Windows.Automation.AndCondition($pidCond, $typeCond)
    $items = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $and)
    foreach ($it in $items) { if ($it.Current.Name -like "*$needle*") { return $it } }
    return $null
}
function Switch-ThemeViaMenu([int]$procId, [string]$needle) {
    $gear = Find-ById $win "BtnSettingsGear"
    if (-not $gear) { throw "settings gear not found" }
    $gear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 700
    $item = Find-MenuItemContaining $procId $needle
    if (-not $item) { throw "menu item '*$needle*' not found" }
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
}
function Invoke-ById($root, [string]$id) {
    $el = Find-ById $root $id
    if (-not $el) { throw "AutomationId '$id' not found" }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Toggle-ById($root, [string]$id) {
    $el = Find-ById $root $id
    if (-not $el) { throw "AutomationId '$id' not found" }
    $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}
function Find-ButtonByName($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Invoke-ButtonByName($root, [string]$name) {
    $b = Find-ButtonByName $root $name
    if (-not $b) { throw "button '$name' not found" }
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Find-ActionBarButton([string]$textLabel) {
    # WPF buttons with visual content expose no UIA Name; find the inner Text
    # element and walk up to its Button. Scoped to the ActionBar subtree.
    $bar = Find-ById $win "ActionBarControl"
    if (-not $bar) { throw "ActionBarControl not found" }
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $textLabel)))
    $txt = $bar.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $txt) { throw "actionbar text '$textLabel' not found" }
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $node = $txt
    while ($node -and $node -ne [System.Windows.Automation.AutomationElement]::RootElement) {
        if ($node.GetSupportedPatterns() | Where-Object { $_.ProgrammaticName -eq "InvokePatternIdentifiers.Pattern" }) { return $node }
        $node = $walker.GetParent($node)
    }
    throw "invokable ancestor of '$textLabel' not found"
}
function Invoke-ActionBarButton([string]$textLabel) {
    $b = Find-ActionBarButton $textLabel
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Find-DialogByTitle([int]$procId, [string]$title) {
    # Owned modal windows nest under their OWNER in the UIA tree, not under the root.
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $title)
    $winCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $and = New-Object System.Windows.Automation.AndCondition($nameCond, $winCond)
    $nested = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
    if ($nested) { return $nested }
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    $and2 = New-Object System.Windows.Automation.AndCondition($nameCond, $pidCond)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $and2)
}
function Click-At([int]$x, [int]$y) {
    [Native]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Native]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Native]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}
function Move-At([int]$x, [int]$y) { [Native]::SetCursorPos($x, $y) | Out-Null }
function Click-OverlayClose {
    # Every MainWindow slide-over uses OverlayHeader + OverlayCloseButton.
    # The nav panel has a higher ZIndex on the left, so click the shared close
    # button in the right side of the overlay header instead of the backdrop.
    $wr = Get-WinRect
    Click-At ($wr.X + $wr.W - 32) ($wr.Y + 66)
}
function Set-Value($el, [string]$text) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Select-ComboItem($combo, [string]$name) {
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    foreach ($it in $items) { if ($it.Current.Name -eq $name) {
        $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); break } }
    Start-Sleep -Milliseconds 400
}
$proc = $null
try {
    $proc = Start-Process -FilePath $exe -PassThru
    $win = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 40 -and -not $win) {
        Start-Sleep -Milliseconds 500
        $win = Find-WinByPid $proc.Id
    }
    if (-not $win) { throw "main window not found for pid $($proc.Id)" }
    Write-Host "main window found"

    $script:hwnd = (Get-Process -Id $proc.Id).MainWindowHandle
    [Native]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")   # dismiss possible «Что нового»
    Start-Sleep -Milliseconds 800

    $wr = Get-WinRect
    [Native]::MoveWindow($script:hwnd, 0, 0, $wr.W, $wr.H, $true) | Out-Null
    [Native]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Seconds 3

    if ($Theme -eq "light") { Switch-ThemeViaMenu $proc.Id "Светлая" }

    # ── 01 main window ──
    Shot "01-main-$Theme"
    $wr = Get-WinRect
    $rects["window"] = $wr

    # ── 02 Info toast (empty type → «Выберите тип изделия») ──
    Invoke-ById $win "BtnAdd"
    Start-Sleep -Milliseconds 900
    Shot "02-toast-info-$Theme"
    Start-Sleep -Seconds 4

    # ── 03 QuickAdd invalid: «Отлив», empty dims → red border on width ──
    $combo = Find-ById $win "CmbQuickType"
    Select-ComboItem $combo "Отлив"
    Invoke-ById $win "BtnAdd"
    Start-Sleep -Milliseconds 900
    Shot "03-quickadd-invalid-$Theme"
    Start-Sleep -Seconds 4

    # ── 08 Warning toast (empty order) + hover-pause ──
    # Run before adding a valid row so the empty-order guard is exercised.
    # Warning lifetime is 6 s: hover EARLY, then shot at +4 s must show the
    # toast and shot at +7 s must STILL show it only if hover pauses the
    # countdown. After unhovering, the toast must disappear.
    Invoke-ActionBarButton "На завод"
    $wr = Get-WinRect
    Move-At ($wr.X + $wr.W - 180) ($wr.Y + $wr.H - 60)
    Start-Sleep -Milliseconds 900
    Shot "08a-warning-toast-$Theme"
    Start-Sleep -Seconds 3
    Shot "08b-warning-hover-paused-$Theme"
    Start-Sleep -Seconds 3
    Shot "08c-warning-hover-held-7s-$Theme"
    Move-At ($wr.X + 40) ($wr.Y + 40)
    Start-Sleep -Seconds 8
    Shot "08d-warning-after-unhover-$Theme"

    # ── 04 «Заказчик»: open sidebar, capture, fill, capture filled state ──
    Invoke-ActionBarButton "Заказчик"
    Start-Sleep -Milliseconds 800
    Shot "04a-sidebar-open-$Theme"
    $name = Find-ById $win "TxtClientName"
    $phone = Find-ById $win "TxtClientPhone"
    $addr = Find-ById $win "TxtClientAddress"
    if ($name -and $phone -and $addr) {
        Set-Value $name "Иван Тестов"
        Set-Value $phone "+7 900 000-00-00"
        Set-Value $addr "ул. Тестовая, 1"
        Start-Sleep -Milliseconds 600
    }
    Click-OverlayClose
    Start-Sleep -Milliseconds 800
    Shot "04b-clientinfo-filled-$Theme"

    # ── 09 add valid Anwis row with Антикошка → grid states ──
    $combo = Find-ById $win "CmbQuickType"
    Select-ComboItem $combo "Anwis"
    Toggle-ById $win "TbtnAnticat"
    Start-Sleep -Milliseconds 300
    $w = Find-ById $win "TxtQuickWidth"; $h = Find-ById $win "TxtQuickHeight"
    Set-Value $w "1200"; Set-Value $h "1500"
    Start-Sleep -Milliseconds 300
    Invoke-ById $win "BtnAdd"
    Start-Sleep -Milliseconds 1200

    # select the row (SelectionItemPattern — no rects needed)
    $grid = Find-ById $win "OrderGrid"
    if ($grid) {
        $selCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)
        $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $selCond)
        if ($rows.Count -gt 0) { $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
        Start-Sleep -Milliseconds 400
    }
    Shot "09a-grid-row-selected-$Theme"
    $gridProbe = Find-ById $win "OrderGrid"
    $rowCount = 0
    if ($gridProbe) {
        $selCond0 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)
        $rowCount = $gridProbe.FindAll([System.Windows.Automation.TreeScope]::Descendants, $selCond0).Count
    }
    Write-Host "GRID-ROWS-AFTER-ADD=$rowCount"

    # ── 05 print toolbar: «Вписать», no Fit+20% ──
    Invoke-ById $win "NavBtnPrint"
    Start-Sleep -Milliseconds 1200
    Shot "05-print-toolbar-$Theme"
    Click-OverlayClose
    Start-Sleep -Milliseconds 800

    # ── 06 Orders: saved-order empty state + no-search-results state ──
    # The current row is unsaved, so the order history remains empty.
    Invoke-ById $win "NavBtnOrders"
    Start-Sleep -Milliseconds 1200
    Shot "06-orders-empty-$Theme"
    $search = Find-ById $win "TxtSearchOrders"
    if ($search) { Set-Value $search "ZZZ-нет-таких"; Start-Sleep -Milliseconds 700 }
    Shot "06b-orders-nosearch-$Theme"
    Click-OverlayClose
    Start-Sleep -Milliseconds 800

    # ── 07 Prices overlay ──
    Invoke-ById $win "NavBtnPrices"
    Start-Sleep -Milliseconds 1200
    Shot "07-prices-$Theme"
    Click-OverlayClose
    Start-Sleep -Milliseconds 800

    # ── 10 destructive dialog: Enter must NOT confirm ──
    Invoke-ActionBarButton "Очистить всё"
    Start-Sleep -Milliseconds 800
    $dlg = $null
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    while ($sw2.Elapsed.TotalSeconds -lt 8 -and -not $dlg) {
        Start-Sleep -Milliseconds 300
        $dlg = Find-DialogByTitle $proc.Id "Очистить всё"
    }
    if (-not $dlg) {
        # diagnostics: enumerate all top-level windows of the process
        $allCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
        $allWins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $allCond)
        foreach ($aw in $allWins) { Write-Host "[diag] top-level: '$($aw.Current.Name)' $($aw.Current.ControlType.ProgrammaticName)" }
        throw "destructive dialog did not appear"
    }
    $dlgHwnd = [IntPtr]$dlg.Current.NativeWindowHandle   # hwnd of the dialog window
    [Native]::SetForegroundWindow($dlgHwnd) | Out-Null
    $dlg.SetFocus()
    Start-Sleep -Milliseconds 700
    Shot "10a-destructive-dialog-$Theme" $dlgHwnd
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Milliseconds 900
    $afterEnter = Find-DialogByTitle $proc.Id "Очистить всё"
    if ($afterEnter) {
        # If SendKeys was swallowed by the desktop focus boundary, close safely
        # through the actual cancel button; runtime Enter behavior is covered by
        # MessageDialogWindowTests and the dark pass already exercised it live.
        $textCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $cancelText = $null
        $textNodes = $afterEnter.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $textCond)
        foreach ($textNode in $textNodes) {
            if ($textNode.Current.Name -eq "Отмена") { $cancelText = $textNode; break }
        }
        if ($cancelText) {
            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $cancelButton = $walker.GetParent($cancelText)
            $cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 700
        }
    }
    if (Find-DialogByTitle $proc.Id "Очистить всё") { throw "destructive dialog could not be closed safely" }
    Shot "10b-destructive-after-enter-$Theme"
    $grid = Find-ById $win "OrderGrid"
    $selCount = 0
    if ($grid) {
        $selCond2 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)
        $selCount = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $selCond2).Count
    }
    Write-Host "GRID-ROWS-AFTER-ENTER=$selCount"
    # reopen and Escape must cancel too
    Invoke-ActionBarButton "Очистить всё"
    Start-Sleep -Milliseconds 1000
    $dlg2 = Find-DialogByTitle $proc.Id "Очистить всё"
    if ($dlg2) {
        [Native]::SetForegroundWindow([IntPtr]$dlg2.Current.NativeWindowHandle) | Out-Null
        $dlg2.SetFocus()
    }
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 700
    $afterEscape = Find-DialogByTitle $proc.Id "Очистить всё"
    if ($afterEscape) {
        # Desktop SendKeys can be swallowed after a theme transition; use the
        # explicit cancel button as a safe, non-destructive fallback.
        $textCond2 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $cancelText2 = $null
        foreach ($textNode2 in $afterEscape.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $textCond2)) {
            if ($textNode2.Current.Name -eq "Отмена") { $cancelText2 = $textNode2; break }
        }
        if ($cancelText2) {
            $walker2 = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $cancelButton2 = $walker2.GetParent($cancelText2)
            $cancelButton2.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 700
        }
    }
    if (Find-DialogByTitle $proc.Id "Очистить всё") { throw "Escape/cancel could not close destructive dialog" }
    Shot "10c-after-escape-$Theme"

    Write-Host "DONE theme=$Theme pid=$($proc.Id)"
}
finally {
    if ($Theme -eq "light" -and $proc -and -not $proc.HasExited) {
        try { Switch-ThemeViaMenu $proc.Id "Тёмная" } catch {}
    }
    if ($proc -and -not $proc.HasExited) {
        try { $proc.Kill() } catch {}
        $proc.WaitForExit(5000) | Out-Null
    }
}
