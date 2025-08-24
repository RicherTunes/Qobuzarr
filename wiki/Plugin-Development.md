# Plugin Development Guide

Comprehensive guide for developing extensions and customizations for Qobuzarr, including ML models, security extensions, and custom integrations.

## 🚀 Getting Started

### Development Prerequisites

**Required Tools:**
- **.NET 8.0 SDK**: Core development framework
- **Visual Studio Code/2022**: IDE with C# support
- **Git**: Version control
- **Lidarr Instance**: For testing (hotio/lidarr:pr-plugins recommended)

**Recommended Tools:**
- **JetBrains Rider**: Advanced C# IDE
- **Docker**: Container testing environments
- **Postman**: API testing and validation

### Development Environment Setup

#### Quick Setup

```bash
# Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# Setup with automatic deployment
./setup.sh --enable-deploy --deploy-path "/path/to/test/lidarr/plugins"

# Windows equivalent
.\setup.ps1 -EnableDeploy -DeployPath "C:\TestLidarr\Plugins\Qobuzarr"
```

#### Manual Setup

```bash
# 1. Install dependencies
dotnet restore

# 2. Download Lidarr assemblies (required for compilation)
./download-lidarr-assemblies.sh --version 2.13.2.4685

# 3. Build the solution
dotnet build --configuration Debug

# 4. Run tests to verify setup
dotnet test
```

### Project Structure Overview

```
qobuzarr/
├── src/                          # Core plugin code
│   ├── API/                      # API client framework
│   ├── Authentication/           # Authentication services
│   ├── Download/                 # Download orchestration
│   ├── Indexers/                 # Search & ML optimization
│   ├── Security/                 # Security framework
│   ├── Services/                 # Business logic
│   └── Integration/              # Lidarr integration
├── QobuzCLI/                     # CLI wrapper
├── tests/                        # Test suites
├── docs/                         # Documentation
└── plugins/                      # Custom plugin directory
    └── YourPlugin/               # Your custom plugin here
```

## 🏗️ Plugin Architecture

### Core Extension Points

Qobuzarr provides several interfaces for extending functionality:

#### 1. Search Optimization Extensions

```csharp
// Custom ML query optimization
public interface IPatternLearningEngine
{
    Task<PredictionResult> PredictComplexityAsync(string artist, string album);
    float GetConfidenceScore(string artist, string album, QueryComplexity complexity);
    Task UpdateModelAsync(QueryResult actualResult);
    MLPerformanceMetrics GetStatistics();
}

// Custom search strategy
public interface ISmartQueryStrategy
{
    Task<QueryOptimizationResult> OptimizeQueryAsync(string artist, string album);
    QueryComplexity ClassifyComplexity(string artist, string album);
    bool ShouldUseOptimization(string query);
}
```

#### 2. Authentication Extensions

```csharp
// Custom authentication provider
public interface IQobuzAuthenticationProvider
{
    Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials);
    Task<bool> ValidateSessionAsync(QobuzSession session);
    Task RefreshSessionAsync(QobuzSession session);
    bool SupportsCredentialType(Type credentialType);
}

// Security extensions
public interface ISecureCredentialProvider
{
    Task<TCredential> GetCredentialAsync<TCredential>() where TCredential : class;
    Task StoreCredentialAsync<TCredential>(TCredential credential) where TCredential : class;
    Task<bool> ValidateCredentialAsync<TCredential>(TCredential credential) where TCredential : class;
}
```

#### 3. Download Extensions

```csharp
// Custom download strategy
public interface IDownloadStrategy
{
    Task<TrackDownloadResult> DownloadTrackAsync(QobuzTrack track, DownloadOptions options);
    bool SupportsQuality(QobuzAudioQuality quality);
    int Priority { get; }
}

// Download orchestration
public interface IDownloadOrchestrator
{
    Task<DownloadSummary> ProcessDownloadAsync(DownloadRequest request);
    Task<BatchDownloadResult> ProcessBatchDownloadAsync(IEnumerable<DownloadRequest> requests);
    event EventHandler<DownloadProgressEventArgs> ProgressUpdate;
}
```

### Plugin Registration

Register your plugin services with Qobuzarr's dependency injection container:

