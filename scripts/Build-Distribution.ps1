param(
    [string]$OutputDirectory = '',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $Root 'distribution-output' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$Artifacts = Join-Path $Root 'artifacts'
$PackageName = "TrueWebsiteCloner-$Version-win-x64"
$Stage = Join-Path $OutputDirectory $PackageName
$ZipPath = Join-Path $OutputDirectory "$PackageName.zip"

$requiredArtifacts = @(
    'desktop\TrueWebsiteCloner.exe',
    'native-host\TrueWebsiteCloner.NativeHost.exe',
    'testlab\TrueWebsiteCloner.TestLab.exe',
    'local-runtime\TrueWebsiteCloner.LocalRuntime.exe',
    'offline-tool\TrueWebsiteCloner.OfflineTool.exe',
    'recovery-tool\TrueWebsiteCloner.RecoveryTool.exe',
    'graph-tool\TrueWebsiteCloner.GraphTool.exe',
    'snapshot-tool\TrueWebsiteCloner.SnapshotTool.exe',
    'portable-tool\TrueWebsiteCloner.PortableTool.exe',
    'release-tool\TrueWebsiteCloner.ReleaseTool.exe',
    'seal-tool\TrueWebsiteCloner.SealTool.exe',
    'bundle-tool\TrueWebsiteCloner.BundleTool.exe'
)
foreach ($relative in $requiredArtifacts) {
    if (-not (Test-Path (Join-Path $Artifacts $relative))) { throw "Required published artifact is missing: $relative. Run scripts\Install-Dev.ps1 first." }
}

if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

function Copy-FilteredTree([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) { throw "Source folder missing: $Source" }
    Get-ChildItem $Source -Recurse -File | Sort-Object FullName | ForEach-Object {
        if ($_.Extension -ieq '.pdb') { return }
        $relative = [System.IO.Path]::GetRelativePath($Source, $_.FullName)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $_.FullName $target -Force
    }
}

Copy-FilteredTree $Artifacts (Join-Path $Stage 'artifacts')
Copy-FilteredTree (Join-Path $Root 'chrome-extension') (Join-Path $Stage 'chrome-extension')

$scriptDir = Join-Path $Stage 'scripts'
New-Item -ItemType Directory -Path $scriptDir -Force | Out-Null
Copy-Item (Join-Path $Root 'scripts\Release-Project.ps1') (Join-Path $scriptDir 'Release-Project.ps1') -Force

$docsDir = Join-Path $Stage 'docs'
New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
foreach ($doc in @('ARCHITECTURE.md','V1.0-RELEASE.md','GATE-0.14.md','GATE-0.15.md','GATE-0.16.md','GATE-0.17.md','extension-id.txt')) {
    $source = Join-Path $Root "docs\$doc"
    if (Test-Path $source) { Copy-Item $source (Join-Path $docsDir $doc) -Force }
}

foreach ($file in @('Install-TrueWebsiteCloner.ps1','Uninstall-TrueWebsiteCloner.ps1','01_INSTALL.bat','02_RUN_TRUEWEBSITECLONER.bat','03_UNINSTALL.bat','README.txt')) {
    Copy-Item (Join-Path $Root "distribution\$file") (Join-Path $Stage $file) -Force
}
Copy-Item (Join-Path $Root 'VERSION') (Join-Path $Stage 'VERSION') -Force

$files = @(Get-ChildItem $Stage -Recurse -File | Sort-Object { [System.IO.Path]::GetRelativePath($Stage, $_.FullName).Replace('\','/') })
$records = @()
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($Stage, $file.FullName).Replace('\','/')
    if ($relative -eq 'distribution-manifest.json') { continue }
    $records += [ordered]@{
        path = $relative
        byteLength = [int64]$file.Length
        sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    format = 'TrueWebsiteCloner.WindowsDistribution'
    version = $Version
    platform = 'win-x64'
    frameworkDependent = $true
    requiredDotNetMajor = 10
    extensionId = 'ggcmdgdiopplpbcfinamhjdkbhiknfbk'
    fileCount = $records.Count
    files = $records
}
[System.IO.File]::WriteAllText((Join-Path $Stage 'distribution-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
$fixedTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
$zipStream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true, [System.Text.Encoding]::UTF8)
    try {
        foreach ($file in Get-ChildItem $Stage -Recurse -File | Sort-Object { [System.IO.Path]::GetRelativePath($Stage, $_.FullName).Replace('\','/') }) {
            $relative = [System.IO.Path]::GetRelativePath($Stage, $file.FullName).Replace('\','/')
            $entryName = "$PackageName/$relative"
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = $fixedTime
            $entry.ExternalAttributes = 0
            $input = [System.IO.File]::OpenRead($file.FullName)
            try { $output = $entry.Open(); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

$zipHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$ZipPath.sha256", "$zipHash  $PackageName.zip`r`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "PASS  Distribution built: $ZipPath" -ForegroundColor Green
Write-Host "SHA-256: $zipHash"
Write-Output $ZipPath
