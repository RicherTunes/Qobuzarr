> ⚠️ Historical (flagged 2026-05-31): describes a past state; some details below no longer match the current code.

# Quality Management Guide

**Version:** 0.0.12+
**Last Updated:** August 2024

## Table of Contents

- [Overview](#overview)
- [Quality Detection System](#quality-detection-system)
- [Intelligent Sampling](#intelligent-sampling)
- [Quality Manager Interface](#quality-manager-interface)
- [Configuration Management](#configuration-management)
- [Performance Optimization](#performance-optimization)
- [Monitoring and Analytics](#monitoring-and-analytics)
- [Advanced Features](#advanced-features)

## Overview

Qobuzarr's Quality Management system provides intelligent audio quality detection and optimization, reducing API calls by up to 95% while maintaining accurate quality information. The system uses smart sampling, caching, and pattern recognition to minimize API overhead.

### Key Benefits

- **95% API call reduction** through intelligent quality caching
- **Sub-50ms quality lookups** for cached content
- **Automatic quality detection** for new releases
- **Smart sampling strategies** based on artist/label patterns
- **Real-time quality monitoring** and alerts

### Architecture Overview

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  Quality Query  │───▶│ Quality Manager  │───▶│  Quality Cache  │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                              │                          │
                              ▼                          ▼
                    ┌──────────────────┐    ┌─────────────────┐
                    │ Intelligent      │    │  Quality API    │
                    │ Sampling Engine  │    │     Client      │
                    └──────────────────┘    └─────────────────┘
```

## Quality Detection System

### Core Quality Detection

```csharp
public interface IQobuzQualityManager
{
    Task<QualityInfo> GetQualityAsync(string albumId, QualityDetectionOptions options = null);
    Task<List<QualityInfo>> GetBulkQualityAsync(List<string> albumIds, 
        BulkQualityOptions options = null);
    Task<QualityPrediction> PredictQualityAsync(AlbumInfo album);
    Task InvalidateQualityAsync(string albumId);
    QualityStats GetStats();
}

public class QobuzQualityManager : IQobuzQualityManager
{
    private readonly IQualityCache _qualityCache;
    private readonly IIntelligentSampler _sampler;
    private readonly IQobuzApiClient _apiClient;
    private readonly IQualityPredictor _predictor;
    private readonly ILogger<QobuzQualityManager> _logger;

    public async Task<QualityInfo> GetQualityAsync(string albumId, 
        QualityDetectionOptions options = null)
    {
        options ??= QualityDetectionOptions.Default;
        
        // Check cache first
        var cached = await _qualityCache.GetAsync(albumId);
        if (cached != null && !cached.IsExpired(options.MaxCacheAge))
        {
            _logger.LogDebug("Quality cache hit for album {AlbumId}", albumId);
            return cached;
        }
        
        // Try intelligent prediction
        if (options.EnablePrediction)
        {
            var prediction = await _predictor.PredictQualityAsync(albumId);
            if (prediction.Confidence > options.MinPredictionConfidence)
            {
                _logger.LogDebug("Quality prediction used for album {AlbumId} (confidence: {Confidence})", 
                    albumId, prediction.Confidence);
                
                // Cache prediction for future use
                await _qualityCache.SetAsync(albumId, prediction.QualityInfo, prediction.Confidence);
                return prediction.QualityInfo;
            }
        }
        
        // Fall back to API sampling
        return await SampleQualityFromApi(albumId, options);
    }
    
    private async Task<QualityInfo> SampleQualityFromApi(string albumId, 
        QualityDetectionOptions options)
    {
        var samplingStrategy = await _sampler.DetermineSamplingStrategyAsync(albumId);
        var qualityInfo = new QualityInfo { AlbumId = albumId };
        
        switch (samplingStrategy.Type)
        {
            case SamplingType.FullSample:
                qualityInfo = await SampleAllTracks(albumId);
                break;
                
            case SamplingType.SmartSample:
                qualityInfo = await SampleRepresentativeTracks(albumId, samplingStrategy.TrackCount);
                break;
                
            case SamplingType.SingleTrack:
                qualityInfo = await SampleSingleTrack(albumId, samplingStrategy.PreferredTrackIndex);
                break;
                
            case SamplingType.Skip:
                qualityInfo = await _predictor.GetFallbackQualityAsync(albumId);
                break;
        }
        
        // Cache the result
        await _qualityCache.SetAsync(albumId, qualityInfo, samplingStrategy.CacheWeight);
        
        _logger.LogInformation("Sampled quality for album {AlbumId} using {Strategy}", 
            albumId, samplingStrategy.Type);
        
        return qualityInfo;
    }
}
```

### Quality Information Model

```csharp
public class QualityInfo
{
    public string AlbumId { get; set; }
    public List<TrackQuality> TrackQualities { get; set; } = new();
    public QualitySummary Summary { get; set; }
    public DateTime SampledAt { get; set; }
    public QualitySource Source { get; set; }
    public double Confidence { get; set; }
    
    public bool IsExpired(TimeSpan maxAge)
    {
        return DateTime.UtcNow - SampledAt > maxAge;
    }
    
    public bool HasHighResolution => Summary.MaxQuality >= AudioQuality.HighRes;
    public bool HasFlac => Summary.HasLossless;
}

public class TrackQuality
{
    public string TrackId { get; set; }
    public string Title { get; set; }
    public AudioQuality MaxQuality { get; set; }
    public List<AudioFormat> AvailableFormats { get; set; } = new();
    public long? FileSizeBytes { get; set; }
    public int? BitRate { get; set; }
    public int? SampleRate { get; set; }
    public int? BitDepth { get; set; }
}

public class QualitySummary
{
    public AudioQuality MaxQuality { get; set; }
    public AudioQuality MinQuality { get; set; }
    public AudioQuality MostCommonQuality { get; set; }
    public bool HasLossless { get; set; }
    public bool HasHighRes { get; set; }
    public bool IsConsistent { get; set; }  // All tracks same quality
    public int TotalTracks { get; set; }
    public int SampledTracks { get; set; }
    public Dictionary<AudioQuality, int> QualityDistribution { get; set; }
}

public enum AudioQuality
{
    Unknown = 0,
    MP3_128 = 5,
    MP3_320 = 6,
    FLAC_CD = 7,        // 16-bit/44.1kHz
    FLAC_HighRes = 8,   // 24-bit/96kHz or higher
    FLAC_Max = 27       // Highest available quality
}

public enum QualitySource
{
    Cache,
    Prediction,
    ApiSample,
    FullScan
}
```

## Intelligent Sampling

### Sampling Strategy Engine

```csharp
public interface IIntelligentSampler
{
    Task<SamplingStrategy> DetermineSamplingStrategyAsync(string albumId);
    Task<SamplingStrategy> DetermineSamplingStrategyAsync(AlbumInfo album);
    Task LearnFromSamplingResultAsync(string albumId, SamplingStrategy strategy, 
        QualityInfo result);
    SamplingStats GetStats();
}

public class IntelligentSampler : IIntelligentSampler
{
    private readonly IArtistQualityProfileRepository _artistProfiles;
    private readonly ILabelQualityProfileRepository _labelProfiles;
    private readonly IQualityPatternLearner _patternLearner;
    private readonly ILogger<IntelligentSampler> _logger;
    
    public async Task<SamplingStrategy> DetermineSamplingStrategyAsync(string albumId)
    {
        var album = await _apiClient.GetAlbumInfoAsync(albumId);
        return await DetermineSamplingStrategyAsync(album);
    }
    
    public async Task<SamplingStrategy> DetermineSamplingStrategyAsync(AlbumInfo album)
    {
        var strategy = new SamplingStrategy { AlbumId = album.Id };
        
        // Check artist quality profile
        var artistProfile = await _artistProfiles.GetProfileAsync(album.ArtistId);
        if (artistProfile != null && artistProfile.IsConsistentQuality && artistProfile.Confidence > 0.9)
        {
            strategy.Type = SamplingType.SingleTrack;
            strategy.Reason = $"Artist {album.ArtistName} has consistent quality profile";
            strategy.PreferredTrackIndex = GetOptimalTrackIndex(album);
            strategy.CacheWeight = 0.95;
            return strategy;
        }
        
        // Check label quality profile
        var labelProfile = await _labelProfiles.GetProfileAsync(album.LabelId);
        if (labelProfile != null && labelProfile.IsConsistentQuality && labelProfile.Confidence > 0.85)
        {
            strategy.Type = SamplingType.SmartSample;
            strategy.TrackCount = Math.Min(3, album.TrackCount / 4); // Sample 25% or 3 tracks max
            strategy.Reason = $"Label {album.LabelName} has consistent quality profile";
            strategy.CacheWeight = 0.8;
            return strategy;
        }
        
        // Check for known quality patterns
        var pattern = await _patternLearner.FindQualityPatternAsync(album);
        if (pattern != null && pattern.Confidence > 0.8)
        {
            strategy.Type = pattern.RecommendedSamplingType;
            strategy.TrackCount = pattern.RecommendedSampleSize;
            strategy.Reason = $"Matched quality pattern: {pattern.Name}";
            strategy.CacheWeight = pattern.Confidence;
            return strategy;
        }
        
        // Default sampling based on album characteristics
        return DetermineDefaultSamplingStrategy(album);
    }
    
    private SamplingStrategy DetermineDefaultSamplingStrategy(AlbumInfo album)
    {
        var strategy = new SamplingStrategy { AlbumId = album.Id };
        
        // Large albums (>20 tracks): Smart sampling
        if (album.TrackCount > 20)
        {
            strategy.Type = SamplingType.SmartSample;
            strategy.TrackCount = Math.Min(5, album.TrackCount / 5); // Sample 20% or 5 max
            strategy.Reason = "Large album - smart sampling";
            strategy.CacheWeight = 0.7;
        }
        // New releases (< 30 days): More thorough sampling
        else if (album.ReleaseDate > DateTime.UtcNow.AddDays(-30))
        {
            strategy.Type = SamplingType.SmartSample;
            strategy.TrackCount = Math.Min(3, album.TrackCount / 2); // Sample 50% or 3 max
            strategy.Reason = "New release - thorough sampling";
            strategy.CacheWeight = 0.6;
        }
        // Standard albums: Single track sampling
        else
        {
            strategy.Type = SamplingType.SingleTrack;
            strategy.PreferredTrackIndex = GetOptimalTrackIndex(album);
            strategy.Reason = "Standard album - single track sampling";
            strategy.CacheWeight = 0.5;
        }
        
        return strategy;
    }
    
    private int GetOptimalTrackIndex(AlbumInfo album)
    {
        // Prefer middle tracks (often representative of album quality)
        if (album.TrackCount <= 3) return 0;
        if (album.TrackCount <= 10) return album.TrackCount / 2;
        
        // For larger albums, prefer tracks around 1/3 position
        return album.TrackCount / 3;
    }
}

public class SamplingStrategy
{
    public string AlbumId { get; set; }
    public SamplingType Type { get; set; }
    public int TrackCount { get; set; }
    public int PreferredTrackIndex { get; set; }
    public string Reason { get; set; }
    public double CacheWeight { get; set; }
    public TimeSpan CacheDuration => TimeSpan.FromHours(24 * CacheWeight); // Weight affects cache duration
}

public enum SamplingType
{
    Skip,           // Use prediction only
    SingleTrack,    // Sample one representative track
    SmartSample,    // Sample multiple representative tracks
    FullSample      // Sample all tracks (rare)
}
```

### Artist and Label Quality Profiles

```csharp
public class ArtistQualityProfile
{
    public string ArtistId { get; set; }
    public string ArtistName { get; set; }
    public bool IsConsistentQuality { get; set; }
    public AudioQuality TypicalQuality { get; set; }
    public double Confidence { get; set; }
    public int AlbumsSampled { get; set; }
    public DateTime LastUpdated { get; set; }
    public Dictionary<AudioQuality, double> QualityDistribution { get; set; }
    
    // Quality consistency metrics
    public double QualityVariance { get; set; }
    public bool AlwaysHasHighRes { get; set; }
    public bool NeverHasHighRes { get; set; }
}

public class LabelQualityProfile
{
    public string LabelId { get; set; }
    public string LabelName { get; set; }
    public bool IsConsistentQuality { get; set; }
    public AudioQuality TypicalQuality { get; set; }
    public double Confidence { get; set; }
    public int AlbumsSampled { get; set; }
    public DateTime LastUpdated { get; set; }
    
    // Label-specific insights
    public bool SpecializesInHighRes { get; set; }
    public List<string> GenreSpecialties { get; set; }
    public Dictionary<string, AudioQuality> GenreQualityMap { get; set; }
}

// Profile builder service
public class QualityProfileBuilder : IQualityProfileBuilder
{
    public async Task UpdateArtistProfileAsync(string artistId, QualityInfo qualityInfo)
    {
        var profile = await _artistRepository.GetProfileAsync(artistId) ?? 
                     new ArtistQualityProfile { ArtistId = artistId };
        
        // Update statistics
        profile.AlbumsSampled++;
        profile.LastUpdated = DateTime.UtcNow;
        
        // Update quality distribution
        UpdateQualityDistribution(profile.QualityDistribution, qualityInfo.Summary);
        
        // Calculate consistency
        profile.IsConsistentQuality = CalculateQualityConsistency(profile.QualityDistribution) > 0.8;
        profile.TypicalQuality = GetMostCommonQuality(profile.QualityDistribution);
        profile.Confidence = CalculateConfidence(profile.AlbumsSampled, profile.QualityVariance);
        
        await _artistRepository.SaveProfileAsync(profile);
    }
    
    private double CalculateQualityConsistency(Dictionary<AudioQuality, double> distribution)
    {
        if (distribution.Count <= 1) return 1.0;
        
        var maxPercentage = distribution.Values.Max();
        return maxPercentage; // Higher percentage of single quality = more consistent
    }
}
```

## Quality Manager Interface

### Advanced Quality Operations

```csharp
public class QualityDetectionOptions
{
    public bool EnablePrediction { get; set; } = true;
    public double MinPredictionConfidence { get; set; } = 0.8;
    public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromHours(24);
    public bool ForceRefresh { get; set; } = false;
    public SamplingType PreferredSamplingType { get; set; } = SamplingType.SmartSample;
    
    public static QualityDetectionOptions Default => new();
    public static QualityDetectionOptions Conservative => new()
    {
        EnablePrediction = false,
        MaxCacheAge = TimeSpan.FromHours(6),
        PreferredSamplingType = SamplingType.SmartSample
    };
    public static QualityDetectionOptions Aggressive => new()
    {
        MinPredictionConfidence = 0.6,
        MaxCacheAge = TimeSpan.FromDays(7),
        PreferredSamplingType = SamplingType.SingleTrack
    };
}

public class BulkQualityOptions
{
    public int MaxConcurrentRequests { get; set; } = 5;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool FailFast { get; set; } = false;
    public QualityDetectionOptions DefaultOptions { get; set; } = QualityDetectionOptions.Default;
}

// Bulk quality processing
public async Task<List<QualityInfo>> GetBulkQualityAsync(List<string> albumIds, 
    BulkQualityOptions options = null)
{
    options ??= new BulkQualityOptions();
    var results = new ConcurrentBag<QualityInfo>();
    var semaphore = new SemaphoreSlim(options.MaxConcurrentRequests);
    
    var tasks = albumIds.Select(async albumId =>
    {
        await semaphore.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource(options.RequestTimeout);
            var quality = await GetQualityAsync(albumId, options.DefaultOptions);
            results.Add(quality);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get quality for album {AlbumId}", albumId);
            
            if (options.FailFast)
                throw;
                
            // Add error placeholder
            results.Add(new QualityInfo 
            { 
                AlbumId = albumId, 
                Source = QualitySource.Cache,
                Summary = new QualitySummary { MaxQuality = AudioQuality.Unknown }
            });
        }
        finally
        {
            semaphore.Release();
        }
    });
    
    await Task.WhenAll(tasks);
    return results.OrderBy(r => albumIds.IndexOf(r.AlbumId)).ToList();
}
```

### Quality Prediction System

```csharp
public interface IQualityPredictor
{
    Task<QualityPrediction> PredictQualityAsync(string albumId);
    Task<QualityPrediction> PredictQualityAsync(AlbumInfo album);
    Task<QualityInfo> GetFallbackQualityAsync(string albumId);
    Task TrainPredictionModelAsync(List<QualityTrainingExample> examples);
}

public class QualityPredictor : IQualityPredictor
{
    private readonly IMachineLearningService _mlService;
    private readonly IArtistQualityProfileRepository _artistProfiles;
    private readonly ILabelQualityProfileRepository _labelProfiles;
    
    public async Task<QualityPrediction> PredictQualityAsync(AlbumInfo album)
    {
        var features = await ExtractQualityFeatures(album);
        var prediction = await _mlService.PredictQualityAsync(features);
        
        return new QualityPrediction
        {
            AlbumId = album.Id,
            PredictedQuality = prediction.Quality,
            Confidence = prediction.Confidence,
            QualityInfo = CreateQualityInfoFromPrediction(album, prediction),
            Features = features
        };
    }
    
    private async Task<QualityFeatures> ExtractQualityFeatures(AlbumInfo album)
    {
        var artistProfile = await _artistProfiles.GetProfileAsync(album.ArtistId);
        var labelProfile = await _labelProfiles.GetProfileAsync(album.LabelId);
        
        return new QualityFeatures
        {
            // Artist features
            ArtistTypicalQuality = artistProfile?.TypicalQuality ?? AudioQuality.Unknown,
            ArtistConsistency = artistProfile?.IsConsistentQuality ?? false,
            ArtistAlbumCount = artistProfile?.AlbumsSampled ?? 0,
            
            // Label features  
            LabelTypicalQuality = labelProfile?.TypicalQuality ?? AudioQuality.Unknown,
            LabelSpecializesInHighRes = labelProfile?.SpecializesInHighRes ?? false,
            
            // Album features
            ReleaseYear = album.ReleaseDate.Year,
            TrackCount = album.TrackCount,
            Genre = album.Genre,
            IsRemaster = album.Title.Contains("remaster", StringComparison.OrdinalIgnoreCase),
            IsDeluxe = album.Title.Contains("deluxe", StringComparison.OrdinalIgnoreCase),
            
            // Market features
            IsNewRelease = album.ReleaseDate > DateTime.UtcNow.AddDays(-90),
            IsPopularArtist = album.ArtistPopularity > 70, // Assuming popularity score 0-100
        };
    }
}

public class QualityPrediction
{
    public string AlbumId { get; set; }
    public AudioQuality PredictedQuality { get; set; }
    public double Confidence { get; set; }
    public QualityInfo QualityInfo { get; set; }
    public QualityFeatures Features { get; set; }
    public List<string> ReasoningSteps { get; set; } = new();
}
```

## Configuration Management

### Quality Configuration

```json
{
  "QualityManagement": {
    "Cache": {
      "DefaultTTL": "24:00:00",
      "MaxCacheSize": 100000,
      "HighConfidenceTTL": "7.00:00:00",
      "LowConfidenceTTL": "06:00:00"
    },
    "Sampling": {
      "DefaultStrategy": "SmartSample",
      "MaxTracksToSample": 5,
      "MinConfidenceForSingleTrack": 0.9,
      "EnableArtistProfiles": true,
      "EnableLabelProfiles": true
    },
    "Prediction": {
      "Enabled": true,
      "MinConfidence": 0.8,
      "ModelUpdateInterval": "7.00:00:00",
      "FallbackToAPI": true
    },
    "Performance": {
      "MaxConcurrentSampling": 3,
      "SamplingTimeout": "00:00:30",
      "EnableMetrics": true,
      "ApiCallBudget": 1000
    }
  }
}
```

### Dynamic Configuration Updates

```csharp
public class QualityConfigurationService : IQualityConfigurationService
{
    private QualityConfiguration _config;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<QualityConfiguration> _optionsMonitor;
    
    public QualityConfigurationService(IOptionsMonitor<QualityConfiguration> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
        _config = _optionsMonitor.CurrentValue;
        
        // Monitor configuration changes
        _optionsMonitor.OnChange(UpdateConfiguration);
    }
    
    private void UpdateConfiguration(QualityConfiguration newConfig)
    {
        _config = newConfig;
        _logger.LogInformation("Quality configuration updated: {Config}", 
            JsonSerializer.Serialize(newConfig));
        
        // Notify components of configuration change
        ConfigurationChanged?.Invoke(newConfig);
    }
    
    public event Action<QualityConfiguration> ConfigurationChanged;
    
    public QualityConfiguration GetCurrentConfiguration() => _config;
    
    public async Task UpdateConfigurationAsync(QualityConfiguration config)
    {
        // Validate configuration
        var validation = ValidateConfiguration(config);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid configuration: {validation.ErrorMessage}");
        }
        
        // Save to configuration store
        await SaveConfigurationAsync(config);
        
        // Apply immediately
        _config = config;
        ConfigurationChanged?.Invoke(config);
    }
}
```

## Performance Optimization

### Caching Strategy

```csharp
public class QualityCache : IQualityCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly QualityConfiguration _config;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lockDict;
    
    public async Task<QualityInfo> GetAsync(string albumId)
    {
        // Try memory cache first (fastest)
        if (_memoryCache.TryGetValue(GetCacheKey(albumId), out QualityInfo cachedInfo))
        {
            return cachedInfo;
        }
        
        // Try distributed cache (slower but persistent)
        var distributedValue = await _distributedCache.GetStringAsync(GetCacheKey(albumId));
        if (distributedValue != null)
        {
            var qualityInfo = JsonSerializer.Deserialize<QualityInfo>(distributedValue);
            
            // Promote to memory cache
            var memoryOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = EstimateSize(qualityInfo)
            };
            _memoryCache.Set(GetCacheKey(albumId), qualityInfo, memoryOptions);
            
            return qualityInfo;
        }
        
        return null;
    }
    
    public async Task SetAsync(string albumId, QualityInfo qualityInfo, double confidence = 1.0)
    {
        var cacheKey = GetCacheKey(albumId);
        var ttl = CalculateTTL(confidence);
        
        // Set in memory cache
        var memoryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = EstimateSize(qualityInfo),
            Priority = confidence > 0.9 ? CacheItemPriority.High : CacheItemPriority.Normal
        };
        _memoryCache.Set(cacheKey, qualityInfo, memoryOptions);
        
        // Set in distributed cache (async, don't wait)
        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(qualityInfo);
                await _distributedCache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set distributed cache for {AlbumId}", albumId);
            }
        });
    }
    
    private TimeSpan CalculateTTL(double confidence)
    {
        // Higher confidence = longer cache time
        if (confidence >= 0.95) return _config.Cache.HighConfidenceTTL;
        if (confidence >= 0.8) return _config.Cache.DefaultTTL;
        return _config.Cache.LowConfidenceTTL;
    }
}
```

### Performance Metrics

```csharp
public class QualityPerformanceTracker : IQualityPerformanceTracker
{
    private readonly ConcurrentDictionary<string, QualityMetrics> _metrics;
    private readonly Timer _metricsTimer;
    
