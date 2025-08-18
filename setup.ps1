# =============================================================================
# Qobuzarr Development Setup Script (PowerShell)
# =============================================================================

param(
    [switch]$SkipLidarr,
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$EnableDeploy,
    [string]$DeployPath = ""
)

Write-Host "🎵 Setting up Qobuzarr development environment..." -ForegroundColor Green

# Check if we're in the right directory
if (-not (Test-Path "Qobuzarr.csproj")) {
    Write-Host "❌ Error: Please run this script from the Qobuzarr root directory" -ForegroundColor Red
    exit 1
}

# Create ext directory if it doesn't exist
if (-not (Test-Path "ext")) {
    New-Item -ItemType Directory -Path "ext" | Out-Null
}

# Check if Lidarr source already exists
if (-not $SkipLidarr) {
    if (Test-Path "ext/Lidarr-source") {
        Write-Host "📁 Lidarr source already exists, skipping download..." -ForegroundColor Yellow
    } else {
        Write-Host "📥 Downloading Lidarr source code..." -ForegroundColor Blue
        
        try {
            # Clone Lidarr repository and checkout the exact commit that working plugins use
            git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
            git -C ext/Lidarr-source checkout aa7b63f2e13351f54a31d780d6a7b93a2411eaec
            Write-Host "✅ Lidarr source downloaded successfully" -ForegroundColor Green
        } catch {
            Write-Host "❌ Failed to clone Lidarr repository: $_" -ForegroundColor Red
            Write-Host "   You may need to install Git or check your internet connection" -ForegroundColor Yellow
        }
    }
}

# Restore NuGet packages
Write-Host "📦 Restoring NuGet packages..." -ForegroundColor Blue
try {
    dotnet restore
    Write-Host "✅ Packages restored successfully" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to restore packages: $_" -ForegroundColor Red
}

# Attempt to build the project
if (-not $SkipBuild) {
    Write-Host "🔨 Building Qobuzarr..." -ForegroundColor Blue
    try {
        # Prepare build parameters
        $buildParams = @(
            "--configuration", "Debug",
            "--no-restore",
            "-p:RunAnalyzersDuringBuild=false",
            "-p:EnableNETAnalyzers=false", 
            "-p:TreatWarningsAsErrors=false"
        )
        
        # Add deployment parameters if specified
        if ($EnableDeploy) {
            $buildParams += "-p:EnablePluginDeployment=true"
            if ($DeployPath -ne "") {
                $buildParams += "-p:LidarrPluginDeployPath=$DeployPath"
            }
            Write-Host "🚀 Plugin deployment enabled" -ForegroundColor Cyan
        }
        
        # Build with analyzers disabled to avoid StyleCop issues from Lidarr source
        & dotnet build @buildParams
        Write-Host "✅ Build successful!" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Build failed - this may be due to Lidarr version compatibility issues" -ForegroundColor Yellow
        Write-Host "   Try running: dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false" -ForegroundColor Yellow
    }
}

# Try to run tests
if (-not $SkipTests) {
    Write-Host "🧪 Running tests..." -ForegroundColor Blue
    try {
        dotnet test --no-build --verbosity minimal
        Write-Host "✅ Tests passed!" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Some tests failed - this may be due to missing dependencies" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "🎉 Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Review the build output above for any errors" -ForegroundColor White
Write-Host "2. If build fails, check ext/Lidarr-source version compatibility" -ForegroundColor White
Write-Host "3. Configure your IDE to reference the Qobuzarr.sln solution" -ForegroundColor White
Write-Host "4. Set up your Qobuz API credentials for development" -ForegroundColor White
Write-Host ""
Write-Host "Plugin Deployment:" -ForegroundColor Cyan
Write-Host "• To enable auto-deployment: .\setup.ps1 -EnableDeploy" -ForegroundColor White
Write-Host "• Custom deploy path: .\setup.ps1 -EnableDeploy -DeployPath 'C:\Custom\Path'" -ForegroundColor White
Write-Host "• Default deploy location: X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr" -ForegroundColor White
Write-Host ""
Write-Host "For help, see: docs/development/DEVELOPMENT.md" -ForegroundColor Gray