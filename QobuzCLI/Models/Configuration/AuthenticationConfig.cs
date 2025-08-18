using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for Qobuz API authentication and regional settings.
    /// </summary>
    public class AuthenticationConfig
    {
        [JsonProperty("email")]
        public string? Email { get; set; }
        
        [JsonProperty("password")]
        public string? Password { get; set; }
        
        [JsonProperty("userId")]
        public string? UserId { get; set; }
        
        [JsonProperty("authToken")]
        public string? AuthToken { get; set; }
        
        [JsonProperty("authMethod")]
        public string AuthMethod { get; set; } = "email"; // email, token
        
        [JsonProperty("appId")]
        public string? AppId { get; set; }
        
        [JsonProperty("appSecret")]
        public string? AppSecret { get; set; }
        
        [JsonProperty("region")]
        public string Region { get; set; } = "CA"; // Default to Canada
        
        [JsonProperty("countryCode")]
        public string CountryCode { get; set; } = "CA"; // ISO 3166-1 alpha-2 country code

        /// <summary>
        /// Check if configuration has valid authentication credentials
        /// </summary>
        public bool HasValidAuth()
        {
            return IsTokenAuth() || IsEmailAuth();
        }

        /// <summary>
        /// Check if using token-based authentication
        /// </summary>
        public bool IsTokenAuth()
        {
            return AuthMethod == "token" && 
                   !string.IsNullOrEmpty(UserId) && 
                   !string.IsNullOrEmpty(AuthToken);
        }

        /// <summary>
        /// Check if using email/password authentication
        /// </summary>
        public bool IsEmailAuth()
        {
            return AuthMethod == "email" && 
                   !string.IsNullOrEmpty(Email) && 
                   !string.IsNullOrEmpty(Password);
        }
    }
}