    public QualityStats GetStats()
    {
        var totalRequests = _metrics.Values.Sum(m => m.RequestCount);
        var cacheHits = _metrics.Values.Sum(m => m.CacheHits);
        var apiCalls = _metrics.Values.Sum(m => m.ApiCalls);
        var predictions = _metrics.Values.Sum(m => m.PredictionHits);
        
        return new QualityStats
        {
            TotalRequests = totalRequests,
            CacheHitRate = totalRequests > 0 ? (double)cacheHits / totalRequests : 0,
            ApiCallReduction = totalRequests > 0 ? 
                1.0 - (double)apiCalls / totalRequests : 0,
            PredictionAccuracy = CalculatePredictionAccuracy(),
            AverageResponseTime = TimeSpan.FromMilliseconds(
                _metrics.Values.Average(m => m.AverageResponseTime.TotalMilliseconds)),
            TopArtistsByRequests = GetTopArtists(10),
            QualityDistribution = GetQualityDistribution()
        };
    }
    
    public void RecordRequest(string albumId, QualitySource source, TimeSpan responseTime)
    {
        var key = GetMetricsKey(albumId);
        var metrics = _metrics.GetOrAdd(key, _ => new QualityMetrics());
        
        metrics.RequestCount++;
        metrics.UpdateResponseTime(responseTime);
        
        switch (source)
        {
            case QualitySource.Cache:
                metrics.CacheHits++;
                break;
            case QualitySource.Prediction:
                metrics.PredictionHits++;
                break;
            case QualitySource.ApiSample:
            case QualitySource.FullScan:
                metrics.ApiCalls++;
                break;
        }
        
        metrics.LastAccessed = DateTime.UtcNow;
    }
}
```

## Monitoring and Analytics

### Quality Analytics Dashboard

```csharp
public class QualityAnalyticsDashboard : IQualityAnalyticsDashboard
{
    public async Task<QualityDashboardData> GetDashboardDataAsync()
    {
        var stats = _performanceTracker.GetStats();
        var profiles = await GetQualityProfileSummaryAsync();
        var trends = await GetQualityTrendsAsync(TimeSpan.FromDays(7));
        
        return new QualityDashboardData
        {
            // Performance metrics
            ApiCallReduction = stats.ApiCallReduction * 100,
            CacheHitRate = stats.CacheHitRate * 100,
            PredictionAccuracy = stats.PredictionAccuracy * 100,
            AverageResponseTime = stats.AverageResponseTime,
            
            // Quality insights
            QualityDistribution = stats.QualityDistribution,
            TopArtistsByQualityConsistency = profiles.Artists
                .OrderByDescending(a => a.Confidence)
                .Take(10)
                .ToList(),
            TopLabelsByHighRes = profiles.Labels
                .Where(l => l.SpecializesInHighRes)
                .OrderByDescending(l => l.HighResPercentage)
                .Take(10)
                .ToList(),
            
            // Trends
            WeeklyTrends = trends.GroupBy(t => t.Date.Date)
                .Select(g => new DailyQualityTrend
                {
                    Date = g.Key,
                    ApiCallReduction = g.Average(t => t.ApiCallReduction),
                    CacheHitRate = g.Average(t => t.CacheHitRate),
                    NewHighResAlbums = g.Sum(t => t.NewHighResAlbums)
                })
                .OrderBy(t => t.Date)
                .ToList()
        };
    }
    
