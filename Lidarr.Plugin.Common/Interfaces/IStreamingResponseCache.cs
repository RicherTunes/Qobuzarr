using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Common.Interfaces
{
    /// <summary>
    /// Generic response cache interface for streaming service API calls.
    /// </summary>
    public interface IStreamingResponseCache
    {
        /// <summary>
        /// Gets a cached response if available.
        /// </summary>
        /// <typeparam name="T">Type of the cached object</typeparam>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <returns>Cached object or null if not found</returns>
        T? Get<T>(string endpoint, Dictionary<string, string> parameters) where T : class;

        /// <summary>
        /// Sets a response in the cache with default duration.
        /// </summary>
        /// <typeparam name="T">Type of the object to cache</typeparam>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <param name="value">Value to cache</param>
        void Set<T>(string endpoint, Dictionary<string, string> parameters, T value) where T : class;

        /// <summary>
        /// Sets a response in the cache with specific duration.
        /// </summary>
        /// <typeparam name="T">Type of the object to cache</typeparam>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <param name="value">Value to cache</param>
        /// <param name="duration">Cache duration</param>
        void Set<T>(string endpoint, Dictionary<string, string> parameters, T value, TimeSpan duration) where T : class;

        /// <summary>
        /// Determines if an endpoint should be cached.
        /// </summary>
        /// <param name="endpoint">API endpoint</param>
        /// <returns>True if the endpoint should be cached</returns>
        bool ShouldCache(string endpoint);

        /// <summary>
        /// Gets the default cache duration for an endpoint.
        /// </summary>
        /// <param name="endpoint">API endpoint</param>
        /// <returns>Cache duration</returns>
        TimeSpan GetCacheDuration(string endpoint);

        /// <summary>
        /// Generates a cache key for the given endpoint and parameters.
        /// </summary>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <returns>Cache key</returns>
        string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters);

        /// <summary>
        /// Clears all cached responses.
        /// </summary>
        void Clear();

        /// <summary>
        /// Clears cached responses for a specific endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint to clear</param>
        void ClearEndpoint(string endpoint);
    }
}