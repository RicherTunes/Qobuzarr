using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Qobuzarr.Tests.Utilities
{
    public static class StringSimilarity
    {
        public static double Calculate(string a, string b)
        {
            var na = Normalize(a);
            var nb = Normalize(b);

            if (na.Length == 0 && nb.Length == 0) return 1.0;
            if (na == nb) return 1.0;

            var s1 = Bigrams(na);
            var s2 = Bigrams(nb);

            if (s1.Count == 0 || s2.Count == 0) return na == nb ? 1.0 : 0.0;

            var intersect = s1.Intersect(s2).Count();
            return (2.0 * intersect) / (s1.Count + s2.Count);
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Lowercase
            var s = input.ToLowerInvariant();

            // Replace common joiners
            s = s.Replace("&", " and ");

            // Strip diacritics
            s = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            s = sb.ToString().Normalize(NormalizationForm.FormC);

            // Remove punctuation and whitespace
            var filtered = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch)) filtered.Append(ch);
            }
            return filtered.ToString();
        }

        private static List<string> Bigrams(string s)
        {
            var list = new List<string>(Math.Max(0, s.Length - 1));
            for (int i = 0; i < s.Length - 1; i++)
            {
                list.Add(s.Substring(i, 2));
            }
            return list;
        }
    }
}

