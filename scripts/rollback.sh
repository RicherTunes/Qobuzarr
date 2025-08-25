#!/bin/bash
# =============================================================================
# Qobuzarr Migration Rollback Script (Bash)
# =============================================================================
# Emergency rollback scripts for migration failures

set -e

# Default values
CHECKPOINT_NAME=""
EMERGENCY=false
LIST_CHECKPOINTS=false
FORCE=false
SKIP_VALIDATION=false
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
    echo -e "${GREEN}🔄 Qobuzarr Migration Rollback Script${NC}"
    echo ""
    echo -e "${CYAN}USAGE:${NC}"
    echo -e "  ${WHITE}./scripts/rollback.sh [Options]${NC}"
    echo ""
    echo -e "${CYAN}OPTIONS:${NC}"
    echo -e "  ${WHITE}--checkpoint-name [name]  Rollback to specific checkpoint${NC}"
    echo -e "  ${WHITE}--emergency               Emergency rollback to most recent safe checkpoint${NC}"
    echo -e "  ${WHITE}--list-checkpoints        List available checkpoints for rollback${NC}"
    echo -e "  ${WHITE}--force                   Force rollback even if safety checks fail${NC}"
    echo -e "  ${WHITE}--skip-validation         Skip post-rollback validation${NC}"
    echo -e "  ${WHITE}--verbose                 Show detailed rollback output${NC}"
    echo -e "  ${WHITE}--help                    Show this help${NC}"
    echo ""
    echo -e "${CYAN}EXAMPLES:${NC}"
    echo -e "  ${GRAY}./scripts/rollback.sh --list-checkpoints                       # Show available checkpoints${NC}"
    echo -e "  ${GRAY}./scripts/rollback.sh --checkpoint-name pre-migration-123      # Rollback to specific checkpoint${NC}"
    echo -e "  ${GRAY}./scripts/rollback.sh --emergency                              # Emergency rollback${NC}"
    echo -e "  ${GRAY}./scripts/rollback.sh --checkpoint-name backup-123 --force     # Force rollback${NC}"
    echo ""
    echo -e "${CYAN}SAFETY:${NC}"
    echo -e "  ${RED}🚨 This script will overwrite current files - ensure you understand the consequences${NC}"
    echo -e "  ${YELLOW}💾 A new emergency backup will be created before rollback${NC}"
    echo ""
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --checkpoint-name)
            CHECKPOINT_NAME="$2"
            shift 2
            ;;
        --emergency)
            EMERGENCY=true
            shift
            ;;
        --list-checkpoints)
            LIST_CHECKPOINTS=true
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        --skip-validation)
            SKIP_VALIDATION=true
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

echo -e "${GREEN}🔄 Qobuzarr Migration Rollback${NC}"
echo -e "${GREEN}==============================${NC}"

# Check if checkpoints directory exists
CHECKPOINT_DIR=".migration-checkpoints"
if [ ! -d "$CHECKPOINT_DIR" ]; then
    echo -e "${RED}❌ No checkpoints directory found${NC}"
    echo -e "${YELLOW}   Expected: $CHECKPOINT_DIR${NC}"
    echo -e "${YELLOW}   No rollback possible without checkpoints${NC}"
    exit 1
fi