```csharp
// Plugin entry point
public class MyCustomPlugin : IQobuzarrPlugin
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register your services
        services.AddScoped<IPatternLearningEngine, MyMLOptimizer>();
        services.AddSingleton<IQobuzAuthenticationProvider, MyAuthProvider>();
        services.AddTransient<IDownloadStrategy, MyDownloadStrategy>();
        
        // Configure ML models
        services.Configure<MLConfiguration>(options =>
        {
            options.ModelPath = "path/to/your/model.zip";
            options.EnablePredictionCaching = true;
        });
    }

    public void Configure(IApplicationBuilder app)
    {
        // Configure your plugin's middleware/services
    }
}
```

## 🤖 ML Model Development

### Custom ML Optimization Models

Qobuzarr's ML system can be extended with custom models:

#### 1. Basic ML Extension

```csharp
public class CustomMLQueryOptimizer : IPatternLearningEngine
{
    private readonly IQobuzLogger _logger;
    private readonly MLContext _mlContext;
    private ITransformer _model;

    public async Task<PredictionResult> PredictComplexityAsync(string artist, string album)
    {
        var features = ExtractFeatures(artist, album);
        var prediction = _model.Transform(features);
        
        return new PredictionResult
        {
            PredictedComplexity = MapToComplexity(prediction),
            Confidence = GetConfidence(prediction),
            Features = features
        };
    }

    public float GetConfidenceScore(string artist, string album, QueryComplexity complexity)
    {
        // Calculate confidence based on historical accuracy
        var historicalAccuracy = GetHistoricalAccuracy(artist, album, complexity);
        var modelConfidence = GetModelConfidence(artist, album);
        
        return (historicalAccuracy + modelConfidence) / 2.0f;
    }

    public async Task UpdateModelAsync(QueryResult actualResult)
    {
        // Online learning - update model with new data
        var trainingData = PrepareTrainingData(actualResult);
        await RetrainModelAsync(trainingData);
    }
}
```

#### 2. Advanced ML Pipeline

```csharp
public class AdvancedMLPipeline : IPatternLearningEngine
{
    private readonly MLContext _mlContext;
    private readonly IDataProcessor _dataProcessor;
    private readonly IFeatureExtractor _featureExtractor;

    // Multi-stage feature extraction
    private MLFeatureSet ExtractAdvancedFeatures(string artist, string album)
    {
        return new MLFeatureSet
        {
            // Text features
            ArtistLength = artist.Length,
            AlbumLength = album.Length,
            SpecialCharCount = CountSpecialChars(artist + album),
            
            // Semantic features
            ArtistPopularity = GetArtistPopularity(artist),
            GenreComplexity = GetGenreComplexity(artist),
            
            // Historical features
            PreviousSearchCount = GetPreviousSearches(artist, album),
            AverageResultCount = GetAverageResults(artist),
            
            // Contextual features
            TimeOfDay = DateTime.Now.Hour,
            DayOfWeek = (int)DateTime.Now.DayOfWeek,
            IsWeekend = IsWeekend()
        };
    }

    // Model ensemble for better predictions
    public async Task<PredictionResult> PredictComplexityAsync(string artist, string album)
    {
        var features = ExtractAdvancedFeatures(artist, album);
        
        // Use ensemble of models
        var predictions = new[]
        {
            await _primaryModel.PredictAsync(features),
            await _secondaryModel.PredictAsync(features),
            await _fallbackModel.PredictAsync(features)
        };

        return CombinePredictions(predictions);
    }
}
```

### Training Data Generation

Create training data for your ML models:

```csharp
public class TrainingDataGenerator
{
    public async Task<IEnumerable<MLTrainingRecord>> GenerateTrainingDataAsync(
        IEnumerable<SearchHistory> searchHistory)
    {
        var trainingData = new List<MLTrainingRecord>();

        foreach (var search in searchHistory)
        {
            var record = new MLTrainingRecord
            {
                // Input features
                Artist = search.Artist,
                Album = search.Album,
                ArtistLength = search.Artist.Length,
                AlbumLength = search.Album.Length,
                SpecialCharCount = CountSpecialChars(search.Artist + search.Album),
                
                // Labels (actual outcomes)
                ActualComplexity = DetermineActualComplexity(search.Results),
                ResultCount = search.Results.Count,
                SearchDuration = search.Duration,
                
                // Success metrics
                FoundExactMatch = search.Results.Any(r => r.IsExactMatch),
                FoundAnyMatch = search.Results.Any(),
                QualityScore = CalculateQualityScore(search.Results)
            };

            trainingData.Add(record);
        }

        return trainingData;
    }
}
```

