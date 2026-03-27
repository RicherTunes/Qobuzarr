#!/bin/bash

# =============================================================================
# Download Lidarr Plugins Branch Assemblies Script (Bash)
# =============================================================================
# Downloads assemblies from Lidarr plugins branch using Docker container

set -e

# Default parameters
LIDARR_VERSION="${1:-2.13.3.4692}"
OUTPUT_PATH="${2:-ext/Lidarr/_output/net8.0}"
FORCE="${3:-false}"

echo -e "\033[32mDownloading Lidarr Plugins Branch Assemblies\033[0m"
echo -e "\033[36mVersion: pr-plugins-$LIDARR_VERSION\033[0m"
echo -e "\033[36mOutput: $OUTPUT_PATH\033[0m"

# Check if Docker is available
if ! command -v docker &> /dev/null; then
    echo -e "\033[31mERROR: Docker is required but not installed or not in PATH\033[0m"
    echo -e "\033[33mPlease install Docker from: https://docs.docker.com/get-docker/\033[0m"
    exit 1
fi

docker_version=$(docker --version)
echo -e "\033[34mDocker found: $docker_version\033[0m"

# Create output directory
if [ ! -d "$OUTPUT_PATH" ]; then
    mkdir -p "$OUTPUT_PATH"
    echo -e "\033[34mCreated directory: $OUTPUT_PATH\033[0m"
fi

# Check if assemblies already exist
existing_files=("$OUTPUT_PATH/Lidarr.Core.dll" "$OUTPUT_PATH/Lidarr.Common.dll")
all_exist=true

for file in "${existing_files[@]}"; do
    if [ ! -f "$file" ]; then
        all_exist=false
        break
    fi
done

if [ "$all_exist" = true ] && [ "$FORCE" != "true" ] && [ "$FORCE" != "--force" ]; then
    echo -e "\033[33mLidarr plugins branch assemblies already exist. Use --force to re-download.\033[0m"
    exit 0
fi

# Main extraction process
container_image="ghcr.io/hotio/lidarr:pr-plugins-$LIDARR_VERSION"
container_name="lidarr-plugins-extract-$$"
temp_dir="/tmp/lidarr-plugins-extract"

echo -e "\033[34mPulling container image: $container_image\033[0m"
if ! docker pull "$container_image"; then
    echo -e "\033[31mFailed to pull container image\033[0m"
    exit 1
fi

echo -e "\033[34mCreating temporary directory: $temp_dir\033[0m"
if [ -d "$temp_dir" ]; then
    rm -rf "$temp_dir"
fi
mkdir -p "$temp_dir"

echo -e "\033[34mCreating container to extract assemblies...\033[0m"
if ! docker create --name "$container_name" "$container_image"; then
    echo -e "\033[31mFailed to create container\033[0m"
    exit 1
fi

echo -e "\033[34mExtracting assemblies from container...\033[0m"
if ! docker cp "${container_name}:/app" "$temp_dir"; then
    echo -e "\033[33mTrying alternative path /opt/lidarr...\033[0m"
    if ! docker cp "${container_name}:/opt/lidarr" "$temp_dir"; then
        echo -e "\033[31mFailed to extract files from container\033[0m"
        docker rm "$container_name" > /dev/null
        exit 1
    fi
fi

# Clean up container
echo -e "\033[34mCleaning up container...\033[0m"
docker rm "$container_name" > /dev/null

# Find the extracted Lidarr directory
lidarr_dir=""
search_paths=("$temp_dir/app" "$temp_dir/lidarr" "$temp_dir/opt/lidarr")

for search_path in "${search_paths[@]}"; do
    if [ -d "$search_path" ]; then
        lidarr_dir="$search_path"
        echo -e "\033[32mFound Lidarr directory: $lidarr_dir\033[0m"
        break
    fi
done

if [ -z "$lidarr_dir" ]; then
    echo -e "\033[33mAvailable directories in extraction:\033[0m"
    find "$temp_dir" -type d -maxdepth 2 | while read -r dir; do
        echo -e "  - $(basename "$dir")"
    done
    echo -e "\033[31mCould not find Lidarr directory in extracted files\033[0m"
    rm -rf "$temp_dir"
    exit 1
fi

# Copy required assemblies
required_assemblies=("Lidarr.Core.dll" "Lidarr.Common.dll" "Lidarr.Http.dll" "Lidarr.Api.V1.dll" "Lidarr.dll" "Lidarr.Host.dll" "Lidarr.SignalR.dll")
copied_count=0

for assembly in "${required_assemblies[@]}"; do
    source_path="$lidarr_dir/$assembly"
    dest_path="$OUTPUT_PATH/$assembly"
    
    if [ -f "$source_path" ]; then
        cp "$source_path" "$dest_path"
        ((copied_count++))
        echo -e "\033[32mCopied: $assembly\033[0m"
    else
        echo -e "\033[33mOptional assembly not found: $assembly\033[0m"
    fi
done

if [ "$copied_count" -eq 0 ]; then
    echo -e "\033[31mERROR: No assemblies were found in the expected location\033[0m"
    echo -e "\033[33mLidarr directory contents:\033[0m"
    find "$lidarr_dir" -name "*.dll" -exec basename {} \; | while read -r dll; do
        echo -e "  - $dll"
    done
    rm -rf "$temp_dir"
    exit 1
fi

# Cleanup temp directory
rm -rf "$temp_dir"

echo
echo -e "\033[32mLidarr plugins branch assemblies downloaded successfully!\033[0m"
echo -e "\033[37mLocation: $OUTPUT_PATH\033[0m"
echo -e "\033[37mCopied $copied_count assemblies\033[0m"

# Show what was downloaded
echo
echo -e "\033[36mDownloaded plugins branch assemblies:\033[0m"
find "$OUTPUT_PATH" -name "*.dll" -exec ls -lh {} \; | while read -r line; do
    size=$(echo "$line" | awk '{print $5}')
    file=$(basename "$(echo "$line" | awk '{print $NF}')")
    echo -e "  - $file ($size)"
done

echo
echo -e "\033[36mNext steps:\033[0m"
echo -e "\033[37m  - These assemblies are from the plugins branch with string Protocol properties\033[0m"
echo -e "\033[37m  - Update your plugin code to return string from Protocol properties\033[0m"
echo -e "\033[37m  - Run: ./build.sh --deploy\033[0m"