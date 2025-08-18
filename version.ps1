# =============================================================================
# Qobuzarr Version Management Script (PowerShell)
# =============================================================================
# Single source of truth for version management

param(
    [Parameter(Position=0)]
    [string]$Action = "show",
    
    [string]$Major,
    [string]$Minor, 
    [string]$Patch,
    [string]$Suffix = "",
    [string]$NewVersion,
    [switch]$Help
)

function Show-Help {
    Write-Host "🔢 Qobuzarr Version Management" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\version.ps1 [action] [options]" -ForegroundColor White
    Write-Host ""
    Write-Host "ACTIONS:" -ForegroundColor Cyan
    Write-Host "  show                  Show current version (default)" -ForegroundColor White
    Write-Host "  set <version>         Set specific version (e.g., 1.0.0)" -ForegroundColor White
    Write-Host "  bump major|minor|patch Increment version component" -ForegroundColor White
    Write-Host "  suffix <text>         Add pre-release suffix (e.g., -beta1)" -ForegroundColor White
    Write-Host "  release               Remove pre-release suffix for final release" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\version.ps1                     # Show: 0.1.0" -ForegroundColor Gray
    Write-Host "  .\version.ps1 set 1.0.0           # Set to 1.0.0" -ForegroundColor Gray
    Write-Host "  .\version.ps1 bump major          # 1.0.0 -> 2.0.0" -ForegroundColor Gray
    Write-Host "  .\version.ps1 bump minor          # 1.0.0 -> 1.1.0" -ForegroundColor Gray
    Write-Host "  .\version.ps1 bump patch          # 1.0.0 -> 1.0.1" -ForegroundColor Gray
    Write-Host "  .\version.ps1 suffix beta1        # 1.0.0 -> 1.0.0-beta1" -ForegroundColor Gray
    Write-Host "  .\version.ps1 release             # 1.0.0-beta1 -> 1.0.0" -ForegroundColor Gray
    Write-Host ""
    Write-Host "RELEASE WORKFLOW:" -ForegroundColor Cyan
    Write-Host "1. .\version.ps1 set 1.0.0-beta1" -ForegroundColor White
    Write-Host "2. git tag v1.0.0-beta1 && git push --tags" -ForegroundColor White
    Write-Host "3. CI automatically builds and creates GitHub release" -ForegroundColor White
    Write-Host ""
}

if ($Help) {
    Show-Help
    exit 0
}

# Read current version from VERSION file
if (Test-Path "VERSION") {
    $currentVersion = Get-Content "VERSION" -Raw | ForEach-Object { $_.Trim() }
} else {
    $currentVersion = "0.1.0"
}

switch ($Action.ToLower()) {
    "show" {
        Write-Host "Current version: $currentVersion" -ForegroundColor Green
    }
    
    "set" {
        if ($NewVersion) {
            $currentVersion = $NewVersion
        } elseif ($args[0]) {
            $currentVersion = $args[0]
        } else {
            Write-Host "❌ Error: Please specify a version" -ForegroundColor Red
            Write-Host "   Usage: .\version.ps1 set 1.0.0" -ForegroundColor Yellow
            exit 1
        }
        
        Set-Content "VERSION" $currentVersion
        Write-Host "✅ Version set to: $currentVersion" -ForegroundColor Green
    }
    
    "bump" {
        $component = if ($args[0]) { $args[0] } else { "patch" }
        
        # Parse current version
        if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)(.*)$') {
            $major = [int]$matches[1]
            $minor = [int]$matches[2] 
            $patch = [int]$matches[3]
            $suffix = $matches[4]
            
            switch ($component.ToLower()) {
                "major" { $major++; $minor = 0; $patch = 0 }
                "minor" { $minor++; $patch = 0 }
                "patch" { $patch++ }
                default {
                    Write-Host "❌ Error: Invalid component '$component'" -ForegroundColor Red
                    Write-Host "   Valid options: major, minor, patch" -ForegroundColor Yellow
                    exit 1
                }
            }
            
            $newVersion = "$major.$minor.$patch$suffix"
            Set-Content "VERSION" $newVersion
            Write-Host "✅ Version bumped: $currentVersion -> $newVersion" -ForegroundColor Green
        } else {
            Write-Host "❌ Error: Invalid version format in VERSION file" -ForegroundColor Red
            exit 1
        }
    }
    
    "suffix" {
        $suffixText = if ($args[0]) { $args[0] } else { $Suffix }
        if (-not $suffixText) {
            Write-Host "❌ Error: Please specify a suffix" -ForegroundColor Red
            Write-Host "   Usage: .\version.ps1 suffix beta1" -ForegroundColor Yellow
            exit 1
        }
        
        # Remove existing suffix and add new one
        $baseVersion = $currentVersion -replace '-.*$', ''
        $newVersion = "$baseVersion-$suffixText"
        Set-Content "VERSION" $newVersion
        Write-Host "✅ Version updated: $currentVersion -> $newVersion" -ForegroundColor Green
    }
    
    "release" {
        # Remove pre-release suffix
        $releaseVersion = $currentVersion -replace '-.*$', ''
        Set-Content "VERSION" $releaseVersion
        Write-Host "✅ Released: $currentVersion -> $releaseVersion" -ForegroundColor Green
    }
    
    default {
        Write-Host "❌ Error: Unknown action '$Action'" -ForegroundColor Red
        Write-Host "   Valid actions: show, set, bump, suffix, release" -ForegroundColor Yellow
        Write-Host "   Use -Help for more information" -ForegroundColor Yellow
        exit 1
    }
}