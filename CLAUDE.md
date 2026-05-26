# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Qobuzarr is a high-performance Lidarr plugin for Qobuz streaming service with ML-powered optimization. Built on TrevTV's foundation, it provides both indexing and download capabilities for lossless audio content.

## Runtime & Docker Image Requirements (CRITICAL)

**Target framework**: `net8.0` -- this plugin MUST target .NET 8.

**Lidarr Docker image**: Use ONLY a `.NET 8` plugins-branch image for CI and local testing. The correct tag format is `pr-plugins-3.x.y.z` (net8). Example:
```
LIDARR_DOCKER_VERSION=pr-plugins-3.1.2.4913
```
- Image: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`

**NEVER use `pr-plugins-2.x` tags** (e.g., `pr-plugins-2.14.2.4786`, `pr-plugins-2.13.3.4692`) -- those are .NET 6 images. Loading a .NET 8 plugin into a .NET 6 host causes `System.Runtime` assembly load failures and Lidarr crash-loops (`Could not load file or assembly 'System.Runtime, Version=8.0.0.0'`).

When bumping the Docker image tag, search the entire repo for the old tag string and update all hits (workflows, scripts, docs).

## Plugin Registration (CRITICAL -- controls Lidarr System->Plugins UI visibility)

Lidarr has **two** distinct `IPlugin` interfaces, and conflating them silently breaks the System->Plugins UI:

| Interface | From | Used by |
|---|---|---|
| `NzbDrone.Core.Plugins.IPlugin` | `Lidarr.Core.dll` (host) | `/api/v1/system/plugins` -- UI listing, update checks, uninstall |
| `Lidarr.Plugin.Abstractions.IPlugin` | Common (internalized via ILRepack) | TestKit `PluginSandbox` -- never read by the live host |

`QobuzarrStreamingPlugin : StreamingPlugin<TModule,TSettings>` satisfies Common's contract. `QobuzIndexer`/`QobuzDownloadClient` are discovered through their Lidarr base classes. Neither satisfies the host's `IPlugin`, so without an additional class the plugin loads fully and works but doesn't appear in System->Plugins (and can't be auto-updated/uninstalled through the UI).

`src/Integration/QobuzarrInstalledPlugin.cs` extends the host's `NzbDrone.Core.Plugins.Plugin` to close the gap:

```csharp
public sealed class QobuzarrInstalledPlugin : NzbDrone.Core.Plugins.Plugin
{
    public override string Name => "Qobuzarr";
    public override string Owner => "RicherTunes";
    public override string GithubUrl => "https://github.com/RicherTunes/Qobuzarr";
}
```

DryIoc's `RegisterMany` (in `NzbDrone.Common.Composition.Extensions.AutoAddServices`) auto-discovers this class from the loaded plugin assembly. `InstalledVersion` is derived from `AssemblyInformationalVersionAttribute` via the base class -- do **not** hardcode it.

The class uses the fully-qualified base type `NzbDrone.Core.Plugins.Plugin` (no `using NzbDrone.Core.Plugins;`) because Qobuzarr's namespace `Lidarr.Plugin.Qobuzarr.Integration` ambiguously imports `Lidarr.Plugin.*` namespaces, making the unqualified `Plugin` lookup resolve to a namespace instead of the type.

## Release Asset Naming (CRITICAL -- controls Lidarr UI install)

**Every release asset filename MUST contain the literal substring `net8.0.zip`.**

Lidarr's plugin install (UI "Install" on a GitHub URL) is implemented in `src/NzbDrone.Core/Plugins/PluginService.cs` on the `plugins` branch. The asset filter is:

```csharp
release.Assets.Any(a => a.Name.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase))
// where Framework = $"net{_platformInfo.Version.Major}.0"  ->  "net8.0"
```

If no asset matches, `GetRemotePlugin` returns `null` and `InstallPluginService.Execute` silently no-ops -- **the UI spinner spins forever with no error**. This is the failure mode users see as "Install button does nothing."

Other constraints the install enforces:

- `draft: false`
- `target_commitish` in `{main, master}` (case-insensitive)
- Tag parses as a version (`v1.2.3`, `1.2.3`, or `1.2.3-prerelease`)
- Optional `Minimum Lidarr Version: X.Y.Z.W` in release body must be <= host version

Our release zip is named `Lidarr.Plugin.Qobuzarr-v<VERSION>.net8.0.zip` (`.github/workflows/release.yml`). Do not rename without keeping the `net8.0.zip` suffix.

**Verify a release is installable:**

```bash
gh api repos/RicherTunes/qobuzarr/releases --jq '.[0] | {tag_name, draft, target_commitish, assets: [.assets[].name]}'
```

At least one asset name must contain `net8.0.zip`.

**ALWAYS**:
- Use constants from `QobuzarrConstants.cs` rather than hardcoding.
- Expose to the user what brings value in `QobuzSettings.cs`; otherwise, it should be in `QobuzarrConstants.cs`.
- Be aware that this project shares a common library with http://github.com/RicherTunes/Lidarr.Plugin.Common so always think of ways to ensure generic code can be shared with this library so other projects may benefits. Think architecturally when doing so.

## Plugin DLL Naming Contract (CRITICAL)

**The main plugin DLL filename MUST match the glob `Lidarr.Plugin.*.dll`.** Lidarr's PluginLoader (`NzbDrone.Common/Extensions/PathExtensions.cs:334`) scans `/config/plugins/{owner}/{name}/` with `Directory.GetFiles(folder, "Lidarr.Plugin.*.dll")` — any other filename is silently ignored. No error, no warning, no log line; the plugin just never appears in `/api/v1/system/plugins`.

For Qobuzarr this is satisfied by `<AssemblyName>Lidarr.Plugin.Qobuzarr</AssemblyName>` in `Qobuzarr.csproj`. Don't drop that line "to clean up" — it's load-bearing.

## Submodule pin coordination (ext-common-sha.txt)

`ext/Lidarr.Plugin.Common` is a git submodule pinned to a specific Common SHA. Two things must always agree on that SHA:

1. **The submodule gitlink** — what `git ls-tree HEAD ext/Lidarr.Plugin.Common` reports (updated by `git add ext/Lidarr.Plugin.Common` after checking out a new Common commit).
2. **`ext-common-sha.txt`** — a plaintext sentinel (40 hex chars + LF) at the repo root. CI's "Submodule Pinning" job (`.github/workflows/submodule-pin.yml`) fails the build if the gitlink and this file disagree.

**Why the sentinel exists**: the gitlink is invisible in a plain `git diff` (it shows only `-Subproject commit <sha>`), so the sentinel makes the pinned version greppable, reviewable in PRs, and assertable in tests (`VersionContractTests` cross-checks it against `plugin.json`'s `commonVersion`). Seeing `ext-common-sha.txt` dirtied in `git status` after a submodule bump is expected — commit it together with the gitlink.

**To bump the pin**: `pwsh ext/Lidarr.Plugin.Common/scripts/repin-common-submodule.sh --sha-from-submodule --stage` (or the `.ps1` variant) reads the submodule HEAD, rewrites `ext-common-sha.txt`, and stages both so they can't drift. The nightly `bump-common.yml` workflow does this automatically when Common's main advances.

## Common helpers in use

- `PluginConfigRoots.Resolve("Qobuzarr")` — `src/Authentication/SessionManager.cs:263`
- `FileTokenStore<QobuzSession>` + `StreamingTokenManager<QobuzSession, QobuzCredentials>` — `src/Authentication/SessionManager.cs:86-90`. Common's canonical token-store stack with at-rest encryption (DPAPI on Windows, Keychain on macOS, Secret Service / DataProtection fallback on Linux). Session envelope persisted to `PluginConfigRoots.Resolve("Qobuzarr")/session.json`. The audit-mismatch axis "Qobuz uses custom JSON I/O for sessions" was a stale finding — the wave-8B `SecureSessionManager` rip-out already migrated to Common; this CLAUDE entry pins the evidence.
- `BackendHealthCache` — `src/API/Http/QobuzHttpClient.cs:31` (fail-fast gate in `ExecuteAsync`), `src/API/Http/QobuzHttpClient.cs:104`
- `AuthFailureGate` — `src/Integration/QobuzarrStreamingPlugin.cs:36` (singleton registration), `src/Integration/Bridge/BridgeQobuzApiClient.cs:35`
- `HttpExceptionClassifier` — `src/API/AdaptiveQobuzApiClient.cs:54`, `src/Services/AuthTokenManager.cs:376`
- `PluginLogContext` — `src/Indexers/QobuzIndexer.cs:189` (Search scope), `src/Indexers/QobuzIndexer.cs:265` (Test scope), `src/Services/AuthTokenManager.cs:266` (AuthRefresh scope)
- `WarnOnce` — `src/Indexers/QobuzIndexer.cs:58` (wire-warn gate)
- `Scrub` — `src/Download/Services/AudioFileDownloader.cs:73` (`Scrub.Url`), `src/API/Signing/QobuzRequestSigner.cs:64` (`Scrub.Secret`)
- `PrefixedReleaseGuidParser` — `src/Indexers/QobuzParser.cs:233`
- `BoundedConcurrentDictionary<TKey, TValue>` — available (Common v1.15.0+ exposes `ContainsKey`, `Values`, indexer setter, and `IEnumerable<KeyValuePair>` alongside the original v1.10.0 TryAdd/TryGetValue/AddOrUpdate/GetOrAdd surface). No qobuz call sites yet — `QobuzHttpClient._hostGates` (`src/API/Http/QobuzHttpClient.cs:40`) is domain-bounded by host count (1-2 hosts in practice) so adoption isn't required; revisit if user-controlled keys grow unboundedly.

See `ext/Lidarr.Plugin.Common/CHANGELOG.md` for the full catalog and [`docs/ECOSYSTEM_PARITY_MATRIX.md`](ext/Lidarr.Plugin.Common/docs/ECOSYSTEM_PARITY_MATRIX.md) for the cross-plugin parity scorecard (30+ axes × 4 plugins).

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

- **NEVER** run git clean ... NEVER!!

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
- **Dependency flow**: CLI -> Plugin (never the reverse)

### Project Structure
```
src/                           # Main plugin (Lidarr.Plugin.Qobuzarr.dll)
+-- API/                       # Qobuz API clients and interfaces
+-- Authentication/            # Authentication services and session management
+-- Download/                  # Download client implementation and orchestration
|   +-- Clients/               # QobuzDownloadClient (implements IDownloadClient)
|   +-- Services/              # Download-related services
|   +-- Orchestration/         # Download workflow coordination
+-- Indexers/                  # QobuzIndexer (implements IIndexer) with ML optimization
+-- Models/                    # Data models for Qobuz API and Lidarr integration
+-- Services/                  # Core business logic services
+-- Integration/               # Lidarr integration adapters

