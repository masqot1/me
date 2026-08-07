param(
  [Parameter(Mandatory = $true)][string]$CaptureRoot,
  [string]$SourceBase = 'http://127.0.0.1:7843',
  [string]$ReplayBase = 'http://127.0.0.1:7852'
)

$ErrorActionPreference = 'Stop'

function Assert-LoopbackUrl([string]$Value, [string]$Name) {
  $uri = [Uri]$Value
  if ($uri.Scheme -notin @('http','https')) { throw "$Name must use HTTP/HTTPS." }
  if ($uri.Host -notin @('127.0.0.1','localhost','::1')) { throw "$Name must be loopback-only in Gate 0.6." }
  return $uri
}

function Normalize-Mime([string]$Value) {
  if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
  return ($Value.Split(';')[0]).Trim().ToLowerInvariant()
}

function Text-Sha256([string]$Value) {
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
  return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Canonical-Json([string]$Value) {
  $obj = $Value | ConvertFrom-Json -Depth 50
  return ($obj | ConvertTo-Json -Compress -Depth 50)
}

$sourceUri = Assert-LoopbackUrl $SourceBase 'SourceBase'
$replayUri = Assert-LoopbackUrl $ReplayBase 'ReplayBase'
$CaptureRoot = [IO.Path]::GetFullPath($CaptureRoot)
$manifestPath = Join-Path $CaptureRoot 'offline\offline-manifest.json'
$missingPath = Join-Path $CaptureRoot 'offline\missing-resources.json'
$reportPath = Join-Path $CaptureRoot 'offline\verification-report.json'
if (-not (Test-Path $manifestPath)) { throw "Offline manifest missing: $manifestPath" }

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -Depth 50
$routes = @()
$seen = @{}

foreach ($mapping in $manifest.mappings) {
  $original = [Uri]$mapping.url
  $key = $original.AbsoluteUri.Split('#')[0]
  if ($seen.ContainsKey($key)) { continue }
  $seen[$key] = $true

  $pathAndQuery = $original.PathAndQuery
  $sourceUrl = $SourceBase.TrimEnd('/') + $pathAndQuery
  $replayUrl = $ReplayBase.TrimEnd('/') + $pathAndQuery
  $comparison = 'exact'
  $note = 'Response text is identical.'
  $sourceStatus = 0
  $replayStatus = 0
  $sourceType = ''
  $replayType = ''
  $sourceHash = ''
  $replayHash = ''

  try {
    $sourceResponse = Invoke-WebRequest $sourceUrl -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 8
    $replayResponse = Invoke-WebRequest $replayUrl -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 8
    $sourceStatus = [int]$sourceResponse.StatusCode
    $replayStatus = [int]$replayResponse.StatusCode
    $sourceType = Normalize-Mime ([string]$sourceResponse.Headers['Content-Type'])
    $replayType = Normalize-Mime ([string]$replayResponse.Headers['Content-Type'])
    $sourceText = [string]$sourceResponse.Content
    $replayText = [string]$replayResponse.Content
    $sourceHash = Text-Sha256 $sourceText
    $replayHash = Text-Sha256 $replayText
    $capturedType = Normalize-Mime ([string]$mapping.mimeType)

    if ($sourceStatus -ne $replayStatus) {
      $comparison = 'divergent'; $note = 'HTTP status differs.'
    }
    elseif ([string]$replayResponse.Headers['X-TrueWebsiteCloner-Replay'] -ne 'offline') {
      $comparison = 'divergent'; $note = 'Offline replay marker header missing.'
    }
    elseif ($sourceType -ne $capturedType -or $replayType -ne $capturedType) {
      $comparison = 'divergent'; $note = 'Content-Type differs from captured MIME type.'
    }
    elseif ($capturedType -in @('application/json','application/ld+json')) {
      try {
        if ((Canonical-Json $sourceText) -eq (Canonical-Json $replayText)) {
          if ($sourceHash -eq $replayHash) { $comparison = 'exact'; $note = 'JSON is byte-identical.' }
          else { $comparison = 'json-equivalent'; $note = 'JSON values are equivalent.' }
        }
        else { $comparison = 'divergent'; $note = 'JSON value differs.' }
      }
      catch { $comparison = 'divergent'; $note = 'JSON parsing failed during comparison.' }
    }
    elseif ($sourceHash -ne $replayHash) {
      if ($capturedType -in @('text/html','text/css')) {
        $comparison = 'expected-rewrite'; $note = 'HTML/CSS differs because V0.4 rewrites resource paths.'
      }
      else { $comparison = 'divergent'; $note = 'Response content differs.' }
    }
  }
  catch {
    $comparison = 'divergent'
    $note = 'Verification request failed: ' + $_.Exception.Message
  }

  $routes += [ordered]@{
    originalUrl = [string]$mapping.url
    requestPath = $pathAndQuery
    mimeType = [string]$mapping.mimeType
    resourceType = [string]$mapping.resourceType
    sourceStatus = $sourceStatus
    replayStatus = $replayStatus
    sourceContentType = $sourceType
    replayContentType = $replayType
    comparison = $comparison
    sourceSha256 = $sourceHash
    replaySha256 = $replayHash
    note = $note
  }
}

$missingCount = 0
if (Test-Path $missingPath) {
  $missingData = Get-Content $missingPath -Raw | ConvertFrom-Json -Depth 20
  if ($null -ne $missingData) { $missingCount = @($missingData).Count }
}
$exact = @($routes | Where-Object comparison -eq 'exact').Count
$jsonEquivalent = @($routes | Where-Object comparison -eq 'json-equivalent').Count
$expectedRewrites = @($routes | Where-Object comparison -eq 'expected-rewrite').Count
$divergent = @($routes | Where-Object comparison -eq 'divergent').Count
$ok = $routes.Count -gt 0 -and $divergent -eq 0

$report = [ordered]@{
  version = '0.6.0'
  mode = 'loopback-testlab-vs-offline-replay'
  result = if ($ok) { 'PASS' } else { 'FAIL' }
  sourceBase = $sourceUri.AbsoluteUri
  replayBase = $replayUri.AbsoluteUri
  routeCount = $routes.Count
  exactMatches = $exact
  jsonEquivalentMatches = $jsonEquivalent
  expectedRewrites = $expectedRewrites
  unexpectedDivergences = $divergent
  reportedMissingReferences = $missingCount
  policy = [ordered]@{ loopbackOnly = $true; cookiesSent = $false; authorizationSent = $false; redirectsFollowed = $false }
  routes = $routes
}
$report | ConvertTo-Json -Depth 20 | Set-Content $reportPath -Encoding UTF8

Write-Host "Routes: $($routes.Count)"
Write-Host "Exact: $exact"
Write-Host "JSON equivalent: $jsonEquivalent"
Write-Host "Expected rewrites: $expectedRewrites"
Write-Host "Unexpected divergences: $divergent"
Write-Host "Report: $reportPath"
if (-not $ok) { throw "Gate 0.6 verification found $divergent unexpected divergence(s)." }
Write-Host 'RESULT: GATE 0.6 VERIFICATION PASS' -ForegroundColor Green
