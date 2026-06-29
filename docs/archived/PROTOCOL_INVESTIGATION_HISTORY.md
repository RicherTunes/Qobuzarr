<!-- docval:ignore-workflow-refs -->
# Protocol Investigation History (2025-08)

> **Archived from CLAUDE.md on 2026-03-26.**
> This document preserves the full investigation trail from Aug 2025.
> The current correct guidance lives in CLAUDE.md -- do NOT use this file
> as authoritative build/protocol instructions.

---

## ROOT CAUSE DISCOVERED (2025-08-24)

After analyzing working plugins (TrevTV's Tidal/Qobuz, TypNull's Tubifarry), the issue is **assembly branch compatibility**:

### Branch Interface Differences

**Plugins Branch** (Required for this plugin):
```csharp
// ext/Lidarr-source/src/NzbDrone.Core/Indexers/IndexerBase.cs:25
public abstract string Protocol { get; }

// ext/Lidarr-source/src/NzbDrone.Core/Download/DownloadClientBase.cs:108
public abstract string Protocol { get; }

// ext/Lidarr-source/src/NzbDrone.Core/Indexers/DownloadProtocol.cs:3
public interface IDownloadProtocol { }
```

**Release Branch** (What we were using before):
```csharp
public abstract DownloadProtocol Protocol { get; } // WRONG TYPE!
// IDownloadProtocol interface doesn't exist
```

### Working Plugin Pattern

**ALL working plugins** use this exact pattern:

```csharp
// 1. Empty protocol class implementing IDownloadProtocol
public class QobuzarrDownloadProtocol : IDownloadProtocol { }

// 2. String Protocol property using nameof()
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}

public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}
```

### Assembly Requirements

**MANDATORY: Use Lidarr plugins branch assemblies**:
- The Lidarr "plugins" branch has **different base class signatures** than regular releases
- **IndexerBase.Protocol**: `public abstract string Protocol { get; }` (NOT DownloadProtocol enum)
- **DownloadClientBase.Protocol**: `public abstract string Protocol { get; }` (NOT DownloadProtocol enum)
- **IDownloadProtocol interface EXISTS** in plugins branch (missing in release branch)

**NEVER use regular Lidarr release assemblies** - they expect DownloadProtocol enum and will cause ReflectionTypeLoadException!

#### Lidarr Plugins Branch Source Build (REQUIRED):
```bash
# CRITICAL: Use plugins branch, NOT develop/main branch
git clone --depth 1 --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr-source

# Build with plugins branch source assemblies
./build.sh --deploy
.\build.ps1 -Deploy
```

**NEVER use these (wrong branch)**:
```bash
# WRONG - These are regular release branches with DownloadProtocol enum
git clone --branch develop https://github.com/Lidarr/Lidarr.git  # Wrong branch
./download-lidarr-assemblies.sh --version 2.13.2.4685            # Release assemblies
```

#### Source Builds (USE ONLY WHEN RUNTIME VERSION UNAVAILABLE):
```bash
# ONLY WHEN: Runtime version (e.g., 2.13.3.4692) not available as pre-built
# This approach works but requires careful management
./setup.sh  # Uses source build with version override
# OR manually:
git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
git -C ext/Lidarr-source checkout origin/plugins
```

**When Source Builds Are Required**:
- Runtime Lidarr version (e.g., 2.13.3.4692) not available as pre-built releases
- Solves version mismatch ReflectionTypeLoadException
- Requires version override to match target runtime

---

## Build Issues and Solutions

**Plugin Version Compatibility**: Critical solution from analyzing working plugins (TrevTV, TypNull):

**THE KEY FIX**: Working plugins **override the Lidarr assembly version** during build to match the target Lidarr version.

**CRITICAL DISCOVERY**: From TrevTV's GitHub Actions CI (`.github/workflows/build.yml` line 52):
```yaml
# TrevTV's secret sauce - this is what makes plugins work with hotio pr-plugins
- name: Update Version Info
  run: |
    sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$MINIMUM_LIDARR_VERSION<\/AssemblyVersion>/g" ext/Lidarr/src/Directory.Build.props
```

Where `MINIMUM_LIDARR_VERSION: 2.13.0.4664` (or `2.13.2.4686` for current hotio pr-plugins).

