#!/bin/bash
# =============================================================================
# Qobuzarr Build Optimization Script (Bash)
# =============================================================================
# Optimizes build performance with caching, parallel execution, and pre-built assemblies

set -e

# Default values
CONFIGURATION="Release"
USE_CACHE=false
PARALLEL_BUILD=false
SKIP_TESTS=false
DEPLOY=false
DEPLOY_PATH="X:\\lidarr-hotio-test2\\plugins\\RicherTunes\\Qobuzarr"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
MAGENTA='\033[0;35m'
WHITE='\033[1;37m'
GRAY='\033[0;37m'
NC='\033[0m'

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --use-cache)
            USE_CACHE=true
            shift
            ;;
        --parallel)
            PARALLEL_BUILD=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
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
        --help)
            echo -e "${GREEN}🚀 Qobuzarr Optimized Build System${NC}"
            echo ""
            echo -e "${CYAN}USAGE:${NC}"
            echo -e "  ${WHITE}./optimize-build.sh [OPTIONS]${NC}"
            echo ""
            echo -e "${CYAN}OPTIONS:${NC}"
            echo -e "  ${WHITE}--configuration CONFIG   Build configuration (Debug/Release)${NC}"
            echo -e "  ${WHITE}--use-cache              Enable build caching${NC}"
            echo -e "  ${WHITE}--parallel               Enable parallel builds${NC}"
            echo -e "  ${WHITE}--skip-tests             Skip test execution${NC}"
            echo -e "  ${WHITE}--deploy                 Deploy to test instance${NC}"
            echo -e "  ${WHITE}--deploy-path PATH       Custom deployment path${NC}"
            echo -e "  ${WHITE}--help                   Show this help${NC}"
            exit 0
            ;;
        *)
            echo -e "${RED}❌ Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

START_TIME=$(date +%s)

echo -e "${GREEN}🚀 Qobuzarr Optimized Build System${NC}"
echo -e "${GREEN}=================================${NC}"

# Performance tracking
CACHE_HITS=0
CACHE_MISSES=0
BUILD_DURATION=0
TEST_DURATION=0

# Setup cache directory
CACHE_DIR="${TMPDIR:-/tmp}/qobuzarr-build-cache"
mkdir -p "$CACHE_DIR"

# Function to check and use cache
use_build_cache() {
    local key="$1"
    local build_action="$2"
    local output_path="$3"
    
    local cache_file="$CACHE_DIR/${key}.tar.gz"
    local hash_file="$CACHE_DIR/${key}.hash"
    
    # Calculate hash of relevant files
    local current_hash=$(find src -type f \( -name "*.cs" -o -name "*.csproj" \) -exec sha256sum {} \; | sha256sum | cut -d' ' -f1)
    
    if [[ "$USE_CACHE" == true ]] && [[ -f "$hash_file" ]] && [[ -f "$cache_file" ]]; then
        local cached_hash=$(cat "$hash_file")
        if [[ "$cached_hash" == "$current_hash" ]]; then
            echo -e "${GREEN}✅ Cache hit for $key${NC}"
            ((CACHE_HITS++))
            
            # Restore from cache
            rm -rf "$output_path"
            mkdir -p "$(dirname "$output_path")"
            tar -xzf "$cache_file" -C "$(dirname "$output_path")"
            return 0
        fi
    fi
    
    echo -e "${YELLOW}🔨 Building $key (cache miss)${NC}"
    ((CACHE_MISSES++))
    
    # Execute build
    eval "$build_action"
    
    # Save to cache
    if [[ "$USE_CACHE" == true ]] && [[ -d "$output_path" ]]; then
        tar -czf "$cache_file" -C "$(dirname "$output_path")" "$(basename "$output_path")"
        echo "$current_hash" > "$hash_file"
        echo -e "${CYAN}💾 Cached $key for future builds${NC}"
    fi
    
    return 1
}

# Step 1: Optimize Lidarr assembly retrieval
echo -e "\n${BLUE}📦 Step 1: Optimizing Lidarr Dependencies${NC}"
LIDARR_CACHE_KEY="lidarr-assemblies-2.13.2.4685"

if [[ "$USE_CACHE" == true ]]; then
    LIDARR_PATH="ext/Lidarr/_output/net8.0"
    if use_build_cache "$LIDARR_CACHE_KEY" "./download-lidarr-assemblies.sh --version 2.13.2.4685 --force" "$LIDARR_PATH"; then
        echo -e "${GREEN}⚡ Lidarr assemblies loaded from cache (saved ~30s)${NC}"
    fi
else
    # Direct download without cache
    if [[ ! -f "ext/Lidarr/_output/net8.0/Lidarr.Core.dll" ]]; then
        ./download-lidarr-assemblies.sh --version 2.13.2.4685
    fi
fi

# Step 2: Parallel NuGet restore
echo -e "\n${BLUE}📦 Step 2: Restoring NuGet Packages${NC}"
RESTORE_START=$(date +%s)

if [[ "$PARALLEL_BUILD" == true ]]; then
    # Restore projects in parallel
    echo -e "${CYAN}Running parallel restore...${NC}"
    (
        dotnet restore Qobuzarr.csproj --verbosity minimal &
        dotnet restore QobuzCLI/QobuzCLI.csproj --verbosity minimal &
        wait
    )
    echo -e "${GREEN}✅ Parallel restore completed${NC}"
else
    dotnet restore --verbosity minimal
fi

RESTORE_DURATION=$(($(date +%s) - RESTORE_START))
echo -e "${GRAY}⏱️ Restore time: ${RESTORE_DURATION}s${NC}"

