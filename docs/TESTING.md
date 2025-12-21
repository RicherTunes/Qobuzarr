# Testing Guide

## Test Categories

### Unit Tests (Default)
- Run by default in CI
- No external dependencies
- Fast execution

### Integration Tests (`Category=Integration`)
- Require Lidarr host assemblies
- Excluded from default CI runs
- Run manually or in dedicated workflow

### Performance Tests (`Category=Performance`)
- ML model benchmarks
- Excluded from default CI
- Run in nightly builds

## Running Tests Locally

### Quick Unit Tests
```bash
dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj --filter "Category!=Integration&Category!=Performance"
```

### All Tests (requires Lidarr assemblies)
```bash
dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj
```

### Specific Test Filter
```bash
dotnet test --filter "FullyQualifiedName~QobuzParserTests"
```

## CI Filters

The CI workflows use this filter by default:
```
Category!=Integration&Category!=Performance
```

This excludes:
- Tests requiring Lidarr host runtime
- Long-running ML performance tests

## Lidarr Assemblies

### LidarrAssembliesPath Convention
Tests that need Lidarr assemblies look for them at:
1. `ext/lidarr-assemblies/` in repo root
2. Path specified by `LidarrAssembliesPath` environment variable

### Acquiring Assemblies
```bash
docker create --name lidarr ghcr.io/hotio/lidarr:pr-plugins-VERSION
docker cp lidarr:/app/bin ext/lidarr-assemblies
docker rm lidarr
```

## Quarantine Process

### When to Quarantine
- Test is failing due to external dependency issues
- Test needs implementation fixes but blocks CI
- Temporary measure only - track in an issue

### How to Quarantine
Add trait to test method:
```csharp
[Trait("Category", "Quarantined")]
[Fact]
public void MyTest() { }
```

### Unquarantining
1. Fix the underlying issue
2. Remove the `[Trait("Category", "Quarantined")]`
3. Verify test passes locally
4. Submit PR

### Tracking
- All quarantined tests must have a tracking issue
- Review quarantined tests monthly
- Goal: Zero permanent quarantines

## Test Conventions

### Naming
Use BDD-style: `Method_Scenario_ExpectedBehavior`

### Assertions
Use FluentAssertions for readable assertions.

### Test Data
- Use builders (e.g., `QobuzAlbumBuilder`)
- Store shared test data in `TestData/` folder
