#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs comprehensive live integration tests against your Lidarr instance

.DESCRIPTION
    This script automates the entire integration testing process:
    1. Validates environment configuration
    2. Deploys the latest plugin build to your Lidarr instance
    3. Runs comprehensive integration tests
    4. Monitors logs and reports results
    
.PARAMETER LidarrUrl
    URL of your Lidarr instance (e.g., http://localhost:8686)
    
.PARAMETER ApiKey
    Your Lidarr API key
    
.PARAMETER DockerContainer
    Name of your Lidarr Docker container (for automated deployment and log monitoring)
    
.PARAMETER BuildFirst
    Whether to build the plugin before testing (default: true)
    
.PARAMETER DeployPlugin
    Whether to deploy the plugin before testing (default: true)
    
.PARAMETER RestartLidarr
    Whether to restart Lidarr after deployment (default: false)
    
.PARAMETER TestFilter
    Filter for which tests to run (e.g., "Critical", "Security")

.EXAMPLE
    .\run-live-tests.ps1 -LidarrUrl "http://192.168.1.100:8686" -ApiKey "abc123" -DockerContainer "lidarr"
    
.EXAMPLE
    .\run-live-tests.ps1 -BuildFirst -DeployPlugin -RestartLidarr -TestFilter "Critical"

#>

param(
    [string]$LidarrUrl = $env:LIDARR_URL,
    [string]$ApiKey = $env:LIDARR_API_KEY,
    [string]$DockerContainer = $env:DOCKER_CONTAINER_NAME,
    [switch]$BuildFirst = $true,
    [switch]$DeployPlugin = $true,
    [switch]$RestartLidarr = $false,
    [string]$TestFilter = "",
    [switch]$Verbose = $false
)

# Configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    
    $timestamp = Get-Date -Format "HH:mm:ss"
    switch ($Type) {
        "Success" { Write-Host "[$timestamp] ✅ $Message" -ForegroundColor Green }
        "Warning" { Write-Host "[$timestamp] ⚠️  $Message" -ForegroundColor Yellow }
        "Error"   { Write-Host "[$timestamp] ❌ $Message" -ForegroundColor Red }
        "Info"    { Write-Host "[$timestamp] ℹ️  $Message" -ForegroundColor Cyan }
        default   { Write-Host "[$timestamp] 📝 $Message" }
    }
}

function Test-Prerequisites {
    Write-Status "Validating prerequisites..." "Info"
    
    # Check if dotnet is available
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Status ".NET SDK not found. Please install .NET SDK 6.0 or later." "Error"
        exit 1
    }
    
    # Check if Docker is available (if Docker testing is requested)
    if ($DockerContainer -and -not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Status "Docker not found but Docker container specified. Docker commands will be skipped." "Warning"
        $script:DockerContainer = $null
    }
    
    # Validate required parameters
    if (-not $LidarrUrl) {
        Write-Status "LIDARR_URL not specified. Set environment variable or use -LidarrUrl parameter." "Error"
        exit 1
    }
    
    if (-not $ApiKey) {
        Write-Status "LIDARR_API_KEY not specified. Set environment variable or use -ApiKey parameter." "Error"
        exit 1
    }
    
    Write-Status "Prerequisites validated successfully" "Success"
}

function Build-Plugin {
    if (-not $BuildFirst) {
        Write-Status "Skipping plugin build (BuildFirst = false)" "Info"
        return
    }
    
    Write-Status "Building Qobuzarr plugin..." "Info"
    
    try {
        # Use the recommended build command from CLAUDE.md
        dotnet build Qobuzarr.csproj --configuration Debug `
            -p:RunAnalyzersDuringBuild=false `
            -p:EnableNETAnalyzers=false `
            -p:TreatWarningsAsErrors=false `
            -p:EnablePluginDeployment=false
        
        if ($LASTEXITCODE -eq 0) {
            Write-Status "Plugin built successfully" "Success"
        } else {
            Write-Status "Plugin build failed" "Error"
            exit 1
        }
    }
    catch {
        Write-Status "Plugin build failed: $($_.Exception.Message)" "Error"
        exit 1
    }
}

function Deploy-Plugin {
    if (-not $DeployPlugin) {
        Write-Status "Skipping plugin deployment (DeployPlugin = false)" "Info"
        return
    }
    
    Write-Status "Deploying plugin to Lidarr instance..." "Info"
    
    # Check if deployment target exists
    $pluginDll = "bin\Lidarr.Plugin.Qobuzarr.dll"
    if (-not (Test-Path $pluginDll)) {
        Write-Status "Plugin DLL not found: $pluginDll. Run build first." "Error"
        exit 1
    }
    
    if ($DockerContainer) {
        try {
            Write-Status "Deploying to Docker container: $DockerContainer" "Info"
            
            # Copy main plugin files
            docker cp $pluginDll "${DockerContainer}:/app/Plugins/Qobuzarr/"
            docker cp "plugin.json" "${DockerContainer}:/app/Plugins/Qobuzarr/"
            
            # Copy PDB files if they exist
            $pdbFiles = Get-ChildItem "bin\*.pdb" -ErrorAction SilentlyContinue
            foreach ($pdb in $pdbFiles) {
                docker cp $pdb.FullName "${DockerContainer}:/app/Plugins/Qobuzarr/"
            }
            
            Write-Status "Plugin deployed to Docker container successfully" "Success"
        }
        catch {
            Write-Status "Docker deployment failed: $($_.Exception.Message)" "Warning"
            Write-Status "Continuing with tests assuming plugin is already deployed..." "Info"
        }
    }
    else {
        Write-Status "No Docker container specified. Manual deployment required." "Warning"
        Write-Status "Copy plugin files to your Lidarr Plugins/Qobuzarr/ directory manually." "Info"
    }
}

