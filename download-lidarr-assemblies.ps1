# =============================================================================
# Download Pre-built Lidarr Assemblies Script (PowerShell)
# =============================================================================
# Alternative to building Lidarr from source - downloads release assemblies

param(
    [string]$LidarrVersion = "2.13.2.4685",
    [string]$OutputPath = "ext\Lidarr\_output\net6.0",
    [switch]$Force
)

Write-Host "Downloading Pre-built Lidarr Assemblies" -ForegroundColor Green
Write-Host "Version: $LidarrVersion" -ForegroundColor Cyan
Write-Host "Output: $OutputPath" -ForegroundColor Cyan

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    Write-Host "Created directory: $OutputPath" -ForegroundColor Blue
}

# Check if assemblies already exist
$existingFiles = @(
    "$OutputPath\Lidarr.Core.dll",
    "$OutputPath\Lidarr.Common.dll"
)

$allExist = $true
foreach ($file in $existingFiles) {
    if (-not (Test-Path $file)) {
        $allExist = $false
        break
    }
}

if ($allExist -and -not $Force) {
    Write-Host "Lidarr assemblies already exist. Use -Force to re-download." -ForegroundColor Yellow
    exit 0
}

try {
    # Download Lidarr release
    $downloadUrl = "https://github.com/Lidarr/Lidarr/releases/download/v$LidarrVersion/Lidarr.develop.$LidarrVersion.linux-core-x64.tar.gz"
    $archivePath = "lidarr-release.tar.gz"
    
    Write-Host "Downloading from: $downloadUrl" -ForegroundColor Blue
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    
    # Extract using tar (requires Windows 10 build 17063+ or WSL)
    Write-Host "Extracting Lidarr assemblies..." -ForegroundColor Blue
    tar -xzf $archivePath
    
    # Copy required assemblies
    $sourceDir = "Lidarr"
    $requiredAssemblies = @(
        # Lidarr host assemblies
        "Lidarr.Core.dll",
        "Lidarr.Common.dll",
        "Lidarr.Http.dll",
        "Lidarr.Api.V1.dll",
        "Lidarr.dll",
        "Lidarr.Host.dll",
        "Lidarr.SignalR.dll",
        # Legacy/host assemblies some tests reference via NzbDrone namespaces
        "NzbDrone.Core.dll",
        "NzbDrone.Common.dll",
        "NzbDrone.Host.dll",
        "NzbDrone.Api.dll"
    )
    
    foreach ($assembly in $requiredAssemblies) {
        $sourcePath = "$sourceDir\$assembly"
        $destPath = "$OutputPath\$assembly"
        
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath $destPath -Force
            Write-Host "Copied: $assembly" -ForegroundColor Green
        } else {
            Write-Host "Optional assembly not found: $assembly" -ForegroundColor Yellow
        }
    }
    
    # Cleanup
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    Remove-Item $sourceDir -Recurse -Force -ErrorAction SilentlyContinue
    
    Write-Host ""
    Write-Host "Lidarr assemblies downloaded successfully!" -ForegroundColor Green
    Write-Host "Location: $OutputPath" -ForegroundColor Gray
    
    # Show what was downloaded
    Write-Host ""
    Write-Host "Downloaded assemblies:" -ForegroundColor Cyan
    Get-ChildItem $OutputPath -Filter "*.dll" | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        $sizeStr = "{0:N2}" -f $size
        Write-Host "  - $($_.Name) ($sizeStr MB)" -ForegroundColor White
    }
    
} catch {
    Write-Host "Failed to download Lidarr assemblies: $_" -ForegroundColor Red
    Write-Host "Try manually downloading from: https://github.com/Lidarr/Lidarr/releases" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  - Update project references to use these assemblies" -ForegroundColor White
Write-Host "  - Run: .\build.ps1 -Deploy" -ForegroundColor White
