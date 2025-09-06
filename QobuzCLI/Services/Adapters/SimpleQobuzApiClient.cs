using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
// Use alias to resolve ambiguity
using IQobuzHttpClient = Lidarr.Plugin.Qobuzarr.API.Http.IQobuzHttpClient;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Simple API client implementation for CLI usage.
    /// Wraps the HTTP client and auth service to provide API functionality.
    /// </summary>
    public class SimpleQobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzLogger _logger;
        private QobuzSession? _session;
        private Func<Task<QobuzCredentials>>? _credentialsProvider;
        private readonly object _sessionLock = new object();

        public SimpleQobuzApiClient(
            IQobuzHttpClient httpClient,
            IQobuzAuthenticationService authService,
            IQobuzLogger logger)
        {
            _httpClient = httpClient;
            _authService = authService;
            _logger = logger;
            _session = authService.GetCachedSession();
        }

        public void SetCredentialsProvider(Func<Task<QobuzCredentials>> credentialsProvider)
        {
            _credentialsProvider = credentialsProvider;
        }

        private async Task EnsureSessionAsync()
        {
            // Fast path
            if (_session != null && await _authService.ValidateSessionAsync(_session).ConfigureAwait(false))
            {
                return;
            }

            if (_credentialsProvider == null)
            {
                return; // Nothing we can do; caller will hit unauthorized if needed
            }

            // Slow path: re-auth using provided credentials
            try
            {
                var creds = await _credentialsProvider().ConfigureAwait(false);
                if (creds == null) return;
                var newSession = await _authService.AuthenticateAsync(creds).ConfigureAwait(false);
                lock (_sessionLock)
                {
                    _session = newSession;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Re-authentication failed: {ex.Message}");
            }
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            await EnsureSessionAsync().ConfigureAwait(false);
            // Build the URL with parameters
            var url = BuildUrl(endpoint, parameters);
            
            // Create HTTP request using the IQobuzHttpClient interface
            var requestBuilder = _httpClient.BuildRequest(url, "GET");
            var request = requestBuilder.Build();
            
            // Execute the request
            var response = await _httpClient.ExecuteAsync(request);
            
            // Deserialize the response
            if (response.Content != null)
            {
                var obj = JsonConvert.DeserializeObject<T>(response.Content);
                return obj ?? default!;
            }
            
            return default!;
        }

        public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
        {
            await EnsureSessionAsync().ConfigureAwait(false);
            // Build the URL
            var url = BuildUrl(endpoint, null);
            
            // Create HTTP request
            var requestBuilder = _httpClient.BuildRequest(url, "POST");
            requestBuilder.SetHeader("Content-Type", "application/json");
            
            var request = requestBuilder.Build();
            
            // Add POST data if provided
            if (data != null)
            {
                request.SetContent(JsonConvert.SerializeObject(data));
            }
            
            // Execute the request
            var response = await _httpClient.ExecuteAsync(request);
            
            // Deserialize the response
            if (response.Content != null)
            {
                var obj = JsonConvert.DeserializeObject<T>(response.Content);
                return obj ?? default!;
            }
            
            return default!;
        }

        public void SetSession(QobuzSession session)
        {
            _session = session;
        }

        public void ClearSession()
        {
            _session = null;
        }

        public bool HasValidSession()
        {
            return _session != null && !string.IsNullOrEmpty(_session.AuthToken);
        }

        public async Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            await EnsureSessionAsync().ConfigureAwait(false);
            var parameters = new Dictionary<string, string>
            {
                { "track_id", trackId },
                { "format_id", formatId.ToString() }
            };
            
            if (_session != null)
            {
                parameters["user_auth_token"] = _session.AuthToken;
                parameters["app_id"] = _session.AppId;
            }
            
            var response = await GetAsync<dynamic>("/track/getFileUrl", parameters);
            return response?.url?.ToString() ?? string.Empty;
        }
        
        private string BuildUrl(string endpoint, Dictionary<string, string>? parameters)
        {
            var baseUrl = "https://www.qobuz.com/api.json/0.2";
            var url = $"{baseUrl}{endpoint}";
            
            // Add auth parameters if we have a session
            var allParams = new Dictionary<string, string>();
            if (_session != null)
            {
                allParams["user_auth_token"] = _session.AuthToken;
                allParams["app_id"] = _session.AppId;
            }
            
            // Add any additional parameters
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    allParams[param.Key] = param.Value;
                }
            }
            
            // Build query string
            if (allParams.Count > 0)
            {
                var queryString = string.Join("&", allParams.Select(kvp => 
                    $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                url = $"{url}?{queryString}";
            }
            
            return url;
        }
    }
}
