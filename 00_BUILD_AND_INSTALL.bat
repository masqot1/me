@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Dev.ps1"
if errorlevel 1 (
  echo.
  echo BUILD/INSTALL FAILED
  pause
  exit /b 1
)
echo.
echo BUILD/INSTALL PASSED
pause
