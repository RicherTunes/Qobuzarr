using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Common.Services.Diagnostics;
using Lidarr.Plugin.Common.Services.Performance;

namespace Lidarr.Plugin.Qobuzarr.API
{
    /// <summary>
    /// Decorator for QobuzApiClient that adds adaptive rate limiting
    /// Wraps the existing client to add automatic rate adjustment based on API responses
    /// </summary>
    public class AdaptiveQobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzApiClient _innerClient;
        private readonly IUniversalAdaptiveRateLimiter _adaptiveRateLimiter;
        private readonly Logger _logger;

        public AdaptiveQobuzApiClient(
            IQobuzApiClient innerClient,
            IUniversalAdaptiveRateLimiter adaptiveRateLimiter,
            Logger logger)
        {
            _innerClient = innerClient;
            _adaptiveRateLimiter = adaptiveRateLimiter;
            _logger = logger;
        }

        private void RecordResponseFromException(string endpoint, Exception ex)
        {
            var classification = HttpExceptionClassifier.Classify(ex);
            var statusCode = classification.Category switch
            {
                HttpFailureCategory.RateLimit => System.Net.HttpStatusCode.TooManyRequests,
                HttpFailureCategory.Auth => System.Net.HttpStatusCode.Unauthorized,
                _ => System.Net.HttpStatusCode.InternalServerError,
            };
            using var response = new HttpResponseMessage(statusCode);
            _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, response);
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            // Apply adaptive rate limiting before making the request
            await _adaptiveRateLimiter.WaitIfNeededAsync(QobuzarrConstants.ServiceName, endpoint).ConfigureAwait(false);

            try
            {
                var result = await _innerClient.GetAsync<T>(endpoint, parameters).ConfigureAwait(false);

                // Record successful response
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, successResponse);

                return result;
            }
            catch (Exception ex)
            {
                RecordResponseFromException(endpoint, ex);
                throw;
            }
        }

        public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
        {
            // Apply adaptive rate limiting before making the request
            await _adaptiveRateLimiter.WaitIfNeededAsync(QobuzarrConstants.ServiceName, endpoint).ConfigureAwait(false);

            try
            {
                var result = await _innerClient.PostAsync<T>(endpoint, data).ConfigureAwait(false);

                // Record successful response
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, successResponse);

                return result;
            }
            catch (Exception ex)
            {
                RecordResponseFromException(endpoint, ex);
                throw;
            }
        }

        /// <inheritdoc />
        /// <remarks>Delegated to the inner client; the gate (if any) lives there.</remarks>
        public AuthFailureGate? Gate => _innerClient.Gate;

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

        /// <summary>
        /// Get current rate limit statistics
        /// </summary>
        public RateLimitStats GetRateLimitStats()
        {
            var serviceStats = _adaptiveRateLimiter.GetServiceStats(QobuzarrConstants.ServiceName);
            // Convert to our expected type
            return new RateLimitStats
            {
                EndpointLimits = new Dictionary<string, int>(),
                TotalEndpoints = serviceStats.EndpointStats.Count
            };
        }

        /// <summary>
        /// Get current rate limit for a specific endpoint
        /// </summary>
        public int GetCurrentRateLimit(string endpoint)
        {
            return _adaptiveRateLimiter.GetCurrentLimit(QobuzarrConstants.ServiceName, endpoint);
        }

        /// <summary>
        /// Delegates streaming URL requests to the inner client with rate limiting.
        /// </summary>
        public async Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            var endpoint = "/track/getFileUrl";

            // Apply rate limiting
            await _adaptiveRateLimiter.WaitIfNeededAsync("Qobuz", endpoint, cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await _innerClient.GetStreamingUrlAsync(trackId, formatId, cancellationToken).ConfigureAwait(false);

                // Record successful response for rate limiter
                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, successResponse);

                return result;
            }
            catch (Exception ex)
            {
                RecordResponseFromException(endpoint, ex);
                throw;
            }
        }

        /// <summary>
        /// Delegates streaming response requests to the inner client with rate limiting.
        /// </summary>
        public async Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            var endpoint = "/track/getFileUrl";

            await _adaptiveRateLimiter.WaitIfNeededAsync("Qobuz", endpoint, cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await _innerClient.GetStreamingInfoAsync(trackId, formatId, cancellationToken).ConfigureAwait(false);

                var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, successResponse);

                return result;
            }
            catch (Exception ex)
            {
                RecordResponseFromException(endpoint, ex);
                throw;
            }
        }
    }
}
