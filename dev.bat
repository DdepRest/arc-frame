@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  MosquitoNetCalculator - Dev Launch
echo ============================================
echo  Source: %CD%
echo.
echo Starting current Debug project...
dotnet run --project "MosquitoNetCalculator\MosquitoNetCalculator.csproj" -c Debug
set "EXIT_CODE=%errorlevel%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo RUN FAILED! Check errors above.
    pause
)

endlocal & exit /b %EXIT_CODE%