QobuzCLI/                      # Test CLI wrapper
+-- Commands/                  # CLI command implementations
+-- Services/Adapters/         # Adapters between CLI and plugin interfaces
+-- Program.cs                 # Entry point

ext/Lidarr/_output/            # Pre-built Lidarr assemblies (ONLY supported method)
```

### Key Components
- **QobuzIndexer** (`src/Indexers/QobuzIndexer.cs`): Implements `HttpIndexerBase<QobuzIndexerSettings>` for Lidarr search integration
- **QobuzDownloadClient** (`src/Download/Clients/QobuzDownloadClient.cs`): Implements `DownloadClientBase<QobuzDownloadSettings>` for Lidarr download integration
- **Plugin Metadata** (`src/Constants/QobuzarrConstants.cs`): Centralized plugin information and constants
- **Authentication Services** (`src/Authentication/`): Handle Qobuz session management
- **ML Optimization** (`src/Indexers/CompiledMLQueryOptimizer.cs`): Pre-compiled ML models for query optimization

## Plugins Branch Protocol Pattern (CORRECT)

The Lidarr plugins branch uses `string Protocol` (not `DownloadProtocol` enum). All working plugins follow this pattern:

```csharp
// src/Download/QobuzarrDownloadProtocol.cs
namespace NzbDrone.Core.Indexers
{
    public class QobuzarrDownloadProtocol : IDownloadProtocol { }
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

**Key rules**:
- The plugins branch base classes declare `public abstract string Protocol { get; }` (NOT `DownloadProtocol` enum)
- `IDownloadProtocol` interface exists only in the plugins branch
- Always compile against **plugins branch assemblies** -- release assemblies have incompatible signatures
- See `docs/archived/PROTOCOL_INVESTIGATION_HISTORY.md` for the full 2025-08 investigation trail

### Assembly Sources

**Docker extraction** (preferred for CI):
```bash
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker create --name temp ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker cp temp:/app/bin/. ext/Lidarr-plugins/_output/
docker rm temp
```

**Plugins branch source** (alternative):
```bash
git clone --depth 1 --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
```

**Assembly version override** (required when building from source): Build scripts automatically patch `AssemblyVersion` in `ext/Lidarr-source/src/Directory.Build.props` to match the target runtime version, preventing `ReflectionTypeLoadException`.

### Build Issues

**StyleCop Analyzer Errors**: The Lidarr source code may trigger StyleCop analyzer errors. These are suppressed in the project configuration, but if you encounter them:
- Always use the build flags: `-p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false`
- The `Directory.Build.props` and `ext/.editorconfig` files are configured to suppress these issues
- If issues persist, delete and re-clone the Lidarr source using the setup scripts

## GitHub Actions CI/CD

**Workflow**: `.github/workflows/build-docker.yml`

**Approach**: Extract plugins branch assemblies from `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` Docker image. This avoids NuGet feed issues and Central Package Management conflicts.

**CI/CD Scripts**:
- **`download-lidarr-assemblies.sh`** / **`download-lidarr-assemblies.ps1`**: Download pre-built Lidarr assemblies
- **`.github/workflows/ci.yml`**: Complete CI/CD pipeline with multi-platform builds
- Security scanning, automated testing, and plugin packaging included

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
- Configured through Lidarr UI: Settings -> Indexers -> Add -> Qobuzarr
- Settings handled by `QobuzIndexerSettings` and `QobuzDownloadSettings`
- Authentication managed by `QobuzAuthenticationService`

## ML Features

The project includes pre-compiled ML optimization:
- **Query optimization**: `src/Indexers/CompiledMLQueryOptimizer.cs`
- **Pattern learning**: `src/Indexers/ml-baseline-patterns.json`
- **No runtime ML.NET**: Uses pre-trained models to avoid ML.NET dependency in production

## Common Issues

### Assembly Reference Conflicts (MOST COMMON ISSUE)
**Symptoms**:
```
error CS0246: The type or namespace name 'DownloadProtocol' could not be found
error CS1715: type must be 'DownloadProtocol' to match overridden member
```

**Root Cause**: Compiling against release assemblies instead of plugins branch assemblies, or having dual assembly sources.

**Solution**:
1. Use only ONE assembly source (plugins branch assemblies from Docker extraction or source build)
2. Never have both `ext/Lidarr-source/_output` and `ext/Lidarr/_output` -- pick one
3. Protocol must be `string Protocol => nameof(QobuzarrDownloadProtocol)` (plugins branch pattern)

### Other Build Issues
- If "Skipping project... because it was not found": Run `./download-lidarr-assemblies.sh`
- Missing Lidarr assemblies: Ensure `ext/Lidarr/_output/` exists with pre-built DLLs
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

### Centralized Package Management

The project uses [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for consistent dependency versions across all projects.

**Configuration**: `Directory.Packages.props` manages all package versions centrally

**Migration Tools**:
```bash
# Preview changes (safe)
./migrate-to-central-packages.sh --dry-run
.\migrate-to-central-packages.ps1 -DryRun

# Apply migration
./migrate-to-central-packages.sh
.\migrate-to-central-packages.ps1
```

## Troubleshooting

### ReflectionTypeLoadException -- Version Mismatch

**Symptoms**: Lidarr fails to start with "Could not load file or assembly 'Lidarr.Core, Version=10.0.0.xxxxx'"

**Root Cause**: Plugin compiled against development Lidarr versions but runtime expects release versions.

**Solution**: Build scripts automatically override `AssemblyVersion` in `Directory.Build.props` to match the target Lidarr runtime version. If building manually, patch the version before compiling.

### Plugin Not Loading

**Check**: Verify plugin files in Lidarr plugins directory:
- `Lidarr.Plugin.Qobuzarr.dll` - Main assembly
- `plugin.json` - Plugin manifest
- Both should have recent timestamps matching your last build

**Restart**: Always restart Lidarr after plugin deployment

## Local Verification (Billing-Blocked CI)

When GitHub Actions billing is blocked, run the merge-critical verification pipeline locally:

```bash
pwsh scripts/verify-local.ps1                    # Full pipeline (extract + build + package + closure + E2E)
pwsh scripts/verify-local.ps1 -SkipExtract       # Fast rerun (reuse cached host assemblies)
pwsh scripts/verify-local.ps1 -SkipTests         # Build + packaging closure only
pwsh scripts/verify-local.ps1 -NoRestore         # Skip dotnet restore (fast iteration)
pwsh scripts/verify-local.ps1 -IncludeSmoke      # + Docker smoke test (mounts plugin in Lidarr)
```

**Prerequisites**: PowerShell 7+ (`pwsh`), .NET 8 SDK, Docker (for extract/smoke stages).

The script delegates to `ext/Lidarr.Plugin.Common/scripts/local-ci.ps1`, which orchestrates the same gates as CI: host assembly extraction with .NET 8 + FV 9.5.4 guardrails, plugin packaging via `New-PluginPackage`, and packaging closure validation via `generate-expected-contents.ps1 -Check`.

## Docker E2E Harness (wave 22b)

A runnable end-to-end harness boots a real Lidarr container, mounts the merged
Qobuzarr plugin DLL, waits for the API, and asserts plugin liveness against the
Lidarr REST API. Built on common's lifted `LidarrContainerFixture` (wave 22a) —
this plugin contributes only ~80 lines of per-plugin glue.

### Run locally

```powershell
# One-shot (builds plugin via verify-local.ps1, then runs the smoke matrix)
pwsh scripts/e2e.ps1

# Re-run without rebuilding (merged DLL already at bin/)
pwsh scripts/e2e.ps1 -SkipBuild

# Run a single test
pwsh scripts/e2e.ps1 -Filter 'FullyQualifiedName~Indexer_Test'
```

If Docker Desktop isn't running the tests **skip gracefully**.

### Pinned image and per-plugin constants

| Knob | Value |
|------|-------|
| Image | `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` |
| Container name | `qobuzarr-e2e` |
| Host port | `8692` (avoids tidalarr `8690` / applemusicarr `8691`) |
| Mount path | `/config/plugins/RicherTunes/Qobuzarr` |
| Mounted DLL | `bin/Lidarr.Plugin.Qobuzarr.dll` (ILRepacked merged output) |
| Schema match substring | `"Qobuz"` |

### ILRepack interaction (critical)

The harness mounts the **merged** plugin DLL (where `Lidarr.Plugin.Common`
types are internalized) — that's what Lidarr loads in production. The
test project itself keeps `PluginPackagingDisable=true;OutputPath=bin-tests/`
on its `<ProjectReference>` (Phase 6 fix) so test compilation resolves against
the standalone Common assembly. The fixture's `FindPluginDll` deliberately
points at `bin/`, **not** `bin-tests/`. Don't conflate the two: tests need the
un-merged DLL for type identity; Lidarr needs the merged DLL.

Per-plugin glue lives in `tests/Qobuzarr.Tests/Runtime/`:

- `QobuzarrLidarrContainerFixture.cs` — subclasses common's fixture and
  populates `LidarrContainerOptions`; defines `[CollectionDefinition]`.
- `DockerE2ETests.cs` — four `[SkippableFact]`s delegating to the
  smoke-assertion extension methods on the fixture
  (`AssertPluginAppearsInIndexerSchemaAsync`,
  `AssertPluginAppearsInDownloadClientSchemaAsync`,
  `AssertIndexerTestReturnsSensibleFailureAsync`,
  `AssertDownloadClientTestReturnsSensibleFailureAsync`).

## Flaky Tests Policy

**Flaky tests are priority tech debt that must be paid immediately.** A test that passes sometimes and fails sometimes erodes trust in the entire test suite. When a flaky test is discovered:

1. **Fix it before starting new feature work** -- flaky tests block reliable CI
2. **Document the root cause** in a commit message so the pattern is not repeated
3. **Never skip or disable** a flaky test without a tracking issue

### Known Flaky Tests (Qobuzarr)

_None outstanding. The previous entries below were resolved either by my changes
or by ambient refactors during the May 2026 wave-17 / wave-18 work. Verified green
across 3+ consecutive `dotnet test --no-build` iterations on 2026-05-25._

### Resolved Flakes (May 2026 verification)

| Test | Root Cause | Resolution |
|------|-----------|-----------|
| 6x `LidarrDecisionEngineTests.*` | NSubstitute mocking non-virtual members | Resolved during refactor — currently passes 3/3 stress iterations |
| `AlbumEditionLidarrIntegrationTests.ParsedAlbumInfo_WithUnicodeVersions_*` | Unicode/diacritical stripping logic | Resolved during refactor — currently passes 3/3 stress iterations |
| `AlbumEditionLidarrIntegrationTests.AlbumRepository_FindByTitle_WithDifferentEditions_*` | GUID collision for editions | Resolved during refactor — currently passes 3/3 stress iterations |
| `PluginPackagingTests.PluginFluentValidationReference_ShouldMatch_HostVersion` | FluentValidation version 9 vs 11 mismatch | Resolved during refactor — currently passes |
| `MLOptimizationRegressionTests.ConcurrentPredictions_MaintainPerformance` | Latency threshold 20ms too tight for CI | Threshold is now `TARGET_PREDICTION_TIME_MS * 10` = 100ms; currently passes |
| `QobuzAuthenticationServiceCovTests.ClearAuthenticationCache_ClearsStoredSession` <br> `QobuzAuthenticationServiceTests.GetCachedSession_WithExpiredSession_ShouldReturnNull` | Two test classes shared `_persistentStore`'s default file path | Fixed May 2026 in `ef73d9f` by adding `internal` constructor overload with `sessionFilePath` parameter; each test instance generates a `Path.GetTempPath()/qobuzarr-test-{Guid}.session.json` and deletes it in `Dispose()`. `[Collection("QobuzAuthentication")]` retained. 5 stress iterations green post-fix. |

### Resolved Bugs (Wave 1 + Wave 2, Mar 26 2026)

| Bug | Root Cause | Resolution |
|-----|-----------|------------|
| `QobuzAlbum.GetGenre()` null-ref | `GenresList` can be null after JSON deserialization; `.FirstOrDefault()` threw NRE | Wave 1: guarded with `GenresList?.FirstOrDefault() ?? "Unknown"` (PR #243) |
| `QobuzAlbum.GetAllArtistNames()` null-ref | `Artists` collection or individual elements null from JSON | Wave 2: null guards on collection and elements (PR #244) |
| `QobuzTrack.GetFullTitle()` null-ref | `Title` null; `.Contains()` threw NRE | Wave 2: fallback to "Unknown Track" (PR #244) |
| `QobuzSearchResultContainer.HasMoreResults` / `GetNextOffset()` null-ref | `Items` null from JSON deserialization | Wave 2: guarded with `?.Count ?? 0` (PR #244) |
