using System;
using System.Collections.Generic;
using System.Linq;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Utility class for detecting preview/sample URLs and content.
    /// Centralizes the logic for identifying limited versions of tracks.
    /// </summary>
    public static class PreviewDetectionUtility
    {
        /// <summary>
        /// Cached patterns for preview/sample URL detection.
        /// These patterns are commonly used by streaming services to denote limited versions.
        /// </summary>
        private static readonly string[] PreviewUrlPatterns = new[]
        {
            // Common preview/sample identifiers
            "_preview_", "_preview.", "preview_", "preview.",
            "_sample_", "_sample.", "sample_", "sample.",
            "/preview/", "/sample/", 
            
            // Query parameters
            "preview=true", "sample=true", "preview=1", "sample=1",
            
            // Demo and duration-limited patterns
            "_demo_", "_demo.", "demo_", 
            "_30sec_", "_30s_", "_clip_", "_short_",
            "duration=30", "clip_", 
            
            // Format-specific patterns
            "_excerpt_", "excerpt_", 
            "_teaser_", "teaser_",
            "_snippet_", "snippet_"
        };

        /// <summary>
        /// Common preview/sample duration limits in seconds.
        /// </summary>
        private static readonly int[] PreviewDurationLimits = new[] { 30, 60, 90 };

        /// <summary>
        /// Checks if a URL appears to be for a preview or sample version of a track.
        /// </summary>
        /// <param name="url">The URL to check</param>
        /// <returns>True if the URL appears to be for a preview/sample</returns>
        public static bool IsPreviewOrSampleUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var urlLower = url.ToLowerInvariant();
            return PreviewUrlPatterns.Any(pattern => urlLower.Contains(pattern));
        }

        /// <summary>
        /// Checks if a track duration indicates it's likely a preview.
        /// </summary>
        /// <param name="durationSeconds">The track duration in seconds</param>
        /// <returns>True if the duration suggests this is a preview</returns>
        public static bool IsPreviewDuration(int durationSeconds)
        {
            return durationSeconds > 0 && PreviewDurationLimits.Contains(durationSeconds);
        }

        /// <summary>
        /// Analyzes multiple indicators to determine if content is a preview.
        /// </summary>
        /// <param name="url">The stream URL</param>
        /// <param name="durationSeconds">The track duration in seconds</param>
        /// <param name="restrictionMessage">Any restriction message from the API</param>
        /// <returns>True if any indicator suggests this is preview content</returns>
        public static bool IsLikelyPreview(string url, int? durationSeconds, string restrictionMessage)
        {
            // Check URL patterns
            if (!string.IsNullOrWhiteSpace(url) && IsPreviewOrSampleUrl(url))
                return true;

            // Check duration
            if (durationSeconds.HasValue && IsPreviewDuration(durationSeconds.Value))
                return true;

            // Check restriction message
            if (!string.IsNullOrWhiteSpace(restrictionMessage))
            {
                var messageLower = restrictionMessage.ToLowerInvariant();
                if (messageLower.Contains("preview") || messageLower.Contains("sample") || 
                    messageLower.Contains("excerpt") || messageLower.Contains("clip"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets a user-friendly message for preview-only content.
        /// </summary>
        /// <param name="trackTitle">The track title for context</param>
        /// <returns>A formatted message explaining the preview limitation</returns>
        public static string GetPreviewMessage(string trackTitle)
        {
            return $"Track '{trackTitle}' is only available as a preview/sample. Full version requires different subscription or is restricted.";
        }
    }
}