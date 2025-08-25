#!/bin/bash
# =============================================================================
# Qobuzarr Migration Execution Script (Bash)
# =============================================================================
# Executes service migration with checkpoints and rollback capability

set -e

# Default values
CREATE_BACKUP=false
START_FROM_STEP=""
SKIP_BUILD=false
SKIP_TESTS=false
STOP_ON_FAILURE=true
VERBOSE=false
DRY_RUN=false
BACKUP_NAME=""
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
    echo -e "${GREEN}🚀 Qobuzarr Migration Execution Script${NC}"
    echo ""
    echo -e "${CYAN}USAGE:${NC}"
    echo -e "  ${WHITE}./scripts/execute-migration.sh [Options]${NC}"
    echo ""
    echo -e "${CYAN}OPTIONS:${NC}"
    echo -e "  ${WHITE}--create-backup           Create backup checkpoint before migration${NC}"
    echo -e "  ${WHITE}--start-from-step [step]  Resume migration from specific step${NC}"
    echo -e "  ${WHITE}--skip-build              Skip build validation${NC}"
    echo -e "  ${WHITE}--skip-tests              Skip test execution${NC}"
    echo -e "  ${WHITE}--stop-on-failure         Stop migration on first failure (default)${NC}"
    echo -e "  ${WHITE}--verbose                 Show detailed execution output${NC}"
    echo -e "  ${WHITE}--dry-run                 Execute in dry run mode (no changes)${NC}"
    echo -e "  ${WHITE}--backup-name [name]      Custom name for backup checkpoint${NC}"
    echo -e "  ${WHITE}--help                    Show this help${NC}"
    echo ""
    echo -e "${CYAN}EXAMPLES:${NC}"
    echo -e "  ${GRAY}./scripts/execute-migration.sh                               # Basic migration${NC}"
    echo -e "  ${GRAY}./scripts/execute-migration.sh --create-backup               # Safe migration with backup${NC}"
    echo -e "  ${GRAY}./scripts/execute-migration.sh --start-from-step migrate-api # Resume from step${NC}"
    echo -e "  ${GRAY}./scripts/execute-migration.sh --dry-run --verbose           # Test run with details${NC}"
    echo ""
    echo -e "${CYAN}SAFETY:${NC}"
    echo -e "  ${YELLOW}⚠️ This script modifies source files - use --create-backup for safety${NC}"
    echo -e "  ${WHITE}🔄 Use ./scripts/rollback.sh if migration needs to be reverted${NC}"
    echo ""
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --create-backup)
            CREATE_BACKUP=true
            shift
            ;;
        --start-from-step)
            START_FROM_STEP="$2"
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
        --stop-on-failure)
            STOP_ON_FAILURE=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --backup-name)
            BACKUP_NAME="$2"
            shift 2
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

echo -e "${GREEN}🚀 Qobuzarr Migration Execution${NC}"
echo -e "${GREEN}===============================${NC}"

if [ "$DRY_RUN" = true ]; then
    echo -e "${BLUE}🧪 DRY RUN MODE - No changes will be made${NC}"
fi

# Initialize execution tracking
EXECUTION_START=$(date +%s)
SUCCESS=true
declare -a EXECUTED_STEPS=()
declare -a ERRORS=()
BACKUP_CREATED=false
BACKUP_CHECKPOINT=""

# Phase 1: Prerequisites and validation
echo ""
echo -e "${BLUE}📋 Phase 1: Prerequisites Validation${NC}"

echo -e "${WHITE}🔍 Running migration dry-run analysis...${NC}"

DRY_RUN_CMD="./scripts/dry-run.sh"
if [ "$VERBOSE" = true ]; then
    DRY_RUN_CMD="$DRY_RUN_CMD --verbose"
fi
if [ -n "$START_FROM_STEP" ]; then
    DRY_RUN_CMD="$DRY_RUN_CMD --start-from-step $START_FROM_STEP"
fi

if ! $DRY_RUN_CMD; then
    echo -e "${RED}❌ Dry run analysis failed - resolve blocking issues before migration${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Prerequisites validation passed${NC}"

