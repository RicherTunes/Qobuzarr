#!/usr/bin/env pwsh
<#
.SYNOPSIS
Deploys Lidarr.Plugin.Common shared library for streaming plugin ecosystem.

.DESCRIPTION
This script handles deployment of the shared library following the chief architect's 
recommendations for proper package structure and separation.

.PARAMETER Target
Target deployment mode: Local, NuGet, or Both
Default: Local

.PARAMETER LidarrPath
Path to Lidarr plugins directory for local deployment
Default: X:\lidarr-hotio-test2\plugins

.PARAMETER Version
Version to build and deploy
Default: Read from VERSION file

.PARAMETER Clean
Whether to clean before building
Default: false

.EXAMPLE
.\scripts\deploy-shared-library.ps1 -Target Local
.\scripts\deploy-shared-library.ps1 -Target NuGet -Version 1.0.0
.\scripts\deploy-shared-library.ps1 -Target Both -LidarrPath "C:\Lidarr\Plugins"
#>

param(
    [ValidateSet("Local", "NuGet", "Both")]
    [string]$Target = "Local",
    
    [string]$LidarrPath = "X:\lidarr-hotio-test2\plugins",
    
    [string]$Version,
    
    [switch]$Clean
)

# Error handling
$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Message)
    Write-Host "🚀 $Message" -ForegroundColor Cyan
    Write-Host ("=" * ($Message.Length + 3)) -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️ $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

# Get version
if (-not $Version) {
    if (Test-Path "Lidarr.Plugin.Common\VERSION") {
        $Version = Get-Content "Lidarr.Plugin.Common\VERSION" -First 1
    } else {
        $Version = "1.0.0-dev"
        Write-Warning "No VERSION file found, using default: $Version"
    }
}

Write-Header "Deploying Lidarr.Plugin.Common v$Version"

try {
    # Clean if requested
    if ($Clean) {
        Write-Host "🧹 Cleaning previous builds..."
        dotnet clean "Lidarr.Plugin.Common\Lidarr.Plugin.Common.csproj" --configuration Release
        Write-Success "Clean completed"
    }

    # Build shared library
    Write-Host "🔨 Building shared library..."
    $buildResult = dotnet build "Lidarr.Plugin.Common\Lidarr.Plugin.Common.csproj" --configuration Release --verbosity minimal
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Success "Build completed successfully"

    # Local deployment
    if ($Target -eq "Local" -or $Target -eq "Both") {
        Write-Host "📦 Deploying locally..."
        
        $commonPath = Join-Path $LidarrPath "Common"
        $sharedLibPath = "Lidarr.Plugin.Common\bin\Release\net6.0"

        # Create Common directory
        if (-not (Test-Path $commonPath)) {
            New-Item -ItemType Directory -Path $commonPath -Force | Out-Null
            Write-Success "Created Common directory: $commonPath"
        }

        # Copy shared library files
        $filesToCopy = @(
            "Lidarr.Plugin.Common.dll",
            "Lidarr.Plugin.Common.pdb",
            "Lidarr.Plugin.Common.deps.json"
        )

        foreach ($file in $filesToCopy) {
            $sourcePath = Join-Path $sharedLibPath $file
            $destPath = Join-Path $commonPath $file
            
            if (Test-Path $sourcePath) {
                Copy-Item $sourcePath $destPath -Force
                Write-Host "  ✓ Copied $file"
            } else {
                Write-Warning "  ⚠ File not found: $file"
            }
        }

        # Create version info
        $versionInfo = @{
            version = $Version
            deployedAt = (Get-Date).ToString("o")
            deployedBy = $env:USERNAME
            buildConfiguration = "Release"
        }

        $versionJson = $versionInfo | ConvertTo-Json -Depth 2
        $versionPath = Join-Path $commonPath "version.json"
        Set-Content -Path $versionPath -Value $versionJson -Encoding UTF8
        
        Write-Success "Local deployment completed to: $commonPath"
    }

    # NuGet packaging
    if ($Target -eq "NuGet" -or $Target -eq "Both") {
        Write-Host "📦 Creating NuGet package..."
        
        # Update version in project file
        $csprojPath = "Lidarr.Plugin.Common\Lidarr.Plugin.Common.csproj"
        $csprojContent = Get-Content $csprojPath -Raw
        
        $csprojContent = $csprojContent -replace '<PackageVersion>.*</PackageVersion>', "<PackageVersion>$Version</PackageVersion>"
        $csprojContent = $csprojContent -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
        
        Set-Content $csprojPath $csprojContent -NoNewline

        # Create NuGet package
        $packResult = dotnet pack "Lidarr.Plugin.Common\Lidarr.Plugin.Common.csproj" --configuration Release --output "packages" --verbosity minimal
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "NuGet packaging failed"
            exit 1
        }

        $packagePath = "packages\Lidarr.Plugin.Common.$Version.nupkg"
        if (Test-Path $packagePath) {
            Write-Success "NuGet package created: $packagePath"
        } else {
            Write-Warning "NuGet package not found at expected path"
        }
    }

    # Display summary
    Write-Header "Deployment Summary"
    Write-Host "📋 Shared Library: Lidarr.Plugin.Common v$Version"
    Write-Host "📋 Target: $Target"
    Write-Host "📋 Build Configuration: Release"
    
    if ($Target -eq "Local" -or $Target -eq "Both") {
        Write-Host "📋 Local Path: $commonPath"
    }
    
    if ($Target -eq "NuGet" -or $Target -eq "Both") {
        Write-Host "📋 NuGet Package: packages\Lidarr.Plugin.Common.$Version.nupkg"
    }

    Write-Success "Deployment completed successfully!"

    # Usage instructions
    Write-Host ""
    Write-Host "📚 Usage Instructions:"
    Write-Host ""
    
    if ($Target -eq "Local" -or $Target -eq "Both") {
        Write-Host "For local development, plugins can reference:"
        Write-Host "  <ProjectReference Include=`"$commonPath\Lidarr.Plugin.Common.csproj`" />"
    }
    
    if ($Target -eq "NuGet" -or $Target -eq "Both") {
        Write-Host "For NuGet usage, plugins can reference:"
        Write-Host "  <PackageReference Include=`"Lidarr.Plugin.Common`" Version=`"$Version`" />"
    }

    Write-Host ""
    Write-Host "🎵 Ready for streaming plugin development!"

} catch {
    Write-Error "Deployment failed: $($_.Exception.Message)"
    exit 1
}