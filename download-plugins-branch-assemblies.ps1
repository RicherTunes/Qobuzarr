# PowerShell script to extract Lidarr assemblies from plugins branch Docker container
param(
    [string]$Version = "2.13.3.4692",
    [switch]$Force,
    [string]$OutputPath = "ext/Lidarr-plugins-branch/_output"
)

$ErrorActionPreference = "Stop"

Write-Host "Extracting Lidarr assemblies from plugins branch Docker container..." -ForegroundColor Green
Write-Host "Version: pr-plugins-$Version"

# Create output directory
$fullOutputPath = Join-Path $PWD $OutputPath
if ($Force -and (Test-Path $fullOutputPath)) {
    Write-Host "Cleaning existing assemblies directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $fullOutputPath
}

if (-not (Test-Path $fullOutputPath)) {
    New-Item -ItemType Directory -Path $fullOutputPath -Force | Out-Null
}

$dockerImage = "ghcr.io/hotio/lidarr:pr-plugins-$Version"
$containerName = "lidarr-plugins-extract-temp"

try {
    # Check if Docker is available
    Write-Host "Checking Docker availability..." -ForegroundColor Cyan
    docker --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not available. Please install Docker first."
    }

    # Pull the plugins branch Docker image
    Write-Host "Pulling plugins branch Docker image..." -ForegroundColor Cyan
    docker pull $dockerImage
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to pull Docker image: $dockerImage"
    }

    # Create temporary container
    Write-Host "Creating temporary container..." -ForegroundColor Cyan
    docker create --name $containerName $dockerImage | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create container: $containerName"
    }

    # Extract assemblies
    Write-Host "Extracting assemblies from container..." -ForegroundColor Cyan
    $extractPath = Join-Path $fullOutputPath "net8.0"
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
    
    # Copy all DLL files from the app/bin directory
    docker cp "${containerName}:/app/bin/." $extractPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract assemblies from container"
    }

    Write-Host "Successfully extracted assemblies to: $extractPath" -ForegroundColor Green
    
    # List extracted files
    $dllFiles = Get-ChildItem -Path $extractPath -Filter "*.dll" | Sort-Object Name
    Write-Host "Extracted $($dllFiles.Count) assembly files:" -ForegroundColor Cyan
    foreach ($dll in $dllFiles | Select-Object -First 10) {
        Write-Host "   $($dll.Name)" -ForegroundColor Gray
    }
    if ($dllFiles.Count -gt 10) {
        Write-Host "   ... and $($dllFiles.Count - 10) more" -ForegroundColor Gray
    }

    # Verify key assemblies
    $keyAssemblies = @("Lidarr.Core.dll", "NzbDrone.Core.dll", "Lidarr.Http.dll", "NzbDrone.Common.dll")
    $missingAssemblies = @()
    foreach ($assembly in $keyAssemblies) {
        if (-not (Test-Path (Join-Path $extractPath $assembly))) {
            $missingAssemblies += $assembly
        }
    }

    if ($missingAssemblies.Count -gt 0) {
        Write-Warning "Some key assemblies are missing: $($missingAssemblies -join ', ')"
        Write-Host "   This might be expected if the plugins branch has different assembly names." -ForegroundColor Yellow
    } else {
        Write-Host "All key assemblies found!" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Green
    Write-Host "1. Update Qobuzarr.csproj to reference these assemblies"
    Write-Host "2. Build the plugin: dotnet build Qobuzarr.csproj --configuration Release"
    Write-Host "3. Test with plugins branch Lidarr runtime"

} catch {
    Write-Error "Failed to extract assemblies: $_"
    exit 1
} finally {
    # Cleanup container
    if (docker ps -a --format "table {{.Names}}" | Select-String $containerName) {
        Write-Host "Cleaning up temporary container..." -ForegroundColor Cyan
        docker rm $containerName | Out-Null
    }
}

Write-Host "Plugins branch assemblies ready for development!" -ForegroundColor Green