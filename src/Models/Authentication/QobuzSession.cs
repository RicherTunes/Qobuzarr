using System;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models.Authentication
{
    public class QobuzSession
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("user_auth_token")]
        public string AuthToken { get; set; }

        [JsonProperty("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [JsonProperty("subscription")]
        public QobuzSubscription Subscription { get; set; }

        [JsonProperty("app_id")]
        public string AppId { get; set; }

        [JsonProperty("app_secret")]
        public string AppSecret { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Check if the session is still valid
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(UserId) && 
                   !string.IsNullOrEmpty(AuthToken) && 
                   DateTime.UtcNow < ExpiresAt;
        }

        /// <summary>
        /// Check if the session expires within the specified timespan
        /// </summary>
        public bool ExpiresWithin(TimeSpan timespan)
        {
            return DateTime.UtcNow.Add(timespan) >= ExpiresAt;
        }

        /// <summary>
        /// Check if session needs refresh (30 minutes before expiry)
        /// </summary>
        public bool NeedsRefresh()
        {
            return ExpiresWithin(TimeSpan.FromMinutes(30));
        }

        /// <summary>
        /// Create a session with 24-hour validity
        /// </summary>
        public static QobuzSession CreateSession(string userId, string authToken, string appId, string appSecret, QobuzSubscription subscription = null)
        {
            if (string.IsNullOrEmpty(appSecret))
            {
                throw new ArgumentException("App secret is required and must be provided via configuration", nameof(appSecret));
            }

            return new QobuzSession
            {
                UserId = userId,
                AuthToken = authToken,
                AppId = appId,
                AppSecret = appSecret,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                Subscription = subscription
            };
        }
    }
}