using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Download;

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
            Action<string>? onRangeReset, CancellationToken cancellationToken, RemoteMediaUriPolicy? policy = null)
        {
            // R2-02: validate the stream URL against the SSRF policy and keep it in force across redirects.
            // Qobuz file URLs are https CDN endpoints, so the Strict default (https-only, public, ResolveDns=true)
            // applies in production; tests inject a policy carrying a deterministic DnsResolver. The Range header
            // is preserved across hops by MediaRedirectSafeSender, and the 416-recovery below is unaffected (a
            // 416 is a non-redirect response, returned as-is for the caller to handle).
            policy ??= RemoteMediaUriPolicy.Strict;

            // Validate the initial URL BEFORE any request is issued — MediaRedirectSafeSender keeps the policy in
            // force across redirect hops, but the first hop must be refused up-front so a private/internal target
            // is never contacted at all.
            var guard = RemoteMediaUriGuard.Validate(url, policy);
            if (!guard.IsAllowed)
            {
                throw new InvalidOperationException($"Refusing to download from an unsafe URL: {guard.Reason}");
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            var response = await MediaRedirectSafeSender.SendValidatedAsync(
                httpClient, request, policy, HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationToken).ConfigureAwait(false);

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
                response = await MediaRedirectSafeSender.SendValidatedAsync(
                    httpClient, retry, policy, HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();
            return (response, existing);
        }
    }
}
