# Qobuzarr Deployment Monitor
# Real-time monitoring and metrics collection for plugin deployments

param(
    [Parameter(Mandatory=$false)]
    [string]$LidarrUrl = "http://localhost:7878",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = $env:LIDARR_API_KEY,
    
    [Parameter(Mandatory=$false)]
    [string]$PluginPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr",
    
    [Parameter(Mandatory=$false)]
    [string]$MetricsPath = "./deployment-metrics",
    
    [Parameter(Mandatory=$false)]
    [switch]$Continuous = $false,
    
    [Parameter(Mandatory=$false)]
    [int]$IntervalSeconds = 30,
    
    [Parameter(Mandatory=$false)]
    [switch]$SendAlerts = $false
)

# Initialize monitoring
$ErrorActionPreference = "Continue"
$script:StartTime = Get-Date
$script:MetricsData = @{
    DeploymentStart = $script:StartTime
    Checks = @()
    Errors = @()
    Performance = @{}
}

# Colors for output
function Write-Success { Write-Host $args[0] -ForegroundColor Green }
function Write-Warning { Write-Host $args[0] -ForegroundColor Yellow }
function Write-Error { Write-Host $args[0] -ForegroundColor Red }
function Write-Info { Write-Host $args[0] -ForegroundColor Cyan }
function Write-Metric { Write-Host $args[0] -ForegroundColor Magenta }

# Ensure metrics directory exists
if (!(Test-Path $MetricsPath)) {
    New-Item -ItemType Directory -Path $MetricsPath -Force | Out-Null
}

# Function to check plugin file integrity
function Test-PluginIntegrity {
    Write-Info "`n🔍 Checking plugin integrity..."
    
    $result = @{
        Timestamp = Get-Date
        CheckType = "PluginIntegrity"
        Status = "Unknown"
        Details = @{}
    }
    
    # Check main DLL
    $mainDll = Join-Path $PluginPath "Lidarr.Plugin.Qobuzarr.dll"
    if (Test-Path $mainDll) {
        $dllInfo = Get-Item $mainDll
        $result.Details.MainDLL = @{
            Exists = $true
            Size = $dllInfo.Length
            LastModified = $dllInfo.LastWriteTime
            Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($mainDll).FileVersion
        }
        Write-Success "✅ Main DLL found: $($result.Details.MainDLL.Version)"
    } else {
        $result.Details.MainDLL = @{ Exists = $false }
        Write-Error "❌ Main DLL not found!"
        $result.Status = "Failed"
        return $result
    }
    
    # Check plugin.json
    $pluginJson = Join-Path $PluginPath "plugin.json"
    if (Test-Path $pluginJson) {
        $jsonContent = Get-Content $pluginJson | ConvertFrom-Json
        $result.Details.PluginJson = @{
            Exists = $true
            Name = $jsonContent.name
            Version = $jsonContent.version
            MinimumVersion = $jsonContent.minimumVersion
        }
        Write-Success "✅ plugin.json found: v$($jsonContent.version)"
    } else {
        $result.Details.PluginJson = @{ Exists = $false }
        Write-Warning "⚠️ plugin.json not found"
    }
    
    # Check dependencies
    $dependencies = @(
        "Newtonsoft.Json.dll",
        "NLog.dll",
        "FluentValidation.dll"
    )
    
    $result.Details.Dependencies = @{}
    $missingDeps = @()
    
    foreach ($dep in $dependencies) {
        $depPath = Join-Path $PluginPath $dep
        if (Test-Path $depPath) {
            $result.Details.Dependencies[$dep] = $true
        } else {
            $result.Details.Dependencies[$dep] = $false
            $missingDeps += $dep
        }
    }
    
    if ($missingDeps.Count -eq 0) {
        Write-Success "✅ All dependencies present"
        $result.Status = "Success"
    } else {
        Write-Warning "⚠️ Missing dependencies: $($missingDeps -join ', ')"
        $result.Status = "PartialSuccess"
    }
    
    return $result
}

