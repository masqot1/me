@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-TrueWebsiteCloner.ps1"
if errorlevel 1 (echo INSTALL FAILED&pause&exit /b 1)
echo INSTALL PASS
pause
