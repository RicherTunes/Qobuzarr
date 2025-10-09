# Qobuzarr Test Guide

## Quick Start

- Fast, deterministic suite (excludes Integration/Live/Slow/Benchmarks):
  - PowerShell: `./tests/run-tests.ps1 -Configuration Release`
  - Direct: `dotnet build -c Release && dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj -c Release --settings tests/Default.runsettings --no-build`

- Full suite (all categories, still skips Live unless explicitly enabled):
  - PowerShell: `./tests/run-tests.ps1 -Configuration Release -Full`
  - Direct: `dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj -c Release --settings tests/Full.runsettings`

- Enable LiveIntegration tests (requires a reachable Lidarr + API key):
  - Set environment: `ENABLE_LIVE_INTEGRATION_TESTS=true`, `LIDARR_URL`, `LIDARR_API_KEY`
  - Run: `./tests/run-tests.ps1 -Configuration Release -Live`

Notes
- Avoid `dotnet watch test` on the solution. Historically, generating `plugin.json` on every build caused infinite watch loops. We’ve moved generation to Pack/Publish, but watchers can still thrash on multi-project rebuilds. Prefer the scripts above.
- The runner builds once, then runs tests with `--no-build` to avoid testhost file-lock issues.

## Running Tests (direct)

- Basic:
  - `dotnet test`
- With coverage:
  - `dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings`
- Verbose:
  - `dotnet test --verbosity detailed`
- TRX report:
  - `dotnet test --logger trx --results-directory TestResults`

## Test Structure

```
tests/
├── Lidarr.Plugin.Qobuz.Tests/
│   ├── Unit/
│   │   ├── Authentication/    # Authentication service tests
│   │   ├── API/              # API client tests  
│   │   ├── Models/           # Data model tests
│   │   └── Download/         # Download client tests
│   ├── Fixtures/             # Test base classes and utilities
│   └── TestData/             # Sample data and responses
└── coverlet.runsettings      # Coverage configuration
```

## Coverage Targets

- **Overall Project**: 80%+ line coverage
- **Critical Components**: 85%+ line coverage
- **Models**: 90%+ line coverage

## Key Categories

### ✅ Authentication Tests (19 tests)
- Email/password authentication flow
- Token-based authentication  
- Session management and caching
- Credential validation
- MD5 password hashing

### ✅ Data Model Tests (35+ tests)
- QobuzAlbum serialization and business logic
- QobuzTrack file naming and metadata
- QobuzCredentials validation
- JSON deserialization edge cases
- Safe filename generation

### ✅ API Client Tests (15+ tests)
- HTTP request/response handling
- Rate limiting and backoff
- Authentication integration
- Caching behavior
- Error handling and retries

### ✅ Download Client Tests (20+ tests)
- Download initiation and tracking
- Progress reporting
- File management
- Error scenarios
- Cleanup operations

## Running Specific Test Categories

```bash
# Authentication tests only
dotnet test --filter "FullyQualifiedName~Authentication"

# Model tests only  
dotnet test --filter "FullyQualifiedName~Models"

# API tests only
dotnet test --filter "FullyQualifiedName~API"

# Download tests only
dotnet test --filter "FullyQualifiedName~Download"
```

## CI/CD Integration

The tests are designed to run in automated environments. All tests use mocking to avoid external dependencies and should complete in under 30 seconds.

## Notes

- Tests require the main project to be buildable
- Missing Lidarr assemblies will cause compilation errors (expected)
- All external dependencies are mocked for isolation
- Tests validate both happy path and error scenarios
