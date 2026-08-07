@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: 04_RELEASE_PROJECT.bat ^<project-folder^> ^<output.twcrelease^>
  exit /b 2
)
if "%~2"=="" (
  echo Usage: 04_RELEASE_PROJECT.bat ^<project-folder^> ^<output.twcrelease^>
  exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Release-Project.ps1" -Project "%~1" -Output "%~2"
exit /b %ERRORLEVEL%
