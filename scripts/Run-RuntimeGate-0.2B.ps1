$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ExtensionId = 'ggcmdgdiopplpbcfinamhjdkbhiknfbk'
$GateRoot = Join-Path $Root 'runtime-gate-output\0.2B'
$ChromeProfile = Join-Path $Root '.runtime\chrome-gate-0.2B'
$ReportPath = Join-Path $GateRoot 'runtime-gate-0.2B-report.json'
$Desktop = $null
$TestLab = $null

function Find-Chrome {
  $candidates = @(
    (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe')
  ) | Where-Object { $_ -and (Test-Path $_) }
  return $candidates | Select-Object -First 1
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds, [string]$Description) {
  $deadline = (Get-Date).AddSeconds($Seconds)
  while ((Get-Date) -lt $deadline) {
    if (& $Condition) { return $true }
    Start-Sleep -Milliseconds 500
  }
  throw "Timeout waiting for $Description"
}

function Stop-GateChrome {
  try {
    $escaped = [regex]::Escape($ChromeProfile)
    Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" -ErrorAction SilentlyContinue |
      Where-Object { $_.CommandLine -and $_.CommandLine -match $escaped } |
      ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  } catch { }
}

try {
  Write-Host 'TrueWebsiteCloner Gate 0.2B - real Chrome runtime test' -ForegroundColor Cyan
  if (Test-Path $GateRoot) { Remove-Item $GateRoot -Recurse -Force }
  if (Test-Path $ChromeProfile) { Remove-Item $ChromeProfile -Recurse -Force }
  New-Item -ItemType Directory -Path $GateRoot -Force | Out-Null
  New-Item -ItemType Directory -Path (Split-Path $ChromeProfile -Parent) -Force | Out-Null

  & (Join-Path $PSScriptRoot 'Install-Dev.ps1')
  if ($LASTEXITCODE -ne 0) { throw 'Install-Dev.ps1 failed.' }

  $chrome = Find-Chrome
  if (-not $chrome) { throw 'Google Chrome was not found.' }
  Write-Host "Chrome: $chrome"

  $env:TWC_PROJECT_ROOT = $GateRoot
  $testLabExe = Join-Path $Root 'artifacts\testlab\TrueWebsiteCloner.TestLab.exe'
  $desktopExe = Join-Path $Root 'artifacts\desktop\TrueWebsiteCloner.exe'
  $extensionDir = Join-Path $Root 'chrome-extension'

  $TestLab = Start-Process -FilePath $testLabExe -PassThru -WindowStyle Hidden
  Wait-Until { try { (Invoke-RestMethod 'http://127.0.0.1:7843/health' -TimeoutSec 2).ok } catch { $false } } 15 'Test Lab /health' | Out-Null
  Write-Host 'PASS  Test Lab ready' -ForegroundColor Green

  $Desktop = Start-Process -FilePath $desktopExe -PassThru
  $bridgeInfo = Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\runtime\bridge-info.json'
  Wait-Until { Test-Path $bridgeInfo } 15 'Desktop bridge' | Out-Null
  Write-Host 'PASS  Desktop bridge ready' -ForegroundColor Green

  $testUrl = 'http://127.0.0.1:7843/?gate=0.2B'
  $gateUrl = "chrome-extension://$ExtensionId/runtime-gate.html"
  $args = @(
    "--user-data-dir=$ChromeProfile",
    '--no-first-run',
    '--no-default-browser-check',
    "--disable-extensions-except=$extensionDir",
    "--load-extension=$extensionDir",
    $testUrl,
    $gateUrl
  )
  Start-Process -FilePath $chrome -ArgumentList $args | Out-Null
  Write-Host 'Chrome launched with isolated Gate profile.'

  $summary = $null
  Wait-Until {
    $script:summary = Get-ChildItem -Path $GateRoot -Filter 'summary.json' -Recurse -File -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $null -ne $script:summary
  } 45 'capture summary from Chrome runtime gate' | Out-Null

  $summaryPath = $summary.FullName
  $networkDir = Split-Path $summaryPath -Parent
  $networkLog = Join-Path $networkDir 'network.jsonl'
  $sessionPath = Join-Path $networkDir 'session.json'
  if (-not (Test-Path $networkLog)) { throw 'network.jsonl was not created.' }
  if (-not (Test-Path $sessionPath)) { throw 'session.json was not created.' }

  $summaryJson = Get-Content $summaryPath -Raw | ConvertFrom-Json
  $networkText = Get-Content $networkLog -Raw
  $lineCount = (Get-Content $networkLog).Count

  if ([int]$summaryJson.eventCount -lt 6) { throw "Too few captured metadata events: $($summaryJson.eventCount)" }
  if ($lineCount -lt 6) { throw "Too few network.jsonl records: $lineCount" }
  if ($networkText -notmatch '/api/sample') { throw 'The Test Lab /api/sample request was not captured.' }
  foreach ($forbidden in @('Authorization','Set-Cookie','Bearer SECRET','SECRET-BODY','postData')) {
    if ($networkText -match [regex]::Escape($forbidden)) { throw "Forbidden sensitive field leaked: $forbidden" }
  }

  $report = [ordered]@{
    gate = '0.2B'
    result = 'PASS'
    chrome = $chrome
    eventCount = [int]$summaryJson.eventCount
    networkRecords = $lineCount
    apiSampleCaptured = $true
    sensitiveFieldsSaved = $false
    captureDirectory = Split-Path $networkDir -Parent
    completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  }
  $report | ConvertTo-Json -Depth 5 | Set-Content $ReportPath -Encoding UTF8

  Write-Host "PASS  Real Chrome metadata capture ($lineCount records)" -ForegroundColor Green
  Write-Host 'PASS  Test Lab API request captured' -ForegroundColor Green
  Write-Host 'PASS  Sensitive-field whitelist' -ForegroundColor Green
  Write-Host "PASS  Report: $ReportPath" -ForegroundColor Green
  Write-Host 'RESULT: GATE 0.2B PASS' -ForegroundColor Green
  exit 0
}
catch {
  $message = $_.Exception.Message
  New-Item -ItemType Directory -Path $GateRoot -Force | Out-Null
  [ordered]@{
    gate = '0.2B'
    result = 'FAIL'
    error = $message
    completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  } | ConvertTo-Json -Depth 5 | Set-Content $ReportPath -Encoding UTF8
  Write-Host "FAIL  $message" -ForegroundColor Red
  Write-Host "Report: $ReportPath" -ForegroundColor Yellow
  exit 1
}
finally {
  Stop-GateChrome
  if ($Desktop -and -not $Desktop.HasExited) { Stop-Process -Id $Desktop.Id -Force -ErrorAction SilentlyContinue }
  if ($TestLab -and -not $TestLab.HasExited) { Stop-Process -Id $TestLab.Id -Force -ErrorAction SilentlyContinue }
  Remove-Item Env:TWC_PROJECT_ROOT -ErrorAction SilentlyContinue
}
