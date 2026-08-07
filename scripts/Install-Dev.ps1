$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$ExtensionId = "ggcmdgdiopplpbcfinamhjdkbhiknfbk"
$HostName = "com.truewebsitecloner.host"

Write-Host "TrueWebsiteCloner v0.10 - development install" -ForegroundColor Cyan
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET SDK was not found. Install .NET 10 SDK first." }
$version = (& dotnet --version).Trim()
if (-not $version.StartsWith("10.")) { throw ".NET 10 SDK is required. Detected: $version" }
if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $Artifacts | Out-Null

$projects = @(
  @{ Name='Native Host'; Project='src\TrueWebsiteCloner.NativeHost\TrueWebsiteCloner.NativeHost.csproj'; Output='native-host' },
  @{ Name='Desktop'; Project='src\TrueWebsiteCloner.Desktop\TrueWebsiteCloner.Desktop.csproj'; Output='desktop' },
  @{ Name='Test Lab'; Project='src\TrueWebsiteCloner.TestLab\TrueWebsiteCloner.TestLab.csproj'; Output='testlab' },
  @{ Name='Local Runtime'; Project='src\TrueWebsiteCloner.LocalRuntime\TrueWebsiteCloner.LocalRuntime.csproj'; Output='local-runtime' },
  @{ Name='Offline Tool'; Project='src\TrueWebsiteCloner.OfflineTool\TrueWebsiteCloner.OfflineTool.csproj'; Output='offline-tool' },
  @{ Name='Recovery Tool'; Project='src\TrueWebsiteCloner.RecoveryTool\TrueWebsiteCloner.RecoveryTool.csproj'; Output='recovery-tool' },
  @{ Name='Graph Tool'; Project='src\TrueWebsiteCloner.GraphTool\TrueWebsiteCloner.GraphTool.csproj'; Output='graph-tool' },
  @{ Name='Snapshot Tool'; Project='src\TrueWebsiteCloner.SnapshotTool\TrueWebsiteCloner.SnapshotTool.csproj'; Output='snapshot-tool' }
)
foreach ($item in $projects) {
  & dotnet publish (Join-Path $Root $item.Project) -c Release -r win-x64 --self-contained false -o (Join-Path $Artifacts $item.Output)
  if ($LASTEXITCODE -ne 0) { throw "$($item.Name) publish failed." }
}

$NativeDir = Join-Path $env:LOCALAPPDATA "TrueWebsiteCloner\native-host"
New-Item -ItemType Directory -Path $NativeDir -Force | Out-Null
$ManifestPath = Join-Path $NativeDir "$HostName.json"
$HostExe = Join-Path $Artifacts "native-host\TrueWebsiteCloner.NativeHost.exe"
$manifest = @{ name=$HostName; description='TrueWebsiteCloner Chrome bridge'; path=$HostExe; type='stdio'; allowed_origins=@("chrome-extension://$ExtensionId/") }
[System.IO.File]::WriteAllText($ManifestPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
$RegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $RegPath -Force | Out-Null
Set-Item -Path $RegPath -Value $ManifestPath
Write-Host "PASS: development binaries published" -ForegroundColor Green
Write-Host "PASS: native host registered for current user" -ForegroundColor Green