# List checkpoints if requested
if [ "$LIST_CHECKPOINTS" = true ]; then
    echo ""
    echo -e "${BLUE}📋 Available Checkpoints${NC}"
    
    if [ ! "$(ls -A "$CHECKPOINT_DIR" 2>/dev/null)" ]; then
        echo -e "${YELLOW}   No checkpoints found${NC}"
        exit 0
    fi
    
    echo ""
    echo -e "${CYAN}   Name                              Created              Files${NC}"
    echo -e "${GRAY}   ─────────────────────────────────────────────────────────────${NC}"
    
    # List checkpoints sorted by modification time (newest first)
    for checkpoint_path in $(ls -dt "$CHECKPOINT_DIR"/*/ 2>/dev/null); do
        checkpoint_name=$(basename "$checkpoint_path")
        manifest_path="$checkpoint_path/backup-manifest.json"
        
        file_count="?"
        if [ -f "$manifest_path" ]; then
            if command -v jq >/dev/null 2>&1; then
                file_count=$(jq -r '.fileCount' "$manifest_path" 2>/dev/null || echo "?")
            else
                file_count=$(grep -o '"fileCount"[^,}]*' "$manifest_path" 2>/dev/null | cut -d':' -f2 | tr -d ' ,"' || echo "?")
            fi
        fi
        
        created_date=$(stat -c %y "$checkpoint_path" 2>/dev/null | cut -d'.' -f1 || date -r "$checkpoint_path" "+%Y-%m-%d %H:%M:%S" 2>/dev/null || echo "Unknown")
        
        printf "   %-35s %-20s %s\n" "$checkpoint_name" "$created_date" "$file_count"
    done
    
    echo ""
    echo -e "${CYAN}💡 To rollback to a checkpoint:${NC}"
    echo -e "${WHITE}   ./scripts/rollback.sh --checkpoint-name <name>${NC}"
    
    exit 0
fi

# Initialize rollback tracking
ROLLBACK_START=$(date +%s)
SUCCESS=true
TARGET_CHECKPOINT=""
FILES_RESTORED=0
declare -a ERRORS=()
EMERGENCY_BACKUP_CREATED=false
EMERGENCY_BACKUP_NAME=""

# Determine target checkpoint
if [ "$EMERGENCY" = true ]; then
    echo ""
    echo -e "${RED}🚨 Emergency Rollback Mode${NC}"
    echo -e "${YELLOW}Finding most recent safe checkpoint...${NC}"
    
    # Find most recent checkpoint
    MOST_RECENT_CHECKPOINT=$(ls -dt "$CHECKPOINT_DIR"/*/ 2>/dev/null | head -1)
    
    if [ -z "$MOST_RECENT_CHECKPOINT" ]; then
        echo -e "${RED}❌ No checkpoints available for emergency rollback${NC}"
        exit 1
    fi
    
    TARGET_CHECKPOINT=$(basename "$MOST_RECENT_CHECKPOINT")
    echo -e "${YELLOW}🎯 Emergency target: $TARGET_CHECKPOINT${NC}"
    
elif [ -n "$CHECKPOINT_NAME" ]; then
    TARGET_CHECKPOINT="$CHECKPOINT_NAME"
    
    # Verify checkpoint exists
    CHECKPOINT_PATH="$CHECKPOINT_DIR/$TARGET_CHECKPOINT"
    if [ ! -d "$CHECKPOINT_PATH" ]; then
        echo -e "${RED}❌ Checkpoint not found: $TARGET_CHECKPOINT${NC}"
        exit 1
    fi
    
else
    echo -e "${RED}❌ No checkpoint specified for rollback${NC}"
    echo ""
    echo -e "${CYAN}💡 Available options:${NC}"
    echo -e "${WHITE}• List checkpoints: ./scripts/rollback.sh --list-checkpoints${NC}"
    echo -e "${WHITE}• Emergency rollback: ./scripts/rollback.sh --emergency${NC}"
    echo -e "${WHITE}• Specific checkpoint: ./scripts/rollback.sh --checkpoint-name <name>${NC}"
    exit 1
fi

echo ""
echo -e "${BLUE}🔍 Phase 1: Rollback Analysis${NC}"

CHECKPOINT_PATH="$CHECKPOINT_DIR/$TARGET_CHECKPOINT"
MANIFEST_PATH="$CHECKPOINT_PATH/backup-manifest.json"

# Load checkpoint manifest
if [ -f "$MANIFEST_PATH" ]; then
    echo -e "${WHITE}📄 Checkpoint manifest loaded${NC}"
    
    if command -v jq >/dev/null 2>&1; then
        CREATED_AT=$(jq -r '.createdAt' "$MANIFEST_PATH" 2>/dev/null || echo "Unknown")
        FILE_COUNT=$(jq -r '.fileCount' "$MANIFEST_PATH" 2>/dev/null || echo "Unknown")
    else
        CREATED_AT=$(grep -o '"createdAt"[^,}]*' "$MANIFEST_PATH" 2>/dev/null | cut -d':' -f2- | tr -d ' ,"' || echo "Unknown")
        FILE_COUNT=$(grep -o '"fileCount"[^,}]*' "$MANIFEST_PATH" 2>/dev/null | cut -d':' -f2 | tr -d ' ,"' || echo "Unknown")
    fi
    
    echo -e "${GRAY}   Created: $CREATED_AT${NC}"
    echo -e "${GRAY}   Files: $FILE_COUNT${NC}"
else
    echo -e "${YELLOW}⚠️ Could not read checkpoint manifest - proceeding with basic rollback${NC}"
fi

# Analyze files to restore
FILES_TO_RESTORE=($(find "$CHECKPOINT_PATH" -name "*.cs" -type f 2>/dev/null))
echo -e "${WHITE}📁 Files to restore: ${#FILES_TO_RESTORE[@]}${NC}"

