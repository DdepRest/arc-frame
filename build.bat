@echo off
echo ============================================
echo  MosquitoNetCalculator - Build Script
echo  Version: %DATE% (GitHub Releases auto-update)
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

REM Copy dependency checker script to publish folder (so it ships with the installer)
copy /y "check-deps.bat" "publish\check-deps.bat" >nul

REM Create fresh settings.json
copy /y NUL "publish\settings.json" >NUL
echo {"Theme":"light","ContractPrefix":"1","LocationName":"","FirstRunComplete":false} > "publish\settings.json"

REM ── Package for GitHub Releases ──
echo.
echo ============================================
echo  Packaging for GitHub Releases...
echo ============================================

REM Extract version from csproj
echo   Reading version from csproj...
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" set "APP_VERSION=0.0.0"
echo   Version: %APP_VERSION%

REM Create ZIP for GitHub Release (exe + dlls only — no settings/prices/orders)
powershell -NoProfile -Command "Compress-Archive -Force -Path 'publish\MosquitoNetCalculator.exe','publish\*.dll' -DestinationPath 'publish\ARC-Frame-%APP_VERSION%-full.zip'" 2>nul

if exist "publish\ARC-Frame-%APP_VERSION%-full.zip" (
    echo   ZIP created: publish\ARC-Frame-%APP_VERSION%-full.zip
    echo.
    echo   Next step: run gh release upload v%APP_VERSION% publish\ARC-Frame-%%APP_VERSION%%-full.zip
    echo   Or: run update-releases-json.ps1 to update the manifest
) else (
    echo   ZIP creation failed!
)

echo.
echo ============================================
echo  BUILD SUCCESS!
echo  Output: publish\MosquitoNetCalculator.exe
echo ============================================
echo.
echo  Next: use gh release upload to publish to GitHub Releases
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