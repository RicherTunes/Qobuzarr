using System;
using System.Text.RegularExpressions;

namespace Qobuzarr.Tests.Utilities
{
    public static class PreviewDetectionUtility
    {
        private static readonly Regex PreviewUrlPattern = new(
            @"(preview|sample|clip|teaser|snippet|demo|30s|30sec|30second)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsPreviewOrSampleUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (PreviewUrlPattern.IsMatch(url)) return true;
            if (url.Contains("preview=true", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.Contains("sample=1", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsPreviewDuration(int durationSeconds)
        {
            return durationSeconds == 30 || durationSeconds == 60 || durationSeconds == 90;
        }

        public static bool IsLikelyPreview(string url, int? durationSeconds, string restrictionMessage)
        {
            if (IsPreviewOrSampleUrl(url)) return true;
            if (durationSeconds.HasValue && IsPreviewDuration(durationSeconds.Value)) return true;
            if (!string.IsNullOrWhiteSpace(restrictionMessage) && ContainsPreviewMessage(restrictionMessage)) return true;
            return false;
        }

        public static string GetPreviewMessage(string trackTitle)
        {
            return $"'{trackTitle}' appears to be a preview/sample. Full version requires a purchase or subscription.";
        }

            private static bool ContainsPreviewMessage(string message)
            {
                if (string.IsNullOrWhiteSpace(message)) return false;
                var m = message.ToLowerInvariant();
                return m.Contains("preview") || m.Contains("sample") || m.Contains("excerpt") || m.Contains("short clip");
            }
    }
}

