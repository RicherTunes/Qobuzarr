using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Centralized, reusable title normalization used by matching strategies.
    /// Keeps behavior stable and avoids ad-hoc implementations.
    /// </summary>
    public static partial class TitleNormalizer
    {
        [GeneratedRegex(@"[^a-z0-9\s]", RegexOptions.IgnoreCase)]
        private static partial Regex NonWord();

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultiSpace();

        public static string Normalize(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var s = title.ToLowerInvariant();
            s = NonWord().Replace(s, " ");
            s = MultiSpace().Replace(s, " ").Trim();
            return s;
        }

        /// <summary>
        /// Normalizes an album edition/version string for use in GUID generation.
        /// Returns empty string for null/blank input (backward-compatible: no edition = no suffix).
        /// Otherwise lowercases, trims, and replaces spaces with dashes.
        /// </summary>
        /// <example>
        /// NormalizeEditionForGuid("Deluxe Edition") => "deluxe-edition"
        /// NormalizeEditionForGuid(null) => ""
        /// NormalizeEditionForGuid("  ") => ""
        /// </example>
        internal static string NormalizeEditionForGuid(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return string.Empty;
            return version.Trim().ToLowerInvariant().Replace(" ", "-");
        }
    }
}