if [ ${#FILES_TO_RESTORE[@]} -eq 0 ]; then
    echo -e "${RED}❌ No files found in checkpoint: $TARGET_CHECKPOINT${NC}"
    exit 1
fi

# Safety checks (unless forced or emergency)
if [ "$FORCE" = false ] && [ "$EMERGENCY" = false ]; then
    echo ""
    echo -e "${BLUE}🔍 Phase 2: Safety Checks${NC}"
    
    declare -a SAFETY_ISSUES=()
    
    # Check if current files have been modified since checkpoint
    CHECKPOINT_DATE=$(stat -c %Y "$CHECKPOINT_PATH" 2>/dev/null || date -r "$CHECKPOINT_PATH" +%s 2>/dev/null || echo 0)
    
    for file_path in "${FILES_TO_RESTORE[@]}"; do
        file_name=$(basename "$file_path")
        
        # Check various common locations
        POSSIBLE_PATHS=(
            "src/Services/$file_name"
            "src/Core/$file_name"
            "src/Services/Consolidated/$file_name"
        )
        
        for possible_path in "${POSSIBLE_PATHS[@]}"; do
            if [ -f "$possible_path" ]; then
                FILE_DATE=$(stat -c %Y "$possible_path" 2>/dev/null || date -r "$possible_path" +%s 2>/dev/null || echo 0)
                if [ "$FILE_DATE" -gt "$CHECKPOINT_DATE" ]; then
                    SAFETY_ISSUES+=("File modified since checkpoint: $possible_path")
                fi
                break
            fi
        done
    done
    
    if [ ${#SAFETY_ISSUES[@]} -gt 0 ]; then
        echo -e "${YELLOW}⚠️ Safety check warnings:${NC}"
        for issue in "${SAFETY_ISSUES[@]}"; do
            echo -e "${YELLOW}   - $issue${NC}"
        done
        
        if [ "$FORCE" = false ]; then
            echo ""
            echo -e "${YELLOW}Continue rollback despite warnings? This will overwrite current changes.${NC}"
            echo -e "${YELLOW}Type 'yes' to continue, anything else to abort: ${NC}"
            read -r response
            
            if [ "$response" != "yes" ]; then
                echo -e "${YELLOW}Rollback aborted by user${NC}"
                exit 0
            fi
        fi
    else
        echo -e "${GREEN}✅ Safety checks passed${NC}"
    fi
fi

# Create emergency backup before rollback
echo ""
echo -e "${BLUE}💾 Phase 3: Emergency Backup${NC}"

EMERGENCY_BACKUP_NAME="emergency-pre-rollback-$(date +%Y%m%d-%H%M%S)"
EMERGENCY_BACKUP_PATH="$CHECKPOINT_DIR/$EMERGENCY_BACKUP_NAME"

if mkdir -p "$EMERGENCY_BACKUP_PATH"; then
    # Backup current state of files that will be restored
    BACKED_UP_COUNT=0
    for file_path in "${FILES_TO_RESTORE[@]}"; do
        file_name=$(basename "$file_path")
        
        POSSIBLE_PATHS=(
            "src/Services/$file_name"
            "src/Core/$file_name"  
            "src/Services/Consolidated/$file_name"
        )
        
        for possible_path in "${POSSIBLE_PATHS[@]}"; do
            if [ -f "$possible_path" ]; then
                backup_file_path="$EMERGENCY_BACKUP_PATH/$file_name"
                cp "$possible_path" "$backup_file_path"
                ((BACKED_UP_COUNT++))
                break
            fi
        done
    done
    
    # Create emergency backup manifest
    cat > "$EMERGENCY_BACKUP_PATH/backup-manifest.json" << EOF
{
  "backupName": "$EMERGENCY_BACKUP_NAME",
  "createdAt": "$(date -Iseconds)",
  "fileCount": $BACKED_UP_COUNT,
  "projectRoot": "$(pwd)",
  "rollbackTarget": "$TARGET_CHECKPOINT",
  "migrationVersion": "2.0.0"
}
EOF
    
    echo -e "${GREEN}✅ Emergency backup created: $BACKED_UP_COUNT files${NC}"
    EMERGENCY_BACKUP_CREATED=true
else
    echo -e "${YELLOW}⚠️ Failed to create emergency backup directory${NC}"
    ERRORS+=("Emergency backup failed: Could not create directory")
fi

# Execute rollback
echo ""
echo -e "${BLUE}🔄 Phase 4: File Restoration${NC}"

for file_path in "${FILES_TO_RESTORE[@]}"; do
    file_name=$(basename "$file_path")
    
    # Determine target location
    TARGET_PATH=""
    POSSIBLE_PATHS=(
        "src/Services/$file_name"
        "src/Core/$file_name"
        "src/Services/Consolidated/$file_name"
    )
    
    # Use existing file location if found, otherwise default to Services
    for possible_path in "${POSSIBLE_PATHS[@]}"; do
        if [ -f "$possible_path" ]; then
            TARGET_PATH="$possible_path"
            break
        fi
    done
    
    if [ -z "$TARGET_PATH" ]; then
        TARGET_PATH="src/Services/$file_name"
    fi
    
    # Ensure target directory exists
    TARGET_DIR=$(dirname "$TARGET_PATH")
    mkdir -p "$TARGET_DIR"
    
    # Restore file
    if cp "$file_path" "$TARGET_PATH"; then
        ((FILES_RESTORED++))
        
        if [ "$VERBOSE" = true ]; then
            echo -e "${GRAY}   📄 Restored: $TARGET_PATH${NC}"
        fi
    else
        echo -e "${RED}❌ Failed to restore $file_name${NC}"
        ERRORS+=("Failed to restore $file_name")
        SUCCESS=false
    fi
done

echo -e "${GREEN}✅ Restored $FILES_RESTORED files${NC}"

# Post-rollback validation
if [ "$SKIP_VALIDATION" = false ]; then
    echo ""
    echo -e "${BLUE}🔍 Phase 5: Post-Rollback Validation${NC}"
    
    echo -e "${WHITE}🔨 Testing build after rollback...${NC}"
    
    BUILD_PARAMS=(
        "--configuration" "Debug"
        "--verbosity" "quiet" 
        "--no-restore"
        "-p:RunAnalyzersDuringBuild=false"
        "-p:EnableNETAnalyzers=false"
        "-p:TreatWarningsAsErrors=false"
    )
    
    if BUILD_OUTPUT=$(dotnet build "${BUILD_PARAMS[@]}" 2>&1); then
        echo -e "${GREEN}✅ Post-rollback build successful${NC}"
    else
        echo -e "${YELLOW}⚠️ Post-rollback build issues detected${NC}"
        ERRORS+=("Post-rollback build failed")
        
        if [ "$VERBOSE" = true ]; then
            echo -e "${GRAY}Build output:${NC}"
            echo "$BUILD_OUTPUT" | sed 's/^/   /'
        fi
    fi
fi

ROLLBACK_END=$(date +%s)
ROLLBACK_DURATION=$((ROLLBACK_END - ROLLBACK_START))

# Rollback Summary
echo ""
echo -e "${GREEN}📊 Rollback Summary${NC}"
echo -e "${GREEN}===================${NC}"

echo ""
echo -e "${CYAN}🔄 Rollback Results:${NC}"
echo -e "${WHITE}   Target Checkpoint: $TARGET_CHECKPOINT${NC}"
echo -e "${WHITE}   Files Restored: $FILES_RESTORED${NC}"
echo -e "${WHITE}   Emergency Backup Created: $EMERGENCY_BACKUP_CREATED${NC}"
if [ "$EMERGENCY_BACKUP_CREATED" = true ]; then
    echo -e "${GRAY}   Emergency Backup Name: $EMERGENCY_BACKUP_NAME${NC}"
fi
echo -e "${WHITE}   Rollback Time: $ROLLBACK_DURATION seconds${NC}"

if [ ${#ERRORS[@]} -gt 0 ]; then
    echo ""
    echo -e "${YELLOW}⚠️ Issues Encountered:${NC}"
    for error in "${ERRORS[@]}"; do
        echo -e "${YELLOW}   - $error${NC}"
    done
fi

echo ""
if [ "$SUCCESS" = true ] && [ "$FILES_RESTORED" -gt 0 ]; then
    echo -e "${GREEN}✅ Rollback completed successfully!${NC}"
    
    echo ""
    echo -e "${CYAN}💡 Next Steps:${NC}"
    echo -e "${WHITE}• Verify that the application works as expected${NC}"
    echo -e "${WHITE}• Review what caused the need for rollback${NC}"
    echo -e "${WHITE}• Fix migration issues before attempting again${NC}"
    if [ "$EMERGENCY_BACKUP_CREATED" = true ]; then
        echo -e "${WHITE}• Emergency backup is available: $EMERGENCY_BACKUP_NAME${NC}"
    fi
else
    echo -e "${RED}❌ Rollback completed with issues${NC}"
    
    echo ""
    echo -e "${CYAN}🔧 Recovery Options:${NC}"
    echo -e "${WHITE}• Review errors above and address manually${NC}"
    echo -e "${WHITE}• Try rollback with --force if safety checks are blocking${NC}"
    if [ "$EMERGENCY_BACKUP_CREATED" = true ]; then
        echo -e "${WHITE}• Emergency backup available if further rollback needed${NC}"
    fi
    echo -e "${WHITE}• Seek help in project documentation or issues${NC}"
fi

echo ""
echo -e "${GREEN}🎉 Rollback operation completed!${NC}"

# Exit with appropriate code
if [ "$SUCCESS" = true ] && [ "$FILES_RESTORED" -gt 0 ]; then
    exit 0
else
    exit 1
fi