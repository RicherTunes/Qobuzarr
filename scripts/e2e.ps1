#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run the Qobuzarr Docker E2E smoke harness against a real Lidarr container.

.DESCRIPTION
    Wave 22b — boots a single-plugin Lidarr container (image
    pr-plugins-3.x.y.z, .NET 8) with the merged Qobuzarr plugin DLL mounted,
    waits for Lidarr to become healthy, and runs the DockerE2E smoke matrix:

      * Plugin appears in /api/v1/indexer/schema
      * Plugin appears in /api/v1/downloadclient/schema
      * POST /api/v1/indexer/test with empty settings -> non-5xx
      * POST /api/v1/downloadclient/test with empty settings -> non-5xx

    All tests skip gracefully when Docker isn't running. The harness pins the
    Lidarr image tag in scripts/verify-local.ps1 (LidarrDockerVersion) so this
    script and the local-CI smoke phase use the same image.

    The plugin DLL must already be built with host-bridge (FluentValidation 9.x).
    The fastest way is:

        pwsh scripts/verify-local.ps1 -SkipExtract -SkipTests

    which produces the merged DLL at bin/Lidarr.Plugin.Qobuzarr.dll. Without
    -SkipBuild the script does this for you.

.PARAMETER SkipBuild
    Skip the verify-local.ps1 build prep step. Use when the merged plugin DLL
    is already present at bin/Lidarr.Plugin.Qobuzarr.dll.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Filter
    Optional xUnit filter override. Defaults to Category=DockerE2E.

.EXAMPLE
    pwsh scripts/e2e.ps1
    pwsh scripts/e2e.ps1 -SkipBuild
    pwsh scripts/e2e.ps1 -Filter 'FullyQualifiedName~Indexer_Test'
#>
param(
    [switch]$SkipBuild,
    [string]$Configuration = 'Release',
    [string]$Filter = 'Category=DockerE2E'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot/.."
$testProject = Join-Path $repoRoot 'tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj'

Push-Location $repoRoot
try {
    Write-Host '================================================================================' -ForegroundColor Cyan
    Write-Host '  QOBUZARR DOCKER E2E HARNESS (wave 22b)' -ForegroundColor Cyan
    Write-Host '================================================================================' -ForegroundColor Cyan

    # Pre-flight: docker engine availability. We do NOT fail when Docker is
    # missing - the tests skip gracefully - but a clear heads-up helps.
    $dockerOk = $false
    try {
        & docker info *>$null
        $dockerOk = ($LASTEXITCODE -eq 0)
    } catch {
        $dockerOk = $false
    }

    if (-not $dockerOk) {
        Write-Host '  WARNING: Docker engine is not running. E2E tests will skip.' -ForegroundColor Yellow
        Write-Host '           Start Docker Desktop and re-run to actually exercise the harness.' -ForegroundColor Yellow
    } else {
        Write-Host '  Docker engine: OK' -ForegroundColor Green
    }

    if (-not $SkipBuild) {
        Write-Host ''
        Write-Host '  [1/2] Building plugin (merged ILRepack output -> bin/)...' -ForegroundColor Cyan
        & pwsh (Join-Path $repoRoot 'scripts/verify-local.ps1') -SkipExtract -SkipTests
        if ($LASTEXITCODE -ne 0) { throw 'verify-local.ps1 build prep failed' }
    } else {
        Write-Host '  [1/2] Skipping build (--SkipBuild)' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host "  [2/2] Running E2E tests (filter: $Filter)..." -ForegroundColor Cyan

    & dotnet test $testProject `
        -c $Configuration `
        -v normal `
        -m:1 `
        -p:PluginPackagingDisable=true `
        --filter $Filter

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test exited with code $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host '  E2E harness complete.' -ForegroundColor Green
}
finally {
    Pop-Location
}
