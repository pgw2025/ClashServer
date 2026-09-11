@echo off
rem One-click stop: kill processes on 5080/5173
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop.ps1" %*
set "code=%errorlevel%"
echo.
pause
exit /b %code%
