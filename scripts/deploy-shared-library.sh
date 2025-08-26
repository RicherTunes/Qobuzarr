#!/bin/bash
set -euo pipefail

# Deploy Lidarr.Plugin.Common shared library for streaming plugin ecosystem
# Based on chief architect's recommendations for proper package separation

# Default values
TARGET="Local"
LIDARR_PATH="X:/lidarr-hotio-test2/plugins"
VERSION=""
CLEAN=false

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo -e "${CYAN}🚀 $1${NC}"
    echo -e "${CYAN}$(printf '=%.0s' $(seq 1 $((${#1} + 3))))${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️ $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --target)
            TARGET="$2"
            shift 2
            ;;
        --lidarr-path)
            LIDARR_PATH="$2"
            shift 2
            ;;
        --version)
            VERSION="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --target <Local|NuGet|Both>    Deployment target (default: Local)"
            echo "  --lidarr-path <path>           Lidarr plugins directory (default: X:/lidarr-hotio-test2/plugins)"
            echo "  --version <version>            Version to deploy (default: from VERSION file)"
            echo "  --clean                        Clean before building"
            echo "  --help                         Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0 --target Local"
            echo "  $0 --target NuGet --version 1.0.0"
            echo "  $0 --target Both --lidarr-path '/custom/lidarr/plugins'"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Validate target
case $TARGET in
    Local|NuGet|Both) ;;
    *)
        print_error "Invalid target: $TARGET. Must be Local, NuGet, or Both"
        exit 1
        ;;
esac

# Get version if not specified
if [[ -z "$VERSION" ]]; then
    if [[ -f "Lidarr.Plugin.Common/VERSION" ]]; then
        VERSION=$(head -n 1 "Lidarr.Plugin.Common/VERSION")
    else
        VERSION="1.0.0-dev"
        print_warning "No VERSION file found, using default: $VERSION"
    fi
fi

print_header "Deploying Lidarr.Plugin.Common v$VERSION"

# Clean if requested
if [[ "$CLEAN" == "true" ]]; then
    echo "🧹 Cleaning previous builds..."
    dotnet clean "Lidarr.Plugin.Common/Lidarr.Plugin.Common.csproj" --configuration Release
    print_success "Clean completed"
fi

# Build shared library
echo "🔨 Building shared library..."
if ! dotnet build "Lidarr.Plugin.Common/Lidarr.Plugin.Common.csproj" --configuration Release --verbosity minimal; then
    print_error "Build failed"
    exit 1
fi
print_success "Build completed successfully"

# Local deployment
if [[ "$TARGET" == "Local" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "📦 Deploying locally..."
    
    COMMON_PATH="$LIDARR_PATH/Common"
    SHARED_LIB_PATH="Lidarr.Plugin.Common/bin/Release/net6.0"

    # Create Common directory
    if [[ ! -d "$COMMON_PATH" ]]; then
        mkdir -p "$COMMON_PATH"
        print_success "Created Common directory: $COMMON_PATH"
    fi

    # Copy shared library files
    FILES_TO_COPY=(
        "Lidarr.Plugin.Common.dll"
        "Lidarr.Plugin.Common.pdb" 
        "Lidarr.Plugin.Common.deps.json"
    )

    for file in "${FILES_TO_COPY[@]}"; do
        SOURCE_PATH="$SHARED_LIB_PATH/$file"
        DEST_PATH="$COMMON_PATH/$file"
        
        if [[ -f "$SOURCE_PATH" ]]; then
            cp "$SOURCE_PATH" "$DEST_PATH"
            echo "  ✓ Copied $file"
        else
            print_warning "  ⚠ File not found: $file"
        fi
    done

    # Create version info
    VERSION_JSON=$(cat << EOF
{
    "version": "$VERSION",
    "deployedAt": "$(date -Iseconds)",
    "deployedBy": "$USER",
    "buildConfiguration": "Release"
}
EOF
)

    echo "$VERSION_JSON" > "$COMMON_PATH/version.json"
    
    print_success "Local deployment completed to: $COMMON_PATH"
fi

# NuGet packaging
if [[ "$TARGET" == "NuGet" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "📦 Creating NuGet package..."
    
    # Update version in project file
    CSPROJ_PATH="Lidarr.Plugin.Common/Lidarr.Plugin.Common.csproj"
    
    # Use sed to update version
    sed -i "s|<PackageVersion>.*</PackageVersion>|<PackageVersion>$VERSION</PackageVersion>|g" "$CSPROJ_PATH"
    sed -i "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$VERSION.0</AssemblyVersion>|g" "$CSPROJ_PATH"

    # Create packages directory
    mkdir -p packages

    # Create NuGet package
    if ! dotnet pack "Lidarr.Plugin.Common/Lidarr.Plugin.Common.csproj" --configuration Release --output "packages" --verbosity minimal; then
        print_error "NuGet packaging failed"
        exit 1
    fi

    PACKAGE_PATH="packages/Lidarr.Plugin.Common.$VERSION.nupkg"
    if [[ -f "$PACKAGE_PATH" ]]; then
        print_success "NuGet package created: $PACKAGE_PATH"
    else
        print_warning "NuGet package not found at expected path"
    fi
fi

# Display summary
print_header "Deployment Summary"
echo "📋 Shared Library: Lidarr.Plugin.Common v$VERSION"
echo "📋 Target: $TARGET"
echo "📋 Build Configuration: Release"

if [[ "$TARGET" == "Local" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "📋 Local Path: $COMMON_PATH"
fi

if [[ "$TARGET" == "NuGet" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "📋 NuGet Package: packages/Lidarr.Plugin.Common.$VERSION.nupkg"
fi

print_success "Deployment completed successfully!"

# Usage instructions
echo ""
echo "📚 Usage Instructions:"
echo ""

if [[ "$TARGET" == "Local" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "For local development, plugins can reference:"
    echo "  <ProjectReference Include=\"$COMMON_PATH/Lidarr.Plugin.Common.csproj\" />"
fi

if [[ "$TARGET" == "NuGet" ]] || [[ "$TARGET" == "Both" ]]; then
    echo "For NuGet usage, plugins can reference:"
    echo "  <PackageReference Include=\"Lidarr.Plugin.Common\" Version=\"$VERSION\" />"
fi

echo ""
echo "🎵 Ready for streaming plugin development!"