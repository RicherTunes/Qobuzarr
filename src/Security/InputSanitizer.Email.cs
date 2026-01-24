using System;
using System.Linq;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    public static partial class InputSanitizer
    {
        /// <summary>
        /// Sanitizes email input for authentication
        /// </summary>
        public static string SanitizeEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be empty");

            email = email.Trim().ToLowerInvariant();

            if (email.Length > MaxEmailLength)
                throw new ArgumentException($"Email exceeds maximum length of {MaxEmailLength} characters");

            if (!EmailRegex().IsMatch(email))
                throw new ArgumentException("Invalid email format");

            // Additional protection against special characters that could be used in injection
            if (email.Contains("'") || email.Contains("\"") || email.Contains(";") ||
                email.Contains("--") || email.Contains("/*") || email.Contains("*/"))
            {
                throw new ArgumentException("Email contains invalid characters");
            }

            return email;
        }

        /// <summary>
        /// Sanitizes password input (validates but returns original for hashing)
        /// </summary>
        public static string ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty");

            if (password.Length > MaxPasswordLength)
                throw new ArgumentException($"Password exceeds maximum length of {MaxPasswordLength} characters");

            // Check for null bytes or control characters
            if (password.Any(c => char.IsControl(c) && c != '\t' && c != '\r' && c != '\n'))
                throw new ArgumentException("Password contains invalid control characters");

            return password;
        }
    }
}

