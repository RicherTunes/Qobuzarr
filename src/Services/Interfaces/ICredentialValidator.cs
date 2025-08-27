using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for validating and sanitizing Qobuz authentication credentials.
    /// </summary>
    public interface ICredentialValidator
    {
        /// <summary>
        /// Performs comprehensive validation of Qobuz credentials.
        /// </summary>
        /// <param name="credentials">The credentials to validate</param>
        /// <returns>Validation result with detailed findings and sanitized values</returns>
        CredentialValidationResult ValidateCredentials(QobuzCredentials credentials);

        /// <summary>
        /// Validates and sanitizes an email address.
        /// </summary>
        /// <param name="email">The email to validate</param>
        /// <returns>Validation result with sanitized email</returns>
        EmailValidationResult ValidateEmail(string email);

        /// <summary>
        /// Validates password format and strength.
        /// </summary>
        /// <param name="password">The password to validate</param>
        /// <param name="requireStrong">Whether to enforce strong password requirements</param>
        /// <returns>Password validation result with strength assessment</returns>
        PasswordValidationResult ValidatePassword(string password, bool requireStrong = false);
    }
}

