#!/bin/bash
# =============================================================================
# Qobuzarr Version Management Script (Bash)
# =============================================================================
# Single source of truth for version management

set -e

ACTION="${1:-show}"
VERSION_FILE="VERSION"

show_help() {
    echo "🔢 Qobuzarr Version Management"
    echo ""
    echo "USAGE:"
    echo "  ./version.sh [action] [options]"
    echo ""
    echo "ACTIONS:"
    echo "  show                  Show current version (default)"
    echo "  set <version>         Set specific version (e.g., 1.0.0)"
    echo "  bump major|minor|patch Increment version component"
    echo "  suffix <text>         Add pre-release suffix (e.g., -beta1)"
    echo "  release               Remove pre-release suffix for final release"
    echo ""
    echo "EXAMPLES:"
    echo "  ./version.sh                     # Show: 0.1.0"
    echo "  ./version.sh set 1.0.0           # Set to 1.0.0"
    echo "  ./version.sh bump major          # 1.0.0 -> 2.0.0"
    echo "  ./version.sh bump minor          # 1.0.0 -> 1.1.0"
    echo "  ./version.sh bump patch          # 1.0.0 -> 1.0.1"
    echo "  ./version.sh suffix beta1        # 1.0.0 -> 1.0.0-beta1"
    echo "  ./version.sh release             # 1.0.0-beta1 -> 1.0.0"
    echo ""
    echo "RELEASE WORKFLOW:"
    echo "1. ./version.sh set 1.0.0-beta1"
    echo "2. git tag v1.0.0-beta1 && git push --tags"
    echo "3. CI automatically builds and creates GitHub release"
    echo ""
}

get_current_version() {
    if [ -f "$VERSION_FILE" ]; then
        cat "$VERSION_FILE" | tr -d '\n\r'
    else
        echo "0.1.0"
    fi
}

case "$ACTION" in
    "help"|"--help"|"-h")
        show_help
        exit 0
        ;;
    
    "show")
        echo "Current version: $(get_current_version)"
        ;;
    
    "set")
        NEW_VERSION="$2"
        if [ -z "$NEW_VERSION" ]; then
            echo "❌ Error: Please specify a version"
            echo "   Usage: ./version.sh set 1.0.0"
            exit 1
        fi
        
        echo "$NEW_VERSION" > "$VERSION_FILE"
        echo "✅ Version set to: $NEW_VERSION"
        ;;
    
    "bump")
        COMPONENT="${2:-patch}"
        CURRENT=$(get_current_version)
        
        # Parse current version
        if [[ $CURRENT =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(.*)$ ]]; then
            MAJOR="${BASH_REMATCH[1]}"
            MINOR="${BASH_REMATCH[2]}"
            PATCH="${BASH_REMATCH[3]}"
            SUFFIX="${BASH_REMATCH[4]}"
            
            case "$COMPONENT" in
                "major")
                    MAJOR=$((MAJOR + 1))
                    MINOR=0
                    PATCH=0
                    ;;
                "minor")
                    MINOR=$((MINOR + 1))
                    PATCH=0
                    ;;
                "patch")
                    PATCH=$((PATCH + 1))
                    ;;
                *)
                    echo "❌ Error: Invalid component '$COMPONENT'"
                    echo "   Valid options: major, minor, patch"
                    exit 1
                    ;;
            esac
            
            NEW_VERSION="$MAJOR.$MINOR.$PATCH$SUFFIX"
            echo "$NEW_VERSION" > "$VERSION_FILE"
            echo "✅ Version bumped: $CURRENT -> $NEW_VERSION"
        else
            echo "❌ Error: Invalid version format in VERSION file"
            exit 1
        fi
        ;;
    
    "suffix")
        SUFFIX_TEXT="$2"
        if [ -z "$SUFFIX_TEXT" ]; then
            echo "❌ Error: Please specify a suffix"
            echo "   Usage: ./version.sh suffix beta1"
            exit 1
        fi
        
        CURRENT=$(get_current_version)
        # Remove existing suffix and add new one
        BASE_VERSION=$(echo "$CURRENT" | sed 's/-.*$//')
        NEW_VERSION="$BASE_VERSION-$SUFFIX_TEXT"
        echo "$NEW_VERSION" > "$VERSION_FILE"
        echo "✅ Version updated: $CURRENT -> $NEW_VERSION"
        ;;
    
    "release")
        CURRENT=$(get_current_version)
        # Remove pre-release suffix
        RELEASE_VERSION=$(echo "$CURRENT" | sed 's/-.*$//')
        echo "$RELEASE_VERSION" > "$VERSION_FILE"
        echo "✅ Released: $CURRENT -> $RELEASE_VERSION"
        ;;
    
    *)
        echo "❌ Error: Unknown action '$ACTION'"
        echo "   Valid actions: show, set, bump, suffix, release"
        echo "   Use --help for more information"
        exit 1
        ;;
esac