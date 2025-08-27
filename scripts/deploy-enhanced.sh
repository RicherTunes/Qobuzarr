#!/bin/bash
# Enhanced Deployment Script with Rollback and Health Checks
# Provides zero-downtime deployment with automatic rollback on failure

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DEPLOY_CONFIG="${DEPLOY_CONFIG:-$PROJECT_ROOT/.deploy.json}"
LOG_FILE="${LOG_FILE:-$PROJECT_ROOT/deploy.log}"
METRICS_FILE="${METRICS_FILE:-$PROJECT_ROOT/deploy-metrics.json}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Deployment defaults
DEFAULT_DEPLOY_PATH="/opt/lidarr/plugins/Qobuzarr"
DEFAULT_BACKUP_PATH="/opt/lidarr/plugin-backups"
DEFAULT_LIDARR_URL="http://localhost:7878"
DEFAULT_HEALTH_CHECK_RETRIES=5
DEFAULT_HEALTH_CHECK_DELAY=10

# Parse command line arguments
ENVIRONMENT="${1:-test}"
SKIP_BUILD="${2:-false}"
SKIP_TESTS="${3:-false}"
FORCE_DEPLOY="${4:-false}"
DRY_RUN="${5:-false}"

# Logging functions
log() {
    echo -e "${1}" | tee -a "$LOG_FILE"
}

log_info() {
    log "${CYAN}[INFO]${NC} $1"
}

log_success() {
    log "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    log "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    log "${RED}[ERROR]${NC} $1"
}

log_metric() {
    log "${MAGENTA}[METRIC]${NC} $1"
}

# Initialize deployment
init_deployment() {
    log_info "🚀 Initializing enhanced deployment"
    log_info "Environment: $ENVIRONMENT"
    log_info "Project root: $PROJECT_ROOT"
    
    # Create necessary directories
    mkdir -p "$(dirname "$LOG_FILE")"
    mkdir -p "$(dirname "$METRICS_FILE")"
    
    # Initialize metrics
    DEPLOY_START=$(date +%s)
    METRICS="{\"environment\":\"$ENVIRONMENT\",\"start_time\":\"$(date -Iseconds)\",\"steps\":[]}"
}

# Load deployment configuration
load_config() {
    log_info "📋 Loading deployment configuration"
    
    if [ -f "$DEPLOY_CONFIG" ]; then
        # Load from JSON config
        DEPLOY_PATH=$(jq -r ".environments.$ENVIRONMENT.deploy_path // \"$DEFAULT_DEPLOY_PATH\"" "$DEPLOY_CONFIG")
        BACKUP_PATH=$(jq -r ".environments.$ENVIRONMENT.backup_path // \"$DEFAULT_BACKUP_PATH\"" "$DEPLOY_CONFIG")
        LIDARR_URL=$(jq -r ".environments.$ENVIRONMENT.lidarr_url // \"$DEFAULT_LIDARR_URL\"" "$DEPLOY_CONFIG")
        API_KEY=$(jq -r ".environments.$ENVIRONMENT.api_key // \"\"" "$DEPLOY_CONFIG")
        HEALTH_CHECK_RETRIES=$(jq -r ".health_check.retries // $DEFAULT_HEALTH_CHECK_RETRIES" "$DEPLOY_CONFIG")
        HEALTH_CHECK_DELAY=$(jq -r ".health_check.delay // $DEFAULT_HEALTH_CHECK_DELAY" "$DEPLOY_CONFIG")
    else
        # Use environment variables or defaults
        DEPLOY_PATH="${LIDARR_PLUGIN_DEPLOY_PATH:-$DEFAULT_DEPLOY_PATH}"
        BACKUP_PATH="${LIDARR_PLUGIN_BACKUP_PATH:-$DEFAULT_BACKUP_PATH}"
        LIDARR_URL="${LIDARR_URL:-$DEFAULT_LIDARR_URL}"
        API_KEY="${LIDARR_API_KEY:-}"
        HEALTH_CHECK_RETRIES=$DEFAULT_HEALTH_CHECK_RETRIES
        HEALTH_CHECK_DELAY=$DEFAULT_HEALTH_CHECK_DELAY
    fi
    
    log_info "Deploy path: $DEPLOY_PATH"
    log_info "Backup path: $BACKUP_PATH"
    log_info "Lidarr URL: $LIDARR_URL"
}

