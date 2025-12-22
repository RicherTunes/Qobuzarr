<#
.SYNOPSIS
    Validates trait hygiene for integration tests.

.DESCRIPTION
    This script ensures trait hygiene: tests that require a live Lidarr environment must be tagged
    with Category=Integration so CI can filter appropriately.

    Rules enforced (tests/Integration/*.cs):
    - If a test file contains [SkippableFact]/[SkippableTheory], it must contain Trait("Category","Integration")
    - If a test file references live prerequisites (LIDARR_URL, Framework, etc.), it must contain Trait("Category","Integration")

.EXAMPLE
    .\scripts\lint-test-traits.ps1
    
.NOTES
    Exit code 0 = all tests properly tagged
    Exit code 1 = missing Integration trait detected
#>

param(
    [switch]$Fix  # Future: could auto-fix missing traits
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$integrationTestPath = Join-Path $repoRoot "tests\Integration"

Write-Host "Checking trait hygiene in $integrationTestPath..." -ForegroundColor Cyan

$issues = @()

# Find all .cs files in tests/Integration
$testFiles = Get-ChildItem -Path $integrationTestPath -Filter "*.cs" -File

foreach ($file in $testFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $lines = Get-Content -Path $file.FullName

    # Ignore XML doc comments which can contain example attributes like [SkippableFact]
    $contentWithoutXmlDocs = $content -replace '(?m)^\s*///.*$', ''

    $hasAnyTestAttribute = $contentWithoutXmlDocs -match '\[(Fact|Theory|SkippableFact|SkippableTheory)\b'
    if (-not $hasAnyTestAttribute) {
        continue
    }

    $hasIntegrationTrait = $contentWithoutXmlDocs -match 'Trait\("Category",\s*"Integration"\)'
    $usesSkippable = $contentWithoutXmlDocs -match '\[(SkippableFact|SkippableTheory)\b'
    $referencesLivePrereq = $contentWithoutXmlDocs -match '(LIDARR_URL|LIDARR_API_KEY|ENABLE_LIVE_INTEGRATION_TESTS|LiveLidarrIntegrationFramework|IntegrationTestBase|SkipIfNotReady|Framework\b)'

    if (($usesSkippable -or $referencesLivePrereq) -and -not $hasIntegrationTrait) {
        $issues += [PSCustomObject]@{
            File = $file.Name
            Line = 1
            Method = "(file-level)"
        }
        continue
    }

    # Find all SkippableFact occurrences and check if they have Integration trait
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # Skip XML documentation comments
        if ($lines[$i] -match '^\s*///' ) {
            continue
        }
        if ($lines[$i] -match '\[(SkippableFact|SkippableTheory)\]') {
            # Look at the next few lines for traits
            $hasIntegration = $false
            $methodName = ""

            for ($j = $i; $j -lt [Math]::Min($i + 10, $lines.Count); $j++) {
                if ($lines[$j] -match 'Trait.*"Category".*"Integration"') {
                    $hasIntegration = $true
                }
                if ($lines[$j] -match 'public\s+.*\s+(\w+)\s*\(') {
                    $methodName = $Matches[1]
                    break
                }
            }
            
            if (-not $hasIntegration -and $methodName) {
                $issues += [PSCustomObject]@{
                    File = $file.Name
                    Line = $i + 1
                    Method = $methodName
                }
            }
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host "`n❌ Found $($issues.Count) trait hygiene issue(s):" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $($issue.File):$($issue.Line) - $($issue.Method)" -ForegroundColor Yellow
    }
    Write-Host "`nRule: Any live-environment integration test must include [Trait(`"Category`", `"Integration`")]" -ForegroundColor Cyan
    exit 1
}

Write-Host "`n✅ Integration trait hygiene OK" -ForegroundColor Green
exit 0
