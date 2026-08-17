# =============================================================================
# create-manual-update.ps1
# Создаёт portable ZIP для РУЧНОГО обновления программы.
#
# Назначение: пользователь скачивает ZIP, распаковывает в папку с программой
# с заменой файлов — и обновление применено.
#
# Использование:
#   1. Собрать проект:   dotnet publish ... (или build.bat)
#   2. Запустить:        powershell -ExecutionPolicy Bypass -File create-manual-update.ps1
#   3. ZIP будет в:      publish\ARC-Frame-X.Y.Z-manual-update.zip
#
# Отличие от автообновления (build.bat → ARC-Frame-X.Y.Z-full.zip):
#   - Добавлен README_ОБНОВЛЕНИЕ.txt с инструкцией.
#   - Добавлен check-deps.bat для проверки зависимостей.
#   - Явная метка "manual-update" в имени файла.
#   - Не требует GitHub Releases, подходит для распространения через флешку/почту.
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$publishDir = Join-Path $scriptRoot "publish"
$csprojPath = Join-Path $scriptRoot "MosquitoNetCalculator\MosquitoNetCalculator.csproj"

# Читаем версию из .csproj
$version = & dotnet msbuild $csprojPath -getProperty:Version -nologo 2>$null
if (-not $version) {
    Write-Error "Не удалось прочитать версию из $csprojPath"
    exit 1
}
$version = $version.Trim()
Write-Host "=== Версия: $version ===" -ForegroundColor Cyan

# Проверяем, что publish существует
if (-not (Test-Path $publishDir)) {
    Write-Error "Папка publish не найдена: $publishDir. Сначала запустите build.bat или dotnet publish."
    exit 1
}

$exePath = Join-Path $publishDir "MosquitoNetCalculator.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "MosquitoNetCalculator.exe не найден в $publishDir. Сначала запустите build.bat."
    exit 1
}

# ── Создаём README с инструкцией ──
$readmePath = Join-Path $publishDir "README_ОБНОВЛЕНИЕ.txt"
$readmeContent = @"
============================================================
  РУЧНОЕ ОБНОВЛЕНИЕ MosquitoNetCalculator (ARC-Frame)
  Версия: $version
============================================================

ЧТО ДЕЛАТЬ:

1. Закройте программу MosquitoNetCalculator, если она запущена.

2. Найдите папку, где установлена программа:
   - Если устанавливали через setup.exe: C:\Program Files\MosquitoNetCalculator\
   - Если используете portable-версию: папка, куда распаковали архив

3. Распакуйте содержимое ЭТОГО ZIP-архива в папку с программой
   С ЗАМЕНОЙ ВСЕХ ФАЙЛОВ.

4. Запустите MosquitoNetCalculator.exe.

   Все ваши данные (заказы, настройки, цены) сохранятся —
   они находятся в %AppData%\MosquitoNetCalculator\

ВАЖНО:
- Не удаляйте папку %AppData%\MosquitoNetCalculator\ — там ваши заказы.
- При проблемах запустите check-deps.bat из папки с программой.
- Если .exe не запускается: ПКМ → Свойства → Разблокировать.

============================================================
"@
[System.IO.File]::WriteAllText($readmePath, $readmeContent, [System.Text.UTF8Encoding]::new($true))
Write-Host "  README_ОБНОВЛЕНИЕ.txt создан" -ForegroundColor Green

# ── Создаём portable ZIP ──
$zipName = "ARC-Frame-$version-manual-update.zip"
$zipPath = Join-Path $publishDir $zipName

# Удаляем старый ZIP если есть
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
    Write-Host "  Старый $zipName удалён" -ForegroundColor DarkYellow
}

# Собираем файлы для ZIP: exe + dll + runtimes + readme + check-deps
$filesToZip = @(
    (Join-Path $publishDir "MosquitoNetCalculator.exe"),
    (Join-Path $publishDir "README_ОБНОВЛЕНИЕ.txt")
)

# DLL
$dlls = Get-ChildItem $publishDir -Filter "*.dll" | Select-Object -ExpandProperty FullName
$filesToZip += $dlls

# runtimes (если есть)
$runtimesDir = Join-Path $publishDir "runtimes"
if (Test-Path $runtimesDir) {
    $filesToZip += $runtimesDir
}

# check-deps.bat (если есть)
$checkDeps = Join-Path $publishDir "check-deps.bat"
if (Test-Path $checkDeps) {
    $filesToZip += $checkDeps
}

Write-Host "  Файлов для упаковки: $($filesToZip.Count)" -ForegroundColor Gray

Compress-Archive -Force -Path $filesToZip -DestinationPath $zipPath

if (Test-Path $zipPath) {
    $zipSize = (Get-Item $zipPath).Length
    $zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    $sizeMB = [math]::Round($zipSize / 1MB, 2)

    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  Portable ZIP ручного обновления создан:" -ForegroundColor Green
    Write-Host "    Файл:  $zipPath" -ForegroundColor White
    Write-Host "    Размер: $sizeMB MB ($zipSize байт)" -ForegroundColor White
    Write-Host "    SHA256: $zipHash" -ForegroundColor White
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Чтобы обновить программу вручную:" -ForegroundColor Yellow
    Write-Host "    1. Скопируйте $zipName на компьютер с программой" -ForegroundColor Yellow
    Write-Host "    2. Распакуйте в папку с программой с заменой файлов" -ForegroundColor Yellow
    Write-Host "    3. Запустите MosquitoNetCalculator.exe" -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Error "Не удалось создать ZIP-архив."
    exit 1
}
