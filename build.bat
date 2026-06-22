@echo off
echo ============================================
echo  MosquitoNetCalculator - Build Script
echo  Version: %DATE% (Velopack auto-update)
echo ============================================
echo.

REM === IMPORTANT: Delete old publish folder completely ===
if exist "publish" (
    echo Cleaning old build...
    rmdir /s /q "publish"
)

REM === Clean bin/obj to force full rebuild ===
if exist "MosquitoNetCalculator\bin" (
    echo Cleaning bin...
    rmdir /s /q "MosquitoNetCalculator\bin"
)
if exist "MosquitoNetCalculator\obj" (
    echo Cleaning obj...
    rmdir /s /q "MosquitoNetCalculator\obj"
)

REM Restore NuGet packages (including WebView2)
echo.
echo Restoring NuGet packages...
dotnet restore MosquitoNetCalculator/MosquitoNetCalculator.csproj
if %errorlevel% neq 0 (
    echo.
    echo RESTORE FAILED! Check errors above.
    pause
    exit /b 1
)

REM Build and publish as single-file executable
echo.
echo Building...
dotnet publish MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish

if %errorlevel% neq 0 (
    echo.
    echo BUILD FAILED! Check errors above.
    pause
    exit /b 1
)

REM Copy prices.json to publish folder
copy /y "MosquitoNetCalculator\prices.json" "publish\prices.json" >nul

REM Copy icon to publish folder
copy /y "MosquitoNetCalculator\Resources\app_icon.ico" "publish\app_icon.ico" >nul

REM Copy dependency checker scripts to publish folder (so they ship with the installer)
copy /y "check-deps.bat" "publish\check-deps.bat" >nul
copy /y "check-deps.ps1" "publish\check-deps.ps1" >nul

REM Create fresh settings.json with Velopack Flow enabled
copy /y NUL "publish\settings.json" >NUL
echo {"Theme":"light","ContractPrefix":"1","LocationName":"","FirstRunComplete":false,"UseVelopackFlow":true} > "publish\settings.json"

REM ── Velopack: package the published output for auto-update ──
echo.
echo ============================================
echo  Packaging for Velopack auto-update...
echo ============================================

REM Extract version from csproj
echo   Reading version from csproj...
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" set "APP_VERSION=0.0.0"
echo   Version: %APP_VERSION%

vpk pack ^
  --packId ARC-Frame ^
  --packVersion %APP_VERSION% ^
  --packDir publish ^
  --mainExe MosquitoNetCalculator.exe ^
  --packTitle "A.R.C. Frame" ^
  --outputDir publish\velo-release

if %errorlevel% neq 0 (
    echo.
    echo   VELOPACK PACK FAILED!
    echo   Install vpk:  dotnet tool install -g vpk
    echo   The .exe is still usable; only auto-update packaging was skipped.
) else (
    echo   Velopack package created: publish\velo-release\
    echo.
    REM Copy Velopack Setup.exe to publish root for Inno Setup to bundle
    copy /y "publish\velo-release\ARC-Frame-win-Setup.exe" "publish\velopack-setup.exe" >nul
    echo   Velopack setup copied to publish\velopack-setup.exe for Inno Setup.
    echo.
    echo   Next step: run deploy-flow.bat to upload to Velopack Flow ^(free^)
    echo   Or manually upload publish\velo-release\ to your server.
)

echo.
echo ============================================
echo  BUILD SUCCESS!
echo  Output: publish\MosquitoNetCalculator.exe
echo ============================================
echo.
echo  Next: run deploy-flow.bat to publish to Velopack Flow
echo ============================================

REM === Launch the application (the published one) ===
set "APP_EXE=publish\MosquitoNetCalculator.exe"
if exist "%APP_EXE%" (
    echo.
    echo Starting application...
    start "" "%APP_EXE%"
) else (
    echo.
    echo WARNING: Published executable not found. Trying original build location...
    set "APP_EXE=C:\Users\Asus\Desktop\gwga\MosquitoNetCalculator\bin\Release\net8.0-windows\win-x64\MosquitoNetCalculator.exe"
    if exist "%APP_EXE%" (
        echo Starting from original location...
        start "" "%APP_EXE%"
    ) else (
        echo.
        echo ERROR: Cannot find MosquitoNetCalculator.exe
        echo Tried: publish\MosquitoNetCalculator.exe
        echo And:  %APP_EXE%
    )
)

echo.
pause