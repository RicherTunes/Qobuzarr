using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Monitoring;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Consolidated quality management service that handles all quality-related operations.
    /// Replaces: QobuzQualityService, QualityMappingService, QualityFallbackService, 
    /// IntelligentQualityDetector, and quality aspects of BatchStreamingUrlProvider.
    /// </summary>
    public class QobuzQualityManager : IQobuzQualityManager
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IQobuzLogger _logger;
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly Dictionary<string, AlbumQualityCache> _qualityCache;
        private readonly SemaphoreSlim _cacheLock;
        private readonly TimeSpan _cacheExpiration;

        // Quality format definitions (consolidated from multiple services)
        public static readonly Dictionary<int, QualityFormat> QobuzQualityFormats = new()
        {
            { 5, new QualityFormat { Id = 5, Name = "MP3 320", DisplayName = "MP3 320kbps", BitRate = 320, IsLossless = false, Priority = 1 } },
            { 6, new QualityFormat { Id = 6, Name = "FLAC CD", DisplayName = "FLAC CD 16bit/44.1kHz", BitRate = 1411, IsLossless = true, Priority = 2 } },
            { 7, new QualityFormat { Id = 7, Name = "FLAC Hi-Res 96", DisplayName = "FLAC Hi-Res 24bit/96kHz", BitRate = 4608, IsLossless = true, Priority = 3 } },
            { 27, new QualityFormat { Id = 27, Name = "FLAC Hi-Res 192", DisplayName = "FLAC Hi-Res 24bit/192kHz", BitRate = 9216, IsLossless = true, Priority = 4 } }
        };

        // Lidarr quality profile mappings (consolidated from QualityMappingService)
        private static readonly Dictionary<string, int> LidarrQualityMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            // Hi-Res patterns
            ["Hi-Res"] = 27, ["HiRes"] = 27, ["High Resolution"] = 27,
            ["24bit"] = 27, ["24-bit"] = 27, ["192khz"] = 27, ["96khz"] = 7,
            
            // CD Quality patterns
            ["Lossless"] = 6, ["FLAC"] = 6, ["CD"] = 6,
            ["16bit"] = 6, ["16-bit"] = 6, ["44.1khz"] = 6,
            
            // Lossy patterns
            ["MP3"] = 5, ["320"] = 5, ["Lossy"] = 5, ["Standard"] = 5
        };

        // Album quality detection settings
        private const int SAMPLE_TRACK_COUNT = 3;
        private const double CONSISTENCY_THRESHOLD = 0.8;
        private const int MAX_CACHE_ENTRIES = 1000;

        public QobuzQualityManager(
            IQobuzApiClient apiClient,
            IQobuzLogger logger,
            IPerformanceMonitor performanceMonitor = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor ?? new PerformanceMonitor(logger);
            _qualityCache = new Dictionary<string, AlbumQualityCache>();
            _cacheLock = new SemaphoreSlim(1, 1);
            _cacheExpiration = TimeSpan.FromHours(24);
        }

        #region Quality Detection

        /// <summary>
        /// Detects available qualities for a single track.
        /// </summary>
        public async Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            return await _performanceMonitor.TrackOperationAsync(
                "QualityDetection.SingleTrack",
                async () => await DetectAvailableQualitiesInternalAsync(trackId, cancellationToken),
                new Dictionary<string, object> { ["track_id"] = trackId }
            );
        }

        private async Task<QualityDetectionResult> DetectAvailableQualitiesInternalAsync(string trackId, CancellationToken cancellationToken)
        {
            _logger.Debug("Detecting available qualities for track {0}", trackId);
            
            var result = new QualityDetectionResult
            {
                TrackId = trackId,
                AvailableQualities = new List<QualityFormat>(),
                CheckedAt = DateTime.UtcNow
            };

            foreach (var qualityFormat in QobuzQualityFormats.Values.OrderByDescending(q => q.Priority))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var streamInfo = await _performanceMonitor.TrackOperationAsync(
                        "API.GetStreamInfo",
                        () => GetStreamInfoInternalAsync(trackId, qualityFormat.Id, cancellationToken),
                        new Dictionary<string, object> 
                        { 
                            ["track_id"] = trackId,
                            ["quality_format"] = qualityFormat.Id,
                            ["quality_name"] = qualityFormat.Name
                        }
                    );
                    
                    if (IsValidStreamUrl(streamInfo?.Url))
                    {
                        result.AvailableQualities.Add(qualityFormat);
                        _logger.Debug("Quality {0} available for track {1}", qualityFormat.Name, trackId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} not available for track {1}: {2}", qualityFormat.Name, trackId, ex.Message);
                }
            }

            result.HighestAvailableQuality = result.AvailableQualities.FirstOrDefault();
            _logger.Info("Track {0} has {1} available qualities, highest: {2}", 
                trackId, result.AvailableQualities.Count, result.HighestAvailableQuality?.Name ?? "None");
            
            return result;
        }

        /// <summary>
        /// Intelligently detects album-level quality availability using sampling.
        /// </summary>
        public async Task<AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                return AlbumQualityResult.Failed("Album has no tracks");
            }

            return await _performanceMonitor.TrackOperationAsync(
                "QualityDetection.Album",
                async () => await DetectAlbumQualityInternalAsync(album, preferredQuality, cancellationToken),
                new Dictionary<string, object> 
                { 
                    ["album_id"] = album.Id,
                    ["album_title"] = album.Title,
                    ["tracks_count"] = album.TracksCount,
                    ["preferred_quality"] = preferredQuality
                }
            );
        }

        private async Task<AlbumQualityResult> DetectAlbumQualityInternalAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"album_quality_{album.Id}_{preferredQuality}";
            
            // Check cache first
            var cached = await _performanceMonitor.TrackOperationAsync(
                "Cache.AlbumQuality.Read",
                () => GetCachedQualityAsync(cacheKey),
                new Dictionary<string, object> { ["cache_key"] = cacheKey }
            );
            if (cached != null)
            {
                _logger.Info("Using cached quality data for album '{0}'", album.Title);
                return cached;
            }

            _logger.Info("Detecting album-level quality for '{0}' ({1} tracks)", album.Title, album.TracksCount);
            
            var tracks = album.GetTracks().ToList();
            var sampleTracks = SelectRepresentativeTracks(tracks, SAMPLE_TRACK_COUNT);
            
            // Check quality for sample tracks with performance monitoring
            var sampleResults = new List<QualityDetectionResult>();
            foreach (var track in sampleTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trackResult = await _performanceMonitor.TrackOperationAsync(
                    "QualityDetection.SampleTrack",
                    () => DetectAvailableQualitiesInternalAsync(track.Id.ToString(), cancellationToken),
                    new Dictionary<string, object> 
                    { 
                        ["track_id"] = track.Id,
                        ["track_title"] = track.Title,
                        ["sample_index"] = sampleResults.Count + 1
                    }
                );
                sampleResults.Add(trackResult);
            }

            // Analyze consistency
            var result = AnalyzeAlbumQuality(album, sampleResults, preferredQuality);
            
            // Cache the result
            await CacheQualityResultAsync(cacheKey, result);
            
            return result;
        }

        #endregion

        #region Quality Mapping

        /// <summary>
        /// Maps a Lidarr quality profile to Qobuz quality.
        /// </summary>
        public QobuzQuality MapLidarrQuality(LidarrQualityProfile profile)
        {
            return _performanceMonitor.TrackOperation(
                "QualityMapping.LidarrProfile",
                () => MapLidarrQualityInternal(profile),
new Dictionary<string, object> 
                { 
                    ["profile_name"] = profile?.Name ?? "null"
                }
            );
        }

        private QobuzQuality MapLidarrQualityInternal(LidarrQualityProfile profile)
        {
            if (profile == null)
            {
                return GetDefaultQuality();
            }

            _logger.Debug("Mapping quality profile '{0}' to Qobuz quality", profile.Name);

            // Try mapping based on profile name
            foreach (var mapping in LidarrQualityMappings)
            {
                if (profile.Name.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var format = QobuzQualityFormats[mapping.Value];
                    return new QobuzQuality
                    {
                        Id = format.Id,
                        Name = format.Name,
                        DisplayName = format.DisplayName
                    };
                }
            }

            // Analyze quality items in profile
            var preferredQuality = profile.GetPreferredQuality();
            if (preferredQuality != null)
            {
                return MapLidarrQuality(preferredQuality);
            }

            // Default fallback
            return GetDefaultQuality();
        }

        /// <summary>
        /// Gets the quality fallback chain for a given preferred quality.
        /// </summary>
        public List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred)
        {
            var chain = new List<QobuzQuality>();
            
            // Start with preferred quality
            if (preferred != null && QobuzQualityFormats.ContainsKey(preferred.Id))
            {
                chain.Add(preferred);
            }
            
            // Add lower qualities as fallbacks
            var preferredPriority = QobuzQualityFormats.TryGetValue(preferred?.Id ?? 0, out var format) 
                ? format.Priority 
                : int.MaxValue;
                
            foreach (var quality in QobuzQualityFormats.Values
                .Where(q => q.Priority < preferredPriority)
                .OrderByDescending(q => q.Priority))
            {
                chain.Add(new QobuzQuality
                {
                    Id = quality.Id,
                    Name = quality.Name,
                    DisplayName = quality.DisplayName
                });
            }
            
            // Ensure at least MP3 is in the chain
            if (!chain.Any(q => q.Id == 5))
            {
                var mp3 = QobuzQualityFormats[5];
                chain.Add(new QobuzQuality
                {
                    Id = mp3.Id,
                    Name = mp3.Name,
                    DisplayName = mp3.DisplayName
                });
            }
            
            return chain;
        }

        #endregion

        #region Quality Selection

        /// <summary>
        /// Selects the best available quality for a track with automatic fallback.
        /// </summary>
        public async Task<QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken = default)
        {
            return await _performanceMonitor.TrackOperationAsync(
                "QualitySelection.BestQuality",
                async () => await SelectBestQualityInternalAsync(trackId, preferred, cancellationToken),
                new Dictionary<string, object> 
                { 
                    ["track_id"] = trackId,
                    ["preferred_quality"] = preferred?.Name ?? "none",
                    ["preferred_quality_id"] = preferred?.Id.ToString() ?? "none"
                }
            );
        }

        private async Task<QualitySelectionResult> SelectBestQualityInternalAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken)
        {
            var fallbackChain = GetQualityFallbackChain(preferred);
            
            _logger.Debug("Attempting quality selection for track {0}, preferred: {1}", trackId, preferred?.Name);
            
            foreach (var quality in fallbackChain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var streamInfo = await GetStreamInfoInternalAsync(trackId, quality.Id, cancellationToken);
                    
                    if (IsValidStreamUrl(streamInfo?.Url))
                    {
                        return new QualitySelectionResult
                        {
                            Success = true,
                            SelectedQuality = quality,
                            StreamInfo = streamInfo,
                            FallbackUsed = quality.Id != preferred?.Id,
                            AttemptsCount = fallbackChain.IndexOf(quality) + 1
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} failed for track {1}: {2}", quality.Name, trackId, ex.Message);
                }
            }
            
            return new QualitySelectionResult
            {
                Success = false,
                Error = $"No available quality found for track {trackId}",
                AttemptsCount = fallbackChain.Count
            };
        }

        /// <summary>
        /// Executes an operation with automatic quality fallback.
        /// </summary>
        public async Task<T> ExecuteWithQualityFallbackAsync<T>(
            Func<QobuzQuality, Task<T>> operation,
            QobuzQuality preferred = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var fallbackChain = GetQualityFallbackChain(preferred ?? GetDefaultQuality());
            var exceptions = new List<Exception>();
            
            foreach (var quality in fallbackChain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    _logger.Debug("Attempting operation with quality: {0}", quality.Name);
                    var result = await operation(quality);
                    
                    if (preferred == null || quality.Id != preferred.Id)
                    {
                        _logger.Info("Operation succeeded with fallback quality: {0}", quality.Name);
                    }
                    
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn("Operation failed with quality {0}: {1}", quality.Name, ex.Message);
                    exceptions.Add(ex);
                }
            }
            
            throw new AggregateException($"All quality levels failed for operation", exceptions);
        }

        #endregion

        #region Stream URL Management

        /// <summary>
        /// Gets stream information for a track with the specified quality.
        /// </summary>
        public async Task<StreamInfo> GetStreamInfoAsync(string trackId, QobuzQuality quality, CancellationToken cancellationToken = default)
        {
            return await GetStreamInfoInternalAsync(trackId, quality.Id, cancellationToken);
        }

        /// <summary>
        /// Gets stream information for multiple tracks in batch.
        /// </summary>
        public async Task<BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default)
        {
            if (trackIds == null || !trackIds.Any())
            {
                throw new ArgumentException("Track IDs cannot be null or empty", nameof(trackIds));
            }

            _logger.Info("Getting batch stream info for {0} tracks with quality {1}", trackIds.Count, quality.Name);
            
            var result = new BatchStreamResult
            {
                RequestedQuality = quality,
                TrackResults = new Dictionary<string, StreamInfo>()
            };

            // Process in parallel with concurrency limit
            var semaphore = new SemaphoreSlim(5); // Max 5 concurrent requests
            var tasks = trackIds.Select(async trackId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var streamInfo = await GetStreamInfoInternalAsync(trackId, quality.Id, cancellationToken);
                    return (trackId, streamInfo);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            
            foreach (var (trackId, streamInfo) in results)
            {
                if (streamInfo != null)
                {
                    result.TrackResults[trackId] = streamInfo;
                }
            }
            
            result.SuccessCount = result.TrackResults.Count;
            result.FailureCount = trackIds.Count - result.SuccessCount;
            
            _logger.Info("Batch stream info complete: {0} successful, {1} failed", 
                result.SuccessCount, result.FailureCount);
            
            return result;
        }

        #endregion

        #region Private Helper Methods

        private async Task<StreamInfo> GetStreamInfoInternalAsync(string trackId, int qualityId, CancellationToken cancellationToken)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["track_id"] = trackId,
                    ["format_id"] = qualityId.ToString()
                };

                var response = await _apiClient.GetAsync<Dictionary<string, object>>(
                    "track/getFileUrl", 
                    parameters);

                if (response != null && response.TryGetValue("url", out var urlObj))
                {
                    return new StreamInfo
                    {
                        Url = urlObj?.ToString(),
                        QualityId = qualityId,
                        TrackId = trackId,
                        ExpiresAt = DateTime.UtcNow.AddHours(1) // URLs typically expire after 1 hour
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to get stream info for track {0} at quality {1}: {2}", 
                    trackId, qualityId, ex.Message);
            }

            return null;
        }

        private bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var urlLower = url.ToLower();

            // Check for preview/sample indicators
            var invalidPatterns = new[]
            {
                "_preview", "preview_", "/preview/", "preview=true",
                "_sample", "sample_", "/sample/", "sample=true",
                "_demo", "demo_", "_30sec", "_30s", "duration=30",
                "_clip", "clip_", "_short"
            };

            return !invalidPatterns.Any(pattern => urlLower.Contains(pattern));
        }

        private List<QobuzTrack> SelectRepresentativeTracks(List<QobuzTrack> tracks, int count)
        {
            if (tracks.Count <= count)
                return tracks;

            var selected = new List<QobuzTrack>();
            
            // Always include first track
            selected.Add(tracks.First());
            
            // Include middle track
            if (count >= 2)
            {
                selected.Add(tracks[tracks.Count / 2]);
            }
            
            // Include last track
            if (count >= 3)
            {
                selected.Add(tracks.Last());
            }
            
            return selected;
        }

        private AlbumQualityResult AnalyzeAlbumQuality(
            QobuzAlbum album, 
            List<QualityDetectionResult> sampleResults,
            int preferredQuality)
        {
            var result = new AlbumQualityResult
            {
                AlbumId = album.Id,
                AlbumTitle = album.Title,
                PreferredQuality = preferredQuality,
                SampleSize = sampleResults.Count,
                TotalTracks = album.TracksCount
            };

            // Find most common highest quality
            var highestQualities = sampleResults
                .Where(r => r.HighestAvailableQuality != null)
                .Select(r => r.HighestAvailableQuality.Id)
                .ToList();

            if (!highestQualities.Any())
            {
                result.Success = false;
                result.Error = "No qualities available for sampled tracks";
                return result;
            }

            var mostCommonQuality = highestQualities
                .GroupBy(q => q)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            var consistency = (double)highestQualities.Count(q => q == mostCommonQuality) / highestQualities.Count;
            
            result.ConsistentQuality = consistency >= CONSISTENCY_THRESHOLD;
            result.DetectedQuality = mostCommonQuality;
            result.ConfidenceScore = consistency;
            result.Success = true;

            if (result.ConsistentQuality)
            {
                result.OptimizationApplied = true;
                result.ApiCallsSaved = album.TracksCount - sampleResults.Count;
                _logger.Info("Album '{0}' has consistent quality {1} (confidence: {2:P0}), saved {3} API calls",
                    album.Title, mostCommonQuality, consistency, result.ApiCallsSaved);
            }
            else
            {
                _logger.Info("Album '{0}' has mixed quality, fallback to individual track checks required",
                    album.Title);
            }

            return result;
        }

        private QobuzQuality MapLidarrQuality(LidarrQuality quality)
        {
            // Map based on quality properties
            var qualityId = 6; // Default to CD quality
            
            if (quality.Name.Contains("Hi-Res", StringComparison.OrdinalIgnoreCase) ||
                quality.Name.Contains("24", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 27;
            }
            else if (quality.Name.Contains("MP3", StringComparison.OrdinalIgnoreCase) ||
                     quality.Name.Contains("320", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 5;
            }

            var format = QobuzQualityFormats[qualityId];
            return new QobuzQuality
            {
                Id = format.Id,
                Name = format.Name,
                DisplayName = format.DisplayName
            };
        }

        private QobuzQuality GetDefaultQuality()
        {
            var format = QobuzQualityFormats[6]; // CD quality as default
            return new QobuzQuality
            {
                Id = format.Id,
                Name = format.Name,
                DisplayName = format.DisplayName
            };
        }

        private async Task<AlbumQualityResult> GetCachedQualityAsync(string cacheKey)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_qualityCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
                    {
                        return cached.Result;
                    }
                    
                    // Remove expired entry
                    _qualityCache.Remove(cacheKey);
                }
            }
            finally
            {
                _cacheLock.Release();
            }
            
            return null;
        }

        private async Task CacheQualityResultAsync(string cacheKey, AlbumQualityResult result)
        {
            await _cacheLock.WaitAsync();
            try
            {
                // Limit cache size
                if (_qualityCache.Count >= MAX_CACHE_ENTRIES)
                {
                    // Remove oldest entries
                    var oldestKeys = _qualityCache
                        .OrderBy(kvp => kvp.Value.CachedAt)
                        .Take(100)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in oldestKeys)
                    {
                        _qualityCache.Remove(key);
                    }
                }
                
                _qualityCache[cacheKey] = new AlbumQualityCache
                {
                    Result = result,
                    CachedAt = DateTime.UtcNow
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        #endregion

        #region Support Classes

        private class AlbumQualityCache
        {
            public AlbumQualityResult Result { get; set; }
            public DateTime CachedAt { get; set; }
        }

        #endregion
    }

    #region Public Result Classes

    public class QualityFormat
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int BitRate { get; set; }
        public bool IsLossless { get; set; }
        public int Priority { get; set; }
    }

    public class QobuzQuality
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
    }

    public class QualityDetectionResult
    {
        public string TrackId { get; set; }
        public List<QualityFormat> AvailableQualities { get; set; }
        public QualityFormat HighestAvailableQuality { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    public class AlbumQualityResult
    {
        public string AlbumId { get; set; }
        public string AlbumTitle { get; set; }
        public int PreferredQuality { get; set; }
        public int DetectedQuality { get; set; }
        public bool ConsistentQuality { get; set; }
        public double ConfidenceScore { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public int SampleSize { get; set; }
        public int TotalTracks { get; set; }
        public bool OptimizationApplied { get; set; }
        public int ApiCallsSaved { get; set; }
        public DateTime CachedAt { get; set; }

        public static AlbumQualityResult Failed(string error)
        {
            return new AlbumQualityResult
            {
                Success = false,
                Error = error
            };
        }
    }

    public class QualitySelectionResult
    {
        public bool Success { get; set; }
        public QobuzQuality SelectedQuality { get; set; }
        public StreamInfo StreamInfo { get; set; }
        public bool FallbackUsed { get; set; }
        public int AttemptsCount { get; set; }
        public string Error { get; set; }
    }

    public class StreamInfo
    {
        public string Url { get; set; }
        public int QualityId { get; set; }
        public string TrackId { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class BatchStreamResult
    {
        public QobuzQuality RequestedQuality { get; set; }
        public Dictionary<string, StreamInfo> TrackResults { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
    }

    #endregion
}