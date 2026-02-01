using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Provides basic input validation for Lidarr integration data to prevent security issues.
    /// Keeps security simple and focused on common attack vectors for enthusiast use.
    /// </summary>
    public static partial class LidarrInputValidator
    {
        [GeneratedRegex(@"^[a-zA-Z0-9\s\-_\.\(\)\[\]&',!]+$")]
        private static partial Regex SafePathRegex();
        private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".pif"
        };

        /// <summary>
        /// Validates and sanitizes an album title for safe file system use
        /// </summary>
        /// <param name="title">Album title from Lidarr</param>
        /// <returns>Sanitized title safe for file paths</returns>
        public static string SanitizeAlbumTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "Unknown Album";

            // Remove or replace dangerous characters for file paths
            var sanitized = title
                .Replace("..", "_")  // Prevent path traversal
                .Replace("\\", "_")  // Prevent path manipulation
                .Replace("/", "_")   // Prevent path manipulation
                .Replace(":", "-")   // Invalid in Windows filenames
                .Replace("*", "_")   // Invalid in Windows filenames
                .Replace("?", "_")   // Invalid in Windows filenames
                .Replace("\"", "'")  // Invalid in Windows filenames
                .Replace("<", "_")   // Invalid in Windows filenames
                .Replace(">", "_")   // Invalid in Windows filenames
                .Replace("|", "_");  // Invalid in Windows filenames

            // Limit length to prevent issues
            if (sanitized.Length > 100)
                sanitized = sanitized[..100].TrimEnd();

            return string.IsNullOrWhiteSpace(sanitized) ? "Sanitized Album" : sanitized;
        }

        /// <summary>
        /// Validates and sanitizes an artist name for safe file system use
        /// </summary>
        /// <param name="artistName">Artist name from Lidarr</param>
        /// <returns>Sanitized artist name safe for file paths</returns>
        public static string SanitizeArtistName(string? artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return "Unknown Artist";

            // Same sanitization as album title
            return SanitizeAlbumTitle(artistName);
        }

        /// <summary>
        /// Validates that a string doesn't contain obvious dangerous content
        /// </summary>
        /// <param name="input">Input string to validate</param>
        /// <returns>True if the input appears safe</returns>
        public static bool IsInputSafe(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return true;

            // Check for basic script injection attempts
            var lowerInput = input.ToLowerInvariant();

            // Check for script tags or obvious injection attempts
            if (lowerInput.Contains("<script") ||
                lowerInput.Contains("javascript:") ||
                lowerInput.Contains("vbscript:") ||
                lowerInput.Contains("onload=") ||
                lowerInput.Contains("onerror="))
            {
                return false;
            }

            // Check for path traversal attempts
            if (input.Contains("../") || input.Contains("..\\"))
            {
                return false;
            }

            // Check for dangerous file extensions
            return !DangerousExtensions.Any(ext => input.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates that a URL is properly formatted and uses safe protocols
        /// </summary>
        /// <param name="url">URL to validate</param>
        /// <returns>True if the URL appears safe to use</returns>
        public static bool IsUrlSafe(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                var uri = new Uri(url);

                // Only allow HTTP/HTTPS
                if (uri.Scheme != "http" && uri.Scheme != "https")
                    return false;

                // Basic checks for malformed URLs
                return !string.IsNullOrWhiteSpace(uri.Host);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates an API key format (basic checks)
        /// </summary>
        /// <param name="apiKey">API key to validate</param>
        /// <returns>True if the API key format looks valid</returns>
        public static bool IsApiKeyFormatValid(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;

            // Basic checks - Lidarr API keys are typically alphanumeric
            return apiKey.Length >= 20 &&
                   apiKey.Length <= 100 &&
                   apiKey.All(c => char.IsLetterOrDigit(c));
        }

        /// <summary>
        /// Sanitizes a file name to be safe for the file system
        /// </summary>
        /// <param name="fileName">Original file name</param>
        /// <returns>Sanitized file name</returns>
        public static string SanitizeFileName(string fileName)
        {
            return Lidarr.Plugin.Common.Utilities.FileNameSanitizer.SanitizeFileName(fileName);
        }

        /// <summary>
        /// Validates response size to prevent memory exhaustion
        /// </summary>
        /// <param name="contentLength">Content length from HTTP response</param>
        /// <returns>True if the response size is acceptable</returns>
        public static bool IsResponseSizeAcceptable(long? contentLength)
        {
            const long MaxResponseSize = 50 * 1024 * 1024; // 50MB limit

            return !contentLength.HasValue || contentLength.Value <= MaxResponseSize;
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings for fuzzy matching.
        /// Used for finding similar album/artist names in search results.
        /// </summary>
        /// <param name="s1">First string</param>
        /// <param name="s2">Second string</param>
        /// <returns>The minimum number of character edits required to transform s1 into s2</returns>
        public static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var matrix = new int[s1.Length + 1, s2.Length + 1];

            // Initialize first row and column
            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            // Fill the matrix
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[s1.Length, s2.Length];
        }
    }
}
