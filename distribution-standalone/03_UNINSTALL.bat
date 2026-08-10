@echo off
set "KEY=HKCU\Software\Google\Chrome\NativeMessagingHosts\com.truewebsitecloner.host"
reg delete "%KEY%" /f >nul 2>&1
del /q "%LOCALAPPDATA%\TrueWebsiteCloner\native-host\com.truewebsitecloner.host.json" >nul 2>&1
echo Native Messaging registration removed. Project data was not deleted.
pause
