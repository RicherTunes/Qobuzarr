#!/bin/bash
# =============================================================================
# Qobuzarr Migration Dry Run Script (Bash)
# =============================================================================
# Full dry run analysis of service migration without making changes

set -e

# Default values
START_FROM_STEP=""
VERBOSE=false
SHOW_DETAILS=false
EXPORT_REPORT=false
REPORT_PATH=""
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
    echo -e "${GREEN}🧪 Qobuzarr Migration Dry Run Script${NC}"
    echo ""
    echo -e "${CYAN}USAGE:${NC}"
    echo -e "  ${WHITE}./scripts/dry-run.sh [Options]${NC}"
    echo ""
    echo -e "${CYAN}OPTIONS:${NC}"
    echo -e "  ${WHITE}--start-from-step [step]  Start analysis from specific step${NC}"
    echo -e "  ${WHITE}--verbose                 Show detailed analysis output${NC}"
    echo -e "  ${WHITE}--show-details            Display step-by-step analysis${NC}"
    echo -e "  ${WHITE}--export-report           Generate detailed analysis report${NC}"
    echo -e "  ${WHITE}--report-path [path]      Custom path for analysis report${NC}"
    echo -e "  ${WHITE}--help                    Show this help${NC}"
    echo ""
    echo -e "${CYAN}EXAMPLES:${NC}"
    echo -e "  ${GRAY}./scripts/dry-run.sh                                     # Basic dry run${NC}"
    echo -e "  ${GRAY}./scripts/dry-run.sh --verbose --show-details            # Detailed analysis${NC}"
    echo -e "  ${GRAY}./scripts/dry-run.sh --export-report                     # Generate report${NC}"
    echo -e "  ${GRAY}./scripts/dry-run.sh --start-from-step migrate-validation # Partial analysis${NC}"
    echo ""
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --start-from-step)
            START_FROM_STEP="$2"
            shift 2
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --show-details)
            SHOW_DETAILS=true
            shift
            ;;
        --export-report)
            EXPORT_REPORT=true
            shift
            ;;
        --report-path)
            REPORT_PATH="$2"
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

echo -e "${GREEN}🧪 Qobuzarr Migration Dry Run Analysis${NC}"
echo -e "${GREEN}=======================================${NC}"

# Initialize analysis tracking
ANALYSIS_START=$(date +%s)
TOTAL_STEPS=0
ANALYZED_STEPS=0
BLOCKING_ISSUES=0
WARNINGS=0
ESTIMATED_MINUTES=0
RISK_LEVEL="Low"
declare -a ISSUES=()
declare -a STEP_ANALYSIS=()

# Step 1: Validate prerequisites
echo ""
echo -e "${BLUE}📋 Phase 1: Prerequisites Analysis${NC}"

echo -e "${WHITE}🔍 Checking project structure...${NC}"

# Check source directory structure
REQUIRED_DIRECTORIES=(
    "src/Services"
    "src/Services/Consolidated" 
    "tools/MigrationController"
    "tools/SessionMigrator"
)

MISSING_DIRECTORIES=()
for dir in "${REQUIRED_DIRECTORIES[@]}"; do
    if [ ! -d "$dir" ]; then
        MISSING_DIRECTORIES+=("$dir")
    fi
done

