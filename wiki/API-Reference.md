# API Reference

Comprehensive reference for all public APIs, services, and interfaces provided by the Qobuzarr plugin.

## 📋 Overview

Qobuzarr provides a rich set of APIs for:
- **Authentication & Security**: Secure credential management and session handling
- **Search & Indexing**: ML-powered search optimization and indexing
- **Downloads**: High-performance download orchestration
- **Integration**: Lidarr plugin interfaces and CLI adapters
- **ML Optimization**: Query intelligence and performance optimization

## 🔐 Authentication API

### IQobuzAuthenticationService

Primary authentication interface for secure Qobuz API access.

```csharp
public interface IQobuzAuthenticationService
{
    Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials);
    Task<bool> ValidateSessionAsync(QobuzSession session);
    Task RefreshSessionAsync(QobuzSession session);
    Task<bool> IsAuthenticatedAsync();
    event EventHandler<AuthenticationEventArgs> AuthenticationStateChanged;
}
```

#### Key Methods

##### AuthenticateAsync
```csharp
Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
```

Authenticates with Qobuz API using secure credential handling.

**Parameters:**
- `credentials` - User credentials with automatic security features

**Security Features:**
- Automatic credential masking in logs
- Secure memory handling for passwords
- Session token protection with encryption
- Automatic credential cleanup

**Example:**
```csharp
var credentials = new QobuzCredentials
{
    Email = \"user@example.com\",
    Password = securePassword, // Handled securely
    AppId = await GetDynamicAppIdAsync() // Auto-fetched
};

try
{
    var session = await authService.AuthenticateAsync(credentials);
    Console.WriteLine($\"Authenticated as: {session.User.DisplayName}\");
}
catch (QobuzAuthenticationException ex)
{
    Console.WriteLine($\"Authentication failed: {ex.Message}\");
}
```

##### ValidateSessionAsync
```csharp
Task<bool> ValidateSessionAsync(QobuzSession session)
```

Validates session integrity with automatic renewal.

**Returns:** `true` if session is valid and active

### SecureCredentialManager

**New in v0.0.12**: Enterprise-grade secure credential management.

```csharp
public class SecureCredentialManager : IDisposable
{
    Task<T> UseSecureCredentialAsync<T>(string key, Func<string, Task<T>> func);
    void StoreSecureCredential(string key, string credential);
    bool HasSecureCredential(string key);
    string MaskSensitiveData(string sensitive);
    Task ClearAllCredentialsAsync();
}
```

**Features:**
- **Memory Protection**: SecureString integration on Windows
- **Automatic Cleanup**: Zero memory footprint after use
- **Concurrent Access**: Thread-safe credential operations
- **Security Validation**: Built-in credential policy enforcement

**Example:**
```csharp
using var credentialManager = new SecureCredentialManager(logger);

// Store credential securely
credentialManager.StoreSecureCredential(\"qobuz_password\", userPassword);

// Use with automatic cleanup
var session = await credentialManager.UseSecureCredentialAsync(\"qobuz_password\", 
    async password => await qobuzApi.AuthenticateAsync(email, password));

// Credential is automatically cleared from memory
```

## 🤖 ML Optimization API

### IPatternLearningEngine

Core interface for ML-powered query optimization.

```csharp
public interface IPatternLearningEngine
{
    Task<PredictionResult> PredictComplexityAsync(string artist, string album);
    float GetConfidenceScore(string artist, string album, QueryComplexity complexity);
    Task UpdateModelAsync(QueryResult actualResult);
    MLPerformanceMetrics GetStatistics();
    Task<bool> IsModelHealthyAsync();
}
```

#### Key Methods

##### PredictComplexityAsync
```csharp
Task<PredictionResult> PredictComplexityAsync(string artist, string album)
```

Predicts search complexity using ML models for optimization.

**Returns:** Prediction with confidence score and recommended strategy

**Example:**
```csharp
var prediction = await mlEngine.PredictComplexityAsync(\"Miles Davis\", \"Kind of Blue\");

Console.WriteLine($\"Predicted complexity: {prediction.Complexity}\");
Console.WriteLine($\"Confidence: {prediction.Confidence:P1}\");
Console.WriteLine($\"Recommended strategy: {prediction.RecommendedStrategy}\");
```

### CompiledMLQueryOptimizer

**Production-ready ML optimization** with 65.8% API call reduction.