# Function to check Lidarr API health
function Test-LidarrApi {
    Write-Info "`n🌐 Checking Lidarr API..."
    
    $result = @{
        Timestamp = Get-Date
        CheckType = "LidarrAPI"
        Status = "Unknown"
        Details = @{}
    }
    
    if ([string]::IsNullOrEmpty($ApiKey)) {
        Write-Warning "⚠️ No API key provided, skipping API checks"
        $result.Status = "Skipped"
        return $result
    }
    
    try {
        $headers = @{
            "X-Api-Key" = $ApiKey
        }
        
        # Check system status
        $systemResponse = Invoke-RestMethod -Uri "$LidarrUrl/api/v1/system/status" -Headers $headers -TimeoutSec 5
        $result.Details.System = @{
            Version = $systemResponse.version
            Branch = $systemResponse.branch
            Authentication = $systemResponse.authentication
            StartupPath = $systemResponse.startupPath
        }
        Write-Success "✅ Lidarr API responsive: v$($systemResponse.version)"
        
        # Check for our plugin
        $indexersResponse = Invoke-RestMethod -Uri "$LidarrUrl/api/v1/indexer" -Headers $headers -TimeoutSec 5
        $ourPlugin = $indexersResponse | Where-Object { $_.implementation -like "*Qobuz*" }
        
        if ($ourPlugin) {
            $result.Details.Plugin = @{
                Found = $true
                Name = $ourPlugin.name
                Enabled = $ourPlugin.enable
                Priority = $ourPlugin.priority
            }
            Write-Success "✅ Qobuzarr plugin detected in Lidarr"
            $result.Status = "Success"
        } else {
            $result.Details.Plugin = @{ Found = $false }
            Write-Warning "⚠️ Qobuzarr plugin not found in indexers"
            $result.Status = "PartialSuccess"
        }
        
    } catch {
        Write-Error "❌ Failed to connect to Lidarr API: $_"
        $result.Status = "Failed"
        $result.Details.Error = $_.ToString()
    }
    
    return $result
}

