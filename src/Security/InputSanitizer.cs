using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LimitConstants = Lidarr.Plugin.Qobuzarr.Constants.QobuzarrConstants;

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
    }
}

