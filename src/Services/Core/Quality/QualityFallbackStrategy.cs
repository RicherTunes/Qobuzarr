using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Quality
{
    /// <summary>
    /// Handles quality fallback chain logic and strategies.
    /// Single responsibility: Generate and manage quality fallback chains based on various criteria.
    /// </summary>
    public interface IQualityFallbackStrategy
    {
        /// <summary>
        /// Creates a fallback chain for a preferred quality.
        /// </summary>
        IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred);

        /// <summary>
        /// Creates a fallback chain based on subscription tier constraints.
        /// </summary>
        IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred, QobuzSubscriptionTier subscriptionTier);

        /// <summary>
        /// Creates a fallback chain from Lidarr quality profile.
        /// </summary>
        IReadOnlyList<QualityFormat> CreateFallbackChainFromProfile(LidarrQualityProfile profile);

        /// <summary>
        /// Determines if quality fallback should be attempted based on error context.
        /// </summary>
        bool ShouldAttemptFallback(Exception exception, QualityFormat attemptedQuality);

        /// <summary>
        /// Gets next quality in fallback sequence.
        /// </summary>
        QualityFormat GetNextFallbackQuality(QualityFormat current, IReadOnlyList<QualityFormat> chain);
    }

    /// <summary>
    /// Implementation of quality fallback strategy with intelligent chain generation.
    /// </summary>
    public class QualityFallbackStrategy : IQualityFallbackStrategy
    {
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly IQobuzLogger _logger;

        // Lidarr quality profile mappings for intelligent fallback
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

        public QualityFallbackStrategy(
            IQualityDefinitionService qualityDefinitionService,
            IQobuzLogger logger)
        {
            _qualityDefinitionService = qualityDefinitionService ?? throw new ArgumentNullException(nameof(qualityDefinitionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred)
        {
            var chain = new List<QualityFormat>();
            
            // Start with preferred quality if valid
            if (preferred != null && _qualityDefinitionService.IsQualitySupported(preferred.Id))
            {
                chain.Add(preferred);
            }
            
            // Add lower qualities as fallbacks, ordered by descending priority
            var preferredPriority = preferred?.Priority ?? int.MaxValue;
            var allQualities = _qualityDefinitionService.GetSupportedQualities();
            
            foreach (var quality in allQualities.Where(q => q.Priority < preferredPriority))
            {
                chain.Add(quality);
            }
            
            // Ensure at least MP3 is in the chain as ultimate fallback
            var mp3Quality = _qualityDefinitionService.GetQualityById(5);
            if (!chain.Any(q => q.Id == mp3Quality.Id))
            {
                chain.Add(mp3Quality);
            }
            
            _logger.Debug("Created fallback chain for quality {0}: [{1}]", 
                preferred?.Name ?? "None", 
                string.Join(", ", chain.Select(q => q.Name)));
            
            return chain.AsReadOnly();
        }

        public IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred, QobuzSubscriptionTier subscriptionTier)
        {
            var baseChain = CreateFallbackChain(preferred).ToList();
            
            // Apply subscription tier constraints
            switch (subscriptionTier)
            {
                case QobuzSubscriptionTier.Free:
                    _logger.Debug("Free subscription detected - no full track access");
                    return new List<QualityFormat>().AsReadOnly(); // No full tracks available
                    
                case QobuzSubscriptionTier.Sublime:
                    // Sublime users limited to CD quality
                    var cdQualityAndBelow = baseChain.Where(q => q.Id <= 6).ToList();
                    _logger.Debug("Sublime subscription detected - limiting to CD quality and below");
                    return cdQualityAndBelow.AsReadOnly();
                    
                case QobuzSubscriptionTier.StudioPremier:
                case QobuzSubscriptionTier.StudioSublime:
                    // Full access to all qualities
                    return baseChain.AsReadOnly();
                    
                default:
                    // Unknown tier - use conservative approach (CD and below)
                    var conservativeChain = baseChain.Where(q => q.Id <= 6).ToList();
                    _logger.Debug("Unknown subscription tier - using conservative quality chain");
                    return conservativeChain.AsReadOnly();
            }
        }

        public IReadOnlyList<QualityFormat> CreateFallbackChainFromProfile(LidarrQualityProfile profile)
        {
            if (profile == null)
            {
                var defaultQuality = _qualityDefinitionService.GetDefaultQuality();
                return CreateFallbackChain(defaultQuality);
            }

            _logger.Debug("Creating fallback chain from Lidarr profile: {0}", profile.Name);

            // Try mapping based on profile name patterns
            foreach (var mapping in LidarrQualityMappings)
            {
                if (profile.Name.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var mappedQuality = _qualityDefinitionService.GetQualityById(mapping.Value);
                    _logger.Debug("Mapped profile '{0}' to quality {1} via pattern '{2}'", 
                        profile.Name, mappedQuality.Name, mapping.Key);
                    return CreateFallbackChain(mappedQuality);
                }
            }

            // Analyze quality items in profile for preferred quality
            var preferredQuality = profile.GetPreferredQuality();
            if (preferredQuality != null)
            {
                var mappedQuality = MapLidarrQualityToQobuz(preferredQuality);
                if (mappedQuality != null)
                {
                    _logger.Debug("Mapped profile preferred quality '{0}' to {1}", 
                        preferredQuality.Name, mappedQuality.Name);
                    return CreateFallbackChain(mappedQuality);
                }
            }

            // Default fallback
            var defaultQuality = _qualityDefinitionService.GetDefaultQuality();
            _logger.Debug("Using default quality {0} for unmappable profile", defaultQuality.Name);
            return CreateFallbackChain(defaultQuality);
        }

        public bool ShouldAttemptFallback(Exception exception, QualityFormat attemptedQuality)
        {
            if (exception == null) return true;
            
            // Don't retry on cancellation
            if (exception is OperationCanceledException)
            {
                _logger.Debug("No fallback - operation was cancelled");
                return false;
            }
            
            // Don't retry on authentication failures
            var message = exception.Message.ToLowerInvariant();
            if (message.Contains("authentication") || message.Contains("unauthorized"))
            {
                _logger.Debug("No fallback - authentication issue detected");
                return false;
            }
            
            // Don't retry on argument exceptions (programming errors)
            if (exception is ArgumentException)
            {
                _logger.Debug("No fallback - argument validation error");
                return false;
            }
            
            // Don't fallback if already at lowest quality
            if (attemptedQuality?.Id == 5) // MP3 320
            {
                _logger.Debug("No fallback - already at lowest quality (MP3)");
                return false;
            }
            
            // Retry on most other exceptions (network issues, quality unavailable, etc.)
            _logger.Debug("Attempting fallback for quality {0} due to: {1}", 
                attemptedQuality?.Name ?? "Unknown", exception.Message);
            return true;
        }

        public QualityFormat GetNextFallbackQuality(QualityFormat current, IReadOnlyList<QualityFormat> chain)
        {
            if (current == null || chain == null || !chain.Any()) return null;
            
            var currentIndex = chain.ToList().FindIndex(q => q.Id == current.Id);
            if (currentIndex == -1 || currentIndex >= chain.Count - 1)
            {
                return null; // No next quality or current not in chain
            }
            
            return chain[currentIndex + 1];
        }

        private QualityFormat MapLidarrQualityToQobuz(LidarrQuality lidarrQuality)
        {
            if (lidarrQuality == null) return null;
            
            // Map based on quality properties
            var qualityId = 6; // Default to CD quality
            
            if (lidarrQuality.Name.Contains("Hi-Res", StringComparison.OrdinalIgnoreCase) ||
                lidarrQuality.Name.Contains("24", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 27; // Hi-Res 192
            }
            else if (lidarrQuality.Name.Contains("96", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 7; // Hi-Res 96
            }
            else if (lidarrQuality.Name.Contains("MP3", StringComparison.OrdinalIgnoreCase) ||
                     lidarrQuality.Name.Contains("320", StringComparison.OrdinalIgnoreCase))
            {
                qualityId = 5; // MP3
            }

            return _qualityDefinitionService.IsQualitySupported(qualityId) 
                ? _qualityDefinitionService.GetQualityById(qualityId) 
                : _qualityDefinitionService.GetDefaultQuality();
        }
    }

    /// <summary>
    /// Qobuz subscription tiers for quality constraints.
    /// </summary>
    public enum QobuzSubscriptionTier
    {
        Free = 0,
        Sublime = 1,
        StudioPremier = 2,
        StudioSublime = 3
    }
}