# Function to measure plugin performance
function Measure-PluginPerformance {
    Write-Info "`n📊 Measuring plugin performance..."
    
    $result = @{
        Timestamp = Get-Date
        CheckType = "Performance"
        Status = "Unknown"
        Metrics = @{}
    }
    
    if ([string]::IsNullOrEmpty($ApiKey)) {
        Write-Warning "⚠️ No API key provided, skipping performance checks"
        $result.Status = "Skipped"
        return $result
    }
    
    try {
        $headers = @{
            "X-Api-Key" = $ApiKey
        }
        
        # Measure search response time
        Write-Info "Testing search performance..."
        $searchStart = Get-Date
        $searchBody = @{
            term = "Miles Davis"
        } | ConvertTo-Json
        
        $searchResponse = Invoke-WebRequest -Uri "$LidarrUrl/api/v1/search" `
            -Headers $headers `
            -Method POST `
            -Body $searchBody `
            -ContentType "application/json" `
            -TimeoutSec 30
            
        $searchTime = (Get-Date) - $searchStart
        $result.Metrics.SearchResponseTime = $searchTime.TotalMilliseconds
        
        Write-Metric "📈 Search response time: $([math]::Round($searchTime.TotalMilliseconds, 2))ms"
        
        # Check memory usage (if available from system endpoint)
        $systemResponse = Invoke-RestMethod -Uri "$LidarrUrl/api/v1/system/status" -Headers $headers
        if ($systemResponse.runtimeVersion) {
            $result.Metrics.RuntimeVersion = $systemResponse.runtimeVersion
        }
        
        # Determine performance status
        if ($searchTime.TotalMilliseconds -lt 1000) {
            Write-Success "✅ Excellent performance (<1s)"
            $result.Status = "Excellent"
        } elseif ($searchTime.TotalMilliseconds -lt 3000) {
            Write-Success "✅ Good performance (<3s)"
            $result.Status = "Good"
        } elseif ($searchTime.TotalMilliseconds -lt 5000) {
            Write-Warning "⚠️ Acceptable performance (<5s)"
            $result.Status = "Acceptable"
        } else {
            Write-Error "❌ Poor performance (>5s)"
            $result.Status = "Poor"
        }
        
    } catch {
        Write-Error "❌ Performance measurement failed: $_"
        $result.Status = "Failed"
        $result.Metrics.Error = $_.ToString()
    }
    
    return $result
}

# Function to check for common issues
function Test-CommonIssues {
    Write-Info "`n🔧 Checking for common issues..."
    
    $issues = @()
    
    # Check for version mismatch
    $mainDll = Join-Path $PluginPath "Lidarr.Plugin.Qobuzarr.dll"
    if (Test-Path $mainDll) {
        $dllVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($mainDll).FileVersion
        $pluginJsonPath = Join-Path $PluginPath "plugin.json"
        
        if (Test-Path $pluginJsonPath) {
            $jsonContent = Get-Content $pluginJsonPath | ConvertFrom-Json
            if ($dllVersion -ne $jsonContent.version) {
                $issues += "Version mismatch: DLL=$dllVersion, JSON=$($jsonContent.version)"
                Write-Warning "⚠️ Version mismatch detected"
            }
        }
    }
    
    # Check for ReflectionTypeLoadException indicators
    $lidarrLogPath = Join-Path (Split-Path $PluginPath -Parent -Parent -Parent) "logs\lidarr.txt"
    if (Test-Path $lidarrLogPath) {
        $recentLogs = Get-Content $lidarrLogPath -Tail 100
        if ($recentLogs -match "ReflectionTypeLoadException.*Qobuzarr") {
            $issues += "ReflectionTypeLoadException detected in logs"
            Write-Error "❌ Plugin loading errors detected"
        }
    }
    
    # Check disk space
    $drive = (Get-Item $PluginPath).PSDrive
    $freeSpace = (Get-PSDrive $drive).Free
    $freeSpaceGB = [math]::Round($freeSpace / 1GB, 2)
    
    if ($freeSpaceGB -lt 1) {
        $issues += "Low disk space: ${freeSpaceGB}GB free"
        Write-Warning "⚠️ Low disk space"
    }
    
    if ($issues.Count -eq 0) {
        Write-Success "✅ No common issues detected"
    }
    
    return @{
        Timestamp = Get-Date
        CheckType = "CommonIssues"
        Issues = $issues
        Status = if ($issues.Count -eq 0) { "Success" } else { "HasIssues" }
    }
}

# Function to save metrics
function Save-Metrics {
    $metricsFile = Join-Path $MetricsPath "deployment-$(Get-Date -Format 'yyyy-MM-dd-HH-mm-ss').json"
    $script:MetricsData | ConvertTo-Json -Depth 10 | Set-Content $metricsFile
    Write-Info "💾 Metrics saved to: $metricsFile"
}

# Function to send alerts
function Send-Alert {
    param(
        [string]$Message,
        [string]$Severity = "Info"
    )
    
    if (!$SendAlerts) { return }
    
    # Here you would implement actual alerting logic
    # For example: webhook to Discord, Slack, email, etc.
    Write-Warning "🔔 Alert ($Severity): $Message"
}

# Main monitoring loop
function Start-Monitoring {
    Write-Success "`n🚀 Starting Qobuzarr Deployment Monitor"
    Write-Info "Plugin Path: $PluginPath"
    Write-Info "Lidarr URL: $LidarrUrl"
    Write-Info "Continuous: $Continuous"
    if ($Continuous) {
        Write-Info "Check Interval: ${IntervalSeconds}s"
    }
    
    do {
        Write-Info "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        Write-Info "🕐 Check started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        
        # Run all checks
        $integrityCheck = Test-PluginIntegrity
        $script:MetricsData.Checks += $integrityCheck
        
        $apiCheck = Test-LidarrApi
        $script:MetricsData.Checks += $apiCheck
        
        if ($apiCheck.Status -eq "Success") {
            $perfCheck = Measure-PluginPerformance
            $script:MetricsData.Checks += $perfCheck
        }
        
        $issuesCheck = Test-CommonIssues
        $script:MetricsData.Checks += $issuesCheck
        
        # Summary
        Write-Info "`n📋 Summary:"
        $successCount = ($script:MetricsData.Checks | Where-Object { $_.Status -in @("Success", "Excellent", "Good") }).Count
        $totalChecks = $script:MetricsData.Checks.Count
        
        Write-Metric "✅ Successful checks: $successCount/$totalChecks"
        
        # Send alerts for critical issues
        if ($integrityCheck.Status -eq "Failed") {
            Send-Alert "Plugin integrity check failed!" "Critical"
        }
        if ($perfCheck.Status -eq "Poor") {
            Send-Alert "Poor plugin performance detected" "Warning"
        }
        if ($issuesCheck.Issues.Count -gt 0) {
            Send-Alert "Common issues detected: $($issuesCheck.Issues -join ', ')" "Warning"
        }
        
        # Save metrics
        Save-Metrics
        
        if ($Continuous) {
            Write-Info "`n⏳ Next check in ${IntervalSeconds} seconds..."
            Start-Sleep -Seconds $IntervalSeconds
        }
        
    } while ($Continuous)
    
    # Final report
    $runtime = (Get-Date) - $script:StartTime
    Write-Success "`n✅ Monitoring completed"
    Write-Info "Total runtime: $([math]::Round($runtime.TotalMinutes, 2)) minutes"
    Write-Info "Total checks performed: $($script:MetricsData.Checks.Count)"
}

# Start the monitoring
Start-Monitoring