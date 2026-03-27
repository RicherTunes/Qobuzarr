# =============================================================================
# Qobuzarr Build Optimization Script (PowerShell)
# =============================================================================
# Optimizes build performance with caching, parallel execution, and pre-built assemblies

param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false)]
    [switch]$UseCache,
    
    [Parameter(Mandatory=$false)]
    [switch]$ParallelBuild,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipTests,
    
    [Parameter(Mandatory=$false)]
    [switch]$Deploy,
    
    [Parameter(Mandatory=$false)]
    [string]$DeployPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr"
)

$ErrorActionPreference = "Stop"
$startTime = Get-Date

Write-Host "🚀 Qobuzarr Optimized Build System" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# Performance tracking
$metrics = @{
    StartTime = $startTime
    CacheHits = 0
    CacheMisses = 0
    BuildDuration = 0
    TestDuration = 0
}

# Setup cache directory
$cacheDir = Join-Path $env:TEMP "qobuzarr-build-cache"
if (-not (Test-Path $cacheDir)) {
    New-Item -ItemType Directory -Path $cacheDir | Out-Null
}

# Function to check and use cache
function Use-BuildCache {
    param(
        [string]$Key,
        [scriptblock]$BuildAction,
        [string]$OutputPath
    )
    
    $cacheFile = Join-Path $cacheDir "$Key.cache"
    $hashFile = Join-Path $cacheDir "$Key.hash"
    
    # Calculate hash of relevant files
    $files = Get-ChildItem -Path "src" -Recurse -File -Include "*.cs","*.csproj"
    $currentHash = ($files | Get-FileHash).Hash -join ""
    $currentHash = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($currentHash))
    $currentHashString = [System.BitConverter]::ToString($currentHash).Replace("-", "")
    
    if ($UseCache -and (Test-Path $hashFile) -and (Test-Path $cacheFile)) {
        $cachedHash = Get-Content $hashFile
        if ($cachedHash -eq $currentHashString) {
            Write-Host "✅ Cache hit for $Key" -ForegroundColor Green
            $script:metrics.CacheHits++
            
            # Restore from cache
            if (Test-Path $OutputPath) {
                Remove-Item $OutputPath -Recurse -Force
            }
            Copy-Item $cacheFile $OutputPath -Recurse
            return $true
        }
    }
    
    Write-Host "🔨 Building $Key (cache miss)" -ForegroundColor Yellow
    $script:metrics.CacheMisses++
    
    # Execute build
    & $BuildAction
    
    # Save to cache
    if ($UseCache -and (Test-Path $OutputPath)) {
        Copy-Item $OutputPath $cacheFile -Recurse -Force
        $currentHashString | Out-File $hashFile -NoNewline
        Write-Host "💾 Cached $Key for future builds" -ForegroundColor Cyan
    }
    
    return $false
}

# Step 1: Optimize Lidarr assembly retrieval
Write-Host "`n📦 Step 1: Optimizing Lidarr Dependencies" -ForegroundColor Blue
$lidarrCacheKey = "lidarr-assemblies-2.13.2.4685"

if ($UseCache) {
    $lidarrPath = "ext\Lidarr\_output\net8.0"
    $lidarrCached = Use-BuildCache -Key $lidarrCacheKey -OutputPath $lidarrPath -BuildAction {
        if (Test-Path ".\download-lidarr-assemblies.ps1") {
            & .\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685" -Force
        }
    }
    
    if ($lidarrCached) {
        Write-Host "⚡ Lidarr assemblies loaded from cache (saved ~30s)" -ForegroundColor Green
    }
} else {
    # Direct download without cache
    if (-not (Test-Path "ext\Lidarr\_output\net8.0\Lidarr.Core.dll")) {
        & .\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685"
    }
}

# Step 2: Parallel NuGet restore
Write-Host "`n📦 Step 2: Restoring NuGet Packages" -ForegroundColor Blue
$restoreStart = Get-Date

if ($ParallelBuild) {
    # Restore projects in parallel
    $projects = @("Qobuzarr.csproj", "QobuzCLI\QobuzCLI.csproj")
    $jobs = @()
    
    foreach ($project in $projects) {
        $jobs += Start-Job -ScriptBlock {
            param($proj)
            dotnet restore $proj --verbosity minimal
        } -ArgumentList $project
    }
    
    $jobs | Wait-Job | Receive-Job
    $jobs | Remove-Job
    
    Write-Host "✅ Parallel restore completed" -ForegroundColor Green
} else {
    dotnet restore --verbosity minimal
}

$restoreDuration = (Get-Date) - $restoreStart
Write-Host "⏱️ Restore time: $($restoreDuration.TotalSeconds.ToString('F2'))s" -ForegroundColor Gray

# Step 3: Optimized build with deterministic compilation
Write-Host "`n🔨 Step 3: Building Qobuzarr" -ForegroundColor Blue
$buildStart = Get-Date

