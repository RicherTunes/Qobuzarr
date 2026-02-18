param(
    [string]$Configuration = 'Release',
    [Alias('Solution')]
    [string]$SolutionOrProject = '',
    [string]$Filter = '',
    [string]$Settings = '',
    [string]$ResultsDirectory = '',
    [string[]]$Logger = @(),
    [string[]]$AdditionalArgs = @(),
    [switch]$NoBuild,
    [string]$LidarrAssembliesPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot/.."

if ([string]::IsNullOrWhiteSpace($SolutionOrProject)) {
    $SolutionOrProject = Join-Path $repoRoot 'Qobuzarr.sln'
}

Write-Host "Running tests with stable flags: -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false" -ForegroundColor Cyan
if ($NoBuild) { Write-Host "  - Using: --no-build" -ForegroundColor DarkGray }
if (-not [string]::IsNullOrWhiteSpace($Settings)) { Write-Host "  - Using: --settings $Settings" -ForegroundColor DarkGray }
if (-not [string]::IsNullOrWhiteSpace($Filter)) { Write-Host "  - Using: --filter $Filter" -ForegroundColor DarkGray }
if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) { Write-Host "  - Using: --results-directory $ResultsDirectory" -ForegroundColor DarkGray }
if ($Logger.Count -gt 0) { Write-Host "  - Using: --logger $($Logger -join ', ')" -ForegroundColor DarkGray }
if (-not [string]::IsNullOrWhiteSpace($LidarrAssembliesPath)) { Write-Host "  - Using: -p:LidarrAssembliesPath=$LidarrAssembliesPath" -ForegroundColor DarkGray }

Push-Location $repoRoot
try {
    $arguments = @(
        'test'
        $SolutionOrProject
        '-c', $Configuration
        '-nr:false'
        '-p:BuildInParallel=false'
        '-p:UseSharedCompilation=false'
    )

    if ($NoBuild) {
        $arguments += '--no-build'
    }

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments += '--filter', $Filter
    }

    if (-not [string]::IsNullOrWhiteSpace($Settings)) {
        $arguments += '--settings', $Settings
    }

    if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $arguments += '--results-directory', $ResultsDirectory
    }

    foreach ($log in $Logger) {
        if (-not [string]::IsNullOrWhiteSpace($log)) {
            $arguments += '--logger', $log
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LidarrAssembliesPath)) {
        $arguments += "-p:LidarrAssembliesPath=$LidarrAssembliesPath"
    }

    foreach ($arg in $AdditionalArgs) {
        if (-not [string]::IsNullOrWhiteSpace($arg)) {
            $arguments += $arg
        }
    }

    dotnet @arguments
}
finally {
    Pop-Location
}
