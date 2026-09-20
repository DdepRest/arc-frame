# verify-baselines.ps1
# Гейт «кадр == базлайн»: сравнивает кадры прогона харнесса с эталонными PNG
# и падает на ЛЮБОМ расхождении сверх допуска.
#
# Зачем он нужен. После того как кадры стали воспроизводимыми (GOTCHAS §30, §35),
# расхождение базлайна означает РЕАЛЬНОЕ изменение вида — но эта проверка
# выполнялась глазами и по git diff, то есть держалась на дисциплине. Здесь она
# становится командой с кодом возврата, её используют:
#   * прогон с проверкой:      .tools/uiverify.ps1 -Theme dark -Verify
#   * проверка «другая дата»:  .tools/uiverify.ps1 -Theme dark -ContractDate 31.12.2027 \
#                                -OutDir .tools/_verify-date -Verify
#                              (тогда -ExpectChange 04a-sidebar-open-dark,05-print-toolbar-dark)
#   * релизный чек-лист перед публикацией сборки.
param(
    [string]$Ref = ".tools/shots",
    [string]$Run = ".tools/shots",
    # Допуск на канал: ниже него различие считается артефактом сжатия/сглаживания.
    [int]$Tolerance = 6,
    # Кадры (имена файлов или их части), расхождение которых ОЖИДАЕТСЯ и не валит
    # гейт — например 04a/05 при прогоне с другой датой договора.
    [string[]]$ExpectChange = @(),
    # Фильтр по имени эталона: прогон одной темы проверяет только свои кадры
    # (иначе гейт справедливо ругался бы «нет кадра» на вторую тему).
    [string]$Filter = "*.png",
    [switch]$Debug
)

