$ErrorActionPreference='Stop'
$HostName='com.truewebsitecloner.host'
$RegPath="HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
$ManifestPath=Join-Path $env:LOCALAPPDATA "TrueWebsiteCloner\native-host\$HostName.json"
if(Test-Path $RegPath){Remove-Item $RegPath -Recurse -Force}
if(Test-Path $ManifestPath){Remove-Item $ManifestPath -Force}
Write-Host 'PASS  Native Messaging registration removed.' -ForegroundColor Green
Write-Host 'Project workspaces and captured data were not deleted.' -ForegroundColor Yellow
