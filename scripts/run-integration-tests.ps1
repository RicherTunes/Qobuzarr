#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs integration test suites locally (host-assembly integration + optional live Lidarr tests).

.DESCRIPTION
    This script is a local "pipeline mimic" for integration validation:
    1) (Optional) Extract host assemblies from a Lidarr Docker image
    2) (Optional) Verify host-coupled package pins match the host
    3) Run tests/Qobuzarr.Tests with Category=Integration (excluding LiveIntegration by default)
    4) (Optional) Run scripts/docker-smoke-test.ps1 (runtime plugin load check)
    5) (Optional) Start local Lidarr via docker-compose.yml, deploy the plugin, and run tests/Integration

    Notes:
    - tests/Qobuzarr.Tests integration tests require Lidarr host assemblies for compilation.
    - tests/Integration are live tests that require a running Lidarr instance and API key.
#>

[CmdletBinding()]
param(
    # Ecosystem baseline: keep aligned with other plugins unless explicitly overridden
    [string]$LidarrTag = "pr-plugins-2.14.2.4786",
    [string]$Configuration = "Release",

    [switch]$ExtractHostAssemblies,
    [string]$HostAssembliesPath,
    [switch]$CheckHostVersions,

    [switch]$SmokeTest,

    [switch]$IncludeLive,
    [string]$LidarrUrl,
    [string]$LidarrApiKey,
    [switch]$EnableLiveIntegrationTests
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

function Resolve-RepoPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $repoRoot $Path)
}

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-DefaultHostAssembliesPath {
    $candidates = @(
        (Join-Path $repoRoot "ext/Lidarr/_output/net8.0"),
        (Join-Path $repoRoot "ext/lidarr-assemblies"),
        (Join-Path $repoRoot "ext/Lidarr-docker/_output/net8.0")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "Lidarr.dll")) {
            return $candidate
        }
    }

    return $candidates[0]
}

function Extract-HostAssembliesFromDocker {
    param(
        [string]$ImageTag,
        [string]$OutputDir
    )

    Require-Command "docker"
    Ensure-Directory $OutputDir

    $containerName = "qobuzarr-host-extract-temp"
    docker rm -f $containerName 2>$null | Out-Null

    try {
        Write-Host "Pulling ghcr.io/hotio/lidarr:$ImageTag..." -ForegroundColor Cyan
        docker pull "ghcr.io/hotio/lidarr:$ImageTag" | Out-Null

        Write-Host "Creating container for extraction..." -ForegroundColor Cyan
        docker create --name $containerName "ghcr.io/hotio/lidarr:$ImageTag" | Out-Null

        Write-Host "Extracting host assemblies to: $OutputDir" -ForegroundColor Cyan
        docker cp "${containerName}:/app/bin/." "$OutputDir/" | Out-Null
    }
    finally {
        docker rm -f $containerName 2>$null | Out-Null
    }

    $required = @("Lidarr.dll", "Lidarr.Core.dll", "Lidarr.Common.dll", "Lidarr.Http.dll")
    $missing = @()
    foreach ($dll in $required) {
        if (-not (Test-Path (Join-Path $OutputDir $dll))) {
            $missing += $dll
        }
    }
    if ($missing.Count -gt 0) {
        throw "Host assembly extraction incomplete. Missing: $($missing -join ', '). OutputDir: $OutputDir"
    }
}

function Read-LidarrApiKeyFromConfigXml {
    param([string]$ConfigXmlPath)

    if (-not (Test-Path $ConfigXmlPath)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -Path $ConfigXmlPath -Raw
        $node = $xml.SelectSingleNode("//ApiKey")
        $value = if ($null -ne $node) { $node.InnerText } else { $null }
        if ([string]::IsNullOrWhiteSpace($value)) { return $null }
        return $value.Trim()
    }
    catch {
        return $null
    }
}

function Copy-PluginToDockerComposeVolume {
    $binDir = Join-Path $repoRoot "bin"
    $pluginDir = Join-Path $repoRoot ".docker/plugins/RicherTunes/Qobuzarr"
    Ensure-Directory $pluginDir

    $required = @("Lidarr.Plugin.Qobuzarr.dll", "plugin.json", "Lidarr.Plugin.Abstractions.dll")
    foreach ($file in $required) {
        $src = Join-Path $binDir $file
        if (-not (Test-Path $src)) {
            throw "Required build output missing: $src"
        }
        Copy-Item -Path $src -Destination $pluginDir -Force
    }

    $optional = @(
        "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        "Microsoft.Extensions.Logging.Abstractions.dll"
    )
    foreach ($file in $optional) {
        $src = Join-Path $binDir $file
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination $pluginDir -Force
        }
    }

    $banned = @("FluentValidation.dll", "NLog.dll")
    foreach ($file in $banned) {
        $dst = Join-Path $pluginDir $file
        if (Test-Path $dst) {
            Remove-Item -Path $dst -Force
        }
    }

    Write-Host "Plugin deployed to: $pluginDir" -ForegroundColor Green
}

