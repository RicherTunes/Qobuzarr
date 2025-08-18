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
    
    # Clone Lidarr repository and checkout the PLUGIN BRANCH (critical!)
    git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
    git -C ext/Lidarr-source checkout origin/plugins
    echo "✅ Lidarr source downloaded successfully (plugin branch)"
    
    # Apply TrevTV's assembly version override for hotio compatibility
    echo "🔧 Applying TrevTV assembly version override..."
    LIDARR_VERSION_OVERRIDE="2.13.2.4686"
    sed -i "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$LIDARR_VERSION_OVERRIDE<\/AssemblyVersion>/g" ext/Lidarr-source/src/Directory.Build.props
    echo "✅ Assembly version override applied: $LIDARR_VERSION_OVERRIDE"
    
    # Build Lidarr source to generate plugin interfaces
    echo "🔨 Building Lidarr source (this takes a few minutes)..."
    cd ext/Lidarr-source
    dotnet restore src/NzbDrone.Core/Lidarr.Core.csproj --disable-build-servers
    dotnet build src/NzbDrone.Core/Lidarr.Core.csproj --configuration Release --no-restore -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false
    cd ../..
    echo "✅ Lidarr source built successfully with plugin interfaces"
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