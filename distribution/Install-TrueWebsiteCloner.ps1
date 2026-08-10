$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$HostName = 'com.truewebsitecloner.host'
$ExtensionId = 'ggcmdgdiopplpbcfinamhjdkbhiknfbk'

Write-Host 'TrueWebsiteCloner 1.0.0 - Windows install' -ForegroundColor Cyan

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw '.NET 10 runtime was not found. Install .NET 10 Desktop Runtime and ASP.NET Core Runtime before running TrueWebsiteCloner.'
}

$runtimes = @(& dotnet --list-runtimes)
foreach ($required in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App','Microsoft.AspNetCore.App')) {
    if (-not ($runtimes | Where-Object { $_ -match "^$([regex]::Escape($required))\s+10\." })) {
        throw "Required .NET 10 runtime is missing: $required"
    }
}

$DesktopExe = Join-Path $Root 'artifacts\desktop\TrueWebsiteCloner.exe'
$HostExe = Join-Path $Root 'artifacts\native-host\TrueWebsiteCloner.NativeHost.exe'
$ExtensionFolder = Join-Path $Root 'chrome-extension'
foreach ($requiredPath in @($DesktopExe,$HostExe,(Join-Path $ExtensionFolder 'manifest.json'))) {
    if (-not (Test-Path $requiredPath)) { throw "Distribution file is missing: $requiredPath" }
}

$NativeDir = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\native-host'
New-Item -ItemType Directory -Path $NativeDir -Force | Out-Null
$ManifestPath = Join-Path $NativeDir "$HostName.json"
$manifest = [ordered]@{
    name = $HostName
    description = 'TrueWebsiteCloner Chrome bridge'
    path = $HostExe
    type = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
}
[System.IO.File]::WriteAllText($ManifestPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $RegPath -Force | Out-Null
Set-Item -Path $RegPath -Value $ManifestPath

$InstallRecord = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\distribution-install.json'
$record = [ordered]@{
    version = '1.0.0'
    distributionRoot = $Root
    nativeHostManifest = $ManifestPath
    extensionId = $ExtensionId
}
[System.IO.File]::WriteAllText($InstallRecord, ($record | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host 'PASS  Native Messaging host registered for the current Windows user.' -ForegroundColor Green
Write-Host 'PASS  Required .NET 10 runtimes detected.' -ForegroundColor Green
Write-Host ''
Write-Host 'Chrome extension setup:' -ForegroundColor Yellow
Write-Host '1. Open chrome://extensions'
Write-Host '2. Enable Developer mode'
Write-Host "3. Load unpacked: $ExtensionFolder"
Write-Host "4. Confirm extension ID: $ExtensionId"
Write-Host ''
Write-Host 'Run 02_RUN_TRUEWEBSITECLONER.bat after loading the extension.' -ForegroundColor Cyan
