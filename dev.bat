@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  MosquitoNetCalculator - Dev Launch
echo ============================================
echo  Source: %CD%
echo.

set "APPDATA_AI=%APPDATA%\MosquitoNetCalculator\ai-settings.json"
set "LOCAL_AI=%~dp0ai-settings.local.json"

rem Restore AI settings from local repo backup if AppData settings are missing
if exist "%LOCAL_AI%" (
    if not exist "%APPDATA%\MosquitoNetCalculator" mkdir "%APPDATA%\MosquitoNetCalculator"
    if not exist "%APPDATA_AI%" (
        copy /y "%LOCAL_AI%" "%APPDATA_AI%" >nul
        echo [dev] Restored AI settings from ai-settings.local.json
    )
)

echo Starting current Debug project...
dotnet run --project "MosquitoNetCalculator\MosquitoNetCalculator.csproj" -c Debug
set "EXIT_CODE=%errorlevel%"

rem Save AI settings back to local repo backup
if exist "%APPDATA_AI%" (
    copy /y "%APPDATA_AI%" "%LOCAL_AI%" >nul
    echo.
    echo [dev] AI settings synchronized to ai-settings.local.json
)

if not "%EXIT_CODE%"=="0" (
    echo.
    echo RUN FAILED! Check errors above.
    pause
)

endlocal & exit /b %EXIT_CODE%
