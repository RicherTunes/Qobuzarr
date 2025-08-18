# Qobuzzarr Testing Guide

## Overview

This guide covers testing strategies, test writing guidelines, and running tests for the Qobuzzarr plugin.

## Test Structure

```
tests/
├── Unit/
│   ├── Authentication/
│   ├── API/
│   ├── Models/
│   └── Services/
├── Integration/
│   ├── QobuzApiTests.cs
│   └── SearchScenarioTests.cs
└── Fixtures/
    └── TestData/
```

## Writing Unit Tests

### Basic Test Structure

```csharp
[TestFixture]
public class QobuzAuthenticationServiceTests
{
    private Mock<IHttpClient> _httpClientMock;
    private QobuzAuthenticationService _service;

    [SetUp]
    public void Setup()
    {
        _httpClientMock = new Mock<IHttpClient>();
        _service = new QobuzAuthenticationService(_httpClientMock.Object);
    }

    [Test]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsSession()
    {
        // Arrange
        var credentials = new QobuzCredentials 
        { 
            Email = "test@example.com",
            MD5Password = "hashed_password"
        };
        
        // Act
        var result = await _service.AuthenticateAsync(credentials);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid(), Is.True);
    }
}
```

### Testing Best Practices

1. **Test Naming Convention**
   ```
   MethodName_StateUnderTest_ExpectedBehavior
   ```

2. **AAA Pattern**
   - Arrange: Set up test data
   - Act: Execute the method
   - Assert: Verify results

3. **Mock External Dependencies**
   ```csharp
   _apiClientMock.Setup(x => x.GetAsync<Album>(It.IsAny<string>()))
                 .ReturnsAsync(new Album { Id = "123" });
   ```

## Integration Tests

### Configuration

```json
{
  "TestSettings": {
    "QobuzTestEmail": "test@example.com",
    "QobuzTestPassword": "password",
    "SkipIntegrationTests": false
  }
}
```

### Writing Integration Tests

```csharp
[TestFixture]
[Category("Integration")]
public class QobuzApiIntegrationTests
{
    private QobuzApiClient _apiClient;

    [OneTimeSetUp]
    public async Task Setup()
    {
        if (ShouldSkipIntegrationTests())
        {
            Assert.Ignore("Integration tests disabled");
        }

        _apiClient = new QobuzApiClient();
        await AuthenticateTestUser();
    }

    [Test]
    public async Task Search_RealApi_ReturnsResults()
    {
        // Act
        var results = await _apiClient.SearchAlbumsAsync("Pink Floyd");
        
        // Assert
        Assert.That(results, Is.Not.Empty);
    }
}
```

## Test Data

### Using Test Fixtures

```csharp
public static class TestAlbums
{
    public static QobuzAlbum DarkSideOfTheMoon => new QobuzAlbum
    {
        Id = "0060254735439",
        Title = "The Dark Side of the Moon",
        Artist = new QobuzArtist { Name = "Pink Floyd" }
    };
}
```

### Mock Responses

```json
// test-data/album-search-response.json
{
  "albums": {
    "items": [
      {
        "id": "0060254735439",
        "title": "The Dark Side of the Moon"
      }
    ]
  }
}
```

## Running Tests

### Command Line

```bash
# All tests
dotnet test

# Unit tests only
dotnet test --filter "Category!=Integration"

# With coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Specific test
dotnet test --filter "FullyQualifiedName~AuthenticationTests"
```

### Visual Studio

1. Open Test Explorer (Test → Test Explorer)
2. Build solution
3. Run All Tests or select specific tests

## Code Coverage

### Generate Coverage Report

```bash
# Install report generator
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutput=./coverage/

# Create HTML report
reportgenerator -reports:coverage.cobertura.xml -targetdir:coveragereport
```

### Coverage Goals

- Overall: >80%
- Core Services: >90%
- API Client: >85%
- Models: >95%

## Performance Testing

```csharp
[Test]
[Timeout(1000)] // 1 second timeout
public async Task Search_Performance_CompletesQuickly()
{
    var stopwatch = Stopwatch.StartNew();
    
    await _service.SearchAsync("test");
    
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500));
}
```

## Testing Checklist

Before submitting PR:
- [ ] All tests pass locally
- [ ] New features have tests
- [ ] Code coverage maintained/improved
- [ ] Integration tests pass (if applicable)
- [ ] No ignored/skipped tests without reason
- [ ] Performance tests pass