```csharp
public class CompiledMLQueryOptimizer : IPatternLearningEngine
{
    public MLPerformanceMetrics GetStatistics()
    public async Task<QueryOptimizationResult> OptimizeQueryAsync(string artist, string album)
    public async Task<bool> ShouldUseOptimizedQueryAsync(string artist, string album)
}
```

**Performance Metrics:**
- **API Call Reduction**: 65.8% average reduction
- **Accuracy**: 98.485% classification accuracy  
- **Speed**: <50ms prediction time
- **Memory**: <10MB model footprint

**Example:**
```csharp
var optimizer = serviceProvider.GetService<CompiledMLQueryOptimizer>();

var result = await optimizer.OptimizeQueryAsync(\"The Beatles\", \"Abbey Road\");
if (result.ShouldOptimize)
{
    // Use optimized search strategy
    var searchResults = await searchService.SearchOptimizedAsync(result.OptimizedQuery);
}
```

## 🔍 Indexer API

### QobuzIndexer

Main indexer implementation with ML-powered search optimization.

```csharp
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
{
    public override string Name { get; }
    public override DownloadProtocol Protocol { get; }
    
    public override IIndexerRequestGenerator GetRequestGenerator()
    public override IParseIndexerResponse GetParser()
    
    // ML-enhanced search
    public async Task<IList<ReleaseInfo>> SearchWithOptimizationAsync(MusicSearchCriteria searchCriteria)
    public async Task<SearchStatistics> GetSearchStatisticsAsync()
}
```

### SmartQueryStrategy

**New in v0.0.12**: Intelligent query adaptation and optimization.

```csharp
public class SmartQueryStrategy : ISmartQueryStrategy
{
    Task<QueryOptimizationResult> OptimizeQueryAsync(string artist, string album);
    QueryComplexity ClassifyComplexity(string artist, string album);
    bool ShouldUseOptimization(string query);
    Task<SearchStrategy> GetBestStrategyAsync(MusicSearchCriteria criteria);
}
```

**Features:**
- **Adaptive Search**: Adjusts strategy based on content type
- **Progressive Fallback**: Multiple search strategies with automatic fallback
- **Context Awareness**: Considers search history and patterns
- **Performance Optimization**: Minimizes API calls while maximizing results

## 📥 Download API

### IDownloadOrchestrator

High-level download coordination and management.

```csharp
public interface IDownloadOrchestrator
{
    Task<DownloadSummary> ProcessDownloadAsync(DownloadRequest request);
    Task<BatchDownloadResult> ProcessBatchDownloadAsync(IEnumerable<DownloadRequest> requests);
    Task<DownloadStatus> GetDownloadStatusAsync(string downloadId);
    
    event EventHandler<DownloadProgressEventArgs> ProgressUpdate;
    event EventHandler<DownloadCompletedEventArgs> DownloadCompleted;
}
```

### QobuzDownloadClient

Lidarr download client implementation.

```csharp
public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>
{
    public override string Name { get; }
    public override DownloadProtocol Protocol { get; }
    
    protected override string AddFromMagnetLink(RemoteEpisode remoteEpisode, string hash, string magnetLink)
    protected override string AddFromNzbFile(RemoteEpisode remoteEpisode, string filename, byte[] fileContent)
    protected override void RemoveItem(DownloadClientItem item, bool deleteData)
    protected override DownloadClientInfo GetStatus()
    protected override List<DownloadClientItem> GetItems()
}
```

### Advanced Download Features

#### ConcurrencyManager
```csharp
public class ConcurrencyManager : IConcurrencyManager
{
    Task<IDisposable> AcquireSlotAsync(string category, CancellationToken cancellationToken);
    Task<ConcurrencyStats> GetStatisticsAsync();
    void ConfigureLimits(string category, int maxConcurrency);
}
```

#### QualityFallbackProvider
```csharp
public class QualityFallbackProvider : IQualityFallbackProvider
{
    Task<QobuzAudioQuality[]> GetFallbackQualitiesAsync(QobuzAudioQuality requestedQuality);
    bool IsQualityAvailable(QobuzTrack track, QobuzAudioQuality quality);
    Task<QobuzAudioQuality> DetectBestAvailableQualityAsync(QobuzTrack track);
}
```

## 🔧 Configuration API

### QobuzIndexerSettings

Configuration model for the indexer component.

