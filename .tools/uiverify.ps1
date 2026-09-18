param(
    [Parameter(Mandatory=$true)][ValidateSet("dark","light")][string]$Theme,
    [string]$OutDir = ".tools/shots",
    # Самопроверка стража «экран здесь наш»: кладёт ПОВЕРХ нашего окна чужое
    # и требует, чтобы харнесс отказался снимать кадры и кликать. Кадры сцен
    # при этом не пишутся (режим только для проверки самого харнесса).
    [switch]$SelfTest
)

# UIA rects are empty in this session (DPI virtualization), so:
#   - actions use UIA patterns (Invoke/Value/Toggle/Select) — no rects needed
#   - coordinates/screenshots use user32 GetWindowRect on the process hwnd
#
# ЗАНЯТЫЙ РАБОЧИЙ СТОЛ больше не даёт «тихий» брак. Прогон 2026-09-16 записал
# два кадра (05 и 06) в чужом состоянии: на экране был свой экземпляр
# приложения, клик по навигации не дошёл, а харнесс снял главное окно вместо
# оверлея и записал его поверх базлайна. Поэтому:
#   * Shot и клики мышью сначала доказывают, что точки внутри НАШЕГО
#     прямоугольника принадлежат нашему pid (WindowFromPoint + GetAncestor +
#     GetWindowThreadProcessId по сетке точек); если нет — окно поднимается и
#     проверка повторяется, а если поднять не удалось — сцена падает с
#     диагнозом (какое окно мешает) и кадр НЕ записывается;
#   * SendKeys не отправляются, пока переднее окно не наше (иначе Enter/Escape
#     ушли бы в чужую программу);
#   * сцены требуют ожидаемого состояния (маркер оверлея, строки грида) и
#     повторяют действие, а не снимают что попало;
#   * состояние оверлеев читается по маркерам UIA, а закрытие проверяется по
#     ЭФФЕКТУ: клик по координатам молча промахивался, и прогон 2026-09-16
#     светлой темы записал 04b и 09a с открытым сайдбаром (235 426 и 204 898
#     пикселей скрима там, где в базлайне их нет). Кадры «главное окно»
#     (01, 04b, 09a, 10b, 10c) теперь требуют, чтобы оверлеев не было вовсе;
#   * кадр пишется через временный файл — недописанный PNG не попадёт в базлайны.
# Самопроверка стражей (включая точку внедрения «закрытие без эффекта»): `-SelfTest`.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    // Страж «экран здесь наш»: чем занята конкретная точка экрана и какому процессу
    // принадлежит корневое окно в ней.
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int max);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
"@
[Native]::SetProcessDPIAware() | Out-Null

$exe = (Resolve-Path "MosquitoNetCalculator/bin/Debug/net8.0-windows10.0.17763.0/MosquitoNetCalculator.exe").Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
# Мусор прошлых оборванных прогонов: недописанные кадры не должны выглядеть как базлайны.
get-childitem -Path $OutDir -Filter "*.tmp.png" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
$rects = @{}
$script:hwnd = [IntPtr]::Zero
$script:written = @()      # кадры, реально записанные в этом прогоне
$script:keysSkipped = @()  # сцены, где нажатия не отправлялись (фокус не наш)
$script:pinnedTopmost = $false   # своё окно пришлось поднять поверх всех
$appPid = 0                # pid приложения: по нему проверяется «точка наша?»

