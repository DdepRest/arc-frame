@echo off
REM ============================================================
REM  MosquitoNetCalculator - Dependency Check
REM ============================================================
REM  Checks Windows 10/11 and VC++ Redistributable 2015-2022.
REM  The application is self-contained and does not require a web runtime.
REM
REM  Usage:
REM     check-deps.bat                 - check and show report
REM     check-deps.bat -Install        - install missing VC++ Redistributable
REM     check-deps.bat -Quiet          - suppress human-readable output
REM     check-deps.bat -Json           - emit JSON output
REM
REM  Requires PowerShell 5.0+ (built into supported Windows versions).
REM ============================================================

setlocal
set "SCRIPT_DIR=%~dp0"

if not exist "%SCRIPT_DIR%check-deps.ps1" (
    echo ERROR: check-deps.ps1 was not found next to this script.
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%check-deps.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

endlocal & exit /b %EXIT_CODE%
