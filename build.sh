#!/bin/bash
# =============================================================================
# Qobuzarr Build Script (Bash)
# =============================================================================
# Quick and easy building with deployment options for development

set -e

# Default values
CONFIGURATION="Debug"
DEPLOY=false
DEPLOY_PATH=""
CLEAN=false
RESTORE=false
NO_BUILD=false
VERBOSE=false
HELP=false

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
WHITE='\033[1;37m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

function show_help() {
    echo -e "${GREEN}🔨 Qobuzarr Build Script${NC}"
    echo ""
    echo -e "${CYAN}USAGE:${NC}"
    echo -e "  ${WHITE}./build.sh [Configuration] [Options]${NC}"
    echo ""
    echo -e "${CYAN}CONFIGURATIONS:${NC}"
    echo -e "  ${WHITE}Debug                 Debug build with symbols (default)${NC}"
    echo -e "  ${WHITE}Release               Optimized release build${NC}"
    echo ""
    echo -e "${CYAN}OPTIONS:${NC}"
    echo -e "  ${WHITE}--deploy              Auto-deploy to test Lidarr instance${NC}"
    echo -e "  ${WHITE}--deploy-path <path>  Custom deployment path${NC}"
    echo -e "  ${WHITE}--clean               Clean before building${NC}"
    echo -e "  ${WHITE}--restore             Force restore packages${NC}"
    echo -e "  ${WHITE}--no-build            Skip build (for clean/restore only)${NC}"
    echo -e "  ${WHITE}--verbose             Show detailed build output${NC}"
    echo -e "  ${WHITE}--help                Show this help${NC}"
    echo ""
    echo -e "${CYAN}EXAMPLES:${NC}"
    echo -e "  ${GRAY}./build.sh                          # Debug build${NC}"
    echo -e "  ${GRAY}./build.sh Release                  # Release build${NC}"
    echo -e "  ${GRAY}./build.sh --deploy                 # Debug build + auto-deploy${NC}"
    echo -e "  ${GRAY}./build.sh Release --deploy         # Release build + deploy${NC}"
    echo -e "  ${GRAY}./build.sh --clean --restore        # Clean, restore, and build${NC}"
    echo -e "  ${GRAY}./build.sh --deploy-path /custom    # Deploy to custom location${NC}"
    echo ""
    echo -e "${CYAN}DEFAULT DEPLOY PATH:${NC}"
    echo -e "  ${GRAY}X:\\\\lidarr-hotio-test2\\\\plugins\\\\RicherTunes\\\\Qobuzarr${NC}"
    echo ""
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        Debug|Release)
            CONFIGURATION="$1"
            shift
            ;;
        --deploy)
            DEPLOY=true
            shift
            ;;
        --deploy-path)
            DEPLOY_PATH="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --restore)
            RESTORE=true
            shift
            ;;
        --no-build)
            NO_BUILD=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --help)
            HELP=true
            shift
            ;;
        *)
            echo -e "${RED}❌ Unknown option: $1${NC}"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

if [ "$HELP" = true ]; then
    show_help
    exit 0
fi

# Check if we're in the right directory
if [ ! -f "Qobuzarr.csproj" ]; then
    echo -e "${RED}❌ Error: Please run this script from the Qobuzarr root directory${NC}"
    echo -e "${YELLOW}   Current directory: $(pwd)${NC}"
    exit 1
fi

echo -e "${GREEN}🔨 Building Qobuzarr Plugin${NC}"
echo -e "${CYAN}Configuration: $CONFIGURATION${NC}"

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo ""
    echo -e "${BLUE}🧹 Cleaning...${NC}"
    if dotnet clean --configuration "$CONFIGURATION" --verbosity minimal; then
        echo -e "${GREEN}✅ Clean completed${NC}"
    else
        echo -e "${YELLOW}⚠️ Clean failed${NC}"
    fi
fi

# Restore if requested or if packages are missing
if [ "$RESTORE" = true ] || [ ! -d "obj" ]; then
    echo ""
    echo -e "${BLUE}📦 Restoring packages...${NC}"
    if dotnet restore --verbosity minimal; then
        echo -e "${GREEN}✅ Packages restored${NC}"
    else
        echo -e "${RED}❌ Package restore failed${NC}"
        exit 1
    fi
