@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0agents\scripts\find-task.ps1" %*
exit /b %ERRORLEVEL%
