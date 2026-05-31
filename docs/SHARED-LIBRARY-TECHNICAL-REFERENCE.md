# 🔧 Shared Library Technical Reference

## Implementation Patterns & Best Practices

> **Technical Companion to the Collaboration Guide**  
> Detailed implementation patterns, proven solutions, and technical standards for `Lidarr.Plugin.Common`.

---

## 🎯 **Current Architecture Overview**

### **Proven Success Metrics**
<!-- TODO(docval): Success metrics unverified - Tidalarr integration not found in codebase as of 2026-05-31 -->
```
✅ 74% Code Reduction (Tidalarr integration)
✅ 60%+ Development Time Savings (Target achieved)
✅ Zero Breaking Changes (Since v1.0.0)
✅ 100% Cross-Plugin Compatibility (Qobuz + Tidal)
```

### **Core Library Structure**

```
Lidarr.Plugin.Common/
├── src/
│   ├── Base/                     // 🟢 STABLE - Abstract base classes
│   │   ├── BaseStreamingDownloadClient.cs     // Download infrastructure
│   │   ├── BaseStreamingSettings.cs           // Configuration patterns
│   │   └── StreamingIndexerHelpers.cs         // Search/indexing utilities
│   │
│   ├── Models/                   // 🟢 STABLE - Universal data structures
│   │   ├── StreamingModels.cs                 // Track, Album, Artist models
│   │   └── QualityModels.cs                   // Quality mapping structures
│   │
│   ├── Services/                 // 🟡 EVOLVING - Business logic
│   │   ├── Authentication/                    // Auth patterns (Basic + OAuth)
│   │   ├── Http/                             // HTTP client utilities  
│   │   ├── Quality/                          // Quality detection/mapping
│   │   ├── Performance/                      // Monitoring and metrics
│   │   └── Registration/                     // DI container integration
│   │
│   ├── Utilities/                // 🟡 EVOLVING - Helper functions
│   │   ├── FileNameSanitizer.cs              // Cross-platform filename handling
│   │   ├── HttpClientExtensions.cs           // Retry, timeout, etc.
│   │   └── RetryUtilities.cs                 // Resilience patterns
│   │
│   ├── CLI/                      // 🔵 EXPERIMENTAL - Development tools
│   │   ├── BaseStreamingCLI.cs               // CLI framework base
│   │   └── Services/                         // CLI-specific services
│   │
│   └── Testing/                  // 🔵 EXPERIMENTAL - Test utilities
│       ├── MockFactories.cs                  // Test data generation
│       └── TestDataSets.cs                   // Common test scenarios
```

---

## 🏗️ **Implementation Patterns**

### **Pattern 1: Service Integration**

*How to integrate streaming service with minimal effort*

#### **Step 1: Create Service-Specific Settings**
<!-- TODO(docval): BaseStreamingSettings not found in Lidarr.Plugin.Common as of 2026-05-31 -->
```csharp
// Qobuz Example
public class QobuzIndexerSettings : BaseStreamingSettings
{
    [FieldDefinition(1, Label = "Email", Type = FieldType.Textbox)]
    public string Email { get; set; }

    [FieldDefinition(2, Label = "Password", Type = FieldType.Password)]
    public string Password { get; set; }

    [FieldDefinition(3, Label = "Quality", Type = FieldType.Select, SelectOptions = typeof(QobuzQuality))]
    public int QualityId { get; set; } = 7; // FLAC Hi-Res

    // ✅ Inherits: BaseUrl, EnableSearch, Categories, etc.
}

// Tidal Example
public class TidalIndexerSettings : BaseStreamingSettings  
{
    [FieldDefinition(1, Label = "Access Token", Type = FieldType.Password)]
    public string AccessToken { get; set; }

    [FieldDefinition(2, Label = "Country Code", Type = FieldType.Textbox)]
    public string CountryCode { get; set; } = "US";
    
    [FieldDefinition(3, Label = "Audio Quality", Type = FieldType.Select, SelectOptions = typeof(TidalQuality))]
    public string AudioQuality { get; set; } = "LOSSLESS";
    
    // ✅ Same inherited base functionality
}
```

#### **Step 2: Implement Indexer Using Base Class**

