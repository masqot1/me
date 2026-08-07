$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root "artifacts\desktop\TrueWebsiteCloner.exe"
if (-not (Test-Path $Exe)) { throw "Desktop build not found. Run Install-Dev.ps1 first." }
Start-Process $Exe
