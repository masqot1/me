@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-RuntimeGate-0.2B.ps1"
set RC=%ERRORLEVEL%
echo.
if %RC%==0 (echo RUNTIME GATE 0.2B PASSED) else (echo RUNTIME GATE 0.2B FAILED)
pause
exit /b %RC%