### Model Deployment

Deploy your custom ML models:

```csharp
public class CustomMLModelDeployment
{
    public async Task DeployModelAsync(string modelPath)
    {
        // 1. Validate model
        await ValidateModelAsync(modelPath);
        
        // 2. Create backup of current model
        await BackupCurrentModelAsync();
        
        // 3. Deploy new model
        await SwapModelAsync(modelPath);
        
        // 4. Verify deployment
        await VerifyModelDeploymentAsync();
        
        _logger.LogInformation("ML model deployed successfully: {ModelPath}", modelPath);
    }

    private async Task ValidateModelAsync(string modelPath)
    {
        // Load model and test with sample data
        var mlContext = new MLContext();
        var model = mlContext.Model.Load(modelPath, out var modelSchema);
        
        // Run validation tests
        var testData = GenerateTestData();
        var predictions = model.Transform(testData);
        
        // Verify predictions are reasonable
        ValidatePredictions(predictions);
    }
}
```

## 🔐 Security Extensions

### Custom Security Providers

Extend Qobuzarr's security framework:

#### 1. Custom Credential Storage

```csharp
public class VaultCredentialProvider : ISecureCredentialProvider
{
    private readonly IVaultClient _vaultClient;
    private readonly IEncryptionService _encryption;

    public async Task<TCredential> GetCredentialAsync<TCredential>() where TCredential : class
    {
        var credentialType = typeof(TCredential).Name;
        var secretPath = $"qobuzarr/credentials/{credentialType}";
        
        var secret = await _vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(secretPath);
        
        var encryptedData = secret.Data.Data["credential"];
        var decryptedJson = await _encryption.DecryptAsync(encryptedData.ToString());
        
        return JsonSerializer.Deserialize<TCredential>(decryptedJson);
    }

    public async Task StoreCredentialAsync<TCredential>(TCredential credential) 
        where TCredential : class
    {
        var credentialType = typeof(TCredential).Name;
        var secretPath = $"qobuzarr/credentials/{credentialType}";
        
        var json = JsonSerializer.Serialize(credential);
        var encryptedJson = await _encryption.EncryptAsync(json);
        
        var secret = new Dictionary<string, object>
        {
            ["credential"] = encryptedJson,
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["type"] = credentialType
        };

        await _vaultClient.V1.Secrets.KeyValue.V2
            .WriteSecretAsync(secretPath, secret);
    }
}
```

#### 2. Security Validation Extensions

```csharp
public class CustomSecurityValidator : ISecurityValidator
{
    public async Task<SecurityValidationResult> ValidateRequestAsync(ApiRequest request)
    {
        var validations = new[]
        {
            ValidateRateLimit(request),
            ValidateInputSanitization(request),
            ValidateAuthorizationLevel(request),
            ValidateGeolocation(request),
            await ValidateRequestSignatureAsync(request)
        };

        return CombineValidationResults(validations);
    }

    private ValidationResult ValidateRateLimit(ApiRequest request)
    {
        var rateLimiter = GetRateLimiterForUser(request.UserId);
        
        if (!rateLimiter.TryConsume(1))
        {
            return ValidationResult.Failure("Rate limit exceeded");
        }

        return ValidationResult.Success;
    }

    private ValidationResult ValidateInputSanitization(ApiRequest request)
    {
        // Validate all input parameters
        foreach (var parameter in request.Parameters)
        {
            if (ContainsMaliciousContent(parameter.Value))
            {
                return ValidationResult.Failure($"Malicious content detected in {parameter.Key}");
            }
        }

        return ValidationResult.Success;
    }
}
```

### Security Monitoring Extensions

