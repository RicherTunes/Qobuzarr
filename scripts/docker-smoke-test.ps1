<#
.SYNOPSIS
    [DEPRECATED] Smoke test for Qobuzarr plugin - verifies plugin loads correctly in Lidarr.

    DEPRECATION NOTICE:
    This script is superseded by Common's multi-plugin-smoke-test workflow.
    For CI/CD, use: .github/workflows/multi-plugin-smoke-test.yml
    For local testing, use: pwsh ext/Lidarr.Plugin.Common/scripts/multi-plugin-docker-smoke-test.ps1

    This script remains available for quick single-plugin local testing only.
    Do NOT add this script to CI workflows - use Common's reusable workflow instead.

.DESCRIPTION
    This script:
    1. Builds the plugin in Release mode
    2. Deploys it to the Docker container
    3. Starts/restarts the container
    4. Waits for Lidarr to become healthy
    5. Checks that the Qobuz indexer appears in the API schema

    Use this to catch plugin loading regressions while GitHub Actions CI is unavailable.

.PARAMETER SkipBuild
    Skip the build step (use existing binaries).

.PARAMETER LidarrTag
    Docker image tag for Lidarr. Default: pr-plugins-3.1.2.4913

.PARAMETER ContainerName
    Name for the Docker container. Default: qobuzarr-smoke-test
    Use different names to run multiple instances concurrently.

.PARAMETER Port
    Host port for Lidarr. Default: 8687
    Use different ports to run multiple instances concurrently.

.PARAMETER TimeoutSeconds
    Maximum time to wait for Lidarr to start. Default: 120

.PARAMETER KeepRunning
    Don't stop the container after test completes.

.PARAMETER SchemaTimeoutSeconds
    Maximum time to wait for plugin schema to appear after Lidarr is healthy.
    Default: 60 (12 retries × 5s). Cold starts may need more time.

.EXAMPLE
    .\scripts\docker-smoke-test.ps1

.EXAMPLE
    .\scripts\docker-smoke-test.ps1 -SkipBuild -KeepRunning

.EXAMPLE
    # Run concurrent tests with different containers
    .\scripts\docker-smoke-test.ps1 -ContainerName "smoke-test-1" -Port 8687
    .\scripts\docker-smoke-test.ps1 -ContainerName "smoke-test-2" -Port 8688

.NOTES
    Requires: Docker, .NET SDK 8.0+
#>

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    # Ecosystem baseline: keep aligned with other plugins unless explicitly overridden
    [string]$LidarrTag = "pr-plugins-3.1.2.4913",
    [string]$ContainerName = "qobuzarr-smoke-test",
    [int]$Port = 8687,
    [int]$TimeoutSeconds = 120,
    [int]$SchemaTimeoutSeconds = 60,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

# Deprecation warning
Write-Warning @"
This script is DEPRECATED for CI/CD use.
For comprehensive multi-plugin testing, use: .github/workflows/multi-plugin-smoke-test.yml
For local testing with Common: pwsh ext/Lidarr.Plugin.Common/scripts/multi-plugin-docker-smoke-test.ps1
"@

$script:ContainerStarted = $false
$script:ExitCode = 0

# Determine project root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
if (-not $ProjectRoot -or -not (Test-Path (Join-Path $ProjectRoot "Qobuzarr.csproj"))) {
    $ProjectRoot = (Get-Location).Path
}

function Cleanup {
    if ($script:ContainerStarted -and -not $KeepRunning) {
        Write-Host "`nCleaning up container..." -ForegroundColor Yellow
        docker stop $ContainerName 2>&1 | Out-Null
        docker rm $ContainerName 2>&1 | Out-Null
    }
}

# Register cleanup for all exit paths
trap {
    Cleanup
    exit $script:ExitCode
}

