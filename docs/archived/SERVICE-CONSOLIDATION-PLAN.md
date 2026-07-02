# Qobuzarr Service Consolidation Plan

> Historical note: Qobuzarr's current `AdaptiveRateLimiter` is no longer a standalone local implementation; it is a plugin auto-registration adapter over Common `NamedServiceRateLimiter`.

## Executive Summary

The Qobuzarr codebase contains **113 service classes** spread across multiple projects, with significant overlapping responsibilities and redundant implementations. This consolidation plan identifies **40+ services** that can be merged, **25+ services** that should move to the shared library, and **15+ adapter services** that can be simplified or eliminated.

## Current Service Inventory

### Distribution by Project
- **src/Services/**: 90 service files
- **QobuzCLI/Services/**: 52 service files  
- **ext/Lidarr.Plugin.Common/Services/**: 18 service files
- **Total**: ~160 service-related files

## Critical Consolidation Opportunities

### 1. Quality Management Services (HIGH PRIORITY)

**Current State - 7 Overlapping Services**:
- `QobuzQualityService` - Basic quality detection and fallback
- `IntelligentQualityDetector` - Album-level quality optimization
- `QobuzQualityManager` (Consolidated) - Orchestrator for decomposed services
- `QualityDetectionService` - Track quality detection
- `QualityCacheService` - Quality result caching
- `QualityMappingService` - Quality ID to format mapping
- `StreamInfoService` - Stream URL and quality info

**Redundancies Identified**:
- Both `QobuzQualityService` and `IntelligentQualityDetector` perform quality detection
- `QobuzQualityManager` orchestrates decomposed services but overlaps with original services
- Quality caching implemented in multiple places
- Quality fallback logic duplicated

**Consolidation Plan**:
```csharp
// MERGE INTO SINGLE SERVICE
public class UnifiedQualityService : IQualityService
{
    // Combines all quality responsibilities:
    // - Intelligent album-level detection (from IntelligentQualityDetector)
    // - Track-level detection with caching (from QualityDetectionService)
    // - Quality mapping and format info (from QualityMappingService)  
    // - Stream URL quality validation (from StreamInfoService)
    // - Unified caching layer (from QualityCacheService)
}
```

**Services to Remove**:
- `QobuzQualityService` - Functionality absorbed into unified service
- `IntelligentQualityDetector` - Algorithm integrated into unified service
- Decomposed services can remain but wrapped by unified interface

**Migration to Shared Library**:
- Quality mapping logic → `Lidarr.Plugin.Common.Services.Quality`
- Quality format definitions → Common constants

---

### 2. Metadata Services (HIGH PRIORITY)

**Current State - 7 Services with Overlap**:
- `HybridMetadataService` - Main metadata service using strategies
- `SafeMetadataOptimizer` - Safe metadata operations
- `MetadataStrategyEngine` - Strategy coordination
- `HybridMetadataStrategy` - Hybrid approach implementation
- `QobuzMetadataStrategy` - Qobuz-specific metadata
- `LidarrMetadataStrategy` - Lidarr-specific metadata
- `ISafeMetadataOptimizer` - Interface for safe operations

**Redundancies**:
- `HybridMetadataService` and `SafeMetadataOptimizer` both handle safe metadata operations
- Strategy pattern over-engineered with too many layers
- Duplicate metadata validation logic

**Consolidation Plan**:
```csharp
// SIMPLIFIED ARCHITECTURE
public class QobuzMetadataService : IMetadataService
{
    // Single service with internal strategy selection
    // Absorbs SafeMetadataOptimizer functionality
    private IMetadataStrategy SelectStrategy(context) { }
    private void ApplySafetyChecks(metadata) { }
}
```

**Services to Remove**:
- `SafeMetadataOptimizer` - Merge into main service
- `HybridMetadataService` - Rename to QobuzMetadataService
- Reduce strategy classes to 2: Qobuz and Lidarr

---

### 3. Authentication & Token Management (MEDIUM PRIORITY)

**Current State - 5 Services**:
- `AuthTokenManager` - Token lifecycle management
- `IQobuzAuthService` - Authentication interface
- `IAuthenticationOrchestrator` - Authentication coordination
- `ITokenRefresher` - Token refresh interface
- `ISessionManager` - Session management interface
- `ICredentialValidator` - Credential validation

**Redundancies**:
- Multiple interfaces for authentication with overlapping responsibilities
- Token management split between AuthTokenManager and refresh interfaces
- Session and token management conceptually the same for Qobuz

**Consolidation Plan**:
```csharp
// SINGLE AUTHENTICATION SERVICE
public class QobuzAuthenticationService : IAuthenticationService
{
    // Combines all auth responsibilities:
    // - Credential validation
    // - Token acquisition and refresh
    // - Session management
    // - Automatic token renewal
}
```

**Move to Shared Library**:
- Base authentication patterns → `Lidarr.Plugin.Common.Services.Authentication`
- Token refresh logic → Common base class

---

### 4. Adaptive/Performance Services (MEDIUM PRIORITY)

**Current State - 9 Services with Duplication**:

**In Plugin (src/Services/)**:
- `AdaptiveRateLimiter` - Dynamic rate limiting
- `AdaptiveConcurrencyManager` - Dynamic concurrency control
- `AdaptiveBatchDownloadService` - Adaptive batch downloads
- `BatchMemoryManager` - Batch memory optimization
- `BatchStreamingUrlProvider` - Batch URL operations
- `NetworkResilienceService` - Network reliability
- `MemoryHealthMonitor` - Memory monitoring
- `ApiHealthMonitor` - API health tracking
- `PerformanceMonitoringService` - Performance metrics

**In Shared Library**:
- `AdaptiveRateLimiter` - Duplicate implementation
- `BatchMemoryManager` - Duplicate implementation
- `PerformanceMonitor` - Similar functionality
- `UniversalAdaptiveRateLimiter` - Another rate limiter variant

**Critical Issues**:
- **3 different AdaptiveRateLimiter implementations!**
- **2 BatchMemoryManager implementations**
- **2 PerformanceMonitor implementations**
- Network resilience and health monitoring overlap significantly

**Consolidation Plan**:

```csharp
// STEP 1: Use shared library implementations
// DELETE plugin versions, use Lidarr.Plugin.Common versions

// STEP 2: Merge health monitoring
public class UnifiedHealthMonitor : IHealthMonitor
{
    // Combines:
    // - API health monitoring
    // - Memory health monitoring
    // - Network resilience monitoring
}

// STEP 3: Single adaptive manager
public class AdaptiveResourceManager : IResourceManager
{
    // Combines:
    // - Rate limiting (use Common version)
    // - Concurrency management
    // - Batch operation coordination
}
```

**Services to Remove**:
- Plugin's `AdaptiveRateLimiter` → Use Common version
- Plugin's `BatchMemoryManager` → Use Common version
- Merge all health monitors into one
- Combine adaptive services into single manager

---

### 5. Caching Layer (LOW PRIORITY)

**Current State - 11 Caching Classes**:

**In Plugin**:
- `CacheValidationService` - Cache validation
- `CacheSerializer` - Serialization
- `CacheStatistics` - Statistics tracking
- `CacheStorage` - Storage implementation
- `SubstringMatcher` - Substring cache matching
- `SubstringCacheEntry` - Entry type
- `LRUCacheEvictionStrategy` - LRU eviction
- `QualityCacheService` - Quality-specific caching
- Plus interfaces for each

**Issues**:
- Generic caching infrastructure should be in shared library
- Quality-specific caching mixed with generic caching
- Too many small classes for simple caching

**Consolidation Plan**:

```csharp
// MOVE TO SHARED LIBRARY
namespace Lidarr.Plugin.Common.Services.Caching
{
    public class UnifiedCache<T> : ICache<T>
    {
        // Single cache implementation with:
        // - Configurable eviction strategies
        // - Built-in statistics
        // - Automatic serialization
    }
}

// PLUGIN KEEPS ONLY
public class QobuzCacheService : IQobuzCache
{
    // Uses UnifiedCache<T> internally
    // Qobuz-specific cache keys and policies only
}
```

---

### 6. Lidarr Integration Services (LOW PRIORITY)

**Current State - 12 Services**:
- `LidarrIntegrationService` + Interface
- `LidarrDownloadOrchestrator` + Interface  
- `LidarrAlbumRetriever` + Interface
- `LidarrProgressReporter` + Interface
- `LidarrQueueManager` + Interface
- `LidarrStatisticsCollector` + Interface
- `ServiceIntegrationLayer` - Additional integration layer

**Issues**:
- Too many single-purpose integration services
- `ServiceIntegrationLayer` duplicates other integration services
- Each service is essentially a thin adapter

**Consolidation Plan**:

```csharp
// SINGLE INTEGRATION SERVICE
public class LidarrIntegrationService : ILidarrIntegration
{
    // Facades for all Lidarr interactions:
    IAlbumOperations Albums { get; }
    IDownloadOperations Downloads { get; }
    IQueueOperations Queue { get; }
    IProgressOperations Progress { get; }
    IStatisticsOperations Statistics { get; }
}
```

---

### 7. CLI Adapter Services (HIGH PRIORITY)

**Current State - 20+ CLI Services**:

**Pure Adapters (Can Eliminate)**:
- `CliCacheAdapter` - Wraps plugin cache
- `CliHttpClientAdapter` - Wraps HTTP client
- `CliLoggerAdapter` - Wraps logger
- `RateLimiterAdapter` - Wraps rate limiter
- `CliQobuzAuthenticationAdapter` - Wraps auth service
- `ConfigurationAdapter` - Wraps config

**CLI-Specific (Keep Simplified)**:
- `Dashboard` - CLI UI dashboard
- `InteractiveSelectionService` - User interaction
- `ConsoleUI` services - Terminal UI

**Redundant Services**:
- `CliApiService` - Duplicates plugin API functionality
- `CliDownloadService` - Duplicates plugin download logic
- `SearchService` - Duplicates plugin search

**Consolidation Plan**:

1. **Eliminate all adapter classes** - Use plugin services directly
2. **Keep only CLI-specific UI services**
3. **PluginHost becomes single adapter point**

```csharp
// SINGLE ADAPTER
public class PluginHostAdapter : IPluginHost
{
    // Direct plugin service access
    // No intermediate adapters
    public IQobuzApiClient Api => _pluginApi;
    public IQualityService Quality => _pluginQuality;
    // etc.
}
```

---

## Implementation Roadmap

### Phase 1: Critical Consolidations (Week 1)
1. **Quality Services Consolidation**
   - Merge 7 services → 1 UnifiedQualityService
   - Estimated reduction: 6 services, ~2000 lines

2. **CLI Adapter Elimination**
   - Remove 6+ adapter classes
   - Estimated reduction: 6 services, ~500 lines

### Phase 2: Shared Library Migration (Week 2)
1. **Move Generic Services to Common**
   - Caching infrastructure → Common
   - Adaptive/Performance base classes → Common
   - Authentication base patterns → Common
   - Estimated: 15+ classes moved

2. **Eliminate Duplicates**
   - Remove plugin's AdaptiveRateLimiter (use Common)
   - Remove plugin's BatchMemoryManager (use Common)
   - Estimated reduction: 4 services, ~1000 lines

### Phase 3: Service Mergers (Week 3)
1. **Metadata Service Simplification**
   - Merge Safe + Hybrid → Single service
   - Reduce strategies from 5 to 2
   - Estimated reduction: 3 services, ~500 lines

2. **Health Monitoring Unification**
   - Merge API + Memory + Network monitors
   - Single health monitoring service
   - Estimated reduction: 2 services, ~800 lines

3. **Lidarr Integration Consolidation**
   - Merge 6 integration services → 1 facade
   - Estimated reduction: 5 services, ~1500 lines

---

## Expected Outcomes

### Quantitative Improvements
- **Service Count**: 113 → ~60 services (47% reduction)
- **Code Lines**: ~15,000 → ~9,000 lines (40% reduction)
- **Duplicate Code**: Eliminate ~3,000 lines of duplication
- **Maintenance Surface**: 50% reduction in files to maintain

### Qualitative Improvements
- **Single Responsibility**: Each service has one clear purpose
- **No Duplication**: Eliminate redundant implementations
- **Clear Boundaries**: Shared vs Plugin-specific code
- **Simplified DI**: Fewer services to inject
- **Better Testing**: Focused services easier to test

### Architecture Benefits
- **Consistent Patterns**: Unified approach across services
- **Reusable Components**: Common functionality in shared library
- **Cleaner Dependencies**: Reduced coupling between services
- **Maintainability**: Easier to understand and modify

---

## Risk Mitigation

### Backward Compatibility
- Keep interfaces stable during transition
- Use adapter pattern for gradual migration
- Maintain legacy method signatures temporarily

### Testing Strategy
- Write comprehensive tests before refactoring
- Test each consolidation independently
- Integration tests for service interactions

### Rollback Plan
- Git branches for each consolidation phase
- Feature flags for switching implementations
- Incremental deployment approach

---

## Service Mapping Reference

### Services to Delete (25 services)
- QobuzQualityService
- IntelligentQualityDetector (merge algorithm)
- SafeMetadataOptimizer
- All CLI adapter classes (6)
- ServiceIntegrationLayer
- Individual Lidarr integration services (5)
- Duplicate adaptive services (4)
- Authentication interfaces (3)
- Extra strategy classes (3)

### Services to Move to Common (15 services)
- All caching infrastructure (8 classes)
- Base authentication patterns
- Base quality mapping
- Base metadata patterns
- Performance monitoring base
- Adaptive patterns base

### Services to Merge (20+ mergers)
- Quality services → UnifiedQualityService
- Metadata services → QobuzMetadataService  
- Health monitors → UnifiedHealthMonitor
- Adaptive services → AdaptiveResourceManager
- Lidarr services → LidarrIntegrationService

### Final Service Count Estimate
- **Current**: 113 services
- **After deletion**: 88 services
- **After moving to Common**: 73 services
- **After merging**: ~60 services
- **Reduction**: 47% fewer services

---

## Conclusion

This consolidation plan addresses the critical technical debt of service proliferation in the Qobuzarr codebase. By implementing these changes, we will achieve a cleaner, more maintainable architecture with clear separation of concerns and minimal duplication. The phased approach ensures safe, incremental improvements while maintaining system stability.
