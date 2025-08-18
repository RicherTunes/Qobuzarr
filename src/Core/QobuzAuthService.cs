using System;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Core authentication service with no Lidarr dependencies
    /// </summary>
    public class QobuzAuthService
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly IQobuzCache _cache;
        private const string API_BASE = "https://www.qobuz.com/api.json/0.2";
        private const string SESSION_CACHE_KEY = "qobuz_session";

        public QobuzAuthService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            IQobuzCache cache)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
        }

        public async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
        {
            if (!credentials.IsValid())
            {
                throw new ArgumentException("Invalid credentials");
            }

            try
            {
                if (credentials.IsTokenAuth())
                {
                    // Token auth - just validate and create session
                    if (string.IsNullOrEmpty(credentials.AppSecret))
                    {
                        throw new ArgumentException("App secret is required for authentication", nameof(credentials));
                    }
                    
                    var session = QobuzSession.CreateSession(
                        credentials.UserId,
                        credentials.AuthToken,
                        credentials.AppId ?? "798273057",
                        credentials.AppSecret);
                    
                    // Validate by making a test call
                    var testUrl = $"{API_BASE}/user/login?app_id={session.AppId}&user_auth_token={session.AuthToken}";
                    await _httpClient.GetStringAsync(testUrl);
                    
                    StoreSession(session);
                    return session;
                }
                else if (credentials.IsEmailAuth())
                {
                    // Email/password auth
                    if (string.IsNullOrEmpty(credentials.AppSecret))
                    {
                        throw new ArgumentException("App secret is required for authentication", nameof(credentials));
                    }
                    
                    var url = $"{API_BASE}/user/login?app_id={credentials.AppId}&email={credentials.Email}&password={credentials.MD5Password}";
                    var response = await _httpClient.GetJsonAsync<QobuzLoginResponse>(url);
                    
                    if (response?.UserAuthToken == null)
                    {
                        throw new InvalidOperationException("Authentication failed");
                    }
                    
                    var session = QobuzSession.CreateSession(
                        response.User.Id,
                        response.UserAuthToken,
                        credentials.AppId ?? "798273057",
                        credentials.AppSecret);
                    
                    StoreSession(session);
                    return session;
                }
                else
                {
                    throw new InvalidOperationException("No valid authentication method");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Authentication failed");
                throw;
            }
        }

        public QobuzSession? GetCurrentSession()
        {
            return _cache.Get<QobuzSession>(SESSION_CACHE_KEY);
        }

        public void StoreSession(QobuzSession session)
        {
            _cache.Set(SESSION_CACHE_KEY, session, TimeSpan.FromHours(24));
            _logger.Info("Session stored successfully");
        }

        public void ClearSession()
        {
            _cache.Remove(SESSION_CACHE_KEY);
            _logger.Info("Session cleared");
        }
    }
}