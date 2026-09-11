@echo off
rem One-click start: backend(5080) + Vite hot-reload(5173)
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
set "code=%errorlevel%"
echo.
pause
exit /b %code%
