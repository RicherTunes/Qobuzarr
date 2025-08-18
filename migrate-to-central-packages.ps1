# PowerShell script to migrate projects to use centralized package management
# This removes PackageReference Version attributes from .csproj files

param(
    [switch]$DryRun,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
Migrate to Central Package Management

This script removes Version attributes from PackageReference elements in .csproj files
to use the centralized versions defined in Directory.Packages.props.

Usage:
  .\migrate-to-central-packages.ps1         # Apply changes
  .\migrate-to-central-packages.ps1 -DryRun # Preview changes without modifying files

"@
    exit 0
}

$ErrorActionPreference = "Stop"

Write-Host "🔧 Migrating to centralized package management..." -ForegroundColor Green

# Find all .csproj files (excluding external Lidarr source)
$projectFiles = Get-ChildItem -Path . -Name "*.csproj" -Recurse | Where-Object { 
    $_ -notmatch "ext[\\/]" -and $_ -notmatch "Lidarr-source" 
}

if ($projectFiles.Count -eq 0) {
    Write-Host "❌ No .csproj files found to migrate" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($projectFiles.Count) project files to migrate:" -ForegroundColor Cyan
$projectFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
Write-Host ""

$totalChanges = 0

foreach ($projectFile in $projectFiles) {
    Write-Host "Processing: $projectFile" -ForegroundColor Yellow
    
    $content = Get-Content -Path $projectFile -Raw
    $originalContent = $content
    $fileChanges = 0
    
    # Remove Version attributes from PackageReference elements
    # This regex matches: <PackageReference Include="PackageName" Version="x.x.x" />
    # And replaces with: <PackageReference Include="PackageName" />
    $pattern = '(<PackageReference\s+Include="[^"]+"\s+)Version="[^"]*"(\s*/?)\s*'
    $replacement = '$1$2'
    
    $content = $content -replace $pattern, $replacement
    
    # Also handle multi-line PackageReference elements
    $pattern2 = '(<PackageReference\s+Include="[^"]+")(\s*>\s*<[^>]*>\s*</PackageReference>|\s*/>)'
    $matches = [regex]::Matches($originalContent, '(<PackageReference[^>]*Version="[^"]*"[^>]*>)')
    
    if ($matches.Count -gt 0) {
        foreach ($match in $matches) {
            $original = $match.Value
            $modified = $original -replace '\s+Version="[^"]*"', ''
            $content = $content.Replace($original, $modified)
            $fileChanges++
        }
    }
    
    # Count line-by-line changes for single-line PackageReference
    $originalLines = $originalContent -split "`n"
    $newLines = $content -split "`n"
    
    for ($i = 0; $i -lt $originalLines.Count; $i++) {
        if ($i -lt $newLines.Count -and $originalLines[$i] -ne $newLines[$i]) {
            if ($originalLines[$i] -match 'PackageReference.*Version=') {
                $fileChanges++
                if ($DryRun) {
                    Write-Host "    Would change: $($originalLines[$i].Trim())" -ForegroundColor Red
                    Write-Host "              to: $($newLines[$i].Trim())" -ForegroundColor Green
                }
            }
        }
    }
    
    if ($fileChanges -gt 0) {
        $totalChanges += $fileChanges
        Write-Host "    📝 $fileChanges package references to update" -ForegroundColor Green
        
        if (-not $DryRun) {
            Set-Content -Path $projectFile -Value $content -NoNewline
            Write-Host "    ✅ Updated $projectFile" -ForegroundColor Green
        }
    } else {
        Write-Host "    ℹ️  No changes needed" -ForegroundColor Gray
    }
    
    Write-Host ""
}

Write-Host "Migration Summary:" -ForegroundColor Cyan
Write-Host "  Total changes: $totalChanges" -ForegroundColor White

if ($DryRun) {
    Write-Host "  🔍 Dry run completed - no files were modified" -ForegroundColor Yellow
    Write-Host "  Run without -DryRun to apply changes" -ForegroundColor Yellow
} else {
    Write-Host "  ✅ Migration completed!" -ForegroundColor Green
    Write-Host "  Next steps:" -ForegroundColor Cyan
    Write-Host "    1. Build the solution to verify everything works: dotnet build" -ForegroundColor Gray
    Write-Host "    2. Run tests to ensure compatibility: dotnet test" -ForegroundColor Gray
    Write-Host "    3. All package versions are now managed in Directory.Packages.props" -ForegroundColor Gray
}