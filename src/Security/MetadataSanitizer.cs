using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Provides comprehensive sanitization for metadata fields from external APIs
    /// to prevent injection attacks and ensure safe usage across all contexts.
    /// </summary>
    public static class MetadataSanitizer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Timeout for regex operations to prevent ReDoS attacks
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        // Regex for safe version strings - alphanumeric plus common punctuation
        private static readonly Regex SafeVersionRegex = new(@"^[a-zA-Z0-9\s\-_\.\(\)\[\]&',!]+$", RegexOptions.Compiled, RegexTimeout);

        // Control characters and zero-width characters that should be removed
        private static readonly Regex ControlCharRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F\u200B-\u200D\uFEFF]", RegexOptions.Compiled, RegexTimeout);

        // HTML/XML tags that should be stripped
        private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled, RegexTimeout);

        // Script tags with their content should be completely removed
        private static readonly Regex ScriptTagRegex = new(@"<script[^>]*>.*?</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // Dangerous patterns that indicate potential attacks and should result in a safe default.
        // Note: Path traversal is handled via normalization/replacement, not rejection, to avoid false positives.
        private static readonly HashSet<string> DangerousPatternsReturnSafeDefault = new(StringComparer.OrdinalIgnoreCase)
        {
            // XSS patterns
            "<script", "</script", "javascript:", "vbscript:", "onload=", "onerror=", "onclick=",

            // SQL injection indicators
            "';", "--", "/*", "*/", "xp_", "sp_execute", "exec(", "execute(",
            "union select", "drop table", "insert into", "delete from",   

            // Command injection
            "&&", "||", "|", "`", "$(", "${",

            // LDAP injection
            ")(", "(&", "(|",

            // XML injection - use more specific patterns
            "<!ENTITY", "<!DOCTYPE", "SYSTEM\"", "SYSTEM '"
        };

        // Maximum allowed length for version strings
        private const int MaxVersionLength = 100;

        /// <summary>
        /// Sanitizes an album version string for safe usage in all contexts.
        /// Removes dangerous characters and patterns while preserving legitimate version indicators.
        /// </summary>
        /// <param name="version">The version string to sanitize</param>
        /// <returns>Sanitized version string safe for use in titles, logs, and file paths</returns>
        public static string SanitizeVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            var original = version;
            string sanitized;

            try
            {
                // Step 1: Remove control characters and zero-width characters
                sanitized = ControlCharRegex.Replace(version, "");

                // Step 2: Remove script tags with their content entirely (XSS prevention)
                sanitized = ScriptTagRegex.Replace(sanitized, "");

                // Step 3: Reject clearly malicious inputs before normalization alters detection (e.g., ':' -> '-')
                if (IsPotentiallyDangerousForVersion(sanitized))
                {
                    Logger.Warn("Potentially malicious version string detected before normalization: '{0}'", original);
                    return "Version";
                }
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while sanitizing version string, returning safe default");
                return "Version";
            }

            // Step 4: Replace dangerous characters for file system safety
            sanitized = sanitized
                .Replace("..", "_")  // Path traversal
                .Replace("~/", "_")  // Home directory access
                .Replace("\\", "_")  // Windows path separator
                .Replace("/", "_")   // Unix path separator
                .Replace(":", "-")   // Windows drive separator
                .Replace("*", "_")   // Wildcard
                .Replace("?", "_")   // Wildcard
                .Replace("&", "_")   // Ampersand
                .Replace("\"", "'")  // Quote
                .Replace("<", "(")   // Less than
                .Replace(">", ")")   // Greater than
                .Replace("|", "_")   // Pipe
                .Replace("\r", " ")  // Carriage return
                .Replace("\n", " ")  // Newline
                .Replace("\t", " "); // Tab

            // Step 5: Collapse multiple spaces
            try
            {
                // Avoid runaway underscore growth from path traversal cleanup: "____" -> "___"
                sanitized = Regex.Replace(sanitized, @"_{3,}", "___", RegexOptions.None, RegexTimeout);
                sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.None, RegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while normalizing whitespace in version string, returning safe default");
                return "Version";
            }

            // Step 6: Trim and enforce length limit
            sanitized = sanitized.Trim();
            if (sanitized.Length > MaxVersionLength)
            {
                sanitized = sanitized.Substring(0, MaxVersionLength).TrimEnd();
            }

            // Step 7: Re-check after normalization for patterns that may survive replacement.
            // Keep this list focused to avoid false positives (path traversal is handled above).
            if (IsPotentiallyDangerousForVersion(sanitized))
            {
                Logger.Warn("Potentially malicious version string detected after normalization: '{0}'", original);
                return "Version";
            }

            // Step 8: Final validation - ensure result is safe
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return string.Empty;
            }

            // Log if significant changes were made (potential attack)
            if (!string.Equals(original, sanitized, StringComparison.Ordinal))
            {
                Logger.Debug("Version string sanitized from '{0}' to '{1}'", original, sanitized);
            }

            return sanitized;
        }

        /// <summary>
        /// Sanitizes an artist name for safe usage.
        /// </summary>
        /// <param name="artistName">The artist name to sanitize</param>
        /// <returns>Sanitized artist name</returns>
        public static string SanitizeArtistName(string? artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return "Unknown Artist";

            // Apply same sanitization as version but with different default
            var sanitized = SanitizeMetadataField(artistName, "Unknown Artist");
            return sanitized;
        }

        /// <summary>
        /// Sanitizes an album title for safe usage.
        /// </summary>
        /// <param name="albumTitle">The album title to sanitize</param>
        /// <returns>Sanitized album title</returns>
        public static string SanitizeAlbumTitle(string? albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return "Unknown Album";

            // Apply same sanitization as version but with different default
            var sanitized = SanitizeMetadataField(albumTitle, "Unknown Album");
            return sanitized;
        }

        /// <summary>
        /// Generic metadata field sanitization.
        /// </summary>
        private static string SanitizeMetadataField(string input, string defaultValue)
        {
            string sanitized;

            try
            {
                // Remove control characters
                sanitized = ControlCharRegex.Replace(input, "");

                // Remove script tags with their content entirely (XSS prevention)
                sanitized = ScriptTagRegex.Replace(sanitized, "");

                // Strip HTML tags
                sanitized = HtmlTagRegex.Replace(sanitized, "");
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while sanitizing metadata field, returning safe default");
                return defaultValue;
            }

            // Basic character replacement for file system safety
            sanitized = sanitized
                .Replace("..", "_")
                .Replace("\\", "_")
                .Replace("/", "_")
                .Replace(":", "-")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "'")
                .Replace("<", "(")
                .Replace(">", ")")
                .Replace("|", "_");

            // Normalize whitespace
            try
            {
                sanitized = Regex.Replace(sanitized, @"_{3,}", "___", RegexOptions.None, RegexTimeout);
                sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while normalizing whitespace in metadata field, returning safe default");
                return defaultValue;
            }

            // Length limit
            if (sanitized.Length > 200)
            {
                sanitized = sanitized.Substring(0, 200).TrimEnd();
            }

            return string.IsNullOrWhiteSpace(sanitized) ? defaultValue : sanitized;
        }

        private static bool IsPotentiallyDangerousForVersion(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            foreach (var pattern in DangerousPatternsReturnSafeDefault)
            {
                if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Escapes a string for safe inclusion in HTML content.
        /// </summary>
        public static string HtmlEncode(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        /// <summary>
        /// Validates if a metadata field contains potentially dangerous content.
        /// </summary>
        public static bool IsPotentiallyDangerous(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (IsPotentiallyDangerousForVersion(input))
            {
                return true;
            }

            // Path traversal indicators are "dangerous" but typically sanitized rather than rejected.
            return input.Contains("../", StringComparison.OrdinalIgnoreCase) ||
                   input.Contains("..\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