function Start-DockerComposeLidarr {
    $composeFile = Join-Path $repoRoot "docker-compose.yml"
    if (-not (Test-Path $composeFile)) {
        throw "docker-compose.yml not found at: $composeFile"
    }

    Require-Command "docker"

    $canUseDockerComposeV2 = $false
    try {
        & docker compose version 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $canUseDockerComposeV2 = $true }
    }
    catch {
        $canUseDockerComposeV2 = $false
    }

    if ($canUseDockerComposeV2) {
        & docker compose -f $composeFile up -d lidarr | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }
        return
    }

    Require-Command "docker-compose"
    & docker-compose -f $composeFile up -d lidarr | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker-compose up failed" }
}

function Wait-LidarrHealthy {
    param(
        [string]$BaseUrl,
        [int]$TimeoutSeconds = 120
    )

    $start = Get-Date
    while ((Get-Date) - $start -lt [TimeSpan]::FromSeconds($TimeoutSeconds)) {
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/api/v1/health" -TimeoutSec 5 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 5
        }
    }

    throw "Timed out waiting for Lidarr health at $BaseUrl"
}

function Run-DotNetTest {
    param(
        [string]$ProjectPath,
        [string]$Filter,
        [string]$RunSettingsPath,
        [string]$ResultsDir,
        [hashtable]$AdditionalEnv = @{},
        [string[]]$MsBuildProps = @()
    )

    Ensure-Directory $ResultsDir

    $loggerName = ([IO.Path]::GetFileNameWithoutExtension($ProjectPath)) + ".trx"
    $dotnetArgs = @(
        "test", $ProjectPath,
        "--configuration", $Configuration,
        "--no-build",
        "--logger", "trx;LogFileName=$loggerName",
        "--results-directory", $ResultsDir,
        "--nologo"
    )

    if ($RunSettingsPath -and (Test-Path $RunSettingsPath)) {
        $dotnetArgs += @("--settings", $RunSettingsPath)
    }
    if ($Filter -and -not [string]::IsNullOrWhiteSpace($Filter)) {
        $dotnetArgs += @("--filter", $Filter)
    }
    foreach ($prop in $MsBuildProps) {
        $dotnetArgs += $prop
    }

    foreach ($key in $AdditionalEnv.Keys) {
        Set-Item -Path ("Env:" + $key) -Value "$($AdditionalEnv[$key])"
    }

    # Avoid emitting dotnet output into the function pipeline; callers often assign the result to a variable.
    & dotnet @dotnetArgs 2>&1 | Out-Host
    $exitCode = [int]$LASTEXITCODE
    return $exitCode
}

