#!/usr/bin/env bash
# Category Drift Detection Script
# Ensures all test categories used in code are documented and filtered appropriately.
#
# Usage: ./scripts/check-test-categories.sh
# Exit codes: 0 = all categories documented, 1 = undocumented categories found

set -euo pipefail

# Documented categories (must match docs/TESTING.md and CI_TEST_FILTER)
DOCUMENTED_CATEGORIES=(
    "Integration"
    "Performance"
    "LiveIntegration"
    "Quarantined"
    "Slow"
    "Benchmark"
    "Stress"
    "Simulations"
    "Unit"
    "Unquarantined"
)

# Find all Category traits in test files
echo "Scanning for test categories..."

FOUND_CATEGORIES=$(grep -roh '\[Trait("Category",\s*"[^"]*"\)' tests/ 2>/dev/null | \
    sed 's/\[Trait("Category",\s*"\([^"]*\)".*/\1/' | \
    sort -u || true)

if [ -z "$FOUND_CATEGORIES" ]; then
    echo "No Category traits found in tests/"
    exit 0
fi

echo "Found categories:"
echo "$FOUND_CATEGORIES" | sed 's/^/  - /'
echo ""

# Check for undocumented categories
UNDOCUMENTED=()
for cat in $FOUND_CATEGORIES; do
    FOUND=0
    for doc in "${DOCUMENTED_CATEGORIES[@]}"; do
        if [ "$cat" = "$doc" ]; then
            FOUND=1
            break
        fi
    done
    if [ $FOUND -eq 0 ]; then
        UNDOCUMENTED+=("$cat")
    fi
done

if [ ${#UNDOCUMENTED[@]} -gt 0 ]; then
    echo "ERROR: Undocumented test categories found:"
    for cat in "${UNDOCUMENTED[@]}"; do
        echo "  - $cat"
    done
    echo ""
    echo "Please add these categories to:"
    echo "  1. docs/TESTING.md (category table)"
    echo "  2. CI_TEST_FILTER in workflow env vars (if should be excluded)"
    echo "  3. tests/Default.runsettings TestCaseFilter (if should be excluded by default)"
    echo "  4. This script's DOCUMENTED_CATEGORIES array"
    exit 1
fi

echo "All categories are documented."
exit 0