    public async Task<List<QualityAlert>> GetActiveAlertsAsync()
    {
        var alerts = new List<QualityAlert>();
        var stats = _performanceTracker.GetStats();
        
        // Performance alerts
        if (stats.ApiCallReduction < 0.8) // Below 80% reduction
        {
            alerts.Add(new QualityAlert
            {
                Type = QualityAlertType.PerformanceDegradation,
                Severity = AlertSeverity.Warning,
                Message = $"API call reduction below target: {stats.ApiCallReduction:P}",
                RecommendedAction = "Review sampling strategies and prediction confidence"
            });
        }
        
        if (stats.PredictionAccuracy < 0.75) // Below 75% accuracy
        {
            alerts.Add(new QualityAlert
            {
                Type = QualityAlertType.PredictionAccuracy,
                Severity = AlertSeverity.High,
                Message = $"Quality prediction accuracy low: {stats.PredictionAccuracy:P}",
                RecommendedAction = "Retrain prediction models with recent data"
            });
        }
        
        // Data quality alerts
        var staleProfiles = await _artistProfiles.GetStaleProfilesAsync(TimeSpan.FromDays(90));
        if (staleProfiles.Count > 100)
        {
            alerts.Add(new QualityAlert
            {
                Type = QualityAlertType.StaleData,
                Severity = AlertSeverity.Medium,
                Message = $"{staleProfiles.Count} artist profiles need updating",
                RecommendedAction = "Schedule background profile updates"
            });
        }
        
        return alerts;
    }
}