```csharp
public class SecurityMonitoringService : ISecurityMonitor
{
    private readonly IMetricsCollector _metrics;
    private readonly IAlertService _alerts;

    public async Task MonitorSecurityEventAsync(SecurityEvent securityEvent)
    {
        // Record metrics
        _metrics.Increment($"security.events.{securityEvent.Type}");
        
        // Analyze event
        var analysis = await AnalyzeSecurityEventAsync(securityEvent);
        
        if (analysis.ThreatLevel >= ThreatLevel.High)
        {
            await _alerts.SendAlertAsync(new SecurityAlert
            {
                Title = $"High threat level detected: {securityEvent.Type}",
                Description = analysis.Description,
                Severity = Severity.Critical,
                Timestamp = DateTimeOffset.UtcNow,
                SourceIp = securityEvent.SourceIp,
                UserId = securityEvent.UserId
            });
        }

        // Update security models
        await UpdateSecurityModelsAsync(securityEvent, analysis);
    }

    private async Task<SecurityAnalysis> AnalyzeSecurityEventAsync(SecurityEvent securityEvent)
    {
        // Use ML models to analyze the event
        var features = ExtractSecurityFeatures(securityEvent);
        var prediction = await _securityMLModel.PredictAsync(features);
        
        return new SecurityAnalysis
        {
            ThreatLevel = MapToThreatLevel(prediction.Probability),
            Description = GenerateDescription(securityEvent, prediction),
            RecommendedActions = GetRecommendedActions(prediction),
            Confidence = prediction.Confidence
        };
    }
}
```

## 🔍 Custom Indexer Development

### Building Custom Search Providers

Create custom search indexers for different music services:

```csharp
public class CustomMusicServiceIndexer : HttpIndexerBase<CustomIndexerSettings>
{
    public override string Name => "Custom Music Service";
    public override DownloadProtocol Protocol => DownloadProtocol.Unknown;

    public override IIndexerRequestGenerator GetRequestGenerator()
    {
        return new CustomRequestGenerator(Settings);
    }

    public override IParseIndexerResponse GetParser()
    {
        return new CustomResponseParser(Settings);
    }

    protected override async Task<IndexerCapabilities> GetCapabilitiesAsync()
    {
        return new IndexerCapabilities
        {
            MusicSearchParams = new List<MusicSearchParam>
            {
                MusicSearchParam.Q,
                MusicSearchParam.Artist,
                MusicSearchParam.Album,
                MusicSearchParam.Year
            }
        };
    }
}

public class CustomRequestGenerator : IIndexerRequestGenerator
{
    private readonly CustomIndexerSettings _settings;

    public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
    {
        var requests = new IndexerPageableRequestChain();
        
        // Build search URL
        var searchUrl = BuildSearchUrl(searchCriteria);
        requests.Add(GetPagedRequests(searchUrl));
        
        return requests;
    }

    private string BuildSearchUrl(MusicSearchCriteria searchCriteria)
    {
        var queryBuilder = new StringBuilder();
        
        if (searchCriteria.Artist.IsNotNullOrWhiteSpace())
        {
            queryBuilder.Append($"artist:\"{searchCriteria.Artist}\"");
        }
        
        if (searchCriteria.Album.IsNotNullOrWhiteSpace())
        {
            if (queryBuilder.Length > 0) queryBuilder.Append(" AND ");
            queryBuilder.Append($"album:\"{searchCriteria.Album}\"");
        }

        return $"{_settings.BaseUrl}/search?q={Uri.EscapeDataString(queryBuilder.ToString())}";
    }
}
```

### Custom Response Parser

```csharp
public class CustomResponseParser : IParseIndexerResponse
{
    private readonly CustomIndexerSettings _settings;

    public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
    {
        var releases = new List<ReleaseInfo>();
        
        try
        {
            var json = JsonDocument.Parse(indexerResponse.Content);
            var results = json.RootElement.GetProperty("results").EnumerateArray();
            
            foreach (var result in results)
            {
                var release = ParseRelease(result);
                if (release != null)
                {
                    releases.Add(release);
                }
            }
        }
        catch (Exception ex)
        {
            throw new IndexerException($"Error parsing response: {ex.Message}");
        }
        
        return releases;
    }

    private ReleaseInfo ParseRelease(JsonElement element)
    {
        return new ReleaseInfo
        {
            Title = GetElementValue(element, "title"),
            InfoUrl = GetElementValue(element, "url"),
            DownloadUrl = GetElementValue(element, "downloadUrl"),
            Guid = GetElementValue(element, "id"),
            Artist = GetElementValue(element, "artist"),
            Album = GetElementValue(element, "album"),
            Size = GetElementLong(element, "size"),
            PublishDate = GetElementDateTime(element, "releaseDate"),
            Categories = ParseCategories(element),
            DownloadVolumeFactor = 1.0,
            UploadVolumeFactor = 1.0
        };
    }
}
```

## 🧪 Testing Framework