function Get-Rect([IntPtr]$h) {
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($h, [ref]$r) | Out-Null
    return @{ X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}
function Get-WinRect { return Get-Rect $script:hwnd }

# ── Страж «экран здесь наш» ─────────────────────────────────────────────────
# Кадры снимаются с ЭКРАНА (CopyFromScreen), а не из окна, поэтому чужое окно
# поверх нашего попадает в базлайн как есть. Проверяем сеткой точек по всему
# прямоугольнику: любая чужая полоса шириной в сотню пикселей задевает хотя бы
# одну точку. Сетка берётся 5x4, а не «угол и центр»: перекрытие бывает полосой
# (уведомление, панель, окно другого приложения посередине).
function Get-PointOwner([int]$x, [int]$y) {
    $p = New-Object Native+POINT
    $p.X = $x; $p.Y = $y
    $h = [Native]::WindowFromPoint($p)
    if ($h -eq [IntPtr]::Zero) { return @{ Pid = 0; Title = "<пусто>"; Hwnd = $h } }
    $root = [Native]::GetAncestor($h, 2)   # GA_ROOT
    if ($root -eq [IntPtr]::Zero) { $root = $h }
    $owner = [uint32]0
    [Native]::GetWindowThreadProcessId($root, [ref]$owner) | Out-Null
    $title = ""
    try {
        $sb = New-Object System.Text.StringBuilder 256
        [Native]::GetWindowTextW($root, $sb, 256) | Out-Null
        $title = $sb.ToString()
    } catch { }
    if (-not $title) {
        try { $title = (Get-Process -Id $owner -ErrorAction Stop).ProcessName } catch { $title = "<pid $owner>" }
    }
    return @{ Pid = [int]$owner; Title = $title; Hwnd = $root }
}
function Get-ProbePoints($r) {
    $pts = @()
    foreach ($fx in 0.02, 0.25, 0.5, 0.75, 0.98) {
        foreach ($fy in 0.04, 0.3, 0.6, 0.95) {
            $pts += , @([int]($r.X + $r.W * $fx), [int]($r.Y + $r.H * $fy))
        }
    }
    return $pts
}
function Assert-ScreenIsOurs([IntPtr]$h, [string]$context, [int]$attempts = 5, [scriptblock]$PointOwned = $null) {
    # $PointOwned — точка внедрения для самопроверки: подставить заведомо чужое
    # владение точкой, не устраивая боёв за z-order на живом рабочем столе.
    for ($i = 1; $i -le $attempts; $i++) {
        $r = Get-Rect $h
        if ($r.W -le 0 -or $r.H -le 0) {
            if ($i -lt $attempts) { Start-Sleep -Milliseconds 300; continue }
            throw "$context`: окно пустое (свёрнуто или закрыто) — кадр НЕ записан"
        }
        $bad = @()
        foreach ($p in (Get-ProbePoints $r)) {
            $o = if ($PointOwned) { & $PointOwned $p[0] $p[1] } else { Get-PointOwner $p[0] $p[1] }
            if ($o.Pid -ne $appPid) { $bad += "точка ($($p[0]),$($p[1])) → «$($o.Title)» pid=$($o.Pid)" }
        }
        if ($bad.Count -eq 0) { return $r }
        if ($i -lt $attempts) {
            Write-Host "[guard] $context`: область окна перекрыта, поднимаю своё окно (попытка $i/$attempts)"
            Write-Host "        $($bad[0])"
            Set-WindowFrontmost $h $context | Out-Null
            Start-Sleep -Milliseconds 300
        }
        else {
            throw "$context`: экран в области нашего окна принадлежит ЧУЖОЙ программе, и поднять окно не удалось.`n        $($bad -join "`n        ")`n        Кадр НЕ записан. Уберите чужое окно с этой области (обычно это второй экземпляр приложения или терминал) и повторите прогон."
        }
    }
}
function Test-ForegroundOurs {
    $owner = [uint32]0
    [Native]::GetWindowThreadProcessId([Native]::GetAncestor([Native]::GetForegroundWindow(), 2), [ref]$owner) | Out-Null
    return ($owner -eq $appPid)
}
function Set-WindowFrontmost([IntPtr]$h, [string]$context) {
    # Windows отказывает фоновому процессу в SetForegroundWindow (foreground-lock),
    # поэтому на занятом столе окно часто остаётся «поднятым, но не активным».
    # Пробуем три способа по возрастанию силы. Нам принципиальна область ЭКРАНА
    # (её проверяет Assert-ScreenIsOurs); фокус нужен только для SendKeys.
    [Native]::ShowWindow($h, 9) | Out-Null            # SW_RESTORE
    [Native]::BringWindowToTop($h) | Out-Null
    [Native]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 250
    if (Test-ForegroundOurs) { return $true }

    $dummy = [uint32]0
    $fgThread = [Native]::GetWindowThreadProcessId([Native]::GetForegroundWindow(), [ref]$dummy)
    $myThread = [Native]::GetCurrentThreadId()
    [Native]::AttachThreadInput($fgThread, $myThread, $true) | Out-Null
    [Native]::SetForegroundWindow($h) | Out-Null
    [Native]::SetFocus($h) | Out-Null
    [Native]::AttachThreadInput($fgThread, $myThread, $false) | Out-Null
    Start-Sleep -Milliseconds 250
    if (Test-ForegroundOurs) { return $true }

    # Последний рубеж: поднять своё окно выше всех (HWND_TOPMOST). Фокуса это не
    # даёт, но гарантирует, что в кадр не попадут чужие пиксели.
    [Native]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x1 -bor 0x2 -bor 0x40) | Out-Null
    $script:pinnedTopmost = $true
    Start-Sleep -Milliseconds 250
    return (Test-ForegroundOurs)
}
function Send-Keys-Safe([string]$keys, [string]$context) {
    # Нажатия уходят в ПЕРЕДНЕЕ окно: если это чужая программа, Enter/Escape могли
    # бы там что-то подтвердить. Поэтому либо фокус наш, либо клавиши НЕ уходят.
    if (-not (Test-ForegroundOurs)) {
        if (-not (Set-WindowFrontmost $script:hwnd $context)) {
            Write-Host "[warn] $context`: клавиши НЕ отправлены — фокус у чужого процесса; окно приложения поднято, но не активно. Сцена продолжит через UIA."
            $script:keysSkipped += $context
            return $false
        }
    }
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    return $true
}
function Close-WhatsNewIfAny {
    # «Что нового» перекрывает кадры. Раньше его закрывали через ESC, но ESC уходит
    # в переднее окно (на занятом столе — в чужое), поэтому сначала ищем окно в UIA
    # и закрываем кнопкой; ESC остаётся мягкой попыткой в Send-Keys-Safe.
    foreach ($title in "Что нового", "Что нового?") {
        $dlg = $null
        try { $dlg = Find-DialogByTitle $proc.Id $title } catch { $dlg = $null }
        if (-not $dlg) { continue }
        Write-Host "[i] найдено окно «$title» — закрываю через UIA (не через ESC)"
        $textCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        foreach ($t in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
            if ($t.Current.Name -ne "Закрыть") { continue }
            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $btn = $walker.GetParent($t)
            try { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { }
            break
        }
    }
}
function Wait-For([scriptblock]$test, [int]$timeoutMs = 6000, [int]$pollMs = 250) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $timeoutMs) {
        $ok = $false
        try { $ok = [bool](& $test) } catch { $ok = $false }
        if ($ok) { return $true }
        Start-Sleep -Milliseconds $pollMs
    }
    return $false
}
function Test-ByIdOnScreen([string]$id) {
    # Маркеры сцен — только реально существующие в дереве UIA контролы: простые
    # Grid (например x:Name="PrintOverlay") в дереве НЕ появляются, поэтому по
    # ним проверка была бы ложной («оверлей не открылся», хотя он открыт).
    $el = Find-ById $win $id
    if (-not $el) { return $false }
    try { return -not $el.Current.IsOffscreen } catch { return $false }
}
function Assert-SceneReady([string[]]$ids, [string]$scene, [int]$timeoutMs = 6000) {
    # Ждём НАСТОЯЩЕГО состояния сцены, а не «через 1.2 с наверное откроется»:
    # если оверлей не открылся, снимать главное окно вместо него нельзя.
    $ready = Wait-For { foreach ($id in $ids) { if (Test-ByIdOnScreen $id) { return $true } }; return $false } $timeoutMs
    if ($ready) { return }
    throw "$scene`: сцена не пришла в ожидаемое состояние (ни один маркер из [$($ids -join ', ')] не появился). Кадр НЕ записан."
}
function Open-Overlay([string]$navId, [string[]]$readyIds, [string]$scene) {
    # Клик по навигации мог не дойти — тогда ПОВТОРЯЕМ действие (подняв своё
    # окно), а не снимаем то, что оказалось на экране.
    # Перед открытием закрываем чужой оверлей: два оверлея сразу — не сцена.
    Close-Overlay "$scene (перед открытием)"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Invoke-ById $win $navId
        foreach ($id in $readyIds) { if (Wait-For { Test-ByIdOnScreen $id } 4000) { return } }
        Write-Host "[retry] $scene`: оверлей не открылся после $navId (попытка $attempt/3) — поднимаю своё окно и повторяю"
        try { Assert-ScreenIsOurs $script:hwnd "$scene/$navId" 2 | Out-Null } catch { }
        Start-Sleep -Milliseconds 400
    }
    throw "$scene`: оверлей не открылся за 3 попытки (маркеры: $($readyIds -join ', ')). Кадр НЕ записан."
}
function Shot([string]$name, [IntPtr]$hwndOverride = [IntPtr]::Zero, [scriptblock]$PointOwned = $null, [switch]$KeepCursor) {
    $h = if ($hwndOverride -ne [IntPtr]::Zero) { $hwndOverride } else { $script:hwnd }
    # Состояние сцены видно в логе: если кадр снят не в том состоянии, это
    # заметно по строке, а не только по картинке.
    Write-Host "[shot] $name — открытые оверлеи: $(if (@(Get-OpenOverlayNames).Count) { @(Get-OpenOverlayNames) -join ', ' } else { 'нет' })"
    # 0. Перед съёмкой уводим курсор в инертную зону (середина шапки кадра):
    #    иначе в базлайн попадает подсказка или hover-подсветка от предыдущей
    #    сцены — шум, который меняется от прогона к прогону и делает базлайны
    #    невоспроизводимыми. Сцены, где наведение и есть предмет проверки
    #    (08a–08c — пауза тоста), снимаются с -KeepCursor.
    if (-not $KeepCursor) {
        $box = Get-Rect $h
        if ($box.W -gt 40 -and $box.H -gt 40) {
            try { Move-At ($box.X + [int]($box.W / 2)) ($box.Y + 18) } catch { }
            Start-Sleep -Milliseconds 250
        }
    }
    # 1. Область кадра — наша? Если нет, окно поднимается и проверка повторяется.
    $r = Assert-ScreenIsOurs $h "кадр $name" 5 $PointOwned
    # 2. Захват
    $b = New-Object System.Drawing.Bitmap($r.W, $r.H)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.X, $r.Y, 0, 0, $b.Size)
    $g.Dispose()
    # 3. Не накрыли ли окно во время захвата — иначе кадр в мусор.
    Assert-ScreenIsOurs $h "кадр $name (проверка после захвата)" 1 $PointOwned | Out-Null
    # 4. Атомарная запись: недописанный PNG не должен попасть в базлайны.
    #    Подмена с ПОВТОРАМИ: файл в .tools/shots может на миг держать читатель
    #    (проводник, индексатор) — без повторов это падало как
    #    «Cannot create a file when that file already exists» и роняло прогон.
    $tmp = Join-Path $OutDir "$name.tmp.png"
    $dest = Join-Path $OutDir "$name.png"
    $b.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    $swapped = $false
    for ($i = 1; $i -le 10; $i++) {
        try { Move-Item -Force $tmp $dest -ErrorAction Stop; $swapped = $true; break }
        catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $swapped) {
        # Временный файл НЕ удаляем: по нему видно, что кадр был снят и в каком состоянии.
        throw "кадр $name`: не удалось заменить $dest за 10 попыток (файл держит читатель?). Снятый кадр остался в $tmp — не удаляйте его вручную, пока не разберётесь."
    }
    $script:written += "$name.png"
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
    # Клик идёт по КООРДИНАТАМ экрана: сначала убеждаемся, что в этой точке наше
    # окно, иначе клик уйдёт в чужую программу (в худшем случае нажмёт там
    # что-нибудь необратимое), а сцена молча сломается.
    $o = Get-PointOwner $x $y
    if ($o.Pid -ne $appPid) {
        throw "клик по ($x,$y) отменён: в этой точке «$($o.Title)» pid=$($o.Pid), а не наше приложение (pid=$appPid)"
    }
    [Native]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Native]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Native]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}