# Build the plugin
build_plugin() {
    if [ "$SKIP_BUILD" == "true" ]; then
        log_warning "⚠️ Skipping build (--skip-build flag)"
        return 0
    fi
    
    log_info "🔨 Building plugin"
    local BUILD_START=$(date +%s)
    
    cd "$PROJECT_ROOT"
    
    # Check if build script exists
    if [ -f "./build.sh" ]; then
        if ./build.sh Release --verbose; then
            log_success "✅ Build completed successfully"
        else
            log_error "❌ Build failed!"
            return 1
        fi
    else
        # Fallback to direct dotnet build
        if dotnet build --configuration Release \
            -p:RunAnalyzersDuringBuild=false \
            -p:EnableNETAnalyzers=false \
            -p:TreatWarningsAsErrors=false; then
            log_success "✅ Build completed successfully"
        else
            log_error "❌ Build failed!"
            return 1
        fi
    fi
    
    local BUILD_END=$(date +%s)
    local BUILD_TIME=$((BUILD_END - BUILD_START))
    log_metric "Build time: ${BUILD_TIME}s"
    
    # Add to metrics
    METRICS=$(echo "$METRICS" | jq ".steps += [{\"name\":\"build\",\"duration\":$BUILD_TIME,\"status\":\"success\"}]")
}

# Run tests
run_tests() {
    if [ "$SKIP_TESTS" == "true" ]; then
        log_warning "⚠️ Skipping tests (--skip-tests flag)"
        return 0
    fi
    
    log_info "🧪 Running tests"
    local TEST_START=$(date +%s)
    
    cd "$PROJECT_ROOT"
    
    if [ -d "tests" ]; then
        if dotnet test --configuration Release --no-build \
            --logger "console;verbosity=minimal" \
            --results-directory ./TestResults; then
            log_success "✅ All tests passed"
        else
            log_error "❌ Tests failed!"
            if [ "$FORCE_DEPLOY" != "true" ]; then
                return 1
            fi
            log_warning "⚠️ Continuing despite test failures (--force flag)"
        fi
    else
        log_warning "⚠️ No tests found"
    fi
    
    local TEST_END=$(date +%s)
    local TEST_TIME=$((TEST_END - TEST_START))
    log_metric "Test time: ${TEST_TIME}s"
    
    # Add to metrics
    METRICS=$(echo "$METRICS" | jq ".steps += [{\"name\":\"test\",\"duration\":$TEST_TIME,\"status\":\"success\"}]")
}

# Create backup
create_backup() {
    log_info "💾 Creating backup of current deployment"
    
    if [ ! -d "$DEPLOY_PATH" ]; then
        log_warning "⚠️ No existing deployment to backup"
        return 0
    fi
    
    # Create backup directory
    mkdir -p "$BACKUP_PATH"
    
    # Create timestamped backup
    BACKUP_NAME="qobuzarr-backup-$(date +%Y%m%d-%H%M%S)"
    CURRENT_BACKUP="$BACKUP_PATH/$BACKUP_NAME"
    
    if cp -r "$DEPLOY_PATH" "$CURRENT_BACKUP"; then
        log_success "✅ Backup created: $CURRENT_BACKUP"
        echo "$CURRENT_BACKUP" > "$BACKUP_PATH/.last-backup"
    else
        log_error "❌ Failed to create backup!"
        return 1
    fi
    
    # Clean old backups (keep last 5)
    log_info "Cleaning old backups..."
    ls -dt "$BACKUP_PATH"/qobuzarr-backup-* 2>/dev/null | tail -n +6 | xargs -r rm -rf
}

