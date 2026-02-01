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
    }
}
