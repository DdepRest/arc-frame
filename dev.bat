@echo off
echo ============================================
echo  MosquitoNetCalculator — Dev Launch
echo ============================================
echo.
echo Building in Debug mode...
dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Debug
if %errorlevel% neq 0 (
    echo.
    echo BUILD FAILED! Check errors above.
    pause
    exit /b 1
)
echo.
echo Starting application...
start "" "MosquitoNetCalculator/bin/Debug/net8.0-windows/MosquitoNetCalculator.exe"
