# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Qobuzarr is a high-performance Lidarr plugin for Qobuz streaming service with ML-powered optimization. Built on TrevTV's foundation, it provides both indexing and download capabilities for lossless audio content.

## Build Commands

**IMPORTANT**: Always use the analyzer suppression flags to avoid StyleCop errors from Lidarr source code.

```bash
# Build the plugin (main project) - RECOMMENDED
dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Debug build with analyzer suppression
dotnet build --configuration Debug -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Restore dependencies and build (full setup)
dotnet restore && dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Build specific projects
dotnet build Qobuzarr.csproj --configuration Release                    # Main plugin only
dotnet build QobuzCLI/QobuzCLI.csproj --configuration Release          # CLI tool only
dotnet build tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj               # Unit tests
dotnet build tests/QobuzCLI.Tests/QobuzCLI.Tests.csproj               # CLI tests
```

**Quick Setup (Recommended)**:
```bash
# Windows PowerShell
.\setup.ps1

# Linux/macOS
chmod +x setup.sh && ./setup.sh

# With automatic plugin deployment to test Lidarr instance
.\setup.ps1 -EnableDeploy
./setup.sh --enable-deploy

# Custom deployment path
.\setup.ps1 -EnableDeploy -DeployPath "C:\Custom\Lidarr\Plugins\Qobuzarr"
./setup.sh --enable-deploy --deploy-path "/custom/lidarr/plugins/qobuzarr"
```

**Quick Build Scripts (New)**:
```bash
# Simple build and deploy
.\build.ps1 --Deploy              # PowerShell
./build.sh --deploy               # Bash

# Release build with deployment
.\build.ps1 Release --Deploy      # PowerShell
./build.sh Release --deploy       # Bash

# Clean build
.\build.ps1 --Clean --Restore     # PowerShell
./build.sh --clean --restore      # Bash

# Show all options
.\build.ps1 --Help                # PowerShell
./build.sh --help                 # Bash
```

## Plugin Deployment

The project includes automatic deployment to test Lidarr instances for faster development iteration.

### Automatic Deployment
```bash
# Enable deployment for Debug builds (will auto-copy to X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr)
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Custom deployment path
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:LidarrPluginDeployPath="C:\Custom\Path" -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Using environment variable
set LIDARR_PLUGIN_DEPLOY_PATH=C:\Custom\Path
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false
```

### Manual Deployment
```bash
# Clean previously deployed plugin
dotnet msbuild -target:CleanDeployedPlugin

# Deploy current build
xcopy /Y /E "bin\*" "X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr\"
```

### Deployment Configuration
- **Default Path**: `X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr`
- **Auto-Deploy**: Enabled for Debug builds when `EnablePluginDeployment=true`
- **Files Copied**: Main DLL, PDB symbols, plugin.json, ML patterns file
- **Environment Override**: Set `LIDARR_PLUGIN_DEPLOY_PATH` to customize default path

## Testing Commands

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run integration tests only
dotnet test --filter Category=Integration

# Run specific test project
dotnet test tests/Qobuzarr.Tests/
dotnet test tests/QobuzCLI.Tests/
```

## CLI Usage (Development/Testing)

```bash
# Build and run CLI
cd QobuzCLI
dotnet build -c Release
dotnet run -- auth login

# Search functionality
dotnet run -- search "Miles Davis Kind of Blue"

# Download operations
dotnet run -- download album <album_id> --output ./Music
dotnet run -- download playlist <playlist_id> --output ./Playlists
```

## Architecture

### Plugin-First Design Philosophy
- **Core principle**: All functionality MUST be implemented in the plugin (`src/`) first
- **CLI role**: The `QobuzCLI/` project is strictly a test wrapper and adapter layer
- **No duplication**: CLI never reimplements plugin functionality, only adapts interfaces
- **Dependency flow**: CLI → Plugin (never the reverse)

### Project Structure
```
src/                           # Main plugin (Lidarr.Plugin.Qobuzarr.dll)
├── API/                       # Qobuz API clients and interfaces
├── Authentication/            # Authentication services and session management
├── Download/                  # Download client implementation and orchestration
│   ├── Clients/               # QobuzDownloadClient (implements IDownloadClient)
│   ├── Services/              # Download-related services
│   └── Orchestration/         # Download workflow coordination
├── Indexers/                  # QobuzIndexer (implements IIndexer) with ML optimization
├── Models/                    # Data models for Qobuz API and Lidarr integration
├── Services/                  # Core business logic services
└── Integration/               # Lidarr integration adapters

