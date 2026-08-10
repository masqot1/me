param([Parameter(Mandatory=$true)][string]$OutputRoot)
$ErrorActionPreference='Stop'
$Root=Split-Path -Parent $PSScriptRoot
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path $OutputRoot){Remove-Item $OutputRoot -Recurse -Force}
New-Item -ItemType Directory -Path $OutputRoot -Force|Out-Null

$projects=@(
  @{Name='Desktop';Project='src\TrueWebsiteCloner.Desktop\TrueWebsiteCloner.Desktop.csproj';Output='desktop'},
  @{Name='Native Host';Project='src\TrueWebsiteCloner.NativeHost\TrueWebsiteCloner.NativeHost.csproj';Output='native-host'},
  @{Name='Test Lab';Project='src\TrueWebsiteCloner.TestLab\TrueWebsiteCloner.TestLab.csproj';Output='testlab'},
  @{Name='Local Runtime';Project='src\TrueWebsiteCloner.LocalRuntime\TrueWebsiteCloner.LocalRuntime.csproj';Output='local-runtime'},
  @{Name='CLI';Project='src\TrueWebsiteCloner.Cli\TrueWebsiteCloner.Cli.csproj';Output='cli'}
)
foreach($item in $projects){
  $out=Join-Path $OutputRoot $item.Output
  & dotnet publish (Join-Path $Root $item.Project) -c Release -r win-x64 --self-contained true -o $out -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:PublishReadyToRun=false
  if($LASTEXITCODE-ne 0){throw "$($item.Name) standalone publish failed."}
  if(-not (Get-ChildItem $out -Filter '*.exe' -File)){throw "$($item.Name) standalone executable missing."}
  Get-ChildItem $out -Filter '*.pdb' -File -ErrorAction SilentlyContinue|Remove-Item -Force
}
Write-Host 'PASS  Standalone executables published' -ForegroundColor Green
