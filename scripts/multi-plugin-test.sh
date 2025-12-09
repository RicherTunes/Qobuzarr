#!/bin/bash
# Multi-Plugin Co-existence Test Script
# Tests that all RicherTunes plugins (Brainarr, Qobuzarr, Tidalarr) can load together
# in a single Lidarr instance without assembly conflicts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
GITHUB_ROOT="$(dirname "$PROJECT_ROOT")"
TEST_DIR="$GITHUB_ROOT/multi-plugin-test"
CONTAINER_NAME="multi-plugin-test"
LIDARR_VERSION="${LIDARR_DOCKER_VERSION:-pr-plugins-2.14.2.4786}"

echo "=== Multi-Plugin Co-existence Test ==="
echo ""
echo "This test verifies that Brainarr, Qobuzarr, and Tidalarr can all"
echo "load together in the same Lidarr instance without conflicts."
echo ""

# Create test directory structure
echo "Setting up plugin directory structure..."
mkdir -p "$TEST_DIR/RicherTunes/Brainarr"
mkdir -p "$TEST_DIR/RicherTunes/Qobuzarr"
mkdir -p "$TEST_DIR/RicherTunes/Tidalarr"

# Find and copy plugin DLLs
echo "Copying plugin files..."

# Brainarr
BRAINARR_DLL=$(find "$GITHUB_ROOT/brainarr" -name "Lidarr.Plugin.Brainarr.dll" -path "*/bin/*" 2>/dev/null | head -1)
if [ -n "$BRAINARR_DLL" ]; then
    cp "$BRAINARR_DLL" "$TEST_DIR/RicherTunes/Brainarr/"
    cp "$GITHUB_ROOT/brainarr/plugin.json" "$TEST_DIR/RicherTunes/Brainarr/" 2>/dev/null || true
    echo "  ✓ Brainarr"
else
    echo "  ✗ Brainarr DLL not found - please build first"
fi

# Qobuzarr
QOBUZARR_DLL=$(find "$GITHUB_ROOT/qobuzarr" -name "Lidarr.Plugin.Qobuzarr.dll" -path "*/bin/*" 2>/dev/null | head -1)
if [ -n "$QOBUZARR_DLL" ]; then
    cp "$QOBUZARR_DLL" "$TEST_DIR/RicherTunes/Qobuzarr/"
    cp "$GITHUB_ROOT/qobuzarr/plugin.json" "$TEST_DIR/RicherTunes/Qobuzarr/" 2>/dev/null || true
    echo "  ✓ Qobuzarr"
else
    echo "  ✗ Qobuzarr DLL not found - please build first"
fi

# Tidalarr
TIDALARR_DLL=$(find "$GITHUB_ROOT/tidalarr" -name "Lidarr.Plugin.Tidalarr.dll" -path "*/bin/*" 2>/dev/null | head -1)
if [ -n "$TIDALARR_DLL" ]; then
    cp "$TIDALARR_DLL" "$TEST_DIR/RicherTunes/Tidalarr/"
    cp "$GITHUB_ROOT/tidalarr/plugin.json" "$TEST_DIR/RicherTunes/Tidalarr/" 2>/dev/null || true
    echo "  ✓ Tidalarr"
else
    echo "  ✗ Tidalarr DLL not found - please build first"
fi

echo ""
echo "Staged plugins:"
find "$TEST_DIR" -type f -name "*.dll" -exec ls -lh {} \;

# Stop existing container if running
echo ""
echo "Cleaning up existing container..."
docker stop "$CONTAINER_NAME" 2>/dev/null || true
docker rm "$CONTAINER_NAME" 2>/dev/null || true

# Start new container with all plugins
echo "Starting Lidarr with all plugins..."
docker run -d \
    --name "$CONTAINER_NAME" \
    -p 8787:8686 \
    -v "$TEST_DIR:/config/plugins:ro" \
    "ghcr.io/hotio/lidarr:$LIDARR_VERSION"

# Wait for Lidarr to start
echo "Waiting for Lidarr to initialize..."
for i in {1..60}; do
    if curl -fsS http://localhost:8787 >/dev/null 2>&1; then
        echo "Lidarr is ready!"
        break
    fi
    sleep 2
done

# Get API key
echo ""
echo "Getting API key..."
sleep 5  # Give it a moment to create config
APIKEY=$(docker exec "$CONTAINER_NAME" sh -c "sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml" 2>/dev/null || echo "")
if [ -z "$APIKEY" ]; then
    echo "ERROR: Could not get API key"
    docker logs "$CONTAINER_NAME" 2>&1 | tail -50
    exit 1
fi

echo "API Key: ${APIKEY:0:4}..."

# Test plugin detection
echo ""
echo "=== Plugin Detection Results ==="

# Check indexer schemas
echo ""
echo "Checking indexer schemas..."
INDEXERS=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/indexer/schema" 2>&1)
if echo "$INDEXERS" | grep -q "QobuzIndexer"; then
    echo "  ✓ QobuzIndexer detected"
else
    echo "  ✗ QobuzIndexer NOT detected"
fi

# Check download client schemas
echo ""
echo "Checking download client schemas..."
CLIENTS=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/downloadclient/schema" 2>&1)
if echo "$CLIENTS" | grep -q "QobuzDownloadClient"; then
    echo "  ✓ QobuzDownloadClient detected"
else
    echo "  ✗ QobuzDownloadClient NOT detected"
fi
if echo "$CLIENTS" | grep -q "TidalarrDownloadClient"; then
    echo "  ✓ TidalarrDownloadClient detected"
else
    echo "  ✗ TidalarrDownloadClient NOT detected"
fi

# Check import list schemas
echo ""
echo "Checking import list schemas..."
LISTS=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/importlist/schema" 2>&1)
if echo "$LISTS" | grep -qi "brainarr"; then
    echo "  ✓ Brainarr import list detected"
else
    echo "  ✗ Brainarr import list NOT detected"
fi

# Check delay profiles for custom protocols
echo ""
echo "Checking delay profiles..."
PROFILES=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/delayprofile" 2>&1)
if echo "$PROFILES" | grep -q "QobuzarrDownloadProtocol"; then
    echo "  ✓ QobuzarrDownloadProtocol registered"
else
    echo "  ✗ QobuzarrDownloadProtocol NOT registered"
fi
if echo "$PROFILES" | grep -q "TidalarrProtocol"; then
    echo "  ✓ TidalarrProtocol registered"
else
    echo "  ✗ TidalarrProtocol NOT registered"
fi

# Check for errors in logs
echo ""
echo "Checking for plugin loading errors..."
ERRORS=$(docker logs "$CONTAINER_NAME" 2>&1 | grep -iE "plugin.*error|error.*plugin|exception.*load|reflection" | head -5 || echo "")
if [ -z "$ERRORS" ]; then
    echo "  ✓ No plugin loading errors detected"
else
    echo "  ⚠ Potential issues found:"
    echo "$ERRORS"
fi

echo ""
echo "=== Test Complete ==="
echo ""
echo "Container '$CONTAINER_NAME' is still running."
echo "Access Lidarr UI at: http://localhost:8787"
echo ""
echo "To clean up: docker stop $CONTAINER_NAME && docker rm $CONTAINER_NAME"
