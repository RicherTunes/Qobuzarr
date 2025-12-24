# Testing Qobuzarr

This guide covers running tests locally and in CI, including category filters, runsettings, and the integration test workflow.

## Quick Reference

```bash
# CI-like fast tests (excludes LiveIntegration, Integration, Performance, Quarantined)
CI_TEST_FILTER='Category!=LiveIntegration&Category!=Integration&Category!=Performance&Category!=Quarantined'
dotnet test --settings tests/Default.runsettings --filter "$CI_TEST_FILTER"

# Using Default.runsettings (recommended for local development)
dotnet test Qobuzarr.sln --settings tests/Default.runsettings

# Full suite with coverage (nightly/manual)
# Note: Full.runsettings does not exclude Integration/LiveIntegration by itself.
dotnet test Qobuzarr.sln --settings tests/Full.runsettings --filter "$CI_TEST_FILTER"
```

## Test Categories

Tests are organized by category trait to enable selective execution:

| Category | Description | Default.runsettings | CI Fast | CI Full |
|----------|-------------|---------------------|---------|---------|
| *(none)* | Standard unit tests | Run | Run | Run |
| `Integration` | Requires Lidarr assemblies or external services | Skip | Skip | Skip (use manual workflow) |
| `Performance` | ML benchmarks, stress tests | Skip | Skip | Skip by default |
| `LiveIntegration` | Requires live Lidarr instance | Skip | Skip | Skip by default |
| `Quarantined` | Temporarily disabled (known issues) | Skip | Skip | Skip |
| `Slow` | Long-running tests (>30s) | Skip | Skip | Run |
| `Benchmark` | Performance benchmarks | Skip | Skip | Run |
| `Stress` | Load/stress tests | Skip | Skip | Run |
| `Simulations` | Simulation-based tests | Skip | Skip | Run |

### Applying Categories

```csharp
[Trait("Category", "Integration")]
public class MyIntegrationTests { }

[Fact]
[Trait("Category", "Performance")]
public void BenchmarkQueryOptimization() { }
```

## Runsettings Files

### `tests/Default.runsettings`
For local development and PR checks. Excludes heavy categories:
- 15-minute session timeout
- 10-second hang detection
- Excludes: `LiveIntegration`, `Integration`, `Performance`, `Simulations`, `Slow`, `Benchmark`, `Stress`

### `tests/Full.runsettings`
For nightly runs and manual full-suite execution:
- 60-minute session timeout
- 60-second hang detection
- Includes coverage collection (Cobertura format)
- No category filter by itself (CI and most local runs should pass an explicit `--filter`)

## CI Workflows

### Fast Unit Tests (PR/Push)
Runs on every PR and push to main branches:
```bash
dotnet test --settings tests/Default.runsettings --filter "$CI_TEST_FILTER"
```

### Integration Tests (Manual Workflow)
Run via `workflow_dispatch` at `.github/workflows/integration-tests.yml`:

```yaml
workflow_dispatch:
  inputs:
    lidarr_version:
      description: 'Lidarr plugins branch version'
      default: 'pr-plugins-4'
```

**How it works:**
1. Extracts Lidarr assemblies from Docker image
2. Passes assembly path via MSBuild property
3. Runs tests with `Category=Integration` filter

**LidarrAssembliesPath Override:**
The csproj conditionally sets `LidarrAssembliesPath` only when empty, allowing CI to override:

```xml
<!-- In Qobuzarr.Tests.csproj -->
<PropertyGroup Condition="'$(LidarrAssembliesPath)' == ''">
  <LidarrAssembliesPath>$(MSBuildThisFileDirectory)..\..\ext\Lidarr\_output\net8.0</LidarrAssembliesPath>
</PropertyGroup>
```

CI overrides with:
```bash
dotnet test -p:LidarrAssembliesPath="${GITHUB_WORKSPACE}/ext/lidarr-assemblies" ...
```

### Local Integration Runner (Recommended)
Use `scripts/run-integration-tests.ps1` to mimic the integration workflow locally.

