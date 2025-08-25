#!/bin/bash
# =============================================================================
# Qobuzarr Service Validation Script (Bash)
# =============================================================================
# Service validation and health checking post-migration

set -e

# Default values
QUICK=false
DEEP=false
INTEGRATION_TESTS=false
SESSION_VALIDATION=false
VERBOSE=false
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
    echo -e "${GREEN}🔍 Qobuzarr Service Validation Script${NC}"
    echo ""
    echo -e "${CYAN}USAGE:${NC}"
    echo -e "  ${WHITE}./scripts/validate-services.sh [Options]${NC}"
    echo ""
    echo -e "${CYAN}OPTIONS:${NC}"
    echo -e "  ${WHITE}--quick               Quick validation (build + basic tests)${NC}"
    echo -e "  ${WHITE}--deep                Deep validation (comprehensive analysis)${NC}"
    echo -e "  ${WHITE}--integration-tests   Run integration tests${NC}"
    echo -e "  ${WHITE}--session-validation  Validate session migration integrity${NC}"
    echo -e "  ${WHITE}--verbose             Show detailed validation output${NC}"
    echo -e "  ${WHITE}--export-report       Generate detailed validation report${NC}"
    echo -e "  ${WHITE}--report-path [path]  Custom path for validation report${NC}"
    echo -e "  ${WHITE}--help                Show this help${NC}"
    echo ""
    echo -e "${CYAN}EXAMPLES:${NC}"
    echo -e "  ${GRAY}./scripts/validate-services.sh                           # Standard validation${NC}"
    echo -e "  ${GRAY}./scripts/validate-services.sh --quick                   # Quick health check${NC}"
    echo -e "  ${GRAY}./scripts/validate-services.sh --deep --verbose          # Comprehensive validation${NC}"
    echo -e "  ${GRAY}./scripts/validate-services.sh --integration-tests       # Integration test focus${NC}"
    echo ""
    echo -e "${CYAN}VALIDATION LEVELS:${NC}"
    echo -e "  ${WHITE}Quick: Build + Unit Tests (2-3 minutes)${NC}"
    echo -e "  ${WHITE}Standard: Build + Tests + Service Analysis (5-10 minutes)${NC}"
    echo -e "  ${WHITE}Deep: Full analysis + Integration + Session validation (10-15 minutes)${NC}"
    echo ""
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --quick)
            QUICK=true
            shift
            ;;
        --deep)
            DEEP=true
            shift
            ;;
        --integration-tests)
            INTEGRATION_TESTS=true
            shift
            ;;
        --session-validation)
            SESSION_VALIDATION=true
            shift
            ;;
        --verbose)
            VERBOSE=true
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

echo -e "${GREEN}🔍 Qobuzarr Service Validation${NC}"
echo -e "${GREEN}==============================${NC}"

# Determine validation level
VALIDATION_LEVEL="Standard"
if [ "$QUICK" = true ]; then
    VALIDATION_LEVEL="Quick"
fi
if [ "$DEEP" = true ]; then
    VALIDATION_LEVEL="Deep"
fi

echo -e "${CYAN}🎯 Validation Level: $VALIDATION_LEVEL${NC}"

# Initialize validation tracking
VALIDATION_START=$(date +%s)
SUCCESS=true
BUILD_PASSED=false
UNIT_TESTS_PASSED=false
INTEGRATION_TESTS_PASSED=false
SERVICE_ANALYSIS_PASSED=false
SESSION_VALIDATION_PASSED=false
declare -a ISSUES=()
declare -a TEST_RESULTS=()
declare -a SERVICE_HEALTH=()

# Phase 1: Build Validation
echo ""
echo -e "${BLUE}🔨 Phase 1: Build Validation${NC}"

echo -e "${WHITE}🔍 Building project with migration changes...${NC}"

BUILD_PARAMS=(
    "--configuration" "Release"
    "--verbosity" "normal"
    "--no-restore"
    "-p:RunAnalyzersDuringBuild=false"
    "-p:EnableNETAnalyzers=false"
    "-p:TreatWarningsAsErrors=false"
)

if BUILD_OUTPUT=$(dotnet build "${BUILD_PARAMS[@]}" 2>&1); then
    echo -e "${GREEN}✅ Build validation passed${NC}"
    BUILD_PASSED=true
