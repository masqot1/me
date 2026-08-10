param([Parameter(Mandatory=$true)][string]$StandaloneArtifacts,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
$StandaloneArtifacts=[IO.Path]::GetFullPath($StandaloneArtifacts)
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$PackageName='TrueWebsiteCloner-1.0.0-win-x64-standalone'
$Stage=Join-Path $OutputDirectory $PackageName
$ZipPath=Join-Path $OutputDirectory "$PackageName.zip"
if(Test-Path $OutputDirectory){Remove-Item $OutputDirectory -Recurse -Force}
New-Item -ItemType Directory -Path $Stage -Force|Out-Null

function CopyTree($src,$dst){Get-ChildItem $src -Recurse -File|Sort-Object FullName|ForEach-Object{$rel=[IO.Path]::GetRelativePath($src,$_.FullName);$target=Join-Path $dst $rel;New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null;Copy-Item $_.FullName $target -Force}}
CopyTree $StandaloneArtifacts (Join-Path $Stage 'bin')
CopyTree (Join-Path $Root 'chrome-extension') (Join-Path $Stage 'chrome-extension')
foreach($file in @('Install-TrueWebsiteCloner.ps1','01_INSTALL.bat','02_RUN_TRUEWEBSITECLONER.bat','03_UNINSTALL.bat','README.txt')){Copy-Item (Join-Path $Root "distribution-standalone\$file") (Join-Path $Stage $file) -Force}
Copy-Item (Join-Path $Root 'VERSION') (Join-Path $Stage 'VERSION') -Force

$records=@()
Get-ChildItem $Stage -Recurse -File|Sort-Object{[IO.Path]::GetRelativePath($Stage,$_.FullName).Replace('\','/')}|ForEach-Object{$rel=[IO.Path]::GetRelativePath($Stage,$_.FullName).Replace('\','/');if($rel-ne'distribution-manifest.json'){$records+=[ordered]@{path=$rel;byteLength=[int64]$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}}
$manifest=[ordered]@{format='TrueWebsiteCloner.StandaloneWindowsDistribution';version='1.0.0';platform='win-x64';selfContained=$true;requiresInstalledDotNet=$false;extensionId='ggcmdgdiopplpbcfinamhjdkbhiknfbk';fileCount=$records.Count;files=$records}
[IO.File]::WriteAllText((Join-Path $Stage 'distribution-manifest.json'),($manifest|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression
$fixed=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
$stream=[IO.File]::Open($ZipPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true,[Text.Encoding]::UTF8);try{foreach($f in Get-ChildItem $Stage -Recurse -File|Sort-Object{[IO.Path]::GetRelativePath($Stage,$_.FullName).Replace('\','/')}){$rel=[IO.Path]::GetRelativePath($Stage,$f.FullName).Replace('\','/');$entry=$zip.CreateEntry("$PackageName/$rel",[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=$fixed;$entry.ExternalAttributes=0;$input=[IO.File]::OpenRead($f.FullName);try{$output=$entry.Open();try{$input.CopyTo($output)}finally{$output.Dispose()}}finally{$input.Dispose()}}}finally{$zip.Dispose()}}finally{$stream.Dispose()}
$hash=(Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant();[IO.File]::WriteAllText("$ZipPath.sha256","$hash  $PackageName.zip`r`n",[Text.UTF8Encoding]::new($false));Write-Host "PASS  Standalone distribution: $ZipPath" -ForegroundColor Green;Write-Host "SHA-256: $hash"
