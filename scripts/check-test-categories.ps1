<#
.SYNOPSIS
    Category Drift Detection Script
.DESCRIPTION
    Ensures all test categories used in code are documented and filtered appropriately.
.EXAMPLE
    ./scripts/check-test-categories.ps1
.NOTES
    Exit codes: 0 = all categories documented, 1 = undocumented categories found
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Documented categories (must match docs/TESTING.md and CI_TEST_FILTER)
$DocumentedCategories = @(
    'Integration'
    'Performance'
    'LiveIntegration'
    'Quarantined'
    'Slow'
    'Benchmark'
    'Stress'
    'Simulations'
    'Unit'
    'Unquarantined'
)

Write-Host "Scanning for test categories..."

# Find all Category traits in test files
$testsPath = Join-Path $PSScriptRoot '..' 'tests'
$pattern = '\[Trait\("Category",\s*"([^"]*)"\)'

$foundCategories = @()
Get-ChildItem -Path $testsPath -Recurse -Include '*.cs' | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $matches = [regex]::Matches($content, $pattern)
    foreach ($match in $matches) {
        $category = $match.Groups[1].Value
        if ($category -and $foundCategories -notcontains $category) {
            $foundCategories += $category
        }
    }
}

if ($foundCategories.Count -eq 0) {
    Write-Host "No Category traits found in tests/"
    exit 0
}

Write-Host "Found categories:"
$foundCategories | Sort-Object | ForEach-Object { Write-Host "  - $_" }
Write-Host ""

# Check for undocumented categories
$undocumented = @()
foreach ($cat in $foundCategories) {
    if ($DocumentedCategories -notcontains $cat) {
        $undocumented += $cat
    }
}

if ($undocumented.Count -gt 0) {
    Write-Host "ERROR: Undocumented test categories found:" -ForegroundColor Red
    $undocumented | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Please add these categories to:" -ForegroundColor Yellow
    Write-Host "  1. docs/TESTING.md (category table)"
    Write-Host "  2. CI_TEST_FILTER in workflow env vars (if should be excluded)"
    Write-Host "  3. tests/Default.runsettings TestCaseFilter (if should be excluded by default)"
    Write-Host "  4. This script's `$DocumentedCategories array"
    exit 1
}

Write-Host "All categories are documented." -ForegroundColor Green
exit 0
