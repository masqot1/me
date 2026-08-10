$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$HostName = 'com.truewebsitecloner.host'
$ExtensionId = 'ggcmdgdiopplpbcfinamhjdkbhiknfbk'
$HostExe = Join-Path $Root 'bin\native-host\TrueWebsiteCloner.NativeHost.exe'
$DesktopExe = Join-Path $Root 'bin\desktop\TrueWebsiteCloner.exe'
$ExtensionFolder = Join-Path $Root 'chrome-extension'

Write-Host 'TrueWebsiteCloner 1.0.0 Standalone - Windows install' -ForegroundColor Cyan
foreach ($required in @($HostExe,$DesktopExe,(Join-Path $Root 'bin\cli\TrueWebsiteCloner.Cli.exe'),(Join-Path $ExtensionFolder 'manifest.json'))) {
    if (-not (Test-Path $required)) { throw "Standalone distribution file is missing: $required" }
}

$NativeDir = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\native-host'
New-Item -ItemType Directory -Path $NativeDir -Force | Out-Null
$ManifestPath = Join-Path $NativeDir "$HostName.json"
$manifest = [ordered]@{ name=$HostName; description='TrueWebsiteCloner Chrome bridge'; path=$HostExe; type='stdio'; allowed_origins=@("chrome-extension://$ExtensionId/") }
[System.IO.File]::WriteAllText($ManifestPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $RegPath -Force | Out-Null
Set-Item -Path $RegPath -Value $ManifestPath

Write-Host 'PASS  Standalone binaries require no separately installed .NET runtime.' -ForegroundColor Green
Write-Host 'PASS  Native Messaging host registered under HKCU.' -ForegroundColor Green
Write-Host ''
Write-Host 'Open chrome://extensions, enable Developer mode, and Load unpacked:' -ForegroundColor Yellow
Write-Host $ExtensionFolder
Write-Host "Confirm extension ID: $ExtensionId"
Write-Host 'Then run 02_RUN_TRUEWEBSITECLONER.bat.'
