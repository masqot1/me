$ErrorActionPreference = "Stop"
$HostName = "com.truewebsitecloner.host"
$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
if (Test-Path $RegPath) { Remove-Item $RegPath -Recurse -Force }
$NativeDir = Join-Path $env:LOCALAPPDATA "TrueWebsiteCloner\native-host"
if (Test-Path $NativeDir) { Remove-Item $NativeDir -Recurse -Force }
Write-Host "TrueWebsiteCloner development native host registration removed." -ForegroundColor Green