**The Problem Without This Fix**:
- Official Lidarr plugins branch: `AssemblyVersion>10.0.0.*` (development versions)
- Hotio pr-plugins runtime: Expects `Version=2.13.2.4686` (release-based versions)
- Result: `ReflectionTypeLoadException` - "Could not load file or assembly 'Lidarr.Core, Version=10.0.0.17650'"

**How The Override Fixes It**:
- Changes Lidarr source from `<AssemblyVersion>10.0.0.*</AssemblyVersion>`
- To `<AssemblyVersion>2.13.2.4686</AssemblyVersion>`
- Plugin compiles against release version numbers that match hotio runtime
- Perfect compatibility with `ghcr.io/hotio/lidarr:pr-plugins`

**Additional Requirements**:
- **Use exact commit pinning**: Working plugins use specific commit `aa7b63f2e13351f54a31d780d6a7b93a2411eaec` from Lidarr
- **Include ILocalizationService**: DownloadClientBase constructor requires `ILocalizationService` parameter
- **Enable ILRepack**: Use ILRepack with `<ILRepackEnabled>true</ILRepackEnabled>` to merge dependencies
- **Constructor signature**: Must match exact signature in DownloadClientBase from the target Lidarr commit

**Assembly Version Conflicts**: If you see `ReflectionTypeLoadException` with version mismatches:
- **ROOT CAUSE**: Plugin compiled with development versions (10.0.0.x) but runtime expects release versions (2.13.2.x)
- **SOLUTION**: Override `AssemblyVersion` in `ext/Lidarr-source/src/Directory.Build.props` to match target Lidarr version
- **AUTOMATION**: Build scripts automatically apply this fix before compilation
- **VERIFICATION**: Check that compiled plugin assembly version matches your Lidarr runtime version

**Automated Solution**: Both `build.ps1` and `build.sh` now automatically apply TrevTV's proven version override approach before every build.

**Version Override in Build Scripts**:
```bash
# PowerShell (build.ps1)
$lidarrVersionOverride = "2.13.2.4686"
(Get-Content "ext\Lidarr-source\src\Directory.Build.props") -replace '<AssemblyVersion>[\d\.\*]+</AssemblyVersion>', "<AssemblyVersion>$lidarrVersionOverride</AssemblyVersion>" | Set-Content "ext\Lidarr-source\src\Directory.Build.props"

# Bash (build.sh)
LIDARR_VERSION_OVERRIDE="2.13.2.4686"
sed -i "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$LIDARR_VERSION_OVERRIDE<\/AssemblyVersion>/g" ext/Lidarr-source/src/Directory.Build.props
```

---

## GitHub Actions CI/CD -- BREAKTHROUGH (2025-08-24)

Based on analysis of TrevTV's and TypNull's successful plugins, we had a working GitHub Actions build.

### Working Solution: Docker Assembly Extraction

**Workflow**: `.github/workflows/build-docker.yml`

**Key Innovation**: Extract plugins branch assemblies from `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` Docker image instead of building from source.

**Why This Works**:
- No private NuGet feeds - Uses pre-built assemblies from Docker
- No Central Package Management conflicts - Temporary project file with direct references
- Plugins branch compatibility - Assemblies are from actual plugins branch runtime
- Fast builds - No time wasted building entire Lidarr codebase
- Reliable - Based on proven patterns from working plugins

### Analysis of Working Plugins (See `docs/infrastructure/WORKING-PLUGIN-CI-ANALYSIS.md`):

**TrevTV's Approach**:
- Uses ProjectReference to Lidarr source
- Applies version override with `sed` command
- Simple single-workflow approach

**TypNull's Approach**:
- Uses Git submodules for Lidarr source
- Minimal NuGet.config (3 sources vs 7)
- Complex multi-workflow system that actually works

**Our Solution**:
- Combines best of both: Docker extraction (like TypNull's submodule idea) + minimal complexity (like TrevTV's simplicity)

### TrevTV's Proven GitHub Actions Workflow (`.github/workflows/ci.yml`):

**CRITICAL SUCCESS FACTORS** (Learned 2025-08-18 after extensive CI debugging):

1. **Use .NET 8.0** (not 6.0!) - `DOTNET_VERSION: 8.0.x`
2. **Apply version override** - `sed` command to fix assembly versions
3. **Use existing assemblies** - Don't try to build Lidarr source (causes NuGet conflicts)
4. **Simple, reliable approach** - Avoid complex source builds

