# Qobuzarr Shared Library Migration - Priority Plan

## Current Usage Analysis

### Already Using from Shared Library (9 files)
- `QobuzSettings` → Uses `BaseStreamingSettings`
- `ApiHealthMonitor` → Uses `PerformanceMonitor`
- `QobuzDownloadService` → Uses shared utilities
- `UnifiedLidarrIntegration` → Uses shared services
- `LidarrAlbumRetriever` → Uses `PerformanceMonitor`
- `AdaptiveQobuzApiClient` → Uses `PerformanceMonitor`
- `LidarrInputValidator` → Uses shared utilities
- `FileSystemUtilities` → Uses shared utilities
- `FileNameUtility` → Uses `FileNameSanitizer`

### Available but NOT Using (Opportunities)

| Shared Component | Current Qobuzarr Alternative | Priority | Effort |
|------------------|------------------------------|----------|--------|
| `OAuthStreamingAuthenticationService` | Custom auth in `QobuzApiClient` | **HIGH** | Medium |
| `StreamingApiRequestBuilder` | Manual HTTP in `QobuzApiClient` | **HIGH** | Low |
| `NetworkResilienceService` | Custom retry logic scattered | **HIGH** | Medium |
| `UniversalAdaptiveRateLimiter` | `AdaptiveConcurrencyManager` | **MEDIUM** | Low |
| `CacheValidationService` | Custom validation logic | **MEDIUM** | Low |
| `DataValidationService` | `QobuzValidationService` | **MEDIUM** | Medium |
| `BatchMemoryManager` | `MemoryHealthMonitor` | **LOW** | Low |
| `CompilationAlbumDetector` | `CompilationAlbumDetector` (duplicate) | **HIGH** | Trivial |
| `Guard` utilities | Custom null checks | **LOW** | Trivial |
| `RetryUtilities` | Custom retry logic | **MEDIUM** | Low |

## Migration Priority Matrix

### Priority 1: CRITICAL - Do This Week
**Impact: Prevents service failures and API bans**

#### 1.1 Extract RequestDeduplicator to Shared
- **Current**: `src/Services/RequestDeduplicator.cs`
- **Target**: `Lidarr.Plugin.Common/src/Services/Deduplication/`
- **Why Critical**: Prevents API rate limiting when multiple users search simultaneously
- **Effort**: 4 hours
- **Risk**: Low (well-tested, isolated component)

#### 1.2 Replace CompilationAlbumDetector
- **Current**: `src/Services/CompilationAlbumDetector.cs`
- **Target**: Use `Lidarr.Plugin.Common.Services.Intelligence.CompilationAlbumDetector`
- **Why Critical**: Exact duplicate, immediate win
- **Effort**: 30 minutes
- **Risk**: None (identical implementation)

#### 1.3 Adopt NetworkResilienceService
- **Current**: Custom retry logic in multiple places
- **Target**: Use `Lidarr.Plugin.Common.Services.Network.NetworkResilienceService`
- **Why Critical**: Consolidates retry logic, improves reliability
- **Effort**: 4 hours
- **Risk**: Low (can run in parallel)

### Priority 2: HIGH VALUE - Complete in 2 Weeks
**Impact: Major code reduction and standardization**

#### 2.1 Extract AuthTokenManager to Shared
- **Current**: `src/Services/AuthTokenManager.cs`
- **Target**: `Lidarr.Plugin.Common/src/Services/Authentication/StreamingAuthTokenManager.cs`
- **Why High**: All OAuth services need this
- **Effort**: 6 hours
- **Risk**: Medium (needs abstraction)

#### 2.2 Migrate to StreamingApiRequestBuilder
- **Current**: Manual HTTP building in `QobuzApiClient`
- **Target**: Use shared `StreamingApiRequestBuilder`
- **Why High**: Standardizes HTTP patterns
- **Effort**: 8 hours
- **Risk**: Medium (API client refactor)

#### 2.3 Extract DefensiveServiceWrapper
- **Current**: `src/Services/DefensiveServiceWrapper.cs`
- **Target**: `Lidarr.Plugin.Common/src/Services/Resilience/`
- **Why High**: Universal fault tolerance pattern
- **Effort**: 4 hours
- **Risk**: Low (well isolated)

### Priority 3: OPTIMIZATION - Complete in Month 1
**Impact: Performance and maintainability improvements**

#### 3.1 Caching Infrastructure Migration
- **Current**: `src/Services/Caching/*` (11 files)
- **Target**: `Lidarr.Plugin.Common/src/Services/Caching/`
- **Why**: Universal caching needs
- **Effort**: 12 hours
- **Risk**: Medium (many touchpoints)

#### 3.2 Replace AdaptiveConcurrencyManager
- **Current**: `src/Services/AdaptiveConcurrencyManager.cs`
- **Target**: Use `UniversalAdaptiveRateLimiter`
- **Why**: Standardized rate limiting
- **Effort**: 4 hours
- **Risk**: Low (similar API)

#### 3.3 Content Matcher Extraction
- **Current**: `src/Services/AdvancedTrackMatcher.cs`
- **Target**: `Lidarr.Plugin.Common/src/Services/Matching/`
- **Why**: All plugins need fuzzy matching
- **Effort**: 6 hours
- **Risk**: Low (pure algorithm)

### Priority 4: CLEANUP - Complete in Month 2
**Impact: Code cleanliness and consistency**

#### 4.1 Consolidate Validation Services
- Replace `QobuzValidationService` with `DataValidationService`
- Replace custom validation with `CacheValidationService`
- Effort: 4 hours

#### 4.2 Memory Management Standardization
- Replace `MemoryHealthMonitor` with `BatchMemoryManager`
- Remove forced GC patterns
- Effort: 2 hours

