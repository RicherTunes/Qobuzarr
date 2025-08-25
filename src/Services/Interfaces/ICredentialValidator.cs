using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Core.Auth;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for validating and sanitizing Qobuz authentication credentials.
    /// </summary>
    /// <remarks>
    /// This interface provides comprehensive credential validation with security checks,
    /// format validation, and sanitization capabilities.
    /// 
    /// Key Features:
    /// - Comprehensive credential validation (email/password, token auth)
    /// - Input sanitization and security validation
    /// - Format validation for all credential types
    /// - Password strength assessment
    /// - Email format and domain validation
    /// - App credential validation (App ID/Secret)
    /// 
    /// This is a pure domain service focused on validation logic without
    /// external dependencies on APIs or authentication systems.
    /// </remarks>
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