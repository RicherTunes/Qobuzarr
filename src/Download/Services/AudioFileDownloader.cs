using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Services.Http;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles downloading audio files from streaming URLs with progress reporting and retry logic
    /// </summary>
    public class AudioFileDownloader : IAudioFileDownloader
    {
        private readonly IHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;
        private readonly SharedSystemHttpClient _sharedHttp;

        private const int MaxRetries = QobuzPluginConstants.Download.MaxRetries;
        private const int RetryDelayMs = QobuzPluginConstants.Download.RetryDelayMs;

        /// <summary>
        /// Hard cap on per-attempt backoff delay. Without this, attempt 6+ at base 1s
        /// reaches 32s+ which is too long to keep a user waiting before failing fast.
        /// 30 seconds was the previous in-line magic number — extracted so it's
        /// discoverable and tunable in one place rather than buried in a Math.Min.
        /// </summary>
        private const int MaxRetryBackoffMs = 30_000;
        private const int LargeFileThresholdBytes = QobuzPluginConstants.Download.LargeFileThresholdBytes;
        private const int BufferSize = QobuzPluginConstants.Download.BufferSize;
        private const int ChunkSize = QobuzPluginConstants.Download.ChunkSize;

        public AudioFileDownloader(IHttpClient httpClient, IQobuzLogger logger, IQualityFallbackProvider qualityFallbackProvider, SharedSystemHttpClient sharedHttp)
        {
            _httpClient = Guard.NotNull(httpClient, nameof(httpClient));
            _logger = Guard.NotNull(logger, nameof(logger));
            _qualityFallbackProvider = Guard.NotNull(qualityFallbackProvider, nameof(qualityFallbackProvider));
            _sharedHttp = Guard.NotNull(sharedHttp, nameof(sharedHttp));
        }

        public async Task DownloadAudioFileAsync(
            string streamUrl,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            Guard.NotNullOrWhiteSpace(streamUrl, nameof(streamUrl));
            Guard.NotNullOrWhiteSpace(outputPath, nameof(outputPath));

            var attempt = 0;
            Exception? lastException = null;

            while (attempt < MaxRetries)
            {
                attempt++;
                try
                {
                    // Ensure output directory exists
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    if (attempt > 1)
                    {
                        _logger.Debug("Download retry attempt {0}/{1} for: {2}", attempt, MaxRetries, streamUrl);
                        // Exponential backoff for retries
                        var delay = Math.Min(RetryDelayMs * (int)Math.Pow(2, attempt - 1), MaxRetryBackoffMs);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    // Resumable streaming via the shared rate-limited HttpClient.
                    // Egress is gated by QobuzRateLimitingHandler inside _sharedHttp,
                    // so 429s + Retry-After are honoured alongside the API path.
                    var http = _sharedHttp.HttpClient;
                    var partialPath = outputPath + ".partial";
                    long existing = 0;
                    if (File.Exists(partialPath))
                    {
                        try { existing = new FileInfo(partialPath).Length; } catch { existing = 0; }
                    }

                    var httpRequest = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                    if (existing > 0)
                    {
                        httpRequest.Headers.Range = new RangeHeaderValue(existing, null);
                    }

                    using var httpResponse = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    httpResponse.EnsureSuccessStatusCode();

                    var isPartial = httpResponse.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    if (!isPartial && File.Exists(partialPath))
                    {
                        try { File.Delete(partialPath); } catch (Exception ex) { _logger.Debug("Best-effort partial file cleanup failed for {Path}: {Error}", partialPath, ex.Message); }
                        existing = 0;
                    }

                    var contentRange = httpResponse.Content.Headers.ContentRange;
                    long expectedTotal = 0;
                    if (contentRange != null && contentRange.HasLength)
                    {
                        expectedTotal = contentRange.Length!.Value;
                    }

                    await using (var network = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var fs = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: BufferSize, useAsync: true))
                    {
                        var buffer = new byte[BufferSize];
                        long written = existing;
                        int read;
                        while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            written += read;
                            if (expectedTotal > 0)
                            {
                                var pct = (double)written / expectedTotal * 100d;
                                progress?.Report(Math.Min(100, pct));
                            }
                        }
                        fs.Flush(true);
                    }

                    // Atomic move
                    File.Move(partialPath, outputPath, overwrite: true);

                    // Validate
                    if (!ValidateDownloadedFile(outputPath))
                    {
                        throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(outputPath)}");
                    }

                    // Log final size for visibility
                    var finalSize = 0L;
                    try { finalSize = new FileInfo(outputPath).Length; } catch (Exception ex) { _logger.Debug("Could not read file size for {Path}: {Error}", outputPath, ex.Message); }
                    _logger.Debug("Successfully downloaded {0} bytes to: {1} (attempt {2})", finalSize, outputPath, attempt);
                    return; // Success - exit retry loop
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    await CleanupPartialFileAsync(outputPath + ".partial");

                    // Determine if this is a retryable error
                    var isRetryable = IsRetryableNetworkError(ex) || _qualityFallbackProvider.IsRetryableException(ex);

                    if (attempt < MaxRetries && isRetryable)
                    {
                        var errorType = GetErrorType(ex);
                        _logger.Warn("Download failed due to {0} (attempt {1}/{2}), will retry: {3}",
                            errorType, attempt, MaxRetries, ex.Message);

                        // For network interruptions, log more details
                        if (ex.Message.Contains("response ended prematurely") ||
                            ex.Message.Contains("copying content to a stream"))
                        {
                            _logger.Debug("Network interruption detected - this is common with large files or unstable connections");
                        }
                    }
                    else
                    {
                        _logger.Error(ex, "Failed to download audio file from: {0} (final attempt {1}/{2})", streamUrl, attempt, MaxRetries);
                        throw;
                    }
                }
            }

            // If we get here, all retries failed
            _logger.Error(lastException, "Failed to download audio file after {0} attempts: {1}", MaxRetries, streamUrl);
            throw lastException ?? new InvalidOperationException("Download failed after all retries");
        }

        public bool ValidateDownloadedFile(string filePath)
        {
            Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));

            try
            {
                // Use centralized validation for basic file checks
                if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(filePath);

                // Basic file validation - try to read the first few bytes
                using var fileStream = File.OpenRead(filePath);
                var buffer = new byte[1024];
                var bytesRead = fileStream.Read(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    _logger.Debug("Could not read any bytes from downloaded file: {0}", filePath);
                    return false;
                }

                // For audio files, check for common magic bytes
                if (IsValidAudioFile(buffer, Path.GetExtension(filePath)))
                {
                    _logger.Debug("Downloaded file validation successful: {0} ({1} bytes)", filePath, fileInfo.Length);
                    return true;
                }

                _logger.Debug("Downloaded file does not appear to be a valid audio file: {0}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating downloaded file: {0}", filePath);
                return false;
            }
        }

        private async Task<long> StreamLargeFileAsync(
            byte[] responseData,
            FileStream fileStream,
            long totalBytes,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            // Write in chunks to avoid memory issues and provide better progress
            var buffer = new byte[ChunkSize];
            var dataStream = new MemoryStream(responseData);
            var downloadedBytes = 0L;

            int bytesRead;
            while ((bytesRead = await dataStream.ReadAsync(buffer, 0, ChunkSize, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                downloadedBytes += bytesRead;

                // Report progress more frequently for streaming
                if (totalBytes > 0)
                {
                    var progressPercentage = (double)downloadedBytes / totalBytes * 100;
                    progress?.Report(Math.Min(100, progressPercentage));
                }
            }

            return downloadedBytes;
        }

        private static void ReportFinalProgress(IProgress<double> progress, long downloadedBytes, long totalBytes)
        {
            if (totalBytes > 0)
            {
                var progressPercentage = (double)downloadedBytes / totalBytes * 100;
                progress?.Report(Math.Min(100, progressPercentage));
            }
            else
            {
                progress?.Report(100); // Complete if total bytes unknown
            }
        }

        private async Task CleanupPartialFileAsync(string outputPath)
        {
            // Clean up partial file on error
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (Exception deleteEx)
            {
                _logger.Warn(deleteEx, "Failed to clean up partial file: {0}", outputPath);
            }
        }

        private static bool IsValidAudioFile(byte[] buffer, string extension)
        {
            if (buffer.Length < 4) return false;

            // M4A is not currently covered by common's AudioMagicBytesValidator (which checks
            // the first 4 bytes); preserve the legacy ftyp-after-size check for that extension.
            // For all other audio extensions, defer to the canonical validator from common.
            if (string.Equals(extension, ".m4a", StringComparison.OrdinalIgnoreCase))
            {
                return buffer.Skip(4).Take(4).SequenceEqual(new byte[] { 0x66, 0x74, 0x79, 0x70 }); // "ftyp" after size
            }

            return AudioMagicBytesValidator.IsValidAudioMagicBytes(buffer.AsSpan());
        }

        private static bool IsRetryableNetworkError(Exception ex)
        {
            // Check for specific network-related errors that are worth retrying
            if (ex is IOException ioEx)
            {
                // "The response ended prematurely" is a common network interruption
                if (ioEx.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (ex is HttpRequestException httpEx)
            {
                // "Error while copying content to a stream" indicates download interruption
                if (httpEx.Message.Contains("copying content", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (ex.InnerException != null)
            {
                return IsRetryableNetworkError(ex.InnerException);
            }

            // Check for WebException which often indicates network issues
            if (ex.GetType().Name == "WebException")
                return true;

            return false;
        }

        private static string GetErrorType(Exception ex)
        {
            if (ex.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase))
                return "network interruption";
            if (ex.Message.Contains("copying content", StringComparison.OrdinalIgnoreCase))
                return "download interruption";
            if (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                return "timeout";
            if (ex is IOException)
                return "IO error";
            if (ex is HttpRequestException)
                return "HTTP error";

            return "error";
        }
    }
}