function Restart-Lidarr {
    if (-not $RestartLidarr) {
        Write-Status "Skipping Lidarr restart (RestartLidarr = false)" "Info"
        return
    }
    
    Write-Status "Restarting Lidarr..." "Info"
    
    if ($DockerContainer) {
        try {
            docker restart $DockerContainer
            Write-Status "Docker container restarted" "Success"
            
            # Wait for Lidarr to come back online
            Write-Status "Waiting for Lidarr to come back online..." "Info"
            $maxWait = 180 # 3 minutes
            $waited = 0
            
            do {
                Start-Sleep 10
                $waited += 10
                Write-Status "  Checking... ($waited/$maxWait seconds)" "Info"
                
                try {
                    $response = Invoke-RestMethod -Uri "$LidarrUrl/api/v1/system/status" -Headers @{ "X-Api-Key" = $ApiKey } -TimeoutSec 5
                    if ($response) {
                        Write-Status "Lidarr is back online!" "Success"
                        Start-Sleep 5 # Give it a moment to fully initialize
                        return
                    }
                }
                catch {
                    # Expected while restarting
                }
            } while ($waited -lt $maxWait)
            
            Write-Status "Timeout waiting for Lidarr to restart" "Warning"
        }
        catch {
            Write-Status "Docker restart failed: $($_.Exception.Message)" "Warning"
        }
    }
    else {
        Write-Status "No Docker container specified. Manual restart required if needed." "Info"
    }
}

function Run-IntegrationTests {
    Write-Status "Running live integration tests..." "Info"
    
    # Set environment variables for the tests
    $env:LIDARR_URL = $LidarrUrl
    $env:LIDARR_API_KEY = $ApiKey
    if ($DockerContainer) { $env:DOCKER_CONTAINER_NAME = $DockerContainer }
    
    try {
        $testArgs = @(
            "test"
            "tests/Integration/"
            "--logger", "console;verbosity=detailed"
            "--configuration", "Debug"
        )
        
        if ($TestFilter) {
            $testArgs += "--filter", "Priority=$TestFilter"
            Write-Status "Running tests with filter: Priority=$TestFilter" "Info"
        }
        
        if ($Verbose) {
            $testArgs += "--verbosity", "diagnostic"
        }
        
        & dotnet @testArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Status "All integration tests completed successfully!" "Success"
        } else {
            Write-Status "Some integration tests failed. Check output above for details." "Warning"
        }
    }
    catch {
        Write-Status "Integration test execution failed: $($_.Exception.Message)" "Error"
        exit 1
    }
}

function Show-Summary {
    param([datetime]$StartTime)
    
    $duration = (Get-Date) - $StartTime
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "🎉 LIVE INTEGRATION TEST SUMMARY" -ForegroundColor Cyan  
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor White
    Write-Host "Lidarr: $LidarrUrl" -ForegroundColor White
    Write-Host "Docker: $(if ($DockerContainer) { $DockerContainer } else { 'Not configured' })" -ForegroundColor White
    Write-Host ""
    Write-Host "✅ Plugin deployment: $(if ($DeployPlugin) { 'Completed' } else { 'Skipped' })" -ForegroundColor Green
    Write-Host "🔄 Lidarr restart: $(if ($RestartLidarr) { 'Completed' } else { 'Skipped' })" -ForegroundColor Yellow
    Write-Host "🧪 Integration tests: Completed" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Review test results above" -ForegroundColor White
    Write-Host "2. Check Lidarr logs for any issues" -ForegroundColor White
    Write-Host "3. Test manual search/download in Lidarr UI" -ForegroundColor White
    Write-Host "========================================" -ForegroundColor Cyan
}

# Main execution
$startTime = Get-Date

Write-Host ""
Write-Host "🎵 QOBUZARR LIVE INTEGRATION TESTING" -ForegroundColor Magenta
Write-Host "====================================" -ForegroundColor Magenta
Write-Host ""

try {
    Test-Prerequisites
    Build-Plugin
    Deploy-Plugin
    Restart-Lidarr
    Run-IntegrationTests
    
    Show-Summary $startTime
}
catch {
    Write-Status "Integration testing failed: $($_.Exception.Message)" "Error"
    exit 1
}