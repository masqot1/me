$ErrorActionPreference = 'Stop'
$HostName = 'com.truewebsitecloner.host'
$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
$NativeDir = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\native-host'
$ManifestPath = Join-Path $NativeDir "$HostName.json"
$InstallRecord = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\distribution-install.json'

if (Test-Path $RegPath) { Remove-Item $RegPath -Recurse -Force }
if (Test-Path $ManifestPath) { Remove-Item $ManifestPath -Force }
if (Test-Path $InstallRecord) { Remove-Item $InstallRecord -Force }

Write-Host 'TrueWebsiteCloner Native Messaging registration removed.' -ForegroundColor Green
Write-Host 'Project workspaces and captured project data were not deleted.' -ForegroundColor Yellow
Write-Host 'Remove the unpacked Chrome extension manually from chrome://extensions if desired.'
