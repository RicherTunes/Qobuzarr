# NLog Dependency Solution for Qobuzarr Plugin

## Problem Statement
The Qobuzarr plugin was failing to load in Lidarr with the error:
```
System.IO.FileNotFoundException: Could not load file or assembly 'NLog, Version=6.0.0.0, Culture=neutral, PublicKeyToken=...
```

## Root Cause Analysis

### Issue #1: Version Mismatch
- **Plugin was using NLog 6.0.0** (specified in Directory.Packages.props)
- **Lidarr runtime provides NLog 5.x** (typically 5.3.x or 5.4.x in current releases)
- Version mismatch prevented the plugin from loading

### Issue #2: Incorrect Dependency Management
- Shared dependencies (NLog, Newtonsoft.Json, FluentValidation) were being bundled with the plugin
- These dependencies should be provided by the Lidarr host, not included in plugin

## Solution Implementation

### 1. Fixed NLog Version (Directory.Packages.props)
```xml
<!-- Changed from Version="6.0.0" to Version="5.4.0" -->
<PackageVersion Include="NLog" Version="5.4.0" />
```

### 2. Excluded Shared Dependencies from Plugin Bundle (Qobuzarr.csproj)
```xml
<!-- Shared dependencies - provided by Lidarr host, don't bundle -->
<PackageReference Include="Newtonsoft.Json">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
<PackageReference Include="NLog">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
<PackageReference Include="FluentValidation">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>

<!-- Plugin-specific dependencies - must be bundled -->
<PackageReference Include="TagLibSharp-Lidarr" />
<PackageReference Include="Microsoft.ML" />
<PackageReference Include="System.Resources.Extensions" />
```

### 3. Updated Deployment Target to Exclude Shared DLLs
```xml
<ItemGroup>
  <PluginFiles Include="$(OutputPath)$(AssemblyName).dll" />
  <PluginFiles Include="$(OutputPath)$(AssemblyName).pdb" />
  <PluginFiles Include="$(OutputPath)plugin.json" />
  <PluginFiles Include="$(OutputPath)src\Indexers\ml-baseline-patterns.json" />
  <!-- Exclude Lidarr assemblies and shared dependencies from deployment -->
  <PluginFiles Remove="$(OutputPath)Lidarr.*.dll" />
  <PluginFiles Remove="$(OutputPath)Lidarr.*.xml" />
  <PluginFiles Remove="$(OutputPath)NLog.dll" />
  <PluginFiles Remove="$(OutputPath)Newtonsoft.Json.dll" />
  <PluginFiles Remove="$(OutputPath)FluentValidation.dll" />
</ItemGroup>
```

## Key Principles for Lidarr Plugin Dependencies

### Dependencies Provided by Lidarr Host (Don't Bundle)
- **NLog** - Logging framework
- **Newtonsoft.Json** - JSON serialization
- **FluentValidation** - Validation framework
- **All Lidarr.*.dll assemblies** - Core Lidarr functionality

### Dependencies to Bundle with Plugin
- **Plugin-specific libraries** not used by Lidarr core
- **TagLibSharp-Lidarr** - Audio metadata processing
- **Microsoft.ML** - Machine learning (for this plugin's ML features)
- **System.Resources.Extensions** - Additional resource handling

## Build and Deployment Process

### Clean Build Command
```bash
dotnet clean
dotnet build --configuration Release \
  -p:RunAnalyzersDuringBuild=false \
  -p:EnableNETAnalyzers=false \
  -p:TreatWarningsAsErrors=false \
  -p:EnablePluginDeployment=true
```

### Verify Deployment
After build, only these files should be in the plugin directory:
- `Lidarr.Plugin.Qobuzarr.dll` - Main plugin assembly
- `Lidarr.Plugin.Qobuzarr.pdb` - Debug symbols
- `plugin.json` - Plugin manifest
- `ml-baseline-patterns.json` - ML configuration data

### What NOT to Deploy
Never deploy these to the plugin directory:
- `NLog.dll`
- `Newtonsoft.Json.dll`
- `FluentValidation.dll`
- Any `Lidarr.*.dll` assemblies
- System assemblies that Lidarr provides

## Testing the Fix

1. **Clean previous deployment**:
   ```bash
   dotnet msbuild -target:CleanDeployedPlugin
   ```

2. **Build with corrected configuration**:
   ```bash
   dotnet build --configuration Release -p:EnablePluginDeployment=true
   ```

3. **Restart Lidarr** and verify plugin loads without NLog errors

## Best Practices Summary

1. **Use Lidarr's dependency versions** - Always match the versions that Lidarr runtime provides
2. **Mark shared dependencies with `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>`**
3. **Only bundle plugin-specific dependencies** that Lidarr doesn't provide
4. **Test deployment** by checking that shared DLLs are NOT in the plugin directory
5. **Follow successful plugin patterns** - TrevTV's plugins demonstrate the correct approach

## References
- Lidarr typically uses NLog 5.x (5.3.4 or 5.4.0 in recent versions)
- Successful plugins (TrevTV's Tidal, Deezer, Qobuz) don't bundle shared dependencies
- ILRepack should only merge plugin-specific assemblies, not shared ones