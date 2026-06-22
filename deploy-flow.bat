@echo off
REM ─────────────────────────────────────────────────────────────────────
REM Optional flags (parse before first use so echo reflects them):
REM   --no-draft=true   Record user intent + warn that vpk's hosted
REM                     service does not expose a CLI flag for skipping
REM                     the Draft step. The manual "Publish" click in
REM                     flow.velopack.io is still required.
REM   --no-draft=false  Default. Uploads as Draft (safe — manual click).
REM   --waitForLive=true  Pass --waitForLive to vpk so the CLI blocks
REM                       until Flow finishes processing (or times out).
REM ─────────────────────────────────────────────────────────────────────
set "NO_DRAFT=false"
set "WAIT_FOR_LIVE=false"
:parse_args
if "%~1"=="" goto :parse_args_done
if /i "%~1"=="--no-draft=true"    set "NO_DRAFT=true"
if /i "%~1"=="--waitForLive=true"  set "WAIT_FOR_LIVE=true"
if /i "%~1"=="--waitForLive=false" set "WAIT_FOR_LIVE=false"
shift
goto :parse_args
:parse_args_done

echo ============================================
echo  MosquitoNetCalculator - Deploy to Velopack Flow
echo  (Free hosting for auto-updates)
echo ============================================
echo.

REM ── Step 1: Check vpk is installed ──
where vpk >nul 2>nul
if %errorlevel% neq 0 (
    echo   vpk not found. Installing...
    dotnet tool install -g vpk
    if %errorlevel% neq 0 (
        echo   ERROR: Failed to install vpk.
        echo   Try manually: dotnet tool install -g vpk
        goto :end
    )
    echo   vpk installed successfully.
    echo.
)

REM ── Step 2: Check velo-release exists ──
if not exist "publish\velo-release\" (
    echo   ERROR: publish\velo-release\ not found.
    echo   Run build.bat first to create the Velopack package.
    goto :end
)

REM ── Step 3: Read version for display ──
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo') do set "APP_VERSION=%%i"
if "%APP_VERSION%"=="" set "APP_VERSION=?"

echo   Publishing v%APP_VERSION% to Velopack Flow...
if /i "%NO_DRAFT%"=="true" (
    echo.
    echo   ┌── NOTE: --no-draft=true does not bypass the Draft step. ──┐
    echo   │ vpk's hosted service does not expose a CLI flag for it.  │
    echo   │ This flag is currently a marker (no effect on upload).   │
    echo   │ The manual Publish click in flow.velopack.io is required.│
    echo   │ Use --waitForLive=true to make the CLI block until Flow  │
    echo   │ finishes processing a Draft you then Publish yourself.  │
    echo   └───────────────────────────────────────────────────────────┘
)
echo.

REM ── Step 4: Publish to Velopack Flow ──
set "WAIT_FLAG="
if /i "%WAIT_FOR_LIVE%"=="true" set "WAIT_FLAG=--waitForLive true"
vpk publish %WAIT_FLAG% --outputDir publish\velo-release --channel win

if %errorlevel% equ 0 goto :success

REM ── Publish failed — diagnose ──
echo.
echo   ╔══════════════════════════════════════════════════════╗
echo   ║  PUBLISH FAILED                                      ║
echo   ║                                                      ║
echo   ║  Most likely you need to log in first:               ║
echo   ║                                                      ║
echo   ║    1. Create account: https://flow.velopack.io       ║
echo   ║    2. Create a project (e.g. MosquitoNetCalculator)  ║
echo   ║    3. Run: vpk login                                  ║
echo   ║    4. Then re-run deploy-flow.bat                    ║
echo   ║                                                      ║
echo   ║  If already logged in — check network / Flow status. ║
echo   ╚══════════════════════════════════════════════════════╝
goto :end

:success
REM ── Step 5: Success! Show next steps ──
echo.
echo   ╔══════════════════════════════════════════════════════╗
echo   ║  PUBLISHED SUCCESSFULLY!                             ║
echo   ║                                                      ║
echo   ║  Next steps:                                         ║
echo   ║                                                      ║
echo   ║  1. Go to https://flow.velopack.io                  ║
echo   ║  2. Open your project                               ║
echo   ║  3. Copy the "Update URL" from project settings     ║
echo   ║  4. Paste it into settings.json as "UpdateUrl"      ║
echo   ║                                                      ║
echo   ║  Example settings.json:                              ║
echo   ║  {                                                   ║
echo   ║    "UpdateUrl": "https://api.velopack.io/...",      ║
echo   ║    ...                                               ║
echo   ║  }                                                   ║
echo   ║                                                      ║
echo   ║  After that, users will see:                        ║
echo   ║  "Доступна новая версия: v%APP_VERSION%"             ║
echo   ║  in the app on next launch.                         ║
echo   ╚══════════════════════════════════════════════════════╝

:end
echo.
pause