if [ ${#MISSING_DIRECTORIES[@]} -gt 0 ]; then
    echo -e "${RED}❌ Missing required directories:${NC}"
    for dir in "${MISSING_DIRECTORIES[@]}"; do
        echo -e "${RED}   - $dir${NC}"
    done
    ((BLOCKING_ISSUES++))
    ISSUES+=("Missing required directories: ${MISSING_DIRECTORIES[*]}")
else
    echo -e "${GREEN}✅ Project structure validation passed${NC}"
fi

# Check consolidated services exist
echo -e "${WHITE}🔍 Checking consolidated services...${NC}"

CONSOLIDATED_SERVICES=(
    "src/Services/Consolidated/QobuzQualityManager.cs"
    "src/Services/Consolidated/IQobuzQualityManager.cs"
    "src/Services/Consolidated/ConsolidatedServiceRegistration.cs"
)

MISSING_SERVICES=()
for service in "${CONSOLIDATED_SERVICES[@]}"; do
    if [ ! -f "$service" ]; then
        MISSING_SERVICES+=("$service")
    fi
done

if [ ${#MISSING_SERVICES[@]} -gt 0 ]; then
    echo -e "${RED}❌ Missing consolidated services:${NC}"
    for service in "${MISSING_SERVICES[@]}"; do
        echo -e "${RED}   - $service${NC}"
    done
    ((BLOCKING_ISSUES++))
    ISSUES+=("Missing consolidated services: ${MISSING_SERVICES[*]}")
else
    echo -e "${GREEN}✅ Consolidated services validation passed${NC}"
fi

# Check legacy services to be migrated
echo -e "${WHITE}🔍 Analyzing legacy services...${NC}"

LEGACY_SERVICES=(
    "src/Services/LidarrAlbumRetriever.cs"
    "src/Services/QobuzValidationService.cs"
    "src/Core/QobuzApiService.cs"
    "src/Services/QobuzQualityService.cs"
    "src/Services/QualityMappingService.cs"
    "src/Services/QualityFallbackService.cs"
)

EXISTING_LEGACY_SERVICES=()
for service in "${LEGACY_SERVICES[@]}"; do
    if [ -f "$service" ]; then
        EXISTING_LEGACY_SERVICES+=("$service")
        
        # Analyze service dependencies
        if [ "$VERBOSE" = true ]; then
            DEPENDENCIES=()
            
            # Check for quality service dependencies
            if grep -q "IQualityMappingService\|QualityMappingService" "$service"; then
                DEPENDENCIES+=("QualityMappingService")
            fi
            if grep -q "IQualityFallbackService\|QualityFallbackService" "$service"; then
                DEPENDENCIES+=("QualityFallbackService")
            fi
            if grep -q "QobuzQualityService" "$service"; then
                DEPENDENCIES+=("QobuzQualityService")
            fi
            
            if [ ${#DEPENDENCIES[@]} -gt 0 ]; then
                echo -e "${GRAY}   📄 $service has dependencies: ${DEPENDENCIES[*]}${NC}"
            fi
        fi
    fi
done

echo -e "${GREEN}✅ Found ${#EXISTING_LEGACY_SERVICES[@]} legacy services to migrate${NC}"

# Step 2: Build validation
echo ""
echo -e "${BLUE}🔨 Phase 2: Build Analysis${NC}"

echo -e "${WHITE}🔍 Testing current build status...${NC}"

# Check if project builds currently
BUILD_PARAMS=(
    "--configuration" "Debug"
    "--verbosity" "quiet"
    "--no-restore"
    "-p:RunAnalyzersDuringBuild=false"
    "-p:EnableNETAnalyzers=false"
    "-p:TreatWarningsAsErrors=false"
)

if BUILD_OUTPUT=$(dotnet build "${BUILD_PARAMS[@]}" 2>&1); then
    echo -e "${GREEN}✅ Project builds successfully${NC}"
    BUILD_SUCCESS=true
else
    echo -e "${YELLOW}⚠️ Project has build issues:${NC}"
    if [ "$VERBOSE" = true ]; then
        echo "$BUILD_OUTPUT" | while IFS= read -r line; do
            echo -e "${GRAY}   $line${NC}"
        done
    fi
    ((WARNINGS++))
    ISSUES+=("Current build has issues - migration may resolve or worsen")
    BUILD_SUCCESS=false
fi

# Step 3: Migration steps analysis
echo ""
echo -e "${BLUE}📝 Phase 3: Migration Steps Analysis${NC}"

# Define migration steps
declare -A MIGRATION_STEPS
MIGRATION_STEPS[migrate-lidarr-album-retriever]="Migrate LidarrAlbumRetriever to IQobuzQualityManager|2A|15|Medium|IQobuzQualityManager implemented"
MIGRATION_STEPS[migrate-qobuz-validation-service]="Migrate QobuzValidationService to consolidated services|2A|10|Low|IQobuzQualityManager implemented"
MIGRATION_STEPS[migrate-qobuz-api-service]="Migrate QobuzApiService quality mappings|2A|8|Low|IQobuzQualityManager implemented"
MIGRATION_STEPS[remove-legacy-quality-services]="Remove legacy quality service files|2B|5|High|All services migrated,Build tests passing"
MIGRATION_STEPS[remove-migration-adapters]="Remove migration adapters and temporary code|2C|5|Medium|Legacy services removed,Integration tests passing"

TOTAL_STEPS=${#MIGRATION_STEPS[@]}
FOUND_START_STEP=false

for step_id in "${!MIGRATION_STEPS[@]}"; do
    IFS='|' read -r description phase minutes risk_level dependencies <<< "${MIGRATION_STEPS[$step_id]}"
    
    if [ -n "$START_FROM_STEP" ] && [ "$FOUND_START_STEP" = false ]; then
        if [ "$step_id" = "$START_FROM_STEP" ]; then
            FOUND_START_STEP=true
        else
            continue
        fi
    fi
    
    ((ANALYZED_STEPS++))
    ((ESTIMATED_MINUTES += minutes))
    
    echo -e "${WHITE}🔍 Analyzing step: $step_id${NC}"
    
    # Track issues for this step
    STEP_ISSUES=()
    CAN_EXECUTE=true
    
    # Check step dependencies
    IFS=',' read -ra DEPS <<< "$dependencies"
    for dependency in "${DEPS[@]}"; do
        case "$dependency" in
            "IQobuzQualityManager implemented")
                if [ ! -f "src/Services/Consolidated/QobuzQualityManager.cs" ]; then
                    STEP_ISSUES+=("Dependency missing: $dependency")
                    CAN_EXECUTE=false
                fi
                ;;
            "All services migrated")
                if [ "$VERBOSE" = true ]; then
                    echo -e "${GRAY}   ℹ️ Dependency check: $dependency (would be verified at runtime)${NC}"
                fi
                ;;
            "Build tests passing")
                if [ "$BUILD_SUCCESS" = false ]; then
                    STEP_ISSUES+=("Dependency not met: Build currently failing")
                    ((WARNINGS++))
                fi
                ;;
        esac
    done
    
    # Risk level aggregation
    if [ "$risk_level" = "High" ]; then
        RISK_LEVEL="High"
    elif [ "$risk_level" = "Medium" ] && [ "$RISK_LEVEL" != "High" ]; then
        RISK_LEVEL="Medium"
    fi
    
    if [ ${#STEP_ISSUES[@]} -gt 0 ]; then
        echo -e "${YELLOW}   ⚠️ Issues found:${NC}"
        for issue in "${STEP_ISSUES[@]}"; do
            echo -e "${YELLOW}     - $issue${NC}"
            ISSUES+=("$step_id: $issue")
        done
        WARNINGS=$((WARNINGS + ${#STEP_ISSUES[@]}))
    else
        echo -e "${GREEN}   ✅ Step analysis passed${NC}"
    fi
    
    if [ "$SHOW_DETAILS" = true ]; then
        echo -e "${CYAN}   📊 Details:${NC}"
        echo -e "${GRAY}     Phase: $phase${NC}"
        echo -e "${GRAY}     Risk: $risk_level${NC}"
        echo -e "${GRAY}     Time: $minutes minutes${NC}"
        echo -e "${GRAY}     Dependencies: $dependencies${NC}"
    fi
done

# Step 4: Session migration analysis
echo ""
echo -e "${BLUE}🔐 Phase 4: Session Migration Analysis${NC}"

SESSION_DIRECTORY=".qobuz-sessions"

if [ -d "$SESSION_DIRECTORY" ]; then
    SESSION_COUNT=$(find "$SESSION_DIRECTORY" -name "*.json" -type f | wc -l)
    echo -e "${WHITE}📄 Found $SESSION_COUNT session files to analyze${NC}"
    
    if [ "$SESSION_COUNT" -gt 0 ]; then
        echo -e "${GREEN}✅ Session migration will be included in migration plan${NC}"
        ((ESTIMATED_MINUTES += 5))
    fi
else
    echo -e "${GRAY}ℹ️ No existing sessions found - fresh installation${NC}"
fi

# Analysis Summary
echo ""
echo -e "${GREEN}📊 Migration Analysis Summary${NC}"
echo -e "${GREEN}=============================${NC}"

ANALYSIS_END=$(date +%s)
ANALYSIS_DURATION=$((ANALYSIS_END - ANALYSIS_START))

echo ""
echo -e "${CYAN}🔍 Analysis Results:${NC}"
echo -e "${WHITE}   Total Steps: $TOTAL_STEPS${NC}"
echo -e "${WHITE}   Analyzed Steps: $ANALYZED_STEPS${NC}"
if [ "$BLOCKING_ISSUES" -gt 0 ]; then
    echo -e "${RED}   Blocking Issues: $BLOCKING_ISSUES${NC}"
else
    echo -e "${GREEN}   Blocking Issues: $BLOCKING_ISSUES${NC}"
fi
if [ "$WARNINGS" -gt 0 ]; then
    echo -e "${YELLOW}   Warnings: $WARNINGS${NC}"
else
    echo -e "${GREEN}   Warnings: $WARNINGS${NC}"
fi
case "$RISK_LEVEL" in
    "High") echo -e "${RED}   Risk Level: $RISK_LEVEL${NC}" ;;
    "Medium") echo -e "${YELLOW}   Risk Level: $RISK_LEVEL${NC}" ;;
    *) echo -e "${GREEN}   Risk Level: $RISK_LEVEL${NC}" ;;
esac
echo -e "${WHITE}   Estimated Duration: $ESTIMATED_MINUTES minutes${NC}"
echo -e "${GRAY}   Analysis Time: $ANALYSIS_DURATION seconds${NC}"

if [ ${#ISSUES[@]} -gt 0 ]; then
    echo ""
    echo -e "${YELLOW}⚠️ Issues Detected:${NC}"
    for issue in "${ISSUES[@]}"; do
        echo -e "${YELLOW}   - $issue${NC}"
    done
fi

echo ""
if [ "$BLOCKING_ISSUES" -eq 0 ]; then
    echo -e "${GREEN}✅ Migration is ready to execute${NC}"
    echo ""
    echo -e "${CYAN}💡 Next Steps:${NC}"
    echo -e "${WHITE}• To execute migration: ./scripts/execute-migration.sh${NC}"
    echo -e "${WHITE}• To create backup first: ./scripts/execute-migration.sh --create-backup${NC}"
    if [ "$RISK_LEVEL" = "High" ]; then
        echo -e "${YELLOW}• ⚠️ High risk migration - consider manual review first${NC}"
    fi
else
    echo -e "${RED}❌ Migration cannot proceed due to blocking issues${NC}"
    echo ""
    echo -e "${CYAN}🔧 Required Actions:${NC}"
    echo -e "${WHITE}• Resolve blocking issues listed above${NC}"
    echo -e "${WHITE}• Run dry-run again to verify fixes${NC}"
fi

# Export detailed report if requested
if [ "$EXPORT_REPORT" = true ]; then
    if [ -z "$REPORT_PATH" ]; then
        REPORT_PATH="migration-analysis-report_$(date +%Y%m%d_%H%M%S).json"
    fi
    
    # Create JSON report
    cat > "$REPORT_PATH" << EOF
{
  "analysisTime": "$(date -Iseconds)",
  "duration": $ANALYSIS_DURATION,
  "results": {
    "totalSteps": $TOTAL_STEPS,
    "analyzedSteps": $ANALYZED_STEPS,
    "blockingIssues": $BLOCKING_ISSUES,
    "warnings": $WARNINGS,
    "estimatedMinutes": $ESTIMATED_MINUTES,
    "riskLevel": "$RISK_LEVEL",
    "issues": $(printf '%s\n' "${ISSUES[@]}" | jq -R . | jq -s .)
  },
  "projectRoot": "$(pwd)",
  "bashVersion": "$BASH_VERSION"
}
EOF
    
    if [ $? -eq 0 ]; then
        echo ""
        echo -e "${GREEN}📄 Detailed analysis report exported: $REPORT_PATH${NC}"
    else
        echo ""
        echo -e "${RED}❌ Failed to export report${NC}"
    fi
fi

echo ""
echo -e "${GREEN}🎉 Dry run analysis completed!${NC}"

# Exit with appropriate code
if [ "$BLOCKING_ISSUES" -gt 0 ]; then
    exit 1
else
    exit 0
fi