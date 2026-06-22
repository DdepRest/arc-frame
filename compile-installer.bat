@echo off
cd /d "%~dp0"
echo === Working dir: %CD% ===
if not exist Output mkdir Output
echo === Starting ISCC.exe ===
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer.iss"
echo === ISCC exit code: %errorlevel% ===
echo === Listing Output\ ===
dir /b "Output\*.exe" 2>nul
exit /b %errorlevel%