QobuzCLI/                      # Test CLI wrapper
├── Commands/                  # CLI command implementations
├── Services/Adapters/         # Adapters between CLI and plugin interfaces
└── Program.cs                 # Entry point

ext/Lidarr-source/             # External Lidarr dependencies (git-ignored)
```

### Key Components
- **QobuzIndexer** (`src/Indexers/QobuzIndexer.cs`): Implements `HttpIndexerBase<QobuzIndexerSettings>` for Lidarr search integration
- **QobuzDownloadClient** (`src/Download/Clients/QobuzDownloadClient.cs`): Implements `DownloadClientBase<QobuzDownloadSettings>` for Lidarr download integration
- **QobuzarrPlugin** (`src/QobuzzarrPlugin.cs`): Main plugin entry point that Lidarr discovers
- **Authentication Services** (`src/Authentication/`): Handle Qobuz session management
- **ML Optimization** (`src/Indexers/CompiledMLQueryOptimizer.cs`): Pre-compiled ML models for query optimization

### Dependency Setup

The project supports two approaches for Lidarr dependencies:

#### Option 1: Pre-built Assemblies (Recommended for CI/GitHub)
```bash
# Download pre-built Lidarr assemblies - much faster and more reliable
./download-lidarr-assemblies.sh --version 2.13.2.4685
.\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685"

# Build with pre-built assemblies  
./build.sh --deploy --use-prebuilt
.\build.ps1 -Deploy -UsePrebuiltAssemblies
```

#### Option 2: Build from Source (Legacy method)
```bash
# Required for compilation (done automatically by setup scripts)
# IMPORTANT: Must use exact commit that working plugins use, not branch head
git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
git -C ext/Lidarr-source checkout aa7b63f2e13351f54a31d780d6a7b93a2411eaec
```

### Build Issues and Solutions

**StyleCop Analyzer Errors**: The Lidarr source code may trigger StyleCop analyzer errors. These are suppressed in the project configuration, but if you encounter them:
- Always use the build flags: `-p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false`
- The `Directory.Build.props` and `ext/.editorconfig` files are configured to suppress these issues
- If issues persist, delete and re-clone the Lidarr source using the setup scripts

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

**Never Again**: This issue is now completely prevented through automation.

## GitHub Actions CI/CD

The project uses **TrevTV's proven CI/CD methodology** that powers successful Lidarr plugins like Tidal, Deezer, and Qobuz:

### **TrevTV's Proven GitHub Actions Workflow** (`.github/workflows/ci.yml`):

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

**Why This Works**:
- ✅ **Battle-tested**: Powers TrevTV's Tidal, Deezer, Qobuz plugins successfully
- ✅ **Simple**: No complex source builds or dependency management
- ✅ **Reliable**: Consistent results across all environments
- ✅ **Fast**: No time wasted building entire Lidarr codebase

### **NEVER DO SOURCE BUILDS AGAIN**:

**❌ What DOESN'T Work (Avoid These Approaches)**:
```bash
# DON'T: Complex source builds (NuGet conflicts, hours of debugging)
git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
dotnet build ext/Lidarr-source/src/Lidarr.sln  # ❌ FAILS with package management errors

# DON'T: Pre-built assembly downloads (missing plugin interfaces)  
curl -L "https://github.com/Lidarr/Lidarr/releases/..." # ❌ MISSING NzbDrone.Core.Plugins
```

**✅ What WORKS (TrevTV's Method)**:
```bash
# Simple: Use existing assemblies + version override
./download-lidarr-assemblies.sh --version 2.13.2.4685
./build.sh --deploy

