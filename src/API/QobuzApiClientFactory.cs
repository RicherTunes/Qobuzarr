using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.API.Auth;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.API.Signing;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Lidarr.Plugin.Qobuzarr.API
{
    /// <summary>
    /// Factory for creating QobuzApiClient instances with all required dependencies.
    /// This factory ensures backward compatibility while using the new decomposed architecture.
    /// </summary>
    public class QobuzApiClientFactory : IQobuzApiClientFactory
    {
        private readonly IHttpClient _httpClient;
        private readonly ICacheManager _cacheManager;
        private readonly Logger _logger;

        public QobuzApiClientFactory(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _cacheManager = cacheManager;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new QobuzApiClient instance with all decomposed components.
        /// </summary>
        /// <param name="authService">Optional authentication service for session renewal.</param>
        /// <returns>A fully configured QobuzApiClient instance.</returns>
        public IQobuzApiClient CreateApiClient(IQobuzAuthenticationService? authService = null)
        {
            // Create specialized components
            var httpClient = new QobuzHttpClient(_httpClient, _logger);
            var authManager = new QobuzAuthenticationManager(_logger, authService);
            var requestSigner = new QobuzRequestSigner(_logger);
            var responseCache = new QobuzResponseCache(_cacheManager, _logger);

            // Create the orchestrator with all components
            var apiClient = new QobuzApiClient(
                httpClient,
                authManager,
                requestSigner,
                responseCache,
                _logger);

            // Set the authentication service if provided
            if (authService != null)
            {
                apiClient.SetAuthenticationService(authService);
            }

            return apiClient;
        }
    }

    /// <summary>
    /// Factory interface for creating QobuzApiClient instances.
    /// </summary>
    public interface IQobuzApiClientFactory
    {
        /// <summary>
        /// Creates a new QobuzApiClient instance.
        /// </summary>
        /// <param name="authService">Optional authentication service.</param>
        /// <returns>A configured QobuzApiClient instance.</returns>
        IQobuzApiClient CreateApiClient(IQobuzAuthenticationService? authService = null);
    }
}