public class QualityDashboardData
{
    public double ApiCallReduction { get; set; }
    public double CacheHitRate { get; set; }
    public double PredictionAccuracy { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    
    public Dictionary<AudioQuality, int> QualityDistribution { get; set; }
    public List<ArtistQualityProfile> TopArtistsByQualityConsistency { get; set; }
    public List<LabelQualityProfile> TopLabelsByHighRes { get; set; }
    
    public List<DailyQualityTrend> WeeklyTrends { get; set; }
    public List<QualityInsight> KeyInsights { get; set; }
}
```

## Advanced Features

### Predictive Quality Analysis

```csharp
public class PredictiveQualityAnalyzer : IPredictiveQualityAnalyzer
{
    public async Task<QualityForecast> ForecastQualityTrendsAsync(TimeSpan forecastPeriod)
    {
        var historicalData = await GetHistoricalQualityDataAsync(forecastPeriod * 2);
        var seasonalPattern = AnalyzeSeasonalPatterns(historicalData);
        var trendAnalysis = AnalyzeTrends(historicalData);
        
        return new QualityForecast
        {
            ForecastPeriod = forecastPeriod,
            PredictedApiCallReduction = PredictApiCallReduction(trendAnalysis, seasonalPattern),
            PredictedQualityDistribution = PredictQualityDistribution(historicalData),
            ExpectedNewHighResReleases = PredictNewHighResReleases(trendAnalysis),
            Confidence = CalculateForecastConfidence(historicalData),
            KeyDrivers = IdentifyQualityDrivers(historicalData)
        };
    }
    