### Plugin Testing Strategy

Comprehensive testing for your plugins:

#### 1. Unit Tests

```csharp
[TestFixture]
public class CustomMLOptimizerTests : TestFixtureBase
{
    private CustomMLQueryOptimizer _optimizer;
    private Mock<IQobuzLogger> _mockLogger;
    private MLContext _mlContext;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<IQobuzLogger>();
        _mlContext = new MLContext(seed: 1);
        _optimizer = new CustomMLQueryOptimizer(_mockLogger.Object, _mlContext);
    }

    [Test]
    public async Task PredictComplexityAsync_WithSimpleQuery_ReturnsLowComplexity()
    {
        // Arrange
        var artist = "Beatles";
        var album = "Abbey Road";

        // Act
        var result = await _optimizer.PredictComplexityAsync(artist, album);

        // Assert
        Assert.That(result.PredictedComplexity, Is.EqualTo(QueryComplexity.Simple));
        Assert.That(result.Confidence, Is.GreaterThan(0.7f));
    }

    [Test]
    public async Task UpdateModelAsync_WithNewData_UpdatesModelSuccessfully()
    {
        // Arrange
        var queryResult = new QueryResult
        {
            Artist = "Test Artist",
            Album = "Test Album",
            ActualComplexity = QueryComplexity.Complex,
            ResultCount = 50,
            Duration = TimeSpan.FromSeconds(2.5)
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(() => _optimizer.UpdateModelAsync(queryResult));
    }
}
```

#### 2. Integration Tests

```csharp
[TestFixture]
public class CustomIndexerIntegrationTests
{
    private CustomMusicServiceIndexer _indexer;
    private TestIndexerSettings _settings;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _settings = new TestIndexerSettings
        {
            BaseUrl = "https://api.custommusicservice.com",
            ApiKey = TestConfiguration.GetApiKey()
        };
        
        _indexer = new CustomMusicServiceIndexer(_settings);
    }

    [Test]
    public async Task Search_WithValidArtist_ReturnsResults()
    {
        // Arrange
        var searchCriteria = new MusicSearchCriteria
        {
            Artist = "Miles Davis"
        };

        // Act
        var results = await _indexer.SearchAsync(searchCriteria);

        // Assert
        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Artist, Contains.Substring("Miles Davis"));
    }

    [Test]
    public async Task Search_WithInvalidCredentials_ThrowsAuthenticationException()
    {
        // Arrange
        var invalidIndexer = new CustomMusicServiceIndexer(new TestIndexerSettings
        {
            ApiKey = "invalid-key"
        });

        var searchCriteria = new MusicSearchCriteria { Artist = "Test" };

        // Act & Assert
        Assert.ThrowsAsync<AuthenticationException>(
            () => invalidIndexer.SearchAsync(searchCriteria));
    }
}
```

#### 3. Performance Tests

```csharp
[TestFixture]
public class MLPerformanceTests
{
    [Test]
    [Timeout(5000)] // 5 second timeout
    public async Task PredictComplexity_Under5Seconds()
    {
        // Arrange
        var optimizer = new CustomMLQueryOptimizer();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await optimizer.PredictComplexityAsync("Test Artist", "Test Album");

        // Assert
        stopwatch.Stop();
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000));
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task BulkPredictions_ProcessesManyRequestsQuickly()
    {
        // Arrange
        var optimizer = new CustomMLQueryOptimizer();
        var testCases = GenerateTestCases(1000);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = testCases.Select(tc => optimizer.PredictComplexityAsync(tc.Artist, tc.Album));
        var results = await Task.WhenAll(tasks);

        // Assert
        stopwatch.Stop();
        Assert.That(results.Length, Is.EqualTo(1000));
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10000)); // Under 10 seconds
    }
}
```

## 🚀 Plugin Deployment

### Building and Packaging

Create deployment packages for your plugins:

```bash
# Build plugin for release
dotnet build --configuration Release

# Create NuGet package
dotnet pack --configuration Release --output ./packages

# Create deployment package
mkdir -p deploy/MyCustomPlugin
cp bin/Release/net6.0/*.dll deploy/MyCustomPlugin/
cp plugin.json deploy/MyCustomPlugin/
tar czf MyCustomPlugin.tar.gz -C deploy MyCustomPlugin/
```

### Plugin Installation Script