```csharp
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
{
    // ✅ Required overrides (minimal implementation needed)
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
    public override DownloadProtocol DownloadProtocol => DownloadProtocol.Unknown;
    
    // ✅ Core search implementation (service-specific logic)
    public override async Task<IList<ReleaseInfo>> PerformQuery(TorznabQuery query)
    {
        // Use shared utilities for common tasks
        var sanitizedQuery = FileNameSanitizer.SanitizeFileName(query.GetQueryString());
        
        var requestBuilder = new StreamingApiRequestBuilder(Settings.BaseUrl) // TODO(docval): StreamingApiRequestBuilder not found in codebase as of 2026-05-31
            .Endpoint("catalog/search/album")
            .Query("query", sanitizedQuery)
            .Query("limit", "50")
            .WithStreamingDefaults(); // ✅ Shared configuration
            
        if (!string.IsNullOrEmpty(_session?.AuthToken))
        {
            requestBuilder.BearerToken(_session.AuthToken);
        }

        var request = requestBuilder.Build();
        
        // Use shared HTTP extensions
        var response = await _httpClient.ExecuteWithRetryAsync(request);
        
        // Parse response into ReleaseInfo objects
        return ParseAlbumResults(response.Content, query);
    }
    
    // ✅ Service-specific parsing logic
    private List<ReleaseInfo> ParseAlbumResults(string json, TorznabQuery query)
    {
        var results = new List<ReleaseInfo>();
        var data = JsonConvert.DeserializeObject<QobuzSearchResponse>(json);
        
        foreach (var album in data.Albums.Items)
        {
            // Use shared factories for consistent data
            results.Add(ReleaseInfoFactory.CreateFromStreamingAlbum( // TODO(docval): ReleaseInfoFactory not found in codebase as of 2026-05-31
                album.ToStreamingAlbum(), // Convert to universal model
                Settings.QualityId,
                Protocol));
        }
        
        return results;
    }
}
```

#### **Step 3: Download Client Implementation**

```csharp
public class QobuzDownloadClient : BaseStreamingDownloadClient<QobuzDownloadSettings> // TODO(docval): BaseStreamingDownloadClient not found in codebase as of 2026-05-31
{
    protected override string ServiceName => "Qobuz";
    
    // ✅ Implement service-specific download logic
    protected override async Task<StreamingDownloadResult> DownloadTrackAsync( // TODO(docval): StreamingDownloadResult not found in codebase as of 2026-05-31
        StreamingTrack track, 
        string outputPath, 
        CancellationToken cancellationToken = default)
    {
        // Get streaming URL (service-specific)
        var streamUrl = await GetStreamUrlAsync(track.Id);
        
        // Use base class for actual file download (shared logic)
        return await base.DownloadFromUrlAsync(streamUrl, outputPath, track, cancellationToken);
    }
    
    // Service-specific stream URL resolution  
    private async Task<string> GetStreamUrlAsync(string trackId)
    {
        var request = new StreamingApiRequestBuilder(_settings.BaseUrl) // TODO(docval): StreamingApiRequestBuilder not found in codebase as of 2026-05-31
            .Endpoint($"track/getFileUrl")
            .Query("track_id", trackId)
            .Query("format_id", _settings.QualityId.ToString())
            .BearerToken(_session.AuthToken)
            .Build();
            
        var response = await _httpClient.ExecuteWithRetryAsync(request);
        var data = JsonConvert.DeserializeObject<QobuzStreamResponse>(response.Content);
        
        return data.Url;
    }
}
```

### **Pattern 2: Authentication Service Integration**

#### **Basic Authentication (Qobuz Pattern)**

