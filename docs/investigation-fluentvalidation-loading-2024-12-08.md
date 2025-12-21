# FluentValidation Loading Investigation - 2024-12-08

## Problem
Plugin fails to load in Lidarr Docker container with error:
```
AssemblyLoadContext is unloading or was already unloaded
```
or:
```
FileNotFoundException: FluentValidation not found
```

## Investigation Steps

### 1. Initial Build Tests
- Built plugin with ILRepack using Docker-extracted assemblies
- FluentValidation.dll kept in output (PR #120 working)
- Plugin DLL size: 3.3MB (merged) vs 1.1MB (unmerged)

### 2. Deployment Tests
| Scenario | FluentValidation.dll | Result |
|----------|---------------------|--------|
| With plugin's own FV.dll | 492KB from NuGet | `AssemblyLoadContext is unloading` |
| Without any FV.dll | None | `FileNotFoundException` |
| With Lidarr's FV.Plugin.dll | 492KB copied from /app/bin | `AssemblyLoadContext is unloading` |

### 3. Assembly Version Analysis
Both assemblies have identical signatures:
- Our FluentValidation.dll: `FluentValidation, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7de548da2fbae0f0`
- Lidarr's FluentValidation.Plugin.dll: Same exact signature

### 4. Comparison with Working Plugin (Brainarr)
**KEY FINDING:**

Brainarr does NOT use a NuGet PackageReference for FluentValidation!
```xml
<!-- Brainarr.Plugin.csproj -->
<Reference Include="FluentValidation">
  <HintPath>$(LidarrPath)\FluentValidation.dll</HintPath>
</Reference>
```

Qobuzarr uses NuGet:
```xml
<!-- Qobuzarr.csproj -->
<PackageReference Include="FluentValidation" />
```

## Root Cause Analysis

When using NuGet PackageReference:
1. NuGet downloads FluentValidation 11.11.0
2. Build copies it to output directory
3. PluginPackaging.targets keeps it (doesn't delete)
4. Plugin ships with its own FluentValidation.dll
5. At runtime, PluginLoadContext loads plugin
6. Plugin requests FluentValidation
7. Context conflict between plugin's FV and Lidarr's FV
8. AssemblyLoadContext fails/unloads

When using direct Lidarr reference (like Brainarr):
1. Build references Lidarr's FluentValidation.dll
2. NO FluentValidation.dll in output
3. At runtime, plugin uses Lidarr's already-loaded FluentValidation
4. No conflicts - works!

## Solution

Change Qobuzarr to reference Lidarr's FluentValidation.dll like Brainarr:

```xml
<!-- Remove NuGet reference -->
<!-- <PackageReference Include="FluentValidation" /> -->

<!-- Add direct reference to Lidarr's FluentValidation -->
<Reference Include="FluentValidation">
  <HintPath>$(LidarrAssembliesPath)\FluentValidation.dll</HintPath>
  <Private>false</Private>
</Reference>
```

## Files Modified
- `Qobuzarr.csproj`: Change FluentValidation from PackageReference to direct Reference
- `Directory.Packages.props`: Keep FluentValidation version for CLI/test projects, add comment
- `ext/Lidarr.Plugin.Common/src/Lidarr.Plugin.Common.csproj`: Update FluentValidation from 9.5.4 to 11.11.0

## Additional Root Cause (Discovered Dec 8, 2024 - Second Issue)

Even after fixing Qobuzarr.csproj, the plugin still failed with "FluentValidation, Version=9.0.0.0".

**Cause**: `Lidarr.Plugin.Common` had a PackageReference to FluentValidation 9.5.4:
```xml
<PackageReference Include="FluentValidation" Version="9.5.4" />
```

This caused the merged plugin assembly to reference FluentValidation 9.0.0.0 (AssemblyVersion),
but Lidarr runtime has FluentValidation 11.x (AssemblyVersion 11.0.0.0).

**Fix**: Updated Lidarr.Plugin.Common to use FluentValidation 11.11.0 to match Lidarr's version.
The Common library only uses `ValidationResult` and `ValidationFailure` which are API-compatible.

## Additional Notes

### Why PluginPackaging.targets keeps FluentValidation
The targets file has this logic:
```xml
<_PluginRuntimeDeps Include="$(OutputPath)FluentValidation.dll"
                    Condition="Exists('$(OutputPath)FluentValidation.dll')" />
```

This was designed for when plugins NEED their own FluentValidation version. But for compatibility with Lidarr's PluginLoadContext, using Lidarr's built-in FluentValidation is better.

### Smoke Test vs Runtime
The smoke test uses `-p:PluginPackagingDisable=true` which skips ILRepack. It only does static analysis, not actual runtime loading. This is why CI passes but runtime fails.

## Attempts That Failed
1. Shipping plugin's own FluentValidation.dll - context conflict
2. Using Lidarr's FluentValidation.Plugin.dll - same conflict
3. Not shipping any FluentValidation.dll - file not found
4. Building without ILRepack - needs all deps, same conflict

## Final Solution
Reference Lidarr's FluentValidation directly, don't ship a separate copy.
