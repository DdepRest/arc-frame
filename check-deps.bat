@echo off
REM ============================================================
REM  MosquitoNetCalculator — Предварительная проверка зависимостей (Windows-обёртка)
REM ============================================================
REM  Этот скрипт просто вызывает PowerShell-версию check-deps.ps1.
REM
REM  Использование:
REM     check-deps.bat             — только проверить и показать отчёт
REM     check-deps.bat -Install    — проверить и доустановить недостающее
REM     check-deps.bat -Quiet      — без информационных сообщений
REM     check-deps.bat -Json       — вывести результат в формате JSON
REM
REM  Требует PowerShell 5.0+ (встроен в Windows 10 / 11).
REM ============================================================

setlocal

REM Путь к самому скрипту (без расширения .bat)
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%check-deps.ps1"

if not exist "%PS_SCRIPT%" (
    echo [ERROR] Не найден скрипт: %PS_SCRIPT%
    echo Разместите check-deps.ps1 рядом с check-deps.bat.
    exit /b 1
)

REM Предварительная проверка версии PowerShell (должна быть 5.0+)
powershell.exe -NoProfile -Command "if ($PSVersionTable.PSVersion.Major -lt 5) { Write-Host '[X] Требуется PowerShell 5.0 или новее. У вас:' $PSVersionTable.PSVersion -ForegroundColor Red; Write-Host '    Установите: https://aka.ms/powershell' -ForegroundColor Yellow; exit 3 }" >nul 2>&1
set "PS_CHECK_ERR=%errorlevel%"
if %PS_CHECK_ERR% GTR 2 (
    if %PS_CHECK_ERR% == 3 (
        echo [X] Требуется PowerShell 5.0 или новее. Установите: https://aka.ms/powershell
        endlocal & exit /b 3
    ) else (
        echo [X] PowerShell не найден или произошла ошибка (код %PS_CHECK_ERR%).
        endlocal & exit /b %PS_CHECK_ERR%
    )
)

REM Передаём все аргументы как есть
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXITCODE=%errorlevel%"

endlocal & exit /b %EXITCODE%