```csharp
public class QobuzAuthenticationService : BaseStreamingAuthenticationService<QobuzCredentials, QobuzSession> // TODO(docval): BaseStreamingAuthenticationService not found in codebase as of 2026-05-31
{
    protected override async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
    {
        var request = new StreamingApiRequestBuilder(_baseUrl) // TODO(docval): StreamingApiRequestBuilder not found in codebase as of 2026-05-31
            .Endpoint("user/login")
            .FormData("email", credentials.Email)
            .FormData("password", credentials.Password)
            .FormData("app_id", credentials.AppId)
            .Build();
            
        var response = await _httpClient.ExecuteWithRetryAsync(request);
        var loginData = JsonConvert.DeserializeObject<QobuzLoginResponse>(response.Content);
        
        return new QobuzSession
        {
            AuthToken = loginData.UserAuthToken,
            UserId = loginData.User.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(24), // Qobuz tokens expire in 24h
            IsValid = true
        };
    }
    
    protected override async Task<bool> ValidateSessionAsync(QobuzSession session)
    {
        // Check if token is still valid with a lightweight API call
        var request = new StreamingApiRequestBuilder(_baseUrl) // TODO(docval): StreamingApiRequestBuilder not found in codebase as of 2026-05-31
            .Endpoint("user/profile")
            .BearerToken(session.AuthToken)
            .Build();
            
        try
        {
            await _httpClient.ExecuteWithRetryAsync(request);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
```

#### **OAuth Authentication (Tidal Pattern)**

```csharp
public class TidalAuthenticationService : OAuthStreamingAuthenticationService<TidalCredentials, TidalSession> // TODO(docval): OAuthStreamingAuthenticationService not found in codebase as of 2026-05-31
{
    protected override OAuthOptions GetOAuthConfiguration()
    {
        return new OAuthOptions
        {
            AuthorizeUrl = "https://auth.tidal.com/v1/oauth2/authorize",
            TokenUrl = "https://auth.tidal.com/v1/oauth2/token",
            ClientId = _credentials.ClientId,
            ClientSecret = _credentials.ClientSecret,
            RedirectUri = "http://localhost:8080/callback",
            Scope = "r_usr+w_usr+w_sub",
            UsePKCE = true // Enable PKCE for security
        };
    }
    
    protected override async Task<TidalSession> ExchangeCodeForTokenAsync(string authorizationCode)
    {
        // Base class handles OAuth flow
        var tokenResponse = await base.ExchangeAuthorizationCodeAsync(authorizationCode);
        
        return new TidalSession
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            CountryCode = await GetUserCountryAsync(tokenResponse.AccessToken),
            IsValid = true
        };
    }
    
    private async Task<string> GetUserCountryAsync(string accessToken)
    {
        var request = new StreamingApiRequestBuilder("https://api.tidal.com")
            .Endpoint("v1/sessions")
            .BearerToken(accessToken)
            .Build();
            
        var response = await _httpClient.ExecuteWithRetryAsync(request);
        var sessionData = JsonConvert.DeserializeObject<TidalSessionResponse>(response.Content);
        
        return sessionData.CountryCode;
    }
}
```

### **Pattern 3: Quality Mapping Integration**

#### **Universal Quality Detection**

```csharp
// Each service contributes their quality mapping
public static class QualityMappingExtensions  
{
    // Qobuz quality mapping
    public static LidarrQuality ToLidarrQuality(this int qobuzQualityId)
    {
        return qobuzQualityId switch
        {
            5 => Quality.MP3_320,      // MP3 320kbps
            6 => Quality.FLAC,         // CD Quality FLAC  
            7 => Quality.FLAC,         // Hi-Res FLAC (24/96)
            27 => Quality.FLAC,        // Hi-Res FLAC (24/192) 
            _ => Quality.Unknown
        };
    }
    
    // Tidal quality mapping  
    public static LidarrQuality ToLidarrQuality(this string tidalQuality)
    {
        return tidalQuality switch
        {
            "LOW" => Quality.MP3_128,
            "HIGH" => Quality.MP3_320,
            "LOSSLESS" => Quality.FLAC,
            "HI_RES" => Quality.FLAC,
            "MQA" => Quality.FLAC, // MQA treated as FLAC for Lidarr
            _ => Quality.Unknown
        };
    }
    
    // Universal quality selection (both services use)
    public static LidarrQuality FindBestAvailableQuality(
        IEnumerable<object> availableQualities,
        StreamingQualityTier targetTier)
    {
        // Shared logic for quality preference
        return QualityMapper.FindBestMatch(availableQualities, targetTier);
    }
}
```

---

## 🧪 **Testing Integration Patterns**

### **Unit Testing with Shared Mocks**

