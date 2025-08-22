#!/bin/bash
# =============================================================================
# Qobuzarr Deployment Manager
# =============================================================================
# Advanced deployment with health checks, rollback, and monitoring
#
# Features:
# - Zero-downtime deployments using blue-green strategy
# - Automatic rollback on failure
# - Health check validation
# - Deployment metrics collection
# - Multi-environment support

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
WHITE='\033[1;37m'
NC='\033[0m'

# Configuration
DEPLOYMENT_VERSION="${1:-latest}"
ENVIRONMENT="${2:-test}"
ROLLBACK_ON_FAILURE="${ROLLBACK_ON_FAILURE:-true}"
HEALTH_CHECK_RETRIES=5
HEALTH_CHECK_INTERVAL=10
DEPLOYMENT_TIMEOUT=300

# Environment-specific settings
case "$ENVIRONMENT" in
    test)
        LIDARR_HOST="${LIDARR_TEST_HOST:-localhost}"
        LIDARR_PORT="${LIDARR_TEST_PORT:-8686}"
        PLUGIN_DIR="${LIDARR_TEST_PLUGIN_DIR:-/lidarr/plugins/Qobuzarr}"
        LIDARR_SERVICE="${LIDARR_TEST_SERVICE:-lidarr-test}"
        ;;
    staging)
        LIDARR_HOST="${LIDARR_STAGING_HOST:-staging.lidarr.local}"
        LIDARR_PORT="${LIDARR_STAGING_PORT:-8686}"
        PLUGIN_DIR="${LIDARR_STAGING_PLUGIN_DIR:-/opt/lidarr/plugins/Qobuzarr}"
        LIDARR_SERVICE="${LIDARR_STAGING_SERVICE:-lidarr-staging}"
        ;;
    production)
        LIDARR_HOST="${LIDARR_PROD_HOST:-lidarr.local}"
        LIDARR_PORT="${LIDARR_PROD_PORT:-8686}"
        PLUGIN_DIR="${LIDARR_PROD_PLUGIN_DIR:-/opt/lidarr/plugins/Qobuzarr}"
        LIDARR_SERVICE="${LIDARR_PROD_SERVICE:-lidarr}"
        ;;
    *)
        echo -e "${RED}❌ Unknown environment: $ENVIRONMENT${NC}"
        exit 1
        ;;
esac

# Logging functions
log_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

log_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

log_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Metrics collection
METRICS_FILE="/tmp/qobuzarr-deployment-metrics.json"
DEPLOYMENT_START=$(date +%s)

record_metric() {
    local metric_name="$1"
    local metric_value="$2"
    local timestamp=$(date -Iseconds)
    
    if [ ! -f "$METRICS_FILE" ]; then
        echo "{}" > "$METRICS_FILE"
    fi
    
    jq --arg name "$metric_name" \
       --arg value "$metric_value" \
       --arg time "$timestamp" \
       --arg env "$ENVIRONMENT" \
       '.[$name] = {value: $value, timestamp: $time, environment: $env}' \
       "$METRICS_FILE" > "$METRICS_FILE.tmp" && mv "$METRICS_FILE.tmp" "$METRICS_FILE"
}

# Health check function
health_check() {
    local attempt=1
    
    while [ $attempt -le $HEALTH_CHECK_RETRIES ]; do
        log_info "Health check attempt $attempt/$HEALTH_CHECK_RETRIES..."
        
        # Check Lidarr is responding
        if curl -sf "http://$LIDARR_HOST:$LIDARR_PORT/ping" > /dev/null 2>&1; then
            # Check plugin is loaded
            local indexers=$(curl -sf "http://$LIDARR_HOST:$LIDARR_PORT/api/v1/indexer" 2>/dev/null || echo "[]")
            
            if echo "$indexers" | grep -q "Qobuzarr"; then
                log_success "Health check passed!"
                record_metric "health_check_attempts" "$attempt"
                return 0
            else
                log_warning "Plugin not found in indexers"
            fi
        else
            log_warning "Lidarr not responding"
        fi
        
        if [ $attempt -lt $HEALTH_CHECK_RETRIES ]; then
            log_info "Waiting $HEALTH_CHECK_INTERVAL seconds before retry..."
            sleep $HEALTH_CHECK_INTERVAL
        fi
        
        attempt=$((attempt + 1))
    done
    
    log_error "Health check failed after $HEALTH_CHECK_RETRIES attempts"
    record_metric "health_check_failed" "true"
    return 1
}

