@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0artifacts\desktop\TrueWebsiteCloner.exe" (
  echo TrueWebsiteCloner.exe was not found.
  pause
  exit /b 1
)
start "TrueWebsiteCloner" "%~dp0artifacts\desktop\TrueWebsiteCloner.exe"