**Core Configuration**:
```yaml
env:
  DOTNET_VERSION: 8.0.x
  MINIMUM_LIDARR_VERSION: 2.13.0.4664
  PLUGIN_VERSION: 0.0.${{ github.run_number }}

steps:
  # TrevTV's magic assembly fix - THE KEY TO SUCCESS
  - name: Update Version Info
    run: |
      sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>${{ env.MINIMUM_LIDARR_VERSION }}<\/AssemblyVersion>/g" ext/Lidarr/src/Directory.Build.props || echo "No Directory.Build.props found"
```

### CRITICAL: NEVER MIX ASSEMBLY SOURCES:

**What CAUSES FAILURES (NEVER DO THIS)**:
```bash
# DON'T: Mix assembly sources - causes type conflicts
git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
./download-lidarr-assemblies.sh  # CREATES DUAL REFERENCES
# Result: MSB3277 conflicts, CS0246/CS1715 errors

# DON'T: Source builds in CI (NuGet conflicts, hours of debugging)
dotnet build ext/Lidarr-source/src/Lidarr.sln  # PACKAGE MANAGEMENT ERRORS
```

**What WORKS (TrevTV's Method)**:
```bash
# Simple: Use existing assemblies + version override
./download-lidarr-assemblies.sh --version 2.13.2.4685
./build.sh --deploy

# Or PowerShell
.\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685"
.\build.ps1 -Deploy
```

---

## CRITICAL CI/CD LESSONS LEARNED (2025-08-18)

### Failed Approaches That Wasted Hours:

1. **Complex Source Builds**:
   - Tried building entire Lidarr source in CI
   - Result: NuGet package source conflicts, 7 different package sources
   - Hours wasted on NU1507 and NU1008 errors

2. **Pre-built Assembly Downloads**:
   - Downloaded release assemblies from GitHub releases
   - Result: Missing `NzbDrone.Core.Plugins` and `IDownloadProtocol` interfaces
   - Plugin compilation failed with CS0234 and CS0246 errors

3. **Wrong .NET Version**:
   - Used .NET 6.0 in CI but tools required .NET 8.0
   - Result: NETSDK1045 and Microsoft.Sbom.Tool compatibility errors

### TrevTV's Working Solution (COPY THIS EXACTLY):

**Environment Setup**:
```yaml
env:
  DOTNET_VERSION: 8.0.x                    # Use .NET 8.0!
  MINIMUM_LIDARR_VERSION: 2.13.0.4664     # TrevTV's proven version
  PLUGIN_VERSION: 0.0.${{ github.run_number }}
```

**Build Steps**:
```yaml
- name: Update Version Info
  run: |
    # THE MAGIC LINE - This is what makes plugins work with hotio
    sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>${{ env.MINIMUM_LIDARR_VERSION }}<\/AssemblyVersion>/g" ext/Lidarr/src/Directory.Build.props || echo "No Directory.Build.props found"

- name: Build
  run: |
    dotnet build --configuration Release --no-restore \
      -p:RunAnalyzersDuringBuild=false \
      -p:EnableNETAnalyzers=false \
      -p:TreatWarningsAsErrors=false
```

### Standard Operating Procedure:

**Local Development**:
1. `./download-lidarr-assemblies.sh --version 2.13.2.4685` (uses existing assemblies)
2. `./build.sh --deploy` (simple build with TrevTV's flags)

**CI/CD**:
1. Use **TrevTV's exact workflow template** from `Lidarr.Plugin.Tidal`
2. **Never** attempt source builds or complex dependency management
3. **Always** apply the `sed` version override (this is the secret sauce)

**Debugging Method**:
- Use `gh run view --log-failed` to check CI failures
- Use `gh` or `git` to validate build status, not web pages

---

## FINAL BREAKTHROUGH DISCOVERY (2025-08-18)

**CRITICAL**: After investigating Brainarr's working implementation, the root cause is now clear:

### Our Plugin Used Wrong Lidarr APIs:
```csharp
// WRONG - These interfaces don't exist in any Lidarr version:
using NzbDrone.Core.Plugins;           // Doesn't exist
public class QobuzarrPlugin : IPlugin  // Wrong interface
public class QobuzDownloadProtocol : IDownloadProtocol  // Wrong interface
```

### Brainarr's Working Approach:
```csharp
// CORRECT - Uses standard Lidarr interfaces:
using NzbDrone.Core.ImportLists;                          // Standard namespace
public class Brainarr : ImportListBase<BrainarrSettings>  // Standard base class
```

### The Fix:

**Our plugin needs complete refactoring** to use **standard Lidarr interfaces**:
- `ImportListBase<Settings>` for content discovery
- `DownloadClientBase<Settings>` for downloads (if this exists)
- `IndexerBase<Settings>` for search (if this exists)

**The CI automation is perfect** - the problem was **never the CI**, it was **plugin API compatibility**.

---

## CRITICAL: Download Protocol Compatibility Issue

### RECURRING PROBLEM -- REMEMBER THIS SOLUTION

**Issue**: CI builds frequently fail with Protocol property type mismatch errors:
```
'QobuzIndexer.Protocol': type must be 'DownloadProtocol' to match overridden member
'QobuzDownloadClient.Protocol': type must be 'DownloadProtocol' to match overridden member
The type or namespace name 'IDownloadProtocol' could not be found
```

**Root Cause**: We use Qobuzarr streaming protocol, NOT Usenet/Torrent protocols
- Lidarr expects specific protocol handling for Usenet (retention) and Torrent (seeding)
- Qobuzarr is a streaming service requiring different protocol identification

### FINAL WORKING SOLUTION (TESTED 2025-08-23):

**Protocol Implementation** (ALL files):
```csharp
// QobuzIndexer.cs
public override DownloadProtocol Protocol => DownloadProtocol.Unknown;

// QobuzDownloadClient.cs
public override DownloadProtocol Protocol => DownloadProtocol.Unknown;

// QobuzParser.cs
DownloadProtocol = DownloadProtocol.Unknown,

// QobuzDownloadItem.cs
Protocol = DownloadProtocol.Unknown,
```

### Key Points:
- **Use DownloadProtocol.Unknown enum** - standard Lidarr pattern for streaming services
- **No custom protocol classes** - avoid interface compatibility issues
- **Consistent across all files** - same enum value everywhere
- **Single assembly source** - prevents type definition conflicts

### Critical Requirements:
1. **ONLY** use pre-built assemblies from `ext/Lidarr/_output`
2. **NEVER** mix with source-built assemblies from `ext/Lidarr-source`
3. **Protocol = DownloadProtocol.Unknown** for all streaming services

### DEFINITIVE SOLUTION -- Assembly Reference Conflicts

**ROOT CAUSE IDENTIFIED** (2025-08-23):

**The Real Issue**: **Conflicting assembly references** caused type definition conflicts:

- **Problem**: Project referenced BOTH `ext/Lidarr-source/_output` AND `ext/Lidarr/_output` assemblies
- **Conflict**: Same types (DownloadProtocol) defined differently in each assembly source
- **MSBuild confusion**: Couldn't resolve which type definition to use

**DEFINITIVE SOLUTION** (TESTED AND WORKING):
1. **Single Assembly Source**: Use ONLY `ext/Lidarr/_output` (pre-built assemblies)
2. **Remove Conflicting Source**: Delete `ext/Lidarr-source` entirely
3. **Consistent Protocol Type**: `DownloadProtocol.Unknown` enum is correct for both CI and local
4. **No Conditional Compilation**: Clean, single code path

**Why This Works**:
- Eliminates conflicts: Single assembly reference source
- Consistent types: Same DownloadProtocol enum definition everywhere
- No hacks: No conditional compilation or build flags needed
- Standard pattern: `DownloadProtocol.Unknown` is proper for streaming services

### CRITICAL LESSON: AVOID DUAL ASSEMBLY REFERENCES

**NEVER have both**:
- `ext/Lidarr-source/_output/net6.0/Lidarr.Core.dll` (source-built)
- `ext/Lidarr/_output/net6.0/Lidarr.Core.dll` (pre-built)

**MSBuild Error Pattern**:
```
warning MSB3277: Found conflicts between different versions of "TagLibSharp"
references which depend on "ext/Lidarr-source/_output" vs "ext/Lidarr/_output"
```

**ALWAYS**: Use single assembly source consistently across all environments.

---

## DEFINITIVE BUILD SUCCESS FORMULA

**LEARNED 2025-08-23 -- NEVER FORGET THIS**:

### What ALWAYS Works:
1. **Single Assembly Source**: `./download-lidarr-assemblies.sh --version 2.13.2.4685`
2. **Delete Conflicts**: `rm -rf ext/Lidarr-source` (prevents dual references)
3. **Protocol = DownloadProtocol.Unknown**: Use enum consistently for streaming services
4. **Build Command**: Standard flags with analyzer suppression

### What ALWAYS Fails:
1. **Mixed Assembly Sources**: Having both `ext/Lidarr-source` and `ext/Lidarr/_output`
2. **Custom Protocol Classes**: Implementing `IDownloadProtocol` (interface doesn't exist)
3. **Conditional Compilation Hacks**: Environment-specific code paths
4. **Source Builds in CI**: Package management conflicts

### Standard Troubleshooting Checklist:
- [ ] Protocol errors -> Check for dual assembly references
- [ ] CS0246/CS1715 errors -> Delete `ext/Lidarr-source`, use only `ext/Lidarr/_output`
- [ ] MSB3277 TagLibSharp conflicts -> Expected, harmless warnings
- [ ] Build failures -> Use `DownloadProtocol.Unknown` enum consistently

**THE GOLDEN RULE**: **One assembly source, standard enums, no hacks**.

---

## DEFINITIVE PLUGINS BRANCH COMPATIBILITY SOLUTION (2025-08-24)

### The Problem
User reported: `Method 'get_Protocol' in type 'Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexer' does not have an implementation`

### Root Cause Analysis
- **User's runtime**: Lidarr plugins branch v2.13.3.4692 (expects `string Protocol`)
- **Our compilation**: Against release assemblies v2.13.2.4685 (expects `DownloadProtocol Protocol`)
- **Result**: Method signature mismatch -> ReflectionTypeLoadException

### Evidence from Working Plugins

**TrevTV's Tidal Plugin**:
```csharp
public class Tidal : HttpIndexerBase<TidalIndexerSettings>
{
    public override string Protocol => nameof(TidalDownloadProtocol);
}

public class TidalDownloadProtocol : IDownloadProtocol { }
```

**TypNull's Tubifarry Plugin**:
```csharp
public class YoutubeIndexer : HttpIndexerBase<SpotifyIndexerSettings>
{
    public override string Protocol => nameof(YoutubeDownloadProtocol);
}
```

**TrevTV's Original Qobuz Plugin**:
```csharp
public class Qobuz : HttpIndexerBase<QobuzIndexerSettings>
{
    public override string Protocol => nameof(QobuzDownloadProtocol);
}
```

### Our Implementation (CORRECT)

We now use the **exact same pattern**:

```csharp
// src/Download/QobuzarrDownloadProtocol.cs
namespace NzbDrone.Core.Indexers
{
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
        // Empty implementation - just a marker class
    }
}

// src/Indexers/QobuzIndexer.cs
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}

// src/Download/Clients/QobuzDownloadClient.cs
public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}
```

### Assembly Solution

**For plugins branch compatibility, use one of these approaches**:

**Option 1 - Plugins Branch Source** (Like TrevTV):
```bash
git clone --depth 1 --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
# Then reference as ProjectReference in .csproj
```

**Option 2 - Docker Assembly Extraction**:
```bash
# Extract from plugins branch Docker container
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker create --name temp ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker cp temp:/app/bin/. ext/Lidarr-plugins/_output/
docker rm temp
```

### Build Status (as of 2025-08-24)

- **Current Implementation**: Plugin uses correct `string Protocol => nameof(QobuzarrDownloadProtocol)` pattern
- **Assembly Mismatch**: Still compiling against release assemblies (needs plugins branch assemblies)
- **Next Step**: Get plugins branch assemblies and rebuild

### Testing with User's Environment

- **User's Runtime**: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`
- **Plugin Pattern**: Matches TrevTV/TypNull working plugins exactly
- **Expected Result**: Plugin should load successfully once built against plugins branch assemblies

---

## Key Lesson

The **key insight**: Different Lidarr branches have **incompatible base class signatures**. Always match your compilation assemblies to your target runtime branch:

- **Plugins branch runtime** -> **Plugins branch assemblies**
- **Release branch runtime** -> **Release branch assemblies**
