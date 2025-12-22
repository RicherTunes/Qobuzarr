<#
.SYNOPSIS
    Validates that all tests in tests/Integration/ that use SkippableFact or access Framework
    have the Category=Integration trait.

.DESCRIPTION
    This script ensures trait hygiene: any test that requires the live Lidarr environment
    must be tagged with Category=Integration so CI can filter appropriately.

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
    
    # Skip files that don't have SkippableFact (they don't need Integration trait)
    if ($content -notmatch '\[SkippableFact\]') {
        continue
    }
    
    # Find all SkippableFact occurrences and check if they have Integration trait
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # Skip XML documentation comments
        if ($lines[$i] -match '^\s*///' ) {
            continue
        }
        if ($lines[$i] -match '\[SkippableFact\]') {
            # Look at the next few lines for traits
            $hasIntegration = $false
            $methodName = ""
            
            for ($j = $i; $j -lt [Math]::Min($i + 10, $lines.Count); $j++) {
                if ($lines[$j] -match 'Trait.*"Category".*"Integration"') {
                    $hasIntegration = $true
                }
                if ($lines[$j] -match 'public\s+async\s+Task\s+(\w+)') {
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
    Write-Host "`n❌ Found $($issues.Count) test(s) with SkippableFact but missing Category=Integration:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $($issue.File):$($issue.Line) - $($issue.Method)" -ForegroundColor Yellow
    }
    Write-Host "`nRule: Any test using [SkippableFact] requires [Trait(`"Category`", `"Integration`")]" -ForegroundColor Cyan
    exit 1
}

Write-Host "`n✅ All SkippableFact tests have Category=Integration trait" -ForegroundColor Green
exit 0
