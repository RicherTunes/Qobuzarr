using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models.Authentication
{
    public class QobuzLoginResponse
    {
        [JsonProperty("user")]
        public QobuzUser User { get; set; }

        [JsonProperty("user_auth_token")]
        public string UserAuthToken { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Code == null || Code == 200;
    }

    public class QobuzUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("credential")]
        public QobuzCredential Credential { get; set; }

        [JsonProperty("subscription")]
        public QobuzSubscriptionDetails Subscription { get; set; }
    }

    public class QobuzCredential
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class QobuzSubscriptionDetails
    {
        [JsonProperty("offer")]
        public string Offer { get; set; }

        [JsonProperty("periodicity")]
        public string Periodicity { get; set; }

        [JsonProperty("start_date")]
        public long StartDate { get; set; }

        [JsonProperty("end_date")]
        public long? EndDate { get; set; }

        [JsonProperty("is_active")]
        public bool IsActive { get; set; }

        [JsonProperty("is_canceled")]
        public bool IsCanceled { get; set; }

        public QobuzSubscription ToSubscription()
        {
            // Map the detailed subscription to our simplified model
            var isHiRes = Offer?.Contains("sublime", StringComparison.OrdinalIgnoreCase) == true ||
                         Offer?.Contains("studio", StringComparison.OrdinalIgnoreCase) == true;

            var maxSampleRate = Offer?.ToLowerInvariant() switch
            {
                var o when o.Contains("sublime") => 192000,
                var o when o.Contains("studio") => 96000,
                _ => 44100
            };

            return new QobuzSubscription
            {
                Type = Offer,
                IsHiRes = isHiRes,
                MaxSampleRate = maxSampleRate,
                MaxBitDepth = isHiRes ? 24 : 16,
                CanStream = IsActive,
                CanDownload = IsActive && !IsCanceled
            };
        }
    }
}