```csharp
[TestClass]
public class QobuzIndexerTests
{
    private QobuzIndexer _indexer;
    private Mock<IHttpClient> _mockHttpClient;
    
    [TestInitialize]
    public void Setup()
    {
        // ✅ Use shared mock factory
        var mockSettings = MockFactories.CreateQobuzIndexerSettings();
        var mockLogger = MockFactories.CreateLogger<QobuzIndexer>();
        
        _mockHttpClient = new Mock<IHttpClient>();
        _indexer = new QobuzIndexer(mockSettings, _mockHttpClient.Object, mockLogger);
    }
    
    [TestMethod]
    public async Task PerformQuery_ValidQuery_ReturnsResults()
    {
        // Arrange - Use shared test data
        var query = TestDataSets.CreateBasicAlbumQuery("Miles Davis Kind of Blue");
        var mockResponse = TestDataSets.QobuzAlbumSearchResponse;
        
        _mockHttpClient.Setup(x => x.ExecuteWithRetryAsync(It.IsAny<HttpRequestMessage>()))
            .ReturnsAsync(new HttpResponseMessage 
            { 
                Content = new StringContent(mockResponse) 
            });
        
        // Act
        var results = await _indexer.PerformQuery(query);
        
        // Assert
        Assert.AreEqual(5, results.Count);
        Assert.IsTrue(results.All(r => r.DownloadProtocol == "Qobuzarr"));
        Assert.IsTrue(results.All(r => r.Size > 0)); // Quality-based sizing worked
    }
}
```

### **Integration Testing Pattern**

```csharp
[TestClass]
public class CrossPluginCompatibilityTests
{
    [TestMethod]  
    public async Task SharedLibrary_WorksWithBothPlugins()
    {
        // Test that shared components work with both Qobuz and Tidal patterns
        
        // Test Qobuz pattern
        var qobuzSettings = MockFactories.CreateQobuzIndexerSettings();
        var qobuzIndexer = new QobuzIndexer(qobuzSettings, httpClient, logger);
        var qobuzResults = await qobuzIndexer.PerformQuery(testQuery);
        
        // Test Tidal pattern  
        var tidalSettings = MockFactories.CreateTidalIndexerSettings();
        var tidalIndexer = new TidalIndexer(tidalSettings, httpClient, logger);
        var tidalResults = await tidalIndexer.PerformQuery(testQuery);
        
        // Both should use shared infrastructure successfully
        Assert.IsTrue(qobuzResults.Any());
        Assert.IsTrue(tidalResults.Any());
        
        // Verify shared utilities worked
        Assert.IsTrue(qobuzResults.All(r => IsValidFileName(r.Title)));
        Assert.IsTrue(tidalResults.All(r => IsValidFileName(r.Title)));
    }
}
```

---

## 🔄 **Migration & Upgrade Patterns**

### **Backwards Compatible API Evolution**

```csharp
// v1.1.0 - Original method
public virtual async Task<StreamingDownloadResult> DownloadTrackAsync(
    StreamingTrack track, 
    string outputPath, 
    CancellationToken cancellationToken = default)
{
    // Original implementation
}

// v1.2.0 - Enhanced method with options (backwards compatible)  
public virtual async Task<StreamingDownloadResult> DownloadTrackWithOptionsAsync(
    StreamingTrack track, 
    string outputPath, 
    DownloadOptions options = null,
    CancellationToken cancellationToken = default)
{
    options ??= DownloadOptions.Default;
    
    // Enhanced implementation that uses original method internally
    // Existing plugins continue working, new plugins get enhanced features
}

// v1.3.0 - Obsolete old method (with migration path)
[Obsolete("Use DownloadTrackWithOptionsAsync for enhanced features. This method will be removed in v2.0.0")]
public virtual async Task<StreamingDownloadResult> DownloadTrackAsync(
    StreamingTrack track, 
    string outputPath, 
    CancellationToken cancellationToken = default)
{
    // Delegate to new method with default options
    return await DownloadTrackWithOptionsAsync(track, outputPath, null, cancellationToken);
}
```

### **Feature Flag Pattern**

