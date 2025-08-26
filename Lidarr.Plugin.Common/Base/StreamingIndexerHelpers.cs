using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Models;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Common.Base
{
    /// <summary>
    /// SIMPLIFIED APPROACH: Helper utilities for streaming indexers instead of complex inheritance.
    /// Provides common functionality that any streaming plugin can use without complex integration.
    /// This follows the chief architect's guidance for incremental adoption.
    /// </summary>
    public static class StreamingIndexerHelpers
    {
        /// <summary>
        /// Builds a standard search URL for streaming services.
        /// </summary>
        public static string BuildSearchUrl(string baseUrl, string endpoint, Dictionary<string, string> parameters)
        {
            var url = $"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";
            return HttpClientExtensions.BuildUrlWithParams(url, parameters);
        }

        /// <summary>
        /// Creates standard HTTP headers for streaming service requests.
        /// </summary>
        public static Dictionary<string, string> CreateStreamingHeaders(string userAgent, string authToken = null)
        {
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = userAgent ?? "Lidarr-StreamingPlugin/1.0",
                ["Accept"] = "application/json",
                ["Accept-Encoding"] = "gzip, deflate"
            };

            if (!string.IsNullOrEmpty(authToken))
            {
                headers["Authorization"] = $"Bearer {authToken}";
            }

            return headers;
        }

        /// <summary>
        /// Estimates release size for Lidarr display.
        /// </summary>
        public static long EstimateReleaseSize(int trackCount, StreamingQualityTier qualityTier = StreamingQualityTier.Lossless)
        {
            var avgTrackSize = qualityTier switch
            {
                StreamingQualityTier.Low => 8_000_000L,      // 8MB - MP3 128kbps
                StreamingQualityTier.Normal => 12_000_000L,  // 12MB - MP3 256kbps
                StreamingQualityTier.High => 15_000_000L,    // 15MB - MP3 320kbps
                StreamingQualityTier.Lossless => 45_000_000L, // 45MB - FLAC CD
                StreamingQualityTier.HiRes => 80_000_000L,   // 80MB - FLAC Hi-Res
                _ => 30_000_000L
            };

            return trackCount * avgTrackSize;
        }

        /// <summary>
        /// Generates cache key for search results using shared patterns.
        /// </summary>
        public static string GenerateSearchCacheKey(string serviceName, string searchTerm, Dictionary<string, string> parameters = null)
        {
            var keyParts = new List<string> { serviceName, "search", searchTerm };
            
            if (parameters != null)
            {
                var sortedParams = parameters
                    .Where(p => !IsSensitiveParameter(p.Key))
                    .OrderBy(p => p.Key)
                    .Select(p => $"{p.Key}={p.Value}");
                keyParts.AddRange(sortedParams);
            }

            var fullKey = string.Join("_", keyParts);
            return Math.Abs(fullKey.GetHashCode()).ToString();
        }

        /// <summary>
        /// Validates search criteria and provides helpful error messages.
        /// </summary>
        public static (bool isValid, string errorMessage) ValidateSearchCriteria(string artist, string album, string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(artist) && 
                string.IsNullOrWhiteSpace(album) && 
                string.IsNullOrWhiteSpace(searchTerm))
            {
                return (false, "At least one search term (artist, album, or search term) is required");
            }

            // Check for potentially problematic search terms
            var combinedTerm = $"{artist} {album} {searchTerm}".Trim();
            if (combinedTerm.Length > 500)
            {
                return (false, "Search term is too long (max 500 characters)");
            }

            if (ContainsSqlInjectionPatterns(combinedTerm))
            {
                return (false, "Search term contains invalid characters");
            }

            return (true, null);
        }

        /// <summary>
        /// Creates standard download URL for streaming services.
        /// </summary>
        public static string CreateDownloadUrl(string serviceName, string albumId, string protocol = "unknown")
        {
            return $"{protocol.ToLowerInvariant()}://{serviceName.ToLowerInvariant()}/album/{albumId}";
        }

        /// <summary>
        /// Creates standard info URL for streaming services.
        /// </summary>
        public static string CreateInfoUrl(string baseWebUrl, string albumId, string urlTemplate = "/album/{0}")
        {
            return $"{baseWebUrl.TrimEnd('/')}{string.Format(urlTemplate, albumId)}";
        }

        /// <summary>
        /// Maps streaming search result to basic release info properties.
        /// Plugins can use this as a starting point and customize as needed.
        /// </summary>
        public static object CreateBasicReleaseInfo(StreamingSearchResult result, string indexerName)
        {
            return new
            {
                Guid = result.Id,
                Title = $"{result.Artist} - {result.Title}",
                Size = EstimateReleaseSize(result.TrackCount ?? 10),
                InfoUrl = result.Metadata?.GetValueOrDefault("infoUrl")?.ToString(),
                PublishDate = result.ReleaseDate ?? DateTime.UtcNow,
                Categories = new[] { 3030 }, // Audio category
                IndexerName = indexerName
            };
        }

        /// <summary>
        /// Safely extracts numeric ID from various string formats.
        /// </summary>
        public static string ExtractNumericId(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            // Extract numbers from URLs like "service://album/12345" or "https://api.service.com/albums/12345"
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Creates standard categories for music content.
        /// </summary>
        public static int[] GetMusicCategories(bool includeAudiobooks = false)
        {
            var categories = new List<int> { 3030 }; // Audio
            
            if (includeAudiobooks)
            {
                categories.Add(3030); // Could add audiobook categories if needed
            }

            return categories.ToArray();
        }

        private static bool IsSensitiveParameter(string parameterName)
        {
            var lowerName = parameterName?.ToLowerInvariant() ?? "";
            return lowerName.Contains("token") ||
                   lowerName.Contains("secret") ||
                   lowerName.Contains("password") ||
                   lowerName.Contains("auth") ||
                   lowerName.Contains("key");
        }

        private static bool ContainsSqlInjectionPatterns(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            
            var lower = input.ToLowerInvariant();
            return lower.Contains("'; ") ||
                   lower.Contains("' or ") ||
                   lower.Contains("union select") ||
                   lower.Contains("drop table") ||
                   lower.Contains("<script");
        }
    }

    /// <summary>
    /// SIMPLIFIED: Configuration helpers for streaming plugins.
    /// Provides utility methods without complex inheritance.
    /// </summary>
    public static class StreamingConfigHelpers
    {
        /// <summary>
        /// Validates common streaming service settings.
        /// </summary>
        public static (bool isValid, List<string> errors) ValidateStreamingSettings(
            string baseUrl, 
            string authMethod, 
            int searchLimit, 
            int rateLimit,
            string countryCode)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(baseUrl))
                errors.Add("Base URL is required");
            else if (!Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                errors.Add("Base URL must be a valid HTTP/HTTPS URL");

            if (searchLimit < 1 || searchLimit > 1000)
                errors.Add("Search limit must be between 1 and 1000");

            if (rateLimit < 1 || rateLimit > 1000)
                errors.Add("Rate limit must be between 1 and 1000 requests per minute");

            if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
                errors.Add("Country code must be a valid 2-letter code (e.g., 'US', 'GB')");

            return (errors.Count == 0, errors);
        }

        /// <summary>
        /// Creates masked settings object for safe logging.
        /// </summary>
        public static object CreateMaskedSettings(object settings)
        {
            if (settings == null) return null;

            // Use reflection to create masked version
            var type = settings.GetType();
            var properties = type.GetProperties();
            var masked = new Dictionary<string, object>();

            foreach (var prop in properties)
            {
                var name = prop.Name;
                var value = prop.GetValue(settings);

                if (IsSensitiveProperty(name))
                {
                    masked[name] = string.IsNullOrEmpty(value?.ToString()) ? "[not set]" : "[MASKED]";
                }
                else
                {
                    masked[name] = value;
                }
            }

            return masked;
        }

        private static bool IsSensitiveProperty(string propertyName)
        {
            var lower = propertyName.ToLowerInvariant();
            return lower.Contains("password") ||
                   lower.Contains("token") ||
                   lower.Contains("secret") ||
                   lower.Contains("key") ||
                   lower.Contains("auth");
        }
    }
}