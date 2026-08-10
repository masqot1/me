param([Parameter(Mandatory=$true)][string]$ZipPath)

$ErrorActionPreference = 'Stop'
$ZipPath = [System.IO.Path]::GetFullPath($ZipPath)
if (-not (Test-Path $ZipPath)) { throw "Distribution ZIP not found: $ZipPath" }
$expectedPackage = [System.IO.Path]::GetFileNameWithoutExtension($ZipPath)
$sidecar = "$ZipPath.sha256"
$actualZipHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not (Test-Path $sidecar)) { throw 'Distribution SHA-256 sidecar is missing.' }
$expectedHash = ((Get-Content $sidecar -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($expectedHash -ne $actualZipHash) { throw 'Distribution ZIP SHA-256 does not match sidecar.' }

Add-Type -AssemblyName System.IO.Compression
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("TrueWebsiteCloner-dist-verify-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $stream = [System.IO.File]::OpenRead($ZipPath)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read, $false, [System.Text.Encoding]::UTF8)
        try {
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName.Replace('\','/')
                if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('/') -or $name.Split('/') -contains '..' -or $name.Contains(':')) { throw "Unsafe ZIP entry: $name" }
                if (-not $name.StartsWith("$expectedPackage/", [StringComparison]::Ordinal)) { throw "Entry escaped the distribution root: $name" }
                if ($name.EndsWith('/')) { continue }
                $relative = $name.Substring($expectedPackage.Length + 1)
                $target = [System.IO.Path]::GetFullPath((Join-Path $temp $relative.Replace('/',[System.IO.Path]::DirectorySeparatorChar)))
                $prefix = $temp.TrimEnd('\') + '\'
                if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe extraction target: $relative" }
                New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                $input = $entry.Open()
                try { $output = [System.IO.File]::Create($target); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    $manifestPath = Join-Path $temp 'distribution-manifest.json'
    if (-not (Test-Path $manifestPath)) { throw 'distribution-manifest.json is missing.' }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -Depth 12
    if ($manifest.format -ne 'TrueWebsiteCloner.WindowsDistribution' -or $manifest.version -ne '1.0.0' -or $manifest.platform -ne 'win-x64') { throw 'Distribution manifest identity mismatch.' }
    if ($manifest.extensionId -ne 'ggcmdgdiopplpbcfinamhjdkbhiknfbk') { throw 'Pinned extension ID mismatch.' }

    $declared = @{}
    foreach ($record in $manifest.files) {
        $relative = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative) -or $relative.Replace('\','/').Split('/') -contains '..') { throw "Unsafe manifest path: $relative" }
        if ($declared.ContainsKey($relative.ToLowerInvariant())) { throw "Duplicate manifest path: $relative" }
        $declared[$relative.ToLowerInvariant()] = $true
        $file = Join-Path $temp $relative.Replace('/',[System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $file)) { throw "Manifest file is missing: $relative" }
        $info = Get-Item $file
        if ([int64]$info.Length -ne [int64]$record.byteLength) { throw "Length mismatch: $relative" }
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne ([string]$record.sha256).ToLowerInvariant()) { throw "SHA-256 mismatch: $relative" }
    }
    if ([int]$manifest.fileCount -ne $declared.Count) { throw 'Manifest fileCount mismatch.' }

    $actualPayloadFiles = @(Get-ChildItem $temp -Recurse -File | Where-Object { [System.IO.Path]::GetRelativePath($temp,$_.FullName).Replace('\','/') -ne 'distribution-manifest.json' })
    if ($actualPayloadFiles.Count -ne $declared.Count) { throw 'ZIP contains undeclared or missing payload files.' }

    foreach ($required in @(
        'artifacts/desktop/TrueWebsiteCloner.exe',
        'artifacts/native-host/TrueWebsiteCloner.NativeHost.exe',
        'artifacts/local-runtime/TrueWebsiteCloner.LocalRuntime.exe',
        'artifacts/release-tool/TrueWebsiteCloner.ReleaseTool.exe',
        'artifacts/seal-tool/TrueWebsiteCloner.SealTool.exe',
        'artifacts/bundle-tool/TrueWebsiteCloner.BundleTool.exe',
        'chrome-extension/manifest.json',
        'Install-TrueWebsiteCloner.ps1',
        '01_INSTALL.bat',
        '02_RUN_TRUEWEBSITECLONER.bat',
        '03_UNINSTALL.bat',
        'VERSION'
    )) {
        if (-not $declared.ContainsKey($required.ToLowerInvariant())) { throw "Required distribution file missing: $required" }
    }

    if (Get-ChildItem $temp -Recurse -Filter '*.pdb' -File) { throw 'Debug PDB files must not be shipped in the V1.0 distribution.' }
    if (Get-ChildItem $temp -Recurse -Filter 'bridge-info.json' -File) { throw 'Runtime bridge session data must not be shipped.' }

    $extension = Get-Content (Join-Path $temp 'chrome-extension\manifest.json') -Raw | ConvertFrom-Json
    if ($extension.manifest_version -ne 3) { throw 'Shipped Chrome extension is not Manifest V3.' }
    foreach ($permission in @('nativeMessaging','storage','tabs','debugger')) { if ($extension.permissions -notcontains $permission) { throw "Shipped extension missing permission: $permission" } }
    if ([string]::IsNullOrWhiteSpace([string]$extension.key)) { throw 'Shipped extension does not contain the pinned public key.' }

    $installerText = Get-Content (Join-Path $temp 'Install-TrueWebsiteCloner.ps1') -Raw
    foreach ($requiredText in @('HKCU:\Software\Google\Chrome\NativeMessagingHosts','ggcmdgdiopplpbcfinamhjdkbhiknfbk','Microsoft.WindowsDesktop.App','Microsoft.AspNetCore.App')) {
        if ($installerText -notmatch [regex]::Escape($requiredText)) { throw "Installer control missing: $requiredText" }
    }

    Write-Host "PASS  Distribution archive SHA-256: $actualZipHash" -ForegroundColor Green
    Write-Host "PASS  $($declared.Count) manifest file hash(es) verified"
    Write-Host 'PASS  Required Windows binaries and Chrome extension present'
    Write-Host 'PASS  Installer uses HKCU Native Messaging and checks .NET 10 runtimes'
    Write-Host 'PASS  No PDB or runtime bridge session data shipped'
    Write-Host 'RESULT: V1.0 DISTRIBUTION PASS' -ForegroundColor Green
}
finally {
    try { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force } } catch { }
}