# Deploy the plugin
deploy_plugin() {
    log_info "📦 Deploying plugin to $DEPLOY_PATH"
    
    if [ "$DRY_RUN" == "true" ]; then
        log_warning "⚠️ DRY RUN - not actually deploying"
        return 0
    fi
    
    local DEPLOY_START=$(date +%s)
    
    # Ensure deploy directory exists
    mkdir -p "$DEPLOY_PATH"
    
    # Copy plugin files
    local SOURCE_DIR="$PROJECT_ROOT/bin/Release/net6.0"
    if [ ! -d "$SOURCE_DIR" ]; then
        SOURCE_DIR="$PROJECT_ROOT/bin"
    fi
    
    if [ ! -d "$SOURCE_DIR" ]; then
        log_error "❌ Build output not found!"
        return 1
    fi
    
    # Deploy with atomic operation (minimize downtime)
    local TEMP_DEPLOY="$DEPLOY_PATH.tmp"
    rm -rf "$TEMP_DEPLOY"
    
    if cp -r "$SOURCE_DIR"/* "$TEMP_DEPLOY/" 2>/dev/null || cp -r "$SOURCE_DIR" "$TEMP_DEPLOY"; then
        # Atomic swap
        if [ -d "$DEPLOY_PATH.old" ]; then
            rm -rf "$DEPLOY_PATH.old"
        fi
        if [ -d "$DEPLOY_PATH" ]; then
            mv "$DEPLOY_PATH" "$DEPLOY_PATH.old"
        fi
        mv "$TEMP_DEPLOY" "$DEPLOY_PATH"
        
        log_success "✅ Plugin deployed successfully"
    else
        log_error "❌ Deployment failed!"
        rm -rf "$TEMP_DEPLOY"
        return 1
    fi
    
    # Set permissions
    if command -v chmod &> /dev/null; then
        chmod -R 755 "$DEPLOY_PATH"
    fi
    
    local DEPLOY_END=$(date +%s)
    local DEPLOY_TIME=$((DEPLOY_END - DEPLOY_START))
    log_metric "Deploy time: ${DEPLOY_TIME}s"
    
    # Add to metrics
    METRICS=$(echo "$METRICS" | jq ".steps += [{\"name\":\"deploy\",\"duration\":$DEPLOY_TIME,\"status\":\"success\"}]")
}

# Restart Lidarr service
restart_lidarr() {
    log_info "🔄 Restarting Lidarr service"
    
    if [ "$DRY_RUN" == "true" ]; then
        log_warning "⚠️ DRY RUN - not actually restarting"
        return 0
    fi
    
    # Try different restart methods
    if systemctl is-active --quiet lidarr; then
        if sudo systemctl restart lidarr; then
            log_success "✅ Lidarr restarted via systemctl"
        else
            log_error "❌ Failed to restart via systemctl"
            return 1
        fi
    elif command -v service &> /dev/null; then
        if sudo service lidarr restart; then
            log_success "✅ Lidarr restarted via service"
        else
            log_error "❌ Failed to restart via service"
            return 1
        fi
    else
        log_warning "⚠️ Unable to restart Lidarr automatically"
        log_info "Please restart Lidarr manually"
    fi
    
    # Wait for service to come up
    log_info "Waiting for Lidarr to start..."
    sleep $HEALTH_CHECK_DELAY
}

# Health check
health_check() {
    log_info "🏥 Running health checks"
    
    local RETRY_COUNT=0
    local HEALTH_STATUS="unknown"
    
    while [ $RETRY_COUNT -lt $HEALTH_CHECK_RETRIES ]; do
        RETRY_COUNT=$((RETRY_COUNT + 1))
        log_info "Health check attempt $RETRY_COUNT/$HEALTH_CHECK_RETRIES"
        
        # Check if Lidarr is responding
        if curl -sf -o /dev/null "$LIDARR_URL/ping"; then
            log_success "✅ Lidarr is responding"
            
            # Check if plugin is loaded (if API key is available)
            if [ -n "$API_KEY" ]; then
                INDEXERS=$(curl -sf -H "X-Api-Key: $API_KEY" "$LIDARR_URL/api/v1/indexer" || echo "[]")
                
                if echo "$INDEXERS" | grep -q "Qobuz"; then
                    log_success "✅ Qobuzarr plugin is loaded"
                    HEALTH_STATUS="healthy"
                    break
                else
                    log_warning "⚠️ Qobuzarr plugin not detected in indexers"
                fi
            else
                log_warning "⚠️ No API key - skipping plugin verification"
                HEALTH_STATUS="partial"
                break
            fi
        else
            log_warning "⚠️ Lidarr not responding yet"
        fi
        
        if [ $RETRY_COUNT -lt $HEALTH_CHECK_RETRIES ]; then
            log_info "Waiting ${HEALTH_CHECK_DELAY}s before retry..."
            sleep $HEALTH_CHECK_DELAY
        fi
    done
    
    if [ "$HEALTH_STATUS" == "healthy" ]; then
        log_success "✅ All health checks passed!"
        return 0
    elif [ "$HEALTH_STATUS" == "partial" ]; then
        log_warning "⚠️ Partial health check success"
        return 0
    else
        log_error "❌ Health checks failed after $HEALTH_CHECK_RETRIES attempts!"
        return 1
    fi
}

# Rollback deployment
rollback() {
    log_error "🔄 Initiating rollback"
    
    if [ ! -f "$BACKUP_PATH/.last-backup" ]; then
        log_error "❌ No backup found for rollback!"
        return 1
    fi
    
    local LAST_BACKUP=$(cat "$BACKUP_PATH/.last-backup")
    
    if [ ! -d "$LAST_BACKUP" ]; then
        log_error "❌ Backup directory not found: $LAST_BACKUP"
        return 1
    fi
    
    log_info "Rolling back to: $LAST_BACKUP"
    
    # Restore backup
    rm -rf "$DEPLOY_PATH"
    if cp -r "$LAST_BACKUP" "$DEPLOY_PATH"; then
        log_success "✅ Rollback completed"
        
        # Restart Lidarr with rolled back version
        restart_lidarr
        
        # Add rollback to metrics
        METRICS=$(echo "$METRICS" | jq ".rollback = true")
    else
        log_error "❌ Rollback failed!"
        return 1
    fi
}

# Finalize deployment
finalize() {
    local DEPLOY_END=$(date +%s)
    local TOTAL_TIME=$((DEPLOY_END - DEPLOY_START))
    
    log_metric "Total deployment time: ${TOTAL_TIME}s"
    
    # Update final metrics
    METRICS=$(echo "$METRICS" | jq ".end_time = \"$(date -Iseconds)\" | .total_duration = $TOTAL_TIME | .status = \"$1\"")
    
    # Save metrics
    echo "$METRICS" > "$METRICS_FILE"
    log_info "📊 Metrics saved to: $METRICS_FILE"
    
    # Generate summary
    log_info ""
    log_info "════════════════════════════════════════"
    log_info "📋 Deployment Summary"
    log_info "════════════════════════════════════════"
    log_info "Environment: $ENVIRONMENT"
    log_info "Status: $1"
    log_info "Duration: ${TOTAL_TIME}s"
    log_info "Deploy Path: $DEPLOY_PATH"
    
    if [ "$1" == "success" ]; then
        log_success "🎉 Deployment completed successfully!"
    else
        log_error "❌ Deployment failed!"
    fi
}

# Main deployment flow
main() {
    init_deployment
    load_config
    
    # Pre-deployment phase
    if ! build_plugin; then
        finalize "build_failed"
        exit 1
    fi
    
    if ! run_tests; then
        finalize "test_failed"
        exit 1
    fi
    
    if ! create_backup; then
        log_warning "⚠️ Proceeding without backup"
    fi
    
    # Deployment phase
    if ! deploy_plugin; then
        finalize "deploy_failed"
        exit 1
    fi
    
    if ! restart_lidarr; then
        log_warning "⚠️ Manual Lidarr restart required"
    fi
    
    # Post-deployment phase
    if ! health_check; then
        log_error "❌ Health check failed - initiating rollback"
        if rollback; then
            finalize "rolled_back"
            exit 1
        else
            finalize "rollback_failed"
            exit 2
        fi
    fi
    
    # Clean up old deployment
    if [ -d "$DEPLOY_PATH.old" ]; then
        rm -rf "$DEPLOY_PATH.old"
    fi
    
    finalize "success"
}

# Run main deployment
main "$@"