```bash
#!/bin/bash
# install-plugin.sh

PLUGIN_NAME="MyCustomPlugin"
PLUGIN_VERSION="1.0.0"
LIDARR_PLUGINS_DIR="/config/plugins"

echo "Installing $PLUGIN_NAME v$PLUGIN_VERSION..."

# Create plugin directory
mkdir -p "$LIDARR_PLUGINS_DIR/$PLUGIN_NAME"

# Extract plugin files
tar xzf "$PLUGIN_NAME-$PLUGIN_VERSION.tar.gz" -C "$LIDARR_PLUGINS_DIR"

# Set permissions
chmod -R 755 "$LIDARR_PLUGINS_DIR/$PLUGIN_NAME"

echo "Plugin installed successfully!"
echo "Please restart Lidarr to load the new plugin."
```

### CI/CD Pipeline for Plugins

GitHub Actions workflow for plugin development:

```yaml
# .github/workflows/plugin-ci.yml
name: Plugin CI/CD

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore --configuration Release
    
    - name: Test
      run: dotnet test --no-build --verbosity normal
    
    - name: Package plugin
      run: |
        mkdir -p packages
        dotnet pack --no-build --configuration Release --output packages
    
    - name: Upload artifacts
      uses: actions/upload-artifact@v3
      with:
        name: plugin-packages
        path: packages/*.nupkg

  integration-test:
    needs: build
    runs-on: ubuntu-latest
    
    services:
      lidarr:
        image: ghcr.io/hotio/lidarr:pr-plugins
        ports:
          - 8686:8686
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Download artifacts
      uses: actions/download-artifact@v3
      with:
        name: plugin-packages
        path: packages
    
    - name: Install plugin in test environment
      run: |
        # Install plugin in test Lidarr instance
        ./scripts/install-plugin-for-testing.sh
    
    - name: Run integration tests
      run: dotnet test tests/Integration/ --logger trx --results-directory TestResults
    
    - name: Publish test results
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: Integration Test Results
        path: TestResults/*.trx
        reporter: dotnet-trx
```

## 📚 Best Practices

### Plugin Development Guidelines

1. **Follow Lidarr Conventions**: Use Lidarr's existing patterns and interfaces
2. **Implement Defensive Programming**: Handle errors gracefully
3. **Use Dependency Injection**: Register services properly
4. **Write Comprehensive Tests**: Unit, integration, and performance tests
5. **Document Your Plugin**: Clear documentation for users and developers
6. **Performance Optimization**: Minimize resource usage and API calls
7. **Security First**: Validate inputs and secure credentials

### Code Quality Standards

```csharp
// Example of well-structured plugin code
public class ExamplePlugin : IQobuzarrPlugin
{
    private readonly ILogger<ExamplePlugin> _logger;
    private readonly IConfiguration _configuration;

    public ExamplePlugin(ILogger<ExamplePlugin> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Validate configuration
        var settings = _configuration.GetSection("ExamplePlugin").Get<ExamplePluginSettings>();
        ValidateSettings(settings);

        // Register services with proper lifetimes
        services.AddSingleton(settings);
        services.AddScoped<IExampleService, ExampleService>();
        services.AddTransient<IExampleRepository, ExampleRepository>();

        _logger.LogInformation("ExamplePlugin services configured successfully");
    }

    private void ValidateSettings(ExamplePluginSettings settings)
    {
        if (settings == null)
            throw new InvalidOperationException("ExamplePlugin configuration is required");
        
        if (string.IsNullOrEmpty(settings.ApiKey))
            throw new InvalidOperationException("ExamplePlugin ApiKey is required");
    }
}
```

## 🔗 Resources and Support

### Documentation
- **[[API Reference]]**: Complete API documentation
- **[[Architecture Overview]]**: System design details
- **[[Testing Guide]]**: Testing methodologies
- **[[Security Features]]**: Security implementation details

### Community
- **GitHub Repository**: [RicherTunes/qobuzarr](https://github.com/RicherTunes/qobuzarr)
- **GitHub Discussions**: Community support and feature requests
- **GitHub Issues**: Bug reports and feature requests

### Development Tools
- **Plugin Template**: Starter template for new plugins
- **Testing Framework**: Comprehensive testing utilities
- **Development Environment**: Docker-based development setup

---

*Ready to build your first plugin? Start with our [[Plugin Template]] or explore existing extensions in the repository.*