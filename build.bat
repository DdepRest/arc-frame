@echo off
SETLOCAL EnableDelayedExpansion
echo ===============================================
echo   ARC-Frame Build + Deploy to GitHub Releases
echo ===============================================
echo.

REM === ENV ===
if "%GITHUB_TOKEN%"=="" (
    echo ERROR: set GITHUB_TOKEN=ghp_... first
    echo Get one at: https://github.com/settings/tokens
    exit /b 1
)
set GITHUB_OWNER=DdepRest
set GITHUB_REPO=arc-frame

REM === 1. Read version from .csproj ===
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" set "APP_VERSION=0.0.0"
echo App version: %APP_VERSION%
echo.

REM === 2. Clean old build ===
if exist "publish" (
    echo Cleaning old build...
    rmdir /s /q "publish"
)
if exist "MosquitoNetCalculator\bin" (
    echo Cleaning bin...
    rmdir /s /q "MosquitoNetCalculator\bin"
)
if exist "MosquitoNetCalculator\obj" (
    echo Cleaning obj...
    rmdir /s /q "MosquitoNetCalculator\obj"
)
echo.

REM === 3. Publish app ===
echo Publishing...
dotnet publish MosquitoNetCalculator\MosquitoNetCalculator.csproj ^
    -c Release -r win-x64 --self-contained ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish --nologo
if %errorlevel% neq 0 (
    echo BUILD FAILED!
    pause
    exit /b 1
)
echo.

REM === 4. Copy extras to publish ===
copy /y "MosquitoNetCalculator\prices.json" "publish\prices.json" >nul
copy /y "MosquitoNetCalculator\Resources\app_icon.ico" "publish\app_icon.ico" >nul
copy /y "check-deps.bat" "publish\check-deps.bat" >nul
copy /y "check-deps.ps1" "publish\check-deps.ps1" >nul
echo.

REM === 5. Create fresh settings.json ===
echo {"Theme":"light","ContractPrefix":"1","LocationName":"","FirstRunComplete":false} > "publish\settings.json"

REM === 6. Create ZIP archive ===
echo Creating ZIP archive...
powershell -NoProfile -Command "Compress-Archive -Force -Path 'publish\MosquitoNetCalculator.exe','publish\*.dll','publish\prices.json','publish\app_icon.ico','publish\runtimes','publish\check-deps.bat','publish\check-deps.ps1' -DestinationPath 'publish\ARC-Frame-%APP_VERSION%-full.zip'"
if not exist "publish\ARC-Frame-%APP_VERSION%-full.zip" (
    echo ERROR: ZIP archive creation failed.
    goto :end
)
echo.

REM === 7. Compute SHA-256 and file size ===
for /f "tokens=*" %%h in ('powershell -NoProfile -Command "(Get-FileHash 'publish\ARC-Frame-%APP_VERSION%-full.zip' -Algorithm SHA256).Hash.ToLower()"') do set "ZIP_SHA=%%h"
for /f %%s in ('powershell -NoProfile -Command "(Get-Item 'publish\ARC-Frame-%APP_VERSION%-full.zip').Length"') do set "ZIP_SIZE=%%s"
echo ZIP SHA-256: %ZIP_SHA%
echo ZIP Size: %ZIP_SIZE% bytes

REM === 8. Update releases.json ===
echo Updating releases.json...
powershell -NoProfile -ExecutionPolicy Bypass -File "update-releases-json.ps1" ^
    -Repository "%GITHUB_OWNER%/%GITHUB_REPO%" ^
    -Version "%APP_VERSION%" ^
    -Size %ZIP_SIZE% ^
    -Sha256 "%ZIP_SHA%"
echo.

REM === 9. Push updated releases.json to repo ===
echo Committing releases.json...
git add releases.json
git commit -m "release: v%APP_VERSION%" 2>nul
git push origin main
echo.

REM === 10. Create GitHub Release with ZIP asset ===
echo Creating GitHub Release v%APP_VERSION%...
gh release create v%APP_VERSION% ^
    "publish\ARC-Frame-%APP_VERSION%-full.zip" ^
    --repo "%GITHUB_OWNER%/%GITHUB_REPO%" ^
    --title "ARC-Frame %APP_VERSION%" ^
    --notes "ARC-Frame v%APP_VERSION%
    
Auto-update release via build.bat pipeline."
if %errorlevel% neq 0 (
    echo RELEASE CREATE FAILED!
    goto :end
)
echo.

REM === 11. Optional: build Inno Setup installer ===
if exist compile-installer.bat (
    echo Building Inno Setup installer...
    call compile-installer.bat
)
echo.

echo ===============================================
echo   DEPLOY SUCCESSFUL
echo   Release: https://github.com/%GITHUB_OWNER%/%GITHUB_REPO%/releases/tag/v%APP_VERSION%
echo   Manifest: https://raw.githubusercontent.com/%GITHUB_OWNER%/%GITHUB_REPO%/main/releases.json
echo ===============================================

:end
ENDLOCAL
pause
