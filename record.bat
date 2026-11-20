@echo off
chcp 65001 >nul
title CCalc Recorder 9.12

rem Overlay recorder: table of clicks + comments.
rem F9 - comment on last click, F10 - stop and save.
rem Old simple recorder is still tools\pvc_bridge\recorder.py
python "%~dp0tools\pvc_bridge\recorder_overlay.py"

if errorlevel 1 pause