#### 4.3 Utility Consolidation
- Replace custom guards with `Guard` utilities
- Use `RetryUtilities` consistently
- Effort: 2 hours

## Implementation Schedule

### Week 1: Critical Components
```
Monday-Tuesday:
  ✓ Extract RequestDeduplicator to shared library
  ✓ Write comprehensive tests
  
Wednesday:
  ✓ Replace CompilationAlbumDetector (30 min)
  ✓ Test and validate
  
Thursday-Friday:
  ✓ Adopt NetworkResilienceService
  ✓ Remove custom retry logic
  ✓ Integration testing
```

### Week 2: High Value Services
```
Monday-Tuesday:
  ✓ Extract AuthTokenManager with abstraction
  ✓ Create plugin-specific implementation
  
Wednesday-Thursday:
  ✓ Migrate to StreamingApiRequestBuilder
  ✓ Refactor QobuzApiClient
  
Friday:
  ✓ Extract DefensiveServiceWrapper
  ✓ Update all defensive calls
```

### Week 3-4: Optimization & Cleanup
```
Week 3:
  ✓ Migrate caching infrastructure
  ✓ Replace AdaptiveConcurrencyManager
  ✓ Extract content matcher
  
Week 4:
  ✓ Consolidate validation services
  ✓ Standardize memory management
  ✓ Consolidate utilities
  ✓ Final testing and documentation
```

## Success Metrics

### Code Metrics
- **Before**: 65 service files, ~15,000 LOC
- **Target**: 43 service files (-34%), ~9,000 LOC (-40%)
- **Shared Components**: 22 extracted

### Performance Metrics
- **API Calls**: -50% duplicate calls (deduplication)
- **Memory Usage**: -20% (better management)
- **Response Time**: -15% (optimized caching)
- **Error Rate**: -30% (defensive patterns)

### Quality Metrics
- **Test Coverage**: 95% on shared components
- **Code Duplication**: <5% (from current 25%)
- **Cyclomatic Complexity**: <10 per method
- **Maintainability Index**: >70

## Risk Mitigation Strategy

### Per-Component Strategy

| Component | Risk | Mitigation |
|-----------|------|------------|
| RequestDeduplicator | Cache poisoning | TTL limits, validation |
| AuthTokenManager | Token leakage | Secure storage, rotation |
| NetworkResilience | Infinite retries | Max attempt limits |
| Caching | Memory bloat | Size limits, eviction |
| DefensiveWrapper | Over-protection | Configurable thresholds |

### Rollback Plan
1. **Feature Flags**: Each migration behind flag
2. **Parallel Running**: Old and new side-by-side
3. **Quick Revert**: Git tags at each milestone
4. **A/B Testing**: Gradual user rollout

## Testing Requirements

### Unit Tests (Required)
```csharp
// RequestDeduplicator
[Fact] Task Should_Deduplicate_Concurrent_Requests()
[Fact] Task Should_Timeout_Stuck_Requests()
[Fact] Task Should_Handle_Factory_Exceptions()

// AuthTokenManager  
[Fact] Task Should_Refresh_Before_Expiry()
[Fact] Task Should_Handle_Refresh_Failures()
[Fact] Task Should_Queue_Concurrent_Refreshes()

// DefensiveServiceWrapper
[Fact] Task Should_Retry_On_Transient_Failures()
[Fact] Task Should_Open_Circuit_After_Threshold()
[Fact] Task Should_Use_Fallback_When_Circuit_Open()
```

### Integration Tests (Required)
```csharp
[Fact] Task Full_Download_With_Token_Refresh()
[Fact] Task Concurrent_Search_Deduplication()
[Fact] Task Circuit_Breaker_Recovery()
```

## Documentation Requirements

### For Shared Library
1. **API Documentation**: XML comments on all public APIs
2. **Usage Examples**: Sample code for each component
3. **Migration Guide**: Step-by-step for other plugins
4. **Performance Guide**: Tuning recommendations

### For Qobuzarr
1. **Architecture Update**: Reflect new structure
2. **Dependency Graph**: Show shared components
3. **Configuration Guide**: New settings/options
4. **Troubleshooting**: Common issues and fixes

## Communication Plan

### Internal Team
- Weekly progress updates
- PR reviews for each component
- Testing sign-offs required

### Plugin Community
- Blog post: "Shared Components for Streaming Plugins"
- Example PR: Show other plugins how to adopt
- Discord announcement: Available components
- GitHub discussions: Feedback and requests

## Definition of Done

### Per Component
- [ ] Code migrated to shared library
- [ ] Unit tests >95% coverage
- [ ] Integration tests passing
- [ ] Documentation complete
- [ ] PR reviewed and approved
- [ ] Qobuzarr updated to use shared
- [ ] Performance metrics validated
- [ ] No increase in error rates

### Overall Migration
- [ ] All Priority 1 components complete
- [ ] All Priority 2 components complete  
- [ ] 50% of Priority 3 complete
- [ ] Zero production incidents
- [ ] 1+ other plugin adopted components
- [ ] Team satisfaction survey >4/5

## Next Immediate Actions

1. **Today**: Create shared library PR for RequestDeduplicator
2. **Tomorrow**: Write tests for RequestDeduplicator
3. **Day 3**: Update Qobuzarr to use shared RequestDeduplicator
4. **Day 4**: Replace CompilationAlbumDetector (quick win)
5. **Day 5**: Begin NetworkResilienceService adoption

## Questions for Team

1. Should we version shared library independently?
2. How to handle breaking changes in shared components?
3. Preferred namespace structure for shared library?
4. Should we create plugin-specific extension packages?
5. How to encourage community contributions?

---

**Prepared by**: Architecture Analysis Agent
**Date**: 2025-08-28
**Status**: Ready for Review
**Next Review**: Week 1 Completion