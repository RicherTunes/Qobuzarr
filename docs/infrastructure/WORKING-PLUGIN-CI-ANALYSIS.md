# Analysis of Working Lidarr Plugin CI/CD Solutions

## Date: 2025-08-24

## Summary

After analyzing TrevTV's and TypNull's successful Lidarr plugins, I've discovered the key techniques that enable GitHub Actions builds to work with Lidarr plugins.

## Repositories Analyzed

1. **TrevTV's Tidal Plugin**: https://github.com/TrevTV/Lidarr.Plugin.Tidal
2. **TrevTV's Qobuz Plugin**: https://github.com/TrevTV/Lidarr.Plugin.Qobuz  
3. **TypNull's Tubifarry Plugin**: https://github.com/TypNull/Tubifarry

## Key Findings

### 1. Protocol Implementation Pattern (CONFIRMED)

**All working plugins use identical Protocol pattern:**

#### TrevTV's Tidal Plugin:
```csharp
// Tidal.cs
public override string Protocol => nameof(TidalDownloadProtocol);

// TidalDownloadProtocol.cs
namespace NzbDrone.Core.Indexers
{
    public class TidalDownloadProtocol : IDownloadProtocol
    {
    }
}
```

#### TypNull's Tubifarry Plugin:
```csharp
// YoutubeIndexer.cs  
public override string Protocol => nameof(YoutubeDownloadProtocol);

// DownloadProtocols.cs
namespace NzbDrone.Core.Indexers
{
    public class YoutubeDownloadProtocol : IDownloadProtocol { }
    public class SoulseekDownloadProtocol : IDownloadProtocol { }
}
```

**✅ Our implementation matches exactly:** `string Protocol => nameof(QobuzarrDownloadProtocol)`

### 2. Assembly Reference Strategy

#### TrevTV's Approach:
- **Path**: `ext\Lidarr\src\NzbDrone.Core\Lidarr.Core.csproj`
- **Method**: Direct ProjectReference to Lidarr source
- **Version Override**: Line 52 in CI: `sed` command to override AssemblyVersion

#### TypNull's Approach:
- **Path**: `Submodules\Lidarr\src\NzbDrone.Core\Lidarr.Core.csproj`
- **Method**: Git submodule + ProjectReference
- **NuGet Sources**: **LIMITED** to only essential feeds (3 sources vs our 7)

### 3. CI/CD Success Factors

#### TrevTV's Build Workflow:
```yaml
env:
  DOTNET_VERSION: 8.0.404    # .NET 8.0!
  MINIMUM_LIDARR_VERSION: 2.13.0.4664

steps:
  - name: Update Version Info
    run: |
      # Override BOTH plugin and Lidarr versions
      sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$PLUGIN_VERSION<\/AssemblyVersion>/g" src/Directory.Build.props
      sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$MINIMUM_LIDARR_VERSION<\/AssemblyVersion>/g" ext/Lidarr/src/Directory.Build.props

  - name: Build
    run: |
      dotnet restore src/*.sln
      dotnet build src/*.sln -c Release -f net6.0
```

#### TypNull's Build Workflow:
```yaml
- name: Build with package version and metadata
  run: |
    # Restore first
    dotnet restore *.sln
    
    # Build with extensive version metadata
    dotnet build *.sln -c Release -f ${{ inputs.framework }} \
      -p:Version=${{ inputs.package_version }} \
      -p:AssemblyVersion=${{ inputs.package_version }} \
      -p:FileVersion=${{ inputs.package_version }} \
      [... many more parameters ...]
```

### 4. The Critical Difference - NuGet Configuration

#### Our Current Setup (7 sources - CAUSES ISSUES):
```xml
<!-- We inherit ALL Lidarr sources from their Directory.Packages.props -->
- nuget.org
- pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json
- pkgs.dev.azure.com/Servarr/Servarr/_packaging/dotnet-bsd-crossbuild/nuget/v3/index.json
- pkgs.dev.azure.com/Servarr/Servarr/_packaging/Mono.Posix.NETStandard/nuget/v3/index.json
- pkgs.dev.azure.com/Servarr/Servarr/_packaging/SQLite/nuget/v3/index.json
- pkgs.dev.azure.com/Servarr/coverlet/_packaging/coverlet-nightly/nuget/v3/index.json
- pkgs.dev.azure.com/Servarr/Servarr/_packaging/FluentMigrator/nuget/v3/index.json
```

#### TypNull's Setup (3 sources - WORKS):
```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="Taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
  <add key="SQLite" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/SQLite/nuget/v3/index.json" />
  <add key="FluentMigrator" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/FluentMigrator/nuget/v3/index.json" />
</packageSources>
```

### 5. Project Structure Comparison

#### TrevTV Structure:
```
ext/Lidarr/               # Direct clone
src/Lidarr.Plugin.Tidal/  # Plugin code
src/TidalSharp/           # Helper library
```

#### TypNull Structure:
```
Submodules/Lidarr/        # Git submodule
Tubifarry/                # Plugin code (single project)
```

#### Our Structure:
```
ext/Lidarr-source/        # Direct clone (like TrevTV)
src/                      # Plugin code (organized in folders)
QobuzCLI/                 # CLI wrapper
```

## Root Cause of Our CI Failure

### The Problem:
1. **Too many NuGet sources** - We inherit all 7 sources from Lidarr's build system
2. **Missing authentication** - GitHub Actions can't authenticate to private Azure DevOps feeds
3. **Package source conflicts** - Central package management + multiple sources causes NU1507/NU1008 errors

### The Solution:
**Use TypNull's approach**: Create a minimal `NuGet.config` with only the essential private feeds that are accessible.

## Recommended Implementation

### Option 1: Minimal NuGet.config (Like TypNull)
Create our own `NuGet.config` that limits sources to essentials:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="Taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

### Option 2: Docker Assembly Extraction (From CLAUDE.md)
Extract assemblies from the Docker image instead of building from source:
```bash
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker create --name temp ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker cp temp:/app/bin/. ext/Lidarr-plugins/_output/
docker rm temp
```

### Option 3: Hybrid Approach
- **Local development**: Continue using source builds (works perfectly)
- **CI/CD**: Use pre-extracted assemblies committed to the repo

## Implementation Priority

1. **High Priority**: Create minimal NuGet.config to reduce package source conflicts
2. **Medium Priority**: Implement TrevTV's exact version override approach  
3. **Low Priority**: Consider Docker assembly extraction for long-term solution

## Key Insights

### Why TypNull's CI Works:
- ✅ **Minimal NuGet sources** - Only what's absolutely needed
- ✅ **Proper submodule setup** - Lidarr source is managed as submodule
- ✅ **Complex but working CI** - Multiple workflows that coordinate properly

### Why TrevTV's CI Works:
- ✅ **Simple approach** - Single workflow file
- ✅ **Version override** - Magic sed command fixes version mismatches
- ✅ **Direct Lidarr integration** - References Lidarr source directly

### Why Our CI Fails:
- ❌ **Too many NuGet sources** - Inherits all 7 sources from Lidarr
- ❌ **Authentication issues** - Can't access private Azure DevOps feeds
- ❌ **Package management conflicts** - Central package management + multiple sources

## Next Steps

1. **Implement minimal NuGet.config** based on TypNull's approach
2. **Apply TrevTV's version override technique** 
3. **Test CI builds** with the new configuration
4. **Document the working solution** in CLAUDE.md

This analysis proves that **CI builds ARE possible** for Lidarr plugins - we just need to implement the right approach!