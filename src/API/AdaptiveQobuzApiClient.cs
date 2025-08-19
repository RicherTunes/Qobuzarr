using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Lidarr.Plugin.Qobuzarr.API
{
    /// <summary>
    /// Decorator for QobuzApiClient that adds adaptive rate limiting
    /// Wraps the existing client to add automatic rate adjustment based on API responses
    /// </summary>
    public class AdaptiveQobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzApiClient _innerClient;
        private readonly IAdaptiveRateLimiter _adaptiveRateLimiter;
        private readonly Logger _logger;

        public AdaptiveQobuzApiClient(
            IQobuzApiClient innerClient,
            IAdaptiveRateLimiter adaptiveRateLimiter,
            Logger logger)
        {
            _innerClient = innerClient;
            _adaptiveRateLimiter = adaptiveRateLimiter;
            _logger = logger;
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            // Apply adaptive rate limiting before making the request
            await _adaptiveRateLimiter.WaitIfNeededAsync(endpoint).ConfigureAwait(false);
            
            try
            {
                var result = await _innerClient.GetAsync<T>(endpoint, parameters).ConfigureAwait(false);
                
                // Record successful response
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(endpoint, successResponse);
                
                return result;
            }
            catch (Exception ex)
            {
                // Record error response
                var errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                if (ex.Message.Contains("429") || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                }
                else if (ex.Message.Contains("401") || ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
                }
                
                _adaptiveRateLimiter.RecordResponse(endpoint, errorResponse);
                throw;
            }
        }

        public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
        {
            // Apply adaptive rate limiting before making the request
            await _adaptiveRateLimiter.WaitIfNeededAsync(endpoint).ConfigureAwait(false);
            
            try
            {
                var result = await _innerClient.PostAsync<T>(endpoint, data).ConfigureAwait(false);
                
                // Record successful response
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(endpoint, successResponse);
                
                return result;
            }
            catch (Exception ex)
            {
                // Record error response
                var errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                if (ex.Message.Contains("429") || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                }
                
                _adaptiveRateLimiter.RecordResponse(endpoint, errorResponse);
                throw;
            }
        }

        public void SetSession(QobuzSession session)
        {
            _innerClient.SetSession(session);
        }

        public void ClearSession()
        {
            _innerClient.ClearSession();
        }

        public bool HasValidSession()
        {
            return _innerClient.HasValidSession();
        }

        public async Task<Models.QobuzAlbum> GetAlbumAsync(string albumId)
        {
            // Apply adaptive rate limiting before making the request
            await _adaptiveRateLimiter.WaitIfNeededAsync("album/get").ConfigureAwait(false);
            
            try
            {
                var result = await _innerClient.GetAlbumAsync(albumId).ConfigureAwait(false);
                
                // Record successful response
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse("album/get", successResponse);
                
                return result;
            }
            catch (Exception ex)
            {
                // Record error response
                var errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                if (ex.Message.Contains("429") || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                }
                else if (ex.Message.Contains("401") || ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
                }
                
                _adaptiveRateLimiter.RecordResponse("album/get", errorResponse);
                throw;
            }
        }

        /// <summary>
        /// Get current rate limit statistics
        /// </summary>
        public RateLimitStats GetRateLimitStats()
        {
            return _adaptiveRateLimiter.GetStats();
        }

        /// <summary>
        /// Get current rate limit for a specific endpoint
        /// </summary>
        public int GetCurrentRateLimit(string endpoint)
        {
            return _adaptiveRateLimiter.GetCurrentLimit(endpoint);
        }
    }
}