```csharp
public class QobuzIndexerSettings : IndexerSettingsBase<QobuzIndexerSettings>
{
    // Authentication
    [FieldDefinition(1, Label = \"Email\", Type = FieldType.Email)]
    public string Email { get; set; }
    
    [FieldDefinition(2, Label = \"Password\", Type = FieldType.Password)]
    public string Password { get; set; }
    
    // Search Configuration  
    [FieldDefinition(3, Label = \"Search Limit\", Type = FieldType.Number, Advanced = true)]
    public int SearchLimit { get; set; } = 100;
    
    // Quality Filters
    [FieldDefinition(4, Label = \"Minimum Quality\", Type = FieldType.Select)]
    public int MinimumQuality { get; set; } = (int)QobuzAudioQuality.Lossless;
    
    // ML Optimization
    [FieldDefinition(5, Label = \"Enable Query Intelligence\", Type = FieldType.Checkbox, Advanced = true)]
    public bool EnableQueryIntelligence { get; set; } = true;
    
    // Advanced Features
    [FieldDefinition(6, Label = \"Enable Adaptive Rate Limiting\", Type = FieldType.Checkbox, Advanced = true)]  
    public bool EnableAdaptiveRateLimiting { get; set; } = true;
}
```

### QobuzDownloadSettings

Configuration for the download client.

```csharp
public class QobuzDownloadSettings : DownloadClientSettingsBase<QobuzDownloadSettings>
{
    [FieldDefinition(1, Label = \"Download Path\", Type = FieldType.Path)]
    public string DownloadPath { get; set; }
    
    [FieldDefinition(2, Label = \"Concurrent Downloads\", Type = FieldType.Number)]
    public int ConcurrentDownloads { get; set; } = 5;
    
    [FieldDefinition(3, Label = \"Preferred Quality\", Type = FieldType.Select)]
    public int PreferredQuality { get; set; } = (int)QobuzAudioQuality.HiRes;
}
```

## 📊 Performance & Monitoring API

### MLPerformanceMetrics

Comprehensive ML optimization statistics.

```csharp
public class MLPerformanceMetrics
{
    public float ApiCallReductionPercentage { get; set; } // 65.8% average
    public float AccuracyPercentage { get; set; } // 98.485% average
    public TimeSpan AveragePredictionTime { get; set; } // <50ms average
    public long TotalPredictions { get; set; }
    public long CorrectPredictions { get; set; }
    public Dictionary<QueryComplexity, ClassificationStats> ComplexityBreakdown { get; set; }
}
```

### AdaptiveRateLimiter

**93x performance improvement** through intelligent rate management.

```csharp
public class AdaptiveRateLimiter : IRateLimiter
{
    Task<bool> TryExecuteAsync(Func<Task> action, string category);
    Task<RateLimitStats> GetStatisticsAsync();
    void ConfigureRateLimit(string category, TimeSpan period, int maxRequests);
    event EventHandler<RateLimitEventArgs> RateLimitAdjusted;
}
```

**Features:**
- **Dynamic Scaling**: Automatically adjusts rate limits based on API responses
- **Category-Based Limiting**: Different limits for different operation types  
- **Predictive Backoff**: ML-powered prediction of optimal request rates
- **Statistics Collection**: Detailed performance metrics

## 🧪 Testing API

### ITestableServiceRegistry

**New in v0.0.12**: Comprehensive testing framework for plugin components.

```csharp
public interface ITestableServiceRegistry
{
    void RegisterTestDouble<TInterface, TImplementation>() where TImplementation : class, TInterface;
    void RegisterTestInstance<TInterface>(TInterface instance);
    TInterface GetTestService<TInterface>();
    void ResetAllTestDoubles();
}
```

### TestabilityExtensions

Extension methods for testing support.

```csharp
public static class TestabilityExtensions
{
    public static IServiceCollection AddTestability(this IServiceCollection services);
    public static IServiceCollection AddMockServices(this IServiceCollection services);
    public static void EnableTestMode(this IConfiguration configuration);
}
```

**Example:**
```csharp
// In test setup
services.AddTestability();
services.RegisterTestDouble<IQobuzApiClient, MockQobuzApiClient>();

// In test
var mockClient = serviceRegistry.GetTestService<IQobuzApiClient>() as MockQobuzApiClient;
mockClient.SetupSearchResponse(artist, album, expectedResults);
```

## 📈 Quality Management API

### IntelligentQualityDetector

**New in v0.0.12**: AI-powered quality assessment and optimization.

```csharp
public class IntelligentQualityDetector : IQualityDetector
{
    Task<QualityAssessment> AssessQualityAsync(QobuzTrack track);
    Task<QualityRecommendation> GetRecommendationAsync(QualityPreferences preferences);
    Task<bool> IsQualityOptimalAsync(QobuzAudioQuality quality, QobuzTrack track);
    QualityMetrics AnalyzeQualityTrends(IEnumerable<QobuzTrack> tracks);
}
```

