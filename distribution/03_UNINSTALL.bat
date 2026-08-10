@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-TrueWebsiteCloner.ps1"
if errorlevel 1 (
  echo.
  echo UNINSTALL FAILED
  pause
  exit /b 1
)
echo.
echo UNINSTALL PASS
pause
