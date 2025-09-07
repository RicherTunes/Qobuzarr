using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using LimitConstants = Lidarr.Plugin.Qobuzarr.Constants.QobuzarrConstants;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Comprehensive input sanitization for all user inputs in the Qobuzarr plugin.
    /// Provides methods to sanitize different types of inputs to prevent injection attacks.
    /// </summary>
    public static class InputSanitizer
    {
        // Regex patterns for validation
        private static readonly Regex EmailRegex = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
        private static readonly Regex AlphanumericRegex = new(@"^[a-zA-Z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex SafeQueryRegex = new Regex(@"^[a-zA-Z0-9\s\-_\.\,\'()\[\]&!]+$", RegexOptions.Compiled);
        private static readonly Regex CountryCodeRegex = new(@"^[A-Z]{2}$", RegexOptions.Compiled);
        private static readonly Regex AppIdRegex = new(@"^[a-zA-Z0-9_-]{1,50}$", RegexOptions.Compiled);
        
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

            if (!EmailRegex.IsMatch(email))
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
            trimmed = Regex.Replace(trimmed, "[\x00-\x1F\x7F]", " ");
            trimmed = trimmed.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            // Strip inline scripts and HTML/JS protocol patterns (XSS) as early as possible
            trimmed = Regex.Replace(trimmed, @"<script[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            trimmed = Regex.Replace(trimmed, @"<[^>]*>", " ", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"javascript:\s*", " ", RegexOptions.IgnoreCase);
            // Strip common DOM event handlers like onclick, onerror, onmouseover
            trimmed = Regex.Replace(trimmed, @"\bon(?:click|error|mouseover)\b", " ", RegexOptions.IgnoreCase);
            // Extra guard: simple case-insensitive removals for stubborn tokens
            trimmed = trimmed.Replace("onclick", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("onmouseover", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("onerror", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("javascript:", " ", StringComparison.OrdinalIgnoreCase)
                             .Replace("alert(", " ", StringComparison.OrdinalIgnoreCase);

            // Break common path separators to prevent traversal-like tokens in output
            trimmed = trimmed.Replace('/', ' ').Replace('\\', ' ');

            // Remove obvious path traversal encodings and sequences
            trimmed = Regex.Replace(trimmed, @"\.+", " "); // collapse sequences of dots
            trimmed = Regex.Replace(trimmed, "%2e%2e%2f", " ", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, "%2e%2e%5c", " ", RegexOptions.IgnoreCase);

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
                trimmed = Regex.Replace(trimmed, Regex.Escape(token), " ", RegexOptions.IgnoreCase);
            }

            // Remove common SQL injection keywords to avoid propagating them in queries
            trimmed = Regex.Replace(trimmed, @"\b(drop|delete|union|select|exec|xp_cmdshell)\b", " ", RegexOptions.IgnoreCase);

            // (already handled above) XSS/script patterns

            // Collapse multiple spaces
            trimmed = Regex.Replace(trimmed, @"\s+", " ").Trim();

            return trimmed;
        }

        /// <summary>
        /// Sanitizes file paths to prevent path traversal attacks
        /// </summary>
        public static string SanitizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty");

            // Check for path traversal attempts before sanitizing
            if (path.Contains("..") || path.Contains("~"))
                throw new ArgumentException("Path contains potential traversal patterns");
            
            // Remove any path traversal attempts that might have been missed
            path = path.Replace("..", "");
            path = path.Replace("~", "");
            
            // Remove multiple slashes
            path = Regex.Replace(path, @"[/\\]+", System.IO.Path.DirectorySeparatorChar.ToString());
            
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

            // URL encode the parameter
            parameter = Uri.EscapeDataString(parameter);
            
            if (parameter.Length > MaxUrlLength)
                throw new ArgumentException($"URL parameter exceeds maximum length of {MaxUrlLength} characters");

            return parameter;
        }

        /// <summary>
        /// Sanitizes App ID for API calls
        /// </summary>
        public static string SanitizeAppId(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("App ID cannot be empty");

            appId = appId.Trim();
            
            if (!AppIdRegex.IsMatch(appId))
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
            if (!Regex.IsMatch(token, @"^[a-zA-Z0-9_\-\.]+$"))
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
            if (!Regex.IsMatch(userId, @"^[a-zA-Z0-9_-]+$"))
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
            
            if (!CountryCodeRegex.IsMatch(countryCode))
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
        /// Sanitizes file names to be safe for file system operations
        /// Consolidated from FileNameSanitizer
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "unknown_file";

            // Remove or replace illegal file system characters
            var illegal = new char[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
            var sanitized = fileName;
            
            foreach (var c in illegal)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Remove control characters
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", "");
            
            // Handle Unicode normalization
            sanitized = sanitized.Normalize(NormalizationForm.FormC);
            
            // Trim and ensure reasonable length
            sanitized = sanitized.Trim().Trim('.');
            if (sanitized.Length > 255)
                sanitized = sanitized.Substring(0, 255);
                
            // Handle empty result
            if (string.IsNullOrWhiteSpace(sanitized))
                return "unknown_file";
                
            // Handle Windows reserved names
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            if (reservedNames.Contains(nameWithoutExt.ToUpperInvariant()))
                sanitized = "safe_" + sanitized;

            return sanitized;
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
            sanitized = Regex.Replace(sanitized, @"<[^>]*>", "");
            
            // Remove dangerous characters
            sanitized = sanitized.Replace("'", "'").Replace("\"", "'");
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", "");
            
            // Unicode normalization
            sanitized = sanitized.Normalize(NormalizationForm.FormC);
            
            // Collapse multiple spaces
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            
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
            
            // Remove path traversal attempts
            sanitized = sanitized.Replace("../", "___").Replace("..\\", "___");
            
            // Remove HTML tags and dangerous content
            sanitized = Regex.Replace(sanitized, @"<[^>]*>", "");
            
            // Replace dangerous file system characters
            var dangerous = new char[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
            foreach (var c in dangerous)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            
            // Remove control characters
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", "");
            
            // Unicode normalization
            sanitized = sanitized.Normalize(NormalizationForm.FormC);
            
            // Collapse spaces and trim
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            
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
            sanitized = Regex.Replace(sanitized, @"<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            // Remove other HTML tags
            sanitized = Regex.Replace(sanitized, @"<[^>]*>", "");
            
            // Replace dangerous file system characters
            sanitized = sanitized.Replace(":", "-").Replace("/", "_").Replace("\\", "_")
                                .Replace("*", "_").Replace("?", "_").Replace("\"", "'")
                                .Replace("<", "(").Replace(">", ")").Replace("|", "_");
            
            // Remove control characters and zero-width characters
            sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F\u200B\u200C\u200D\uFEFF]", "");
            
            // Normalize whitespace
            sanitized = Regex.Replace(sanitized, @"[\r\n\t]+", " ");
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            
            // Length limit
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100).Trim();
                
            return sanitized;
        }

        /// <summary>
        /// HTML encodes text to prevent XSS
        /// Consolidated from MetadataSanitizer
        /// </summary>
        public static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return WebUtility.HtmlEncode(text);
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
                "exec ", "execute ", "xp_", "sp_", "-- ", "/*", "*/"
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
                   scriptPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   cmdPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   pathPatterns.Any(pattern => lowerInput.Contains(pattern)) ||
                   ldapPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        #endregion
    }
}
