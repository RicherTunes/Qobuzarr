param(
    [string]$Configuration = "Debug",
    [int]$TimeoutMinutes = 12
)

Write-Host "[QUICK] Fast unit test runner" -ForegroundColor Cyan

# Build once
Write-Host "[BUILD] Building solution ($Configuration)..." -ForegroundColor Yellow
& dotnet build "Qobuzarr.sln" -c $Configuration --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed" -ForegroundColor Red; exit 1 }

# Apply fast-retry knob for tests only
$env:QOBUZ_TEST_FAST_RETRY = "true"

# Run tests with default (fast) runsettings
$resultsDir = Join-Path $PSScriptRoot "TestResults"
if (!(Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }

Write-Host "[TEST] Running unit tests (fast profile, excluding Integration)..." -ForegroundColor Yellow
# Target only fast unit test projects explicitly to avoid Integration project discovery side-effects
$testProjects = @(
  "tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj",
  "tests/QobuzCLI.Tests/QobuzCLI.Tests.csproj",
  "tests/Minimal.Tests/Minimal.Tests.csproj"
)

 $timeoutMs = $TimeoutMinutes * 60 * 1000
 $code = 0
 foreach ($proj in $testProjects) {
   Write-Host "[TEST] -> $proj" -ForegroundColor DarkCyan
   $args = @('test', $proj,
     '-c', $Configuration,
     '--no-build',
     '--settings', (Join-Path $PSScriptRoot 'Default.runsettings'),
     '--results-directory', $resultsDir,
     '--verbosity', 'minimal')
   $argString = ($args -join ' ')
   $proc = Start-Process -FilePath 'dotnet' -ArgumentList $argString -PassThru -NoNewWindow
   $exited = $proc.WaitForExit($timeoutMs)
   if (-not $exited) {
     Write-Host "[ERROR] Test run timed out after $TimeoutMinutes minutes for $proj. Killing process..." -ForegroundColor Red
     try { $proc.Kill() } catch {}
     $code = 1
     break
   }
   if ($proc.ExitCode -ne 0) { $code = $proc.ExitCode; break }
 }

# Summarize
$trx = Get-ChildItem -Path $resultsDir -Filter *.trx -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($trx) {
  try {
    [xml]$xml = Get-Content $trx.FullName
    $c = $xml.TestRun.ResultSummary.Counters
    Write-Host "[SUMMARY] Total=$($c.total) Passed=$($c.passed) Failed=$($c.failed) Skipped=$($c.inconclusive)" -ForegroundColor Cyan
  } catch { Write-Host "[SUMMARY] Unable to parse TRX" -ForegroundColor Yellow }
}

exit $code
