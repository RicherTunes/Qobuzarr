# Check what version of NLog is referenced by Lidarr.Core.dll
$assembly = [System.Reflection.Assembly]::LoadFrom("I:\Arr-Plugins\Lidarr\Qobuzarr\ext\Lidarr\_output\net8.0\Lidarr.Core.dll")
$refs = $assembly.GetReferencedAssemblies()
foreach ($ref in $refs) {
    if ($ref.Name -eq "NLog") {
        Write-Host "NLog reference found in Lidarr.Core.dll:"
        Write-Host "  Name: $($ref.Name)"
        Write-Host "  Version: $($ref.Version)"
        Write-Host "  Full Name: $($ref.FullName)"
    }
}