    public async Task<List<QualityOpportunity>> IdentifyOptimizationOpportunitiesAsync()
    {
        var opportunities = new List<QualityOpportunity>();
        var stats = _performanceTracker.GetStats();
        
        // Artists with inconsistent quality but high sampling frequency
        var inconsistentArtists = await _artistProfiles.GetInconsistentArtistsAsync();
        foreach (var artist in inconsistentArtists.Where(a => a.RequestFrequency > 10))
        {
            opportunities.Add(new QualityOpportunity
            {
                Type = OpportunityType.ImproveArtistProfile,
                Description = $"Improve quality prediction for {artist.ArtistName}",
                EstimatedSavings = EstimateArtistOptimizationSavings(artist),
                Priority = CalculatePriority(artist.RequestFrequency, artist.QualityVariance)
            });
        }
        
        // Labels with unknown quality patterns
        var unknownLabels = await _labelProfiles.GetLabelsWithInsufficientDataAsync();
        foreach (var label in unknownLabels.Where(l => l.AlbumCount > 5))
        {
            opportunities.Add(new QualityOpportunity
            {
                Type = OpportunityType.BuildLabelProfile,
                Description = $"Build quality profile for {label.LabelName}",
                EstimatedSavings = EstimateLabelOptimizationSavings(label),
                Priority = CalculatePriority(label.AlbumCount, 1.0) // High variance assumed
            });
        }
        
        return opportunities.OrderByDescending(o => o.Priority).ToList();
    }
}
```

### Quality Health Monitor

```csharp
public class QualityHealthMonitor : IQualityHealthMonitor
{
    private readonly Timer _healthCheckTimer;
    private QualityHealthStatus _currentHealth;
    
