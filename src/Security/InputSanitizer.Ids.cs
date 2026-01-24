using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LPCFileNameSanitizer = Lidarr.Plugin.Common.Utilities.FileNameSanitizer;
using LPCSanitize = Lidarr.Plugin.Common.Security.Sanitize;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    public static partial class InputSanitizer
    {
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
        /// Sanitizes file names to be safe for file system operations.
        /// Handles empty results, Windows reserved names, multi-dot extensions, and length limits.
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "unknown_file";

            // Delegate to the shared library's sanitizer for consistent cross-platform rules
            var sanitized = LPCFileNameSanitizer.SanitizeFileName(fileName);

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
    }
}
