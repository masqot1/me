@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Verify-ReleaseCandidate.ps1" -OutputDirectory "%~dp0release-candidate-output"
set RC=%ERRORLEVEL%
echo.
if %RC%==0 (echo V1.0-RC1 MASTER GATE PASSED) else (echo V1.0-RC1 MASTER GATE FAILED)
pause
exit /b %RC%