## 🔗 Integration API

### CLI Integration Services

Services for bridging plugin functionality with CLI interfaces.

#### PluginHost
```csharp
public class PluginHost : IPluginHost
{
    Task<SearchResult> SearchAsync(string artist, string album, SearchOptions options);
    Task<DownloadResult> DownloadAsync(string itemId, DownloadOptions options);
    Task<AuthenticationResult> AuthenticateAsync(AuthenticationOptions options);
    
    // Statistics and monitoring
    Task<PluginStatistics> GetStatisticsAsync();
    Task<HealthStatus> GetHealthStatusAsync();
}
```

#### ModelConverter
```csharp
public class ModelConverter : IModelConverter
{
    // Convert between CLI and plugin models
    CliSearchResult ToCliModel(QobuzSearchResult pluginResult);
    QobuzSearchCriteria FromCliModel(CliSearchCriteria cliCriteria);
    
    // Batch conversions
    IEnumerable<TOutput> ConvertAll<TInput, TOutput>(IEnumerable<TInput> items);
}
```

## ⚠️ Error Handling

### Exception Types

#### QobuzApiException
```csharp
public class QobuzApiException : Exception
{
    public int StatusCode { get; }
    public string ApiError { get; }
    public string RequestUrl { get; }
    public QobuzErrorResponse ErrorResponse { get; }
}
```

#### QobuzAuthenticationException
```csharp
public class QobuzAuthenticationException : QobuzApiException
{
    public AuthenticationFailureReason Reason { get; }
    public TimeSpan? RetryAfter { get; }
}
```

#### QobuzSearchException
```csharp
public class QobuzSearchException : QobuzApiException
{
    public string SearchQuery { get; }
    public SearchFailureReason Reason { get; }
}
```

### Error Handling Patterns

```csharp
try
{
    var results = await indexer.SearchAsync(criteria);
}
catch (QobuzAuthenticationException ex) when (ex.Reason == AuthenticationFailureReason.InvalidCredentials)
{
    // Handle authentication issues
    logger.LogWarning(\"Authentication failed: {Message}\", ex.Message);
    throw;
}
catch (QobuzSearchException ex) when (ex.StatusCode == 429)
{
    // Handle rate limiting
    await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(60));
    // Retry logic here
}
catch (QobuzApiException ex)
{
    // Handle general API errors
    logger.LogError(ex, \"API error: {StatusCode} - {Message}\", ex.StatusCode, ex.Message);
    throw;
}
```

## 🔧 Extension Points

### Custom Service Registration

```csharp
// In your plugin or custom code
public void ConfigureServices(IServiceCollection services)
{
    // Register custom ML models
    services.AddScoped<IPatternLearningEngine, CustomMLOptimizer>();
    
    // Register custom authentication providers
    services.AddSingleton<IQobuzAuthenticationProvider, CustomAuthProvider>();
    
    // Register custom search strategies
    services.AddTransient<ISmartQueryStrategy, CustomSearchStrategy>();
    
    // Configure custom settings
    services.Configure<CustomSettings>(Configuration.GetSection(\"Custom\"));
}
```

### Custom Interface Implementations

Implement core interfaces to extend functionality:

```csharp
public class CustomQueryOptimizer : IPatternLearningEngine
{
    public async Task<PredictionResult> PredictComplexityAsync(string artist, string album)
    {
        // Your custom ML logic here
        return new PredictionResult
        {
            Complexity = QueryComplexity.Simple,
            Confidence = 0.95f,
            RecommendedStrategy = \"optimized\"
        };
    }
}
```

## 📋 Version Compatibility

### API Stability

- **Core APIs**: Stable across minor versions
- **Extension APIs**: Stable within major versions  
- **Internal APIs**: May change between minor versions
- **Experimental APIs**: Marked with `[Experimental]` attribute

### Versioning Information

```csharp
public static class QobuzarrVersion
{
    public static readonly Version Current = new Version(0, 0, 12);
    public static readonly string ApiVersion = \"v1\";
    public static readonly DateTime BuildDate = new DateTime(2025, 1, 15);
}
```

---

*This API reference covers the complete public surface of Qobuzarr v0.0.12. For implementation examples and usage patterns, see the [[Usage Examples]] and [[Plugin Development]] guides.*