# Or PowerShell
.\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685"  
.\build.ps1 -Deploy
```

**LESSON LEARNED (2025-08-18)**: After hours of debugging CI failures, the solution was to **copy TrevTV's simple approach exactly**, not reinvent complex automation.

### **CI/CD Scripts**:
- **`download-lidarr-assemblies.sh`** / **`download-lidarr-assemblies.ps1`**: Download pre-built Lidarr assemblies
- **`.github/workflows/ci.yml`**: Complete CI/CD pipeline with multi-platform builds
- **Security scanning**, **automated testing**, and **plugin packaging** included

## Development Practices

### Security Requirements
- **No hardcoded credentials**: All credentials must use environment variables or secure storage
- **No stub/placeholder data**: Production code paths must connect to real APIs
- **Fail-fast principle**: If real APIs unavailable, fail immediately with clear errors
- **Input validation**: All user inputs must be validated and sanitized

### Code Organization
- **Plugin-first**: Always implement features in `src/` before creating CLI wrappers
- **Interface segregation**: Use dependency injection with clear interface boundaries
- **Error handling**: Use specific exception types (`QobuzApiException`, `QobuzAuthenticationException`)
- **Async patterns**: All I/O operations must be async/await

### File Naming Conventions
- **Plugin files**: Follow Lidarr namespace conventions (`Lidarr.Plugin.Qobuzarr.*`)
- **Interfaces**: Prefix with `I` (e.g., `IQobuzApiClient`)
- **Services**: Suffix with `Service` (e.g., `QobuzAuthenticationService`)
- **Models**: Simple names matching Qobuz API structure (e.g., `QobuzAlbum`, `QobuzTrack`)

## Configuration

### Environment Variables (for development/testing)
```bash
QOBUZ_APP_ID="your_app_id"
QOBUZ_APP_SECRET="your_app_secret"
QOBUZ_EMAIL="your@email.com"        # Optional
QOBUZ_PASSWORD="your_password"      # Optional
QOBUZ_QUALITY="27"                  # 5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max
```

### Plugin Configuration
- Configured through Lidarr UI: Settings → Indexers → Add → Qobuzarr
- Settings handled by `QobuzIndexerSettings` and `QobuzDownloadSettings`
- Authentication managed by `QobuzAuthenticationService`

## ML Features

The project includes pre-compiled ML optimization:
- **Query optimization**: `src/Indexers/CompiledMLQueryOptimizer.cs`
- **Pattern learning**: `src/Indexers/ml-baseline-patterns.json`
- **No runtime ML.NET**: Uses pre-trained models to avoid ML.NET dependency in production

## Common Issues

### Build Issues
- If "Skipping project... because it was not found" appears: Run setup script to clone Lidarr dependencies
- Missing Lidarr assemblies: Ensure `ext/Lidarr-source/` exists and contains Lidarr source code
- Analyzer warnings: Use `Directory.Build.props` settings to suppress non-critical warnings

### Plugin Development
- Plugin discovery: Lidarr automatically discovers classes implementing `IIndexer` and `IDownloadClient`
- DI registration: Services implementing interfaces are auto-registered by Lidarr's DryIoC container
- Testing: Use CLI project to test plugin functionality without full Lidarr installation

## Version Management

- Version is managed in single source of truth: `Qobuzarr.csproj`
- `plugin.json` is auto-generated from `plugin.json.template` during build
- Assembly version format must match Lidarr requirements (x.x.x.x format)

## Development Quality Tools

### Pre-commit Hooks

The project includes automated pre-commit hooks that run essential checks before each commit:

**Setup**: Pre-commit hooks are automatically available in `.git/hooks/pre-commit` (executable)

**Checks performed**:
- **Build artifact prevention**: Blocks `.dll`, `.pdb`, `.exe`, `bin/`, `obj/` files from being committed
- **Secret detection**: Scans for hardcoded credentials in code files
- **Code quality validation**: Basic syntax checks, TODO/FIXME detection, Console.WriteLine warnings  
- **JSON validation**: Validates `plugin.json` if modified
- **Package management**: Alerts about Directory.Packages.props changes

**Usage**:
```bash
# Hooks run automatically on commit
git commit -m "your changes"

# Manual testing
.git/hooks/pre-commit

