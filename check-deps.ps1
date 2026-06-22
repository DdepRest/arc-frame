# ============================================================
#  MosquitoNetCalculator — Предварительная проверка зависимостей
# ============================================================
#
#  Использование:
#     .\check-deps.ps1                — только проверить и показать отчёт
#     .\check-deps.ps1 -Install       — проверить и доустановить недостающее
#     .\check-deps.ps1 -Quiet         — без информационных сообщений (только ошибки)
#     .\check-deps.ps1 -Json          — вывести результат в формате JSON (для CI/CI)
#
#  Требует PowerShell 5.0+ (встроен в Windows 10/11).
#
#  Проверяет:
#    • Windows >= 10.0.17763 (October 2018 Update) / Windows 11
#    • .NET 8 Desktop Runtime (опционально, для диагностики — программа сама self-contained)
#    • WebView2 Runtime >= 100.0.0.0
#    • Visual C++ Redistributable 2015-2022 (x64) >= 14.30
#
# ============================================================

[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Quiet,
    [switch]$Json
)

# Требуется PowerShell 5.0+ (встроен в Windows 10 / 11)
if ($PSVersionTable.PSVersion.Major -lt 5) {
    Write-Host "[X] Этот скрипт требует PowerShell 5.0 или новее." -ForegroundColor Red
    Write-Host "    У вас установлен: $($PSVersionTable.PSVersion)" -ForegroundColor Red
    Write-Host "    Обновите Windows или установите PowerShell: https://aka.ms/powershell" -ForegroundColor Yellow
    exit 3
}

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Цвета для вывода в консоли
function Write-Step($msg) {
    if (-not $Quiet -and -not $Json) {
        Write-Host ""
        Write-Host "==> $msg" -ForegroundColor Cyan
    }
}

function Write-Ok($msg) {
    if (-not $Quiet -and -not $Json) {
        Write-Host "  [OK]   $msg" -ForegroundColor Green
    }
}

function Write-Warn($msg) {
    if (-not $Json) {
        Write-Host "  [!]    $msg" -ForegroundColor Yellow
    }
}

function Write-Err($msg) {
    if (-not $Json) {
        Write-Host "  [X]    $msg" -ForegroundColor Red
    }
}

# ------------------------------------------------------------
# Сравнение версий: возвращает 1 если A > B, 0 если равны, -1 если A < B
# Защита от пустых элементов (например "8." после split даёт ["8", ""])
# ------------------------------------------------------------
function Compare-Version($A, $B) {
    if ([string]::IsNullOrWhiteSpace($A)) { $A = '0' }
    if ([string]::IsNullOrWhiteSpace($B)) { $B = '0' }
    $A = $A.Replace(',', '.')
    $B = $B.Replace(',', '.')
    $aParts = @($A -split '\.' | Where-Object { $_ -ne '' })
    $bParts = @($B -split '\.' | Where-Object { $_ -ne '' })
    $aNums  = $aParts | ForEach-Object { [int]$_ }
    $bNums  = $bParts | ForEach-Object { [int]$_ }
    $len = [Math]::Max($aNums.Count, $bNums.Count)
    for ($i = 0; $i -lt $len; $i++) {
        $an = if ($i -lt $aNums.Count) { $aNums[$i] } else { 0 }
        $bn = if ($i -lt $bNums.Count) { $bNums[$i] } else { 0 }
        if ($an -gt $bn) { return 1 }
        if ($an -lt $bn) { return -1 }
    }
    return 0
}

