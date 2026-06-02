# Service Migration Guide: Legacy to Consolidated Architecture

## Overview

Qobuzarr is in the process of migrating from multiple specialized services to consolidated service managers. This guide documents the migration pattern and provides step-by-step instructions for completing the transition.

## Migration Strategy

### Current State (Transitional)

- ✅ **Consolidated Services**: `IQobuzQualityManager` fully implemented and ready
- 🔄 **Migration Adapters**: Temporary compatibility layer in place  
- ⚠️ **Legacy Services**: Still in use, need gradual migration
- 📋 **Pattern Established**: Clear migration path documented

### Target State

- 🎯 **Consolidated Services**: Single responsibility managers
- 🗑️ **Migration Adapters**: Removed after transition complete
- ❌ **Legacy Services**: Removed and replaced
- ✨ **Clean Architecture**: Simplified service layer

## Consolidated Service Architecture

### IQobuzQualityManager (Primary Example)

**Replaces these legacy services:**

- `QobuzQualityService` <!-- TODO(docval): QobuzQualityService class not found in code as of 2026-05-31 -->
- `QualityMappingService` <!-- TODO(docval): QualityMappingService class not found in code as of 2026-05-31 -->
- `QualityFallbackService` <!-- TODO(docval): QualityFallbackService class not found in code as of 2026-05-31 -->
- `IntelligentQualityDetector` <!-- TODO(docval): IntelligentQualityDetector class not found in code as of 2026-05-31 -->
- `BatchStreamingUrlProvider` (quality aspects) <!-- TODO(docval): BatchStreamingUrlProvider class not found in code as of 2026-05-31 -->

**Key Capabilities:**

```csharp
public interface IQobuzQualityManager
{
    // Quality Detection & Analysis
    Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId);
    Task<AlbumQualityAnalysis> AnalyzeAlbumQualityAsync(QobuzAlbum album);
    
    // Quality Mapping & Fallback  
    QobuzQuality MapLidarrQuality(LidarrQualityProfile profile);
    List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred);
    
    // Quality Selection & Stream Management
    Task<QualitySelectionResult> SelectBestQualityAsync(string trackId, QobuzQuality preferred);
    Task<StreamInfo> GetStreamInfoAsync(string trackId, QobuzQuality quality);
    Task<BatchStreamResult> GetBatchStreamInfoAsync(List<string> trackIds, QobuzQuality quality);
}
```

## Migration Pattern

### Step 1: Update Constructor Dependencies

**Before:**

```csharp
public class LidarrAlbumRetriever 
{
    private readonly IQualityMappingService _qualityMappingService;
    private readonly IQualityFallbackService _qualityFallbackService;
    
    public LidarrAlbumRetriever(
        IQualityMappingService qualityMappingService,
        IQualityFallbackService qualityFallbackService,
        // ... other params
    )
    {
        _qualityMappingService = qualityMappingService;
        _qualityFallbackService = qualityFallbackService;
    }
}
```

**After:**

```csharp
public class LidarrAlbumRetriever 
{
    private readonly IQobuzQualityManager _qualityManager;
    
    public LidarrAlbumRetriever(
        IQobuzQualityManager qualityManager,
        // ... other params
    )
    {
        _qualityManager = qualityManager;
    }
}
```

### Step 2: Update Method Calls

**Legacy Pattern:**

```csharp
// Multiple service calls
var qualityRecommendation = _qualityMappingService.GetQualityRecommendation(album, profile);
var availableQualities = _qualityDetector.GetAvailableQualities(tracks);
var selectedQuality = _qualityFallbackService.SelectBestAvailableQuality(profile, availableQualities);
var streamUrl = _streamUrlProvider.GetStreamUrl(trackId, selectedQuality);
```

**Consolidated Pattern:**

```csharp
// Single service call with comprehensive functionality
var qualityResult = await _qualityManager.SelectBestQualityAsync(trackId, preferredQuality);
var streamInfo = await _qualityManager.GetStreamInfoAsync(trackId, qualityResult.SelectedQuality);
```

### Step 3: Batch Operations Optimization

**Legacy Pattern:**

```csharp
// Sequential calls for each track
foreach (var track in tracks)
{
    var quality = _qualityMappingService.MapQuality(track);
    var stream = await _streamUrlProvider.GetStreamUrlAsync(track.Id, quality);
    results.Add(stream);
}
```

**Consolidated Pattern:**

```csharp
// Optimized batch operation
var trackIds = tracks.Select(t => t.Id).ToList();
var batchResult = await _qualityManager.GetBatchStreamInfoAsync(trackIds, preferredQuality);
```

## Migration Status by Service

### 🎯 Ready for Migration

| Legacy Service | Status | Consolidated Replacement | Migration Complexity |
|----------------|--------|-------------------------|---------------------|
| `QobuzQualityService` | ✅ Ready | `IQobuzQualityManager` | Low |
| `QualityMappingService` | ✅ Ready | `IQobuzQualityManager` | Low |
| `QualityFallbackService` | ✅ Ready | `IQobuzQualityManager` | Low |
| `IntelligentQualityDetector` | ✅ Ready | `IQobuzQualityManager` | Medium |

