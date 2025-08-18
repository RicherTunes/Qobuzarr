using System;
using System.Security.Cryptography;
using System.Text;

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
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            using (var md5 = MD5.Create())
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = md5.ComputeHash(inputBytes);
                
                // Convert to lowercase hex string - optimized for performance
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Generates a cache key by combining multiple components and hashing the result.
        /// Provides a stable, collision-resistant cache key for complex objects.
        /// </summary>
        /// <param name="components">The components to combine into a cache key</param>
        /// <returns>A stable cache key string</returns>
        public static string GenerateCacheKey(params string[] components)
        {
            if (components == null || components.Length == 0)
            {
                throw new ArgumentException("At least one component is required", nameof(components));
            }

            var combined = string.Join("|", components);
            return ComputeMD5Hash(combined);
        }
    }
}