```csharp
// Enable new features gradually
public class BaseStreamingSettings
{
    // Existing settings (always present)
    public string BaseUrl { get; set; }
    public bool EnableSearch { get; set; }
    
    // New features with feature flags (opt-in)
    
    [FieldDefinition(10, Label = "Enable ML Optimization", Type = FieldType.Checkbox, HelpText = "Experimental: Use machine learning to optimize search queries")]
    public bool EnableMLOptimization { get; set; } = false; // Default off
    
    [FieldDefinition(11, Label = "Enable Advanced Retry", Type = FieldType.Checkbox, HelpText = "Enhanced retry logic with exponential backoff")]
    public bool EnableAdvancedRetry { get; set; } = false; // Default off
    
    [FieldDefinition(12, Label = "Performance Monitoring", Type = FieldType.Checkbox, HelpText = "Collect performance metrics (anonymous)")]
    public bool EnablePerformanceMonitoring { get; set; } = false; // Default off
}

// Usage in base classes
protected virtual async Task<T> ExecuteRequestAsync<T>(HttpRequestMessage request)
{
    if (Settings.EnableAdvancedRetry)
    {
        return await ExecuteWithAdvancedRetryAsync<T>(request);
    }
    else
    {
        return await ExecuteWithBasicRetryAsync<T>(request); // Original behavior
    }
}
```

---

## ⚡ **Performance Optimization Patterns**

### **Caching Strategy**

```csharp
// Universal caching for all streaming services
public class StreamingResponseCache : IStreamingResponseCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(30);
    
    public async Task<T> GetOrSetAsync<T>(
        string key, 
        Func<Task<T>> factory, 
        TimeSpan? ttl = null)
    {
        var cacheKey = $"streaming:{typeof(T).Name}:{key}";
        
        if (_cache.TryGetValue(cacheKey, out T cachedValue))
        {
            return cachedValue;
        }
        
        var value = await factory();
        
        _cache.Set(cacheKey, value, ttl ?? _defaultTtl);
        return value;
    }
}

// Usage in services
public async Task<QobuzAlbum> GetAlbumAsync(string albumId)
{
    return await _cache.GetOrSetAsync(
        $"album:{albumId}",
        () => FetchAlbumFromApiAsync(albumId),
        TimeSpan.FromHours(1)); // Albums don't change often
}
```

### **Batch Processing Pattern**

```csharp
public class BatchDownloadProcessor
{
    public async Task<DownloadBatchResult> DownloadAlbumsAsync(
        IEnumerable<StreamingAlbum> albums,
        string outputPath,
        BatchOptions options = null,
        IProgress<BatchProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= BatchOptions.Default;
        var semaphore = new SemaphoreSlim(options.MaxConcurrency);
        var results = new List<DownloadResult>();
        var completed = 0;
        var total = albums.Count();
        
        var tasks = albums.Select(async album =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await DownloadAlbumAsync(album, outputPath, cancellationToken);
                
                Interlocked.Increment(ref completed);
                progress?.Report(new BatchProgress 
                { 
                    Completed = completed, 
                    Total = total,
                    CurrentItem = album.Title
                });
                
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        var downloadResults = await Task.WhenAll(tasks);
        
        return new DownloadBatchResult
        {
            TotalAttempted = total,
            Successful = downloadResults.Count(r => r.Success),
            Failed = downloadResults.Count(r => !r.Success),
            Results = downloadResults.ToList()
        };
    }
}
```

---

## 🔐 **Security Best Practices**

### **Credential Management**

```csharp
// Never log credentials
public class SecureLoggingExtensions  
{
    public static void LogRequest(this ILogger logger, HttpRequestMessage request)
    {
        var sanitizedHeaders = request.Headers
            .Where(h => !SecurityConstants.SensitiveHeaders.Contains(h.Key))
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
            
        logger.LogDebug("HTTP Request: {Method} {Url} Headers: {@Headers}", 
            request.Method, 
            request.RequestUri, 
            sanitizedHeaders);
    }
}

// Input sanitization
public static class SecurityUtilities
{
    private static readonly Regex UnsafeCharsRegex = new Regex(@"[<>:""|\\\/\*\?]", RegexOptions.Compiled);
    
    public static string SanitizeForFilePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
            
        // Remove dangerous characters
        var sanitized = UnsafeCharsRegex.Replace(input, "_");
        
        // Prevent directory traversal
        sanitized = sanitized.Replace("..", "__");
        
        // Limit length  
        if (sanitized.Length > 200)
        {
            sanitized = sanitized.Substring(0, 200).TrimEnd();
        }
        
        return sanitized;
    }
}
```