else
    echo -e "${RED}❌ Build validation failed${NC}"
    SUCCESS=false
    ISSUES+=("Build failed")
    
    if [ "$VERBOSE" = true ]; then
        echo -e "${GRAY}Build output:${NC}"
        echo "$BUILD_OUTPUT" | sed 's/^/   /'
    fi
fi

# Phase 2: Unit Test Validation
if [ "$BUILD_PASSED" = true ]; then
    echo ""
    echo -e "${BLUE}🧪 Phase 2: Unit Test Validation${NC}"
    
    echo -e "${WHITE}🔍 Running unit tests...${NC}"
    
    if TEST_OUTPUT=$(dotnet test --no-build --verbosity normal --logger "trx" --collect:"XPlat Code Coverage" 2>&1); then
        echo -e "${GREEN}✅ Unit tests passed${NC}"
        UNIT_TESTS_PASSED=true
        
        # Analyze test results if trx file is available
        TEST_RESULT_FILE=$(find . -name "*.trx" -type f -exec ls -t {} + 2>/dev/null | head -1)
        
        if [ -n "$TEST_RESULT_FILE" ] && command -v xmllint >/dev/null 2>&1; then
            # Extract test summary using xmllint if available
            if TOTAL=$(xmllint --xpath "//ns:Counters/@total" --namespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" "$TEST_RESULT_FILE" 2>/dev/null | sed 's/total="//; s/"//'); then
                PASSED=$(xmllint --xpath "//ns:Counters/@passed" --namespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" "$TEST_RESULT_FILE" 2>/dev/null | sed 's/passed="//; s/"//' || echo "0")
                FAILED=$(xmllint --xpath "//ns:Counters/@failed" --namespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" "$TEST_RESULT_FILE" 2>/dev/null | sed 's/failed="//; s/"//' || echo "0")
                
                if [ "$VERBOSE" = true ]; then
                    echo -e "${GRAY}   📊 Test Summary:${NC}"
                    echo -e "${GRAY}      Total: $TOTAL${NC}"
                    echo -e "${GRAY}      Passed: $PASSED${NC}"
                    echo -e "${GRAY}      Failed: $FAILED${NC}"
                fi
            fi
        fi
        
    else
        echo -e "${RED}❌ Unit tests failed${NC}"
        SUCCESS=false
        ISSUES+=("Unit tests failed")
        
        if [ "$VERBOSE" = true ]; then
            echo -e "${GRAY}Test output:${NC}"
            echo "$TEST_OUTPUT" | sed 's/^/   /'
        fi
    fi
fi

# Phase 3: Service Analysis (Standard and Deep only)
if [ "$QUICK" = false ] && [ "$BUILD_PASSED" = true ]; then
    echo ""
    echo -e "${BLUE}🔧 Phase 3: Service Analysis${NC}"
    
    echo -e "${WHITE}🔍 Analyzing consolidated services...${NC}"
    
    # Check consolidated services exist
    CONSOLIDATED_SERVICES=(
        "src/Services/Consolidated/QobuzQualityManager.cs"
        "src/Services/Consolidated/IQobuzQualityManager.cs"
        "src/Services/Consolidated/ConsolidatedServiceRegistration.cs"
    )
    
    MISSING_CONSOLIDATED=()
    for service in "${CONSOLIDATED_SERVICES[@]}"; do
        if [ ! -f "$service" ]; then
            MISSING_CONSOLIDATED+=("$service")
        fi
    done
    
    if [ ${#MISSING_CONSOLIDATED[@]} -eq 0 ]; then
        echo -e "${GREEN}✅ All consolidated services present${NC}"
        CONSOLIDATED_SERVICES_PRESENT=true
    else
        echo -e "${RED}❌ Missing consolidated services:${NC}"
        for service in "${MISSING_CONSOLIDATED[@]}"; do
            echo -e "${RED}   - $service${NC}"
        done
        ISSUES+=("Missing consolidated services: ${MISSING_CONSOLIDATED[*]}")
        SUCCESS=false
        CONSOLIDATED_SERVICES_PRESENT=false
    fi
    
    # Check legacy services (should be removed in Phase 2B+)
    LEGACY_SERVICES=(
        "src/Services/QobuzQualityService.cs"
        "src/Services/QualityMappingService.cs"
        "src/Services/QualityFallbackService.cs"
        "src/Services/IQualityMappingService.cs"
    )
    
    REMAINING_LEGACY=()
    for service in "${LEGACY_SERVICES[@]}"; do
        if [ -f "$service" ]; then
            REMAINING_LEGACY+=("$service")
        fi
    done
    
    if [ ${#REMAINING_LEGACY[@]} -eq 0 ]; then
        echo -e "${GREEN}✅ Legacy services properly removed${NC}"
        LEGACY_SERVICES_REMOVED=true
    else
        echo -e "${BLUE}ℹ️ Legacy services still present (may be intentional):${NC}"
        for service in "${REMAINING_LEGACY[@]}"; do
            echo -e "${BLUE}   - $service${NC}"
        done
        LEGACY_SERVICES_REMOVED=false
        # This is not necessarily an error - depends on migration phase
    fi
    
    # Analyze service registration
    REGISTRATION_FILE="src/Services/Consolidated/ConsolidatedServiceRegistration.cs"
    SERVICE_REGISTRATION_VALID=false
    
    if [ -f "$REGISTRATION_FILE" ]; then
        # Check for proper service registration patterns
        if grep -q "IQobuzQualityManager.*QobuzQualityManager" "$REGISTRATION_FILE"; then
            echo -e "${GREEN}✅ Service registration appears valid${NC}"
            SERVICE_REGISTRATION_VALID=true
        else
            echo -e "${YELLOW}⚠️ Service registration may have issues${NC}"
            ISSUES+=("Service registration validation failed")
        fi
        
        # Check for migration adapters (should be cleaned up in Phase 2C)
        if grep -q "MigrationAdapter\|Legacy" "$REGISTRATION_FILE"; then
            echo -e "${BLUE}ℹ️ Migration adapters still present (cleanup pending)${NC}"
        fi
    else
        echo -e "${RED}❌ Service registration file not found${NC}"
        ISSUES+=("Service registration file missing")
        SUCCESS=false
    fi
    
    # Overall service analysis result
    if [ "$CONSOLIDATED_SERVICES_PRESENT" = true ] && [ "$SERVICE_REGISTRATION_VALID" = true ]; then
        SERVICE_ANALYSIS_PASSED=true
    else
        SERVICE_ANALYSIS_PASSED=false
    fi
    
    if [ "$VERBOSE" = true ]; then
        echo -e "${GRAY}   📊 Service Analysis Results:${NC}"
        echo -e "${GRAY}      Consolidated Services: $CONSOLIDATED_SERVICES_PRESENT${NC}"
        echo -e "${GRAY}      Legacy Services Removed: $LEGACY_SERVICES_REMOVED${NC}"
        echo -e "${GRAY}      Registration Valid: $SERVICE_REGISTRATION_VALID${NC}"
    fi
fi

# Phase 4: Integration Tests (if requested or Deep validation)
if ([ "$INTEGRATION_TESTS" = true ] || [ "$DEEP" = true ]) && [ "$UNIT_TESTS_PASSED" = true ]; then
    echo ""
    echo -e "${BLUE}🔗 Phase 4: Integration Test Validation${NC}"
    
    echo -e "${WHITE}🔍 Running integration tests...${NC}"
    
    INTEGRATION_TEST_PROJECT="tests/Integration/Qobuzarr.IntegrationTests.csproj"
    
    if [ -f "$INTEGRATION_TEST_PROJECT" ]; then
        if INTEGRATION_TEST_OUTPUT=$(dotnet test "$INTEGRATION_TEST_PROJECT" --no-build --verbosity normal 2>&1); then
            echo -e "${GREEN}✅ Integration tests passed${NC}"
            INTEGRATION_TESTS_PASSED=true
        else
            echo -e "${RED}❌ Integration tests failed${NC}"
            ISSUES+=("Integration tests failed")
            
            if [ "$VERBOSE" = true ]; then
                echo -e "${GRAY}Integration test output:${NC}"
                echo "$INTEGRATION_TEST_OUTPUT" | sed 's/^/   /'
            fi
        fi
    else
        echo -e "${BLUE}ℹ️ No integration test project found - skipping${NC}"
    fi
fi

# Phase 5: Session Validation (if requested or Deep validation)
if ([ "$SESSION_VALIDATION" = true ] || [ "$DEEP" = true ]) && [ "$BUILD_PASSED" = true ]; then
    echo ""
    echo -e "${BLUE}🔐 Phase 5: Session Migration Validation${NC}"
    
    echo -e "${WHITE}🔍 Validating session integrity...${NC}"
    
    SESSION_DIR=".qobuz-sessions"
    SESSION_DIRECTORY_EXISTS=false
    SESSIONS_VALID=false
    
    if [ -d "$SESSION_DIR" ]; then
        SESSION_DIRECTORY_EXISTS=true
        
        SESSION_FILES=($(find "$SESSION_DIR" -name "*.json" -type f 2>/dev/null))
        echo -e "${WHITE}📄 Found ${#SESSION_FILES[@]} session files${NC}"
        
        VALID_SESSIONS=0
        INVALID_SESSIONS=0
        
        for session_file in "${SESSION_FILES[@]}"; do
            if command -v jq >/dev/null 2>&1; then
                # Use jq for JSON validation if available
                if SESSION_ID=$(jq -r '.sessionId' "$session_file" 2>/dev/null) && \
                   USER_ID=$(jq -r '.userId' "$session_file" 2>/dev/null); then
                    if [ "$SESSION_ID" != "null" ] && [ "$USER_ID" != "null" ]; then
                        ((VALID_SESSIONS++))
                    else
                        ((INVALID_SESSIONS++))
                        ISSUES+=("Invalid session: $(basename "$session_file")")
                    fi
                else
                    ((INVALID_SESSIONS++))
                    ISSUES+=("Corrupted session: $(basename "$session_file")")
                fi
            else
                # Basic JSON validation without jq
                if grep -q '"sessionId"' "$session_file" && grep -q '"userId"' "$session_file"; then
                    ((VALID_SESSIONS++))
                else
                    ((INVALID_SESSIONS++))
                    ISSUES+=("Invalid session: $(basename "$session_file")")
                fi
            fi
        done
        
        if [ ${#SESSION_FILES[@]} -gt 0 ] && [ $INVALID_SESSIONS -eq 0 ]; then
            echo -e "${GREEN}✅ All sessions valid${NC}"
            SESSIONS_VALID=true
        elif [ ${#SESSION_FILES[@]} -eq 0 ]; then
            echo -e "${BLUE}ℹ️ No sessions found (fresh installation)${NC}"
            SESSIONS_VALID=true
        else
            echo -e "${RED}❌ $INVALID_SESSIONS invalid sessions found${NC}"
            SUCCESS=false
        fi
        
        if [ "$VERBOSE" = true ]; then
            echo -e "${GRAY}   📊 Session Summary:${NC}"
            echo -e "${GRAY}      Total Sessions: ${#SESSION_FILES[@]}${NC}"
            echo -e "${GRAY}      Valid Sessions: $VALID_SESSIONS${NC}"
            echo -e "${GRAY}      Invalid Sessions: $INVALID_SESSIONS${NC}"
        fi
        
    else
        echo -e "${BLUE}ℹ️ No session directory found - fresh installation${NC}"
        SESSIONS_VALID=true
    fi
    
    if [ ${#ISSUES[@]} -eq 0 ] || [ "$SESSIONS_VALID" = true ]; then
        SESSION_VALIDATION_PASSED=true
    else
        SESSION_VALIDATION_PASSED=false
    fi
fi

# Validation Summary
VALIDATION_END=$(date +%s)
VALIDATION_DURATION=$((VALIDATION_END - VALIDATION_START))

echo ""
echo -e "${GREEN}📊 Service Validation Summary${NC}"
echo -e "${GREEN}=============================${NC}"

echo ""
echo -e "${CYAN}🎯 Validation Results:${NC}"
echo -e "${WHITE}   Validation Level: $VALIDATION_LEVEL${NC}"

if [ "$BUILD_PASSED" = true ]; then
    echo -e "${GREEN}   Build Passed: $BUILD_PASSED${NC}"
else
    echo -e "${RED}   Build Passed: $BUILD_PASSED${NC}"
fi

if [ "$UNIT_TESTS_PASSED" = true ]; then
    echo -e "${GREEN}   Unit Tests Passed: $UNIT_TESTS_PASSED${NC}"
else
    echo -e "${RED}   Unit Tests Passed: $UNIT_TESTS_PASSED${NC}"
fi

if [ "$QUICK" = false ]; then
    if [ "$SERVICE_ANALYSIS_PASSED" = true ]; then
        echo -e "${GREEN}   Service Analysis Passed: $SERVICE_ANALYSIS_PASSED${NC}"
    else
        echo -e "${RED}   Service Analysis Passed: $SERVICE_ANALYSIS_PASSED${NC}"
    fi
fi

if [ "$INTEGRATION_TESTS" = true ] || [ "$DEEP" = true ]; then
    if [ "$INTEGRATION_TESTS_PASSED" = true ]; then
        echo -e "${GREEN}   Integration Tests Passed: $INTEGRATION_TESTS_PASSED${NC}"
    else
        echo -e "${RED}   Integration Tests Passed: $INTEGRATION_TESTS_PASSED${NC}"
    fi
fi

if [ "$SESSION_VALIDATION" = true ] || [ "$DEEP" = true ]; then
    if [ "$SESSION_VALIDATION_PASSED" = true ]; then
        echo -e "${GREEN}   Session Validation Passed: $SESSION_VALIDATION_PASSED${NC}"
    else
        echo -e "${RED}   Session Validation Passed: $SESSION_VALIDATION_PASSED${NC}"
    fi
fi

if [ "$SUCCESS" = true ]; then
    echo -e "${GREEN}   Overall Success: $SUCCESS${NC}"
else
    echo -e "${RED}   Overall Success: $SUCCESS${NC}"
fi

echo -e "${WHITE}   Validation Time: $VALIDATION_DURATION seconds${NC}"

if [ ${#ISSUES[@]} -gt 0 ]; then
    echo ""
    echo -e "${RED}❌ Issues Found:${NC}"
    for issue in "${ISSUES[@]}"; do
        echo -e "${RED}   - $issue${NC}"
    done
fi

echo ""
if [ "$SUCCESS" = true ]; then
    echo -e "${GREEN}✅ Service validation completed successfully!${NC}"
    
    echo ""
    echo -e "${CYAN}💡 Migration Status:${NC}"
    echo -e "${WHITE}• Service migration appears to be working correctly${NC}"
    echo -e "${WHITE}• All critical validations passed${NC}"
    echo -e "${WHITE}• System is ready for production use${NC}"
    
    if [ "$DEEP" = false ]; then
        echo -e "${WHITE}• Consider running deep validation for comprehensive analysis${NC}"
    fi
    
else
    echo -e "${RED}❌ Service validation failed${NC}"
    
    echo ""
    echo -e "${CYAN}🔧 Recommended Actions:${NC}"
    echo -e "${WHITE}• Review and fix issues listed above${NC}"
    echo -e "${WHITE}• Check migration was completed correctly${NC}"
    echo -e "${WHITE}• Consider rolling back if issues are severe${NC}"
    echo -e "${WHITE}• Run validation again after fixes${NC}"
fi

# Export detailed report if requested
if [ "$EXPORT_REPORT" = true ]; then
    if [ -z "$REPORT_PATH" ]; then
        REPORT_PATH="service-validation-report_$(date +%Y%m%d_%H%M%S).json"
    fi
    
    # Create JSON report
    cat > "$REPORT_PATH" << EOF
{
  "validationTime": "$(date -Iseconds)",
  "duration": $VALIDATION_DURATION,
  "results": {
    "validationLevel": "$VALIDATION_LEVEL",
    "success": $SUCCESS,
    "buildPassed": $BUILD_PASSED,
    "unitTestsPassed": $UNIT_TESTS_PASSED,
    "integrationTestsPassed": $INTEGRATION_TESTS_PASSED,
    "serviceAnalysisPassed": $SERVICE_ANALYSIS_PASSED,
    "sessionValidationPassed": $SESSION_VALIDATION_PASSED,
    "issues": $(printf '%s\n' "${ISSUES[@]}" | jq -R . | jq -s .)
  },
  "projectRoot": "$(pwd)",
  "bashVersion": "$BASH_VERSION"
}
EOF
    
    if [ $? -eq 0 ]; then
        echo ""
        echo -e "${GREEN}📄 Detailed validation report exported: $REPORT_PATH${NC}"
    else
        echo ""
        echo -e "${RED}❌ Failed to export report${NC}"
    fi
fi

echo ""
echo -e "${GREEN}🎉 Service validation completed!${NC}"

# Exit with appropriate code
if [ "$SUCCESS" = true ]; then
    exit 0
else
    exit 1
fi