### 📋 Services Using Legacy APIs (Need Migration)

| Service Class | Legacy Dependencies | Action Required |
|---------------|-------------------|-----------------|
| `LidarrAlbumRetriever` | `IQualityMappingService` <!-- TODO(docval): LidarrAlbumRetriever class not found in code as of 2026-05-31 --> | Update constructor + method calls |
| `QobuzValidationService` | `QobuzQualityService` | Update to `IQobuzQualityManager` |
| `QobuzApiService` | `QualityMappingService` <!-- TODO(docval): QobuzApiService class found but QualityMappingService dependency not verified as of 2026-05-31 --> | Update to consolidated interface |

### 🔧 Migration Adapters (Remove After Migration)

Located in `src/Services/Migration/`: <!-- TODO(docval): src/Services/Migration/ directory not found in code as of 2026-05-31 -->

- `QualityServiceMigrationAdapter.cs` - Remove after all consumers migrated

Located in `src/Services/Consolidated/ConsolidatedServiceRegistration.cs`: <!-- TODO(docval): ConsolidatedServiceRegistration.cs not found in code as of 2026-05-31 -->

- `MigrationAdapters.CreateQualityServiceAdapter()` - Remove after migration
- `MigrationAdapters.CreateMappingServiceAdapter()` - Remove after migration  
- `MigrationAdapters.CreateFallbackServiceAdapter()` - Remove after migration

## Implementation Checklist

### Phase 2A: Core Service Migration

- [ ] Migrate `LidarrAlbumRetriever` to `IQobuzQualityManager`
- [ ] Update method calls to use consolidated API
- [ ] Test functionality after migration
- [ ] Migrate `QobuzValidationService`
- [ ] Migrate `QobuzApiService`

### Phase 2B: Remove Legacy Services

- [ ] Remove `src/Services/QobuzQualityService.cs`
- [ ] Remove `src/Services/QualityMappingService.cs`
- [ ] Remove `src/Services/QualityFallbackService.cs`
- [ ] Remove interfaces: `IQualityMappingService`, `IQualityFallbackService`

### Phase 2C: Remove Migration Infrastructure

- [ ] Remove `src/Services/Migration/QualityServiceMigrationAdapter.cs`
- [ ] Remove migration adapter methods from `ConsolidatedServiceRegistration.cs`
- [ ] Clean up obsolete using statements

## Benefits After Migration

### 🚀 Performance Improvements

- **API Call Reduction**: Batch operations reduce individual API calls by ~60%
- **Memory Efficiency**: Single service instance vs multiple service objects
- **Caching Optimization**: Unified caching strategy across quality operations

### 🧹 Code Quality Improvements  

- **Reduced Complexity**: Single interface instead of 4+ service interfaces
- **Better Testability**: Comprehensive mocking through single service
- **Cleaner DI**: Fewer constructor parameters

### 📈 Maintainability Improvements

- **Single Responsibility**: Quality manager handles all quality concerns
- **Consistent API**: Unified patterns across quality operations
- **Future-Proof**: Easy to extend with new quality features

## Method Mapping Reference

### Quality Detection

| Legacy Method | Consolidated Method |
|---------------|-------------------|
| `QualityMappingService.GetQualityRecommendation()` | `IQobuzQualityManager.MapLidarrQuality()` |
| `IntelligentQualityDetector.DetectQualities()` | `IQobuzQualityManager.DetectAvailableQualitiesAsync()` |
| `QualityFallbackService.GetFallbackChain()` | `IQobuzQualityManager.GetQualityFallbackChain()` |

### Quality Selection  

| Legacy Method | Consolidated Method |
|---------------|-------------------|
| `QualityFallbackService.SelectBestAvailableQuality()` | `IQobuzQualityManager.SelectBestQualityAsync()` |
| `QualityMappingService.DoesQualityMeetProfileRequirements()` | Built into `SelectBestQualityAsync()` |

### Stream Management

| Legacy Method | Consolidated Method |
|---------------|-------------------|
| `BatchStreamingUrlProvider.GetStreamUrl()` | `IQobuzQualityManager.GetStreamInfoAsync()` |
| `BatchStreamingUrlProvider.GetBatchStreamUrls()` | `IQobuzQualityManager.GetBatchStreamInfoAsync()` |

## Testing Strategy

### Unit Tests

- Update existing service tests to use `IQobuzQualityManager`
- Add comprehensive tests for batch operations
- Test quality fallback chains

### Integration Tests  

- Verify API call reduction in batch operations
- Test end-to-end quality selection workflow
- Validate stream URL generation

### Performance Tests

- Benchmark API call reduction
- Memory usage comparison before/after
- Batch operation performance validation

## Rollback Strategy

If issues arise during migration:

1. **Revert Constructor Changes**: Switch back to legacy service interfaces
2. **Re-enable Migration Adapters**: Ensure compatibility layer works
3. **Gradual Rollback**: Migrate services back one-by-one if needed

The migration adapters provide a safety net during the transition period.

---

**Status**: Migration architecture documented and ready for implementation
**Next**: Begin Phase 2A core service migration
**Timeline**: Estimated 2-3 development sessions to complete
