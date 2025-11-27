using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Centralized, reusable title normalization used by matching strategies.
    /// Keeps behavior stable and avoids ad-hoc implementations.
    /// </summary>
    public static class TitleNormalizer
    {
        private static readonly Regex NonWord = new Regex(@"[^a-z0-9\s]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MultiSpace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string Normalize(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var s = title.ToLowerInvariant();
            s = NonWord.Replace(s, " ");
            s = MultiSpace.Replace(s, " ").Trim();
            return s;
        }
    }
}
