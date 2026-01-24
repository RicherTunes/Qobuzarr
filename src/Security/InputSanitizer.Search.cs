using System;
using System.Linq;
using LPCSanitize = Lidarr.Plugin.Common.Security.Sanitize;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    public static partial class InputSanitizer
    {
        /// <summary>
        /// Sanitizes search query parameters for API usage.
        /// Note: Do not HTML-encode here; rely on URL encoding at build time.
        /// </summary>
        public static string SanitizeSearchQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var trimmed = query.Trim();

            // Enforce max length ASAP to limit allocations on pathological inputs
            if (trimmed.Length > MaxQueryLength)
                trimmed = trimmed.Substring(0, MaxQueryLength);

            // Normalize control characters and whitespace
            trimmed = ControlCharsRegex().Replace(trimmed, " ");
            trimmed = trimmed.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            // Strip inline scripts and HTML/JS protocol patterns (XSS) as early as possible
            trimmed = ScriptTagRegex().Replace(trimmed, " ");
            trimmed = HtmlTagRegex().Replace(trimmed, " ");
            trimmed = JavascriptProtocolRegex().Replace(trimmed, " ");
            // Strip common DOM event handlers like onclick, onerror, onmouseover
            trimmed = DomEventHandlerRegex().Replace(trimmed, " ");
            // Extra guard: simple case-insensitive removals for stubborn tokens
            trimmed = trimmed.Replace("onclick", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("onmouseover", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("onerror", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("javascript:", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("alert(", " ", StringComparison.OrdinalIgnoreCase);

            // Break common path separators to prevent traversal-like tokens in output
            trimmed = trimmed.Replace('/', ' ').Replace('\\', ' ');

            // Remove obvious path traversal encodings and sequences
            trimmed = MultipleDotsRegex().Replace(trimmed, " "); // collapse sequences of dots
            trimmed = EncodedTraversalSlashRegex().Replace(trimmed, " ");
            trimmed = EncodedTraversalBackslashRegex().Replace(trimmed, " ");

            // Remove command chaining/operators frequently used in injection
            var operators = new[] { "&&", "||", "|", ";", "`", "$(", "${" };
            foreach (var op in operators)
            {
                trimmed = trimmed.Replace(op, " ");
            }

            // Remove explicitly dangerous command tokens seen in tests and common exploits
            string[] dangerousTokens = new[]
            {
                "rm -rf", "del /f", "powershell", "wget", "curl", "nc -l"
            };
            foreach (var token in dangerousTokens)
            {
                trimmed = trimmed.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
            }

            // Remove common SQL injection keywords to avoid propagating them in queries
            trimmed = SqlKeywordsRegex().Replace(trimmed, " ");

            // (already handled above) XSS/script patterns

            // Collapse multiple spaces
            trimmed = MultipleSpacesRegex().Replace(trimmed, " ").Trim();

            return trimmed;
        }

        /// <summary>
        /// Validates if a string contains any potentially dangerous content
        /// </summary>
        public static bool ContainsDangerousContent(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();

            // Check for SQL injection patterns
            var sqlPatterns = new[]
            {
                "select ", "insert ", "update ", "delete ", "drop ",
                "create ", "alter ", "exec ", "execute ", "union ",
                "' or ", "\" or ", "1=1", "1 = 1", "'; --", "\"; --"
            };

            // Check for script injection patterns
            var scriptPatterns = new[]
            {
                "<script", "javascript:", "vbscript:", "onload=", "onerror=",
                "onclick=", "onmouseover=", "<iframe", "<object", "<embed"
            };

            // Check for command injection patterns
            var cmdPatterns = new[]
            {
                "&&", "||", "|", ";", "`", "$(", "${", "\n", "\r\n",
                "cmd.exe", "powershell", "/bin/sh", "/bin/bash"
            };

            return sqlPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   scriptPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   cmdPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// Sanitizes artist names for metadata
        /// Consolidated from MetadataSanitizer
        /// </summary>
        public static string SanitizeArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return "Unknown Artist";

            var sanitized = artistName.Trim();

            // Check for dangerous content first
            if (IsPotentiallyDangerous(sanitized))
                return "Unknown Artist";

            // Remove HTML tags
            sanitized = HtmlTagRegex().Replace(sanitized, "");

            // Remove dangerous characters
            sanitized = sanitized.Replace("'", "'").Replace("\"", "'");
            sanitized = ControlCharsRegex().Replace(sanitized, "");

            // Unicode normalization
            sanitized = NormalizeSafe(sanitized);

            // Collapse multiple spaces
            sanitized = MultipleSpacesRegex().Replace(sanitized, " ").Trim();

            // Length limit
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100).Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown Artist" : sanitized;
        }

        /// <summary>
        /// Sanitizes album titles for metadata
        /// Consolidated from MetadataSanitizer
        /// </summary>
        public static string SanitizeAlbumTitle(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return "Unknown Album";

            var sanitized = albumTitle.Trim();

            // Check for dangerous content first
            if (IsPotentiallyDangerous(sanitized))
                return "Unknown Album";

            // Remove path traversal attempts
            sanitized = sanitized.Replace("../", "___").Replace("..\\", "___");

            // Remove HTML tags and dangerous content
            sanitized = HtmlTagRegex().Replace(sanitized, "");

            // Replace dangerous file system characters
            var dangerous = new[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
            foreach (var c in dangerous)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Remove control characters
            sanitized = ControlCharsRegex().Replace(sanitized, "");

            // Unicode normalization
            sanitized = NormalizeSafe(sanitized);

            // Collapse spaces and trim
            sanitized = MultipleSpacesRegex().Replace(sanitized, " ").Trim();

            // Length limit
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100).Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown Album" : sanitized;
        }

        /// <summary>
        /// Sanitizes version strings for metadata
        /// Consolidated from MetadataSanitizer
        /// </summary>
        public static string SanitizeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "";

            var sanitized = version.Trim();

            // Check for dangerous content first
            if (IsPotentiallyDangerous(sanitized))
                return "Version"; // Safe default for dangerous input

            // Remove script tags
            sanitized = ScriptTagRegex().Replace(sanitized, "");

            // Remove other HTML tags
            sanitized = HtmlTagRegex().Replace(sanitized, "");

            // Replace dangerous file system characters
            sanitized = sanitized.Replace(":", "-").Replace("/", "_").Replace("\\", "_")
                                .Replace("*", "_").Replace("?", "_").Replace("\"", "'")
                                .Replace("<", "(").Replace(">", ")").Replace("|", "_");

            // Remove control characters and zero-width characters
            sanitized = ControlAndZeroWidthRegex().Replace(sanitized, "");

            // Normalize whitespace
            sanitized = WhitespaceCharsRegex().Replace(sanitized, " ");
            sanitized = MultipleSpacesRegex().Replace(sanitized, " ").Trim();

            // Length limit
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100).Trim();

            return sanitized;
        }

        /// <summary>
        /// Encodes text for safe display by neutralizing both HTML and template-injection vectors.
        /// </summary>
        /// <remarks>
        /// <para><b>SECURITY-CRITICAL:</b> This method performs two distinct security transforms:</para>
        /// <list type="number">
        ///   <item>HTML entity encoding (via LPCSanitize.DisplayText) to prevent XSS</item>
        ///   <item>Template-injection token breaking (e.g., <c>${...}</c> → <c>$&amp;#123;...</c>)
        ///         to prevent Log4Shell-style JNDI injection and similar expression-language attacks</item>
        /// </list>
        /// <para>Do NOT simplify this method to pure HTML encoding without understanding the
        /// template-injection implications. The chaos/fuzz tests in InputSanitizerTests validate
        /// that dangerous tokens cannot survive this transform intact.</para>
        /// </remarks>
        /// <param name="text">Raw text that may contain malicious content</param>
        /// <returns>Safe text suitable for display in HTML or log contexts</returns>
        public static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Lidarr.Plugin.Common encodes HTML-significant characters; we additionally
            // break common template-injection sequences (e.g., Log4Shell-style `${...}`)
            // so they can't survive as a contiguous token in downstream renderers.
            var encoded = LPCSanitize.DisplayText(text);
            encoded = encoded.Replace("${", "$&#123;", StringComparison.Ordinal);
            return encoded;
        }

        /// <summary>
        /// Checks if input is potentially dangerous (enhanced version)
        /// Consolidated and enhanced from MetadataSanitizer
        /// </summary>
        public static bool IsPotentiallyDangerous(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();

            // SQL injection patterns
            var sqlPatterns = new[]
            {
                "';", "\";", "drop table", "insert into", "delete from", "update ", "union select",
                "exec ", "execute ", "xp_", "sp_", "-- ", "/*", "*/",
                "waitfor delay", "sleep(", "benchmark(", "or 1=1", "or 1 = 1"
            };

            // Format string / template injection patterns
            var formatPatterns = new[]
            {
                "%n", "%s", "%p", "%x", "%d", "%u",
                "{0}", "{1}", "{2}",
                "${jndi:", "${"
            };

            // Script injection patterns
            var scriptPatterns = new[]
            {
                "<script", "javascript:", "vbscript:", "onload=", "onerror=", "onclick=",
                "onmouseover=", "</script>", "alert(", "eval(", "document."
            };

            // Command injection patterns
            var cmdPatterns = new[]
            {
                "&&", "||", ";", "`", "$(", "${", "rm -rf", "del /", "format ",
                "shutdown", "reboot", "/bin/", "cmd.exe", "powershell"
            };

            // Path traversal patterns
            var pathPatterns = new[]
            {
                "../", "..\\", "%2e%2e%2f", "%2e%2e%5c", "....//", "....//"
            };

            // LDAP injection patterns
            var ldapPatterns = new[]
            {
                ")(", ")(&", ")(|", "*)(", "admin)", "(cn=", "(uid="
            };

            return sqlPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   formatPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   scriptPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   cmdPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   pathPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   ldapPatterns.Any(pattern => lowerInput.Contains(pattern));
        }
    }
}