# Backup current deployment
backup_current() {
    local backup_dir="${PLUGIN_DIR}.backup.$(date +%Y%m%d_%H%M%S)"
    
    if [ -d "$PLUGIN_DIR" ]; then
        log_info "Backing up current deployment to $backup_dir"
        cp -r "$PLUGIN_DIR" "$backup_dir"
        
        # Keep only last 5 backups
        ls -dt "${PLUGIN_DIR}.backup."* 2>/dev/null | tail -n +6 | xargs rm -rf 2>/dev/null || true
        
        log_success "Backup completed"
        record_metric "backup_created" "$backup_dir"
        echo "$backup_dir"
    else
        log_warning "No existing deployment to backup"
        echo ""
    fi
}

# Deploy new version
deploy_new_version() {
    local source_dir="$1"
    local staging_dir="${PLUGIN_DIR}.new"
    
    log_info "Deploying version $DEPLOYMENT_VERSION to $ENVIRONMENT"
    
    # Create staging directory
    rm -rf "$staging_dir"
    mkdir -p "$staging_dir"
    
    # Copy files to staging
    log_info "Copying files to staging directory..."
    cp -r "$source_dir"/* "$staging_dir/"
    
    # Validate deployment files
    if [ ! -f "$staging_dir/Lidarr.Plugin.Qobuzarr.dll" ]; then
        log_error "Main plugin DLL not found in deployment"
        return 1
    fi
    
    if [ ! -f "$staging_dir/plugin.json" ]; then
        log_error "plugin.json not found in deployment"
        return 1
    fi
    
    # Atomic swap (blue-green deployment)
    log_info "Performing atomic deployment swap..."
    
    if [ -d "$PLUGIN_DIR" ]; then
        mv "$PLUGIN_DIR" "${PLUGIN_DIR}.old"
    fi
    
    mv "$staging_dir" "$PLUGIN_DIR"
    
    log_success "Files deployed successfully"
    record_metric "deployment_method" "blue_green"
    
    # Clean up old deployment
    if [ -d "${PLUGIN_DIR}.old" ]; then
        rm -rf "${PLUGIN_DIR}.old"
    fi
    
    return 0
}

# Restart Lidarr service
restart_lidarr() {
    log_info "Restarting Lidarr service: $LIDARR_SERVICE"
    
    # Try systemctl first, then docker
    if command -v systemctl > /dev/null 2>&1; then
        sudo systemctl restart "$LIDARR_SERVICE" || return 1
    elif command -v docker > /dev/null 2>&1; then
        docker restart "$LIDARR_SERVICE" || return 1
    else
        log_error "Cannot restart Lidarr - no systemctl or docker found"
        return 1
    fi
    
    log_success "Lidarr service restarted"
    sleep 5  # Give Lidarr time to start
    return 0
}

# Rollback deployment
rollback() {
    local backup_dir="$1"
    
    log_warning "Initiating rollback..."
    record_metric "rollback_initiated" "true"
    
    if [ -z "$backup_dir" ] || [ ! -d "$backup_dir" ]; then
        log_error "No backup available for rollback"
        return 1
    fi
    
    # Remove failed deployment
    if [ -d "$PLUGIN_DIR" ]; then
        log_info "Removing failed deployment..."
        rm -rf "${PLUGIN_DIR}.failed"
        mv "$PLUGIN_DIR" "${PLUGIN_DIR}.failed"
    fi
    
    # Restore backup
    log_info "Restoring from backup: $backup_dir"
    cp -r "$backup_dir" "$PLUGIN_DIR"
    
    # Restart Lidarr
    restart_lidarr
    
    # Verify rollback
    if health_check; then
        log_success "Rollback completed successfully"
        record_metric "rollback_success" "true"
        
        # Archive failed deployment for investigation
        if [ -d "${PLUGIN_DIR}.failed" ]; then
            tar -czf "${PLUGIN_DIR}.failed.$(date +%Y%m%d_%H%M%S).tar.gz" "${PLUGIN_DIR}.failed"
            rm -rf "${PLUGIN_DIR}.failed"
        fi
        
        return 0
    else
        log_error "Rollback failed - manual intervention required"
        record_metric "rollback_failed" "true"
        return 1
    fi
}

# Send deployment notification
send_notification() {
    local status="$1"
    local message="$2"
    
    # Send to monitoring endpoint if configured
    if [ -n "$MONITORING_WEBHOOK" ]; then
        curl -X POST "$MONITORING_WEBHOOK" \
            -H "Content-Type: application/json" \
            -d "{
                \"environment\": \"$ENVIRONMENT\",
                \"version\": \"$DEPLOYMENT_VERSION\",
                \"status\": \"$status\",
                \"message\": \"$message\",
                \"timestamp\": \"$(date -Iseconds)\",
                \"duration\": $(($(date +%s) - DEPLOYMENT_START))
            }" 2>/dev/null || true
    fi
    
    # Log to system journal if available
    if command -v logger > /dev/null 2>&1; then
        logger -t "qobuzarr-deploy" -p "user.info" "$status: $message"
    fi
}

# Main deployment workflow
main() {
    log_info "=== Qobuzarr Deployment Manager ==="
    log_info "Version: $DEPLOYMENT_VERSION"
    log_info "Environment: $ENVIRONMENT"
    log_info "Plugin Directory: $PLUGIN_DIR"
    
    record_metric "deployment_started" "$(date -Iseconds)"
    record_metric "deployment_version" "$DEPLOYMENT_VERSION"
    
    # Step 1: Pre-deployment health check
    log_info "Performing pre-deployment health check..."
    local pre_deployment_healthy=true
    if ! health_check; then
        log_warning "Pre-deployment health check failed - continuing anyway"
        pre_deployment_healthy=false
        record_metric "pre_deployment_healthy" "false"
    fi
    
    # Step 2: Backup current deployment
    local backup_dir=$(backup_current)
    
    # Step 3: Deploy new version
    if [ ! -d "bin" ]; then
        log_error "Deployment source directory 'bin' not found"
        exit 1
    fi
    
    if ! deploy_new_version "bin"; then
        log_error "Deployment failed"
        send_notification "FAILED" "Failed to deploy files"
        exit 1
    fi
    
    # Step 4: Restart Lidarr
    if ! restart_lidarr; then
        log_error "Failed to restart Lidarr"
        if [ "$ROLLBACK_ON_FAILURE" = "true" ] && [ -n "$backup_dir" ]; then
            rollback "$backup_dir"
        fi
        send_notification "FAILED" "Failed to restart Lidarr"
        exit 1
    fi
    
    # Step 5: Post-deployment health check
    log_info "Performing post-deployment health check..."
    if ! health_check; then
        log_error "Post-deployment health check failed"
        
        if [ "$ROLLBACK_ON_FAILURE" = "true" ] && [ -n "$backup_dir" ]; then
            if rollback "$backup_dir"; then
                send_notification "ROLLED_BACK" "Deployment failed, successfully rolled back"
                exit 1
            else
                send_notification "CRITICAL" "Deployment failed and rollback failed"
                exit 2
            fi
        else
            send_notification "FAILED" "Post-deployment health check failed"
            exit 1
        fi
    fi
    
    # Step 6: Cleanup and finalize
    local deployment_duration=$(($(date +%s) - DEPLOYMENT_START))
    record_metric "deployment_duration" "$deployment_duration"
    record_metric "deployment_completed" "$(date -Iseconds)"
    record_metric "deployment_success" "true"
    
    log_success "=== Deployment Completed Successfully ==="
    log_info "Duration: ${deployment_duration}s"
    log_info "Version $DEPLOYMENT_VERSION deployed to $ENVIRONMENT"
    
    send_notification "SUCCESS" "Version $DEPLOYMENT_VERSION deployed in ${deployment_duration}s"
    
    # Output metrics for CI/CD pipeline
    if [ -f "$METRICS_FILE" ]; then
        echo "::set-output name=metrics::$(cat $METRICS_FILE | jq -c .)"
    fi
}

# Run main deployment
main "$@"