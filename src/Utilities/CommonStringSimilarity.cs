using System;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Lightweight string similarity helper used across matching strategies.
    /// Returns a normalized score in [0,1], where 1.0 is an exact match.
    /// Implementation: case-insensitive, whitespace-normalized Levenshtein ratio.
    /// </summary>
    public static class CommonStringSimilarity
    {
        public static double Calculate(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 1.0;
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0.0;

            var s1 = TitleNormalizer.Normalize(a);
            var s2 = TitleNormalizer.Normalize(b);

            if (s1.Length == 0 && s2.Length == 0) return 1.0;
            if (s1.Equals(s2, StringComparison.Ordinal)) return 1.0;

            int dist = LevenshteinDistance(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            if (maxLen == 0) return 1.0;
            return 1.0 - (double)dist / maxLen;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            var dp = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[n, m];
        }
    }
}

