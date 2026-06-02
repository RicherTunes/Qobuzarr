# Qobuz API Client Architecture

## Overview

The Qobuz API client has been refactored from a monolithic 598-line God class into a clean, decomposed architecture following SOLID principles. Each component has a single, well-defined responsibility.

## Architecture Components

### 1. **QobuzApiClient** (Orchestrator)

**Location**: `src/API/QobuzApiClient.cs`
**Lines**: 835
**Responsibility**: Orchestrates API operations by coordinating specialized components

The main API client now acts as an orchestrator, delegating specific responsibilities to focused components while maintaining backward compatibility through dual constructors.

### 2. **QobuzHttpClient** (HTTP Communication)

**Location**: `src/API/Http/QobuzHttpClient.cs`  
**Interface**: `IQobuzHttpClient`  
**Responsibilities**:

- Pure HTTP request/response handling
- Rate limiting enforcement
- Retry logic for transient failures
- HTTP header management

### 3. **Session Management** (Authentication)

**Location**: `src/Authentication/SessionManager.cs`
**Interface**: `ISessionManager` <!-- TODO(docval): IQobuzAuthenticationManager not found; uses ISessionManager from Authentication namespace as of 2026-05-31 -->
**Responsibilities**:

- Session validation and storage
- Expiration tracking
- Session renewal notifications
- Thread-safe session access

### 4. **QobuzRequestSigner** (Request Signing)

**Location**: `src/API/Signing/QobuzRequestSigner.cs`
**Interface**: `IRequestSigner` (from `Lidarr.Plugin.Common.Services.Http`) <!-- TODO(docval): IQobuzRequestSigner not found; uses IRequestSigner from Common as of 2026-05-31 -->
**Responsibilities**:

- MD5 signature generation for protected endpoints
- Request timestamp management
- Signature algorithm implementation (TrevTV format)
- Endpoint signing requirements determination

### 5. **QobuzResponseCache** (Caching)

**Location**: `src/API/Caching/QobuzResponseCache.cs`  
**Interface**: `IQobuzResponseCache`  
**Responsibilities**:

- Cache key generation
- TTL determination per endpoint type
- Cache storage and retrieval
- Cache invalidation strategies

## Benefits of Decomposition

### 1. **Single Responsibility Principle**

Each component has one clear purpose, making the codebase easier to understand and maintain.

### 2. **Testability**

Components can be tested in isolation:

```csharp
// Example: Testing request signing without HTTP calls
var signer = new QobuzRequestSigner(logger);
var signature = signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, appSecret);
Assert.Equal(expectedSignature, signature);
```

### 3. **Maintainability**

Changes are isolated to specific components:

- Need to change caching strategy? Only modify `QobuzResponseCache`
- New authentication method? Update `QobuzAuthenticationManager`
- Rate limiting adjustment? Modify `QobuzHttpClient`

### 4. **Reusability**

Components can be used independently in different contexts:

```csharp
// Use the HTTP client directly for custom operations
var httpClient = new QobuzHttpClient(lidarrHttpClient, logger);
var response = await httpClient.ExecuteAsync(customRequest);
```

## Dependency Injection

### New Component Registration

```csharp
// Register decomposed components
services.AddSingleton<IQobuzHttpClient, QobuzHttpClient>();
services.AddSingleton<ISessionManager, SessionManager>(); <!-- TODO(docval): IQobuzAuthenticationManager not found as of 2026-05-31 -->
services.AddSingleton<IRequestSigner, QobuzRequestSigner>(); <!-- TODO(docval): from Common; IQobuzRequestSigner not found as of 2026-05-31 -->
services.AddSingleton<IQobuzResponseCache, QobuzResponseCache>();
services.AddSingleton<IQobuzApiClient, QobuzApiClient>();
```

### Backward Compatibility

The `QobuzApiClient` provides two constructors:

1. **New architecture** (for DI containers):

```csharp
public QobuzApiClient(
    IQobuzHttpClient httpClient,
    IQobuzAuthenticationManager authManager,
    IQobuzRequestSigner requestSigner,
    IQobuzResponseCache responseCache,
    Logger logger)
```

1. **Legacy compatibility** (for existing code):

```csharp
public QobuzApiClient(
    IHttpClient httpClient,
    ICacheManager cacheManager,
    Logger logger)
```

