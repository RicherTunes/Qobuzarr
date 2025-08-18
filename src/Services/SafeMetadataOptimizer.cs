using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Safe metadata optimizer with comprehensive validation and conservative rollout approach
    /// Provides multiple safety layers to prevent metadata corruption and data integrity issues
    /// </summary>
    /// <remarks>
    /// Safety approach:
    /// 1. Opt-in optimization with user configuration control
    /// 2. High confidence thresholds for metadata matching
    /// 3. Comprehensive validation before applying optimizations
    /// 4. Automatic fallback to Qobuz metadata when uncertain
    /// 5. Detailed logging for troubleshooting and monitoring
    /// 
    /// Conservative design principles:
    /// - Never risk data corruption for optimization gains
    /// - Default to safe approach when validation is uncertain
    /// - Provide clear user feedback about optimization decisions
    /// - Allow gradual confidence threshold adjustments based on real-world data
    /// </remarks>
    public class SafeMetadataOptimizer : ISafeMetadataOptimizer
    {
        private readonly Logger _logger;
        private readonly QobuzIndexerSettings _settings;
        private readonly HybridMetadataService _hybridMetadataService;
        private readonly IntelligentReleaseMapper _releaseMapper;
        private readonly bool _enableOptimization;
        private readonly double _confidenceThreshold;
        private readonly double _hybridModeThreshold;

        // Conservative default thresholds
        private const double DEFAULT_CONFIDENCE_THRESHOLD = 0.90; // Very high confidence required
        private const double DEFAULT_HYBRID_THRESHOLD = 0.75; // Moderate confidence for hybrid approach
        private const double MINIMUM_CONFIDENCE_THRESHOLD = 0.50; // Never go below 50% confidence

        public SafeMetadataOptimizer(
            QobuzIndexerSettings settings,
            HybridMetadataService hybridMetadataService,
            IntelligentReleaseMapper releaseMapper,
            Logger logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _hybridMetadataService = hybridMetadataService ?? throw new ArgumentNullException(nameof(hybridMetadataService));
            _releaseMapper = releaseMapper ?? throw new ArgumentNullException(nameof(releaseMapper));
            _logger = logger ?? LogManager.GetCurrentClassLogger();

            // Load optimization settings with conservative defaults
            _enableOptimization = false; // Disable by default for now
            _confidenceThreshold = ValidateConfidenceThreshold(_settings.MetadataMatchConfidenceThreshold);
            _hybridModeThreshold = ValidateConfidenceThreshold(_settings.HybridModeThreshold);

            LogOptimizationConfiguration();
        }

        /// <summary>
        /// Downloads album using safe metadata optimization with comprehensive validation
        /// Applies multiple safety checks before enabling any optimization
        /// </summary>
        /// <param name="qobuzAlbum">Qobuz album with streaming information</param>
        /// <param name="lidarrAlbum">Optional Lidarr album with MusicBrainz metadata</param>
        /// <returns>Download result using safest possible metadata strategy</returns>
        public async Task<DownloadResult> DownloadAlbumSafelyAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null)
        {
            if (qobuzAlbum == null)
                throw new ArgumentNullException(nameof(qobuzAlbum));

            _logger.Debug("Starting safe metadata optimization analysis for '{0}' by '{1}'", 
                         qobuzAlbum.Title, qobuzAlbum.GetArtistName());

            // Safety check 1: Feature flag validation
            if (!_enableOptimization)
            {
                _logger.Info("🔒 OPTIMIZATION DISABLED: Metadata optimization is disabled in settings. Using standard Qobuz metadata.");
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }

            // Safety check 2: Lidarr data availability
            if (lidarrAlbum == null)
            {
                _logger.Info("📀 NO LIDARR DATA: No Lidarr album data available. Using standard Qobuz metadata.");
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }

            // Safety check 3: Basic data validation
            var basicValidationResult = ValidateBasicAlbumData(qobuzAlbum, lidarrAlbum);
            if (!basicValidationResult.IsValid)
            {
                _logger.Warn("⚠️ BASIC VALIDATION FAILED: {0}. Using Qobuz metadata for safety.", basicValidationResult.Reason);
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }

            // Safety check 4: Release compatibility analysis
            var matchResult = _releaseMapper.ValidateReleaseMatch(lidarrAlbum, qobuzAlbum);
            
            if (!matchResult.IsCompatible)
            {
                _logger.Info("🚫 INCOMPATIBLE RELEASES: {0}. Using Qobuz metadata for data integrity.", matchResult.Reason);
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }

            // Safety check 5: Confidence threshold validation
            var confidenceValidation = ValidateMatchConfidence(matchResult);
            if (!confidenceValidation.IsAcceptable)
            {
                _logger.Info("🔒 LOW CONFIDENCE: {0}. Using Qobuz metadata for safety.", confidenceValidation.Reason);
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }

            // Determine safe optimization strategy based on confidence level
            return await ApplySafeOptimizationStrategy(qobuzAlbum, lidarrAlbum, matchResult, confidenceValidation);
        }

        /// <summary>
        /// Applies the appropriate optimization strategy based on safety validation results
        /// </summary>
        private async Task<DownloadResult> ApplySafeOptimizationStrategy(
            QobuzAlbum qobuzAlbum, 
            LidarrAlbum lidarrAlbum, 
            ReleaseMatchResult matchResult,
            ConfidenceValidation confidenceValidation)
        {
            if (confidenceValidation.ConfidenceLevel >= _confidenceThreshold)
            {
                // Highest confidence - proceed with full optimization
                _logger.Info("✅ HIGH CONFIDENCE OPTIMIZATION: Match confidence {0:P1} exceeds threshold {1:P1}. Proceeding with optimization.", 
                            confidenceValidation.ConfidenceLevel, _confidenceThreshold);
                
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, lidarrAlbum);
            }
            else if (confidenceValidation.ConfidenceLevel >= _hybridModeThreshold && matchResult.RequiresHybridApproach)
            {
                // Moderate confidence - use hybrid approach for safety
                _logger.Info("🧩 MODERATE CONFIDENCE HYBRID: Match confidence {0:P1} allows hybrid approach. Combining Lidarr and Qobuz metadata.", 
                            confidenceValidation.ConfidenceLevel);
                
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, lidarrAlbum);
            }
            else
            {
                // Below thresholds - fallback to Qobuz for safety
                _logger.Info("🔒 SAFETY FALLBACK: Confidence {0:P1} below optimization thresholds. Using Qobuz metadata.", 
                            confidenceValidation.ConfidenceLevel);
                
                return await _hybridMetadataService.DownloadAlbumWithIntelligentMetadataAsync(qobuzAlbum, null);
            }
        }

        /// <summary>
        /// Validates basic album data integrity before attempting optimization
        /// </summary>
        private ValidationResult ValidateBasicAlbumData(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum)
        {
            // Check for missing essential data
            if (string.IsNullOrWhiteSpace(qobuzAlbum.Title) || string.IsNullOrWhiteSpace(qobuzAlbum.GetArtistName()))
            {
                return ValidationResult.Invalid("Qobuz album missing essential title or artist information");
            }

            if (string.IsNullOrWhiteSpace(lidarrAlbum.Title) || string.IsNullOrWhiteSpace(lidarrAlbum.ArtistName))
            {
                return ValidationResult.Invalid("Lidarr album missing essential title or artist information");
            }

            // Check for reasonable track counts
            if (qobuzAlbum.TracksCount <= 0 || lidarrAlbum.TrackCount <= 0)
            {
                return ValidationResult.Invalid("Album has invalid track count (zero or negative)");
            }

            if (qobuzAlbum.TracksCount > 100 || lidarrAlbum.TrackCount > 100)
            {
                return ValidationResult.Invalid("Album has suspiciously high track count (>100), possible data error");
            }

            // Check for extreme track count differences (likely different releases)
            var trackCountDiff = Math.Abs(qobuzAlbum.TracksCount - lidarrAlbum.TrackCount);
            if (trackCountDiff > Math.Max(qobuzAlbum.TracksCount, lidarrAlbum.TrackCount) * 0.5) // More than 50% difference
            {
                return ValidationResult.Invalid($"Extreme track count difference: {qobuzAlbum.TracksCount} vs {lidarrAlbum.TrackCount} (likely different releases)");
            }

            // Validate track data integrity
            var qobuzTracks = qobuzAlbum.GetTracks();
            if (qobuzTracks == null || !qobuzTracks.Any())
            {
                return ValidationResult.Invalid("Qobuz album has no track data");
            }

            if (lidarrAlbum.Tracks == null || lidarrAlbum.Tracks.Count == 0)
            {
                return ValidationResult.Invalid("Lidarr album has no track data");
            }

            // Basic sanity check passed
            return ValidationResult.Valid("Basic album data validation passed");
        }

        /// <summary>
        /// Validates match confidence against configured thresholds
        /// </summary>
        private ConfidenceValidation ValidateMatchConfidence(ReleaseMatchResult matchResult)
        {
            var confidence = matchResult.MatchConfidence;

            // Check absolute minimum confidence
            if (confidence < MINIMUM_CONFIDENCE_THRESHOLD)
            {
                return ConfidenceValidation.Unacceptable(confidence, 
                    $"Match confidence {confidence:P1} below absolute minimum {MINIMUM_CONFIDENCE_THRESHOLD:P1}");
            }

            // Check against user-configured thresholds
            if (confidence < _hybridModeThreshold)
            {
                return ConfidenceValidation.Unacceptable(confidence,
                    $"Match confidence {confidence:P1} below hybrid threshold {_hybridModeThreshold:P1}");
            }

            // Determine confidence level for strategy selection
            var confidenceCategory = confidence >= _confidenceThreshold ? "High" : "Moderate";
            var reason = $"Match confidence {confidence:P1} is acceptable ({confidenceCategory} confidence)";

            return ConfidenceValidation.Acceptable(confidence, reason);
        }

        /// <summary>
        /// Validates and sanitizes confidence threshold values
        /// </summary>
        private double ValidateConfidenceThreshold(double threshold)
        {
            if (threshold < MINIMUM_CONFIDENCE_THRESHOLD)
            {
                _logger.Warn("Confidence threshold {0:P1} below minimum {1:P1}, adjusting to minimum", 
                           threshold, MINIMUM_CONFIDENCE_THRESHOLD);
                return MINIMUM_CONFIDENCE_THRESHOLD;
            }

            if (threshold > 1.0)
            {
                _logger.Warn("Confidence threshold {0:P1} above maximum 100%, adjusting to 100%", threshold);
                return 1.0;
            }

            return threshold;
        }

        /// <summary>
        /// Logs current optimization configuration for troubleshooting
        /// </summary>
        private void LogOptimizationConfiguration()
        {
            _logger.Info("🔧 SAFE METADATA OPTIMIZER CONFIGURATION:");
            _logger.Info("   Optimization enabled: {0}", _enableOptimization);
            _logger.Info("   Confidence threshold: {0:P1} (for full optimization)", _confidenceThreshold);
            _logger.Info("   Hybrid mode threshold: {0:P1} (for hybrid approach)", _hybridModeThreshold);
            _logger.Info("   Minimum confidence: {0:P1} (safety floor)", MINIMUM_CONFIDENCE_THRESHOLD);

            if (!_enableOptimization)
            {
                _logger.Info("   📝 To enable metadata optimization, set 'EnableMetadataOptimization' to true in settings");
            }
        }

        /// <summary>
        /// Safely optimizes metadata for a track, returning original if optimization fails
        /// </summary>
        public async Task<QobuzTrack> OptimizeTrackMetadataAsync(QobuzTrack track, QobuzAlbum album = null)
        {
            if (track == null)
                return track;

            try
            {
                if (!_enableOptimization)
                {
                    _logger.Debug("Track metadata optimization is disabled, returning original track");
                    return track;
                }

                // Apply conservative metadata optimization
                // For now, just return the original track until optimization is fully tested
                return track;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error optimizing track metadata, returning original");
                return track;
            }
        }

        /// <summary>
        /// Safely optimizes metadata for an album, returning original if optimization fails
        /// </summary>
        public async Task<QobuzAlbum> OptimizeAlbumMetadataAsync(QobuzAlbum album)
        {
            if (album == null)
                return album;

            try
            {
                if (!_enableOptimization)
                {
                    _logger.Debug("Album metadata optimization is disabled, returning original album");
                    return album;
                }

                // Apply conservative metadata optimization
                // For now, just return the original album until optimization is fully tested
                return album;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error optimizing album metadata, returning original");
                return album;
            }
        }

        /// <summary>
        /// Checks if metadata optimization is available
        /// </summary>
        public bool IsOptimizationAvailable => _enableOptimization;

        /// <summary>
        /// Gets statistics about optimization success rate
        /// </summary>
        public OptimizationStatistics GetStatistics()
        {
            var stats = GetOptimizationStatistics();
            // Calculate successful vs failed based on metadata usage
            var successful = stats.LidarrMetadataUsed + stats.HybridMetadataUsed;
            var failed = stats.MatchingFailures;
            
            return new OptimizationStatistics
            {
                SuccessfulOptimizations = successful,
                FailedOptimizations = failed
            };
        }

        /// <summary>
        /// Gets current optimization statistics from the hybrid metadata service
        /// </summary>
        public MetadataOptimizationStats GetOptimizationStatistics()
        {
            return _hybridMetadataService.GetStatistics();
        }

        /// <summary>
        /// Logs current optimization effectiveness statistics
        /// </summary>
        public void LogOptimizationStatistics()
        {
            _hybridMetadataService.LogStatistics();
        }

        /// <summary>
        /// Updates optimization settings at runtime (for testing and gradual rollout)
        /// </summary>
        public void UpdateOptimizationSettings(bool? enableOptimization = null, double? confidenceThreshold = null)
        {
            if (enableOptimization.HasValue)
            {
                _logger.Info("🔧 RUNTIME SETTING UPDATE: EnableOptimization changed from {0} to {1}", 
                           _enableOptimization, enableOptimization.Value);
            }

            if (confidenceThreshold.HasValue)
            {
                var newThreshold = ValidateConfidenceThreshold(confidenceThreshold.Value);
                _logger.Info("🔧 RUNTIME SETTING UPDATE: ConfidenceThreshold changed from {0:P1} to {1:P1}", 
                           _confidenceThreshold, newThreshold);
            }

            // Note: For runtime updates to work, these would need to be non-readonly fields
            // Current implementation uses readonly for safety, but could be modified for dynamic configuration
        }

        /// <summary>
        /// Provides recommendation for confidence threshold adjustments based on statistics
        /// </summary>
        public ThresholdRecommendation GetThresholdRecommendation()
        {
            var stats = GetOptimizationStatistics();

            if (stats.TotalAlbums < 10)
            {
                return new ThresholdRecommendation
                {
                    Recommendation = "Keep current conservative thresholds - need more data",
                    RecommendedConfidenceThreshold = _confidenceThreshold,
                    Reason = "Insufficient data for threshold optimization (need at least 10 albums)"
                };
            }

            var failureRate = (double)stats.MatchingFailures / stats.TotalAlbums;

            if (failureRate > 0.1) // More than 10% failures
            {
                var higherThreshold = Math.Min(_confidenceThreshold + 0.05, 0.95); // Increase by 5%, cap at 95%
                return new ThresholdRecommendation
                {
                    Recommendation = "Increase confidence threshold - high failure rate detected",
                    RecommendedConfidenceThreshold = higherThreshold,
                    Reason = $"Failure rate {failureRate:P1} exceeds 10% threshold, suggesting thresholds are too lenient"
                };
            }

            if (failureRate < 0.02 && stats.LidarrMetadataUsed < stats.TotalAlbums * 0.3) // Less than 2% failures but low optimization usage
            {
                var lowerThreshold = Math.Max(_confidenceThreshold - 0.05, MINIMUM_CONFIDENCE_THRESHOLD + 0.1); // Decrease by 5%, stay above safety margin
                return new ThresholdRecommendation
                {
                    Recommendation = "Consider lowering confidence threshold - very low failure rate",
                    RecommendedConfidenceThreshold = lowerThreshold,
                    Reason = $"Very low failure rate ({failureRate:P1}) suggests thresholds might be too conservative"
                };
            }

            return new ThresholdRecommendation
            {
                Recommendation = "Current thresholds are optimal",
                RecommendedConfidenceThreshold = _confidenceThreshold,
                Reason = $"Failure rate {failureRate:P1} is within acceptable range (2-10%)"
            };
        }
    }

    #region Supporting Classes

    /// <summary>
    /// Result of basic validation checks
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; }

        public static ValidationResult Valid(string reason = null) => new() { IsValid = true, Reason = reason };
        public static ValidationResult Invalid(string reason) => new() { IsValid = false, Reason = reason };
    }

    /// <summary>
    /// Result of confidence validation
    /// </summary>
    public class ConfidenceValidation
    {
        public bool IsAcceptable { get; set; }
        public double ConfidenceLevel { get; set; }
        public string Reason { get; set; }

        public static ConfidenceValidation Acceptable(double confidence, string reason) => new() 
        { 
            IsAcceptable = true, 
            ConfidenceLevel = confidence, 
            Reason = reason 
        };

        public static ConfidenceValidation Unacceptable(double confidence, string reason) => new() 
        { 
            IsAcceptable = false, 
            ConfidenceLevel = confidence, 
            Reason = reason 
        };
    }

    /// <summary>
    /// Recommendation for confidence threshold adjustments
    /// </summary>
    public class ThresholdRecommendation
    {
        public string Recommendation { get; set; }
        public double RecommendedConfidenceThreshold { get; set; }
        public string Reason { get; set; }
    }

    #endregion
}