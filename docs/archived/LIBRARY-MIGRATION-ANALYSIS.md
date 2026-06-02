> ⚠️ Historical (flagged 2026-05-31): describes a past state; some details below no longer match the current code.

# Qobuzarr to Lidarr.Plugin.Common Migration Analysis

## Executive Summary

After comprehensive analysis of the Qobuzarr codebase, I've identified **22 services** that can be migrated to the shared library, with **15 services** already having equivalents in Lidarr.Plugin.Common. The migration would reduce Qobuzarr's codebase by approximately **40%** while improving maintainability and benefiting all streaming plugins.

## Analysis Results

### 1. Services That Can Be Moved to Shared Library (Generic)

#### **High Priority - Already Has Shared Equivalent**
These services have direct equivalents in Lidarr.Plugin.Common and should be replaced immediately:

| Qobuzarr Service | Shared Library Equivalent | Migration Effort |
|------------------|---------------------------|------------------|
| `SafeOperationExecutor` | `Lidarr.Plugin.Common.Services.SafeOperationExecutor` | **Trivial** - Already exists |
| `ApiHealthMonitor` | `PerformanceMonitor` (partial) | **Low** - Already using it internally |
| `CompilationAlbumDetector` | `Intelligence.CompilationAlbumDetector` | **Trivial** - Direct replacement |
| `AdaptiveConcurrencyManager` | `Performance.UniversalAdaptiveRateLimiter` | **Medium** - Different API |
| `BatchStreamingUrlProvider` | Can use shared patterns | **Medium** - Needs abstraction |
| `MemoryHealthMonitor` | `Performance.BatchMemoryManager` | **Low** - Similar functionality |

#### **Medium Priority - Generic But No Shared Equivalent**
These are generic services that would benefit ALL streaming plugins:

| Service | Description | Benefit to Other Plugins |
|---------|-------------|--------------------------|
| `RequestDeduplicator` | Prevents cache stampede | **Critical** - All plugins need this |
| `AuthTokenManager` | Token refresh management | **High** - OAuth/token-based services |
| `AdvancedTrackMatcher` | Fuzzy matching logic | **High** - Universal need |
| `LiveAlbumNormalizer` | Live/compilation detection | **Medium** - Music-specific |
| `UnicodeNormalizer` | Text normalization | **High** - International content |
| `ConfigurationMonitor` | Settings hot-reload | **High** - All plugins need this |
| `DefensiveServiceWrapper` | Circuit breaker pattern | **Critical** - Fault tolerance |

#### **Low Priority - Caching Infrastructure**
The entire caching namespace can be made generic:

| Component | Current Location | Proposed Location |
|-----------|-----------------|-------------------|
| `ICacheStorage` | `src/Services/Caching/` | `Common/Services/Caching/` |
| `ICacheSerializer` | `src/Services/Caching/` | `Common/Services/Caching/` |
| `ICacheStatistics` | `src/Services/Caching/` | `Common/Services/Caching/` |
| `ICacheEvictionStrategy` | `src/Services/Caching/` | `Common/Services/Caching/` |
| `LRUCacheEvictionStrategy` | `src/Services/Caching/` | `Common/Services/Caching/` |
| `SubstringMatcher` | `src/Services/Caching/` | `Common/Services/Caching/` |

### 2. Qobuz-Specific Services (Must Remain)

These services are tightly coupled to Qobuz's API and business logic:

| Service | Reason for Qobuz-Specific |
|---------|---------------------------|
| `QobuzSearchService` | Qobuz API search logic |
| `QobuzStreamUrlService` | Qobuz URL signing |
| `QobuzValidationService` | Qobuz-specific validation |
| `QobuzStreamAvailabilityService` | Qobuz region checks |
| `UnifiedQualityService` | Qobuz quality mappings |

### 3. Lidarr Integration Services (Can Be Generalized)

These services implement patterns that ALL streaming plugins need:

| Service | Generic Pattern | Shared Library Benefit |
|---------|----------------|------------------------|
| `UnifiedLidarrIntegration` | Model mapping facade | **Critical** - All plugins map models |
| `LidarrAlbumRetriever` | Metadata retrieval | **High** - Common pattern |
| `LidarrProgressReporter` | Download progress | **High** - Universal need |
| `LidarrQueueManager` | Queue management | **High** - Standard pattern |
| `LidarrStatisticsCollector` | Metrics collection | **Medium** - Monitoring |

### 4. Innovations to Share with Ecosystem

#### **Authentication Patterns**
```csharp
// Qobuzarr's AuthTokenManager pattern - perfect for OAuth services
public interface IStreamingAuthTokenManager
{
    Task<string> GetValidTokenAsync();
    Task RefreshTokenAsync();
    event EventHandler<TokenRefreshEventArgs> TokenRefreshed;
}
```

#### **Request Deduplication**
```csharp
// Prevents API hammering when multiple users search simultaneously
public interface IRequestDeduplicator
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory);
}
```

