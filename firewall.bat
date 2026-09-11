@echo off
rem One-click firewall: allow LAN access to 5080/5173 (run as administrator once)
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0firewall.ps1" %*
set "code=%errorlevel%"
echo.
pause
exit /b %code%
