#!/bin/bash
# =============================================================================
# Download Pre-built Lidarr Assemblies Script (Bash)
# =============================================================================
# Alternative to building Lidarr from source - downloads release assemblies

set -e

# Default values
LIDARR_VERSION="2.13.2.4685"
OUTPUT_PATH="ext/Lidarr/_output/net6.0"
FORCE=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
WHITE='\033[1;37m'
GRAY='\033[0;37m'
NC='\033[0m'

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --version)
            LIDARR_VERSION="$2"
            shift 2
            ;;
        --output)
            OUTPUT_PATH="$2"
            shift 2
            ;;
        --force)
            FORCE=true
            shift
            ;;
        --help)
            echo -e "${GREEN}📥 Download Pre-built Lidarr Assemblies${NC}"
            echo ""
            echo -e "${CYAN}USAGE:${NC}"
            echo -e "  ${WHITE}./download-lidarr-assemblies.sh [OPTIONS]${NC}"
            echo ""
            echo -e "${CYAN}OPTIONS:${NC}"
            echo -e "  ${WHITE}--version VERSION    Lidarr version to download (default: $LIDARR_VERSION)${NC}"
            echo -e "  ${WHITE}--output PATH        Output directory (default: $OUTPUT_PATH)${NC}"
            echo -e "  ${WHITE}--force              Force re-download even if files exist${NC}"
            echo -e "  ${WHITE}--help               Show this help${NC}"
            exit 0
            ;;
        *)
            echo -e "${RED}❌ Unknown option: $1${NC}"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

echo -e "${GREEN}📥 Downloading Pre-built Lidarr Assemblies${NC}"
echo -e "${CYAN}Version: $LIDARR_VERSION${NC}"
echo -e "${CYAN}Output: $OUTPUT_PATH${NC}"

# Create output directory
mkdir -p "$OUTPUT_PATH"
echo -e "${BLUE}📁 Created directory: $OUTPUT_PATH${NC}"

# Check if assemblies already exist
required_files=("$OUTPUT_PATH/Lidarr.Core.dll" "$OUTPUT_PATH/Lidarr.Common.dll")
all_exist=true

for file in "${required_files[@]}"; do
    if [[ ! -f "$file" ]]; then
        all_exist=false
        break
    fi
done

if [[ "$all_exist" == true && "$FORCE" == false ]]; then
    echo -e "${YELLOW}✅ Lidarr assemblies already exist. Use --force to re-download.${NC}"
    exit 0
fi

# Download Lidarr release
download_url="https://github.com/Lidarr/Lidarr/releases/download/v$LIDARR_VERSION/Lidarr.develop.$LIDARR_VERSION.linux-core-x64.tar.gz"
archive_path="lidarr-release.tar.gz"

echo -e "${BLUE}📥 Downloading from: $download_url${NC}"
curl -L -o "$archive_path" "$download_url"

# Extract assemblies
echo -e "${BLUE}📦 Extracting Lidarr assemblies...${NC}"
tar -xzf "$archive_path"

# Copy required assemblies
source_dir="Lidarr"
required_assemblies=(
    "Lidarr.Core.dll"
    "Lidarr.Common.dll" 
    "Lidarr.Http.dll"
    "Lidarr.Api.V1.dll"
    "NzbDrone.Core.dll"
    "NzbDrone.Common.dll"
    "NzbDrone.Host.dll"
    "NzbDrone.Api.dll"
    "Microsoft.AspNetCore.Authorization.dll"
    "Microsoft.AspNetCore.Mvc.Core.dll"
    "TagLibSharp.dll"
)

for assembly in "${required_assemblies[@]}"; do
    source_path="$source_dir/$assembly"
    dest_path="$OUTPUT_PATH/$assembly"
    
    if [[ -f "$source_path" ]]; then
        cp "$source_path" "$dest_path"
        echo -e "${GREEN}✅ Copied: $assembly${NC}"
    else
        echo -e "${YELLOW}⚠️ Optional assembly not found: $assembly${NC}"
    fi
done

# Cleanup
rm -f "$archive_path"
rm -rf "$source_dir"

echo ""
echo -e "${GREEN}🎉 Lidarr assemblies downloaded successfully!${NC}"
echo -e "${GRAY}📍 Location: $OUTPUT_PATH${NC}"

# Show what was downloaded
echo ""
echo -e "${CYAN}Downloaded assemblies:${NC}"
for file in "$OUTPUT_PATH"/*.dll; do
    if [[ -f "$file" ]]; then
        filename=$(basename "$file")
        size=$(du -h "$file" | cut -f1)
        echo -e "${WHITE}• $filename ($size)${NC}"
    fi
done

echo ""
echo -e "${CYAN}💡 Next steps:${NC}"
echo -e "${WHITE}• Update project references to use these assemblies${NC}"
echo -e "${WHITE}• Run: ./build.sh --deploy${NC}"