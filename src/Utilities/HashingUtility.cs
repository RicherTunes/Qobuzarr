using System;
using System.Security.Cryptography;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Utility class for common hashing operations used by the Qobuz plugin.
    /// Centralizes hashing logic to avoid duplication and ensure consistency.
    /// </summary>
    public static class HashingUtility
    {
        /// <summary>
        /// Computes the MD5 hash of the input string.
        /// Note: MD5 is used here because it's required by the Qobuz API, not for security purposes.
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>The MD5 hash as a lowercase hexadecimal string</returns>
        /// <exception cref="ArgumentNullException">Thrown when input is null</exception>
        public static string ComputeMD5Hash(string input)
        {
            return Lidarr.Plugin.Common.Utilities.HashingUtility.ComputeMD5Hash(input);
        }

        /// <summary>
        /// Computes the MD5 hash of a password after validation.
        /// Validates the password for security before hashing.
        /// </summary>
        /// <param name="password">The password to hash</param>
        /// <returns>The MD5 hash as a lowercase hexadecimal string</returns>
        /// <exception cref="ArgumentException">Thrown when password is invalid</exception>
        public static string ComputePasswordMD5Hash(string password)
        {
            // Validate password for security
            password = InputSanitizer.ValidatePassword(password);

            return ComputeMD5Hash(password);
        }

        /// <summary>
        /// Generates a cache key by combining multiple components and hashing the result.
        /// Provides a stable, collision-resistant cache key for complex objects.
        /// </summary>
        /// <param name="components">The components to combine into a cache key</param>
        /// <returns>A stable cache key string</returns>
        public static string GenerateCacheKey(params string[] components)
        {
            return Lidarr.Plugin.Common.Utilities.HashingUtility.GenerateCacheKey(components);
        }
    }
}
