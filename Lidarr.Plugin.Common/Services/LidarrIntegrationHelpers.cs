using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Models;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Common.Services
{
    /// <summary>
    /// Helper utilities for Lidarr integration that work with ANY streaming plugin.
    /// These are composition helpers, not inheritance - avoiding complex base class issues.
    /// Based on patterns from working Qobuzarr implementation.
    /// </summary>
    public static class LidarrIntegrationHelpers
    {
        /// <summary>
        /// CORRECTED: Creates ReleaseInfo using generic approach to avoid circular dependencies.
        /// Plugins provide the factory method, shared library provides the data mapping.
        /// </summary>
        public static T CreateReleaseInfo<T>(
            StreamingSearchResult result, 
            string indexerName, 
            Func<string, string, long, string, string, DateTime, int[], T> factory,
            string protocol = "unknown") where T : new()
        {
            var guid = result.Id;
            var title = GenerateTitle(result);
            var size = EstimateSize(result);
            var downloadUrl = CreateDownloadUrl(result, indexerName.ToLowerInvariant());
            var infoUrl = result.Metadata?.GetValueOrDefault("infoUrl")?.ToString() ?? "";
            var publishDate = result.ReleaseDate ?? DateTime.UtcNow;
            var categories = new[] { 3030 }; // Audio category

            return factory(guid, title, size, downloadUrl, infoUrl, publishDate, categories);
        }

        /// <summary>
        /// Alternative: Create properties dictionary for flexible ReleaseInfo creation.
        /// Avoids type dependencies while providing all necessary data.
        /// </summary>
        public static Dictionary<string, object> CreateReleaseProperties(StreamingSearchResult result, string indexerName, string protocol = "unknown")
        {
            return new Dictionary<string, object>
            {
                ["Guid"] = result.Id,
                ["Title"] = GenerateTitle(result),
                ["Size"] = EstimateSize(result),
                ["DownloadUrl"] = CreateDownloadUrl(result, indexerName.ToLowerInvariant()),
                ["InfoUrl"] = result.Metadata?.GetValueOrDefault("infoUrl")?.ToString() ?? "",
                ["Indexer"] = indexerName,
                ["PublishDate"] = result.ReleaseDate ?? DateTime.UtcNow,
                ["Categories"] = new[] { 3030 },
                ["DownloadProtocol"] = protocol,
                // Streaming-specific properties
                ["Artist"] = result.Artist,
                ["Album"] = result.Album,
                ["TrackCount"] = result.TrackCount,
                ["Genre"] = result.Genre,
                ["Label"] = result.Label
            };
        }

        /// <summary>
        /// Generates Lidarr-compatible titles from streaming search results.
        /// Follows patterns from working QobuzParser implementation.
        /// </summary>
        public static string GenerateTitle(StreamingSearchResult result)
        {
            if (result == null) return "Unknown Release";

            var parts = new List<string>();

            // Artist name (required)
            if (!string.IsNullOrEmpty(result.Artist))
                parts.Add(result.Artist);
            else
                parts.Add("Unknown Artist");

            // Album/track title (required)
            if (!string.IsNullOrEmpty(result.Album))
                parts.Add(result.Album);
            else if (!string.IsNullOrEmpty(result.Title))
                parts.Add(result.Title);
            else
                parts.Add("Unknown Title");

            // Additional context for disambiguation
            if (result.ReleaseDate.HasValue)
            {
                parts.Add($"({result.ReleaseDate.Value.Year})");
            }

            // Quality hint if available
            if (result.Metadata?.ContainsKey("quality") == true)
            {
                var quality = result.Metadata["quality"]?.ToString();
                if (!string.IsNullOrEmpty(quality) && 
                    (quality.Contains("FLAC") || quality.Contains("Hi-Res") || quality.Contains("MQA")))
                {
                    parts.Add($"[{quality}]");
                }
            }

            return string.Join(" - ", parts);
        }

        /// <summary>
        /// Creates standard download URLs for streaming services.
        /// Based on working patterns from Qobuzarr.
        /// </summary>
        public static string CreateDownloadUrl(StreamingSearchResult result, string serviceName)
        {
            // Use protocol://service/type/id format
            var type = result.Type.ToString().ToLowerInvariant();
            return $"{serviceName}://{type}/{result.Id}";
        }

        /// <summary>
        /// Estimates release size for Lidarr display.
        /// Based on working patterns and realistic streaming service file sizes.
        /// </summary>
        public static long EstimateSize(StreamingSearchResult result)
        {
            var trackCount = result.TrackCount ?? 10;
            
            // Try to detect quality from metadata
            var qualityTier = StreamingQualityTier.Normal; // Default
            
            if (result.Metadata?.ContainsKey("quality") == true)
            {
                var qualityStr = result.Metadata["quality"]?.ToString()?.ToLowerInvariant() ?? "";
                
                qualityTier = qualityStr switch
                {
                    var q when q.Contains("flac") && q.Contains("hires") => StreamingQualityTier.HiRes,
                    var q when q.Contains("flac") => StreamingQualityTier.Lossless,
                    var q when q.Contains("320") => StreamingQualityTier.High,
                    var q when q.Contains("256") => StreamingQualityTier.Normal,
                    _ => StreamingQualityTier.Normal
                };
            }

            return StreamingIndexerHelpers.EstimateReleaseSize(trackCount, qualityTier);
        }

        /// <summary>
        /// Builds search requests using shared HTTP patterns.
        /// Returns data that plugins can easily convert to IndexerRequest.
        /// </summary>
        public static RequestInfo BuildSearchRequest(
            string baseUrl, 
            string searchEndpoint,
            string searchTerm, 
            Dictionary<string, string> parameters = null,
            Dictionary<string, string> headers = null)
        {
            var requestParams = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>());
            
            // Add standard search parameters
            if (!requestParams.ContainsKey("q") && !requestParams.ContainsKey("query"))
                requestParams["q"] = searchTerm;

            var url = StreamingIndexerHelpers.BuildSearchUrl(baseUrl, searchEndpoint, requestParams);
            var requestHeaders = headers ?? new Dictionary<string, string>();

            return new RequestInfo
            {
                Url = url,
                Headers = requestHeaders,
                Parameters = requestParams,
                SearchTerm = searchTerm
            };
        }

        /// <summary>
        /// Parses common streaming API error responses.
        /// Based on patterns from working Qobuzarr error handling.
        /// </summary>
        public static (bool isError, string errorMessage, int? statusCode) ParseApiError(string responseContent, int statusCode)
        {
            if (statusCode >= 200 && statusCode < 300)
                return (false, null, statusCode);

            var errorMessage = statusCode switch
            {
                400 => "Bad request - check search parameters",
                401 => "Authentication failed - check credentials",
                403 => "Access forbidden - check subscription/permissions",
                404 => "Content not found",
                429 => "Rate limit exceeded - too many requests",
                500 => "Server error - try again later",
                503 => "Service unavailable - try again later",
                _ => $"API error (HTTP {statusCode})"
            };

            // Try to extract more specific error from response body
            if (!string.IsNullOrEmpty(responseContent))
            {
                try
                {
                    // Look for common error message patterns
                    if (responseContent.Contains("\"error\"") || responseContent.Contains("\"message\""))
                    {
                        // This could be enhanced to parse JSON error messages
                        errorMessage += " - See response for details";
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }

            return (true, errorMessage, statusCode);
        }

        /// <summary>
        /// Validates search criteria using shared patterns.
        /// Based on working validation from Qobuzarr.
        /// </summary>
        public static (bool isValid, string errorMessage) ValidateSearchRequest(
            string artist, 
            string album, 
            string searchTerm,
            int maxLength = 500)
        {
            return StreamingIndexerHelpers.ValidateSearchCriteria(artist, album, searchTerm);
        }

        /// <summary>
        /// Creates standard categories for streaming content.
        /// Based on working patterns from Qobuzarr.
        /// </summary>
        public static int[] GetStreamingCategories(bool includePodcasts = false)
        {
            var categories = new List<int> { 3030 }; // Audio

            if (includePodcasts)
            {
                categories.Add(3030); // Use same category for now
            }

            return categories.ToArray();
        }

        /// <summary>
        /// Helper for plugins to safely log request information.
        /// Masks sensitive data automatically.
        /// </summary>
        public static void LogRequest(object logger, string operation, RequestInfo requestInfo)
        {
            var maskedParams = HttpClientExtensions.MaskSensitiveParams(requestInfo.Parameters);
            var maskedHeaders = HttpClientExtensions.MaskSensitiveParams(requestInfo.Headers);

            // Use reflection to call logger.Debug if it's available
            try
            {
                var logMethod = logger.GetType().GetMethod("Debug", new[] { typeof(string), typeof(object[]) });
                logMethod?.Invoke(logger, new object[] { 
                    $"{operation} request: {requestInfo.Url} with {maskedParams.Count} parameters", 
                    new object[] { maskedParams, maskedHeaders } 
                });
            }
            catch
            {
                // Fallback to console if logger doesn't work
                System.Diagnostics.Debug.WriteLine($"{operation} request to {requestInfo.Url}");
            }
        }
    }

    /// <summary>
    /// Information about an HTTP request for streaming services.
    /// </summary>
    public class RequestInfo
    {
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public string SearchTerm { get; set; }
    }

    /// <summary>
    /// WORKING PATTERN: Helper for plugins to use shared caching without complex inheritance.
    /// Based on successful patterns from QobuzResponseCache.
    /// </summary>
    public class StreamingCacheHelper
    {
        private readonly string _serviceName;
        private readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();
        private readonly object _cacheLock = new object();

        public StreamingCacheHelper(string serviceName)
        {
            _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        }

        /// <summary>
        /// Gets cached data if available and not expired.
        /// </summary>
        public T Get<T>(string endpoint, Dictionary<string, string> parameters) where T : class
        {
            var cacheKey = GenerateCacheKey(endpoint, parameters);
            
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out var item))
                {
                    if (item.ExpiresAt > DateTime.UtcNow)
                    {
                        return item.Data as T;
                    }
                    else
                    {
                        _cache.Remove(cacheKey); // Clean up expired item
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets data in cache with TTL.
        /// </summary>
        public void Set<T>(string endpoint, Dictionary<string, string> parameters, T data, TimeSpan duration) where T : class
        {
            if (data == null) return;

            var cacheKey = GenerateCacheKey(endpoint, parameters);
            var expiresAt = DateTime.UtcNow.Add(duration);

            lock (_cacheLock)
            {
                _cache[cacheKey] = new CacheItem { Data = data, ExpiresAt = expiresAt };
            }
        }

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public void Clear()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        private string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters)
        {
            return StreamingIndexerHelpers.GenerateSearchCacheKey(_serviceName, endpoint, parameters);
        }

        private class CacheItem
        {
            public object Data { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}