# Skip hooks (emergency only)
git commit --no-verify -m "emergency fix"
```

**Benefits**:
- Prevents common mistakes (build artifacts, secrets)
- Catches issues locally before CI/CD
- Maintains repository cleanliness
- Enforces coding standards

### Centralized Package Management

The project uses [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for consistent dependency versions across all projects.

**Configuration**: `Directory.Packages.props` manages all package versions centrally

**Structure**:
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- All package versions defined here -->
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**Migration Tools**:
```bash
# Preview changes (safe)
./migrate-to-central-packages.sh --dry-run
.\migrate-to-central-packages.ps1 -DryRun

# Apply migration
./migrate-to-central-packages.sh
.\migrate-to-central-packages.ps1
```

**Benefits**:
- **Single source of truth**: All package versions in one file
- **Version consistency**: Prevents conflicts across projects  
- **Easier security updates**: Update vulnerable packages once
- **Cleaner project files**: No version attributes in .csproj files
- **Faster builds**: No version conflict resolution needed

**Migration**: Existing projects automatically migrated. Scripts are idempotent (safe to run multiple times).

## Troubleshooting

### ReflectionTypeLoadException - Version Mismatch

**Symptoms**: Lidarr fails to start with "Could not load file or assembly 'Lidarr.Core, Version=10.0.0.xxxxx'"

**Root Cause**: Plugin compiled against development Lidarr versions but runtime expects release versions

**Solution**: 
1. Ensure using correct Lidarr source commit: `aa7b63f2e13351f54a31d780d6a7b93a2411eaec`
2. Build scripts automatically override assembly version to `2.13.2.4686` 
3. Verify `ext/Lidarr-source/src/Directory.Build.props` shows `<AssemblyVersion>2.13.2.4686</AssemblyVersion>`

**Prevention**: Always use `./build.sh --deploy` or `.\build.ps1 -Deploy` which include automatic version override

### Plugin Not Loading

**Check**: Verify plugin files in Lidarr plugins directory:
- `Lidarr.Plugin.Qobuzarr.dll` - Main assembly
- `plugin.json` - Plugin manifest  
- Both should have recent timestamps matching your last build

**Restart**: Always restart Lidarr after plugin deployment

### Assembly Version Debugging

**Check Runtime Version**: Your Lidarr logs should show `Version 2.13.2.4686`
**Check Plugin Version**: Build output should compile against matching `AssemblyVersion>2.13.2.4686`
**Verify Match**: Runtime version and plugin assembly version must exactly match

## CRITICAL CI/CD LESSONS LEARNED (2025-08-18)

**NEVER REPEAT THESE MISTAKES**:

### ❌ **Failed Approaches That Wasted Hours**:

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

### ✅ **TrevTV's Working Solution (COPY THIS EXACTLY)**:

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

### 🎯 **Standard Operating Procedure**:

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

## FINAL BREAKTHROUGH DISCOVERY (2025-08-18)

**CRITICAL**: After investigating Brainarr's working implementation, the root cause is now clear:

### ✅ **Current Plugin Architecture (Correct)**:

**Our plugin already uses the CORRECT Lidarr interfaces**:
```csharp
// ✅ CORRECT - These are the proper Lidarr plugin interfaces:
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>        // For search/indexing
public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>  // For downloads
```

**Key Components Using Standard Lidarr APIs**:
- **QobuzIndexer**: Extends `HttpIndexerBase<QobuzIndexerSettings>` - standard for search indexers
- **QobuzDownloadClient**: Extends `DownloadClientBase<QobuzDownloadSettings>` - standard for download clients  
- **Plugin Discovery**: Lidarr automatically discovers these classes via interface inheritance
- **No IPlugin interface needed**: Lidarr finds plugins through base class inheritance

### 🎯 **Architecture Validation**:

**The plugin architecture is ALREADY CORRECT** - we use standard Lidarr base classes:
- `HttpIndexerBase<T>` for content search and discovery
- `DownloadClientBase<T>` for handling downloads
- Automatic DI registration through Lidarr's DryIoC container
- Standard plugin lifecycle management through base class methods