function Move-At([int]$x, [int]$y) {
    # Наведение курсора тоже адресное: при занятом столе hover мог случиться в
    # чужом окне, и тогда «пауза тоста» проверялась бы ни на чём.
    $o = Get-PointOwner $x $y
    if ($o.Pid -ne $appPid) {
        throw "наведение на ($x,$y) отменено: в этой точке «$($o.Title)» pid=$($o.Pid), а не наше приложение (pid=$appPid)"
    }
    [Native]::SetCursorPos($x, $y) | Out-Null
}
# ── Состояние оверлеев: маркеры из дерева UIA ───────────────────────────────
# У каждого оверлея MainWindow есть контрол, который существует в UIA ТОЛЬКО
# пока оверлей открыт. По ним и проверяется состояние сцены — состояние, а не
# подобие: прошлый прогон записал кадр 04b светлой темы с открытым сайдбаром
# (в базлайне скрима нет, а записано 235 426 пикселей скрима).
$script:overlayMarkers = [ordered]@{
    "клиент" = "TxtClientName"
    "печать" = "PrintButton"
    "заказы" = "TxtSearchOrders"
    "цены"   = "TxtSearchPrices"
}
function Get-OpenOverlayNames {
    $open = @()
    foreach ($key in $script:overlayMarkers.Keys) {
        if (Test-ByIdOnScreen $script:overlayMarkers[$key]) { $open += $key }
    }
    return @($open)
}
function Find-OverlayCloseButton {
    # Кнопка закрытия slide-over'а ищется по AutomationId из общего стиля
    # OverlayCloseButton (MainWindow.xaml), затем по имени для диктора.
    # Кнопка закрытия ОКНА (AutomationId «BtnClose») — другой элемент, её нажатие
    # закрыло бы приложение.
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.OrCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "OverlayClose")),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, "Закрыть панель")))))
    foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try { if (-not $b.Current.IsOffscreen) { return $b } } catch { }
    }
    return $null
}
function Invoke-OverlayCloseButton {
    # Закрываем оверлей СЕМАНТИЧЕСКИ (UIA Invoke кнопки), а не кликом по
    # координатам: клик по точке молча промахивался, если окно успело сместиться
    # или под ним был другой элемент, и тогда снимался не тот экран.
    $btn = Find-OverlayCloseButton
    if (-not $btn) {
        throw "кнопка закрытия оверлея (AutomationId «OverlayClose») в дереве не найдена — панель нельзя закрыть семантически"
    }
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Click-OverlayClose {
    # Резервный путь, если UIA Invoke почему-то не дал эффекта: адресный клик
    # мышью по САМОЙ кнопке (её прямоугольник из UIA), а если прямоугольника нет —
    # по её месту в шапке панели. Клик идёт через Click-At, то есть с проверкой,
    # что точка принадлежит нашему процессу.
    $btn = Find-OverlayCloseButton
    if ($btn) {
        $r = $btn.Current.BoundingRectangle
        if ($r.Width -gt 4 -and $r.Height -gt 4) {
            Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
            return
        }
    }
    $wr = Get-WinRect
    Click-At ($wr.X + $wr.W - 32) ($wr.Y + 66)
}
function Close-Overlay([string]$scene, [int]$Attempts = 3, [bool]$CloseHasNoEffect = $false) {
    # Закрывает оверлей и ПРОВЕРЯЕТ ЭФФЕКТ. Если эффекта нет — повторяет, а не
    # оставляет как есть: иначе кадр главного окна записался бы затемнённым или
    # с чужой панелью, и это молча ушло бы в базлайны.
    # $CloseHasNoEffect — точка внедрения для самопроверки: воспроизводит ровно
    # тот отказ, что случился молча в прогоне светлой темы («клик был, эффекта
    # нет»), чтобы проверить, что страж на него реагирует.
    # Тип именно [bool], а не [switch]: switch-параметр не связывается
    # ПОЗИЦИОННО, и точка внедрения молча получала $false — самопроверка была бы
    # вакуумной (закрытие проходило настоящим путём).
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $open = @(Get-OpenOverlayNames)
        if ($open.Count -eq 0) { return }
        $why = ""
        if ($CloseHasNoEffect) {
            Start-Sleep -Milliseconds 200
            $why = "точка внедрения: закрытие без эффекта"
        }
        else {
            try { Invoke-OverlayCloseButton } catch { $why = $_.Exception.Message }
            if (Wait-For { @(Get-OpenOverlayNames).Count -eq 0 } 2500) { return }
            try { Click-OverlayClose } catch { $why = (@($why, $_.Exception.Message) | Where-Object { $_ }) -join " / " }
            if (Wait-For { @(Get-OpenOverlayNames).Count -eq 0 } 2000) { return }
        }
        Write-Host "[retry] $scene`: оверлей [$($open -join ', ')] не закрылся (попытка $attempt/$Attempts)$(if ($why) { ' — ' + $why })"
        Start-Sleep -Milliseconds 400
    }
    throw "$scene`: оверлей [$((Get-OpenOverlayNames) -join ', ')] остался открыт за $Attempts попытки закрытия. Кадр НЕ записан."
}
function Assert-NoOverlay([string]$scene, [int]$Attempts = 3, [bool]$CloseHasNoEffect = $false) {
    # Инвариант кадров «главное окно» (01, 04b, 09a, 10b, 10c): на экране не должно
    # быть ни одного оверлея. Сначала лечим (закрываем), потом требуем.
    Close-Overlay $scene $Attempts $CloseHasNoEffect
    $open = @(Get-OpenOverlayNames)
    if ($open.Count -gt 0) {
        throw "$scene`: на экране открыт оверлей [$($open -join ', ')] — кадр главного окна НЕ записан"
    }
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
function Test-MagentaAt($bmp, [int]$x, [int]$y) {
    # Магента (255,0,255) — служебный цвет формы-нарушителя: в интерфейсе такого нет.
    $c = $bmp.GetPixel($x, $y)
    return ($c.R -gt 200 -and $c.G -lt 60 -and $c.B -gt 200)
}
function Count-MagentaInRect($bmp, $rect) {
    $n = 0
    for ($fx = 0.1; $fx -le 0.9; $fx += 0.2) {
        for ($fy = 0.1; $fy -le 0.9; $fy += 0.2) {
            $x = [int]($rect.X + $rect.W * $fx); $y = [int]($rect.Y + $rect.H * $fy)
            if ($x -ge 0 -and $y -ge 0 -and $x -lt $bmp.Width -and $y -lt $bmp.Height) {
                if (Test-MagentaAt $bmp $x $y) { $n++ }
            }
        }
    }
    return $n
}
function Invoke-SelfTest {
    # Проверка, что страж НЕ ВАКУУМНЫЙ и не пропускает чужие пиксели в кадр.
    # Поверх нашего окна кладётся ЧУЖОЕ окно (WinForms-форма самого харнесса:
    # её pid не равен pid приложения, значит для стража она чужая) и заливается
    # магентой — по цвету видно, попали ли чужие пиксели в записанный кадр.
    # Проверяется РЕЗУЛЬТАТ, а не факт падения: страж вправе поднять своё окно и
    # ПЕРЕСНЯТЬ кадр — именно этого от него и хотят.
    Write-Host "== SELFTEST: страж «экран здесь наш» =="

    $r = Get-Rect $script:hwnd
    $form = New-Object System.Windows.Forms.Form
    $form.FormBorderStyle = "None"
    $form.StartPosition = "Manual"
    $form.BackColor = [System.Drawing.Color]::FromArgb(255, 0, 255)
    $form.Left = $r.X + [int]($r.W / 3)
    $form.Top = $r.Y + [int]($r.H / 3)
    $form.Width = [int]($r.W / 3)
    $form.Height = [int]($r.H / 3)
    $form.TopMost = $true
    $form.Show()
    $form.BringToFront()
    Start-Sleep -Milliseconds 300
    # Своё окно страж мог поднять «поверх всех» (TOPMOST) — снимаем это, иначе
    # форма TOPMOST не окажется НАД ним и проверять будет нечего.
    [Native]::SetWindowPos($script:hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x1 -bor 0x2 -bor 0x40) | Out-Null
    $script:pinnedTopmost = $false
    $form.BringToFront()
    Start-Sleep -Milliseconds 600
    $formRect = @{ X = $form.Left; Y = $form.Top; W = $form.Width; H = $form.Height }

    # 1. Положительный контроль примитива: точка нашего окна ВНЕ формы — наша.
    $mine = Get-PointOwner ($r.X + 12) ($r.Y + 12)
    if ($mine.Pid -ne $appPid) {
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: точка в углу нашего окна принадлежит «$($mine.Title)» pid=$($mine.Pid) — страж проверяет не тот процесс"
    }
    Write-Host "  [ok] точка в углу нашего окна — наша (pid=$appPid)"

    # 2. Средство измерения чужих пикселей: в СЫРОМ захвате области формы магента
    #    видна. Если форму накрыло что-то ещё (в некоторых средах поверх всего
    #    лежит окно консоли) — это тоже чужое, но магенты там нет, поэтому
    #    пиксельный контроль просто пропускается, а не врёт.
    $raw = New-Object System.Drawing.Bitmap($formRect.W, $formRect.H)
    $rg = [System.Drawing.Graphics]::FromImage($raw)
    $rg.CopyFromScreen($formRect.X, $formRect.Y, 0, 0, $raw.Size)
    $rg.Dispose()
    $rawHits = Count-MagentaInRect $raw @{ X = 0; Y = 0; W = $formRect.W; H = $formRect.H }
    $raw.Dispose()
    if ($rawHits -gt 0) {
        Write-Host "  [ok] чужие пиксели видны в сыром захвате ($rawHits/25 точек) — средство проверки работает"
    }
    else {
        Write-Host "  [i] форму накрыло что-то ещё (поверх всего в этой среде лежит окно консоли) — пиксельный контроль пропущен"
    }

    # 3. Подставное перекрытие: предикат «в прямоугольнике всегда чужое» — ровно
    #    то, что бывает при занятом столе. Работает на любом рабочем столе.
    $alwaysForeign = { param($x, $y) @{ Pid = 999999; Title = "<тестовое чужое окно>"; Hwnd = [IntPtr]::Zero } }
    $refusedByFake = $false
    try { Assert-ScreenIsOurs $script:hwnd "selftest/подставное перекрытие" 2 $alwaysForeign | Out-Null }
    catch { $refusedByFake = $true; Write-Host "  [ok] страж отказывается при перекрытии: $($_.Exception.Message.Split("`n")[0])" }
    if (-not $refusedByFake) {
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: с подставным перекрытием страж не отказался — проверка вакуумна"
    }

    # 4. Клик в чужую область отменяется (иначе он уйдёт в чужую программу).
    $foreignPoint = $null
    foreach ($p in (Get-ProbePoints $formRect)) {
        $o = Get-PointOwner $p[0] $p[1]
        if ($o.Pid -ne $appPid) { $foreignPoint = $p; break }
    }
    if ($foreignPoint) {
        $refusedClick = $false
        try { Click-At $foreignPoint[0] $foreignPoint[1] }
        catch { $refusedClick = $true; Write-Host "  [ok] клик по чужой области отменён: $($_.Exception.Message.Split("`n")[0])" }
        if (-not $refusedClick) {
            $form.Close(); $form.Dispose()
            throw "SELFTEST FAIL: клик мышью по чужой области не отменён"
        }
    }
    else {
        Write-Host "  [i] чужой точки в области формы нет (всё наше) — клик по чужому проверить не удалось"
    }

    # 5. Shot с подставным перекрытием: кадр не должен появиться вообще.
    $fakeProbe = "zz-selftest-guard-fake-$Theme"
    $fakePath = Join-Path $OutDir "$fakeProbe.png"
    if (Test-Path $fakePath) { Remove-Item $fakePath -Force }
    $shotRefused = $false
    try { Shot $fakeProbe ([IntPtr]::Zero) $alwaysForeign }
    catch { $shotRefused = $true; Write-Host "  [ok] Shot отказался снимать: $($_.Exception.Message.Split("`n")[0])" }
    if (Test-Path $fakePath) {
        Remove-Item $fakePath -Force
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: Shot записал кадр, хотя видел перекрытие"
    }
    if (-not $shotRefused) {
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: Shot не отказался снимать при перекрытии"
    }
    Write-Host "  [ok] при перекрытии кадр НЕ записан"

    # 5б. Оверлей, который «не закрылся»: кадры главного окна должны блокироваться.
    #     Это точка внедрения — реальный отказ прошлого прогона светлой темы
    #     (клик был, оверлей остался) — и проверка того, что он не пройдёт молча.
    Invoke-ActionBarButton "Заказчик"
    try { Assert-SceneReady @("TxtClientName") "selftest/оверлей «Заказчик»" 8000 } catch {
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: оверлей «Заказчик» не открылся — страж на незакрытый оверлей проверить нечем"
    }
    Write-Host "  [i] видны маркеры оверлеев: [$((Get-OpenOverlayNames) -join ', ')] (TxtClientName=$(Test-ByIdOnScreen 'TxtClientName'))"
    $refusedOverlay = $false
    try { Assert-NoOverlay "selftest/оверлей не закрывается" 1 $true }
    catch {
        $refusedOverlay = $true
        Write-Host "  [ok] незакрытый оверлей блокирует кадр: $($_.Exception.Message.Split("`n")[0])"
    }
    if (-not $refusedOverlay) {
        Close-Overlay "selftest/уборка"
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: страж не заметил незакрытый оверлей — кадр главного окна ушёл бы затемнённым (точка внедрения была вакуумна)"
    }
    Assert-NoOverlay "selftest/закрытие оверлея"   # настоящее закрытие должно сработать
    if (@(Get-OpenOverlayNames).Count -gt 0) {
        $form.Close(); $form.Dispose()
        throw "SELFTEST FAIL: оверлей «Заказчик» не закрылся настоящим путём"
    }
    Write-Host "  [ok] настоящий путь закрывает оверлей (состояние чистое)"

    # 6. Настоящее перекрытие, если оно есть на этом столе: кадр либо не пишется,
    #    либо пишется без чужих пикселей (для формы — по цвету магенты).
    $probe = "zz-selftest-guard-$Theme"
    $probePath = Join-Path $OutDir "$probe.png"
    if (Test-Path $probePath) { Remove-Item $probePath -Force }
    try { Shot $probe }
    catch { Write-Host "  [ok] Shot отказался снимать перекрытое окно: $($_.Exception.Message.Split("`n")[0])" }
    if (Test-Path $probePath) {
        $frame = New-Object System.Drawing.Bitmap($probePath)
        $wr = Get-Rect $script:hwnd
        $inFrame = @{
            X = $formRect.X - $wr.X; Y = $formRect.Y - $wr.Y
            W = $formRect.W; H = $formRect.H
        }
        $hits = Count-MagentaInRect $frame $inFrame
        $frame.Dispose()
        Remove-Item $probePath -Force
        if ($hits -gt 0) {
            $form.Close(); $form.Dispose()
            throw "SELFTEST FAIL: в записанном кадре есть пиксели чужого окна ($hits/25 точек) — именно так появляются «чужие» базлайны"
        }
        Write-Host "  [ok] кадр снят после поднятия своего окна — чужих пикселей нет (0/25)"
    }
    else {
        Write-Host "  [ok] кадр перекрытого окна не записан"
    }

    $form.Close()
    $form.Dispose()
    Start-Sleep -Milliseconds 600

    # Обратная сторона: без чужого окна страж молчит. Если на столе УЖЕ есть
    # чужая программа в нашей области (второй экземпляр приложения) — это не
    # ошибка харнесса, а занятый рабочий стол: говорим об этом прямо.
    try {
        Assert-ScreenIsOurs $script:hwnd "selftest/после уборки" 2 | Out-Null
        Write-Host "  [ok] без чужого окна страж молчит (ложных срабатываний нет)"
    }
    catch {
        Write-Host "  [i] на столе есть ещё чужая программа в нашей области — это занятый стол, а не ошибка харнесса:"
        Write-Host "      $($_.Exception.Message.Split("`n")[0])"
    }
    Write-Host "SELFTEST PASS (тема $Theme)"
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
    $appPid = $proc.Id          # по нему проверяется «точка экрана / фокус наш?»
    [Native]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
    Close-WhatsNewIfAny
    Send-Keys-Safe "{ESC}" "стартовый ESC («Что нового»)" | Out-Null   # dismiss possible «Что нового»
    Start-Sleep -Milliseconds 800

    $wr = Get-WinRect
    [Native]::MoveWindow($script:hwnd, 0, 0, $wr.W, $wr.H, $true) | Out-Null
    [Native]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Seconds 3

    if ($Theme -eq "light") { Switch-ThemeViaMenu $proc.Id "Светлая" }

    if ($SelfTest) {
        # Проверяем САМ ХАРНЕСС, а не приложение: кадры сцен не пишутся.
        Invoke-SelfTest
        Write-Host "SELFTEST DONE theme=$Theme (сцены не выполнялись, базлайны не тронуты)"
        return
    }

    # ── 01 main window ──
    Assert-NoOverlay "01-main"
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
    Shot "08a-warning-toast-$Theme" -KeepCursor      # курсор держим НА тосте: проверяется пауза
    Start-Sleep -Seconds 3
    Shot "08b-warning-hover-paused-$Theme" -KeepCursor
    Start-Sleep -Seconds 3
    Shot "08c-warning-hover-held-7s-$Theme" -KeepCursor
    Move-At ($wr.X + 40) ($wr.Y + 40)
    Start-Sleep -Seconds 8
    Shot "08d-warning-after-unhover-$Theme"

    # ── 04 «Заказчик»: open sidebar, capture, fill, capture filled state ──
    Invoke-ActionBarButton "Заказчик"
    Assert-SceneReady @("TxtClientName") "04a-sidebar-open"
    Start-Sleep -Milliseconds 500
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
    Assert-NoOverlay "04b-clientinfo-filled"
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
    if (-not $grid) { throw "09a-grid-row-selected: грид позиций не найден — кадр НЕ записан" }
    if ($rows.Count -eq 0) { throw "09a-grid-row-selected: в гриде нет строки для выделения — кадр НЕ записан" }
    Assert-NoOverlay "09a-grid-row-selected"
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
    # Печать отказывается открываться на пустом заказе (ShowPrintOverlay показывает
    # тост и выходит), поэтому выше уже проверено, что строка в гриде есть.
    Open-Overlay "NavBtnPrint" @("PrintButton", "PrinterCombo") "05-print-toolbar"
    Start-Sleep -Milliseconds 700
    Shot "05-print-toolbar-$Theme"
    Close-Overlay "после 05-print-toolbar"

    # ── 06 Orders: saved-order empty state + no-search-results state ──
    # The current row is unsaved, so the order history remains empty.
    Open-Overlay "NavBtnOrders" @("TxtSearchOrders") "06-orders-empty"
    Start-Sleep -Milliseconds 700
    Shot "06-orders-empty-$Theme"
    $search = Find-ById $win "TxtSearchOrders"
    if (-not $search) {
        throw "06b-orders-nosearch: поле поиска заказов не найдено — кадр «ничего не найдено» не снят, кадр НЕ записан"
    }
    Set-Value $search "ZZZ-нет-таких"
    Start-Sleep -Milliseconds 700
    Shot "06b-orders-nosearch-$Theme"
    Close-Overlay "после 06b-orders-nosearch"

    # ── 07 Prices overlay ──
    Open-Overlay "NavBtnPrices" @("TxtSearchPrices", "PriceGrid") "07-prices"
    Start-Sleep -Milliseconds 700
    Shot "07-prices-$Theme"
    Close-Overlay "после 07-prices"

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
    Send-Keys-Safe "{ENTER}" "10a: Enter в диалоге подтверждения" | Out-Null
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
    Assert-NoOverlay "10b-destructive-after-enter"
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
    Send-Keys-Safe "{ESC}" "10c: Escape в диалоге подтверждения" | Out-Null
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
    Assert-NoOverlay "10c-after-escape"
    Shot "10c-after-escape-$Theme"

    $frames = @($script:written | Sort-Object)
    Write-Host ("FRAMES-WRITTEN={0}" -f $frames.Count)
    Write-Host ("FRAMES: " + ($frames -join ", "))
    $leftOpen = @(Get-OpenOverlayNames)
    Write-Host ("OVERLAYS-OPEN-AT-END=" + $(if ($leftOpen.Count) { $leftOpen -join ", " } else { "нет" }))
    if ($script:keysSkipped.Count -gt 0) {
        Write-Host ("KEYS-SKIPPED={0}" -f $script:keysSkipped.Count)
        Write-Host ("  не отправлены (фокус не наш): " + ($script:keysSkipped -join "; "))
        Write-Host "  Значит, сцены диалогов на этой прогонке проверены ТОЛЬКО через UIA — про нажатия они ничего не говорят."
    }
    Write-Host "DONE theme=$Theme pid=$($proc.Id)"
}
finally {
    if ($script:pinnedTopmost -and $script:hwnd -ne [IntPtr]::Zero) {
        # Снимаем «поверх всех»: это было нужно только для съёмки.
        try { [Native]::SetWindowPos($script:hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x1 -bor 0x2 -bor 0x40) | Out-Null } catch { }
    }
    if ($Theme -eq "light" -and $proc -and -not $proc.HasExited) {
        try { Switch-ThemeViaMenu $proc.Id "Тёмная" } catch {}
    }
    if ($proc -and -not $proc.HasExited) {
        try { $proc.Kill() } catch {}
        $proc.WaitForExit(5000) | Out-Null
    }
}
