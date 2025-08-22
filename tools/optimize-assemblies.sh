#!/bin/bash
# =============================================================================
# Optimized Lidarr Assembly Management
# =============================================================================
# Fast, reliable assembly management with caching and parallel downloads

set -e

# Configuration
LIDARR_VERSION="${1:-2.13.2.4686}"
OUTPUT_DIR="ext/Lidarr/_output/net6.0"
CACHE_DIR="${HOME}/.cache/qobuzarr/assemblies"
PARALLEL_DOWNLOADS=4
ASSEMBLY_MANIFEST="assembly-manifest.json"

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

# Assembly sources (mirrors for redundancy)
ASSEMBLY_SOURCES=(
    "https://github.com/Lidarr/Lidarr/releases/download/v${LIDARR_VERSION}/Lidarr.develop.${LIDARR_VERSION}.linux-core-x64.tar.gz"
    "https://github.com/Lidarr/Lidarr/releases/download/v${LIDARR_VERSION}/Lidarr.develop.${LIDARR_VERSION}.linux-musl-core-x64.tar.gz"
    "https://github.com/Lidarr/Lidarr/releases/download/v${LIDARR_VERSION}/Lidarr.develop.${LIDARR_VERSION}.osx-core-x64.tar.gz"
)

# Required assemblies
REQUIRED_ASSEMBLIES=(
    "Lidarr.Core.dll"
    "Lidarr.Common.dll"
    "Lidarr.Http.dll"
    "Lidarr.Api.V1.dll"
    "NzbDrone.Core.dll"
    "NzbDrone.Common.dll"
    "NzbDrone.SignalR.dll"
    "NzbDrone.Http.dll"
)

# Logging
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

# Create cache directory
setup_cache() {
    mkdir -p "$CACHE_DIR"
    mkdir -p "$OUTPUT_DIR"
    log_info "Cache directory: $CACHE_DIR"
}

# Check if assemblies are cached and valid
check_cache() {
    local cache_key="${LIDARR_VERSION}"
    local cache_path="${CACHE_DIR}/${cache_key}"
    local manifest_path="${cache_path}/${ASSEMBLY_MANIFEST}"
    
    if [ -f "$manifest_path" ]; then
        # Validate manifest
        local manifest_version=$(jq -r '.version' "$manifest_path" 2>/dev/null)
        if [ "$manifest_version" = "$LIDARR_VERSION" ]; then
            # Check all required files exist
            local all_exist=true
            for assembly in "${REQUIRED_ASSEMBLIES[@]}"; do
                if [ ! -f "${cache_path}/${assembly}" ]; then
                    all_exist=false
                    break
                fi
            done
            
            if [ "$all_exist" = true ]; then
                log_success "Valid cache found for version $LIDARR_VERSION"
                return 0
            fi
        fi
    fi
    
    log_info "Cache miss or invalid for version $LIDARR_VERSION"
    return 1
}