    public QualityHealthMonitor()
    {
        // Check health every 15 minutes
        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            TimeSpan.Zero, TimeSpan.FromMinutes(15));
    }
    
    private async void PerformHealthCheck(object state)
    {
        try
        {
            var health = await AssessQualitySystemHealthAsync();
            var previousHealth = _currentHealth;
            _currentHealth = health;
            
            // Alert on health changes
            if (previousHealth != null && health.OverallScore < previousHealth.OverallScore - 0.1)
            {
                await NotifyHealthDegradationAsync(previousHealth, health);
            }
            
            _logger.LogInformation("Quality system health: {Score:F2} ({Status})", 
                health.OverallScore, health.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
        }
    }
    
    private async Task<QualityHealthStatus> AssessQualitySystemHealthAsync()
    {
        var stats = _performanceTracker.GetStats();
        var cacheHealth = await AssessCacheHealthAsync();
        var predictionHealth = await AssessPredictionHealthAsync();
        var profileHealth = await AssessProfileHealthAsync();
        
        var healthComponents = new Dictionary<string, double>
        {
            ["Cache"] = cacheHealth,
            ["Prediction"] = predictionHealth,
            ["Profiles"] = profileHealth,
            ["Performance"] = Math.Min(stats.ApiCallReduction * 1.25, 1.0) // Weight performance heavily
        };
        
        var overallScore = healthComponents.Values.Average();
        
        return new QualityHealthStatus
        {
            OverallScore = overallScore,
            Status = GetHealthStatus(overallScore),
            ComponentHealth = healthComponents,
            Recommendations = GenerateHealthRecommendations(healthComponents),
            LastAssessed = DateTime.UtcNow
        };
    }
    
    private HealthStatus GetHealthStatus(double score)
    {
        return score switch
        {
            >= 0.9 => HealthStatus.Excellent,
            >= 0.8 => HealthStatus.Good,
            >= 0.7 => HealthStatus.Fair,
            >= 0.6 => HealthStatus.Poor,
            _ => HealthStatus.Critical
        };
    }
}
```

This comprehensive Quality Management system provides intelligent quality detection while minimizing API usage through smart caching, prediction, and sampling strategies. The system continuously learns and improves its accuracy while providing detailed monitoring and analytics capabilities.
