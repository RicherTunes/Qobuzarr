using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a Qobuz user
    /// </summary>
    public class QobuzUser
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("publicId")]
        public string PublicId { get; set; }

        [JsonProperty("login")]
        public string Login { get; set; }

        [JsonProperty("firstname")]
        public string FirstName { get; set; }

        [JsonProperty("lastname")]
        public string LastName { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("avatar")]
        public string AvatarUrl { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        /// <summary>
        /// Get display name or fallback to login
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return DisplayName;
            
            if (!string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName))
                return $"{FirstName} {LastName}".Trim();
            
            return Login ?? $"User {Id}";
        }
    }
}