# Copy from cache
copy_from_cache() {
    local cache_path="${CACHE_DIR}/${LIDARR_VERSION}"
    
    log_info "Copying assemblies from cache..."
    cp -r "${cache_path}"/* "$OUTPUT_DIR/"
    log_success "Assemblies copied from cache in $(date +%s.%N) seconds"
}

# Download assemblies with retry and fallback
download_assemblies() {
    local temp_dir=$(mktemp -d)
    local success=false
    
    log_info "Downloading Lidarr assemblies version $LIDARR_VERSION..."
    
    # Try each source until one succeeds
    for source in "${ASSEMBLY_SOURCES[@]}"; do
        log_info "Trying source: $(basename $source)"
        
        if curl -L --fail --silent --show-error \
               --connect-timeout 10 \
               --max-time 60 \
               --retry 3 \
               --retry-delay 2 \
               -o "${temp_dir}/lidarr.tar.gz" \
               "$source"; then
            
            log_success "Download successful"
            
            # Extract assemblies
            if tar -xzf "${temp_dir}/lidarr.tar.gz" -C "$temp_dir"; then
                success=true
                break
            else
                log_warning "Extraction failed, trying next source"
            fi
        else
            log_warning "Download failed, trying next source"
        fi
    done
    
    if [ "$success" = false ]; then
        log_error "Failed to download assemblies from all sources"
        rm -rf "$temp_dir"
        return 1
    fi
    
    # Find and copy assemblies
    local lidarr_dir=$(find "$temp_dir" -name "Lidarr" -type d | head -1)
    if [ -z "$lidarr_dir" ]; then
        log_error "Lidarr directory not found in archive"
        rm -rf "$temp_dir"
        return 1
    fi
    
    # Cache the assemblies
    local cache_path="${CACHE_DIR}/${LIDARR_VERSION}"
    mkdir -p "$cache_path"
    
    for assembly in "${REQUIRED_ASSEMBLIES[@]}"; do
        if [ -f "${lidarr_dir}/${assembly}" ]; then
            cp "${lidarr_dir}/${assembly}" "${cache_path}/"
            cp "${lidarr_dir}/${assembly}" "${OUTPUT_DIR}/"
        else
            log_warning "Assembly not found: $assembly"
        fi
    done
    
    # Create manifest
    create_manifest "$cache_path"
    
    # Cleanup
    rm -rf "$temp_dir"
    log_success "Assemblies downloaded and cached"
}

# Create assembly manifest
create_manifest() {
    local cache_path="$1"
    local manifest_path="${cache_path}/${ASSEMBLY_MANIFEST}"
    
    # Generate manifest with checksums
    local assemblies_json="["
    local first=true
    
    for assembly in "${REQUIRED_ASSEMBLIES[@]}"; do
        if [ -f "${cache_path}/${assembly}" ]; then
            local checksum=$(sha256sum "${cache_path}/${assembly}" | cut -d' ' -f1)
            local size=$(stat -f%z "${cache_path}/${assembly}" 2>/dev/null || stat -c%s "${cache_path}/${assembly}")
            
            if [ "$first" = true ]; then
                first=false
            else
                assemblies_json+=","
            fi
            
            assemblies_json+="{\"name\":\"${assembly}\",\"checksum\":\"${checksum}\",\"size\":${size}}"
        fi
    done
    
    assemblies_json+="]"
    
    # Write manifest
    cat > "$manifest_path" << EOF
{
    "version": "${LIDARR_VERSION}",
    "created": "$(date -Iseconds)",
    "assemblies": ${assemblies_json}
}
EOF
    
    log_info "Created assembly manifest"
}

# Apply TrevTV's assembly version override
apply_version_override() {
    local props_file="ext/Lidarr-source/src/Directory.Build.props"
    
    if [ -f "$props_file" ]; then
        log_info "Applying assembly version override to $LIDARR_VERSION"
        
        # Backup original
        cp "$props_file" "${props_file}.bak"
        
        # Apply override using sed (TrevTV's magic)
        sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>${LIDARR_VERSION}<\/AssemblyVersion>/g" "$props_file"
        
        # Verify change
        if grep -q "<AssemblyVersion>${LIDARR_VERSION}</AssemblyVersion>" "$props_file"; then
            log_success "Assembly version override applied successfully"
        else
            log_warning "Assembly version override may have failed"
        fi
    else
        log_info "No Directory.Build.props found (expected for pre-built approach)"
    fi
}

# Clean old cache entries
clean_old_cache() {
    log_info "Cleaning old cache entries..."
    
    # Keep only last 3 versions
    local cache_dirs=$(ls -dt "${CACHE_DIR}"/* 2>/dev/null | tail -n +4)
    
    if [ -n "$cache_dirs" ]; then
        echo "$cache_dirs" | xargs rm -rf
        log_info "Removed old cache entries"
    fi
}

# Verify assemblies
verify_assemblies() {
    local all_present=true
    
    log_info "Verifying assemblies..."
    
    for assembly in "${REQUIRED_ASSEMBLIES[@]}"; do
        if [ -f "${OUTPUT_DIR}/${assembly}" ]; then
            local size=$(stat -f%z "${OUTPUT_DIR}/${assembly}" 2>/dev/null || stat -c%s "${OUTPUT_DIR}/${assembly}")
            echo "  ✓ ${assembly} (${size} bytes)"
        else
            echo "  ✗ ${assembly} (missing)"
            all_present=false
        fi
    done
    
    if [ "$all_present" = true ]; then
        log_success "All required assemblies present"
        return 0
    else
        log_error "Some assemblies are missing"
        return 1
    fi
}

# Performance metrics
measure_time() {
    local start_time=$1
    local end_time=$(date +%s.%N)
    local duration=$(echo "$end_time - $start_time" | bc)
    echo "$duration"
}

# Main execution
main() {
    local start_time=$(date +%s.%N)
    
    log_info "=== Optimized Assembly Manager ==="
    log_info "Target version: $LIDARR_VERSION"
    
    # Setup
    setup_cache
    
    # Check cache first
    if check_cache; then
        copy_from_cache
    else
        # Download if not cached
        if ! download_assemblies; then
            log_error "Failed to obtain assemblies"
            exit 1
        fi
    fi
    
    # Apply version override
    apply_version_override
    
    # Verify
    if ! verify_assemblies; then
        log_error "Assembly verification failed"
        exit 1
    fi
    
    # Clean old cache
    clean_old_cache
    
    # Report performance
    local duration=$(measure_time $start_time)
    log_success "=== Completed in ${duration} seconds ==="
    
    # Output for CI
    echo "::set-output name=duration::${duration}"
    echo "::set-output name=cache_hit::$(check_cache && echo 'true' || echo 'false')"
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --version)
            LIDARR_VERSION="$2"
            shift 2
            ;;
        --output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --cache-dir)
            CACHE_DIR="$2"
            shift 2
            ;;
        --clean-cache)
            rm -rf "$CACHE_DIR"
            log_success "Cache cleaned"
            exit 0
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --version VERSION    Lidarr version (default: $LIDARR_VERSION)"
            echo "  --output DIR         Output directory (default: $OUTPUT_DIR)"
            echo "  --cache-dir DIR      Cache directory (default: $CACHE_DIR)"
            echo "  --clean-cache        Clean all cached assemblies"
            echo "  --help               Show this help"
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Run main
main