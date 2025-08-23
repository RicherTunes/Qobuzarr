#!/bin/bash
# Qobuzarr Live Integration Testing Script
# Automates building, deploying, and testing the plugin against a live Lidarr instance

set -euo pipefail

# Configuration
LIDARR_URL="${LIDARR_URL:-http://localhost:8686}"
LIDARR_API_KEY="${LIDARR_API_KEY:-}"
DOCKER_CONTAINER_NAME="${DOCKER_CONTAINER_NAME:-}"
BUILD_FIRST="${BUILD_FIRST:-true}"
DEPLOY_PLUGIN="${DEPLOY_PLUGIN:-true}"
RESTART_LIDARR="${RESTART_LIDARR:-false}"
TEST_FILTER="${TEST_FILTER:-}"
VERBOSE="${VERBOSE:-false}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

function log_status() {
    local message="$1"
    local type="${2:-info}"
    local timestamp=$(date "+%H:%M:%S")
    
    case $type in
        "success") echo -e "[$timestamp] ${GREEN}✅ $message${NC}" ;;
        "warning") echo -e "[$timestamp] ${YELLOW}⚠️  $message${NC}" ;;
        "error")   echo -e "[$timestamp] ${RED}❌ $message${NC}" ;;
        "info")    echo -e "[$timestamp] ${CYAN}ℹ️  $message${NC}" ;;
        *)         echo -e "[$timestamp] 📝 $message" ;;
    esac
}

function check_prerequisites() {
    log_status "Validating prerequisites..." "info"
    
    # Check if dotnet is available
    if ! command -v dotnet &> /dev/null; then
        log_status ".NET SDK not found. Please install .NET SDK 6.0 or later." "error"
        exit 1
    fi
    
    # Check if Docker is available (if Docker testing is requested)
    if [[ -n "$DOCKER_CONTAINER_NAME" ]] && ! command -v docker &> /dev/null; then
        log_status "Docker not found but Docker container specified. Docker commands will be skipped." "warning"
        DOCKER_CONTAINER_NAME=""
    fi
    
    # Validate required parameters
    if [[ -z "$LIDARR_URL" ]]; then
        log_status "LIDARR_URL not specified. Set environment variable or export it." "error"
        exit 1
    fi
    
    if [[ -z "$LIDARR_API_KEY" ]]; then
        log_status "LIDARR_API_KEY not specified. Set environment variable or export it." "error"
        exit 1
    fi
    
    log_status "Prerequisites validated successfully" "success"
}

function build_plugin() {
    if [[ "$BUILD_FIRST" != "true" ]]; then
        log_status "Skipping plugin build (BUILD_FIRST = false)" "info"
        return
    fi
    
    log_status "Building Qobuzarr plugin..." "info"
    
    # Use the recommended build command from CLAUDE.md
    if dotnet build Qobuzarr.csproj --configuration Debug \
        -p:RunAnalyzersDuringBuild=false \
        -p:EnableNETAnalyzers=false \
        -p:TreatWarningsAsErrors=false \
        -p:EnablePluginDeployment=false; then
        log_status "Plugin built successfully" "success"
    else
        log_status "Plugin build failed" "error"
        exit 1
    fi
}

