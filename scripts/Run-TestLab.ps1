$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root "artifacts\testlab\TrueWebsiteCloner.TestLab.exe"
if (-not (Test-Path $Exe)) { throw "Test Lab build not found. Run Install-Dev.ps1 first." }
& $Exe
