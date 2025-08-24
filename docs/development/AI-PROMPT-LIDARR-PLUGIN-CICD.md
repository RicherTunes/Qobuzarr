# AI Prompt: Implementing Working CI/CD for Lidarr Plugins

## AI System Prompt for Lidarr Plugin Development

*Use this prompt to teach AI models how to properly implement CI/CD for Lidarr plugins based on proven working solutions.*

---

### Context for AI Assistant

You are helping a developer implement GitHub Actions CI/CD for a Lidarr plugin. This is a **complex challenge** that has specific requirements due to Lidarr's plugin architecture.

### Critical Knowledge

#### **The Problem**: 
Lidarr plugins require private Azure DevOps NuGet feeds that GitHub Actions cannot authenticate to, causing CI builds to fail with errors like:
```
error NU1101: Unable to find package Servarr.FluentMigrator.Runner
error NU1008: Projects that use central package version management should not define the version
```

#### **The Solution**:
Based on analysis of **TypNull's Tubifarry plugin** (first successful Lidarr plugin CI) and **TrevTV's Tidal plugin**, there are proven approaches:

### Working Implementation Pattern

#### **Method 1: Docker Assembly Extraction** (RECOMMENDED)

**Why it works**: Extract pre-built plugins branch assemblies from the Docker image, avoiding private NuGet feeds entirely.

```yaml
name: Build Plugin
on: [push, pull_request]

env:
  DOTNET_VERSION: 8.0.404
  PLUGIN_VERSION: 1.0.${{ github.run_number }}
  LIDARR_DOCKER_VERSION: pr-plugins-2.13.3.4692

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Extract Lidarr assemblies from Docker
      run: |
        # Pull plugins branch Docker image
        docker pull ghcr.io/hotio/lidarr:${{ env.LIDARR_DOCKER_VERSION }}
        docker create --name temp-lidarr ghcr.io/hotio/lidarr:${{ env.LIDARR_DOCKER_VERSION }}
        
        # Create output directory
        mkdir -p ext/Lidarr-docker/_output/net6.0
        
        # Extract required assemblies
        docker cp temp-lidarr:/app/bin/Lidarr.dll ext/Lidarr-docker/_output/net6.0/
        docker cp temp-lidarr:/app/bin/Lidarr.Common.dll ext/Lidarr-docker/_output/net6.0/
        docker cp temp-lidarr:/app/bin/Lidarr.Core.dll ext/Lidarr-docker/_output/net6.0/
        docker cp temp-lidarr:/app/bin/Lidarr.Http.dll ext/Lidarr-docker/_output/net6.0/
        
        # Cleanup
        docker rm temp-lidarr

    - name: Create CI project file
      run: |
        cat > Plugin.CI.csproj << 'EOF'
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net6.0</TargetFramework>
            <AssemblyName>Lidarr.Plugin.YourPlugin</AssemblyName>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup>
          
          <ItemGroup>
            <Compile Include="src/**/*.cs" />
          </ItemGroup>
          
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            <PackageReference Include="NLog" Version="5.4.0" />
            <PackageReference Include="FluentValidation" Version="9.5.4" />
          </ItemGroup>
          
          <ItemGroup>
            <Reference Include="Lidarr.Core">
              <HintPath>ext/Lidarr-docker/_output/net6.0/Lidarr.Core.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="Lidarr.Common">
              <HintPath>ext/Lidarr-docker/_output/net6.0/Lidarr.Common.dll</HintPath>
              <Private>false</Private>
            </Reference>
          </ItemGroup>
        </Project>
        EOF

    - name: Build plugin
      run: |
        dotnet restore Plugin.CI.csproj
        dotnet build Plugin.CI.csproj --configuration Release --no-restore

    - name: Upload artifact
      uses: actions/upload-artifact@v4
      with:
        name: plugin-build
        path: bin/**/*.dll
```

#### **Method 2: Minimal NuGet Sources** (Alternative)

Create `NuGet.config` with only essential sources:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="Taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

### Critical Protocol Implementation

**All working Lidarr plugins use this exact pattern:**

```csharp
// 1. Create empty protocol class
namespace NzbDrone.Core.Indexers
{
    public class YourPluginDownloadProtocol : IDownloadProtocol { }
}

// 2. Use string Protocol with nameof()
public class YourIndexer : HttpIndexerBase<YourIndexerSettings>
{
    public override string Protocol => nameof(YourPluginDownloadProtocol);
}

public class YourDownloadClient : DownloadClientBase<YourDownloadSettings>
{
    public override string Protocol => nameof(YourPluginDownloadProtocol);
}
```

**NEVER use:**
- `DownloadProtocol.Unknown` enum (wrong for plugins branch)
- Custom interfaces that don't exist
- Conditional compilation between branches

### Project Structure Requirements

```
YourPlugin/
├── src/                           # Plugin source code
├── .github/workflows/             # CI/CD workflows
├── NuGet.config                   # Minimal package sources
├── Directory.Packages.props       # Central package management
└── ext/                          # External dependencies
    └── Lidarr-docker/_output/    # Docker-extracted assemblies (CI only)
```

### Common Pitfalls to Avoid

1. **❌ Building Lidarr source in CI** - Causes private NuGet feed errors
2. **❌ Using too many package sources** - Triggers NU1507 warnings and auth failures
3. **❌ Wrong Protocol implementation** - Must use `string` not enum for plugins branch
4. **❌ Missing IDownloadProtocol class** - Required for plugins branch compatibility

### Success Verification

**Local Build**: Must work with your development setup
```bash
dotnet build YourPlugin.csproj --configuration Release
```

**CI Build**: Should work with Docker method
- Workflow completes without errors
- Produces plugin DLL artifact
- Artifact contains all required files

### Key References

- **TypNull's Tubifarry**: https://github.com/TypNull/Tubifarry (working CI example)
- **TrevTV's Tidal**: https://github.com/TrevTV/Lidarr.Plugin.Tidal (protocol implementation)
- **Qobuzarr Implementation**: https://github.com/RicherTunes/Qobuzarr (combined approach)

### Instructions for AI Assistant

When helping with Lidarr plugin CI/CD:

1. **Always recommend Docker extraction method first** - It's the most reliable
2. **Emphasize minimal NuGet sources** - Don't inherit all Lidarr feeds
3. **Verify Protocol implementation** - Must use `string` with `nameof()`
4. **Reference working examples** - Point to TypNull/TrevTV for verification
5. **Test locally first** - CI won't work if local build doesn't work

### Expected Outcome

Following this approach should result in:
- ✅ Plugin builds successfully in GitHub Actions
- ✅ Artifacts uploaded automatically
- ✅ No private NuGet authentication issues
- ✅ Compatible with Lidarr plugins branch

This methodology has been **proven to work** through analysis of successful implementations and should be the standard approach for all new Lidarr plugins requiring CI/CD automation.