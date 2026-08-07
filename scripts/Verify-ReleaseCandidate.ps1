param([string]$OutputDirectory = "")
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $Root 'release-candidate-output'}
$OutputDirectory=[System.IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $OutputDirectory){Remove-Item $OutputDirectory -Recurse -Force}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$results=@()
function Add-Result([string]$Name,[string]$Status,[string]$Detail){$script:results += [ordered]@{name=$Name;status=$Status;detail=$Detail}}
function Run-Step([string]$Name,[scriptblock]$Action){Write-Host "`n=== $Name ===" -ForegroundColor Cyan;try{& $Action;Add-Result $Name 'PASS' 'Completed successfully.';Write-Host "PASS  $Name" -ForegroundColor Green}catch{Add-Result $Name 'FAIL' $_.Exception.Message;Write-Host "FAIL  $Name : $($_.Exception.Message)" -ForegroundColor Red;throw}}

Run-Step 'Foundation static gate' { & (Join-Path $Root 'tests\Test-Foundation.ps1'); if($LASTEXITCODE-ne 0){throw "Foundation static gate exited $LASTEXITCODE"} }
Run-Step 'Extension manifest and JavaScript syntax' {
  $manifest=Get-Content (Join-Path $Root 'chrome-extension\manifest.json') -Raw|ConvertFrom-Json
  if($manifest.manifest_version-ne 3){throw 'Extension is not Manifest V3'}
  foreach($permission in @('nativeMessaging','storage','tabs','debugger')){if($manifest.permissions-notcontains $permission){throw "Missing extension permission: $permission"}}
  foreach($js in Get-ChildItem (Join-Path $Root 'chrome-extension') -Filter '*.js' -File){& node --check $js.FullName;if($LASTEXITCODE-ne 0){throw "JavaScript syntax failed: $($js.Name)"}}
}

$gateProjects=Get-ChildItem (Join-Path $Root 'tests') -Recurse -Filter '*.csproj' -File | Where-Object { $_.BaseName -like '*GateTests' } | Sort-Object FullName
if($gateProjects.Count-eq 0){throw 'No *GateTests projects were discovered.'}
foreach($project in $gateProjects){
  $relative=[System.IO.Path]::GetRelativePath($Root,$project.FullName)
  Run-Step "Gate test: $($project.BaseName)" { & dotnet run --project $project.FullName -c Release; if($LASTEXITCODE-ne 0){throw "$relative exited $LASTEXITCODE"} }
}

$failed=@($results|Where-Object{$_.status-ne 'PASS'}).Count
$report=[ordered]@{
  format='TrueWebsiteCloner.ReleaseCandidateReport'
  version='1.0.0-rc1'
  result=if($failed-eq 0){'PASS'}else{'FAIL'}
  gateProjectCount=$gateProjects.Count
  checkCount=$results.Count
  failedCount=$failed
  checks=$results
}
$reportPath=Join-Path $OutputDirectory 'release-candidate-report.json'
[System.IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 10),[System.Text.UTF8Encoding]::new($false))
if($failed-ne 0){throw "Release Candidate failed $failed check(s)."}
Write-Host "`nRESULT: V1.0-RC1 PASS" -ForegroundColor Green
Write-Host "Report: $reportPath"
