$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$ExtensionId = "ggcmdgdiopplpbcfinamhjdkbhiknfbk"
$HostName = "com.truewebsitecloner.host"

Write-Host "TrueWebsiteCloner v0.5 - development install" -ForegroundColor Cyan

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET SDK was not found. Install .NET 10 SDK first." }
$version = (& dotnet --version).Trim()
if (-not $version.StartsWith("10.")) { throw ".NET 10 SDK is required. Detected: $version" }

if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $Artifacts | Out-Null

& dotnet publish (Join-Path $Root "src\TrueWebsiteCloner.NativeHost\TrueWebsiteCloner.NativeHost.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $Artifacts "native-host")
if ($LASTEXITCODE -ne 0) { throw "Native Host publish failed." }

& dotnet publish (Join-Path $Root "src\TrueWebsiteCloner.Desktop\TrueWebsiteCloner.Desktop.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $Artifacts "desktop")
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }

& dotnet publish (Join-Path $Root "src\TrueWebsiteCloner.TestLab\TrueWebsiteCloner.TestLab.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $Artifacts "testlab")
if ($LASTEXITCODE -ne 0) { throw "Test Lab publish failed." }

& dotnet publish (Join-Path $Root "src\TrueWebsiteCloner.LocalRuntime\TrueWebsiteCloner.LocalRuntime.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $Artifacts "local-runtime")
if ($LASTEXITCODE -ne 0) { throw "Local Runtime publish failed." }

$NativeDir = Join-Path $env:LOCALAPPDATA "TrueWebsiteCloner\native-host"
New-Item -ItemType Directory -Path $NativeDir -Force | Out-Null
$ManifestPath = Join-Path $NativeDir "$HostName.json"
$HostExe = Join-Path $Artifacts "native-host\TrueWebsiteCloner.NativeHost.exe"
$manifest = @{
  name = $HostName
  description = "TrueWebsiteCloner Chrome bridge"
  path = $HostExe
  type = "stdio"
  allowed_origins = @("chrome-extension://$ExtensionId/")
}
[System.IO.File]::WriteAllText($ManifestPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $RegPath -Force | Out-Null
Set-Item -Path $RegPath -Value $ManifestPath

Write-Host ""
Write-Host "PASS: Native Host, Desktop, Test Lab and Local Runtime published" -ForegroundColor Green
Write-Host "PASS: native host registered for current user" -ForegroundColor Green
Write-Host "Extension ID: $ExtensionId"
Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "1. Open chrome://extensions"
Write-Host "2. Enable Developer mode"
Write-Host "3. Load unpacked: $(Join-Path $Root 'chrome-extension')"
Write-Host "4. Run: $(Join-Path $Artifacts 'desktop\TrueWebsiteCloner.exe')"
Write-Host "5. Capture, Build Offline Site, then Start Offline Runtime from the Desktop app"
