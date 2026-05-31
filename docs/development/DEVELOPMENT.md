# Qobuzzarr Development Guide

This guide covers everything you need to know to develop, build, test, and contribute to the Qobuzzarr plugin.

## Table of Contents

- [Development Environment Setup](#development-environment-setup)
- [Project Structure](#project-structure)
- [Building the Plugin](#building-the-plugin)
- [Testing](#testing)
- [Debugging](#debugging)
- [Adding New Features](#adding-new-features)
- [Code Standards](#code-standards)
- [Documentation Guidelines](#documentation-guidelines)
- [Release Process](#release-process)

## Development Environment Setup

### Prerequisites

1. **.NET 8.0 SDK** or later

   ```bash
   # Verify installation
   dotnet --version
   ```

2. **IDE** (Choose one):
   - Visual Studio 2022 (Windows/Mac)
   - Visual Studio Code with C# extension
   - JetBrains Rider

3. **Git** for version control

4. **Docker** (optional, for testing with Lidarr)

### Initial Setup

1. **Clone the Repository**

   ```bash
   git clone https://github.com/richertunes/qobuzarr.git
   cd qobuzarr
   ```

2. **Install Dependencies**

   ```bash
   # Restore NuGet packages
   dotnet restore
   
   # Build to verify setup
   dotnet build
   ```

3. **Copy Lidarr Dependencies**

   ```bash
   # Linux/macOS
   ./scripts/extract-lidarr-assemblies.sh
   
   # Windows
   .\download-lidarr-assemblies.ps1
   ```

## Project Structure

```
qobuzarr/
├── src/                          # Main source code
│   ├── API/                      # Qobuz API client implementation
│   ├── Authentication/           # Authentication services
│   ├── Download/                 # Download client implementation
│   │   ├── Clients/             # Download client classes
│   │   └── Queue/               # Queue management
│   ├── Indexers/                # Search indexer implementation
│   ├── Models/                  # Data models
│   │   └── Authentication/      # Auth-specific models
│   ├── Services/                # Business logic services
│   ├── Utilities/               # Helper classes
│   ├── QobuzModule.cs          # DI configuration
│   └── QobuzzarrPlugin.cs      # Plugin entry point
├── tests/                       # Test projects
│   ├── Unit/                    # Unit tests
│   └── Integration/             # Integration tests
├── docs/                        # Documentation
├── scripts/                     # Build and utility scripts
├── QobuzCLI/                   # CLI tool for testing
└── Lidarr.Plugin.Qobuz.csproj # Main project file
```

## Building the Plugin

### Development Build

```bash
# Debug build (for development)
dotnet build --configuration Debug

# Output: bin/Debug/net8.0/Lidarr.Plugin.Qobuzarr.dll
```

### Release Build

```bash
# Release build with optimizations
dotnet build --configuration Release

# ILRepack will automatically merge dependencies
# Output: bin/Release/net8.0/Lidarr.Plugin.Qobuzarr.dll
```

### Build Targets

The project includes custom MSBuild targets:

- `ILRepack`: Merges dependencies into single DLL
- `CopyToPlugins`: Copies to local Lidarr instance (if configured)

## Testing

### Unit Tests

```bash
# Run all unit tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run specific test
dotnet test --filter "FullyQualifiedName~QobuzAuthenticationService"
```

### Integration Tests

Integration tests require valid Qobuz credentials:

```bash
# Set test credentials
export QOBUZ_TEST_EMAIL="test@example.com"
export QOBUZ_TEST_PASSWORD="password"

# Run integration tests
dotnet test tests/Integration
```

### Query Intelligence Testing

The Query Intelligence system has comprehensive test coverage with specific testing procedures:

#### Unit Tests

```bash
# Run all Query Intelligence unit tests
dotnet test --filter "QueryComplexity" --verbosity normal

# Run specific component tests
dotnet test --filter "QueryComplexityClassifierTests" 
dotnet test --filter "SmartQueryStrategyTests"

# Run edge case tests
dotnet test --filter "QueryIntelligenceEdgeCaseTests"

# Coverage report for Query Intelligence
dotnet test /p:CollectCoverage=true /p:Include="[*]Lidarr.Plugin.Qobuzarr.Indexers.QueryComplexityClassifier*,[*]Lidarr.Plugin.Qobuzarr.Indexers.SmartQueryStrategy*"
```

#### Integration Testing

```bash
# Test Query Intelligence with real data (124 test scenarios)
dotnet test tests/Qobuzarr.Tests/Unit/Indexers/QueryIntelligenceIntegrationTests.cs

# Real data simulation tests (322 albums)
dotnet test tests/Qobuzarr.Tests/Simulations/RealDataQueryIntelligenceTests.cs

# Performance validation tests
dotnet test tests/Qobuzarr.Tests/Simulations/QueryIntelligenceSimulationTests.cs
```

#### CLI Testing Commands

The QobuzCLI provides specialized Query Intelligence testing:

```bash
cd QobuzCLI

# Test Query Intelligence classification
dotnet run -- analyze-complexity --artist "Pink Floyd" --album "The Wall"
dotnet run -- analyze-complexity --artist "AC/DC" --album "Back in Black"
dotnet run -- analyze-complexity --artist "Björk" --album "Homogenic"

# Performance testing with Query Intelligence
dotnet run -- test-performance --albums 50 --enable-query-intelligence
dotnet run -- test-performance --albums 50 --disable-query-intelligence

# Query Intelligence simulation on real data
dotnet run -- test-queries --real-data --verbose
dotnet run -- test-queries --simulation --count 100

# Debug Query Intelligence classifications
export QOBUZ_DEBUG_QUERIES="true"
dotnet run -- search "Various Artists" --debug
dotnet run -- search "The Beatles Abbey Road" --debug
```

#### Edge Case Testing

Query Intelligence handles many edge cases. Test these scenarios:

```bash
# Unicode and special characters
dotnet run -- analyze-complexity --artist "Sigur Rós" --album "Ágætis byrjun"
dotnet run -- analyze-complexity --artist "Mötley Crüe" --album "Shout at the Devil"

# Various Artists and compilations
dotnet run -- analyze-complexity --artist "Various Artists" --album "Top Hits 2024"
dotnet run -- analyze-complexity --artist "Compilation" --album "Greatest Movie Themes"

# Complex album titles
dotnet run -- analyze-complexity --artist "Miles Davis" --album "Kind of Blue (Deluxe Remastered Edition)"
dotnet run -- analyze-complexity --artist "Pink Floyd" --album "The Dark Side of the Moon (50th Anniversary)"

# Live recordings and classical
dotnet run -- analyze-complexity --artist "Queen" --album "Live at Wembley '86"
dotnet run -- analyze-complexity --artist "Ludwig van Beethoven" --album "Symphony No. 9 in D minor, Op. 125"
```

#### Performance Validation

Validate Query Intelligence performance improvements:

```bash
# Measure API call reduction
dotnet run -- test-queries --measure-reduction --albums 50

# Compare with/without optimization
dotnet run -- test-performance --baseline --albums 20
export QOBUZ_QUERY_INTELLIGENCE="false"
dotnet run -- test-performance --no-optimization --albums 20

# Real-world library testing
dotnet run -- test-queries --lidarr-export "path/to/exported/albums.json"
```

#### Test Data Generation

Generate test data for Query Intelligence validation:

```bash
# Create test scenarios
dotnet run -- generate-test-data --scenarios 50 --complexity-mix

# Export real Lidarr data for testing
dotnet run -- export-lidarr-albums --output "test-albums.json" --count 100

# Validate test data quality
dotnet run -- validate-test-data --file "test-albums.json"
```

#### Continuous Integration Testing

For CI/CD pipelines, use these commands:

```bash
# Fast Query Intelligence validation (CI-friendly)
dotnet test --filter "QueryIntelligence&!Integration" --logger trx --results-directory TestResults

# Performance regression tests
dotnet run --project QobuzCLI -- test-performance --ci-mode --threshold 45 --albums 30

# Quality assurance tests
dotnet test --filter "QueryIntelligenceEdgeCase" --logger "console;verbosity=detailed"
```

### Manual Testing with CLI

The QobuzCLI tool helps test functionality:

```bash
cd QobuzCLI

# Test authentication
dotnet run -- auth --email user@example.com --password pass

# Test search
dotnet run -- search "Pink Floyd" --limit 10

# Test download (when implemented)
dotnet run -- download --album-id 123456
```

## Debugging

### Local Lidarr Setup

1. **Run Lidarr with Plugin Support**

   ```bash
   docker run -d \
     --name=lidarr-dev \
     -e PUID=1000 \
     -e PGID=1000 \
     -p 8686:8686 \
     -v ./config:/config \
     -v ./plugins:/config/plugins \
     ghcr.io/hotio/lidarr:pr-plugins
   ```

2. **Copy Plugin DLL**

   ```bash
   cp bin/Debug/net8.0/Lidarr.Plugin.Qobuzarr.dll ./plugins/
   ```

3. **Restart Lidarr**

   ```bash
   docker restart lidarr-dev
   ```

### Debugging in VS Code

1. Create `.vscode/launch.json`:

   ```json
   {
     "version": "0.2.0",
     "configurations": [
       {
         "name": "Debug QobuzCLI",
         "type": "coreclr",
         "request": "launch",
         "preLaunchTask": "build",
         "program": "${workspaceFolder}/QobuzCLI/bin/Debug/net8.0/QobuzCLI.dll",
         "args": ["search", "test"],
         "cwd": "${workspaceFolder}/QobuzCLI",
         "console": "internalConsole"
       }
     ]
   }
   ```

2. Set breakpoints in code
3. Press F5 to start debugging

### Logging

Enable detailed logging for debugging:

```csharp
// In development
_logger.Debug("API Request: {0}", request.Url);
_logger.Trace("Response: {0}", response.Content);

// Use log levels appropriately
_logger.Error(ex, "Failed to authenticate");
_logger.Warn("Rate limit approaching: {0}/60", requestCount);
_logger.Info("Search completed: {0} results", results.Count);
```

## Adding New Features

### 1. API Endpoints

To add a new Qobuz API endpoint:

```csharp
// 1. Add to IQobuzApiClient
public interface IQobuzApiClient
{
    Task<QobuzPlaylist> GetPlaylistAsync(string playlistId);
}

// 2. Implement in QobuzApiClient
public async Task<QobuzPlaylist> GetPlaylistAsync(string playlistId)
{
    var parameters = new Dictionary<string, string>
    {
        ["playlist_id"] = playlistId
    };
    return await GetAsync<QobuzPlaylist>("/playlist/get", parameters);
}

// 3. Create model
public class QobuzPlaylist
{
    [JsonProperty("id")]
    public string Id { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    // ... other properties
}
```

### 2. Search Strategies

To add a new search strategy:

```csharp
// In QobuzRequestGenerator
private IEnumerable<IndexerRequest> GetPlaylistSearchRequests(string query)
{
    var parameters = new Dictionary<string, string>
    {
        ["query"] = query,
        ["type"] = "playlists",
        ["limit"] = Settings.SearchLimit.ToString()
    };
    
    yield return new IndexerRequest(BuildUrl("/playlist/search", parameters), HttpAccept.Json);
}
```

### 3. Settings

To add new configuration options:

```csharp
// 1. Add to QobuzIndexerSettings
[FieldDefinition(15, Label = "Include Playlists", Type = FieldType.Checkbox)]
public bool IncludePlaylists { get; set; }

// 2. Add validation if needed
public NzbDroneValidationResult Validate()
{
    if (IncludePlaylists && string.IsNullOrEmpty(UserId))
    {
        return new NzbDroneValidationResult(
            new[] { new ValidationFailure(nameof(IncludePlaylists), 
                "User ID required for playlist access") });
    }
}
```

## Code Standards

### C# Coding Conventions

1. **Naming**
   - PascalCase for public members
   - camelCase for private fields
   - _underscore prefix for injected dependencies

2. **Documentation**

   ```csharp
   /// <summary>
   /// Searches for albums matching the specified criteria.
   /// </summary>
   /// <param name="criteria">Search criteria including artist and album.</param>
   /// <returns>List of matching albums.</returns>
   /// <exception cref="QobuzApiException">Thrown when API returns an error.</exception>
   public async Task<List<QobuzAlbum>> SearchAlbumsAsync(SearchCriteria criteria)
   ```

3. **Async/Await**
   - Always use async/await for I/O operations
   - Suffix async methods with `Async`
   - Avoid `.Result` or `.Wait()`

4. **Error Handling**

   ```csharp
   try
   {
       var result = await _apiClient.GetAsync<T>(endpoint);
       return result;
   }
   catch (QobuzApiException ex) when (ex.StatusCode == 404)
   {
       _logger.Debug("Resource not found: {0}", endpoint);
       return null;
   }
   catch (Exception ex)
   {
       _logger.Error(ex, "Unexpected error");
       throw;
   }
   ```

### Code Review Checklist

- [ ] Code follows C# conventions
- [ ] Public APIs have XML documentation
- [ ] Unit tests added/updated
- [ ] No hardcoded values
- [ ] Proper error handling
- [ ] Logging at appropriate levels
- [ ] No sensitive data in logs
- [ ] Performance considerations addressed

## Documentation Guidelines

The Qobuzzarr project maintains comprehensive documentation both in code and in separate documentation files. This section outlines the standards and practices for maintaining high-quality documentation.

### XML Documentation Standards

All public APIs must include comprehensive XML documentation:

```csharp
/// <summary>
/// Brief description of what the method/class does.
/// Include context about when and how it should be used.
/// </summary>
/// <remarks>
/// Detailed explanation of:
/// - Key features and capabilities
/// - Important behaviors or side effects
/// - Usage patterns and examples
/// - Integration points with other components
/// </remarks>
/// <param name="paramName">Clear description of the parameter's purpose and constraints.</param>
/// <returns>Description of what is returned, including possible null values.</returns>
/// <exception cref="ExceptionType">When this exception is thrown and why.</exception>
public async Task<ResultType> ExampleMethodAsync(string paramName)
```

### Documentation Structure

1. **Interfaces**: Document the contract and expected behavior
2. **Implementations**: Focus on specific implementation details and behaviors
3. **Models**: Describe the data structure and its purpose
4. **Exceptions**: Explain when and why they are thrown

### Key Documentation Areas

#### Plugin Core (src/)

- All public interfaces must have comprehensive XML docs
- Implementation classes should document non-obvious behaviors
- Model classes should explain their purpose and key properties
- Service classes should document their responsibilities and dependencies

#### CLI Application (QobuzCLI/)

- Command classes should document their purpose and usage
- Service interfaces should clearly define their contracts
- Configuration models should explain all options
- Utility classes should document their specific use cases

### Documentation File Standards

#### In-Code Documentation

- Use `///` XML comments for all public members
- Include `<summary>`, `<param>`, `<returns>`, and `<exception>` tags
- Add `<remarks>` for complex behaviors or important context
- Document thread safety, async patterns, and disposal requirements

#### README and Markdown Files

- Keep README.md current with latest features and installation steps
- Maintain CHANGELOG.md for all releases
- Update API documentation when interfaces change
- Include code examples in documentation files

### Documentation Maintenance

1. **Code Reviews**: Verify documentation is updated with code changes
2. **Release Process**: Update all relevant documentation before releases
3. **API Changes**: Document breaking changes and migration paths
4. **Examples**: Keep code examples current and tested

### Tools and Automation

- XML documentation is included in NuGet packages
- Documentation warnings are treated as build warnings
- Use `<inheritdoc/>` for inherited implementations when appropriate
- Generate API documentation from XML comments for releases

## Release Process

### Version Numbering

Follow Semantic Versioning (SemVer):

- **Major**: Breaking changes
- **Minor**: New features (backward compatible)
- **Patch**: Bug fixes

### Release Steps

1. **Update Version**

   ```xml
   <!-- In .csproj -->
   <Version>1.1.0</Version>
   <AssemblyVersion>1.1.0.0</AssemblyVersion>
   <FileVersion>1.1.0.0</FileVersion>
   ```

2. **Update Documentation**
   - Update README.md with version
   - Create release notes
   - Update CHANGELOG.md

3. **Create Release Build**

   ```bash
   # Clean previous builds
   dotnet clean
   
   # Build release
   dotnet build --configuration Release
   
   # Run tests
   dotnet test --configuration Release
   ```

4. **Create GitHub Release**

   ```bash
   # Tag the release
   git tag -a v1.1.0 -m "Release version 1.1.0"
   git push origin v1.1.0
   ```

5. **Upload Release Assets**
   - `Lidarr.Plugin.Qobuzarr.dll`
   - Release notes
   - Installation instructions

### Continuous Integration

GitHub Actions workflow example:

```yaml
name: Build and Test

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 8.0.x
    - name: Restore
      run: dotnet restore
    - name: Build
      run: dotnet build --configuration Release
    - name: Test
      run: dotnet test --no-build --verbosity normal
```

## Troubleshooting Development Issues

### Common Issues

1. **Missing Lidarr References**

   ```
   Error: Could not load file or assembly 'Lidarr.Core'
   ```

   Solution: Run `download-lidarr-assemblies.ps1` or `./scripts/extract-lidarr-assemblies.sh`

2. **ILRepack Failures**

   ```
   Error: ILRepack failed with exit code 1
   ```

   Solution: Check for conflicting assembly versions

3. **Plugin Not Loading**
   - Check Lidarr logs: `/config/logs/lidarr.txt`
   - Verify .NET version compatibility
   - Ensure all dependencies are merged

### Development Tips

1. **Use the CLI Tool** for rapid testing
2. **Enable Debug Logging** during development
3. **Test with Different Accounts** (free vs. paid)
4. **Mock External Dependencies** in unit tests
5. **Profile Performance** for large result sets

## Resources

- [Lidarr Plugin Development](https://wiki.servarr.com/lidarr/plugins)
- [Qobuz API Documentation](https://github.com/Qobuz/api-documentation)
- [.NET 8 Documentation](https://docs.microsoft.com/dotnet/)
- [C# Coding Conventions](https://docs.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