# Phase 2: Create backup checkpoint
if [ "$CREATE_BACKUP" = true ] && [ "$DRY_RUN" = false ]; then
    echo ""
    echo -e "${BLUE}💾 Phase 2: Creating Backup Checkpoint${NC}"
    
    if [ -z "$BACKUP_NAME" ]; then
        BACKUP_NAME="pre-migration-$(date +%Y%m%d-%H%M%S)"
    fi
    
    echo -e "${WHITE}📸 Creating checkpoint: $BACKUP_NAME...${NC}"
    
    # Create backup directory structure
    BACKUP_DIR=".migration-checkpoints/$BACKUP_NAME"
    if mkdir -p "$BACKUP_DIR"; then
        # Backup critical files
        CRITICAL_FILES=(
            "src/Services/LidarrAlbumRetriever.cs"
            "src/Services/QobuzValidationService.cs"
            "src/Core/QobuzApiService.cs"
            "src/Services/QobuzQualityService.cs"
            "src/Services/QualityMappingService.cs"
            "src/Services/QualityFallbackService.cs"
        )
        
        BACKED_UP_COUNT=0
        for file in "${CRITICAL_FILES[@]}"; do
            if [ -f "$file" ]; then
                cp "$file" "$BACKUP_DIR/$(basename "$file")"
                ((BACKED_UP_COUNT++))
            fi
        done
        
        # Create backup manifest
        cat > "$BACKUP_DIR/backup-manifest.json" << EOF
{
  "backupName": "$BACKUP_NAME",
  "createdAt": "$(date -Iseconds)",
  "fileCount": $BACKED_UP_COUNT,
  "projectRoot": "$(pwd)",
  "migrationVersion": "2.0.0"
}
EOF
        
        echo -e "${GREEN}✅ Backup created: $BACKED_UP_COUNT files backed up${NC}"
        BACKUP_CREATED=true
        BACKUP_CHECKPOINT="$BACKUP_NAME"
    else
        echo -e "${RED}❌ Failed to create backup directory${NC}"
        ERRORS+=("Backup creation failed: Could not create backup directory")
        
        echo ""
        echo -e "${YELLOW}⚠️ Continue migration without backup? [y/N]: ${NC}"
        read -r response
        
        if [ "$response" != "y" ] && [ "$response" != "Y" ]; then
            echo -e "${RED}Migration aborted by user - backup creation failed${NC}"
            exit 1
        fi
    fi
fi

# Phase 3: Execute migration steps
echo ""
echo -e "${BLUE}🔄 Phase 3: Migration Execution${NC}"

# Define migration step functions
execute_lidarr_album_retriever_migration() {
    local file="src/Services/LidarrAlbumRetriever.cs"
    
    if [ ! -f "$file" ]; then
        echo "File not found: $file"
        return 1
    fi
    
    if [ "$VERBOSE" = true ]; then
        echo -e "${GRAY}   📄 Processing: $file${NC}"
    fi
    
    if [ "$DRY_RUN" = false ]; then
        # Create temporary file for processing
        local temp_file=$(mktemp)
        
        # Process the file with sed
        sed -e 's/IQualityMappingService[[:space:]]\+[[:alnum:]_]\+,\?[[:space:]]*//' \
            -e 's/IQualityFallbackService[[:space:]]\+[[:alnum:]_]\+,\?[[:space:]]*//' \
            -e 's/\([[:space:]]\+\)\(IQobuzLogger[[:space:]]\+[[:alnum:]_]\+\)/\1IQobuzQualityManager qualityManager,\1\2/' \
            -e 's/private readonly IQualityMappingService [[:alnum:]_]\+;//' \
            -e 's/private readonly IQualityFallbackService [[:alnum:]_]\+;//' \
            -e 's/\(private readonly IQobuzLogger [[:alnum:]_]\+;\)/private readonly IQobuzQualityManager _qualityManager;\1/' \
            -e 's/_[[:alnum:]_]*[[:space:]]*=[[:space:]]*[[:alnum:]_]*MappingService;//' \
            -e 's/_[[:alnum:]_]*[[:space:]]*=[[:space:]]*[[:alnum:]_]*FallbackService;//' \
            -e 's/\(_logger = logger;\)/_qualityManager = qualityManager;\1/' \
            -e 's/_qualityMappingService\.GetQualityRecommendation/_qualityManager.MapLidarrQuality/g' \
            -e 's/_qualityFallbackService\.SelectBestAvailableQuality/_qualityManager.SelectBestQualityAsync/g' \
            -e 's/_qualityFallbackService\.GetFallbackChain/_qualityManager.GetQualityFallbackChain/g' \
            "$file" > "$temp_file"
        
        # Replace original file
        mv "$temp_file" "$file"
    fi
    
    echo "LidarrAlbumRetriever migration completed"
    return 0
}

