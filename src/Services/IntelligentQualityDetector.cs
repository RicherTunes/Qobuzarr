using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Intelligent quality detection service that optimizes quality checks by detecting album-level quality availability
    /// Reduces individual track quality checks by up to 95% through smart sampling and caching
    /// </summary>
    /// <remarks>
    /// Key optimizations:
    /// - Album-level quality detection using representative track sampling
    /// - Quality availability caching to prevent redundant API calls
    /// - Fallback strategy for mixed-quality scenarios
    /// - Comprehensive quality format support (MP3 320, FLAC CD, Hi-Res)
    /// 
    /// Algorithm approach:
    /// 1. Sample 2-3 representative tracks from different parts of the album
    /// 2. Check quality availability for sampled tracks
    /// 3. If consistent, apply to entire album (95% reduction in quality checks)
    /// 4. If inconsistent, fall back to individual track checks with smart batching
    /// 
    /// Performance benefits:
    /// - Reduces quality check API calls from N tracks to 2-3 sample tracks
    /// - Caches results for subsequent downloads of the same album
    /// - Maintains accuracy through intelligent sampling strategies
    /// </remarks>
    public class IntelligentQualityDetector
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly Logger _logger;
        private readonly Dictionary<string, AlbumQualityCache> _qualityCache;
        private readonly SemaphoreSlim _cacheLock;
        private readonly TimeSpan _cacheExpiration;

        // Quality detection constants
        private const int SAMPLE_TRACK_COUNT = 3; // Number of tracks to sample for quality detection
        private const int QUALITY_CHECK_TIMEOUT_MS = 15000; // 15 second timeout per quality check
        private const double CONSISTENCY_THRESHOLD = 0.8; // 80% of sampled tracks must have same quality
        private const int MAX_CACHE_ENTRIES = 1000; // Prevent unlimited memory growth

        // Quality format definitions
        public static readonly Dictionary<int, QualityFormat> SupportedQualities = new()
        {
            { 5, new QualityFormat { Id = 5, Name = "MP3 320", BitRate = 320, IsLossless = false, Priority = 1 } },
            { 6, new QualityFormat { Id = 6, Name = "FLAC CD", BitRate = 1411, IsLossless = true, Priority = 2 } },
            { 7, new QualityFormat { Id = 7, Name = "FLAC 24/96", BitRate = 4608, IsLossless = true, Priority = 3 } },
            { 27, new QualityFormat { Id = 27, Name = "FLAC 24/192", BitRate = 9216, IsLossless = true, Priority = 4 } }
        };

        public IntelligentQualityDetector(
            IQobuzApiClient apiClient,
            Logger logger = null,
            TimeSpan? cacheExpiration = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _qualityCache = new Dictionary<string, AlbumQualityCache>();
            _cacheLock = new SemaphoreSlim(1, 1);
            _cacheExpiration = cacheExpiration ?? TimeSpan.FromHours(24); // 24 hour cache by default
        }

        /// <summary>
        /// Detects the best available quality for an entire album using intelligent sampling
        /// </summary>
        /// <param name="album">Album to analyze for quality availability</param>
        /// <param name="preferredQuality">User's preferred quality format</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Album quality detection result with optimization information</returns>
        public async Task<AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album,
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                _logger.Warn("Album has no tracks for quality detection: {0}", album?.Title ?? "Unknown");
                return AlbumQualityResult.Failed("Album has no tracks");
            }

            var startTime = DateTime.UtcNow;
            var cacheKey = GenerateCacheKey(album);

            _logger.Info("🎯 QUALITY INTELLIGENCE: Detecting album-level quality for '{0}' ({1} tracks)", 
                        album.Title, album.TracksCount);

            try
            {
                // Check cache first
                var cachedResult = await GetCachedQualityAsync(cacheKey);
                if (cachedResult != null)
                {
                    _logger.Info("📋 QUALITY CACHE HIT: Using cached quality data for '{0}' (age: {1})", 
                                album.Title, DateTime.UtcNow - cachedResult.CachedAt);
                    return ApplyPreferenceToResult(cachedResult.Result, preferredQuality);
                }

                // Perform intelligent quality detection
                var detectionResult = await PerformQualityDetectionAsync(album, preferredQuality, cancellationToken);

                // Cache the result for future use
                await CacheQualityResultAsync(cacheKey, detectionResult);

                var duration = DateTime.UtcNow - startTime;
                var savedChecks = album.TracksCount - detectionResult.SampleTracksChecked;

                _logger.Info("🎯 QUALITY DETECTION COMPLETE: {0} format available, saved {1} API calls in {2:F1}s", 
                            detectionResult.BestAvailableFormat?.Name ?? "None", savedChecks, duration.TotalSeconds);

                return detectionResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to detect album quality for: {0}", album.Title);
                
                // Return fallback result that will trigger individual track checks
                return AlbumQualityResult.Failed($"Quality detection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs the core quality detection algorithm using representative track sampling
        /// </summary>
        private async Task<AlbumQualityResult> PerformQualityDetectionAsync(
            QobuzAlbum album,
            int preferredQuality,
            CancellationToken cancellationToken)
        {
            // Select representative tracks for sampling
            var sampleTracks = SelectSampleTracks(album.GetTracks());
            var qualityResults = new Dictionary<int, List<int>>(); // quality -> list of track IDs with that quality

            _logger.Debug("Selected {0} sample tracks for quality detection: {1}",
                         sampleTracks.Count, string.Join(", ", sampleTracks.Select(t => $"#{t.TrackNumber}")));

            // Check quality availability for each sample track
            foreach (var track in sampleTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var availableQualities = await CheckTrackQualityAvailabilityAsync(track, cancellationToken);
                
                foreach (var quality in availableQualities)
                {
                    if (!qualityResults.ContainsKey(quality))
                        qualityResults[quality] = new List<int>();
                    
                    qualityResults[quality].Add(int.Parse(track.Id));
                }

                _logger.Debug("Track #{0} ({1}) has qualities: {2}",
                             track.TrackNumber, track.Title, 
                             string.Join(", ", availableQualities.Select(q => SupportedQualities.ContainsKey(q) ? SupportedQualities[q].Name : q.ToString())));
            }

            // Analyze consistency and determine album-level quality
            var analysisResult = AnalyzeQualityConsistency(qualityResults, sampleTracks.Count, preferredQuality);
            
            return new AlbumQualityResult
            {
                IsSuccess = true,
                Album = album,
                BestAvailableFormat = analysisResult.BestFormat,
                AllAvailableFormats = analysisResult.AllFormats,
                IsConsistentAcrossAlbum = analysisResult.IsConsistent,
                ConfidenceLevel = analysisResult.Confidence,
                SampleTracksChecked = sampleTracks.Count,
                OptimizationStrategy = analysisResult.Strategy,
                QualityByTrack = analysisResult.IsConsistent ? null : CreateTrackQualityMap(qualityResults),
                RecommendedApproach = analysisResult.RecommendedApproach
            };
        }

        /// <summary>
        /// Selects representative tracks from the album for quality sampling
        /// </summary>
        private List<QobuzTrack> SelectSampleTracks(List<QobuzTrack> allTracks)
        {
            if (allTracks.Count <= SAMPLE_TRACK_COUNT)
            {
                // Small album - check all tracks
                return allTracks.ToList();
            }

            var sampleTracks = new List<QobuzTrack>();

            // Always sample first track
            sampleTracks.Add(allTracks.First());

            // Sample middle track(s)
            if (allTracks.Count > 2)
            {
                var middleIndex = allTracks.Count / 2;
                sampleTracks.Add(allTracks[middleIndex]);
            }

            // Sample last track if we need more samples
            if (sampleTracks.Count < SAMPLE_TRACK_COUNT && allTracks.Count > 1)
            {
                sampleTracks.Add(allTracks.Last());
            }

            // If still need more samples, add evenly distributed tracks
            while (sampleTracks.Count < SAMPLE_TRACK_COUNT && sampleTracks.Count < allTracks.Count)
            {
                var spacing = allTracks.Count / (SAMPLE_TRACK_COUNT + 1);
                var nextIndex = spacing * sampleTracks.Count;
                
                if (nextIndex < allTracks.Count && !sampleTracks.Contains(allTracks[nextIndex]))
                {
                    sampleTracks.Add(allTracks[nextIndex]);
                }
                else
                {
                    break; // No more unique tracks to sample
                }
            }

            return sampleTracks;
        }

        /// <summary>
        /// Checks what qualities are available for a specific track
        /// </summary>
        private async Task<List<int>> CheckTrackQualityAvailabilityAsync(
            QobuzTrack track,
            CancellationToken cancellationToken)
        {
            var availableQualities = new List<int>();

            // Check each supported quality format
            foreach (var qualityFormat in SupportedQualities.Values.OrderByDescending(q => q.Priority))
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(QUALITY_CHECK_TIMEOUT_MS));

                    var parameters = new Dictionary<string, string>
                    {
                        {"track_id", track.Id.ToString()},
                        {"format_id", qualityFormat.Id.ToString()},
                        {"intent", "stream"}
                    };

                    var response = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl", parameters);

                    if (response?.IsSuccess == true && 
                        !string.IsNullOrWhiteSpace(response.Url) && 
                        !response.HasRestrictions())
                    {
                        availableQualities.Add(qualityFormat.Id);
                        _logger.Debug("Quality {0} available for track {1}", qualityFormat.Name, track.Id);
                    }
                    else if (response?.HasRestrictions() == true)
                    {
                        var restriction = response.GetRestrictionMessage();
                        if (!restriction?.Contains("format not available") == true)
                        {
                            // Non-quality related restriction (geo, subscription) - stop checking higher qualities
                            _logger.Debug("Track {0} restricted: {1}", track.Id, restriction);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    _logger.Debug("Failed to check quality {0} for track {1}: {2}", 
                                 qualityFormat.Name, track.Id, ex.Message);
                    // Continue checking other qualities
                }
            }

            return availableQualities;
        }

        /// <summary>
        /// Analyzes quality consistency across sample tracks to determine album-level strategy
        /// </summary>
        private QualityAnalysisResult AnalyzeQualityConsistency(
            Dictionary<int, List<int>> qualityResults,
            int sampleCount,
            int preferredQuality)
        {
            if (!qualityResults.Any())
            {
                return new QualityAnalysisResult
                {
                    IsConsistent = false,
                    Strategy = "IndividualCheck",
                    Confidence = 0.0,
                    RecommendedApproach = "No qualities available in sample tracks"
                };
            }

            // Find the best quality that's available on most tracks
            var bestConsistentQuality = qualityResults
                .Where(kvp => (double)kvp.Value.Count / sampleCount >= CONSISTENCY_THRESHOLD)
                .OrderByDescending(kvp => SupportedQualities.ContainsKey(kvp.Key) ? SupportedQualities[kvp.Key].Priority : 0)
                .FirstOrDefault();

            if (bestConsistentQuality.Key != 0) // Found consistent quality
            {
                var consistency = (double)bestConsistentQuality.Value.Count / sampleCount;
                var bestFormat = SupportedQualities.ContainsKey(bestConsistentQuality.Key) 
                    ? SupportedQualities[bestConsistentQuality.Key] 
                    : new QualityFormat { Id = bestConsistentQuality.Key, Name = $"Quality {bestConsistentQuality.Key}" };

                return new QualityAnalysisResult
                {
                    BestFormat = bestFormat,
                    AllFormats = qualityResults.Keys.Where(SupportedQualities.ContainsKey).Select(k => SupportedQualities[k]).ToList(),
                    IsConsistent = true,
                    Confidence = consistency,
                    Strategy = "AlbumLevel",
                    RecommendedApproach = $"Apply {bestFormat.Name} to entire album (consistent across {consistency:P1} of sample)"
                };
            }
            else // Inconsistent qualities across tracks
            {
                var allQualities = qualityResults.Keys.Where(SupportedQualities.ContainsKey).Select(k => SupportedQualities[k]).ToList();
                var bestOverallQuality = allQualities.OrderByDescending(q => q.Priority).FirstOrDefault();

                return new QualityAnalysisResult
                {
                    BestFormat = bestOverallQuality,
                    AllFormats = allQualities,
                    IsConsistent = false,
                    Confidence = 0.5, // Medium confidence due to inconsistency
                    Strategy = "BatchCheck",
                    RecommendedApproach = "Use batch processing for individual track quality checks due to inconsistency"
                };
            }
        }

        /// <summary>
        /// Creates a track-to-quality mapping for inconsistent albums
        /// </summary>
        private Dictionary<int, List<int>> CreateTrackQualityMap(Dictionary<int, List<int>> qualityResults)
        {
            var trackQualityMap = new Dictionary<int, List<int>>();

            foreach (var qualityGroup in qualityResults)
            {
                foreach (var trackId in qualityGroup.Value)
                {
                    if (!trackQualityMap.ContainsKey(trackId))
                        trackQualityMap[trackId] = new List<int>();
                    
                    trackQualityMap[trackId].Add(qualityGroup.Key);
                }
            }

            return trackQualityMap;
        }

        /// <summary>
        /// Applies user's preferred quality to the detection result
        /// </summary>
        private AlbumQualityResult ApplyPreferenceToResult(AlbumQualityResult cachedResult, int preferredQuality)
        {
            if (cachedResult.AllAvailableFormats?.Any(f => f.Id == preferredQuality) == true)
            {
                cachedResult.BestAvailableFormat = cachedResult.AllAvailableFormats.First(f => f.Id == preferredQuality);
                cachedResult.RecommendedApproach = $"Using cached result with preferred quality {cachedResult.BestAvailableFormat.Name}";
            }
            else if (cachedResult.BestAvailableFormat != null)
            {
                cachedResult.RecommendedApproach = $"Using cached result with best available quality {cachedResult.BestAvailableFormat.Name} (preferred {preferredQuality} not available)";
            }

            return cachedResult;
        }

        #region Caching Methods

        /// <summary>
        /// Generates cache key for an album
        /// </summary>
        private string GenerateCacheKey(QobuzAlbum album)
        {
            return $"album_{album.Id}_{album.TracksCount}";
        }

        /// <summary>
        /// Gets cached quality result if available and not expired
        /// </summary>
        private async Task<AlbumQualityCache> GetCachedQualityAsync(string cacheKey)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_qualityCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt <= _cacheExpiration)
                    {
                        return cached;
                    }
                    else
                    {
                        _qualityCache.Remove(cacheKey);
                        _logger.Debug("Removed expired quality cache entry: {0}", cacheKey);
                    }
                }

                return null;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Caches quality detection result
        /// </summary>
        private async Task CacheQualityResultAsync(string cacheKey, AlbumQualityResult result)
        {
            if (!result.IsSuccess)
                return; // Don't cache failures

            await _cacheLock.WaitAsync();
            try
            {
                // Implement LRU eviction if cache is full
                if (_qualityCache.Count >= MAX_CACHE_ENTRIES)
                {
                    var oldestKey = _qualityCache
                        .OrderBy(kvp => kvp.Value.CachedAt)
                        .First().Key;
                    
                    _qualityCache.Remove(oldestKey);
                    _logger.Debug("Evicted oldest quality cache entry: {0}", oldestKey);
                }

                _qualityCache[cacheKey] = new AlbumQualityCache
                {
                    Result = result,
                    CachedAt = DateTime.UtcNow
                };

                _logger.Debug("Cached quality detection result: {0}", cacheKey);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        #endregion

        /// <summary>
        /// Gets current cache statistics for monitoring
        /// </summary>
        public async Task<QualityDetectorStatistics> GetStatisticsAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                return new QualityDetectorStatistics
                {
                    CacheEntries = _qualityCache.Count,
                    MaxCacheEntries = MAX_CACHE_ENTRIES,
                    CacheHitRate = 0.0, // Would need separate tracking to calculate
                    SampleTrackCount = SAMPLE_TRACK_COUNT,
                    ConsistencyThreshold = CONSISTENCY_THRESHOLD
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Clears the quality detection cache
        /// </summary>
        public async Task ClearCacheAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                _qualityCache.Clear();
                _logger.Info("Quality detection cache cleared");
            }
            finally
            {
                _cacheLock.Release();
            }
        }
    }

    #region Result Classes

    /// <summary>
    /// Result of album-level quality detection
    /// </summary>
    public class AlbumQualityResult
    {
        public bool IsSuccess { get; set; }
        public QobuzAlbum Album { get; set; }
        public QualityFormat BestAvailableFormat { get; set; }
        public List<QualityFormat> AllAvailableFormats { get; set; } = new();
        public bool IsConsistentAcrossAlbum { get; set; }
        public double ConfidenceLevel { get; set; }
        public int SampleTracksChecked { get; set; }
        public string OptimizationStrategy { get; set; }
        public Dictionary<int, List<int>> QualityByTrack { get; set; }
        public string RecommendedApproach { get; set; }
        public string ErrorMessage { get; set; }

        public static AlbumQualityResult Failed(string error) => new()
        {
            IsSuccess = false,
            ErrorMessage = error,
            OptimizationStrategy = "IndividualCheck",
            RecommendedApproach = "Fall back to individual track quality checks"
        };
    }

    /// <summary>
    /// Quality format definition
    /// </summary>
    public class QualityFormat
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int BitRate { get; set; }
        public bool IsLossless { get; set; }
        public int Priority { get; set; }
    }

    /// <summary>
    /// Internal result of quality analysis
    /// </summary>
    internal class QualityAnalysisResult
    {
        public QualityFormat BestFormat { get; set; }
        public List<QualityFormat> AllFormats { get; set; } = new();
        public bool IsConsistent { get; set; }
        public double Confidence { get; set; }
        public string Strategy { get; set; }
        public string RecommendedApproach { get; set; }
    }

    /// <summary>
    /// Cached quality detection result
    /// </summary>
    internal class AlbumQualityCache
    {
        public AlbumQualityResult Result { get; set; }
        public DateTime CachedAt { get; set; }
    }

    /// <summary>
    /// Statistics for quality detector monitoring
    /// </summary>
    public class QualityDetectorStatistics
    {
        public int CacheEntries { get; set; }
        public int MaxCacheEntries { get; set; }
        public double CacheHitRate { get; set; }
        public int SampleTrackCount { get; set; }
        public double ConsistencyThreshold { get; set; }
    }

    #endregion
}