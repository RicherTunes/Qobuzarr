#!/bin/bash
#
# Migrate projects to use centralized package management
# Removes Version attributes from PackageReference elements in .csproj files
#

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

DRY_RUN=false
SHOW_HELP=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --help)
            SHOW_HELP=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

if [ "$SHOW_HELP" = true ]; then
    cat << EOF
Migrate to Central Package Management

This script removes Version attributes from PackageReference elements in .csproj files
to use the centralized versions defined in Directory.Packages.props.

Usage:
  ./migrate-to-central-packages.sh              # Apply changes  
  ./migrate-to-central-packages.sh --dry-run    # Preview changes without modifying files
  ./migrate-to-central-packages.sh --help       # Show this help

EOF
    exit 0
fi

echo -e "${GREEN}🔧 Migrating to centralized package management...${NC}"

# Find all .csproj files (excluding external Lidarr source)
mapfile -t project_files < <(find . -name "*.csproj" -not -path "*/ext/*" -not -path "*/Lidarr-source/*")

if [ ${#project_files[@]} -eq 0 ]; then
    echo -e "${RED}❌ No .csproj files found to migrate${NC}"
    exit 1
fi

echo -e "${CYAN}Found ${#project_files[@]} project files to migrate:${NC}"
for file in "${project_files[@]}"; do
    echo -e "${GRAY}  - $file${NC}"
done
echo ""

total_changes=0

for project_file in "${project_files[@]}"; do
    echo -e "${YELLOW}Processing: $project_file${NC}"
    
    if [ ! -f "$project_file" ]; then
        echo -e "${RED}    ❌ File not found: $project_file${NC}"
        continue
    fi
    
    file_changes=0
    
    # Create a temporary file for processing
    temp_file=$(mktemp)
    cp "$project_file" "$temp_file"
    
    # Use sed to remove Version attributes from PackageReference elements
    # This handles both self-closing and multi-line formats
    if sed -i 's/<PackageReference\([^>]*\) Version="[^"]*"\([^>]*\)>/<PackageReference\1\2>/g' "$temp_file" 2>/dev/null; then
        # Count the differences
        if ! diff -q "$project_file" "$temp_file" >/dev/null 2>&1; then
            file_changes=$(diff "$project_file" "$temp_file" | grep -c "^<" || true)
            
            if [ "$DRY_RUN" = true ]; then
                echo -e "${CYAN}    Would make $file_changes changes:${NC}"
                diff "$project_file" "$temp_file" | grep -E "^[<>].*PackageReference" | head -10 || true
            else
                cp "$temp_file" "$project_file"
                echo -e "${GREEN}    ✅ Updated $project_file ($file_changes changes)${NC}"
            fi
            
            total_changes=$((total_changes + file_changes))
        else
            echo -e "${GRAY}    ℹ️  No changes needed${NC}"
        fi
    else
        echo -e "${RED}    ❌ Error processing file${NC}"
    fi
    
    rm -f "$temp_file"
    echo ""
done

echo -e "${CYAN}Migration Summary:${NC}"
echo -e "  Total changes: $total_changes"

if [ "$DRY_RUN" = true ]; then
    echo -e "${YELLOW}  🔍 Dry run completed - no files were modified${NC}"
    echo -e "${YELLOW}  Run without --dry-run to apply changes${NC}"
else
    echo -e "${GREEN}  ✅ Migration completed!${NC}"
    echo -e "${CYAN}  Next steps:${NC}"
    echo -e "${GRAY}    1. Build the solution to verify everything works: dotnet build${NC}"
    echo -e "${GRAY}    2. Run tests to ensure compatibility: dotnet test${NC}"
    echo -e "${GRAY}    3. All package versions are now managed in Directory.Packages.props${NC}"
fi