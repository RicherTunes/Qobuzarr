# Critical Shared Component Implementation Guide

## 1. RequestDeduplicator - Prevent API Rate Limiting

### Problem It Solves
When 50 users search "Taylor Swift" simultaneously, without deduplication you get 50 API calls. With deduplication, you get 1 API call and 49 users share the result.

### Current Implementation Location
`src/Services/RequestDeduplicator.cs`

### Proposed Shared Library Interface
```csharp
namespace Lidarr.Plugin.Common.Services.Deduplication
{
    public interface IRequestDeduplicator
    {
        /// <summary>
        /// Ensures only one execution per unique key, other callers wait for result
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string key, 
            Func<Task<T>> factory,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Try to get existing request without waiting
        /// </summary>
        bool TryGetPending<T>(string key, out Task<T> pendingTask);

        /// <summary>
        /// Cancel and remove a pending request
        /// </summary>
        bool CancelRequest(string key);

        /// <summary>
        /// Get current statistics
        /// </summary>
        DeduplicationStatistics GetStatistics();
    }

    public class DeduplicationStatistics
    {
        public int PendingRequests { get; set; }
        public int TotalDeduplicatedCalls { get; set; }
        public int TotalPrimaryCalls { get; set; }
        public double DeduplicationRatio { get; set; }
        public Dictionary<string, int> TopDeduplicatedKeys { get; set; }
    }
}
```

### Migration Steps
1. Copy current implementation to `Lidarr.Plugin.Common/src/Services/Deduplication/`
2. Remove Qobuz-specific logging
3. Add interface abstractions
4. Create unit tests in shared library
5. Update Qobuzarr to use shared version

### Usage Example
```csharp
// In any streaming plugin
var searchResult = await _deduplicator.GetOrCreateAsync(
    $"search_{query}_{quality}",
    async () => await _apiClient.SearchAsync(query, quality),
    timeout: TimeSpan.FromSeconds(30));
```

## 2. AuthTokenManager - Handle Token Refresh

### Problem It Solves
Long operations (30min+ downloads) fail when auth tokens expire mid-operation. This provides automatic refresh.

### Current Implementation
`src/Services/AuthTokenManager.cs`

### Proposed Shared Library Interface
```csharp
namespace Lidarr.Plugin.Common.Services.Authentication
{
    public interface IStreamingAuthTokenManager
    {
        /// <summary>
        /// Gets valid token, refreshing if necessary
        /// </summary>
        Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Force token refresh
        /// </summary>
        Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Set initial token and expiry
        /// </summary>
        void SetToken(string token, DateTime expiryTime);

        /// <summary>
        /// Check if token needs refresh
        /// </summary>
        bool NeedsRefresh(TimeSpan? bufferTime = null);

        /// <summary>
        /// Events for token lifecycle
        /// </summary>
        event EventHandler<TokenRefreshEventArgs> TokenRefreshed;
        event EventHandler<TokenRefreshFailedEventArgs> TokenRefreshFailed;
    }

    public abstract class BaseStreamingAuthTokenManager : IStreamingAuthTokenManager
    {
        protected abstract Task<TokenRefreshResult> PerformTokenRefreshAsync();
        // Base implementation with timer, semaphore, retry logic
    }
}
```

### Plugin-Specific Implementation
```csharp
public class QobuzAuthTokenManager : BaseStreamingAuthTokenManager
{
    protected override async Task<TokenRefreshResult> PerformTokenRefreshAsync()
    {
        // Qobuz-specific refresh logic
        var newSession = await _qobuzApi.RefreshSessionAsync();
        return new TokenRefreshResult
        {
            Token = newSession.Token,
            ExpiryTime = DateTime.UtcNow.AddSeconds(newSession.ExpiresIn)
        };
    }
}
```

## 3. DefensiveServiceWrapper - Circuit Breaker Pattern

### Problem It Solves
Prevents cascading failures by implementing circuit breaker, retry, and fallback patterns.

### Current Implementation
`src/Services/DefensiveServiceWrapper.cs`

### Proposed Shared Library Interface
```csharp
namespace Lidarr.Plugin.Common.Services.Resilience
{
    public interface IDefensiveServiceWrapper
    {
        /// <summary>
        /// Execute with all defensive patterns
        /// </summary>
        Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            DefensiveOptions options = null);

        /// <summary>
        /// Execute with custom fallback
        /// </summary>
        Task<T> ExecuteWithFallbackAsync<T>(
            Func<Task<T>> operation,
            Func<Exception, Task<T>> fallback,
            DefensiveOptions options = null);

        /// <summary>
        /// Get circuit state
        /// </summary>
        CircuitState GetCircuitState(string circuitName = "default");
    }

    public class DefensiveOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public bool UseExponentialBackoff { get; set; } = true;
        public TimeSpan? Timeout { get; set; }
        public string CircuitName { get; set; } = "default";
        public int CircuitFailureThreshold { get; set; } = 5;
        public TimeSpan CircuitResetTimeout { get; set; } = TimeSpan.FromMinutes(1);
    }
}
```

### Universal Benefits
- All plugins need fault tolerance
- Prevents service-wide outages
- Automatic recovery
- Consistent error handling

## 4. Advanced Caching Infrastructure

### Current Components to Share
```
src/Services/Caching/
├── ICacheStorage.cs
├── ICacheSerializer.cs  
├── ICacheStatistics.cs
├── ICacheEvictionStrategy.cs
├── LRUCacheEvictionStrategy.cs
├── SubstringMatcher.cs (fuzzy search cache)
```

