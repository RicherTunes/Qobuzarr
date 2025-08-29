#!/bin/bash
# GitHub Build Monitor Script - Linux/macOS version
# Run this periodically to check build status

LIMIT=${1:-5}
SHOW_DETAILS=${2:-false}
CONTINUOUS=${3:-false}  
INTERVAL_MINUTES=${4:-10}

check_build_status() {
    echo "🔍 Checking build status..."
    
    # Get recent workflow runs
    runs=$(gh run list --limit $LIMIT --json status,conclusion,workflowName,createdAt,headSha,url)
    
    if [ -z "$runs" ] || [ "$runs" = "null" ] || [ "$runs" = "[]" ]; then
        echo "❌ No workflow runs found"
        return 1
    fi
    
    failed_count=0
    success_count=0
    progress_count=0
    
    # Parse JSON and display results
    echo "$runs" | jq -r '.[] | "\(.workflowName)|\(.conclusion // .status)|\(.createdAt)|\(.url)|\(.headSha)"' | while IFS='|' read -r workflow conclusion created url sha; do
        time=$(date -d "$created" "+%Y-%m-%d %H:%M:%S" 2>/dev/null || date -j -f "%Y-%m-%dT%H:%M:%SZ" "$created" "+%Y-%m-%d %H:%M:%S" 2>/dev/null || echo "$created")
        
        case $conclusion in
            "success")
                echo "✅ $workflow - $conclusion ($time)"
                ((success_count++))
                ;;
            "failure")
                echo "❌ $workflow - $conclusion ($time)"
                if [ "$SHOW_DETAILS" = "true" ]; then
                    echo "   URL: $url"
                fi
                ((failed_count++))
                ;;
            "in_progress")
                echo "🔄 $workflow - in progress ($time)"
                ((progress_count++))
                ;;
            *)
                echo "⚠️  $workflow - $conclusion ($time)"
                ;;
        esac
    done
    
    echo ""
    echo "📊 Summary:"
    echo "   ✅ Successful: $success_count"
    echo "   ❌ Failed: $failed_count"  
    echo "   🔄 In Progress: $progress_count"
    
    return $failed_count
}

# Check if required tools are installed
if ! command -v gh &> /dev/null; then
    echo "❌ GitHub CLI (gh) is not installed. Please install it first."
    exit 1
fi

if ! command -v jq &> /dev/null; then
    echo "❌ jq is not installed. Please install it first."
    exit 1
fi

# Main execution
if [ "$CONTINUOUS" = "true" ]; then
    echo "🔄 Starting continuous monitoring (every $INTERVAL_MINUTES minutes)..."
    echo "Press Ctrl+C to stop"
    echo ""
    
    while true; do
        check_build_status
        exit_code=$?
        
        if [ $exit_code -eq 0 ]; then
            echo ""
            echo "🎉 All builds are healthy!"
        else
            echo ""
            echo "⚠️  Some builds have issues!"
        fi
        
        echo "Next check in $INTERVAL_MINUTES minutes..."
        echo ""
        sleep $((INTERVAL_MINUTES * 60))
    done
else
    check_build_status
fi