using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Common.Services.Intelligence;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Qobuz-specific metadata sanitization layer over <see cref="MetadataFieldSanitizer"/>.
    /// Adds the plugin policy of returning the safe default "Version" when an input
    /// matches a dangerous-pattern allow-list (XSS / SQLi / LDAPi / cmd / XML markers).
    /// </summary>
    /// <remarks>
    /// Phase 5d: music-domain text mechanics (control-char stripping, zero-width Unicode,
    /// FS-unsafe substitution, whitespace normalization, length caps) live in common's
    /// <see cref="MetadataFieldSanitizer"/>. The dangerous-pattern allow-list below is
    /// Qobuzarr policy and stays plugin-local.
    /// </remarks>
    public static class MetadataSanitizer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        // Pre-pass to remove script tags before dangerous-pattern detection so that
        // "<script>alert('XSS')</script>Deluxe" yields "Deluxe", not the safe default.
        private static readonly Regex ScriptTagRegex = new(
            @"<script[^>]*>.*?</script>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline,
            RegexTimeout);

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

        /// <summary>
        /// Sanitizes an album version string for safe usage in all contexts.
        /// Returns the safe default "Version" if a dangerous pattern is detected;
        /// otherwise delegates the music-domain pipeline to <see cref="MetadataFieldSanitizer.SanitizeVersion"/>.
        /// </summary>
        public static string SanitizeVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            var original = version;

            try
            {
                // Strip script tags (with content) first so the dangerous-pattern check
                // does not misfire on legitimately-prefixed content that follows.
                var prePass = ScriptTagRegex.Replace(version, "");

                // Reject clearly malicious inputs before common's pipeline normalizes
                // characters (e.g. ':' -> '-') in ways that would defeat detection.
                if (IsPotentiallyDangerousForVersion(prePass))
                {
                    Logger.Warn("Potentially malicious version string detected before normalization: '{0}'", original);
                    return "Version";
                }

                var sanitized = MetadataFieldSanitizer.SanitizeVersion(prePass);

                // Plugin-local FS extra: replace '&' with '_' (common's pipeline preserves '&').
                // Done after delegation because the dangerous-pattern check above already
                // rejected '&&'; a single '&' in legitimate metadata is safe to substitute.
                if (!string.IsNullOrEmpty(sanitized) && sanitized.Contains('&'))
                {
                    sanitized = sanitized.Replace('&', '_');
                }

                // Re-check after normalization for patterns that may survive replacement.
                if (IsPotentiallyDangerousForVersion(sanitized))
                {
                    Logger.Warn("Potentially malicious version string detected after normalization: '{0}'", original);
                    return "Version";
                }

                if (!string.Equals(original, sanitized, StringComparison.Ordinal))
                {
                    Logger.Debug("Version string sanitized from '{0}' to '{1}'", original, sanitized);
                }

                return sanitized;
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while sanitizing version string, returning safe default");
                return "Version";
            }
        }

        /// <summary>Sanitizes an artist name. Falls back to "Unknown Artist" on empty input.</summary>
        public static string SanitizeArtistName(string? artistName)
            => MetadataFieldSanitizer.SanitizeArtistName(artistName);

        /// <summary>Sanitizes an album title. Falls back to "Unknown Album" on empty input.</summary>
        public static string SanitizeAlbumTitle(string? albumTitle)
            => MetadataFieldSanitizer.SanitizeAlbumTitle(albumTitle);

        /// <summary>Escapes a string for safe inclusion in HTML content.</summary>
        public static string HtmlEncode(string? input)
            => MetadataFieldSanitizer.HtmlEncode(input);

        /// <summary>
        /// Validates if a metadata field contains potentially dangerous content.
        /// Combines the Qobuz-local dangerous-pattern allow-list with common's
        /// path-traversal heuristic.
        /// </summary>
        public static bool IsPotentiallyDangerous(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return IsPotentiallyDangerousForVersion(input)
                || MetadataFieldSanitizer.ContainsPathTraversal(input);
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
    }
}