fi

# Build (unless --no-build is specified)
if [ "$NO_BUILD" = false ]; then
    echo ""
    echo -e "${BLUE}🔨 Building...${NC}"
    
    # Override Lidarr assembly version to match target hotio version (like TrevTV does)
    # Only apply if Lidarr source exists (not needed for pre-built assemblies)
    if [ -f "ext/Lidarr-source/src/Directory.Build.props" ]; then
        LIDARR_VERSION_OVERRIDE="2.13.2.4686"
        echo -e "${BLUE}🔧 Setting Lidarr assembly version to $LIDARR_VERSION_OVERRIDE${NC}"
        sed -i "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$LIDARR_VERSION_OVERRIDE<\/AssemblyVersion>/g" ext/Lidarr-source/src/Directory.Build.props
    else
        echo -e "${BLUE}📦 Using pre-built Lidarr assemblies (no version override needed)${NC}"
    fi
    
    # Prepare build parameters (always suppress analyzers to avoid Lidarr source issues)
    BUILD_PARAMS="Qobuzarr.csproj --configuration $CONFIGURATION --no-restore"
    BUILD_PARAMS="$BUILD_PARAMS -p:RunAnalyzersDuringBuild=false"
    BUILD_PARAMS="$BUILD_PARAMS -p:EnableNETAnalyzers=false"
    BUILD_PARAMS="$BUILD_PARAMS -p:TreatWarningsAsErrors=false"
    
    # Add deployment parameters
    if [ "$DEPLOY" = true ]; then
        BUILD_PARAMS="$BUILD_PARAMS -p:EnablePluginDeployment=true"
        if [ -n "$DEPLOY_PATH" ]; then
            BUILD_PARAMS="$BUILD_PARAMS -p:LidarrPluginDeployPath=$DEPLOY_PATH"
        fi
        echo -e "${CYAN}🚀 Plugin deployment enabled${NC}"
        if [ -n "$DEPLOY_PATH" ]; then
            echo -e "${CYAN}📁 Deploy path: $DEPLOY_PATH${NC}"
        else
            echo -e "${CYAN}📁 Deploy path: X:\\\\lidarr-hotio-test2\\\\plugins\\\\RicherTunes\\\\Qobuzarr${NC}"
        fi
    fi
    
    # Add verbosity
    if [ "$VERBOSE" = true ]; then
        BUILD_PARAMS="$BUILD_PARAMS --verbosity normal"
    else
        BUILD_PARAMS="$BUILD_PARAMS --verbosity minimal"
    fi
    
    # Execute build
    if dotnet build $BUILD_PARAMS; then
        echo ""
        echo -e "${GREEN}✅ Build successful!${NC}"
        echo -e "${GRAY}📍 Output: bin/Lidarr.Plugin.Qobuzarr.dll${NC}"
        
        if [ "$DEPLOY" = true ]; then
            echo -e "${GREEN}🚀 Plugin deployed and ready for testing${NC}"
            echo -e "${YELLOW}💡 Restart Lidarr to load the updated plugin${NC}"
        fi
    else
        echo ""
        echo -e "${RED}❌ Build failed!${NC}"
        echo -e "${YELLOW}💡 Try running with --verbose for more details${NC}"
        exit 1
    fi
fi

echo ""
echo -e "${GREEN}🎉 Build script completed!${NC}"

# Show next steps
if [ "$DEPLOY" = false ] && [ "$NO_BUILD" = false ]; then
    echo ""
    echo -e "${CYAN}💡 Next steps:${NC}"
    echo -e "${WHITE}• To deploy: ./build.sh $CONFIGURATION --deploy${NC}"
    echo -e "${WHITE}• Plugin location: bin/Lidarr.Plugin.Qobuzarr.dll${NC}"
    echo -e "${WHITE}• Manual deploy: Copy bin/* to Lidarr plugins folder${NC}"
fi