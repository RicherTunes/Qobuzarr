#!/bin/bash
# =============================================================================
# Qobuzarr Development Setup Script
# =============================================================================

set -e

# Parse command line arguments
ENABLE_DEPLOY=false
DEPLOY_PATH=""
SKIP_BUILD=false
SKIP_TESTS=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --enable-deploy)
            ENABLE_DEPLOY=true
            shift
            ;;
        --deploy-path)
            DEPLOY_PATH="$2"
            shift 2
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--enable-deploy] [--deploy-path PATH] [--skip-build] [--skip-tests]"
            exit 1
            ;;
    esac
done

echo "🎵 Setting up Qobuzarr development environment..."

# Check if we're in the right directory
if [ ! -f "Qobuzarr.csproj" ]; then
    echo "❌ Error: Please run this script from the Qobuzarr root directory"
    exit 1
fi

# Create ext directory if it doesn't exist
mkdir -p ext

# Check if Lidarr source already exists
if [ -d "ext/Lidarr-source" ]; then
    echo "📁 Lidarr source already exists, skipping download..."
else
    echo "📥 Downloading Lidarr source code..."
    
    # Clone Lidarr repository and checkout the exact commit that working plugins use
    git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
    git -C ext/Lidarr-source checkout aa7b63f2e13351f54a31d780d6a7b93a2411eaec
    
    echo "✅ Lidarr source downloaded successfully"
fi

# Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# Attempt to build the project
if [ "$SKIP_BUILD" = false ]; then
    echo "🔨 Building Qobuzarr..."
    
    # Prepare build parameters
    BUILD_PARAMS="--configuration Debug --no-restore -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false"
    
    # Add deployment parameters if specified
    if [ "$ENABLE_DEPLOY" = true ]; then
        BUILD_PARAMS="$BUILD_PARAMS -p:EnablePluginDeployment=true"
        if [ -n "$DEPLOY_PATH" ]; then
            BUILD_PARAMS="$BUILD_PARAMS -p:LidarrPluginDeployPath=$DEPLOY_PATH"
        fi
        echo "🚀 Plugin deployment enabled"
    fi
    
    # Build with analyzers disabled to avoid StyleCop issues from Lidarr source
    if dotnet build $BUILD_PARAMS; then
        echo "✅ Build successful!"
    else
        echo "⚠️ Build failed - this may be due to Lidarr version compatibility issues"
        echo "   Try running: dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false"
    fi
fi

# Try to run tests
if [ "$SKIP_TESTS" = false ]; then
    echo "🧪 Running tests..."
    if dotnet test --no-build --verbosity minimal; then
        echo "✅ Tests passed!"
    else
        echo "⚠️ Some tests failed - this may be due to missing dependencies"
    fi
fi

echo ""
echo "🎉 Setup complete!"
echo ""
echo "Next steps:"
echo "1. Review the build output above for any errors"
echo "2. If build fails, check ext/Lidarr-source version compatibility"
echo "3. Configure your IDE to reference the Qobuzarr.sln solution"
echo "4. Set up your Qobuz API credentials for development"
echo ""
echo "Plugin Deployment:"
echo "• To enable auto-deployment: ./setup.sh --enable-deploy"
echo "• Custom deploy path: ./setup.sh --enable-deploy --deploy-path '/custom/path'"
echo "• Default deploy location: X:\\lidarr-hotio-test2\\plugins\\RicherTunes\\Qobuzarr"
echo ""
echo "For help, see: docs/development/DEVELOPMENT.md"