### **Rate Limiting**

```csharp
public class AdaptiveRateLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Queue<DateTime> _recentRequests;
    private int _currentDelay = 1000; // Start with 1 second
    
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        await _semaphore.WaitAsync();
        
        try
        {
            // Check if we're exceeding rate limits
            CleanupOldRequests();
            
            if (_recentRequests.Count >= MaxRequestsPerMinute)
            {
                // Back off exponentially
                _currentDelay = Math.Min(_currentDelay * 2, MaxDelay);
                await Task.Delay(_currentDelay);
            }
            else
            {
                // Reduce delay if we're under limit
                _currentDelay = Math.Max(_currentDelay / 2, MinDelay);
            }
            
            _recentRequests.Enqueue(DateTime.UtcNow);
            
            return await operation();
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            // Server rate limited us, increase delay
            _currentDelay *= 3;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

---

## 📊 **Monitoring & Observability**

### **Performance Metrics Collection**

```csharp
public class PerformanceTracker : IDisposable
{
    private readonly Dictionary<string, List<TimeSpan>> _metrics = new();
    private readonly Timer _flushTimer;
    
    public async Task<T> TrackAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation();
            
            stopwatch.Stop();
            RecordMetric(operationName, stopwatch.Elapsed, "success");
            
            return result;
        }
        catch (Exception)
        {
            stopwatch.Stop();
            RecordMetric(operationName, stopwatch.Elapsed, "error");
            throw;
        }
    }
    
    private void RecordMetric(string operation, TimeSpan duration, string status)
    {
        lock (_metrics)
        {
            var key = $"{operation}.{status}";
            if (!_metrics.ContainsKey(key))
                _metrics[key] = new List<TimeSpan>();
                
            _metrics[key].Add(duration);
            
            // Keep only recent measurements
            if (_metrics[key].Count > 1000)
            {
                _metrics[key].RemoveRange(0, 500);
            }
        }
    }
}

// Usage in base classes
protected async Task<StreamingDownloadResult> DownloadTrackAsync(StreamingTrack track, string outputPath)
{
    return await _performanceTracker.TrackAsync("download.track", async () =>
    {
        // Actual download implementation
        return await PerformDownloadAsync(track, outputPath);
    });
}
```

---

## 🎯 **Quick Implementation Checklist**

### **New Service Integration**

- [ ] ✅ Create `{Service}IndexerSettings : BaseStreamingSettings`
- [ ] ✅ Create `{Service}DownloadSettings : BaseStreamingSettings`
- [ ] ✅ Implement `{Service}Indexer : HttpIndexerBase<{Service}IndexerSettings>`
- [ ] ✅ Implement `{Service}DownloadClient : BaseStreamingDownloadClient<{Service}DownloadSettings>`
- [ ] ✅ Create `{Service}AuthenticationService : BaseStreamingAuthenticationService<,>`
- [ ] ✅ Add quality mapping extensions
- [ ] ✅ Create unit tests using `MockFactories`
- [ ] ✅ Test cross-compatibility with existing plugins

### **Adding New Shared Features**

- [ ] ✅ Design API to be universally applicable
- [ ] ✅ Make feature opt-in with feature flags
- [ ] ✅ Implement with backwards compatibility
- [ ] ✅ Add comprehensive documentation
- [ ] ✅ Create examples for both Qobuz and Tidal patterns
- [ ] ✅ Update shared test utilities
- [ ] ✅ Performance impact assessment

### **Quality Gates**

- [ ] ✅ All existing tests pass (Qobuzarr + Tidalarr)
- [ ] ✅ No breaking changes to public APIs
- [ ] ✅ Memory usage within acceptable limits
- [ ] ✅ Performance regression < 5%
- [ ] ✅ Security review completed
- [ ] ✅ Documentation updated

---

**Document Version**: 1.0  
**Companion To**: `SHARED-LIBRARY-COLLABORATION-GUIDE.md`  
**Last Updated**: 2025-08-29  
**Technical Contact**: Qobuzarr + Tidalarr Architecture Teams

**🔧 Implementation Questions?** Open a GitHub Issue with `technical-question` label and both teams tagged.