try {
    Write-Host "=== Qobuzarr Docker Smoke Test ===" -ForegroundColor Cyan
    Write-Host "Project root: $ProjectRoot"
    Write-Host "Container: $ContainerName"
    Write-Host "Lidarr tag: $LidarrTag"
    Write-Host "Port: $Port"

    # Step 1: Build plugin
    if (-not $SkipBuild) {
        Write-Host "`n[1/5] Building plugin..." -ForegroundColor Yellow
        Push-Location $ProjectRoot
        try {
            $buildOutput = dotnet build Qobuzarr.csproj -c Release --verbosity quiet 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Build failed:`n$($buildOutput -join "`n")"
                $script:ExitCode = 1
                return
            }
            Write-Host "  Build succeeded" -ForegroundColor Green
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "`n[1/5] Skipping build (--SkipBuild)" -ForegroundColor Yellow
    }

    # Verify required files exist
    $binDir = Join-Path $ProjectRoot "bin"
    $requiredFiles = @(
        "Lidarr.Plugin.Qobuzarr.dll",
        "plugin.json",
        "Lidarr.Plugin.Abstractions.dll"
    )
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path (Join-Path $binDir $file))) {
            Write-Error "Required file not found: $binDir\$file"
            $script:ExitCode = 1
            return
        }
    }

    # Step 2: Prepare plugin directory
    Write-Host "`n[2/5] Preparing plugin deployment..." -ForegroundColor Yellow
    $tempPluginDir = Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName/plugins/RicherTunes/Qobuzarr"
    $tempConfigDir = Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName/config"

    # Clean and create directories
    if (Test-Path (Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName")) {
        Remove-Item -Path (Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName") -Recurse -Force
    }
    New-Item -ItemType Directory -Path $tempPluginDir -Force | Out-Null
    New-Item -ItemType Directory -Path $tempConfigDir -Force | Out-Null

    # Copy plugin files
    foreach ($file in $requiredFiles) {
        Copy-Item -Path (Join-Path $binDir $file) -Destination $tempPluginDir
        Write-Host "  Copied: $file" -ForegroundColor Gray
    }
    Write-Host "  Plugin staged to: $tempPluginDir" -ForegroundColor Green

    # Step 3: Start container
    Write-Host "`n[3/5] Starting container..." -ForegroundColor Yellow
    
    # Stop existing container if running
    $existing = docker ps -aq --filter "name=$ContainerName" 2>&1
    if ($existing) {
        Write-Host "  Removing existing container..." -ForegroundColor Gray
        docker stop $ContainerName 2>&1 | Out-Null
        docker rm $ContainerName 2>&1 | Out-Null
    }

    $pluginMount = (Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName/plugins").Replace('\', '/')
    $configMount = (Join-Path $ProjectRoot ".docker-smoke-test/$ContainerName/config").Replace('\', '/')

    $dockerArgs = @(
        "run", "-d",
        "--name", $ContainerName,
        "-p", "${Port}:8686",
        "-v", "${configMount}:/config",
        "-v", "${pluginMount}:/config/plugins",
        "-e", "PUID=1000",
        "-e", "PGID=1000",
        "-e", "TZ=UTC",
        "ghcr.io/hotio/lidarr:$LidarrTag"
    )

    $startResult = & docker @dockerArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to start container:`n$startResult"
        $script:ExitCode = 1
        return
    }
    $script:ContainerStarted = $true
    Write-Host "  Container started" -ForegroundColor Green

    # Step 3b: Wait for config.xml and extract API key
    Write-Host "  Waiting for config.xml..." -ForegroundColor Gray
    $configReady = $false
    $apiKey = $null
    for ($i = 1; $i -le 30; $i++) {
        $configExists = docker exec $ContainerName sh -c "test -f /config/config.xml" 2>&1
        if ($LASTEXITCODE -eq 0) {
            $configReady = $true
            # Extract API key from config.xml (same technique as screenshots.yml)
            $apiKey = docker exec $ContainerName sh -c "sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml" 2>&1
            if ($apiKey) {
                Write-Host "  API key extracted: $($apiKey.Substring(0,4))..." -ForegroundColor Gray
            }
            break
        }
        Start-Sleep -Seconds 2
    }

    if (-not $configReady) {
        Write-Host "  Warning: config.xml not found, API calls may fail" -ForegroundColor Yellow
    }

    # Step 4: Wait for Lidarr health
    Write-Host "`n[4/5] Waiting for Lidarr to start (max ${TimeoutSeconds}s)..." -ForegroundColor Yellow
    $lidarrUrl = "http://localhost:$Port"
    $startTime = Get-Date
    $healthy = $false
    $headers = @{}
    if ($apiKey) {
        $headers["X-Api-Key"] = $apiKey
    }

    while ((Get-Date) - $startTime -lt [TimeSpan]::FromSeconds($TimeoutSeconds)) {
        try {
            $response = Invoke-WebRequest -Uri "$lidarrUrl/api/v1/system/status" -Headers $headers -TimeoutSec 5 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                $status = $response.Content | ConvertFrom-Json
                Write-Host "  Lidarr is online (v$($status.version))" -ForegroundColor Green
                $healthy = $true
                break
            }
        }
        catch {
            $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
            Write-Host "  Waiting... (${elapsed}s)" -ForegroundColor Gray
            Start-Sleep -Seconds 5
        }
    }

    if (-not $healthy) {
        Write-Error "Timeout waiting for Lidarr to start"
        Write-Host "`nContainer logs:" -ForegroundColor Yellow
        docker logs $ContainerName --tail 50 2>&1
        $script:ExitCode = 1
        return
    }

    # Step 5: Verify Qobuz indexer with retry loop
    Write-Host "`n[5/5] Checking for Qobuz indexer (max ${SchemaTimeoutSeconds}s)..." -ForegroundColor Yellow
    
    $schemaDelay = 5
    $schemaRetries = [Math]::Ceiling($SchemaTimeoutSeconds / $schemaDelay)
    $qobuzIndexer = $null
    $qobuzDownloader = $null

    for ($i = 1; $i -le $schemaRetries; $i++) {
        try {
            # Check indexer schema
            $schemaResponse = Invoke-WebRequest -Uri "$lidarrUrl/api/v1/indexer/schema" -Headers $headers -TimeoutSec 10 -ErrorAction Stop
            $schemas = $schemaResponse.Content | ConvertFrom-Json
            $qobuzIndexer = $schemas | Where-Object { $_.implementation -eq "QobuzIndexer" }

            # Check download client schema
            $downloadSchemaResponse = Invoke-WebRequest -Uri "$lidarrUrl/api/v1/downloadclient/schema" -Headers $headers -TimeoutSec 10 -ErrorAction Stop
            $downloadSchemas = $downloadSchemaResponse.Content | ConvertFrom-Json
            $qobuzDownloader = $downloadSchemas | Where-Object { $_.implementation -eq "QobuzDownloadClient" }

            if ($qobuzIndexer -and $qobuzDownloader) {
                break
            }
            
            Write-Host "  Waiting for plugin to register (attempt $i/$schemaRetries)..." -ForegroundColor Gray
            Start-Sleep -Seconds $schemaDelay
        }
        catch {
            Write-Host "  Schema query failed (attempt $i/$schemaRetries): $_" -ForegroundColor Gray
            Start-Sleep -Seconds $schemaDelay
        }
    }

    # Report results
    $success = $true

    if ($qobuzIndexer) {
        Write-Host "  QobuzIndexer found in schema" -ForegroundColor Green
        Write-Host "    Implementation: $($qobuzIndexer.implementation)" -ForegroundColor Gray
        Write-Host "    Name: $($qobuzIndexer.implementationName)" -ForegroundColor Gray
    }
    else {
        Write-Host "  QobuzIndexer NOT found in Lidarr schema!" -ForegroundColor Red
        $success = $false
    }

    if ($qobuzDownloader) {
        Write-Host "  QobuzDownloadClient found in schema" -ForegroundColor Green
    }
    else {
        Write-Host "  QobuzDownloadClient NOT found in schema!" -ForegroundColor Red
        $success = $false
    }

    if (-not $success) {
        Write-Host "`nAvailable indexers:" -ForegroundColor Yellow
        $schemas | ForEach-Object { Write-Host "  - $($_.implementation)" }
        Write-Host "`nContainer logs (plugin-related):" -ForegroundColor Yellow
        docker logs $ContainerName 2>&1 | Select-String -Pattern "plugin|qobuz|error|exception" -CaseSensitive:$false | Select-Object -Last 30
        $script:ExitCode = 1
        return
    }

    Write-Host "`n=== SMOKE TEST PASSED ===" -ForegroundColor Green
    if ($KeepRunning) {
        Write-Host "Container running at: $lidarrUrl" -ForegroundColor Cyan
        Write-Host "Stop with: docker stop $ContainerName && docker rm $ContainerName" -ForegroundColor Gray
    }
}
finally {
    Cleanup
}

exit $script:ExitCode
