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
        
        // Regex for safe version strings - alphanumeric plus common punctuation
        private static readonly Regex SafeVersionRegex = new(@"^[a-zA-Z0-9\s\-_\.\(\)\[\]&',!]+$", RegexOptions.Compiled);
        
        // Control characters and zero-width characters that should be removed
        private static readonly Regex ControlCharRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F\u200B-\u200D\uFEFF]", RegexOptions.Compiled);
        
        // HTML/XML tags that should be stripped
        private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);
        
        // Dangerous patterns that indicate potential attacks
        private static readonly HashSet<string> DangerousPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            // XSS patterns
            "<script", "</script", "javascript:", "vbscript:", "onload=", "onerror=", "onclick=",
            
            // Path traversal
            "../", "..\\", "~", 
            
            // SQL injection indicators
            "';", "--", "/*", "*/", "xp_", "sp_execute", "exec(", "execute(",
            "union select", "drop table", "insert into", "delete from",
            
            // Command injection
            "&&", "||", "|", ";", "`", "$(", "${",
            
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
            
            // Step 1: Remove control characters and zero-width characters
            var sanitized = ControlCharRegex.Replace(version, "");
            
            // Step 2: Strip HTML/XML tags completely
            sanitized = HtmlTagRegex.Replace(sanitized, "");
            
            // Step 3: Replace dangerous characters for file system safety
            sanitized = sanitized
                .Replace("..", "_")  // Path traversal
                .Replace("~/", "_")  // Home directory access
                .Replace("\\", "_")  // Windows path separator
                .Replace("/", "_")   // Unix path separator
                .Replace(":", "-")   // Windows drive separator
                .Replace("*", "_")   // Wildcard
                .Replace("?", "_")   // Wildcard
                .Replace("\"", "'")  // Quote
                .Replace("<", "(")   // Less than
                .Replace(">", ")")   // Greater than
                .Replace("|", "_")   // Pipe
                .Replace("\r", " ")  // Carriage return
                .Replace("\n", " ")  // Newline
                .Replace("\t", " "); // Tab
            
            // Step 4: Collapse multiple spaces
            sanitized = Regex.Replace(sanitized, @"\s+", " ");
            
            // Step 5: Trim and enforce length limit
            sanitized = sanitized.Trim();
            if (sanitized.Length > MaxVersionLength)
            {
                sanitized = sanitized.Substring(0, MaxVersionLength).TrimEnd();
            }
            
            // Step 6: Check for dangerous patterns
            foreach (var pattern in DangerousPatterns)
            {
                if (sanitized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn("Potentially malicious version string detected: Pattern '{0}' found in '{1}'", 
                        pattern, original);
                    
                    // Return a safe default rather than the suspicious content
                    return "Version";
                }
            }
            
            // Step 7: Final validation - ensure result is safe
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
            // Remove control characters
            var sanitized = ControlCharRegex.Replace(input, "");
            
            // Strip HTML tags
            sanitized = HtmlTagRegex.Replace(sanitized, "");
            
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
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            
            // Length limit
            if (sanitized.Length > 200)
            {
                sanitized = sanitized.Substring(0, 200).TrimEnd();
            }
            
            return string.IsNullOrWhiteSpace(sanitized) ? defaultValue : sanitized;
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
            
            var lowerInput = input.ToLowerInvariant();
            
            return DangerousPatterns.Any(pattern => 
                lowerInput.Contains(pattern.ToLowerInvariant()));
        }
    }
}