execute_qobuz_validation_migration() {
    local file="src/Services/QobuzValidationService.cs"
    
    if [ ! -f "$file" ]; then
        echo "File not found: $file"
        return 1
    fi
    
    if [ "$VERBOSE" = true ]; then
        echo -e "${GRAY}   📄 Processing: $file${NC}"
    fi
    
    if [ "$DRY_RUN" = false ]; then
        # Replace QobuzQualityService references
        sed -i 's/QobuzQualityService/IQobuzQualityManager/g' "$file"
        sed -i 's/_qualityService\.ValidateQuality/_qualityManager.DetectAvailableQualitiesAsync/g' "$file"
        sed -i 's/_qualityService\.GetAvailableQualities/_qualityManager.DetectAvailableQualitiesAsync/g' "$file"
    fi
    
    echo "QobuzValidationService migration completed"
    return 0
}

execute_qobuz_api_migration() {
    local file="src/Core/QobuzApiService.cs"
    
    if [ ! -f "$file" ]; then
        echo "File not found: $file"
        return 1
    fi
    
    if [ "$VERBOSE" = true ]; then
        echo -e "${GRAY}   📄 Processing: $file${NC}"
    fi
    
    if [ "$DRY_RUN" = false ]; then
        # Replace QualityMappingService references
        sed -i 's/QualityMappingService/IQobuzQualityManager/g' "$file"
        sed -i 's/_qualityMappingService\.MapQuality/_qualityManager.MapLidarrQuality/g' "$file"
    fi
    
    echo "QobuzApiService migration completed"
    return 0
}

# Define migration steps array
declare -A MIGRATION_STEPS
MIGRATION_STEPS["migrate-lidarr-album-retriever"]="Migrate LidarrAlbumRetriever to IQobuzQualityManager|2A|execute_lidarr_album_retriever_migration"
MIGRATION_STEPS["migrate-qobuz-validation-service"]="Migrate QobuzValidationService to consolidated services|2A|execute_qobuz_validation_migration"
MIGRATION_STEPS["migrate-qobuz-api-service"]="Migrate QobuzApiService quality mappings|2A|execute_qobuz_api_migration"

# Execute migration steps
FOUND_START_STEP=false
for step_id in "${!MIGRATION_STEPS[@]}"; do
    IFS='|' read -r description phase function_name <<< "${MIGRATION_STEPS[$step_id]}"
    
    # Skip steps until we reach the start step
    if [ -n "$START_FROM_STEP" ] && [ "$FOUND_START_STEP" = false ]; then
        if [ "$step_id" = "$START_FROM_STEP" ]; then
            FOUND_START_STEP=true
        else
            continue
        fi
    fi
    
    echo ""
    echo -e "${WHITE}🔄 Executing step: $step_id${NC}"
    echo -e "${GRAY}   Description: $description${NC}"
    echo -e "${GRAY}   Phase: $phase${NC}"
    
    STEP_START=$(date +%s)
    
    if STEP_OUTPUT=$($function_name 2>&1); then
        STEP_END=$(date +%s)
        STEP_DURATION=$((STEP_END - STEP_START))
        
        echo -e "${GREEN}✅ Step completed successfully in ${STEP_DURATION}s${NC}"
        if [ -n "$STEP_OUTPUT" ]; then
            echo -e "${GRAY}   $STEP_OUTPUT${NC}"
        fi
        
        EXECUTED_STEPS+=("$step_id:SUCCESS:$STEP_DURATION:$STEP_OUTPUT")
    else
        STEP_END=$(date +%s)
        STEP_DURATION=$((STEP_END - STEP_START))
        
        echo -e "${RED}❌ Step failed: $STEP_OUTPUT${NC}"
        ERRORS+=("$step_id: $STEP_OUTPUT")
        EXECUTED_STEPS+=("$step_id:FAILED:$STEP_DURATION:$STEP_OUTPUT")
        
        if [ "$STOP_ON_FAILURE" = true ]; then
            SUCCESS=false
            echo -e "${RED}Migration failed at step: $step_id${NC}"
            break
        fi
    fi
done

