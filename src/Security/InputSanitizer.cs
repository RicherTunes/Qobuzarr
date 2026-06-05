using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using LimitConstants = Lidarr.Plugin.Qobuzarr.Constants.QobuzarrConstants;
using LPCSanitize = Lidarr.Plugin.Common.Security.Sanitize;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Comprehensive input sanitization for all user inputs in the Qobuzarr plugin.
    /// Provides methods to sanitize different types of inputs to prevent injection attacks.
    /// </summary>
    public static partial class InputSanitizer
    {
        // Generated regex patterns for validation (SYSLIB1045)
        [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9]+$")]
        private static partial Regex AlphanumericRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9\s\-_\.\,\'()\[\]&!]+$")]
        private static partial Regex SafeQueryRegex();

        [GeneratedRegex(@"^[A-Z]{2}$")]
        private static partial Regex CountryCodeRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,50}$")]
        private static partial Regex AppIdRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$")]
        private static partial Regex AuthTokenRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
        private static partial Regex UserIdRegex();

        [GeneratedRegex(@"[\x00-\x1F\x7F]")]
        private static partial Regex ControlCharsRegex();

        [GeneratedRegex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ScriptTagRegex();

        [GeneratedRegex(@"<[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex HtmlTagRegex();

        [GeneratedRegex(@"javascript:\s*", RegexOptions.IgnoreCase)]
        private static partial Regex JavascriptProtocolRegex();

        [GeneratedRegex(@"\bon(?:click|error|mouseover)\b", RegexOptions.IgnoreCase)]
        private static partial Regex DomEventHandlerRegex();

        [GeneratedRegex(@"\.+")]
        private static partial Regex MultipleDotsRegex();

        [GeneratedRegex(@"%2e%2e%2f", RegexOptions.IgnoreCase)]
        private static partial Regex EncodedTraversalSlashRegex();

        [GeneratedRegex(@"%2e%2e%5c", RegexOptions.IgnoreCase)]
        private static partial Regex EncodedTraversalBackslashRegex();

        [GeneratedRegex(@"\b(drop|delete|union|select|exec|xp_cmdshell)\b", RegexOptions.IgnoreCase)]
        private static partial Regex SqlKeywordsRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultipleSpacesRegex();

        [GeneratedRegex(@"[/\\]+")]
        private static partial Regex MultipleSlashesRegex();

        [GeneratedRegex(@"[\x00-\x1F\x7F\u200B\u200C\u200D\uFEFF]")]
        private static partial Regex ControlAndZeroWidthRegex();

        [GeneratedRegex(@"[\r\n\t]+")]
        private static partial Regex WhitespaceCharsRegex();

        // Windows reserved file names (case-insensitive)
        private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Common multi-part extensions to preserve during truncation
        private static readonly HashSet<string> MultiPartExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst"
        };

        // Maximum lengths for various input types (centralized)
        private const int MaxEmailLength = LimitConstants.Limits.MaxEmailLength;
        private const int MaxPasswordLength = LimitConstants.Limits.MaxPasswordLength;
        private const int MaxQueryLength = LimitConstants.Limits.MaxQueryLength;
        private const int MaxPathLength = LimitConstants.Limits.MaxPathLength;
        private const int MaxUrlLength = LimitConstants.Limits.MaxUrlLength;
        private const int MaxTokenLength = LimitConstants.Limits.MaxTokenLength;

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
        /// Sanitizes file paths to prevent path traversal attacks
        /// </summary>
        public static string SanitizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty");

            // Check for path traversal attempts before sanitizing. Use Common's canonical, segment-aware
            // guard (matches ../ ..\ /.. \.. exact-".." and URL-encoded %2e%2e — and does NOT false-positive
            // on a literal ".." inside a filename like "Vol..2") instead of a bare Contains("..").
            if (Lidarr.Plugin.Common.HostBridge.PathTraversalGuard.ContainsTraversalAttempt(path) || path.Contains("~"))
                throw new ArgumentException("Path contains potential traversal patterns");

            // Remove multiple slashes
            path = MultipleSlashesRegex().Replace(path, System.IO.Path.DirectorySeparatorChar.ToString());

            // Remove any null bytes
            path = path.Replace("\0", "");

            if (path.Length > MaxPathLength)
                throw new ArgumentException($"Path exceeds maximum length of {MaxPathLength} characters");

            // Ensure the path doesn't contain any executable extensions
            var dangerousExtensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".sh", ".vbs", ".com" };
            foreach (var ext in dangerousExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Path contains dangerous extension: {ext}");
            }

            return path;
        }

        /// <summary>
        /// Sanitizes URL parameters
        /// </summary>
        public static string SanitizeUrlParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
                return string.Empty;

            // Delegate to shared library's RFC 3986 URL component encoder
            var encoded = LPCSanitize.UrlComponent(parameter);

            if (encoded.Length > MaxUrlLength)
                throw new ArgumentException($"URL parameter exceeds maximum length of {MaxUrlLength} characters");

            return encoded;
        }

        /// <summary>
        /// Sanitizes App ID for API calls
        /// </summary>
        public static string SanitizeAppId(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("App ID cannot be empty");

            appId = appId.Trim();

            if (!AppIdRegex().IsMatch(appId))
                throw new ArgumentException("App ID contains invalid characters");

            return appId;
        }

        /// <summary>
        /// Sanitizes App Secret (validates format only)
        /// </summary>
        public static string ValidateAppSecret(string appSecret)
        {
            if (string.IsNullOrWhiteSpace(appSecret))
                throw new ArgumentException("App Secret cannot be empty");

            appSecret = appSecret.Trim();

            // App secrets are typically hex strings or base64
            if (appSecret.Length > 100)
                throw new ArgumentException("App Secret exceeds maximum length");

            // Check for obviously malicious patterns
            if (appSecret.Contains("'") || appSecret.Contains("\"") || appSecret.Contains(";"))
                throw new ArgumentException("App Secret contains invalid characters");

            return appSecret;
        }

        /// <summary>
        /// Sanitizes authentication tokens
        /// </summary>
        public static string SanitizeAuthToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Auth token cannot be empty");

            token = token.Trim();

            if (token.Length > MaxTokenLength)
                throw new ArgumentException($"Auth token exceeds maximum length of {MaxTokenLength} characters");

            // Tokens should typically be alphanumeric with some special chars
            if (!AuthTokenRegex().IsMatch(token))
                throw new ArgumentException("Auth token contains invalid characters");

            return token;
        }

        /// <summary>
        /// Sanitizes user ID
        /// </summary>
        public static string SanitizeUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be empty");

            userId = userId.Trim();

            // User IDs are typically numeric or alphanumeric
            if (!UserIdRegex().IsMatch(userId))
                throw new ArgumentException("User ID contains invalid characters");

            if (userId.Length > 50)
                throw new ArgumentException("User ID exceeds maximum length");

            return userId;
        }

        /// <summary>
        /// Sanitizes country code
        /// </summary>
        public static string SanitizeCountryCode(string countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                return "US"; // Default to US

            countryCode = countryCode.Trim().ToUpperInvariant();

            if (!CountryCodeRegex().IsMatch(countryCode))
                throw new ArgumentException("Invalid country code format. Must be 2-letter ISO code.");

            return countryCode;
        }

        /// <summary>
        /// Sanitizes query parameters for API requests
        /// </summary>
        public static string SanitizeQueryParam(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Query parameter key cannot be empty");

            // Sanitize based on the parameter type
            switch (key.ToLowerInvariant())
            {
                case "email":
                    return SanitizeEmail(value);
                case "password":
                    return value; // Don't modify password, just validate
                case "app_id":
                    return SanitizeAppId(value);
                case "app_secret":
                    return value; // Don't modify secret, just validate
                case "user_auth_token":
                case "auth_token":
                    return SanitizeAuthToken(value);
                case "user_id":
                    return SanitizeUserId(value);
                case "query":
                case "q":
                    return SanitizeSearchQuery(value);
                case "country_code":
                case "country":
                    return SanitizeCountryCode(value);
                default:
                    // Generic sanitization for unknown parameters
                    return SanitizeUrlParameter(value);
            }
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
            var sqlPatterns = new[] {
                "select ", "insert ", "update ", "delete ", "drop ",
                "create ", "alter ", "exec ", "execute ", "union ",
                "' or ", "\" or ", "1=1", "1 = 1", "'; --", "\"; --"
            };

            // Check for script injection patterns
            var scriptPatterns = new[] {
                "<script", "javascript:", "vbscript:", "onload=", "onerror=",
                "onclick=", "onmouseover=", "<iframe", "<object", "<embed"
            };

            // Check for command injection patterns
            var cmdPatterns = new[] {
                "&&", "||", "|", ";", "`", "$(", "${", "\n", "\r\n",
                "cmd.exe", "powershell", "/bin/sh", "/bin/bash"
            };

            return sqlPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   scriptPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   cmdPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        #region Consolidated Sanitization Methods

        /// <summary>
        /// Sanitizes file names to be safe for file system operations.
        /// Handles empty results, Windows reserved names, multi-dot extensions, and length limits.
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "unknown_file";

            // Delegate to Common's Sanitize.FileNameSegment (the security-correct successor
            // to the legacy FileNameSanitizer.SanitizeFileName — both preserve the
            // "unknown_file" fallback / replace-with-space semantics, but FileNameSegment
            // also fixes the dot-corruption bug present in older FileNameSanitizer versions).
            var sanitized = LPCSanitize.FileNameSegment(fileName, "unknown_file");

            if (string.IsNullOrWhiteSpace(sanitized))
                return "unknown_file";

            // Windows does not allow trailing dots/spaces in file names; trim for cross-platform safety.
            sanitized = sanitized.TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(sanitized))
                return "unknown_file";

            // Handle Windows reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
            var extension = GetFullExtension(sanitized);
            var nameWithoutExt = extension.Length > 0
                ? sanitized.Substring(0, sanitized.Length - extension.Length)
                : sanitized;

            // Prefix Windows reserved device names on all platforms for portability.
            // This ensures files created on Linux/macOS remain valid when moved to Windows.
            if (WindowsReservedNames.Contains(nameWithoutExt))
            {
                sanitized = "_" + sanitized;
                nameWithoutExt = "_" + nameWithoutExt;
            }

            // Enforce 255-char limit with extension preservation
            const int MaxFileNameLength = 255;
            if (sanitized.Length > MaxFileNameLength)
            {
                var maxNameLength = MaxFileNameLength - extension.Length;
                if (maxNameLength > 0)
                {
                    sanitized = nameWithoutExt.Substring(0, Math.Min(nameWithoutExt.Length, maxNameLength)) + extension;
                }
                else
                {
                    // Extension alone is too long, truncate the whole thing
                    sanitized = sanitized.Substring(0, MaxFileNameLength);
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Gets the full extension including multi-part extensions like .tar.gz
        /// </summary>
        private static string GetFullExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            // Check for known multi-part extensions first
            foreach (var multiExt in MultiPartExtensions)
            {
                if (fileName.EndsWith(multiExt, StringComparison.OrdinalIgnoreCase))
                {
                    return fileName.Substring(fileName.Length - multiExt.Length);
                }
            }

            // Fall back to standard extension
            return Path.GetExtension(fileName);
        }

        private static string NormalizeSafe(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                return value.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                var cleaned = RemoveInvalidSurrogates(value);
                try
                {
                    return cleaned.Normalize(NormalizationForm.FormC);
                }
                catch
                {
                    return cleaned;
                }
            }
        }

        private static string RemoveInvalidSurrogates(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsHighSurrogate(current))
                {
                    if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(current);
                        builder.Append(value[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (char.IsLowSurrogate(current))
                {
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
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
            var dangerous = new char[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
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
        /// Checks if a URL is safe for use
        /// Consolidated from LidarrInputValidator
        /// </summary>
        public static bool IsUrlSafe(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Check for dangerous protocols
            var dangerousProtocols = new[] { "javascript:", "data:", "file:", "ftp:" };
            var lowerUrl = url.ToLowerInvariant();

            if (dangerousProtocols.Any(proto => lowerUrl.StartsWith(proto)))
                return false;

            // Must be HTTP or HTTPS
            return lowerUrl.StartsWith("http://") || lowerUrl.StartsWith("https://");
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
            var sqlPatterns = new[] {
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
            var scriptPatterns = new[] {
                "<script", "javascript:", "vbscript:", "onload=", "onerror=", "onclick=",
                "onmouseover=", "</script>", "alert(", "eval(", "document."
            };

            // Command injection patterns
            var cmdPatterns = new[] {
                "&&", "||", ";", "`", "$(", "${", "rm -rf", "del /", "format ",
                "shutdown", "reboot", "/bin/", "cmd.exe", "powershell"
            };

            // Path traversal patterns
            var pathPatterns = new[] {
                "../", "..\\", "%2e%2e%2f", "%2e%2e%5c", "....//", "....//"
            };

            // LDAP injection patterns
            var ldapPatterns = new[] {
                ")(", ")(&", ")(|", "*)(", "admin)", "(cn=", "(uid="
            };

            return sqlPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   formatPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   scriptPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   cmdPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   pathPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   ldapPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        #endregion
    }
}
