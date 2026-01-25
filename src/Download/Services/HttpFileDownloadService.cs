using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for downloading files from HTTP URLs with resume support and validation.
    /// Extracted from QobuzDownloadClient to reduce god-class complexity.
    /// </summary>
    public class HttpFileDownloadService : IHttpFileDownloadService
    {
        private readonly Logger _logger;

        public HttpFileDownloadService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken)
        {
            // Stream to a temporary .partial file, then atomic move to final
            var httpClient = SharedSystemHttpClient.Instance;
            var partialPath = filePath + ".partial";
            long existing = 0;
            if (File.Exists(partialPath))
            {
                try { existing = new FileInfo(partialPath).Length; } catch { existing = 0; }
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            var contentLength = response.Content.Headers.ContentLength;
            var urlHost = DownloadResponseDiagnostics.TryGetHost(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || contentLength == 0)
            {
                throw new InvalidOperationException($"Download returned no content (HTTP {(int)response.StatusCode} {response.StatusCode}, Host={urlHost}, Content-Type={contentType}).");
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!isPartial && File.Exists(partialPath))
            {
                // Server didn't honor range; start fresh
                try { File.Delete(partialPath); } catch { }
                existing = 0;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[131072];
            long totalWritten = existing;
            int read;

            // Explicit scope ensures fileStream is closed before File.Move
            await using (var fileStream = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, 131072, useAsync: true))
            {
                read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new InvalidOperationException($"Downloaded stream contained no data (Host={urlHost}, Content-Type={contentType}, Content-Length={contentLength?.ToString() ?? "unknown"}).");
                }

                if (DownloadResponseDiagnostics.IsTextLikeContentType(contentType) || DownloadResponseDiagnostics.LooksLikeTextPayload(buffer, read))
                {
                    var snippet = System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Min(read, 512))
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();
                    var safeSnippet = DownloadResponseDiagnostics.GetSafeSnippetForLogging(snippet);
                    throw new InvalidOperationException($"Unexpected content type '{contentType}' when downloading audio (Host={urlHost}). Snippet: {safeSnippet}");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalWritten += read;

                while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalWritten += read;
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, filePath, overwrite: true);

            AudioMagicBytesValidator.ValidateAudioMagicBytes(filePath);

            // Validate file (basic; no size/hash guarantees from server)
            if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }
            return totalWritten;
        }
    }
}