```powershell
# Compile-time integration tests (requires host assemblies)
.\scripts\run-integration-tests.ps1 -CheckHostVersions

# Extract host assemblies from Docker, validate pins, run compile-time integration tests
.\scripts\run-integration-tests.ps1 -ExtractHostAssemblies -CheckHostVersions -LidarrTag "pr-plugins-2.14.2.4786"

# Also perform a runtime load check (plugin loads in Lidarr container)
.\scripts\run-integration-tests.ps1 -ExtractHostAssemblies -CheckHostVersions -SmokeTest

# Run live integration tests against local docker-compose Lidarr (uses .docker/config and .docker/plugins)
.\scripts\run-integration-tests.ps1 -IncludeLive
```

Notes:
- `-IncludeLive` expects a configured Lidarr instance (artists present); it reads the API key from `.docker/config/config.xml` if not provided.
- `tests/Default.runsettings` excludes Integration by design; `run-integration-tests.ps1` uses `tests/Full.runsettings`.

### Nightly Full Suite
Scheduled workflow that runs `Full.runsettings` with coverage collection.       

## Local Development

### Running Without Lidarr Assemblies
Most unit tests don't require Lidarr assemblies. Use the category filter:
```bash
dotnet test --filter "Category!=LiveIntegration&Category!=Integration&Category!=Performance"
```

### Setting Up Lidarr Assemblies Locally
For integration tests, extract assemblies from Docker:

```bash
# Extract from plugins branch
docker create --name lidarr ghcr.io/hotio/lidarr:pr-plugins-4
docker cp lidarr:/app/bin/. ext/lidarr-assemblies/
docker rm lidarr

# Set the path (optional - csproj has fallback)
export LidarrAssembliesPath="$(pwd)/ext/lidarr-assemblies"
```

### Live Integration Tests
Require a running Lidarr instance:

```bash
export LIDARR_URL="http://localhost:8686"
export LIDARR_API_KEY="your-api-key"
export ENABLE_LIVE_INTEGRATION_TESTS=true

# Optional Qobuz credentials
export QOBUZ_APP_ID="..."
export QOBUZ_APP_SECRET="..."
export QOBUZ_EMAIL="..."
export QOBUZ_PASSWORD="..."

dotnet test --filter "Category=LiveIntegration"
```

## Quarantine Process

When a test is flaky or blocked by external issues:

1. Add the `Quarantined` trait:
   ```csharp
   [Trait("Category", "Quarantined")]
   [Trait("Quarantine", "Issue #123 - Flaky on Windows")]
   ```

2. Create a tracking issue describing:
   - Why the test was quarantined
   - Steps to reproduce the failure
   - Acceptance criteria for unquarantining

3. To unquarantine:
   - Fix the underlying issue
   - Remove the `Quarantined` trait
   - Close the tracking issue

## Known Issues

### GitHub Actions CI Unavailable
GitHub Actions workflows fail with billing/spending limit errors while repositories are private.

**Workaround:** Test locally using the commands above.

**Resolution:** Will be fixed when repositories are made public.

### Known Build Warnings
Some warnings are expected when building/running tests locally due to host-assembly and legacy binding-redirect behavior:
- `MSB3836` (TagLibSharp binding redirect conflict): caused by an explicit binding redirect conflicting with auto-generated redirects; usually safe to ignore unless it causes runtime load errors.
- `MSB3277` (assembly version conflicts): expected when referencing extracted Lidarr host assemblies alongside NuGet packages in the test project; generally non-actionable unless it manifests as runtime type identity/load failures.

## Test Naming Convention

Follow the `Given_When_Then` or `Method_Scenario_Expected` pattern:

```csharp
// Method_Scenario_Expected
public void ExtractVersion_FromParentheses_ShouldExtractCorrectly()

// Given_When_Then  
public void GivenValidCredentials_WhenAuthenticating_ThenReturnsToken()
```

## Coverage

Coverage is collected via Coverlet in `Full.runsettings`:
- Format: Cobertura XML
- Excludes: Test assemblies, migrations, generated code
- Output (local): `tests/TestResults/**/coverage.cobertura.xml`
- Output (CI): `test-results/**/coverage.cobertura.xml` (and `tests/TestResults/**` for compatibility with `run-tests.ps1`)

View coverage locally:
```bash
dotnet test --settings tests/Full.runsettings
# Coverage XML in tests/TestResults/
```
