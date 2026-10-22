@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0agents\scripts\arc-finish.ps1" %*
exit /b %ERRORLEVEL%
