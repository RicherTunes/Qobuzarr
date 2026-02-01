using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Models.Authentication
{
    public class QobuzCredentials : IAuthCredentials
    {
        [JsonProperty("email")]
        public string? Email { get; set; }

        [JsonProperty("password")]
        public string? MD5Password { get; set; }

        [JsonProperty("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("user_auth_token")]
        public string? AuthToken { get; set; }

        [JsonProperty("app_id")]
        public string? AppId { get; set; }

        [JsonProperty("app_secret")]
        public string? AppSecret { get; set; }

        /// <summary>
        /// Validates the credentials based on authentication method
        /// App ID is optional - empty values will use built-in defaults
        /// </summary>
        public bool IsValid()
        {
            // Validate email/password authentication
            if (!string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(MD5Password))
            {
                return IsValidEmail(Email);
            }

            // Validate token authentication
            if (!string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(AuthToken))
            {
                return true;
            }

            return false;
        }

        // IAuthCredentials implementation for shared contract
        [JsonIgnore]
        public AuthenticationType Type => IsEmailAuth()
            ? AuthenticationType.UsernamePassword
            : AuthenticationType.Token;

        public bool IsValid(out string errorMessage)
        {
            if (IsValid())
            {
                errorMessage = string.Empty;
                return true;
            }
            errorMessage = "Invalid credentials: provide Email+MD5Password or UserId+AuthToken.";
            return false;
        }

        /// <summary>
        /// Returns true if using email/password authentication
        /// </summary>
        public bool IsEmailAuth()
        {
            return !string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(MD5Password);
        }

        /// <summary>
        /// Returns true if using token authentication
        /// Email auth takes precedence over token auth if both are present
        /// </summary>
        public bool IsTokenAuth()
        {
            // Email auth takes precedence - if we have email auth data, this is not token auth
            if (IsEmailAuth())
            {
                return false;
            }

            return !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(AuthToken);
        }

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var emailAttribute = new EmailAddressAttribute();
                var isValidFormat = emailAttribute.IsValid(email);

                // Additional validation: email must have a username part before @
                var hasUsernameBeforeAt = email.IndexOf('@') > 0;

                return isValidFormat && hasUsernameBeforeAt;
            }
            catch
            {
                return false;
            }
        }
    }
}
