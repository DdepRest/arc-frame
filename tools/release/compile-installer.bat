@echo off
cd /d "%~dp0"
echo === Working dir: %CD% ===

REM === Extract version from .csproj (single source of truth) ===
echo === Reading version from .csproj... ===
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo 2^>nul') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" (
    echo [WARNING] Could not read version from .csproj, falling back to 0.0.0
    set "APP_VERSION=0.0.0"
)
echo === Version: %APP_VERSION% ===

if not exist Output mkdir Output
echo === Starting ISCC.exe with version %APP_VERSION% ===
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=%APP_VERSION%" "installer.iss"
echo === ISCC exit code: %errorlevel% ===
echo === Listing Output\ ===
dir /b "Output\*.exe" 2>nul
exit /b %errorlevel%
