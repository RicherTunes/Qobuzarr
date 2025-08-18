using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Migration
{
    /// <summary>
    /// Migration adapter that allows existing code to continue using old service interfaces
    /// while internally delegating to the new consolidated QobuzQualityManager.
    /// This enables incremental migration without breaking existing functionality.
    /// </summary>
    [Obsolete("This is a migration adapter. Update code to use IQobuzQualityManager directly.")]
    public class QualityServiceMigrationAdapter
    {
        private readonly IQobuzQualityManager _qualityManager;
        private readonly IQobuzLogger _logger;

        public QualityServiceMigrationAdapter(
            IQobuzQualityManager qualityManager,
            IQobuzLogger logger)
        {
            _qualityManager = qualityManager ?? throw new ArgumentNullException(nameof(qualityManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region QobuzQualityService Compatibility

        /// <summary>
        /// Legacy method from QobuzQualityService.
        /// </summary>
        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            _logger.Debug("[MIGRATION] QobuzQualityService.GetAvailableQualitiesAsync called - redirecting to QobuzQualityManager");
            
            var result = await _qualityManager.DetectAvailableQualitiesAsync(trackId);
            return result.AvailableQualities.Select(q => q.Id).ToList();
        }

        /// <summary>
        /// Legacy method from QobuzQualityService.
        /// </summary>
        public async Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(
            string trackId, 
            int preferredQuality)
        {
            _logger.Debug("[MIGRATION] QobuzQualityService.GetBestAvailableStreamAsync called - redirecting to QobuzQualityManager");
            
            var quality = new QobuzQuality { Id = preferredQuality };
            var result = await _qualityManager.SelectBestQualityAsync(trackId, quality);
            
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? $"No available quality found for track {trackId}");
            }

            // Convert to legacy format
            var legacyStreamInfo = new QobuzStreamInfo
            {
                Url = result.StreamInfo.Url,
                Format = GetQualityDescription(result.SelectedQuality.Id),
                BitDepth = GetBitDepth(result.SelectedQuality.Id),
                SamplingRate = GetSamplingRate(result.SelectedQuality.Id)
            };

            return (result.SelectedQuality.Id, legacyStreamInfo);
        }

        /// <summary>
        /// Legacy method from QobuzQualityService.
        /// </summary>
        public string GetQualityDescription(int qualityId)
        {
            return qualityId switch
            {
                5 => "MP3 320kbps",
                6 => "FLAC CD 16bit/44.1kHz",
                7 => "FLAC Hi-Res 24bit up to 96kHz",
                27 => "FLAC Hi-Res 24bit up to 192kHz",
                _ => "Unknown Quality"
            };
        }

        #endregion

        #region QualityMappingService Compatibility

        /// <summary>
        /// Legacy method from QualityMappingService (IQualityMappingService).
        /// </summary>
        public string GetPreferredQobuzQuality(LidarrQualityProfile qualityProfile)
        {
            _logger.Debug("[MIGRATION] QualityMappingService.GetPreferredQobuzQuality called - redirecting to QobuzQualityManager");
            
            var quality = _qualityManager.MapLidarrQuality(qualityProfile);
            return ConvertToLegacyQualityString(quality);
        }

        /// <summary>
        /// Legacy method from QualityMappingService.
        /// </summary>
        public List<string> GetQualityFallbackChain(LidarrQualityProfile qualityProfile)
        {
            _logger.Debug("[MIGRATION] QualityMappingService.GetQualityFallbackChain called - redirecting to QobuzQualityManager");
            
            var quality = _qualityManager.MapLidarrQuality(qualityProfile);
            var chain = _qualityManager.GetQualityFallbackChain(quality);
            return chain.Select(ConvertToLegacyQualityString).ToList();
        }

        /// <summary>
        /// Legacy method from QualityMappingService.
        /// </summary>
        public string SelectBestAvailableQuality(LidarrQualityProfile qualityProfile, List<string> availableQualities)
        {
            _logger.Debug("[MIGRATION] QualityMappingService.SelectBestAvailableQuality called - using compatibility logic");
            
            var fallbackChain = GetQualityFallbackChain(qualityProfile);
            
            foreach (var preferred in fallbackChain)
            {
                if (availableQualities.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                {
                    return preferred;
                }
            }
            
            return availableQualities.FirstOrDefault() ?? "mp3-320";
        }

        /// <summary>
        /// Legacy method from QualityMappingService.
        /// </summary>
        public QualityRecommendation GetQualityRecommendation(LidarrAlbum album, LidarrQualityProfile qualityProfile)
        {
            _logger.Debug("[MIGRATION] QualityMappingService.GetQualityRecommendation called - using compatibility logic");
            
            var quality = _qualityManager.MapLidarrQuality(qualityProfile);
            var fallbackChain = _qualityManager.GetQualityFallbackChain(quality);
            
            return new QualityRecommendation
            {
                PrimaryQuality = ConvertToLegacyQualityString(quality),
                FallbackQualities = fallbackChain.Skip(1).Select(ConvertToLegacyQualityString).ToList(),
                PreferLossless = quality.Id >= 6,
                MinimumQuality = "mp3-320",
                Reason = $"Based on profile: {qualityProfile?.Name ?? "Default"}"
            };
        }

        #endregion

        #region QualityFallbackService Compatibility

        /// <summary>
        /// Legacy method from QualityFallbackService (IQualityFallbackService).
        /// </summary>
        public async Task<T> ExecuteWithFallbackAsync<T>(
            Func<QobuzAudioQuality, System.Threading.CancellationToken, Task<T>> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            _logger.Debug("[MIGRATION] QualityFallbackService.ExecuteWithFallbackAsync called - redirecting to QobuzQualityManager");
            
            // Convert legacy quality chain to new format
            QobuzQuality? preferredQuality = null;
            if (qualityChain?.Any() == true)
            {
                var first = qualityChain.First();
                preferredQuality = ConvertFromLegacyQuality(first);
            }
            
            // Wrap the legacy operation to work with new quality format
            async Task<T> wrappedOperation(QobuzQuality quality)
            {
                var legacyQuality = ConvertToLegacyQuality(quality);
                return await operation(legacyQuality, cancellationToken);
            }
            
            return await _qualityManager.ExecuteWithQualityFallbackAsync(
                wrappedOperation, 
                preferredQuality, 
                cancellationToken);
        }

        #endregion

        #region IntelligentQualityDetector Compatibility

        /// <summary>
        /// Legacy method from IntelligentQualityDetector.
        /// </summary>
        public async Task<AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album,
            int preferredQuality,
            System.Threading.CancellationToken cancellationToken = default)
        {
            _logger.Debug("[MIGRATION] IntelligentQualityDetector.DetectAlbumQualityAsync called - redirecting to QobuzQualityManager");
            
            // Get result from new implementation
            var newResult = await _qualityManager.DetectAlbumQualityAsync(album, preferredQuality, cancellationToken);
            
            // Convert to legacy type (Services namespace)
            return new AlbumQualityResult
            {
                IsSuccess = newResult.Success,
                Album = album,
                BestAvailableFormat = new QualityFormat
                {
                    Id = newResult.DetectedQuality,
                    Name = GetQualityName(newResult.DetectedQuality)
                },
                AllAvailableFormats = new List<QualityFormat>
                {
                    new QualityFormat
                    {
                        Id = newResult.DetectedQuality,
                        Name = GetQualityName(newResult.DetectedQuality)
                    }
                },
                IsConsistentAcrossAlbum = newResult.ConsistentQuality,
                ConfidenceLevel = newResult.ConfidenceScore,
                SampleTracksChecked = newResult.SampleSize,
                OptimizationStrategy = newResult.OptimizationApplied ? "SmartSampling" : "IndividualCheck",
                QualityByTrack = new Dictionary<int, List<int>>(),
                RecommendedApproach = newResult.ConsistentQuality ? "Use album-wide quality" : "Check individual tracks",
                ErrorMessage = newResult.Error
            };
        }

        #endregion

        #region Private Helper Methods

        private string GetQualityName(int qualityId)
        {
            return qualityId switch
            {
                5 => "MP3 320",
                6 => "FLAC CD",
                7 => "FLAC Hi-Res 24-bit/96kHz",
                27 => "FLAC Hi-Res 24-bit/192kHz",
                _ => "FLAC CD"
            };
        }

        private string ConvertToLegacyQualityString(QobuzQuality quality)
        {
            if (quality == null) return "flac-cd";
            
            return quality.Id switch
            {
                27 => "flac-hires",
                7 => "flac-hires",
                6 => "flac-cd",
                5 => "mp3-320",
                _ => "flac-cd"
            };
        }

        private QobuzQuality ConvertFromLegacyQuality(QobuzAudioQuality legacyQuality)
        {
            var id = legacyQuality switch
            {
                QobuzAudioQuality.FLACHiRes24Bit192Khz => 27,
                QobuzAudioQuality.FLACHiRes24Bit96kHz => 7,
                QobuzAudioQuality.FLACLossless => 6,
                QobuzAudioQuality.MP3320 => 5,
                _ => 6
            };
            
            var format = QobuzQualityManager.QobuzQualityFormats[id];
            return new QobuzQuality
            {
                Id = format.Id,
                Name = format.Name,
                DisplayName = format.DisplayName
            };
        }

        private QobuzAudioQuality ConvertToLegacyQuality(QobuzQuality quality)
        {
            return quality.Id switch
            {
                27 => QobuzAudioQuality.FLACHiRes24Bit192Khz,
                7 => QobuzAudioQuality.FLACHiRes24Bit96kHz,
                6 => QobuzAudioQuality.FLACLossless,
                5 => QobuzAudioQuality.MP3320,
                _ => QobuzAudioQuality.FLACLossless
            };
        }

        private int GetBitDepth(int qualityId)
        {
            return qualityId switch
            {
                27 => 24,
                7 => 24,
                6 => 16,
                5 => 0, // MP3 doesn't have bit depth
                _ => 16
            };
        }

        private double GetSamplingRate(int qualityId)
        {
            return qualityId switch
            {
                27 => 192.0,
                7 => 96.0,
                6 => 44.1,
                5 => 0, // MP3 doesn't specify sampling rate this way
                _ => 44.1
            };
        }

        #endregion
    }

    #region Legacy Support Classes

    /// <summary>
    /// Legacy quality recommendation class from QualityMappingService.
    /// </summary>
    [Obsolete("Use consolidated quality management instead")]
    public class QualityRecommendation
    {
        public string PrimaryQuality { get; set; }
        public List<string> FallbackQualities { get; set; }
        public bool PreferLossless { get; set; }
        public string MinimumQuality { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Legacy audio quality enum.
    /// </summary>
    [Obsolete("Use QobuzQuality class instead")]
    public enum QobuzAudioQuality
    {
        MP3320 = 5,
        FLACLossless = 6,
        FLACHiRes24Bit96kHz = 7,
        FLACHiRes24Bit192Khz = 27
    }

    /// <summary>
    /// Legacy stream info class.
    /// </summary>
    [Obsolete("Use StreamInfo class instead")]
    public class QobuzStreamInfo
    {
        public string Url { get; set; }
        public string Format { get; set; }
        public int BitDepth { get; set; }
        public double SamplingRate { get; set; }
    }

    /// <summary>
    /// Legacy quality fallback service interface.
    /// </summary>
    [Obsolete("Use IQobuzQualityManager instead")]
    public interface IQualityFallbackService
    {
        Task<T> ExecuteWithFallbackAsync<T>(
            Func<QobuzAudioQuality, System.Threading.CancellationToken, Task<T>> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null,
            System.Threading.CancellationToken cancellationToken = default);
    }

    #endregion
}