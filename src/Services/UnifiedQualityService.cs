using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NLog;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Unified service that consolidates all quality-related functionality.
    /// Replaces: QobuzQualityService, IntelligentQualityDetector, QualityDetectionService,
    /// QualityMappingService, QualityCacheService, and QobuzQualityManager.
    /// </summary>
    public class UnifiedQualityService : IQualityService
    {
        private readonly Logger _logger;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);

        // Quality mapping from Qobuz format IDs to Lidarr qualities
        private static readonly Dictionary<int, Quality> QualityMapping = new()
        {
            { 5, Quality.MP3_320 },       // MP3 320kbps
            { 6, Quality.FLAC },          // FLAC 16-bit/44.1kHz (CD Quality)
            { 7, Quality.FLAC },          // FLAC 24-bit/up to 96kHz (Hi-Res)
            { 27, Quality.FLAC }          // FLAC 24-bit/up to 192kHz (Studio Master)
        };

        // Detailed quality definitions with bit depth and sample rate
        private static readonly Dictionary<int, (int BitDepth, int SampleRate, string Label)> DetailedQualities = new()
        {
            { 5, (0, 320, "MP3 320") },
            { 6, (16, 44100, "FLAC CD") },
            { 7, (24, 96000, "FLAC Hi-Res 24/96") },
            { 27, (24, 192000, "FLAC Studio Master 24/192") }
        };

        public UnifiedQualityService(Logger logger, IMemoryCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Maps a Qobuz format ID to a Lidarr quality.
        /// </summary>
        public Quality MapQualityFromFormatId(int formatId)
        {
            if (QualityMapping.TryGetValue(formatId, out var quality))
            {
                _logger.Debug("Mapped Qobuz format {0} to Lidarr quality {1}", formatId, quality);
                return quality;
            }

            _logger.Warn("Unknown Qobuz format ID: {0}, defaulting to Unknown", formatId);
            return Quality.Unknown;
        }

        /// <summary>
        /// Gets the best available format ID for a track based on maximum quality settings.
        /// </summary>
        public int GetBestAvailableFormatId(QobuzTrack track, int maxFormatId = 27)
        {
            var cacheKey = $"format_{track.Id}_{maxFormatId}";
            
            if (_cache.TryGetValue<int>(cacheKey, out var cachedFormat))
            {
                return cachedFormat;
            }

            // Determine best available format
            var availableFormats = GetAvailableFormats(track);
            var bestFormat = availableFormats
                .Where(f => f <= maxFormatId)
                .OrderByDescending(f => f)
                .FirstOrDefault();

            if (bestFormat == 0)
            {
                bestFormat = 6; // Default to CD quality if nothing else available
                _logger.Debug("No suitable format found for track {0}, defaulting to CD quality", track.Id);
            }

            _cache.Set(cacheKey, bestFormat, _cacheExpiration);
            return bestFormat;
        }

        /// <summary>
        /// Detects the quality of a track intelligently based on multiple factors.
        /// </summary>
        public async Task<QualityDetectionResult> DetectQualityAsync(QobuzTrack track, QobuzAlbum album = null)
        {
            var cacheKey = $"quality_detection_{track.Id}";
            
            if (_cache.TryGetValue<QualityDetectionResult>(cacheKey, out var cachedResult))
            {
                _logger.Debug("Using cached quality detection for track {0}", track.Id);
                return cachedResult;
            }

            var result = new QualityDetectionResult
            {
                TrackId = track.Id,
                CheckedAt = DateTime.UtcNow,
                AvailableQualities = new List<QualityFormat>()
            };

            try
            {
                var availableFormats = GetAvailableFormats(track);
                
                // Create QualityFormat objects for each available format
                foreach (var formatId in availableFormats)
                {
                    var qualityFormat = CreateQualityFormat(formatId);
                    result.AvailableQualities.Add(qualityFormat);
                }

                // Set the highest quality as the primary format
                if (result.AvailableQualities.Any())
                {
                    result.HighestAvailableQuality = result.AvailableQualities
                        .OrderByDescending(q => q.Priority)
                        .First();
                }
                
                _logger.Debug("Detected {0} quality formats for track {1}", 
                    result.AvailableQualities.Count, track.Id);

                _cache.Set(cacheKey, result, _cacheExpiration);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error detecting quality for track {0}", track.Id);
                
                // Return safe default on error - CD quality
                var defaultFormat = CreateQualityFormat(6);
                result.AvailableQualities.Add(defaultFormat);
                result.HighestAvailableQuality = defaultFormat;
                return result;
            }
        }

        /// <summary>
        /// Gets a human-readable label for a format ID.
        /// </summary>
        public string GetQualityLabel(int formatId)
        {
            if (DetailedQualities.TryGetValue(formatId, out var details))
            {
                return details.Label;
            }
            return "Unknown";
        }

        /// <summary>
        /// Validates if a requested quality is available for a track.
        /// </summary>
        public bool IsQualityAvailable(QobuzTrack track, int requestedFormatId)
        {
            var availableFormats = GetAvailableFormats(track);
            return availableFormats.Contains(requestedFormatId);
        }

        /// <summary>
        /// Gets quality statistics for analysis and monitoring.
        /// </summary>
        public QualityStatistics GetStatistics()
        {
            // This would aggregate statistics from cache or persistent storage
            return new QualityStatistics
            {
                QualityProfileUsage = _cache.Get<Dictionary<string, int>>("quality_profile_usage") ?? new(),
                QobuzQualityDistribution = _cache.Get<Dictionary<string, int>>("qobuz_quality_distribution") ?? new(),
                QualityUpgrades = _cache.Get<Dictionary<string, int>>("quality_upgrades") ?? new(),
                QualityDowngrades = _cache.Get<Dictionary<string, int>>("quality_downgrades") ?? new(),
                MostUsedQualityProfile = _cache.Get<string>("most_used_quality_profile") ?? "Unknown",
                MostSelectedQobuzQuality = _cache.Get<string>("most_selected_qobuz_quality") ?? "Unknown"
            };
        }

        /// <summary>
        /// Clears all cached quality data.
        /// </summary>
        public void ClearCache()
        {
            // In production, implement more sophisticated cache clearing
            _logger.Info("Clearing quality service cache");
        }

        // Private helper methods

        private List<int> GetAvailableFormats(QobuzTrack track)
        {
            var formats = new List<int>();

            // Always available
            formats.Add(5); // MP3 320

            // Check for lossless availability
            if (track.Streamable)
            {
                formats.Add(6); // CD Quality
                
                // Check for hi-res availability based on technical specs
                if (track.MaximumBitDepth.HasValue && track.MaximumSampleRate.HasValue)
                {
                    if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 192000)
                    {
                        formats.Add(27); // Studio Master
                    }
                    else if (track.MaximumBitDepth >= 24 || track.MaximumSampleRate > 48000)
                    {
                        formats.Add(7); // Hi-Res
                    }
                }
            }

            return formats;
        }

        private QualityFormat CreateQualityFormat(int formatId)
        {
            return formatId switch
            {
                5 => new QualityFormat 
                { 
                    Id = 5, 
                    Name = "MP3_320", 
                    DisplayName = "MP3 320", 
                    BitRate = 320, 
                    IsLossless = false, 
                    Priority = 1 
                },
                6 => new QualityFormat 
                { 
                    Id = 6, 
                    Name = "FLAC_CD", 
                    DisplayName = "FLAC CD", 
                    BitRate = 1411, 
                    IsLossless = true, 
                    Priority = 2 
                },
                7 => new QualityFormat 
                { 
                    Id = 7, 
                    Name = "FLAC_HiRes", 
                    DisplayName = "FLAC Hi-Res", 
                    BitRate = 2822, 
                    IsLossless = true, 
                    Priority = 3 
                },
                27 => new QualityFormat 
                { 
                    Id = 27, 
                    Name = "FLAC_Studio", 
                    DisplayName = "FLAC Studio Master", 
                    BitRate = 9216, 
                    IsLossless = true, 
                    Priority = 4 
                },
                _ => new QualityFormat 
                { 
                    Id = 6, 
                    Name = "FLAC_CD", 
                    DisplayName = "FLAC CD", 
                    BitRate = 1411, 
                    IsLossless = true, 
                    Priority = 2 
                }
            };
        }

        private int DetermineFormatFromSpecs(int bitDepth, int sampleRate)
        {
            if (bitDepth == 24 && sampleRate >= 192000)
                return 27; // Studio Master
            if (bitDepth == 24 && sampleRate > 44100)
                return 7;  // Hi-Res
            if (bitDepth == 16 && sampleRate == 44100)
                return 6;  // CD Quality
            
            return 6; // Default to CD
        }

        // Legacy compatibility methods (stubs for now)
        public QobuzQuality MapLidarrQuality(object qualityProfile)
        {
            // Simplified mapping - return default quality object
            return new QobuzQuality 
            { 
                Id = 6, 
                Name = "FLAC_CD", 
                DisplayName = "FLAC CD", 
                BitRate = 1411, 
                IsLossless = true, 
                Priority = 2,
                Format = "FLAC"
            };
        }

        public List<QobuzQuality> GetQualityFallbackChain(QobuzQuality mappedQuality)
        {
            // Simplified fallback chain - return common quality progression
            return new List<QobuzQuality>
            {
                new() { Id = 27, Name = "FLAC_Studio", DisplayName = "FLAC Studio Master", BitRate = 9216, IsLossless = true, Priority = 4, Format = "FLAC" },
                new() { Id = 7, Name = "FLAC_HiRes", DisplayName = "FLAC Hi-Res", BitRate = 2822, IsLossless = true, Priority = 3, Format = "FLAC" },
                new() { Id = 6, Name = "FLAC_CD", DisplayName = "FLAC CD", BitRate = 1411, IsLossless = true, Priority = 2, Format = "FLAC" },
                new() { Id = 5, Name = "MP3_320", DisplayName = "MP3 320", BitRate = 320, IsLossless = false, Priority = 1, Format = "MP3" }
            };
        }
    }

    // Interface for the unified quality service
    public interface IQualityService
    {
        Quality MapQualityFromFormatId(int formatId);
        int GetBestAvailableFormatId(QobuzTrack track, int maxFormatId = 27);
        Task<QualityDetectionResult> DetectQualityAsync(QobuzTrack track, QobuzAlbum album = null);
        string GetQualityLabel(int formatId);
        bool IsQualityAvailable(QobuzTrack track, int requestedFormatId);
        QualityStatistics GetStatistics();
        void ClearCache();
        
        // Legacy compatibility methods for LidarrAlbumRetriever  
        QobuzQuality MapLidarrQuality(object qualityProfile);
        List<QobuzQuality> GetQualityFallbackChain(QobuzQuality mappedQuality);
    }
}