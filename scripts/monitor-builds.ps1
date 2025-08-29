# GitHub Build Monitor Script
# Run this periodically to check build status

param(
    [int]$Limit = 5,
    [switch]$ShowDetails,
    [switch]$Continuous,
    [int]$IntervalMinutes = 10
)

function Check-BuildStatus {
    Write-Host "🔍 Checking build status..." -ForegroundColor Cyan
    
    # Get recent workflow runs
    $runs = gh run list --limit $Limit --json status,conclusion,workflowName,createdAt,headSha,url | ConvertFrom-Json
    
    if (-not $runs) {
        Write-Host "❌ No workflow runs found" -ForegroundColor Red
        return
    }
    
    $failedRuns = @()
    $successRuns = @()
    $inProgressRuns = @()
    
    foreach ($run in $runs) {
        $time = [DateTime]::Parse($run.createdAt).ToString("yyyy-MM-dd HH:mm:ss")
        $status = if ($run.conclusion) { $run.conclusion } else { $run.status }
        
        switch ($run.conclusion) {
            "success" { 
                $successRuns += $run
                Write-Host "✅ $($run.workflowName) - $status ($time)" -ForegroundColor Green 
            }
            "failure" { 
                $failedRuns += $run
                Write-Host "❌ $($run.workflowName) - $status ($time)" -ForegroundColor Red 
                if ($ShowDetails) {
                    Write-Host "   URL: $($run.url)" -ForegroundColor Yellow
                }
            }
            default { 
                if ($run.status -eq "in_progress") {
                    $inProgressRuns += $run
                    Write-Host "🔄 $($run.workflowName) - in progress ($time)" -ForegroundColor Yellow 
                } else {
                    Write-Host "⚠️  $($run.workflowName) - $status ($time)" -ForegroundColor Yellow 
                }
            }
        }
    }
    
    # Summary
    Write-Host "`n📊 Summary:" -ForegroundColor Cyan
    Write-Host "   ✅ Successful: $($successRuns.Count)" -ForegroundColor Green
    Write-Host "   ❌ Failed: $($failedRuns.Count)" -ForegroundColor Red  
    Write-Host "   🔄 In Progress: $($inProgressRuns.Count)" -ForegroundColor Yellow
    
    # Show failed run details
    if ($failedRuns.Count -gt 0 -and $ShowDetails) {
        Write-Host "`n🔍 Failed Run Details:" -ForegroundColor Red
        foreach ($run in $failedRuns) {
            Write-Host "   Workflow: $($run.workflowName)" -ForegroundColor White
            Write-Host "   URL: $($run.url)" -ForegroundColor Yellow
            Write-Host "   Commit: $($run.headSha.Substring(0,7))" -ForegroundColor Gray
            Write-Host ""
        }
    }
    
    return $failedRuns.Count -eq 0
}

# Main execution
if ($Continuous) {
    Write-Host "🔄 Starting continuous monitoring (every $IntervalMinutes minutes)..." -ForegroundColor Green
    Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Gray
    
    while ($true) {
        $allGood = Check-BuildStatus
        
        if ($allGood) {
            Write-Host "`n🎉 All builds are healthy!" -ForegroundColor Green
        } else {
            Write-Host "`n⚠️  Some builds have issues!" -ForegroundColor Red
        }
        
        Write-Host "Next check in $IntervalMinutes minutes...`n" -ForegroundColor Gray
        Start-Sleep -Seconds ($IntervalMinutes * 60)
    }
} else {
    Check-BuildStatus | Out-Null
}