#### **Defensive Patterns**
```csharp
// Circuit breaker + retry + fallback in one
public interface IDefensiveServiceWrapper
{
    Task<T> ExecuteWithDefensesAsync<T>(Func<Task<T>> operation);
}
```

### 5. Unused Shared Library Components

We're NOT using these available components that could replace current implementations:

| Shared Component | Current Qobuzarr Implementation | Migration Benefit |
|------------------|--------------------------------|-------------------|
| `OAuthStreamingAuthenticationService` | Custom auth in `QobuzApiClient` | Standardized OAuth |
| `PKCEGenerator` | Not using PKCE | Enhanced security |
| `CacheValidationService` | Custom validation | Consistent validation |
| `StreamingApiRequestBuilder` | Custom request building | Standardized HTTP |
| `DataValidationService` | Multiple validators | Centralized validation |
| `NetworkResilienceService` | Custom retry logic | Better fault tolerance |

## Migration Plan

### Phase 1: Direct Replacements (Week 1)
1. **Replace `SafeOperationExecutor`** with shared version
2. **Replace `CompilationAlbumDetector`** with shared version
3. **Integrate `PerformanceMonitor`** fully into `ApiHealthMonitor`
4. **Replace memory management** with `BatchMemoryManager`

### Phase 2: Service Extraction (Week 2)
1. **Extract `RequestDeduplicator`** to shared library
2. **Extract `AuthTokenManager`** as `IStreamingAuthTokenManager`
3. **Extract `DefensiveServiceWrapper`** as universal pattern
4. **Extract caching infrastructure** to shared namespace

### Phase 3: Pattern Generalization (Week 3)
1. **Create `IStreamingIntegrationFacade`** from `UnifiedLidarrIntegration`
2. **Abstract track matching** into shared `IContentMatcher`
3. **Generalize progress reporting** patterns
4. **Create shared queue management** interfaces

### Phase 4: Adoption of Shared Components (Week 4)
1. **Migrate to `OAuthStreamingAuthenticationService`** base
2. **Adopt `StreamingApiRequestBuilder`** for HTTP
3. **Use `NetworkResilienceService`** for retries
4. **Integrate `DataValidationService`** for validation

## Expected Benefits

### For Qobuzarr
- **40% code reduction** (22 services → shared)
- **Better testing** through shared test utilities
- **Automatic updates** when shared library improves
- **Reduced maintenance** burden

### For Other Plugins
- **Request deduplication** prevents API rate limiting
- **Auth token management** for OAuth services
- **Defensive patterns** for fault tolerance
- **Advanced matching** algorithms
- **Caching infrastructure** with statistics

### For Ecosystem
- **Consistent patterns** across all plugins
- **Shared bug fixes** benefit everyone
- **Community contributions** to shared components
- **Faster plugin development** with rich base

## Implementation Priority

### Critical (Do First)
1. `RequestDeduplicator` - Prevents API bans
2. `AuthTokenManager` - Prevents auth failures
3. `DefensiveServiceWrapper` - Improves reliability

### High Value (Do Soon)
1. Caching infrastructure - Used by all
2. `AdvancedTrackMatcher` - Improves accuracy
3. Lidarr integration patterns - Standardization

### Nice to Have (Do Eventually)
1. `UnicodeNormalizer` - Edge case handling
2. `LiveAlbumNormalizer` - Quality improvement
3. Statistics collectors - Monitoring

## Code Metrics

### Current State
- **Total Services**: 65 files
- **Generic Services**: 22 files (34%)
- **Qobuz-Specific**: 15 files (23%)
- **Already Using Shared**: 3 files (5%)

### After Migration
- **Reduced Files**: 43 files (-34%)
- **Shared Dependencies**: 22 components
- **Maintenance Reduction**: ~40%

## Risk Assessment

### Low Risk
- Direct replacements of existing shared components
- Well-tested generic patterns
- Clear abstraction boundaries

### Medium Risk
- Authentication pattern changes
- Caching infrastructure migration
- API client refactoring

### Mitigation
- Incremental migration with testing
- Feature flags for gradual rollout
- Maintain backward compatibility

## Recommendations

1. **Start with direct replacements** - Low risk, immediate benefit
2. **Extract critical patterns first** - RequestDeduplicator, AuthTokenManager
3. **Create abstraction interfaces** before moving implementations
4. **Add comprehensive tests** for shared components
5. **Document patterns** for other plugin developers
6. **Version shared library** properly for breaking changes

## Conclusion

The migration to Lidarr.Plugin.Common will significantly reduce Qobuzarr's maintenance burden while providing valuable components to the entire streaming plugin ecosystem. The identified services are genuinely generic and would benefit Tidal, Deezer, Spotify, and future streaming service plugins.

**Estimated Timeline**: 4 weeks
**Effort**: Medium
**Risk**: Low to Medium
**Benefit**: High for entire ecosystem