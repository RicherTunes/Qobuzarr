using System;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    internal static class DownloadResponseDiagnostics
    {
        internal static string TryGetHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "unknown";
            }

            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "unknown";
            }
        }

        internal static bool IsTextLikeContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("html", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool LooksLikeTextPayload(byte[] buffer, int length)
        {
            var max = Math.Min(length, 32);
            var i = 0;
            while (i < max)
            {
                var b = buffer[i];
                if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
                {
                    break;
                }

                i++;
            }

            if (i >= max)
            {
                return false;
            }

            var first = buffer[i];
            return first == (byte)'<' || first == (byte)'{' || first == (byte)'[';
        }

        internal static bool ShouldRedactSnippet(string snippet)
        {
            if (string.IsNullOrWhiteSpace(snippet))
            {
                return false;
            }

            return snippet.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("user_auth_token", StringComparison.OrdinalIgnoreCase) ||
                   snippet.Contains("app_secret", StringComparison.OrdinalIgnoreCase);
        }
    }
}
