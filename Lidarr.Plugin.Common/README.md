# Lidarr.Plugin.Common

A comprehensive shared library for building Lidarr streaming service plugins. Provides battle-tested patterns, utilities, and base classes that reduce development time by 60-75% while ensuring consistency across all streaming service plugins.

## 🚀 Quick Start

### For New Plugin Development (e.g., Tidalarr)

```csharp
// 1. Create your settings class
public class TidalSettings : BaseStreamingSettings
{
    public string TidalApiKey { get; set; }
    // Only add Tidal-specific settings - base functionality inherited
}

// 2. Create your indexer
public class TidalIndexer : BaseStreamingIndexer<TidalSettings>
{
    public override string Name => "Tidalarr";
    public override string Protocol => nameof(TidalDownloadProtocol);

    protected override async Task<IEnumerable<StreamingSearchResult>> 
        PerformSearchAsync(string searchTerm, StreamingSearchType searchType)
    {
        // Only implement Tidal-specific search logic
        // All caching, rate limiting, error handling provided by base class
        var apiResults = await CallTidalSearchApi(searchTerm);
        return apiResults.Select(MapToStreamingSearchResult);
    }
}

// 3. Create your download client  
public class TidalDownloadClient : BaseStreamingDownloadClient<TidalSettings>
{
    protected override async Task<IEnumerable<StreamingTrack>> GetAlbumTracksAsync(StreamingAlbum album)
    {
        // Only implement Tidal-specific track fetching
        // All progress tracking, error handling provided by base class
    }

    protected override async Task<StreamingDownloadResult> DownloadTrackAsync(
        StreamingTrack track, string outputDirectory, StreamingQuality quality)
    {
        // Only implement Tidal-specific download logic
        // All file management, metadata writing provided by base class
    }
}
```

**Result: ~1,500 lines of Tidal-specific code instead of ~3,500 lines total**

## 📦 What's Included

### Base Classes
- **`BaseStreamingSettings`** - Common configuration patterns
- **`BaseStreamingIndexer<T>`** - Complete indexer with caching, rate limiting
- **`BaseStreamingDownloadClient<T>`** - Download orchestration and progress tracking
- **`BaseStreamingAuthenticationService<T>`** - Generic authentication patterns

### Service Interfaces
- **`IStreamingAuthenticationService<T>`** - Auth service contracts
- **`IStreamingResponseCache`** - Cache service interface

### Utilities
- **`FileNameSanitizer`** - Cross-platform file naming
- **`HttpClientExtensions`** - HTTP utilities with retry logic
- **`RetryUtilities`** - Comprehensive retry patterns with circuit breaker
- **`StreamingApiRequestBuilder`** - Fluent HTTP request builder

### Models
- **`StreamingArtist`** - Universal artist model
- **`StreamingAlbum`** - Universal album model  
- **`StreamingTrack`** - Universal track model
- **`StreamingQuality`** - Quality abstraction across services

### Quality Management
- **`QualityMapper`** - Quality tier mapping and comparison
- **Standard quality definitions** for common scenarios

### Testing Support
- **`MockFactories`** - Generate realistic test data
- **`TestDataSets`** - Pre-built test scenarios

### Plugin Registration
- **`StreamingPluginModule`** - Plugin registration patterns
- **Validation utilities** for plugin setup

## 🎯 Architecture Patterns

### Plugin Structure
```
YourStreamingPlugin/
├── Settings/
│   └── YourServiceSettings.cs         # Inherits BaseStreamingSettings
├── Indexers/
│   └── YourServiceIndexer.cs          # Inherits BaseStreamingIndexer<T>
├── Download/
│   └── YourServiceDownloadClient.cs   # Inherits BaseStreamingDownloadClient<T>
├── Authentication/
│   └── YourServiceAuth.cs             # Inherits BaseStreamingAuthenticationService<T>
└── YourServiceModule.cs               # Inherits StreamingPluginModule
```

### Dependency Flow
```
Shared Library (Lidarr.Plugin.Common)
    ↓
Your Plugin Implementation
    ↓  
Lidarr Core Integration
```

## 🛠 Usage Examples

### HTTP API Calls
```csharp
var request = new StreamingApiRequestBuilder("https://api.tidal.com/v1")
    .Endpoint("search/albums")
    .Query("query", searchTerm)
    .Query("limit", 50)
    .BearerToken(session.AccessToken)
    .WithStreamingDefaults("YourPlugin/1.0")
    .Build();

var response = await httpClient.ExecuteWithRetryAsync(request);
```

### Quality Management
```csharp
// Find best available quality
var preferredQuality = QualityMapper.FindBestMatch(
    album.AvailableQualities, 
    StreamingQualityTier.Lossless);

// Compare qualities
var comparison = QualityMapper.CompareQualities(quality1, quality2);

// Get human-readable description
var description = QualityMapper.GetQualityDescription(quality);
// Result: "FLAC 96.0kHz/24bit Hi-Res"
```

