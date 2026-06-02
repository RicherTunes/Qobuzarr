using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Shared HTTP send-with-resume helper for the qobuz download paths. Centralises the resume
    /// <c>Range</c> request and — critically — the <c>416 Range Not Satisfiable</c> recovery so the logic
    /// lives in exactly one place instead of being copy-pasted across every downloader.
    /// </summary>
    internal static class ResumeHttpDownloader
    {
        /// <summary>
        /// Sends the download GET, using a resume <c>Range</c> header when <paramref name="existing"/> bytes are
        /// already on disk. If the server answers <c>416 Range Not Satisfiable</c> (the <c>.partial</c> is already
        /// complete or larger than the content — e.g. the process was killed in the stream→atomic-move window), the
        /// stale partial is deleted and the GET is retried once WITHOUT the range header, so a complete <c>.partial</c>
        /// can't poison the download forever (it would otherwise 416 on every retry). Returns the success-validated
        /// response (caller disposes it) and the effective resume offset (0 after a 416 reset).
        /// </summary>
        public static async Task<(HttpResponseMessage Response, long Existing)> SendDownloadRequestAsync(
            HttpClient httpClient, string url, string partialPath, long existing,
            Action<string>? onRangeReset, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (existing > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // The .partial is already >= the server's content (e.g. the process was killed after the full
                // stream but before the atomic move). Drop the stale partial and restart from scratch so the
                // track isn't stuck 416-ing on every retry until someone deletes the .partial by hand.
                response.Dispose();
                try { System.IO.File.Delete(partialPath); } catch { /* best-effort: a missing/locked partial is non-fatal */ }
                onRangeReset?.Invoke(partialPath);
                existing = 0;

                var retry = new HttpRequestMessage(HttpMethod.Get, url);
                response = await httpClient.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();
            return (response, existing);
        }
    }
}