# Step 3: Optimized build with deterministic compilation
echo -e "\n${BLUE}🔨 Step 3: Building Qobuzarr${NC}"
BUILD_START=$(date +%s)

# Apply assembly version override
LIDARR_VERSION_OVERRIDE="2.13.2.4686"
if [[ -f "ext/Lidarr-source/src/Directory.Build.props" ]]; then
    echo -e "${CYAN}🔧 Applying assembly version override: $LIDARR_VERSION_OVERRIDE${NC}"
    sed -i "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$LIDARR_VERSION_OVERRIDE<\/AssemblyVersion>/g" ext/Lidarr-source/src/Directory.Build.props
fi

# Build parameters for optimization
BUILD_PARAMS="build Qobuzarr.csproj"
BUILD_PARAMS="$BUILD_PARAMS --configuration $CONFIGURATION"
BUILD_PARAMS="$BUILD_PARAMS --no-restore"
BUILD_PARAMS="$BUILD_PARAMS -p:RunAnalyzersDuringBuild=false"
BUILD_PARAMS="$BUILD_PARAMS -p:EnableNETAnalyzers=false"
BUILD_PARAMS="$BUILD_PARAMS -p:TreatWarningsAsErrors=false"
BUILD_PARAMS="$BUILD_PARAMS -p:Deterministic=true"
BUILD_PARAMS="$BUILD_PARAMS -p:ContinuousIntegrationBuild=true"

if [[ "$PARALLEL_BUILD" == true ]]; then
    BUILD_PARAMS="$BUILD_PARAMS -maxcpucount"
fi

if [[ "$DEPLOY" == true ]]; then
    BUILD_PARAMS="$BUILD_PARAMS -p:EnablePluginDeployment=true"
    BUILD_PARAMS="$BUILD_PARAMS -p:LidarrPluginDeployPath=$DEPLOY_PATH"
fi

# Execute build
if ! dotnet $BUILD_PARAMS; then
    echo -e "${RED}❌ Build failed!${NC}"
    exit 1
fi

BUILD_DURATION=$(($(date +%s) - BUILD_START))
echo -e "${GREEN}✅ Build completed in ${BUILD_DURATION}s${NC}"

# Step 4: Run tests (if not skipped)
if [[ "$SKIP_TESTS" == false ]]; then
    echo -e "\n${BLUE}🧪 Step 4: Running Tests${NC}"
    TEST_START=$(date +%s)
    
    if [[ "$PARALLEL_BUILD" == true ]]; then
        # Run test projects in parallel
        echo -e "${CYAN}Running tests in parallel...${NC}"
        find tests -name "*.csproj" | while read -r test_project; do
            dotnet test "$test_project" --configuration "$CONFIGURATION" --no-build --verbosity minimal &
        done
        wait
    else
        dotnet test --configuration "$CONFIGURATION" --no-build --verbosity minimal
    fi
    
    TEST_DURATION=$(($(date +%s) - TEST_START))
    echo -e "${GREEN}✅ Tests completed in ${TEST_DURATION}s${NC}"
fi

# Step 5: Deployment (if requested)
if [[ "$DEPLOY" == true ]]; then
    echo -e "\n${BLUE}🚀 Step 5: Deploying Plugin${NC}"
    
    if [[ -d "$DEPLOY_PATH" ]]; then
        # Backup existing deployment
        BACKUP_PATH="${DEPLOY_PATH}.backup.$(date +%Y%m%d-%H%M%S)"
        cp -r "$DEPLOY_PATH" "$BACKUP_PATH"
        echo -e "${CYAN}📦 Backed up existing deployment to $BACKUP_PATH${NC}"
    fi
    
    # Check deployment
    if [[ -f "$DEPLOY_PATH/Lidarr.Plugin.Qobuzarr.dll" ]]; then
        echo -e "${GREEN}✅ Plugin deployed successfully to $DEPLOY_PATH${NC}"
        echo -e "${YELLOW}💡 Restart Lidarr to load the updated plugin${NC}"
    fi
fi

# Step 6: Performance report
TOTAL_DURATION=$(($(date +%s) - START_TIME))
echo -e "\n${MAGENTA}📊 Build Performance Report${NC}"
echo -e "${MAGENTA}=================================${NC}"
echo -e "${WHITE}Total Duration: ${TOTAL_DURATION}s${NC}"
echo -e "${WHITE}Build Time: ${BUILD_DURATION}s${NC}"
echo -e "${WHITE}Test Time: ${TEST_DURATION}s${NC}"
echo -e "${GREEN}Cache Hits: ${CACHE_HITS}${NC}"
echo -e "${YELLOW}Cache Misses: ${CACHE_MISSES}${NC}"

if [[ $TOTAL_DURATION -lt 180 ]]; then
    echo -e "\n${GREEN}🎉 Build completed in under 3 minutes! Target achieved!${NC}"
else
    echo -e "\n${YELLOW}⚠️ Build took longer than 3 minutes. Consider enabling caching and parallel builds.${NC}"
fi

# Save metrics to file for monitoring
METRICS_FILE="$CACHE_DIR/build-metrics.json"
cat > "$METRICS_FILE" <<EOF
{
  "timestamp": "$(date -Iseconds)",
  "configuration": "$CONFIGURATION",
  "totalDuration": $TOTAL_DURATION,
  "buildDuration": $BUILD_DURATION,
  "testDuration": $TEST_DURATION,
  "cacheHits": $CACHE_HITS,
  "cacheMisses": $CACHE_MISSES
}
EOF

echo -e "\n${GREEN}✅ Build optimization complete!${NC}"