# ------------------------------------------------------------
# Безопасное чтение реестра (не падает на недоступных ключах)
# ------------------------------------------------------------
function Test-RegKey {
    param([string]$Path)
    try {
        $null = Get-Item -Path $Path -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Get-RegStringValue {
    param([string]$Path, [string]$Name)
    try {
        $item = Get-Item -Path $Path -ErrorAction Stop
        $val = $item.GetValue($Name, $null)
        if ($null -ne $val) { return [string]$val }
        return $null
    } catch {
        return $null
    }
}

# ------------------------------------------------------------
# Проверка версии Windows
# ------------------------------------------------------------
function Get-WindowsInfo {
    $info = [PSCustomObject]@{
        Major    = 0
        Minor    = 0
        Build    = 0
        ProductName    = 'Windows'
        DisplayVersion = ''
    }
    $winVerKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    if (Test-RegKey -Path $winVerKey) {
        try {
            $w = Get-ItemProperty -Path $winVerKey -ErrorAction Stop
            $info.Major = [int]$w.CurrentMajorVersionNumber
            $info.Minor = [int]$w.CurrentMinorVersionNumber
            $info.Build = [int]([string]$w.CurrentBuild).Trim()
            $info.ProductName    = [string]$w.ProductName
            $info.DisplayVersion = [string]$w.DisplayVersion
        } catch {
            # Win 7/8/8.1 не имеют CurrentMajorVersionNumber — fallback на CurrentVersion
            try {
                $cv = Get-RegStringValue -Path $winVerKey -Name 'CurrentVersion'
                if ($cv) {
                    if ($cv -like '6.1*') { $info.ProductName = 'Windows 7'; $info.Build = 7600 }
                    elseif ($cv -like '6.2*') { $info.ProductName = 'Windows 8'; $info.Build = 9200 }
                    elseif ($cv -like '6.3*') { $info.ProductName = 'Windows 8.1'; $info.Build = 9600 }
                }
            } catch {}
        }
    }
    return $info
}

# ------------------------------------------------------------
# Проверка .NET 8 Desktop Runtime
# ------------------------------------------------------------
function Get-DotNetDesktop8Info {
    $info = [PSCustomObject]@{
        Installed = $false
        MaxVersion = ''
        AllVersions = @()
    }
    $keyPath = 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App'
    if (-not (Test-RegKey -Path $keyPath)) {
        return $info
    }
    try {
        $versions = (Get-Item $keyPath -ErrorAction Stop).GetSubKeyNames()
        foreach ($v in $versions) {
            if ($v -like '8.*') {
                $info.AllVersions += $v
                if (-not $info.Installed -or (Compare-Version $v $info.MaxVersion) -gt 0) {
                    $info.MaxVersion = $v
                    $info.Installed = $true
                }
            }
        }
    } catch {}
    return $info
}

# ------------------------------------------------------------
# Проверка WebView2 Runtime
# ------------------------------------------------------------
function Get-WebView2Info {
    $info = [PSCustomObject]@{
        Installed = $false
        Version   = ''
        Scope     = ''  # "HKLM" или "HKCU"
    }
    $keys = @(
        @{ Scope = 'HKLM'; Path = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4F95-ADA8-00C4A42566F8}' },
        @{ Scope = 'HKCU'; Path = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4F95-ADA8-00C4A42566F8}' }
    )
    foreach ($k in $keys) {
        $hive = "$($k.Scope):"
        $full = "$hive\$($k.Path)"
        if (Test-RegKey -Path $full) {
            $info.Installed = $true
            $info.Scope = $k.Scope
            $v = Get-RegStringValue -Path $full -Name 'pv'
            if ($v) { $info.Version = $v }
            return $info
        }
    }
    return $info
}

# ------------------------------------------------------------
# Проверка VC++ Redistributable 2015-2022 (x64)
# ------------------------------------------------------------
function Get-VCRedistInfo {
    $info = [PSCustomObject]@{
        Installed = $false
        Version   = ''
    }
    $keyPath = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
    if (-not (Test-RegKey -Path $keyPath)) {
        return $info
    }
    try {
        $p = Get-ItemProperty -Path $keyPath -ErrorAction Stop
        if ($p.Installed -eq 1) {
            $info.Installed = $true
            $info.Version = [string]$p.Version
        }
    } catch {}
    return $info
}

# ------------------------------------------------------------
# Попытка установки через winget / прямое скачивание
# ------------------------------------------------------------
function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-Winget {
    try {
        $null = Get-Command winget -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Install-Component($kind, $url, $wingetId) {
    Write-Step "Установка: $kind"

    # Установка через winget и Microsoft installers требует прав администратора
    if (-not (Test-IsAdmin)) {
        Write-Err "Установка требует прав администратора. Запустите скрипт от имени администратора."
        return $false
    }

    # Попытка 1: winget
    if (Test-Winget) {
        Write-Host "  Пробую winget install $wingetId ..."
        try {
            $proc = Start-Process -FilePath 'winget' `
                -ArgumentList @('install', '--id', $wingetId, '-e', '--silent', '--accept-package-agreements', '--accept-source-agreements') `
                -NoNewWindow -PassThru -Wait -ErrorAction Stop
            if ($proc.ExitCode -eq 0) {
                Write-Ok "$kind установлено через winget"
                return $true
            } else {
                Write-Warn "winget вернул код $($proc.ExitCode), пробую скачать напрямую..."
            }
        } catch {
            Write-Warn "winget недоступен/ошибка: $($_.Exception.Message)"
        }
    } else {
        Write-Warn "winget не установлен, скачиваю напрямую..."
    }

    # Попытка 2: прямое скачивание и запуск
    if (-not $url) {
        Write-Err "Нет URL для прямой загрузки"
        return $false
    }

    $tmp = Join-Path $env:TEMP ("{0}-{1}.exe" -f $kind, [guid]::NewGuid().ToString('N'))
    try {
        Write-Host "  Скачиваю: $url"
        [Net.ServicePointManager]::SecurityProtocol = `
            ([Net.ServicePointManager]::SecurityProtocol `
                -bor [Net.SecurityProtocolType]::Tls12 `
                -bor [Net.SecurityProtocolType]::Tls13)
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -ErrorAction Stop
    } catch {
        Write-Err "Ошибка загрузки: $($_.Exception.Message)"
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
        return $false
    }

    Write-Host "  Запускаю установщик..."
    try {
        $proc = Start-Process -FilePath $tmp `
            -ArgumentList '/install','/quiet','/norestart' `
            -NoNewWindow -PassThru -Wait -ErrorAction Stop
        $code = $proc.ExitCode
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        if ($code -eq 0 -or $code -eq 3010) {
            Write-Ok "$kind установлено (code=$code)"
            return $true
        } else {
            Write-Err "$kind — ошибка установки, код $code"
            return $false
        }
    } catch {
        Write-Err "Не удалось запустить установщик: $($_.Exception.Message)"
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
        return $false
    }
}

# ------------------------------------------------------------
# Главная логика
# ------------------------------------------------------------
$minWinBuild = 17763  # Win10 1809 / Win11
$minWebView2 = '100.0.0.0'
$minVCRedist = '14.30.0.0'

$report = [PSCustomObject]@{
    Windows        = $null
    DotNetDesktop8 = $null
    WebView2       = $null
    VCRedist       = $null
    AllOk          = $false
    NeedsInstall   = $false
    InstalledAny   = $false
    Actions        = @()
}

# Windows version
Write-Step "Проверка версии Windows..."
$win = Get-WindowsInfo
$report.Windows = [PSCustomObject]@{
    ProductName    = $win.ProductName
    DisplayVersion = $win.DisplayVersion
    Build          = $win.Build
    MinBuild       = $minWinBuild
    Supported      = ($win.Build -ge $minWinBuild)
    Description    = "$($win.ProductName) $(if ($win.DisplayVersion) { $win.DisplayVersion }) (Build $($win.Build))"
}
if ($report.Windows.Supported) {
    Write-Ok "Windows: $($report.Windows.Description)"
} else {
    Write-Err "Windows: $($report.Windows.Description) — требуется Build >= $minWinBuild (Windows 10 1809 или новее)"
}

# .NET 8 Desktop Runtime
Write-Step "Проверка .NET 8 Desktop Runtime..."
$dn8 = Get-DotNetDesktop8Info
$report.DotNetDesktop8 = [PSCustomObject]@{
    Installed = $dn8.Installed
    MaxVersion = $dn8.MaxVersion
    Necessary = $false   # Self-contained, только для диагностики
    Description = if ($dn8.Installed) {
        "Установлен $($dn8.MaxVersion) (программа использует встроенный .NET)"
    } else {
        "Не установлен (программа использует встроенный .NET — диагностика)"
    }
}
if ($dn8.Installed) {
    Write-Ok ".NET 8 Desktop Runtime: $($dn8.MaxVersion) (программа использует свой .NET)"
} else {
    Write-Warn ".NET 8 Desktop Runtime не установлен (программа использует свой встроенный .NET)"
}

# WebView2 Runtime
Write-Step "Проверка WebView2 Runtime..."
$wv2 = Get-WebView2Info
$webView2Ok = $false
$webView2Reason = ''
if (-not $wv2.Installed) {
    $webView2Reason = 'не установлен'
} elseif ($wv2.Version -ne '' -and (Compare-Version $wv2.Version $minWebView2) -lt 0) {
    $webView2Reason = "версия $($wv2.Version) устарела (нужна >= $minWebView2)"
} elseif ($wv2.Version -eq '') {
    $webView2Reason = 'установлен, но версия неизвестна'
} else {
    $webView2Ok = $true
    $webView2Reason = "актуальная версия $($wv2.Version)"
}
$report.WebView2 = [PSCustomObject]@{
    Installed = $wv2.Installed
    Version   = $wv2.Version
    Scope     = $wv2.Scope
    MinVersion = $minWebView2
    Necessary = $true
    Ok = $webView2Ok
    Description = "WebView2 Runtime — $webView2Reason" + ($(if ($wv2.Scope) { " [$($wv2.Scope)]" } else { '' }))
}
if ($webView2Ok) {
    Write-Ok $report.WebView2.Description
} else {
    Write-Err $report.WebView2.Description
}

# VC++ Redistributable
Write-Step "Проверка VC++ Redistributable 2015-2022 (x64)..."
$vc = Get-VCRedistInfo
$vcOk = $false
$vcReason = ''
if (-not $vc.Installed) {
    $vcReason = 'не установлен'
} elseif ($vc.Version -ne '' -and (Compare-Version $vc.Version $minVCRedist) -lt 0) {
    $vcReason = "версия $($vc.Version) устарела (нужна >= $minVCRedist)"
} else {
    $vcOk = $true
    $vcReason = if ($vc.Version) { "актуальная версия $($vc.Version)" } else { "установлен" }
}
$report.VCRedist = [PSCustomObject]@{
    Installed = $vc.Installed
    Version   = $vc.Version
    MinVersion = $minVCRedist
    Necessary = $true
    Ok = $vcOk
    Description = "VC++ Redistributable 2015-2022 — $vcReason"
}
if ($vcOk) {
    Write-Ok $report.VCRedist.Description
} else {
    Write-Err $report.VCRedist.Description
}

# Итог
$report.AllOk = $report.Windows.Supported -and $webView2Ok -and $vcOk
$report.NeedsInstall = (-not $report.AllOk)

Write-Step "Итог"
if ($Json) {
    $report | ConvertTo-Json -Depth 4
    exit
}

if ($report.AllOk) {
    Write-Host ""
    Write-Host "  SYSTEM READY — все обязательные зависимости в порядке." -ForegroundColor Green
    Write-Host "  Можно запускать SetupMosquitoNetCalculator-3.24.0.exe" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "  SYSTEM NOT READY — нужно доустановить компоненты:" -ForegroundColor Yellow
    if (-not $report.Windows.Supported) {
        Write-Host "    - Обновить Windows до версии 10 1809 / 11" -ForegroundColor Yellow
    }
    if (-not $webView2Ok) {
        Write-Host "    - WebView2 Runtime (>= $minWebView2)" -ForegroundColor Yellow
        Write-Host "        https://go.microsoft.com/fwlink/p/?LinkId=2124703" -ForegroundColor Gray
    }
    if (-not $vcOk) {
        Write-Host "    - VC++ Redistributable 2015-2022 (>= $minVCRedist)" -ForegroundColor Yellow
        Write-Host "        https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Gray
    }
}

# Автоматическая установка
if ($Install -and $report.NeedsInstall) {
    if (-not $report.Windows.Supported) {
        Write-Err "Невозможно автоматически обновить Windows до требуемой версии. Сделайте это вручную."
        exit 1
    }

    $installedSomething = $false

    if (-not $webView2Ok) {
        $ok = Install-Component -kind 'WebView2 Runtime' `
            -url 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' `
            -wingetId 'Microsoft.WebView2'
        if ($ok) { $installedSomething = $true }
    }

    if (-not $vcOk) {
        $ok = Install-Component -kind 'VC++ Redistributable 2015-2022' `
            -url 'https://aka.ms/vs/17/release/vc_redist.x64.exe' `
            -wingetId 'Microsoft.VCRedist.2015+.x64'
        if ($ok) { $installedSomething = $true }
    }

    $report.InstalledAny = $installedSomething

    if ($installedSomething) {
        Write-Step "Повторная проверка..."
        # Рекурсивно вызываем себя без -Install, чтобы получить актуальный статус
        & $PSCommandPath -Quiet
        exit
    } else {
        Write-Host ""
        Write-Err "Автоматическая установка не удалась. Установите компоненты вручную (ссылки выше)."
        exit 2
    }
}

if (-not $report.AllOk) {
    exit 1
}
exit 0