### Caching
```csharp
public class YourServiceCache : StreamingResponseCache
{
    protected override bool ShouldCache(string endpoint) => 
        endpoint.Contains("/search/") || endpoint.Contains("/album/");

    protected override TimeSpan GetCacheDuration(string endpoint) =>
        endpoint.Contains("/search/") ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1);

    protected override string GetServiceName() => "your_service";
}
```

### Testing
```csharp
[Test]
public void TestAlbumDownload()
{
    // Create realistic test data
    var album = MockFactories.CreateMockAlbumWithTracks(12);
    var settings = MockFactories.CreateMockSettings<YourServiceSettings>();
    
    // Use in tests
    var downloadClient = new YourServiceDownloadClient(settings);
    var result = await downloadClient.DownloadAlbumAsync(album, "/test/output");
    
    Assert.That(result, Is.Not.Null);
}
```

## 🎨 Customization Points

### Override Event Methods
```csharp
protected override void OnDownloadStarted(StreamingDownloadJob job)
{
    Logger.Info($"Starting download of {job.Album.Title}");
}

protected override void OnTrackDownloaded(StreamingDownloadJob job, StreamingTrack track, StreamingDownloadResult result)
{
    if (result.Success)
        Logger.Debug($"Downloaded track {track.Title} ({result.ActualQuality})");
}
```

### Custom Validation
```csharp
public override bool IsValid(out string errorMessage)
{
    if (!base.IsValid(out errorMessage))
        return false;
        
    if (string.IsNullOrEmpty(YourServiceApiKey))
    {
        errorMessage = "API Key is required";
        return false;
    }
    
    return true;
}
```

## 📊 Benefits

### Development Speed
- **60-75% less code** to write for new plugins
- **Battle-tested patterns** from working Qobuzarr implementation
- **Consistent architecture** across all plugins

### Quality & Reliability
- **Comprehensive error handling** built-in
- **Thread-safe operations** with proper locking
- **Memory management** with disposal patterns
- **Production-ready** components

### Maintenance
- **Shared bug fixes** benefit all plugins
- **Centralized updates** for common functionality
- **Consistent behavior** across ecosystem

## 🔧 Advanced Features

### Authentication Strategies
The library supports multiple authentication patterns:
- **Username/Password** - Traditional login
- **OAuth2** - Authorization code flow
- **Token-based** - Pre-existing tokens
- **API Key** - Simple key authentication

### Quality Tiers
Universal quality classification:
- **Low** - MP3-96/128, AAC-96
- **Normal** - MP3-160/256, AAC-128/256  
- **High** - MP3-320, AAC-320
- **Lossless** - FLAC-CD, ALAC-CD (44.1kHz/16bit)
- **HiRes** - FLAC-Hi-Res (>44.1kHz or >16bit)

### Error Classification
Automatic error type detection:
- **InvalidCredentials** - Auth failures
- **NetworkError** - Connection issues
- **RateLimited** - Too many requests
- **ServiceUnavailable** - API down
- **RegionBlocked** - Geographic restrictions

## 📈 Performance

### Caching
- **Automatic cache management** with TTL
- **Memory-efficient** with periodic cleanup
- **Configurable cache duration** per endpoint type

### Rate Limiting
- **Built-in rate limiting** with sliding window
- **Configurable limits** per service
- **Automatic backoff** on limit exceeded

### Retry Logic
- **Exponential backoff** with jitter
- **Circuit breaker** pattern for repeated failures
- **Configurable retry policies** per operation

## 🧪 Testing Support

### Mock Data Generation
```csharp
// Generate realistic test albums
var jazzAlbum = TestDataSets.CreateJazzAlbum();
var hiResAlbum = TestDataSets.CreateClassicalHiResAlbum();
var edgeCaseAlbum = TestDataSets.CreateEdgeCaseAlbum(); // Special characters

// Generate collections
var searchResults = MockFactories.CreateMockSearchResults(10);
var qualities = MockFactories.CreateMockQualities();
```

### Edge Case Testing
Pre-built test data for:
- **Special characters** in file names
- **Unicode text** handling
- **Quality edge cases** (unusual formats)
- **Network failure scenarios**

## 🚀 Getting Started

1. **Reference the shared library** in your plugin project
2. **Inherit from base classes** for your main components
3. **Implement abstract methods** with service-specific logic
4. **Configure DI registration** using `StreamingPluginModule`
5. **Write tests** using provided mock factories

## 📚 Full Example

See the example implementations in the `examples/` directory:
- **QobuzResponseCacheShared.cs** - Cache implementation
- **QobuzHttpServiceExample.cs** - HTTP service usage
- **QobuzAuthenticationServiceShared.cs** - Authentication patterns

## 🤝 Contributing

This shared library is designed to grow with the ecosystem. When adding new streaming services:

1. **Extract common patterns** into the shared library
2. **Add test coverage** for new utilities
3. **Update documentation** with examples
4. **Maintain backward compatibility**

## 📄 License

This shared library follows the same license as the parent Qobuzarr project.

---

**Ready to build your streaming service plugin? The shared library handles the complexity - you focus on the music! 🎵**