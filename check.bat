@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0agents\scripts\arc-check.ps1" %*
exit /b %ERRORLEVEL%
