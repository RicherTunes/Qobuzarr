# =============================================================================
# Qobuzarr Build Script (PowerShell)
# =============================================================================
# Quick and easy building with deployment options for development

param(
    [Parameter(Position=0)]
    [ValidateSet("Debug", "Release", "")]
    [string]$Configuration = "Debug",
    
    [switch]$Deploy,
    [string]$DeployPath = "",
    
    [switch]$Clean,
    [switch]$Restore,
    [switch]$NoBuild,
    [switch]$VerboseOutput,
    [switch]$UsePrebuiltAssemblies,
    [string]$LidarrVersion = "2.13.2.4685",
    [switch]$Help
)

function Show-Help {
    Write-Host "🔨 Qobuzarr Build Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\build.ps1 [Configuration] [Options]" -ForegroundColor White
    Write-Host ""
    Write-Host "CONFIGURATIONS:" -ForegroundColor Cyan
    Write-Host "  Debug                 Debug build with symbols (default)" -ForegroundColor White
    Write-Host "  Release               Optimized release build" -ForegroundColor White
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Cyan
    Write-Host "  -Deploy               Auto-deploy to test Lidarr instance" -ForegroundColor White
    Write-Host "  -DeployPath [path]    Custom deployment path" -ForegroundColor White
    Write-Host "  -Clean                Clean before building" -ForegroundColor White
    Write-Host "  -Restore              Force restore packages" -ForegroundColor White
    Write-Host "  -NoBuild              Skip build (for clean/restore only)" -ForegroundColor White
    Write-Host "  -VerboseOutput        Show detailed build output" -ForegroundColor White
    Write-Host "  -UsePrebuiltAssemblies Use pre-built Lidarr assemblies (CI approach)" -ForegroundColor White
    Write-Host "  -LidarrVersion        Lidarr version for pre-built assemblies" -ForegroundColor White
    Write-Host "  -Help                 Show this help" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\build.ps1                          # Debug build" -ForegroundColor Gray
    Write-Host "  .\build.ps1 Release                  # Release build" -ForegroundColor Gray
    Write-Host "  .\build.ps1 -Deploy                  # Debug build + auto-deploy" -ForegroundColor Gray
    Write-Host "  .\build.ps1 Release -Deploy          # Release build + deploy" -ForegroundColor Gray
    Write-Host "  .\build.ps1 -Clean -Restore          # Clean, restore, and build" -ForegroundColor Gray
    Write-Host "  .\build.ps1 -DeployPath C:\Custom    # Deploy to custom location" -ForegroundColor Gray
    Write-Host "  .\build.ps1 -UsePrebuiltAssemblies   # Use CI approach with pre-built assemblies" -ForegroundColor Gray
    Write-Host ""
    Write-Host "DEFAULT DEPLOY PATH:" -ForegroundColor Cyan
    Write-Host "  X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr" -ForegroundColor Gray
    Write-Host ""
}

if ($Help) {
    Show-Help
    exit 0
}

# Check if we're in the right directory
if (-not (Test-Path "Qobuzarr.csproj")) {
    Write-Host "❌ Error: Please run this script from the Qobuzarr root directory" -ForegroundColor Red
    Write-Host "   Current directory: $(Get-Location)" -ForegroundColor Yellow
    exit 1
}

Write-Host "🔨 Building Qobuzarr Plugin" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan

# Clean if requested
if ($Clean) {
    Write-Host ""
    Write-Host "🧹 Cleaning..." -ForegroundColor Blue
    try {
        dotnet clean --configuration $Configuration --verbosity minimal
        Write-Host "✅ Clean completed" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Clean failed: $_" -ForegroundColor Yellow
    }
}

# Restore if requested or if packages are missing
if ($Restore -or -not (Test-Path "obj")) {
    Write-Host ""
    Write-Host "📦 Restoring packages..." -ForegroundColor Blue
    try {
        dotnet restore --verbosity minimal
        Write-Host "✅ Packages restored" -ForegroundColor Green
    } catch {
        Write-Host "❌ Package restore failed: $_" -ForegroundColor Red
        exit 1
    }
}

# Build (unless -NoBuild is specified)
if (-not $NoBuild) {
    Write-Host ""
    Write-Host "🔨 Building..." -ForegroundColor Blue
    
    # Override Lidarr assembly version to match target hotio version
    # Only apply if Lidarr source exists (not needed for pre-built assemblies)
    if (Test-Path "ext\Lidarr-source\src\Directory.Build.props") {
        $lidarrVersionOverride = "2.13.2.4685"
        Write-Host "🔧 Setting Lidarr assembly version to $lidarrVersionOverride" -ForegroundColor Blue
        (Get-Content "ext\Lidarr-source\src\Directory.Build.props") -replace '<AssemblyVersion>[\d\.\*]+</AssemblyVersion>', "<AssemblyVersion>$lidarrVersionOverride</AssemblyVersion>" | Set-Content "ext\Lidarr-source\src\Directory.Build.props"
    } else {
        Write-Host "📦 Using pre-built Lidarr assemblies (no version override needed)" -ForegroundColor Blue
    }
    
    # Prepare build parameters (always suppress analyzers to avoid Lidarr source issues)
    $buildParams = @(
        "Qobuzarr.csproj",  # Build only the main plugin project
        "--configuration", $Configuration,
        "--no-restore",
        "-p:RunAnalyzersDuringBuild=false",
        "-p:EnableNETAnalyzers=false",
        "-p:TreatWarningsAsErrors=false"
    )
    
    # Add deployment parameters
    if ($Deploy) {
        $buildParams += "-p:EnablePluginDeployment=true"
        if ($DeployPath -ne "") {
            $buildParams += "-p:LidarrPluginDeployPath=$DeployPath"
        }
        Write-Host "🚀 Plugin deployment enabled" -ForegroundColor Cyan
        if ($DeployPath -ne "") {
            Write-Host "📁 Deploy path: $DeployPath" -ForegroundColor Cyan
        } else {
            Write-Host "📁 Deploy path: X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr" -ForegroundColor Cyan
        }
    }
    
    # Add verbosity if requested
    if ($VerboseOutput) {
        $buildParams += "--verbosity", "normal"
    } else {
        $buildParams += "--verbosity", "minimal"
    }
    
    # Execute build
    try {
        $buildResult = & dotnet build @buildParams
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✅ Build successful!" -ForegroundColor Green
            Write-Host "📍 Output: bin\Lidarr.Plugin.Qobuzarr.dll" -ForegroundColor Gray
            
            if ($Deploy) {
                Write-Host "🚀 Plugin deployed and ready for testing" -ForegroundColor Green
                Write-Host "💡 Restart Lidarr to load the updated plugin" -ForegroundColor Yellow
            }
        } else {
            Write-Host ""
            Write-Host "❌ Build failed!" -ForegroundColor Red
            Write-Host "💡 Try running with -VerboseOutput for more details" -ForegroundColor Yellow
            exit 1
        }
    } catch {
        Write-Host ""
        Write-Host "❌ Build failed with exception: $_" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "🎉 Build script completed!" -ForegroundColor Green

# Show next steps
if (-not $Deploy -and -not $NoBuild) {
    Write-Host ""
    Write-Host "💡 Next steps:" -ForegroundColor Cyan
    Write-Host "• To deploy: .\build.ps1 $Configuration -Deploy" -ForegroundColor White
    Write-Host "• Plugin location: bin\Lidarr.Plugin.Qobuzarr.dll" -ForegroundColor White
    Write-Host "• Manual deploy: Copy bin\* to Lidarr plugins folder" -ForegroundColor White
}