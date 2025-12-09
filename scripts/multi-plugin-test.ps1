# Multi-Plugin Co-existence Test Script (PowerShell)
# Tests that all RicherTunes plugins (Brainarr, Qobuzarr, Tidalarr) can load together
# in a single Lidarr instance without assembly conflicts.

param(
    [string]$LidarrVersion = "pr-plugins-2.14.2.4786"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$GitHubRoot = Split-Path -Parent $ProjectRoot
$TestDir = Join-Path $GitHubRoot "multi-plugin-test"
$ContainerName = "multi-plugin-test"

Write-Host "=== Multi-Plugin Co-existence Test ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This test verifies that Brainarr, Qobuzarr, and Tidalarr can all"
Write-Host "load together in the same Lidarr instance without conflicts."
Write-Host ""

# Create test directory structure
Write-Host "Setting up plugin directory structure..."
New-Item -ItemType Directory -Force -Path "$TestDir\RicherTunes\Brainarr" | Out-Null
New-Item -ItemType Directory -Force -Path "$TestDir\RicherTunes\Qobuzarr" | Out-Null
New-Item -ItemType Directory -Force -Path "$TestDir\RicherTunes\Tidalarr" | Out-Null

# Find and copy plugin DLLs
Write-Host "Copying plugin files..."

# Brainarr
$BrainarrDll = Get-ChildItem -Path "$GitHubRoot\brainarr" -Filter "Lidarr.Plugin.Brainarr.dll" -Recurse |
    Where-Object { $_.FullName -like "*\bin\*" } | Select-Object -First 1
if ($BrainarrDll) {
    Copy-Item $BrainarrDll.FullName -Destination "$TestDir\RicherTunes\Brainarr\"
    $BrainarrJson = Join-Path $GitHubRoot "brainarr\plugin.json"
    if (Test-Path $BrainarrJson) { Copy-Item $BrainarrJson -Destination "$TestDir\RicherTunes\Brainarr\" }
    Write-Host "  [OK] Brainarr" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Brainarr DLL not found - please build first" -ForegroundColor Red
}

# Qobuzarr
$QobuzarrDll = Get-ChildItem -Path "$GitHubRoot\qobuzarr" -Filter "Lidarr.Plugin.Qobuzarr.dll" -Recurse |
    Where-Object { $_.FullName -like "*\bin\*" } | Select-Object -First 1
if ($QobuzarrDll) {
    Copy-Item $QobuzarrDll.FullName -Destination "$TestDir\RicherTunes\Qobuzarr\"
    $QobuzarrJson = Join-Path $GitHubRoot "qobuzarr\plugin.json"
    if (Test-Path $QobuzarrJson) { Copy-Item $QobuzarrJson -Destination "$TestDir\RicherTunes\Qobuzarr\" }
    Write-Host "  [OK] Qobuzarr" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Qobuzarr DLL not found - please build first" -ForegroundColor Red
}

# Tidalarr
$TidalarrDll = Get-ChildItem -Path "$GitHubRoot\tidalarr" -Filter "Lidarr.Plugin.Tidalarr.dll" -Recurse |
    Where-Object { $_.FullName -like "*\bin\*" } | Select-Object -First 1
if ($TidalarrDll) {
    Copy-Item $TidalarrDll.FullName -Destination "$TestDir\RicherTunes\Tidalarr\"
    $TidalarrJson = Join-Path $GitHubRoot "tidalarr\plugin.json"
    if (Test-Path $TidalarrJson) { Copy-Item $TidalarrJson -Destination "$TestDir\RicherTunes\Tidalarr\" }
    Write-Host "  [OK] Tidalarr" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Tidalarr DLL not found - please build first" -ForegroundColor Red
}

Write-Host ""
Write-Host "Staged plugins:"
Get-ChildItem -Path $TestDir -Filter "*.dll" -Recurse | Format-Table Name, Length, DirectoryName

# Stop existing container if running
Write-Host ""
Write-Host "Cleaning up existing container..."
docker stop $ContainerName 2>$null
docker rm $ContainerName 2>$null

# Start new container with all plugins
Write-Host "Starting Lidarr with all plugins..."
# Convert Windows path to Docker-compatible path
$DockerPath = $TestDir -replace '\\', '/' -replace '^([A-Za-z]):', { "/$($_.Groups[1].Value.ToLower())" }
docker run -d --name $ContainerName -p 8787:8686 -v "${DockerPath}:/config/plugins:ro" "ghcr.io/hotio/lidarr:$LidarrVersion"

# Wait for Lidarr to start
Write-Host "Waiting for Lidarr to initialize..."
for ($i = 1; $i -le 60; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8787" -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 401) {
            Write-Host "Lidarr is ready!"
            break
        }
    } catch {}
    Start-Sleep -Seconds 2
}

# Get API key
Write-Host ""
Write-Host "Getting API key..."
Start-Sleep -Seconds 5
$ApiKey = docker exec $ContainerName sh -c "sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml" 2>$null
if (-not $ApiKey) {
    Write-Host "ERROR: Could not get API key" -ForegroundColor Red
    docker logs $ContainerName 2>&1 | Select-Object -Last 50
    exit 1
}
Write-Host "API Key: $($ApiKey.Substring(0, 4))..."

# Test plugin detection
Write-Host ""
Write-Host "=== Plugin Detection Results ===" -ForegroundColor Cyan

$Headers = @{ "X-Api-Key" = $ApiKey }

# Check indexer schemas
Write-Host ""
Write-Host "Checking indexer schemas..."
try {
    $Indexers = Invoke-RestMethod -Uri "http://localhost:8787/api/v1/indexer/schema" -Headers $Headers
    if ($Indexers | Where-Object { $_.implementation -eq "QobuzIndexer" }) {
        Write-Host "  [OK] QobuzIndexer detected" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] QobuzIndexer NOT detected" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Failed to query indexer schema: $_" -ForegroundColor Red
}

# Check download client schemas
Write-Host ""
Write-Host "Checking download client schemas..."
try {
    $Clients = Invoke-RestMethod -Uri "http://localhost:8787/api/v1/downloadclient/schema" -Headers $Headers
    if ($Clients | Where-Object { $_.implementation -eq "QobuzDownloadClient" }) {
        Write-Host "  [OK] QobuzDownloadClient detected" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] QobuzDownloadClient NOT detected" -ForegroundColor Red
    }
    if ($Clients | Where-Object { $_.implementation -eq "TidalarrDownloadClient" }) {
        Write-Host "  [OK] TidalarrDownloadClient detected" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] TidalarrDownloadClient NOT detected" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Failed to query download client schema: $_" -ForegroundColor Red
}

# Check import list schemas
Write-Host ""
Write-Host "Checking import list schemas..."
try {
    $Lists = Invoke-RestMethod -Uri "http://localhost:8787/api/v1/importlist/schema" -Headers $Headers
    if ($Lists | Where-Object { $_.implementationName -like "*Brainarr*" }) {
        Write-Host "  [OK] Brainarr import list detected" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Brainarr import list NOT detected" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Failed to query import list schema: $_" -ForegroundColor Red
}

# Check delay profiles
Write-Host ""
Write-Host "Checking delay profiles..."
try {
    $Profiles = Invoke-RestMethod -Uri "http://localhost:8787/api/v1/delayprofile" -Headers $Headers
    $Items = $Profiles | ForEach-Object { $_.items } | Where-Object { $_ }
    if ($Items | Where-Object { $_.protocol -eq "QobuzarrDownloadProtocol" }) {
        Write-Host "  [OK] QobuzarrDownloadProtocol registered" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] QobuzarrDownloadProtocol NOT registered" -ForegroundColor Red
    }
    if ($Items | Where-Object { $_.protocol -eq "TidalarrProtocol" }) {
        Write-Host "  [OK] TidalarrProtocol registered" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] TidalarrProtocol NOT registered" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Failed to query delay profiles: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container '$ContainerName' is still running."
Write-Host "Access Lidarr UI at: http://localhost:8787"
Write-Host ""
Write-Host "To clean up: docker stop $ContainerName; docker rm $ContainerName"
