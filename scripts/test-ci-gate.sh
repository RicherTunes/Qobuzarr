#!/bin/bash
# Local test of the Multi-Plugin CI Gate verification logic
# Tests the same checks that the GitHub Actions workflow performs

set -euo pipefail

echo "=== Multi-Plugin CI Gate - Local Test ==="
echo ""

# Get API key from running container
CONTAINER="multi-plugin-test"
APIKEY=$(docker exec "$CONTAINER" sh -c "sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml")

if [ -z "$APIKEY" ]; then
    echo "ERROR: Could not get API key from container $CONTAINER"
    exit 1
fi

echo "Using API Key: ${APIKEY:0:4}..."
echo ""

# Initialize results
BRAINARR_LOADED="false"
QOBUZARR_LOADED="false"
TIDALARR_LOADED="false"

echo "=== Checking Brainarr (Import List) ==="
IMPORTLIST_SCHEMA=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/importlist/schema" 2>&1)
if grep -qi "brainarr" <<< "$IMPORTLIST_SCHEMA"; then
    echo "  PASS: Brainarr import list detected"
    BRAINARR_LOADED="true"
else
    echo "  FAIL: Brainarr NOT detected in import list schema"
fi

echo ""
echo "=== Checking Qobuzarr (Indexer + Download Client) ==="
INDEXER_OK="false"
CLIENT_OK="false"

INDEXER_SCHEMA=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/indexer/schema" 2>&1)
if grep -qi "QobuzIndexer" <<< "$INDEXER_SCHEMA"; then
    echo "  PASS: QobuzIndexer detected"
    INDEXER_OK="true"
else
    echo "  FAIL: QobuzIndexer NOT detected"
fi

DOWNLOADCLIENT_SCHEMA=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/downloadclient/schema" 2>&1)
if grep -qi "QobuzDownloadClient" <<< "$DOWNLOADCLIENT_SCHEMA"; then
    echo "  PASS: QobuzDownloadClient detected"
    CLIENT_OK="true"
else
    echo "  FAIL: QobuzDownloadClient NOT detected"
fi

if [ "$INDEXER_OK" = "true" ] && [ "$CLIENT_OK" = "true" ]; then
    QOBUZARR_LOADED="true"
fi

echo ""
echo "=== Checking Tidalarr (Download Client) ==="
if grep -qi "TidalarrDownloadClient" <<< "$DOWNLOADCLIENT_SCHEMA"; then
    echo "  PASS: TidalarrDownloadClient detected"
    TIDALARR_LOADED="true"
else
    echo "  FAIL: TidalarrDownloadClient NOT detected"
fi

echo ""
echo "=== Checking Delay Profiles (Protocol Registration) ==="
PROFILES=$(curl -fsS -H "X-Api-Key: $APIKEY" "http://localhost:8787/api/v1/delayprofile" 2>&1)
echo "Registered protocols:"
grep -oE '"protocol":"[^"]+"' <<< "$PROFILES" | sort -u | sed 's/^/  /' || true

echo ""
echo "=========================================="
echo "           CI GATE RESULTS"
echo "=========================================="
echo ""
printf "  Brainarr:  %s\n" "$BRAINARR_LOADED"
printf "  Qobuzarr:  %s\n" "$QOBUZARR_LOADED"
printf "  Tidalarr:  %s\n" "$TIDALARR_LOADED"
echo ""

# Determine overall pass/fail
ALL_PASSED="false"
if [ "$BRAINARR_LOADED" = "true" ] && [ "$QOBUZARR_LOADED" = "true" ] && [ "$TIDALARR_LOADED" = "true" ]; then
    ALL_PASSED="true"
fi

if [ "$ALL_PASSED" = "true" ]; then
    echo "=========================================="
    echo "  CI GATE: PASSED"
    echo "  All plugins loaded successfully!"
    echo "=========================================="
    exit 0
else
    echo "=========================================="
    echo "  CI GATE: FAILED"
    echo "  Some plugins did not load!"
    echo "=========================================="
    exit 1
fi