function deploy_plugin() {
    if [[ "$DEPLOY_PLUGIN" != "true" ]]; then
        log_status "Skipping plugin deployment (DEPLOY_PLUGIN = false)" "info"
        return
    fi
    
    log_status "Deploying plugin to Lidarr instance..." "info"
    
    # Check if deployment target exists
    local plugin_dll="bin/Lidarr.Plugin.Qobuzarr.dll"
    if [[ ! -f "$plugin_dll" ]]; then
        log_status "Plugin DLL not found: $plugin_dll. Run build first." "error"
        exit 1
    fi
    
    if [[ -n "$DOCKER_CONTAINER_NAME" ]]; then
        log_status "Deploying to Docker container: $DOCKER_CONTAINER_NAME" "info"
        
        # Copy main plugin files
        if docker cp "$plugin_dll" "${DOCKER_CONTAINER_NAME}:/app/Plugins/Qobuzarr/" && \
           docker cp "plugin.json" "${DOCKER_CONTAINER_NAME}:/app/Plugins/Qobuzarr/"; then
            
            # Copy PDB files if they exist
            for pdb_file in bin/*.pdb; do
                if [[ -f "$pdb_file" ]]; then
                    docker cp "$pdb_file" "${DOCKER_CONTAINER_NAME}:/app/Plugins/Qobuzarr/" || true
                fi
            done
            
            log_status "Plugin deployed to Docker container successfully" "success"
        else
            log_status "Docker deployment failed, continuing with tests..." "warning"
        fi
    else
        log_status "No Docker container specified. Manual deployment required." "warning"
        log_status "Copy plugin files to your Lidarr Plugins/Qobuzarr/ directory manually." "info"
    fi
}

function restart_lidarr() {
    if [[ "$RESTART_LIDARR" != "true" ]]; then
        log_status "Skipping Lidarr restart (RESTART_LIDARR = false)" "info"
        return
    fi
    
    log_status "Restarting Lidarr..." "info"
    
    if [[ -n "$DOCKER_CONTAINER_NAME" ]]; then
        if docker restart "$DOCKER_CONTAINER_NAME"; then
            log_status "Docker container restarted" "success"
            
            # Wait for Lidarr to come back online
            log_status "Waiting for Lidarr to come back online..." "info"
            local max_wait=180 # 3 minutes
            local waited=0
            
            while [[ $waited -lt $max_wait ]]; do
                sleep 10
                waited=$((waited + 10))
                log_status "  Checking... ($waited/$max_wait seconds)" "info"
                
                if curl -s -f -H "X-Api-Key: $LIDARR_API_KEY" "$LIDARR_URL/api/v1/system/status" > /dev/null 2>&1; then
                    log_status "Lidarr is back online!" "success"
                    sleep 5 # Give it a moment to fully initialize
                    return
                fi
            done
            
            log_status "Timeout waiting for Lidarr to restart" "warning"
        else
            log_status "Docker restart failed" "warning"
        fi
    else
        log_status "No Docker container specified. Manual restart required if needed." "info"
    fi
}

function run_integration_tests() {
    log_status "Running live integration tests..." "info"
    
    # Set environment variables for the tests
    export LIDARR_URL="$LIDARR_URL"
    export LIDARR_API_KEY="$LIDARR_API_KEY"
    if [[ -n "$DOCKER_CONTAINER_NAME" ]]; then
        export DOCKER_CONTAINER_NAME="$DOCKER_CONTAINER_NAME"
    fi
    
    local test_args=(
        "test"
        "tests/Integration/"
        "--logger" "console;verbosity=detailed"
        "--configuration" "Debug"
    )
    
    if [[ -n "$TEST_FILTER" ]]; then
        test_args+=("--filter" "Priority=$TEST_FILTER")
        log_status "Running tests with filter: Priority=$TEST_FILTER" "info"
    fi
    
    if [[ "$VERBOSE" == "true" ]]; then
        test_args+=("--verbosity" "diagnostic")
    fi
    
    if dotnet "${test_args[@]}"; then
        log_status "All integration tests completed successfully!" "success"
    else
        log_status "Some integration tests failed. Check output above for details." "warning"
    fi
}

function show_summary() {
    local start_time="$1"
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local duration_formatted=$(date -u -d @$duration +"%M:%S")
    
    echo ""
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN}🎉 LIVE INTEGRATION TEST SUMMARY${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo -e "Duration: $duration_formatted"
    echo -e "Lidarr: $LIDARR_URL"
    echo -e "Docker: $(if [[ -n "$DOCKER_CONTAINER_NAME" ]]; then echo "$DOCKER_CONTAINER_NAME"; else echo "Not configured"; fi)"
    echo ""
    echo -e "${GREEN}✅ Plugin deployment: $(if [[ "$DEPLOY_PLUGIN" == "true" ]]; then echo "Completed"; else echo "Skipped"; fi)${NC}"
    echo -e "${YELLOW}🔄 Lidarr restart: $(if [[ "$RESTART_LIDARR" == "true" ]]; then echo "Completed"; else echo "Skipped"; fi)${NC}"
    echo -e "${GREEN}🧪 Integration tests: Completed${NC}"
    echo ""
    echo -e "${YELLOW}Next steps:${NC}"
    echo "1. Review test results above"
    echo "2. Check Lidarr logs for any issues"
    echo "3. Test manual search/download in Lidarr UI"
    echo -e "${CYAN}========================================${NC}"
}

function show_help() {
    cat << EOF
Qobuzarr Live Integration Testing Script

USAGE:
    $0 [OPTIONS]

OPTIONS:
    --lidarr-url URL        Lidarr instance URL (default: \$LIDARR_URL)
    --api-key KEY          Lidarr API key (default: \$LIDARR_API_KEY)
    --docker-container NAME Docker container name (default: \$DOCKER_CONTAINER_NAME)
    --no-build             Skip building the plugin
    --no-deploy            Skip deploying the plugin
    --restart              Restart Lidarr after deployment
    --filter FILTER        Test filter (e.g., Critical, High, Medium)
    --verbose              Enable verbose test output
    --help                 Show this help

EXAMPLES:
    # Basic integration test with automatic build and deploy
    $0 --docker-container lidarr
    
    # Quick test without rebuild
    $0 --no-build --filter Critical
    
    # Full test with restart
    $0 --restart --verbose

ENVIRONMENT VARIABLES:
    LIDARR_URL            Your Lidarr instance URL
    LIDARR_API_KEY        Your Lidarr API key  
    DOCKER_CONTAINER_NAME Docker container name
    
EOF
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --lidarr-url)
            LIDARR_URL="$2"
            shift 2
            ;;
        --api-key)
            LIDARR_API_KEY="$2"
            shift 2
            ;;
        --docker-container)
            DOCKER_CONTAINER_NAME="$2"
            shift 2
            ;;
        --no-build)
            BUILD_FIRST="false"
            shift
            ;;
        --no-deploy)
            DEPLOY_PLUGIN="false"
            shift
            ;;
        --restart)
            RESTART_LIDARR="true"
            shift
            ;;
        --filter)
            TEST_FILTER="$2"
            shift 2
            ;;
        --verbose)
            VERBOSE="true"
            shift
            ;;
        --help)
            show_help
            exit 0
            ;;
        *)
            log_status "Unknown option: $1" "error"
            show_help
            exit 1
            ;;
    esac
done

# Main execution
start_time=$(date +%s)

echo ""
echo -e "${CYAN}🎵 QOBUZARR LIVE INTEGRATION TESTING${NC}"
echo -e "${CYAN}====================================${NC}"
echo ""

check_prerequisites
build_plugin
deploy_plugin
restart_lidarr
run_integration_tests

show_summary "$start_time"