# Phase 4: Post-migration validation
if [ "$DRY_RUN" = false ] && [ "$SUCCESS" = true ]; then
    echo ""
    echo -e "${BLUE}🔍 Phase 4: Post-Migration Validation${NC}"
    
    if [ "$SKIP_BUILD" = false ]; then
        echo -e "${WHITE}🔨 Running build validation...${NC}"
        
        BUILD_PARAMS=(
            "--configuration" "Debug"
            "--verbosity" "minimal"
            "--no-restore"
            "-p:RunAnalyzersDuringBuild=false"
            "-p:EnableNETAnalyzers=false"
            "-p:TreatWarningsAsErrors=false"
        )
        
        if dotnet build "${BUILD_PARAMS[@]}"; then
            echo -e "${GREEN}✅ Build validation passed${NC}"
        else
            echo -e "${RED}❌ Build validation failed${NC}"
            ERRORS+=("Post-migration build failed")
            SUCCESS=false
        fi
    fi
    
    if [ "$SKIP_TESTS" = false ] && [ "$SUCCESS" = true ]; then
        echo -e "${WHITE}🧪 Running test validation...${NC}"
        
        if dotnet test --no-build --verbosity minimal; then
            echo -e "${GREEN}✅ Test validation passed${NC}"
        else
            echo -e "${YELLOW}⚠️ Some tests failed - review test output${NC}"
            ERRORS+=("Some post-migration tests failed")
        fi
    fi
fi

EXECUTION_END=$(date +%s)
EXECUTION_DURATION=$((EXECUTION_END - EXECUTION_START))

# Execution Summary
echo ""
echo -e "${GREEN}📊 Migration Execution Summary${NC}"
echo -e "${GREEN}==============================${NC}"

SUCCESSFUL_STEPS=$(printf '%s\n' "${EXECUTED_STEPS[@]}" | grep -c ":SUCCESS:" || true)
FAILED_STEPS=$(printf '%s\n' "${EXECUTED_STEPS[@]}" | grep -c ":FAILED:" || true)

echo ""
echo -e "${CYAN}🎯 Execution Results:${NC}"
echo -e "${WHITE}   Total Steps Executed: ${#EXECUTED_STEPS[@]}${NC}"
echo -e "${WHITE}   Successful Steps: $SUCCESSFUL_STEPS${NC}"
echo -e "${WHITE}   Failed Steps: $FAILED_STEPS${NC}"
echo -e "${WHITE}   Backup Created: $BACKUP_CREATED${NC}"
if [ "$BACKUP_CREATED" = true ]; then
    echo -e "${GRAY}   Backup Name: $BACKUP_CHECKPOINT${NC}"
fi
echo -e "${WHITE}   Execution Time: $EXECUTION_DURATION seconds${NC}"

if [ ${#ERRORS[@]} -gt 0 ]; then
    echo ""
    echo -e "${RED}❌ Errors Encountered:${NC}"
    for error in "${ERRORS[@]}"; do
        echo -e "${RED}   - $error${NC}"
    done
fi

echo ""
if [ "$SUCCESS" = true ]; then
    if [ "$DRY_RUN" = true ]; then
        echo -e "${GREEN}✅ Dry run completed successfully${NC}"
    else
        echo -e "${GREEN}✅ Migration completed successfully!${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}💡 Next Steps:${NC}"
    if [ "$DRY_RUN" = false ]; then
        echo -e "${WHITE}• Review migrated code for any manual adjustments needed${NC}"
        echo -e "${WHITE}• Run comprehensive tests to validate functionality${NC}"
        echo -e "${WHITE}• Consider removing legacy services once confident in migration${NC}"
        echo -e "${WHITE}• Update documentation to reflect new architecture${NC}"
    else
        echo -e "${WHITE}• Run actual migration: ./scripts/execute-migration.sh --create-backup${NC}"
    fi
else
    echo -e "${RED}❌ Migration failed with errors${NC}"
    
    echo ""
    echo -e "${CYAN}🔧 Recovery Options:${NC}"
    if [ "$BACKUP_CREATED" = true ]; then
        echo -e "${WHITE}• Rollback to backup: ./scripts/rollback.sh --checkpoint-name $BACKUP_CHECKPOINT${NC}"
    fi
    echo -e "${WHITE}• Review and fix errors above${NC}"
    echo -e "${WHITE}• Run migration again with --start-from-step to resume${NC}"
    echo -e "${WHITE}• Get help in project documentation or issues${NC}"
fi

echo ""
echo -e "${GREEN}🎉 Migration execution completed!${NC}"

# Exit with appropriate code
if [ "$SUCCESS" = true ]; then
    exit 0
else
    exit 1
fi