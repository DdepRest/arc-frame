@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PROJECT_ROOT=%SCRIPT_DIR%..\.."
cd /d "%PROJECT_ROOT%"

echo === Working dir: %CD% ===

REM === Extract version from .csproj (single source of truth) ===
echo === Reading version from .csproj... ===
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo 2^>nul') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" (
    echo [ERROR] Could not read version from .csproj
    endlocal & exit /b 1
)
echo === Version: %APP_VERSION% ===

if not exist Output mkdir Output

set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" (
    echo [ERROR] Inno Setup 6 ISCC.exe was not found.
    echo         Install it from https://jrsoftware.org/isdl.php
    endlocal & exit /b 1
)

if not exist publish\MosquitoNetCalculator.exe (
    echo [ERROR] publish\MosquitoNetCalculator.exe was not found.
    echo         Run build.bat or dotnet publish first.
    endlocal & exit /b 1
)
if not exist publish\check-deps.ps1 (
    echo [ERROR] publish\check-deps.ps1 was not found.
    echo         Re-run build.bat so both dependency-check scripts are packaged.
    endlocal & exit /b 1
)

echo === Starting ISCC.exe with version %APP_VERSION% ===
"%ISCC_EXE%" "/DMyAppVersion=%APP_VERSION%" "installer.iss"
set "EXIT_CODE=%ERRORLEVEL%"
echo === ISCC exit code: %EXIT_CODE% ===
echo === Listing Output\ ===
dir /b "Output\*.exe" 2>nul

endlocal & exit /b %EXIT_CODE%
