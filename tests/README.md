# Qobuzzarr Unit Tests

## Running Tests

### Basic Test Execution

```bash
dotnet test
```

### With Code Coverage

```bash
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

### Verbose Output

```bash
dotnet test --verbosity detailed
```

### Generate Test Report

```bash
dotnet test --logger trx --results-directory TestResults
```

## Test Structure

```
tests/
├── Qobuzarr.Tests/           # Main test project (Lidarr.Plugin.Qobuzarr.Tests namespace)
│   ├── Unit/
│   │   ├── Authentication/    # Authentication service tests
│   │   ├── API/              # API client tests
│   │   ├── Models/           # Data model tests
│   │   └── Download/         # Download client tests
│   ├── Fixtures/             # Test base classes and utilities
│   └── TestData/             # Sample data and responses
└── coverlet.runsettings      # Coverage configuration
```

## Test Coverage Targets

- **Overall Project**: 80%+ line coverage
- **Critical Components**: 85%+ line coverage
- **Models**: 90%+ line coverage

## Key Test Categories

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