### Proposed Shared Library Structure
```csharp
namespace Lidarr.Plugin.Common.Services.Caching
{
    public interface IStreamingCache<TKey, TValue>
    {
        Task<TValue> GetOrCreateAsync(
            TKey key,
            Func<Task<TValue>> factory,
            CacheEntryOptions options = null);

        bool TryGet(TKey key, out TValue value);
        void Set(TKey key, TValue value, CacheEntryOptions options = null);
        bool Remove(TKey key);
        void Clear();
        
        ICacheStatistics GetStatistics();
    }

    public interface ICacheStatistics
    {
        long TotalRequests { get; }
        long CacheHits { get; }
        long CacheMisses { get; }
        double HitRatio { get; }
        long CurrentSize { get; }
        long EvictionCount { get; }
        Dictionary<string, long> HitsByCategory { get; }
    }
}
```

## 5. Content Matching Algorithms

### Current Implementation
`src/Services/AdvancedTrackMatcher.cs`

### Proposed Shared Interface
```csharp
namespace Lidarr.Plugin.Common.Services.Matching
{
    public interface IContentMatcher<TSource, TTarget>
    {
        /// <summary>
        /// Find best match with confidence score
        /// </summary>
        MatchResult<TTarget> FindBestMatch(
            TSource source,
            IEnumerable<TTarget> candidates,
            MatchingOptions options = null);

        /// <summary>
        /// Find all matches above threshold
        /// </summary>
        IEnumerable<MatchResult<TTarget>> FindMatches(
            TSource source,
            IEnumerable<TTarget> candidates,
            double minConfidence = 0.8,
            MatchingOptions options = null);
    }

    public class MatchResult<T>
    {
        public T Item { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, double> ComponentScores { get; set; }
        public string MatchReason { get; set; }
    }

    public class MatchingOptions
    {
        public bool UseFuzzyMatching { get; set; } = true;
        public bool NormalizeUnicode { get; set; } = true;
        public bool IgnoreCase { get; set; } = true;
        public bool RemoveDiacritics { get; set; } = true;
        public double FuzzyThreshold { get; set; } = 0.85;
    }
}
```

## 6. Progress Reporting Infrastructure

### Proposed Shared Pattern
```csharp
namespace Lidarr.Plugin.Common.Services.Progress
{
    public interface IStreamingProgressReporter
    {
        void ReportProgress(ProgressInfo info);
        void ReportError(string message, Exception exception = null);
        void Complete(CompletionInfo info);
        
        event EventHandler<ProgressEventArgs> ProgressChanged;
        event EventHandler<ErrorEventArgs> ErrorOccurred;
        event EventHandler<CompletionEventArgs> Completed;
    }

    public class ProgressInfo
    {
        public string OperationId { get; set; }
        public string Description { get; set; }
        public long CurrentBytes { get; set; }
        public long TotalBytes { get; set; }
        public int CurrentItems { get; set; }
        public int TotalItems { get; set; }
        public double PercentComplete { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public double BytesPerSecond { get; set; }
    }
}
```

## Implementation Timeline

### Week 1: Critical Components
- [ ] RequestDeduplicator → Shared library
- [ ] AuthTokenManager → Shared library  
- [ ] DefensiveServiceWrapper → Shared library

### Week 2: Caching Infrastructure
- [ ] Cache interfaces → Shared library
- [ ] LRU eviction → Shared library
- [ ] Cache statistics → Shared library
- [ ] Substring matcher → Shared library

### Week 3: Integration Patterns
- [ ] Content matcher → Shared library
- [ ] Progress reporter → Shared library
- [ ] Model mappers → Shared library

### Week 4: Testing & Documentation
- [ ] Unit tests for all components
- [ ] Integration tests
- [ ] Migration guides
- [ ] API documentation

## Testing Strategy

### Unit Tests Required
```csharp
[Fact]
public async Task RequestDeduplicator_PreventsSimultaneousCalls()
{
    var deduplicator = new RequestDeduplicator();
    var callCount = 0;
    
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => deduplicator.GetOrCreateAsync(
            "test_key",
            async () => {
                Interlocked.Increment(ref callCount);
                await Task.Delay(100);
                return "result";
            }))
        .ToArray();
    
    var results = await Task.WhenAll(tasks);
    
    Assert.Equal(1, callCount); // Only one actual call
    Assert.All(results, r => Assert.Equal("result", r)); // All get same result
}
```

## Migration Validation

### Before Migration Metrics
- Code duplication: High
- Service count: 65
- Maintenance burden: High
- Cross-plugin sharing: None

### After Migration Metrics
- Code duplication: Minimal
- Service count: 43 (-34%)
- Maintenance burden: Low
- Cross-plugin sharing: 22 components

## Risk Mitigation

1. **Feature Flags**: Enable gradual rollout
```csharp
if (_featureFlags.UseSharedDeduplicator)
    return await _sharedDeduplicator.GetOrCreateAsync(key, factory);
else
    return await _legacyDeduplicator.Execute(key, factory);
```

2. **Backward Compatibility**: Maintain interfaces
3. **Comprehensive Testing**: Before switching
4. **Monitoring**: Track performance metrics
5. **Rollback Plan**: Quick revert capability

## Success Criteria

- [ ] Zero increase in API errors
- [ ] Reduced memory usage (-20%)
- [ ] Improved response times (-15%)
- [ ] Successful adoption by 1+ other plugin
- [ ] 95%+ test coverage on shared components
- [ ] Documentation approved by team

## Next Steps

1. **Create PR** for shared library components
2. **Get team feedback** on interfaces
3. **Implement critical three** first
4. **Test in Qobuzarr** as proof of concept
5. **Document for other** plugin developers
6. **Promote adoption** in plugin community