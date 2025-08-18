using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.API.Caching
{
    /// <summary>
    /// Manages response caching for Qobuz API calls to reduce redundant requests.
    /// This interface handles cache key generation, TTL determination, and cache operations.
    /// </summary>
    public interface IQobuzResponseCache
    {
        /// <summary>
        /// Attempts to get a cached response.
        /// </summary>
        /// <typeparam name="T">The type of the cached object.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="parameters">The request parameters.</param>
        /// <returns>The cached object if found; null otherwise.</returns>
        T? Get<T>(string endpoint, Dictionary<string, string> parameters) where T : class;

        /// <summary>
        /// Stores a response in the cache.
        /// </summary>
        /// <typeparam name="T">The type of the object to cache.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="parameters">The request parameters.</param>
        /// <param name="value">The value to cache.</param>
        void Set<T>(string endpoint, Dictionary<string, string> parameters, T value) where T : class;

        /// <summary>
        /// Stores a response in the cache with a specific duration.
        /// </summary>
        /// <typeparam name="T">The type of the object to cache.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="parameters">The request parameters.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="duration">The cache duration.</param>
        void Set<T>(string endpoint, Dictionary<string, string> parameters, T value, TimeSpan duration) where T : class;

        /// <summary>
        /// Determines if an endpoint should be cached.
        /// </summary>
        /// <param name="endpoint">The API endpoint to check.</param>
        /// <returns>True if the endpoint should be cached; false otherwise.</returns>
        bool ShouldCache(string endpoint);

        /// <summary>
        /// Gets the appropriate cache duration for an endpoint.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <returns>The cache duration for the endpoint.</returns>
        TimeSpan GetCacheDuration(string endpoint);

        /// <summary>
        /// Generates a cache key for the given endpoint and parameters.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="parameters">The request parameters.</param>
        /// <returns>The cache key.</returns>
        string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters);

        /// <summary>
        /// Clears all cached responses.
        /// </summary>
        void Clear();

        /// <summary>
        /// Clears cached responses for a specific endpoint.
        /// </summary>
        /// <param name="endpoint">The API endpoint to clear.</param>
        void ClearEndpoint(string endpoint);
    }
}