## Usage Examples

### Using Individual Components

```csharp
// Direct HTTP operations
var httpClient = new QobuzHttpClient(lidarrHttp, logger);
var request = httpClient.BuildRequest(url, "GET");
var response = await httpClient.ExecuteAsync(request);

// Session management
var authManager = new QobuzAuthenticationManager(logger);
authManager.SetSession(session);
if (authManager.NeedsRenewal())
{
    await authManager.ValidateAndRenewIfNeededAsync();
}

// Request signing
var signer = new QobuzRequestSigner(logger);
if (signer.RequiresSigning(endpoint))
{
    signer.SignRequest(endpoint, parameters, appId, appSecret);
}

// Caching
var cache = new QobuzResponseCache(cacheManager, logger);
var cached = cache.Get<AlbumResponse>(endpoint, parameters);
if (cached == null)
{
    var response = await FetchFromApi();
    cache.Set(endpoint, parameters, response);
}
```

### Using the Orchestrator

```csharp
// The orchestrator coordinates all components transparently
var apiClient = new QobuzApiClient(httpClient, cacheManager, logger);
apiClient.SetSession(session);

// Makes HTTP call, applies rate limiting, checks cache, signs request if needed
var album = await apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
```

## Testing Strategy

### Unit Testing

Each component can be unit tested in isolation:

```csharp
[Test]
public async Task HttpClient_AppliesRateLimiting()
{
    var mockHttp = new Mock<IHttpClient>();
    var httpClient = new QobuzHttpClient(mockHttp.Object, logger);
    
    // Make multiple requests
    var tasks = Enumerable.Range(0, 100)
        .Select(_ => httpClient.ExecuteAsync(request))
        .ToArray();
    
    await Task.WhenAll(tasks);
    
    // Verify rate limiting was applied
    Assert.LessOrEqual(mockHttp.Invocations.Count, 60); // 60 req/min limit
}
```

### Integration Testing

Test component interaction through the orchestrator:

```csharp
[Test]
public async Task ApiClient_CachesSuccessfulResponses()
{
    var apiClient = CreateApiClientWithMocks();
    
    // First call - hits API
    var result1 = await apiClient.GetAsync<Album>("/album/get", params);
    
    // Second call - returns cached
    var result2 = await apiClient.GetAsync<Album>("/album/get", params);
    
    // Verify only one HTTP call was made
    VerifyHttpCallCount(1);
}
```

## Migration Path

### Phase 1: Backward Compatible (Current)

- New architecture is in place
- Legacy constructor maintains compatibility
- No breaking changes for existing consumers

### Phase 2: Gradual Migration

- Update DI registrations to use new interfaces
- Migrate high-level services to use decomposed components directly
- Add component-specific configuration options

### Phase 3: Full Decomposition

- Remove legacy constructor
- Require explicit component injection
- Enable advanced scenarios (custom caching, auth strategies, etc.)

## Performance Considerations

### Memory Efficiency

- Components are singletons, reducing memory overhead
- Shared cache manager across all instances
- No duplicate rate limiter instances

### CPU Efficiency

- Cache checks before expensive operations
- Rate limiting prevents API throttling
- Parallel-safe implementations

### Network Efficiency

- Response caching reduces API calls
- Intelligent cache TTLs per endpoint type
- Automatic retry with exponential backoff

## Future Enhancements

### Potential Extensions

1. **Pluggable authentication strategies** - OAuth, API keys, etc.
2. **Custom cache providers** - Redis, Memcached, etc.
3. **Advanced rate limiting** - Per-endpoint limits, burst handling
4. **Request/response interceptors** - Logging, metrics, transformation
5. **Circuit breaker pattern** - Fail fast on repeated failures

### Configuration Options

```csharp
services.Configure<QobuzApiOptions>(options =>
{
    options.RateLimit = 100; // Requests per minute
    options.CacheStrategy = CacheStrategy.Aggressive;
    options.RetryPolicy = RetryPolicy.ExponentialBackoff;
    options.EnableRequestLogging = true;
});
```

## Conclusion

The refactored architecture transforms a monolithic 598-line class into a maintainable, testable, and extensible system. Each component has a clear responsibility, making the codebase easier to understand, test, and modify. The architecture maintains full backward compatibility while enabling future enhancements and advanced usage scenarios.