Push-Location $repoRoot
try {
    Require-Command "dotnet"

    $hostAssembliesDir = Resolve-RepoPath $HostAssembliesPath
    if (-not $hostAssembliesDir) { $hostAssembliesDir = Get-DefaultHostAssembliesPath }

    $resultsRoot = Join-Path $repoRoot ("test-results/integration/" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Ensure-Directory $resultsRoot

    Write-Host "=== Qobuzarr Integration Runner ===" -ForegroundColor Cyan
    Write-Host "Config: $Configuration"
    Write-Host "Host assemblies: $hostAssembliesDir"
    Write-Host "Lidarr tag: $LidarrTag"
    Write-Host "Results: $resultsRoot"

    if ($ExtractHostAssemblies) {
        Write-Host "`n[1/6] Extracting host assemblies..." -ForegroundColor Yellow
        Extract-HostAssembliesFromDocker -ImageTag $LidarrTag -OutputDir $hostAssembliesDir
    }

    if (-not (Test-Path (Join-Path $hostAssembliesDir "Lidarr.dll"))) {
        Write-Host "`n[!] Host assemblies missing at: $hostAssembliesDir" -ForegroundColor Red
        Write-Host "Either extract them from Docker:" -ForegroundColor Gray
        Write-Host "  .\\scripts\\run-integration-tests.ps1 -ExtractHostAssemblies -LidarrTag $LidarrTag" -ForegroundColor Gray
        Write-Host "Or manually docker-cp /app/bin into that directory." -ForegroundColor Gray
        throw "Missing Lidarr.dll under host assemblies directory"
    }

    if ($CheckHostVersions) {
        Write-Host "`n[2/6] Checking host-coupled package pins..." -ForegroundColor Yellow
        & (Join-Path $repoRoot "scripts/check-host-versions.ps1") -Strict -HostAssembliesDir $hostAssembliesDir
        if ($LASTEXITCODE -ne 0) { throw "Host version check failed" }
    }

    Write-Host "`n[3/6] Building test projects..." -ForegroundColor Yellow
    dotnet build tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj -c $Configuration --nologo `
        -p:LidarrAssembliesPath=$([char]34)$hostAssembliesDir$([char]34) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj" }

    Write-Host "`n[4/6] Running host-assembly integration tests (tests/Qobuzarr.Tests)..." -ForegroundColor Yellow
    $compileIntegrationFilter = "Category=Integration&Category!=LiveIntegration"
    $exit = Run-DotNetTest `
        -ProjectPath "tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj" `
        -Filter $compileIntegrationFilter `
        -RunSettingsPath (Join-Path $repoRoot "tests/Full.runsettings") `
        -ResultsDir (Join-Path $resultsRoot "host-integration") `
        -MsBuildProps @("-p:LidarrAssembliesPath=$([char]34)$hostAssembliesDir$([char]34)")

    if ($exit -ne 0) {
        throw "Host-assembly integration tests failed"
    }

    if ($SmokeTest) {
        Write-Host "`n[5/6] Running Docker smoke test..." -ForegroundColor Yellow
        & (Join-Path $repoRoot "scripts/docker-smoke-test.ps1") -SkipBuild -LidarrTag $LidarrTag
        if ($LASTEXITCODE -ne 0) {
            throw "Docker smoke test failed"
        }
    }

    if ($IncludeLive) {
        Write-Host "`n[6/6] Running live Lidarr integration tests (tests/Integration)..." -ForegroundColor Yellow

        Start-DockerComposeLidarr

        Write-Host "Building plugin..." -ForegroundColor Gray
        dotnet build Qobuzarr.csproj -c $Configuration --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Build failed: Qobuzarr.csproj" }

        Copy-PluginToDockerComposeVolume
        Write-Host "Restarting Lidarr..." -ForegroundColor Gray
        docker restart qobuzarr-lidarr 2>$null | Out-Null

        if (-not $LidarrUrl) { $LidarrUrl = "http://localhost:8686" }
        Wait-LidarrHealthy -BaseUrl $LidarrUrl -TimeoutSeconds 180

        if (-not $LidarrApiKey) { $LidarrApiKey = $env:LIDARR_API_KEY }
        if (-not $LidarrApiKey) {
            $configXml = Join-Path $repoRoot ".docker/config/config.xml"
            $LidarrApiKey = Read-LidarrApiKeyFromConfigXml -ConfigXmlPath $configXml
        }
        if (-not $LidarrApiKey) {
            throw "Lidarr API key not set. Provide -LidarrApiKey, set LIDARR_API_KEY, or ensure .docker/config/config.xml contains <ApiKey>."
        }

        $liveEnv = @{
            "LIDARR_URL" = $LidarrUrl
            "LIDARR_API_KEY" = $LidarrApiKey
        }
        if ($EnableLiveIntegrationTests) {
            $liveEnv["ENABLE_LIVE_INTEGRATION_TESTS"] = "true"
        }

        dotnet build "tests/Integration/Qobuzarr.IntegrationTests.csproj" -c $Configuration --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Build failed: tests/Integration/Qobuzarr.IntegrationTests.csproj" }

        $liveExit = Run-DotNetTest `
            -ProjectPath "tests/Integration/Qobuzarr.IntegrationTests.csproj" `
            -Filter "Category=Integration" `
            -RunSettingsPath (Join-Path $repoRoot "tests/Full.runsettings") `
            -ResultsDir (Join-Path $resultsRoot "live-integration") `
            -AdditionalEnv $liveEnv

        if ($liveExit -ne 0) { throw "Live integration tests failed" }
    }

    Write-Host "`n✅ Integration run complete." -ForegroundColor Green
}
finally {
    Pop-Location
}
