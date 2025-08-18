using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for managing quality detection and fallback strategies
    /// </summary>
    public class QobuzQualityService
    {
        private readonly QobuzStreamUrlService _streamUrlService;
        private readonly IQobuzLogger _logger;
        
        // Quality IDs from Qobuz API:
        // 5 = MP3 320kbps
        // 6 = FLAC CD 16bit/44.1kHz  
        // 7 = FLAC Hi-Res 24bit up to 96kHz
        // 27 = FLAC Hi-Res 24bit up to 192kHz (max quality)
        private static readonly int[] QualityIds = { 27, 7, 6, 5 };

        public QobuzQualityService(
            QobuzStreamUrlService streamUrlService,
            IQobuzLogger logger)
        {
            _streamUrlService = streamUrlService ?? throw new ArgumentNullException(nameof(streamUrlService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Check which audio qualities are available for a track
        /// </summary>
        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            var available = new List<int>();
            
            _logger.Debug("Checking available qualities for track {0}", trackId);
            
            foreach (var quality in QualityIds)
            {
                try 
                {
                    var streamInfo = await _streamUrlService.GetStreamInfoAsync(trackId, quality);
                    
                    if (!string.IsNullOrWhiteSpace(streamInfo?.Url) && !IsPreviewOrSampleUrl(streamInfo.Url))
                    {
                        available.Add(quality);
                        _logger.Debug("Quality {0} available for track {1}", quality, trackId);
                    }
                    else
                    {
                        var reason = string.IsNullOrWhiteSpace(streamInfo?.Url) ? "invalid URL" : "preview/sample detected";
                        _logger.Debug("Quality {0} not available for track {1} ({2})", quality, trackId, reason);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} not available for track {1}: {2}", quality, trackId, ex.Message);
                }
            }

            _logger.Info("Track {0} has {1} available qualities: [{2}]", 
                trackId, available.Count, string.Join(", ", available));
            
            return available;
        }

        /// <summary>
        /// Get the best available stream URL with automatic quality fallback
        /// </summary>
        public async Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(string trackId, int preferredQuality)
        {
            var qualityChain = GetQualityFallbackChain(preferredQuality);
            
            _logger.Debug("Attempting quality fallback for track {0}, preferred quality {1}, chain: [{2}]", 
                trackId, preferredQuality, string.Join(", ", qualityChain));
            
            foreach (var quality in qualityChain)
            {
                try
                {
                    var streamInfo = await _streamUrlService.GetStreamInfoAsync(trackId, quality);
                    
                    if (!string.IsNullOrWhiteSpace(streamInfo?.Url) && !IsPreviewOrSampleUrl(streamInfo.Url))
                    {
                        if (quality != preferredQuality)
                        {
                            _logger.Info("Quality fallback for track {0}: requested {1}, using {2}", 
                                trackId, preferredQuality, quality);
                        }
                        
                        return (quality, streamInfo);
                    }
                    else
                    {
                        var reason = string.IsNullOrWhiteSpace(streamInfo?.Url) ? "invalid URL" : "preview/sample detected";
                        _logger.Debug("Quality {0} not suitable for track {1} ({2})", quality, trackId, reason);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} failed for track {1}: {2}", quality, trackId, ex.Message);
                }
            }
            
            throw new InvalidOperationException($"No available quality found for track {trackId}. Tried qualities: [{string.Join(", ", qualityChain)}]");
        }

        /// <summary>
        /// Generate quality fallback chain based on user's preferred quality
        /// </summary>
        private int[] GetQualityFallbackChain(int preferredQuality)
        {
            return preferredQuality switch
            {
                27 => new[] { 27, 7, 6, 5 },     // Hi-Res Max → Hi-Res → FLAC → MP3
                7  => new[] { 7, 6, 5 },         // Hi-Res → FLAC → MP3  
                6  => new[] { 6, 5 },            // FLAC → MP3
                5  => new[] { 5 },               // MP3 only
                _ => new[] { 27, 7, 6, 5 }       // Default: try all qualities
            };
        }

        /// <summary>
        /// Enhanced preview/sample detection based on URL patterns
        /// </summary>
        private bool IsPreviewOrSampleUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            var urlLower = url.ToLower();

            var previewPatterns = new[]
            {
                "_preview_", "_preview.", "preview_", "preview.",
                "sample_", "sample.", "_sample_", "_sample.",
                "/preview/", "/sample/", "preview=true", "sample=true",
                "_demo_", "_demo.", "demo_", "_30sec_", "_30s_",
                "duration=30", "clip_", "_clip_", "_short_"
            };

            foreach (var pattern in previewPatterns)
            {
                if (urlLower.Contains(pattern))
                {
                    _logger.Debug("Preview/sample detected in URL: pattern '{0}' found in {1}", pattern, url);
                    return true;
                }
            }

            // Additional heuristics based on URL structure
            if (urlLower.Contains("duration=") && 
                (urlLower.Contains("duration=30") || urlLower.Contains("duration=60")))
            {
                _logger.Debug("Short duration detected in URL, likely preview: {0}", url);
                return true;
            }

            // Check for preview-specific domains or subdomains
            if (urlLower.Contains("preview.") || urlLower.Contains("sample.") || urlLower.Contains("demo."))
            {
                _logger.Debug("Preview/sample subdomain detected: {0}", url);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get quality description for a quality ID
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

        /// <summary>
        /// Get all supported quality IDs
        /// </summary>
        public IReadOnlyList<int> GetSupportedQualities()
        {
            return QualityIds.ToList().AsReadOnly();
        }
    }
}