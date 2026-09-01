[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Quiet,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$MinWindowsBuild = 17763
$MinVCRedistVersion = [version]'14.30.0.0'
$VCRedistUrl = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'
$VCRedistRegistryPath = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
$WindowsRegistryPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'

function Get-RegistryValue {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    try {
        return (Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop).$Name
    }
    catch {
        return $null
    }
}

function Convert-ToVersion {
    param([object]$Value)

    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim().Replace(',', '.')
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    try {
        return [version]$text
    }
    catch {
        $parts = @($text -split '\.' | ForEach-Object {
            $number = 0
            [void][int]::TryParse($_, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)
            $number
        })
        while ($parts.Count -lt 4) { $parts += 0 }
        return [version]$parts[0], $parts[1], $parts[2], $parts[3]
    }
}

function Test-VersionAtLeast {
    param(
        [object]$Current,
        [version]$Minimum
    )

    $currentVersion = Convert-ToVersion $Current
    return $null -ne $currentVersion -and $currentVersion -ge $Minimum
}

function Get-WindowsResult {
    $productName = Get-RegistryValue $WindowsRegistryPath 'ProductName'
    $buildValue = Get-RegistryValue $WindowsRegistryPath 'CurrentBuild'
    $build = 0
    [void][int]::TryParse(([string]$buildValue), [ref]$build)

    [ordered]@{
        ProductName = if ($productName) { [string]$productName } else { 'Windows' }
        Build = $build
        MinBuild = $MinWindowsBuild
        Supported = $build -ge $MinWindowsBuild
    }
}

function Get-VCRedistResult {
    $installedValue = Get-RegistryValue $VCRedistRegistryPath 'Installed'
    $versionValue = Get-RegistryValue $VCRedistRegistryPath 'Version'
    $installed = $installedValue -eq 1
    $version = Convert-ToVersion $versionValue

    [ordered]@{
        Installed = $installed
        Version = if ($version) { $version.ToString() } else { $null }
        MinVersion = $MinVCRedistVersion.ToString()
        Supported = $installed -and $null -ne $version -and $version -ge $MinVCRedistVersion
    }
}

function Write-CheckLine {
    param(
        [string]$Title,
        [bool]$Ok,
        [string]$Detail
    )

    if ($Quiet -or $Json) { return }
    $label = if ($Ok) { '[OK] ' } else { '[X]  ' }
    $color = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  {0}{1}: {2}" -f $label, $Title, $Detail) -ForegroundColor $color
}

$windows = Get-WindowsResult
$vcRedist = Get-VCRedistResult

if (-not $Json -and -not $Quiet) {
    Write-Host 'Windows...' -ForegroundColor Cyan
}
Write-CheckLine 'Windows Build' $windows.Supported ("$($windows.Build) (requires $MinWindowsBuild+)")
Write-CheckLine 'VC++ Redistributable 2015-2022 (x64)' $vcRedist.Supported (
    if ($vcRedist.Version) { "$($vcRedist.Version) (requires $($vcRedist.MinVersion)+)" } else { "not installed (requires $($vcRedist.MinVersion)+)" }
)

$allOk = $windows.Supported -and $vcRedist.Supported

if ($Install -and -not $allOk -and $windows.Supported -and -not $vcRedist.Supported) {
    if (-not $Json -and -not $Quiet) {
        Write-Host ''
        Write-Host 'VC++ Redistributable is missing or outdated; attempting installation...' -ForegroundColor Yellow
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdministrator) {
        if (-not $Json) {
            Write-Host '  [X]  Installation requires an elevated PowerShell window.' -ForegroundColor Red
        }
        exit 2
    }

    $downloadPath = Join-Path ([System.IO.Path]::GetTempPath()) 'mnc-vc_redist.x64.exe'
    try {
        Invoke-WebRequest -Uri $VCRedistUrl -OutFile $downloadPath -UseBasicParsing
        $process = Start-Process -FilePath $downloadPath -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
        if ($process.ExitCode -notin @(0, 1638, 3010)) {
            throw "VC++ Redistributable installer returned exit code $($process.ExitCode)."
        }

        $vcRedist = Get-VCRedistResult
        $allOk = $windows.Supported -and $vcRedist.Supported
    }
    catch {
        if (-not $Json) {
            Write-Host ("  [X]  Installation failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
        }
        exit 2
    }
    finally {
        Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    }
}

$result = [ordered]@{
    Windows = $windows
    VCRedist = $vcRedist
    AllOk = $allOk
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
    exit $(if ($allOk) { 0 } else { 1 })
}

if (-not $Quiet) {
    Write-Host ''
    if ($allOk) {
        Write-Host '  SYSTEM READY' -ForegroundColor Green
    }
    else {
        Write-Host '  SYSTEM NOT READY' -ForegroundColor Yellow
        if (-not $Install) {
            Write-Host '  Run check-deps.bat -Install from an elevated terminal to install VC++ Redistributable.' -ForegroundColor Yellow
        }
    }
}

exit $(if ($allOk) { 0 } else { 1 })
