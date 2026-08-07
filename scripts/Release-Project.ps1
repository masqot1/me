param(
  [Parameter(Mandatory=$true)][string]$Project,
  [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
$Project=[System.IO.Path]::GetFullPath($Project)
$Output=[System.IO.Path]::GetFullPath($Output)
$ReleaseTool=Join-Path $Root 'artifacts\release-tool\TrueWebsiteCloner.ReleaseTool.exe'
$SealTool=Join-Path $Root 'artifacts\seal-tool\TrueWebsiteCloner.SealTool.exe'
$BundleTool=Join-Path $Root 'artifacts\bundle-tool\TrueWebsiteCloner.BundleTool.exe'
foreach($tool in @($ReleaseTool,$SealTool,$BundleTool)){if(-not(Test-Path $tool)){throw "Required release tool is missing: $tool. Run Install-Dev.ps1 first."}}
Write-Host '1/5 Validate release readiness' -ForegroundColor Cyan
& $ReleaseTool --project $Project
if($LASTEXITCODE-ne 0){throw 'Project is not release-ready.'}
$SealPath=Join-Path $Project '_release\release-seal.json'
if(Test-Path $SealPath){Write-Host '2/5 Existing seal found; verify immutable seal' -ForegroundColor Cyan;& $SealTool verify --project $Project}else{Write-Host '2/5 Create immutable release seal' -ForegroundColor Cyan;& $SealTool create --project $Project}
if($LASTEXITCODE-ne 0){throw 'Release seal step failed.'}
Write-Host '3/5 Verify release seal' -ForegroundColor Cyan
& $SealTool verify --project $Project
if($LASTEXITCODE-ne 0){throw 'Release seal verification failed.'}
Write-Host '4/5 Create deterministic .twcrelease bundle' -ForegroundColor Cyan
& $BundleTool create --project $Project --output $Output
if($LASTEXITCODE-ne 0){throw 'Release bundle creation failed.'}
Write-Host '5/5 Verify complete release bundle chain' -ForegroundColor Cyan
& $BundleTool verify --bundle $Output
if($LASTEXITCODE-ne 0){throw 'Release bundle verification failed.'}
Write-Host "RESULT: RELEASE PASS`n$Output" -ForegroundColor Green
