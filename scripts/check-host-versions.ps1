<#
.SYNOPSIS
    Checks host assembly versions and compares with Directory.Packages.props.

.DESCRIPTION
    This script:
    1. Reads assembly versions from ext/Lidarr/_output/net8.0/
    2. Reads pinned versions from Directory.Packages.props
    3. Reports any mismatches that would cause runtime failures

    Use after updating Lidarr host assemblies to verify package pins are correct.

.PARAMETER ExtractFrom
    Docker image tag to extract assemblies from (optional).
    If specified, extracts fresh assemblies before checking.

.PARAMETER Strict
    Exit with non-zero code on any mismatch. Use in CI pipelines.
    Default: off (reports mismatches but exits 0 for dev convenience).

.EXAMPLE
    .\scripts\check-host-versions.ps1

.EXAMPLE
    # Extract from new Lidarr version and check
    .\scripts\check-host-versions.ps1 -ExtractFrom "pr-plugins-3.2.0.5000"

.EXAMPLE
    # CI mode - fail on mismatch
    .\scripts\check-host-versions.ps1 -Strict

.NOTES
    Host-coupled packages (types cross plugin boundary):
    - FluentValidation: ValidationFailure returned by Test()
    - NLog: Logger injected by DI container
#>

[CmdletBinding()]
param(
    [string]$ExtractFrom,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# Host-coupled packages that must match exactly
$HostCoupledPackages = @{
    "FluentValidation" = @{
        Reason = "ValidationFailure type crosses plugin boundary in DownloadClientBase.Test()"
        DllName = "FluentValidation.dll"
    }
    "NLog" = @{
        Reason = "Logger type injected by Lidarr DI container"
        DllName = "NLog.dll"
    }
}

Write-Host "=== Host Version Checker ===" -ForegroundColor Cyan

# Extract from Docker if requested
if ($ExtractFrom) {
    Write-Host "`nExtracting assemblies from ghcr.io/hotio/lidarr:$ExtractFrom..." -ForegroundColor Yellow
    
    $containerName = "lidarr-extract-temp"
    docker rm $containerName 2>&1 | Out-Null
    
    try {
        docker create --name $containerName "ghcr.io/hotio/lidarr:$ExtractFrom" | Out-Null
        
        $outputDir = Join-Path $ProjectRoot "ext/Lidarr/_output/net8.0"
        if (-not (Test-Path $outputDir)) {
            New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
        }
        
        # Extract only the DLLs we care about
        foreach ($pkg in $HostCoupledPackages.Keys) {
            $dllName = $HostCoupledPackages[$pkg].DllName
            docker cp "${containerName}:/app/bin/$dllName" "$outputDir/$dllName" 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Extracted: $dllName" -ForegroundColor Gray
            }
        }
        
        Write-Host "  Assemblies extracted to: $outputDir" -ForegroundColor Green
    }
    finally {
        docker rm $containerName 2>&1 | Out-Null
    }
}

# Read Directory.Packages.props
Write-Host "`nReading Directory.Packages.props..." -ForegroundColor Yellow
$packagesPropsPath = Join-Path $ProjectRoot "Directory.Packages.props"
if (-not (Test-Path $packagesPropsPath)) {
    Write-Error "Directory.Packages.props not found at: $packagesPropsPath"
    exit 1
}

[xml]$packagesProps = Get-Content $packagesPropsPath
$pinnedVersions = @{}

foreach ($item in $packagesProps.Project.ItemGroup.PackageVersion) {
    if ($item.Include -and $item.Version) {
        $pinnedVersions[$item.Include] = $item.Version
    }
}

# Read host assembly versions
Write-Host "Reading host assembly versions..." -ForegroundColor Yellow
$hostDir = Join-Path $ProjectRoot "ext/Lidarr/_output/net8.0"
if (-not (Test-Path $hostDir)) {
    Write-Error "Host assemblies not found at: $hostDir"
    Write-Host "Run: docker cp <container>:/app/bin ext/Lidarr/_output/net8.0" -ForegroundColor Gray
    exit 1
}

$hostVersions = @{}
foreach ($pkg in $HostCoupledPackages.Keys) {
    $dllPath = Join-Path $hostDir $HostCoupledPackages[$pkg].DllName
    if (Test-Path $dllPath) {
        try {
            $assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)
            $version = $assembly.GetName().Version
            # Get file version which usually matches NuGet version
            $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath)
            $hostVersions[$pkg] = @{
                AssemblyVersion = $version.ToString()
                FileVersion = $fileVersion.FileVersion
                ProductVersion = $fileVersion.ProductVersion
            }
        }
        catch {
            Write-Warning "Failed to read version from $dllPath`: $_"
        }
    }
    else {
        Write-Warning "Host assembly not found: $dllPath"
    }
}

# Compare and report
Write-Host "`n=== Version Comparison ===" -ForegroundColor Cyan
$hasErrors = $false

foreach ($pkg in $HostCoupledPackages.Keys) {
    Write-Host "`n$pkg" -ForegroundColor White
    Write-Host "  Reason: $($HostCoupledPackages[$pkg].Reason)" -ForegroundColor Gray
    
    $pinned = $pinnedVersions[$pkg]
    $host = $hostVersions[$pkg]
    
    if (-not $pinned) {
        Write-Host "  Pinned: NOT FOUND in Directory.Packages.props" -ForegroundColor Red
        $hasErrors = $true
        continue
    }
    
    if (-not $host) {
        Write-Host "  Host: NOT FOUND in ext/Lidarr/_output/net8.0/" -ForegroundColor Red
        $hasErrors = $true
        continue
    }
    
    Write-Host "  Pinned version: $pinned" -ForegroundColor $(if ($pinned) { "White" } else { "Red" })
    Write-Host "  Host assembly: $($host.AssemblyVersion)" -ForegroundColor Gray
    Write-Host "  Host file ver: $($host.FileVersion)" -ForegroundColor Gray
    Write-Host "  Host product:  $($host.ProductVersion)" -ForegroundColor Gray
    
    # Check if pinned version is compatible with host
    # NuGet version should match ProductVersion or be compatible with AssemblyVersion major.minor
    $pinnedMajorMinor = ($pinned -split '\.')[0..1] -join '.'
    $hostMajorMinor = ($host.AssemblyVersion -split '\.')[0..1] -join '.'
    
    if ($pinnedMajorMinor -ne $hostMajorMinor) {
        Write-Host "  STATUS: MISMATCH - Major.Minor differs!" -ForegroundColor Red
        Write-Host "  ACTION: Update Directory.Packages.props to match host version" -ForegroundColor Yellow
        $hasErrors = $true
    }
    else {
        Write-Host "  STATUS: OK" -ForegroundColor Green
    }
}

Write-Host "`n"
if ($hasErrors) {
    Write-Host "=== ISSUES FOUND ===" -ForegroundColor Red
    Write-Host "Update Directory.Packages.props to match host versions, then run:" -ForegroundColor Yellow
    Write-Host "  dotnet test --filter `"FullyQualifiedName~PluginPackagingTests`"" -ForegroundColor Gray
    if ($Strict) {
        exit 1
    }
    else {
        Write-Host "`n(Use -Strict to fail with exit code 1)" -ForegroundColor Gray
        exit 0
    }
}
else {
    Write-Host "=== ALL VERSIONS OK ===" -ForegroundColor Green
    exit 0
}
