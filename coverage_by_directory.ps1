# Coverage analysis by directory
Write-Host "Qobuzarr Coverage by Directory" -ForegroundColor Green
Write-Host "=============================" -ForegroundColor Green
Write-Host ""

# Get all C# files in src
$allSourceFiles = Get-ChildItem -Path "src" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }

# Get test files
$testFiles = Get-ChildItem -Path "tests/Qobuzarr.Tests/Unit" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }

# Analyze by directory
$directoryCoverage = @{}

foreach ($srcFile in $allSourceFiles) {
    $dir = [System.IO.Path]::GetDirectoryName($srcFile).Split('\')[1]
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($srcFile)

    if (-not $directoryCoverage.ContainsKey($dir)) {
        $directoryCoverage[$dir] = @{
            TotalFiles = 0
            TestedFiles = 0
            UnlistedFiles = @()
        }
    }

    $directoryCoverage[$dir].TotalFiles += 1
    $foundMatch = $false

    foreach ($testFile in $testFiles) {
        if ($testFile -like "*$fileName*") {
            $directoryCoverage[$dir].TestedFiles += 1
            $foundMatch = $true
            break
        }
    }

    if (-not $foundMatch) {
        $directoryCoverage[$dir].UnlistedFiles += $fileName
    }
}

# Display results
Write-Host "Coverage by Directory:" -ForegroundColor Yellow
Write-Host ""

foreach ($dir in $directoryCoverage.Keys | Sort-Object) {
    $data = $directoryCoverage[$dir]
    $percent = [math]::Round(($data.TestedFiles / $data.TotalFiles) * 100, 2)

    Write-Host "$($dir):" -ForegroundColor Cyan
    Write-Host "  Files tested: $($data.TestedFiles)/$($data.TotalFiles) ($percent%)"

    if ($data.UnlistedFiles.Count -gt 0) {
        Write-Host "  Untested files: $($data.UnlistedFiles.Count)"
        $data.UnlistedFiles | Select-Object -First 3 | ForEach-Object {
            Write-Host "    - $_"
        }
        if ($data.UnlistedFiles.Count -gt 3) {
            Write-Host "    ... and $($data.UnlistedFiles.Count - 3) more"
        }
    }
    Write-Host ""
}

# Find top 5 directories with lowest coverage
Write-Host "Top 5 Least Covered Directories:" -ForegroundColor Red
$coverageData = @()
foreach ($dir in $directoryCoverage.Keys) {
    $data = $directoryCoverage[$dir]
    if ($data.TotalFiles -gt 0) {
        $percent = [math]::Round(($data.TestedFiles / $data.TotalFiles) * 100, 2)
        $coverageData += [PSCustomObject]@{
            Directory = $dir
            Percent = $percent
            Tested = $data.TestedFiles
            Total = $data.TotalFiles
        }
    }
}

$coverageData | Sort-Object Percent | Select-Object -First 5 | Format-Table -AutoSize