# Шаблоны ожидаемых расхождений. Разбираем и запятые внутри значения: вызов через
# `powershell -File ... -ExpectChange a,b` приходит ОДНОЙ строкой (аргументы -File
# не переразбираются как синтаксис PowerShell), и без этого «ожидаемое» молча
# превратилось бы в ошибку гейта.
$expectedPatterns = @()
foreach ($p in $ExpectChange) { $expectedPatterns += (@($p -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Compare-Frame([string]$a, [string]$b, [int]$tol) {
    $ia = [System.Drawing.Bitmap]::FromFile($a)
    $ib = [System.Drawing.Bitmap]::FromFile($b)
    try {
        if ($ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) {
            return [pscustomobject]@{ Diff = -1; Box = ("размер {0}x{1} против {2}x{3}" -f $ia.Width, $ia.Height, $ib.Width, $ib.Height) }
        }
        $rect = New-Object System.Drawing.Rectangle(0, 0, $ia.Width, $ia.Height)
        $fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
        $ba = $ia.Clone($rect, $fmt); $bb = $ib.Clone($rect, $fmt)
        try {
            $da = $ba.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
            $db = $bb.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
            try {
                $n = [Math]::Abs($da.Stride) * $ia.Height
                $bufA = New-Object byte[] $n
                $bufB = New-Object byte[] $n
                [System.Runtime.InteropServices.Marshal]::Copy($da.Scan0, $bufA, 0, $n)
                [System.Runtime.InteropServices.Marshal]::Copy($db.Scan0, $bufB, 0, $n)
                $stride = [Math]::Abs($da.Stride)
                $count = 0; $minX = $ia.Width; $minY = $ia.Height; $maxX = -1; $maxY = -1
                for ($y = 0; $y -lt $ia.Height; $y++) {
                    $row = $y * $stride
                    for ($x = 0; $x -lt $ia.Width; $x++) {
                        $i = $row + $x * 4
                        $d = [Math]::Abs($bufA[$i] - $bufB[$i])
                        if ([Math]::Abs($bufA[$i + 1] - $bufB[$i + 1]) -gt $d) { $d = [Math]::Abs($bufA[$i + 1] - $bufB[$i + 1]) }
                        if ([Math]::Abs($bufA[$i + 2] - $bufB[$i + 2]) -gt $d) { $d = [Math]::Abs($bufA[$i + 2] - $bufB[$i + 2]) }
                        if ($d -gt $tol) {
                            $count++
                            if ($x -lt $minX) { $minX = $x }
                            if ($x -gt $maxX) { $maxX = $x }
                            if ($y -lt $minY) { $minY = $y }
                            if ($y -gt $maxY) { $maxY = $y }
                        }
                    }
                }
                $box = if ($count -gt 0) { "$minX,$minY - $maxX,$maxY" } else { "нет" }
                return [pscustomobject]@{ Diff = $count; Box = $box }
            }
            finally { $ba.UnlockBits($da); $bb.UnlockBits($db) }
        }
        finally { $ba.Dispose(); $bb.Dispose() }
    }
    finally { $ia.Dispose(); $ib.Dispose() }
}

if (-not (Test-Path $Ref)) { Write-Host "BASELINE-GATE: нет каталога эталонов: $Ref"; exit 2 }
if (-not (Test-Path $Run)) { Write-Host "BASELINE-GATE: нет каталога прогона: $Run"; exit 2 }

$refFiles = @(Get-ChildItem -Path $Ref -Filter $Filter -File | Sort-Object Name)
$runFiles = @(Get-ChildItem -Path $Run -Filter $Filter -File | Sort-Object Name)
if ($Debug) {
    Write-Host ("[debug] Ref='{0}' Run='{1}' refFiles={2} runFiles={3}" -f $Ref, $Run, $refFiles.Count, $runFiles.Count)
    if ($refFiles.Count -gt 0) { Write-Host ("[debug] первый эталон: '{0}' типа {1}" -f $refFiles[0].Name, $refFiles[0].GetType().Name) }
}
$runNames = $runFiles | ForEach-Object { $_.Name }
$fails = New-Object System.Collections.Generic.List[string]
$expectedDiffs = 0
$checked = 0

# Имена переменных цикла НЕ должны совпадать с параметрами ($Ref/$Run): PowerShell
# не различает регистр, и foreach ($ref in ...) при параметре [string]$Ref молча
# приводит каждый элемент к строке — $ref.Name становится пустым, и гейт начинает
# ругаться «нет кадра» на все кадры сразу (ловушка, пойманная на первом прогоне).
foreach ($refItem in $refFiles) {
    if ($runNames -notcontains $refItem.Name) { $fails.Add("нет кадра в прогоне: $($refItem.Name)"); continue }
    $isExpected = $false
    foreach ($pattern in $expectedPatterns) { if ($refItem.Name -like "*$pattern*") { $isExpected = $true } }
    $r = Compare-Frame $refItem.FullName (Join-Path $Run $refItem.Name) $Tolerance
    $checked++
    if ($r.Diff -ne 0) {
        if ($isExpected) { $expectedDiffs++; Write-Host ("  [ожидаемо] {0,-46} расхождение {1} px, область {2}" -f $refItem.Name, $r.Diff, $r.Box) }
        else { $fails.Add(("{0}: расхождение {1} px, область {2}" -f $refItem.Name, $r.Diff, $r.Box)) }
    }
}
foreach ($runItem in $runFiles) {
    if (-not (Test-Path (Join-Path $Ref $runItem.Name))) { $fails.Add("лишний кадр (нет эталона): $($runItem.Name)") }
}

Write-Host ""
Write-Host ("BASELINE-GATE: проверено {0} кадров, допуск {1}/канал" -f $checked, $Tolerance)
if ($expectedDiffs -gt 0) { Write-Host ("BASELINE-GATE: ожидаемых расхождений {0} ({1})" -f $expectedDiffs, ($expectedPatterns -join ", ")) }
if ($fails.Count -eq 0) {
    Write-Host "BASELINE-GATE: OK — все кадры совпали с эталоном"
    exit 0
}
Write-Host "BASELINE-GATE: РАСХОЖДЕНИЯ"
foreach ($f in $fails) { Write-Host "  $f" }
exit 1
