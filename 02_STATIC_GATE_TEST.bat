@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tests\Test-Foundation.ps1"
set RC=%ERRORLEVEL%
echo.
if %RC%==0 (echo STATIC GATE PASSED) else (echo STATIC GATE FAILED)
pause
exit /b %RC%
