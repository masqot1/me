$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$HostName = "com.truewebsitecloner.host"
$ExpectedExtensionId = "ggcmdgdiopplpbcfinamhjdkbhiknfbk"
$failures = @()

function Check([bool]$Condition, [string]$Name) {
  if ($Condition) { Write-Host "PASS  $Name" -ForegroundColor Green }
  else { Write-Host "FAIL  $Name" -ForegroundColor Red; $script:failures += $Name }
}

Write-Host "TrueWebsiteCloner Foundation Gate 0.1" -ForegroundColor Cyan
Check (Test-Path (Join-Path $Root "chrome-extension\manifest.json")) "Extension manifest exists"
$manifest = Get-Content (Join-Path $Root "chrome-extension\manifest.json") -Raw | ConvertFrom-Json
Check ($manifest.manifest_version -eq 3) "Manifest V3"
Check ($manifest.permissions -contains "nativeMessaging") "nativeMessaging permission"
Check ((Get-Content (Join-Path $Root "docs\extension-id.txt") -Raw).Trim() -eq $ExpectedExtensionId) "Pinned extension ID"
Check (Test-Path (Join-Path $Root "artifacts\desktop\TrueWebsiteCloner.exe")) "Desktop published"
Check (Test-Path (Join-Path $Root "artifacts\native-host\TrueWebsiteCloner.NativeHost.exe")) "Native Host published"
Check (Test-Path (Join-Path $Root "artifacts\testlab\TrueWebsiteCloner.TestLab.exe")) "Test Lab published"
$reg = Get-ItemProperty -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName" -ErrorAction SilentlyContinue
Check ($null -ne $reg) "Native Host registry key"

if ($failures.Count -gt 0) {
  Write-Host "`nRESULT: FAIL ($($failures.Count) checks)" -ForegroundColor Red
  exit 1
}
Write-Host "`nRESULT: STATIC PASS" -ForegroundColor Green
Write-Host "Runtime gate still requires pressing Test Desktop Connection in Chrome while the desktop app is running." -ForegroundColor Yellow
