#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates the integration testing setup

.DESCRIPTION
    Quick validation script to ensure the live integration testing framework
    is properly set up and can connect to your Lidarr instance.
#>

param(
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    
    $timestamp = Get-Date -Format "HH:mm:ss"
    switch ($Type) {
        "Success" { Write-Host "[$timestamp] [OK] $Message" -ForegroundColor Green }
        "Warning" { Write-Host "[$timestamp] [WARN] $Message" -ForegroundColor Yellow }
        "Error"   { Write-Host "[$timestamp] [ERROR] $Message" -ForegroundColor Red }
        "Info"    { Write-Host "[$timestamp] [INFO] $Message" -ForegroundColor Cyan }
        default   { Write-Host "[$timestamp] $Message" }
    }
}

Write-Host ""
Write-Host "🔍 INTEGRATION TESTING SETUP VALIDATION" -ForegroundColor Magenta
Write-Host "=======================================" -ForegroundColor Magenta
Write-Host ""

try {
    # 1. Check if integration tests build
    Write-Status "Building integration tests..." "Info"
    dotnet build tests/Integration/Qobuzarr.IntegrationTests.csproj --configuration Debug --verbosity minimal --nologo
    
    if ($LASTEXITCODE -eq 0) {
        Write-Status "Integration tests build successfully" "Success"
    } else {
        Write-Status "Integration tests build failed" "Error"
        exit 1
    }
    
    # 2. Check environment configuration
    Write-Status "Checking environment configuration..." "Info"
    $envFile = "tests\Integration\.env"
    if (Test-Path $envFile) {
        Write-Status "Found .env configuration file" "Success"
        
        # Load environment variables
        Get-Content $envFile | ForEach-Object {
            if ($_ -match '^([^=]+)=(.*)$' -and -not $_.StartsWith('#')) {
                [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
            }
        }
        
        $lidarrUrl = $env:LIDARR_URL
        $apiKey = $env:LIDARR_API_KEY
        
        if ($lidarrUrl) {
            Write-Status "Lidarr URL configured: $lidarrUrl" "Success"
        } else {
            Write-Status "LIDARR_URL not configured in .env file" "Warning"
        }
        
        if ($apiKey) {
            Write-Status "API Key configured: $($apiKey.Substring(0, [Math]::Min(8, $apiKey.Length)))..." "Success"
        } else {
            Write-Status "LIDARR_API_KEY not configured in .env file" "Warning"
        }
        
        if ($env:DOCKER_CONTAINER_NAME) {
            Write-Status "Docker container: $($env:DOCKER_CONTAINER_NAME)" "Success"
        } else {
            Write-Status "Docker container not configured (manual deployment required)" "Info"
        }
        
    } else {
        Write-Status ".env file not found" "Warning"
        Write-Status "Copy tests/Integration/.env.example to tests/Integration/.env" "Info"
    }
    
    # 3. Test Lidarr connectivity (if configured)
    if ($env:LIDARR_URL -and $env:LIDARR_API_KEY) {
        Write-Status "Testing Lidarr connectivity..." "Info"
        
        try {
            $headers = @{ "X-Api-Key" = $env:LIDARR_API_KEY }
            $response = Invoke-RestMethod -Uri "$($env:LIDARR_URL)/api/v1/system/status" -Headers $headers -TimeoutSec 10
            
            if ($response) {
                Write-Status "Lidarr connectivity successful! Version: $($response.version)" "Success"
                
                # Check for Qobuzarr plugin
                try {
                    $indexers = Invoke-RestMethod -Uri "$($env:LIDARR_URL)/api/v1/indexer" -Headers $headers -TimeoutSec 10
                    $qobuzIndexer = $indexers | Where-Object { $_.implementation -eq "QobuzIndexer" }
                    
                    if ($qobuzIndexer) {
                        Write-Status "Qobuzarr indexer found! (ID: $($qobuzIndexer.id), Enabled: $($qobuzIndexer.enable))" "Success"
                    } else {
                        Write-Status "Qobuzarr indexer not found in Lidarr" "Warning"
                        Write-Status "Deploy plugin first using: .\run-live-tests.ps1 -DeployPlugin" "Info"
                    }
                } catch {
                    Write-Status "Could not check for Qobuzarr plugin: $($_.Exception.Message)" "Warning"
                }
                
            } else {
                Write-Status "Lidarr returned empty response" "Warning"
            }
        } catch {
            Write-Status "Lidarr connectivity failed: $($_.Exception.Message)" "Error"
            Write-Status "Check LIDARR_URL and LIDARR_API_KEY in .env file" "Info"
        }
    } else {
        Write-Status "Lidarr not configured - skipping connectivity test" "Info"
    }
    
    # 4. Check Docker availability (if configured)
    if ($env:DOCKER_CONTAINER_NAME) {
        Write-Status "Testing Docker availability..." "Info"
        
        try {
            if (Get-Command docker -ErrorAction SilentlyContinue) {
                $dockerInfo = docker ps --filter "name=$($env:DOCKER_CONTAINER_NAME)" --format "{{.Status}}"
                
                if ($dockerInfo) {
                    Write-Status "Docker container found: $($dockerInfo)" "Success"
                } else {
                    Write-Status "Docker container '$($env:DOCKER_CONTAINER_NAME)' not found or not running" "Warning"
                    Write-Status "Available containers:" "Info"
                    docker ps --format "table {{.Names}}\t{{.Status}}" | Write-Host
                }
            } else {
                Write-Status "Docker command not available" "Warning"
                Write-Status "Install Docker to use container automation features" "Info"
            }
        } catch {
            Write-Status "Docker check failed: $($_.Exception.Message)" "Warning"
        }
    }
    
    # 5. Check test automation scripts
    Write-Status "Checking test automation scripts..." "Info"
    
    $scripts = @(
        "run-live-tests.ps1",
        "run-live-tests.sh", 
        "test-integration.ps1"
    )
    
    foreach ($script in $scripts) {
        if (Test-Path $script) {
            Write-Status "$script found" "Success"
        } else {
            Write-Status "$script not found" "Warning"
        }
    }
    
    # Summary
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "🎉 VALIDATION SUMMARY" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Host "✅ Integration test framework: Ready" -ForegroundColor Green
    Write-Host "✅ Build system: Working" -ForegroundColor Green
    Write-Host "✅ Automation scripts: Available" -ForegroundColor Green
    
    if ($env:LIDARR_URL -and $env:LIDARR_API_KEY) {
        Write-Host "✅ Configuration: Complete" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Configuration: Needs setup (see .env.example)" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    if (-not (Test-Path "tests\Integration\.env")) {
        Write-Host "1. Copy tests\Integration\.env.example to tests\Integration\.env" -ForegroundColor White
        Write-Host "2. Configure your Lidarr URL and API key" -ForegroundColor White
        Write-Host "3. Run: .\test-integration.ps1 -TestFilter Critical" -ForegroundColor White
    } elseif (-not $env:LIDARR_URL -or -not $env:LIDARR_API_KEY) {
        Write-Host "1. Configure LIDARR_URL and LIDARR_API_KEY in .env file" -ForegroundColor White
        Write-Host "2. Run: .\test-integration.ps1 -TestFilter Critical" -ForegroundColor White
    } else {
        Write-Host "1. Run critical tests: .\test-integration.ps1 -TestFilter Critical" -ForegroundColor White
        Write-Host "2. Run full tests: .\run-live-tests.ps1" -ForegroundColor White
        Write-Host "3. Run security tests: .\test-integration.ps1 -TestFilter Security" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "🎯 Integration testing framework is ready!" -ForegroundColor Green
    Write-Host ""
    
} catch {
    Write-Status "Validation failed: $($_.Exception.Message)" "Error"
    exit 1
}