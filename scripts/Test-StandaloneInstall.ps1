param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference='Stop'
$ZipPath=[IO.Path]::GetFullPath($ZipPath)
$PackageName=[IO.Path]::GetFileNameWithoutExtension($ZipPath)
$Temp=Join-Path ([IO.Path]::GetTempPath()) ('twc-clean-install-'+[Guid]::NewGuid().ToString('N'))
$Workspace=Join-Path $Temp 'workspace'
$BridgeInfo=Join-Path $env:LOCALAPPDATA 'TrueWebsiteCloner\runtime\bridge-info.json'
$RegPath='HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.truewebsitecloner.host'
$Desktop=$null
$nativeProcess=$null
$oldPath=$env:PATH;$oldRoot=$env:DOTNET_ROOT;$oldRootX64=$env:DOTNET_ROOT_X64;$oldProject=$env:TWC_PROJECT_ROOT
try{
  New-Item -ItemType Directory -Path $Temp,$Workspace -Force|Out-Null
  Expand-Archive $ZipPath -DestinationPath $Temp
  $Root=Join-Path $Temp $PackageName
  & (Join-Path $Root 'Install-TrueWebsiteCloner.ps1')
  if(-not(Test-Path $RegPath)){throw 'Installer did not create HKCU Native Messaging registration.'}
  $manifestPath=(Get-Item $RegPath).GetValue('')
  if(-not(Test-Path $manifestPath)){throw 'Installed Native Messaging manifest path is missing.'}
  $manifest=Get-Content $manifestPath -Raw|ConvertFrom-Json
  $expectedHost=Join-Path $Root 'bin\native-host\TrueWebsiteCloner.NativeHost.exe'
  if([IO.Path]::GetFullPath([string]$manifest.path)-ne[IO.Path]::GetFullPath($expectedHost)){throw 'Native Host manifest points to the wrong executable.'}
  if($manifest.allowed_origins-notcontains'chrome-extension://ggcmdgdiopplpbcfinamhjdkbhiknfbk/'){throw 'Pinned extension origin missing from installed manifest.'}

  # Hide global dotnet to prove the shipped EXEs are self-contained.
  $env:PATH="$env:SystemRoot\System32;$env:SystemRoot";$env:DOTNET_ROOT='Z:\definitely-missing-dotnet';$env:DOTNET_ROOT_X64='Z:\definitely-missing-dotnet';$env:TWC_PROJECT_ROOT=$Workspace
  $cli=Join-Path $Root 'bin\cli\TrueWebsiteCloner.Cli.exe'
  $psi=[Diagnostics.ProcessStartInfo]::new($cli,'version');$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
  $cliProcess=[Diagnostics.Process]::Start($psi);$cliOut=$cliProcess.StandardOutput.ReadToEnd();$cliErr=$cliProcess.StandardError.ReadToEnd();$cliProcess.WaitForExit()
  if($cliProcess.ExitCode-ne 0-or$cliOut-notmatch'TrueWebsiteCloner\.Cli 1\.0\.0'){throw "Self-contained CLI failed without global dotnet. $cliErr"}

  if(Test-Path $BridgeInfo){Remove-Item $BridgeInfo -Force}
  $desktopExe=Join-Path $Root 'bin\desktop\TrueWebsiteCloner.exe'
  $Desktop=Start-Process -FilePath $desktopExe -PassThru
  for($i=0;$i-lt 40-and-not(Test-Path $BridgeInfo);$i++){Start-Sleep -Milliseconds 500}
  if(-not(Test-Path $BridgeInfo)){throw 'Self-contained Desktop did not create bridge-info.json.'}
  $bridge=Get-Content $BridgeInfo -Raw|ConvertFrom-Json
  if([int]$bridge.port-le 0-or[string]::IsNullOrWhiteSpace([string]$bridge.token)){throw 'Desktop bridge info is incomplete.'}

  # Direct Native Messaging framing test against the running Desktop bridge.
  $hostExe=Join-Path $Root 'bin\native-host\TrueWebsiteCloner.NativeHost.exe'
  $hostPsi=[Diagnostics.ProcessStartInfo]::new();$hostPsi.FileName=$hostExe;$hostPsi.ArgumentList.Add('chrome-extension://ggcmdgdiopplpbcfinamhjdkbhiknfbk/');$hostPsi.UseShellExecute=$false;$hostPsi.RedirectStandardInput=$true;$hostPsi.RedirectStandardOutput=$true;$hostPsi.RedirectStandardError=$true
  $nativeProcess=[Diagnostics.Process]::Start($hostPsi)
  $json='{"type":"foundation.ping","data":{"extensionId":"ggcmdgdiopplpbcfinamhjdkbhiknfbk","version":"1.0.0"}}';$body=[Text.Encoding]::UTF8.GetBytes($json);$header=[BitConverter]::GetBytes([int]$body.Length)
  $nativeProcess.StandardInput.BaseStream.Write($header,0,4);$nativeProcess.StandardInput.BaseStream.Write($body,0,$body.Length);$nativeProcess.StandardInput.BaseStream.Flush()
  $replyHeader=New-Object byte[] 4;$read=$nativeProcess.StandardOutput.BaseStream.Read($replyHeader,0,4);if($read-ne 4){throw 'Native Host did not return a framed reply.'};$replyLength=[BitConverter]::ToInt32($replyHeader,0);if($replyLength-le 0-or$replyLength-gt 1048576){throw 'Native Host reply length is invalid.'};$replyBody=New-Object byte[] $replyLength;$offset=0;while($offset-lt$replyLength){$n=$nativeProcess.StandardOutput.BaseStream.Read($replyBody,$offset,$replyLength-$offset);if($n-le 0){throw 'Native Host reply ended early.'};$offset+=$n};$nativeProcess.StandardInput.Close();$replyText=[Text.Encoding]::UTF8.GetString($replyBody);$reply=$replyText|ConvertFrom-Json
  if(-not$reply.ok){throw "Native Host/Desktop bridge returned failure: $replyText"}
  $nativeProcess.WaitForExit(5000)|Out-Null;if(-not$nativeProcess.HasExited){$nativeProcess.Kill()}

  $env:PATH=$oldPath;$env:DOTNET_ROOT=$oldRoot;$env:DOTNET_ROOT_X64=$oldRootX64
  & (Join-Path $Root 'Uninstall-TrueWebsiteCloner.ps1')
  if(Test-Path $RegPath){throw 'Uninstaller left the Native Messaging registry key behind.'}
  if(-not(Test-Path $Workspace)){throw 'Uninstaller deleted the project workspace.'}
  Write-Host 'PASS  standalone installer registered HKCU Native Messaging' -ForegroundColor Green
  Write-Host 'PASS  CLI and Desktop launched with global dotnet hidden'
  Write-Host 'PASS  NativeHost -> Desktop framed bridge reply'
  Write-Host 'PASS  uninstaller removed registration without deleting workspace'
  Write-Host 'RESULT: V1.0 STANDALONE CLEAN INSTALL PASS' -ForegroundColor Green
}
finally{
  if($nativeProcess-and-not$nativeProcess.HasExited){try{$nativeProcess.Kill()}catch{}}
  if($Desktop-and-not$Desktop.HasExited){try{$Desktop.Kill($true)}catch{}}
  $env:PATH=$oldPath;$env:DOTNET_ROOT=$oldRoot;$env:DOTNET_ROOT_X64=$oldRootX64;$env:TWC_PROJECT_ROOT=$oldProject
  try{if(Test-Path $RegPath){Remove-Item $RegPath -Recurse -Force}}catch{}
  try{if(Test-Path $Temp){Remove-Item $Temp -Recurse -Force}}catch{}
}
