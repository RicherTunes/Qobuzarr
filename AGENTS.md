# Qobuzarr - Lidarr Plugin for Qobuz

Qobuz streaming integration for Lidarr providing indexer and download client functionality.

## Project Overview

| Aspect | Details |
|--------|---------|
| **Type** | Lidarr Plugin (Indexer + Download Client) |
| **Target** | Qobuz streaming service |
| **Assembly** | `Lidarr.Plugin.Qobuzarr.dll` |
| **Protocol** | `QobuzDownloadProtocol` (unique marker) |

## Quick Commands

```bash
# Build
dotnet build Qobuzarr.sln -c Release

# Test
dotnet test Qobuzarr.sln

# Test (fast, no integration)
dotnet test --filter "Category!=Integration&Category!=Performance"
```

## Architecture

```
src/
├── API/              # Qobuz API client (598-line God class - needs refactoring)
├── Authentication/   # Dynamic auth extraction from web player
├── Download/         # Download orchestration
├── Indexers/         # Search with ML optimization
├── Services/         # Business logic (40+ classes)
├── Security/         # Credential management
└── Models/           # DTOs and domain models
```

## Critical Architecture Issues

### 1. QobuzApiClient God Class
**Location**: `src/API/QobuzApiClient.cs` (598 LOC)
**Problem**: HTTP + auth + caching + rate limiting all in one class
**Agent**: `@qobuzarr-architecture`

### 2. Manual DI in Download Client
**Location**: `src/Download/Clients/QobuzDownloadClient.cs:571`
**Problem**: `CreateTrackDownloaderFactory()` manually instantiates dependencies

### 3. Disabled Tests
**CRITICAL**: 3 major test files disabled:
- `QobuzDownloadClientTests.cs` (416 lines)
- `QobuzAlbumTests.cs`
- `QobuzTrackTests.cs`
**Agent**: `@qobuzarr-testing`

## ML Query Optimization

The indexer uses ML-powered query optimization:
- **49.83% API call reduction**
- **94.7% cache hit rate**
- Pre-trained decision tree with 100K+ training examples

**Key file**: `src/Indexers/CompiledMLQueryOptimizer.cs`
**Agent**: `@qobuzarr-ml`

## Authentication System

Dynamic credential extraction from Qobuz web player:
- Parses obfuscated JavaScript bundle
- Extracts app secrets via regex
- Multi-fallback authentication strategies

**Key file**: `src/Authentication/QobuzAuthenticationService.cs` (447 LOC)
**Agent**: `@qobuzarr-security`

## Available Agents

| Agent | Scope |
|-------|-------|
| `@qobuzarr-architecture` | API client refactoring, DI patterns, service consolidation |
| `@qobuzarr-cicd` | Build system, version compatibility, deployment |
| `@qobuzarr-ml` | Query optimization, ML performance, retraining |
| `@qobuzarr-security` | Authentication, credentials, vulnerabilities |
| `@qobuzarr-testing` | Test restoration, coverage, quality assurance |

## Infrastructure Gaps

| Area | Status | Priority |
|------|--------|----------|
| CHANGELOG.md | Missing | High |
| release.yml workflow | Missing | High |
| CodeQL | Partial | Medium |
| Disabled tests | Blocked | Critical |

## Lidarr Plugin Rules

### Protocol Pattern
```csharp
// Unique protocol marker for multi-plugin cohabitation
public class QobuzDownloadProtocol : IDownloadProtocol { }
```

### Host-Version-Coupled Dependencies

**Critical**: FluentValidation and NLog versions must match the Lidarr host exactly.

**Why this coupling exists**: These aren't arbitrary pins—types from these assemblies cross the
plugin boundary at runtime:
- `FluentValidation.Results.ValidationFailure` is returned by `DownloadClientBase.Test()`
- `NLog.Logger` is injected into plugin constructors by Lidarr's DI container

When plugin and host have different versions, .NET sees them as incompatible types, causing
`MissingMethodException` or assembly load failures. Do not "optimize" these pins away.

**How versions are enforced**: Guard tests in `tests/Qobuzarr.Tests/Unit/Packaging/PluginPackagingTests.cs`
read actual assembly versions from `ext/Lidarr/_output/*/` and fail if `Directory.Packages.props` doesn't match.

#### Update Procedure (when Lidarr bumps versions)

```bash
# 1. Update host assemblies from new Lidarr container
docker create --name lidarr-temp ghcr.io/hotio/lidarr:pr-plugins-NEW_VERSION
docker cp lidarr-temp:/app/bin ext/lidarr-assemblies-new
docker rm lidarr-temp

# 2. Check what changed
.\scripts\check-host-versions.ps1  # Shows current vs new versions

# 3. Update Directory.Packages.props to match new host versions

# 4. Run guard tests to verify
dotnet test --filter "FullyQualifiedName~PluginPackagingTests"
```

### ILRepack Configuration
```xml
<ILRepack Internalize="true">
  <ExcludeAssemblies>FluentValidation</ExcludeAssemblies>
</ILRepack>
```

### Constructor Pattern
```csharp
// ILocalizationService required by DownloadClientBase
public QobuzDownloadClient(
    ILocalizationService localizationService,
    Logger logger)
    : base(..., localizationService, logger)
```

## Security Standards

- Use `SecureString` for credentials
- SHA-256 with salt for hashing
- Never log sensitive data
- Proper disposal patterns

## Test Standards

- xUnit with FluentAssertions
- NSubstitute for mocking (standardizing away from Moq)
- Given_When_Then naming convention
- >80% coverage on critical paths

---

For shared Lidarr plugin patterns, see `../lidarr-plugin-claude-skills/AGENTS.md`