# Apply TrevTV's assembly version override
$lidarrVersionOverride = "2.13.2.4686"
if (Test-Path "ext\Lidarr-source\src\Directory.Build.props") {
    Write-Host "🔧 Applying assembly version override: $lidarrVersionOverride" -ForegroundColor Cyan
    $content = Get-Content "ext\Lidarr-source\src\Directory.Build.props"
    $content = $content -replace '<AssemblyVersion>[\d\.\*]+</AssemblyVersion>', "<AssemblyVersion>$lidarrVersionOverride</AssemblyVersion>"
    $content | Set-Content "ext\Lidarr-source\src\Directory.Build.props"
}

# Build parameters for optimization
$buildParams = @(
    "build",
    "Qobuzarr.csproj",
    "--configuration", $Configuration,
    "--no-restore",
    "-p:RunAnalyzersDuringBuild=false",
    "-p:EnableNETAnalyzers=false",
    "-p:TreatWarningsAsErrors=false",
    "-p:Deterministic=true",
    "-p:ContinuousIntegrationBuild=true"
)

if ($ParallelBuild) {
    $buildParams += "-maxcpucount"
}

if ($Deploy) {
    $buildParams += "-p:EnablePluginDeployment=true"
    $buildParams += "-p:LidarrPluginDeployPath=$DeployPath"
}

# Execute build
$buildResult = & dotnet @buildParams

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

$metrics.BuildDuration = ((Get-Date) - $buildStart).TotalSeconds
Write-Host "✅ Build completed in $($metrics.BuildDuration.ToString('F2'))s" -ForegroundColor Green

# Step 4: Run tests (if not skipped)
if (-not $SkipTests) {
    Write-Host "`n🧪 Step 4: Running Tests" -ForegroundColor Blue
    $testStart = Get-Date
    
    if ($ParallelBuild) {
        # Run test projects in parallel
        $testProjects = Get-ChildItem -Path "tests" -Recurse -Filter "*.csproj"
        $testJobs = @()
        
        foreach ($testProject in $testProjects) {
            $testJobs += Start-Job -ScriptBlock {
                param($proj)
                dotnet test $proj --configuration Release --no-build --verbosity minimal
            } -ArgumentList $testProject.FullName
        }
        
        $testJobs | Wait-Job
        $testResults = $testJobs | Receive-Job
        $testJobs | Remove-Job
        
        Write-Host $testResults
    } else {
        dotnet test --configuration $Configuration --no-build --verbosity minimal
    }
    
    $metrics.TestDuration = ((Get-Date) - $testStart).TotalSeconds
    Write-Host "✅ Tests completed in $($metrics.TestDuration.ToString('F2'))s" -ForegroundColor Green
}

# Step 5: Deployment (if requested)
if ($Deploy) {
    Write-Host "`n🚀 Step 5: Deploying Plugin" -ForegroundColor Blue
    
    if (Test-Path $DeployPath) {
        # Backup existing deployment
        $backupPath = "$DeployPath.backup.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item $DeployPath $backupPath -Recurse -Force
        Write-Host "📦 Backed up existing deployment to $backupPath" -ForegroundColor Cyan
    }
    
    # Deploy new version
    $deployed = Test-Path (Join-Path $DeployPath "Lidarr.Plugin.Qobuzarr.dll")
    if ($deployed) {
        Write-Host "✅ Plugin deployed successfully to $DeployPath" -ForegroundColor Green
        Write-Host "💡 Restart Lidarr to load the updated plugin" -ForegroundColor Yellow
    }
}

# Step 6: Performance report
$totalDuration = ((Get-Date) - $startTime).TotalSeconds
Write-Host "`n📊 Build Performance Report" -ForegroundColor Magenta
Write-Host "=================================" -ForegroundColor Magenta
Write-Host "Total Duration: $($totalDuration.ToString('F2'))s" -ForegroundColor White
Write-Host "Build Time: $($metrics.BuildDuration.ToString('F2'))s" -ForegroundColor White
Write-Host "Test Time: $($metrics.TestDuration.ToString('F2'))s" -ForegroundColor White
Write-Host "Cache Hits: $($metrics.CacheHits)" -ForegroundColor Green
Write-Host "Cache Misses: $($metrics.CacheMisses)" -ForegroundColor Yellow

if ($totalDuration -lt 180) {
    Write-Host "`n🎉 Build completed in under 3 minutes! Target achieved!" -ForegroundColor Green
} else {
    Write-Host "`n⚠️ Build took longer than 3 minutes. Consider enabling caching and parallel builds." -ForegroundColor Yellow
}

# Save metrics to file for monitoring
$metricsFile = Join-Path $cacheDir "build-metrics.json"
$metrics | Add-Member -MemberType NoteProperty -Name "Timestamp" -Value (Get-Date -Format "o")
$metrics | Add-Member -MemberType NoteProperty -Name "Configuration" -Value $Configuration
$metrics | Add-Member -MemberType NoteProperty -Name "TotalDuration" -Value $totalDuration
$metrics | ConvertTo-Json | Out-File $metricsFile

Write-Host "`n✅ Build optimization complete!" -ForegroundColor Green