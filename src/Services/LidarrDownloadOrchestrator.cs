using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for orchestrating the complete download process for Lidarr albums
    /// with comprehensive parallel processing, resource management, and error handling
    /// optimized for the *arr ecosystem.
    /// </summary>
    public class LidarrDownloadOrchestrator : ILidarrDownloadOrchestrator
    {
        private readonly QobuzTrackDownloader _trackDownloader;
        private readonly ILidarrStatisticsCollector _statisticsCollector;
        private readonly Logger _logger;

        // Configuration constants
        private const int DEFAULT_MAX_CONCURRENCY = 0; // Will use Environment.ProcessorCount
        private const int MIN_CONCURRENCY = 1;
        private const int MAX_CONCURRENCY = 20;
        private const int DEFAULT_MAX_RETRIES = 3;
        private const int BASE_RETRY_DELAY_MS = 1000;
        private const double RETRY_BACKOFF_MULTIPLIER = 2.0;

        /// <summary>
        /// Initializes a new instance of the LidarrDownloadOrchestrator with required dependencies.
        /// </summary>
        /// <param name="trackDownloader">Service for downloading individual tracks.</param>
        /// <param name="statisticsCollector">Service for collecting statistics.</param>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        public LidarrDownloadOrchestrator(
            QobuzTrackDownloader trackDownloader,
            ILidarrStatisticsCollector statisticsCollector,
            Logger logger)
        {
            _trackDownloader = Guard.NotNull(trackDownloader, nameof(trackDownloader));
            _statisticsCollector = Guard.NotNull(statisticsCollector, nameof(statisticsCollector));
            _logger = Guard.NotNull(logger, nameof(logger));

            _logger.Info("LidarrDownloadOrchestrator initialized");
        }

        /// <summary>
        /// Orchestrates the complete download process for Lidarr albums with parallel execution.
        /// </summary>
        public async Task<DownloadBatchResult> DownloadLidarrAlbumsAsync(
            IEnumerable<AlbumDownloadItem> downloadItems,
            string outputPath,
            int maxConcurrency = DEFAULT_MAX_CONCURRENCY,
            System.IProgress<DownloadProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            var items = downloadItems?.ToList() ?? throw new ArgumentNullException(nameof(downloadItems));
            
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            }

            if (!items.Any())
            {
                _logger.Info("No download items provided");
                return new DownloadBatchResult { TotalItems = 0 };
            }

            var effectiveConcurrency = GetEffectiveConcurrency(maxConcurrency);
            _logger.Info("Starting batch download of {0} albums with concurrency {1}", items.Count, effectiveConcurrency);

            var result = new DownloadBatchResult
            {
                TotalItems = items.Count
            };

            var stopwatch = Stopwatch.StartNew();
            var completed = 0;
            var resultLock = new object();

            // Create semaphore for this operation
            using var semaphore = new SemaphoreSlim(effectiveConcurrency, effectiveConcurrency);

            // Create tasks for parallel execution
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var downloadResult = await DownloadSingleAlbumAsync(item, outputPath, cancellationToken).ConfigureAwait(false);
                    
                    lock (resultLock)
                    {
                        if (downloadResult.Success)
                        {
                            result.SuccessfulDownloads++;
                            result.SuccessItems.Add(downloadResult.SuccessItem);
                            result.TotalBytesDownloaded += downloadResult.SuccessItem?.BytesDownloaded ?? 0;
                        }
                        else
                        {
                            result.FailedDownloads++;
                            result.FailureItems.Add(downloadResult.FailureItem);
                        }

                        completed++;

                        // Update statistics
                        _statisticsCollector.RecordDownloadAttempt(downloadResult.Success, downloadResult.SuccessItem?.BytesDownloaded ?? 0);
                        if (!downloadResult.Success)
                        {
                            _statisticsCollector.RecordError(downloadResult.FailureItem?.LastException, "Download");
                        }

                        // Report progress
                        if (progress != null)
                        {
                            var elapsed = stopwatch.Elapsed;
                            var estimatedTotal = items.Count > 0 && completed > 0 
                                ? TimeSpan.FromTicks(elapsed.Ticks * items.Count / completed) 
                                : TimeSpan.Zero;
                            var remaining = estimatedTotal - elapsed;

                            progress.Report(new DownloadProgressReport
                            {
                                Completed = completed,
                                Total = items.Count,
                                SuccessCount = result.SuccessfulDownloads,
                                FailureCount = result.FailedDownloads,
                                BytesDownloaded = result.TotalBytesDownloaded,
                                CurrentSpeedMBps = elapsed.TotalSeconds > 0 
                                    ? (result.TotalBytesDownloaded / 1024.0 / 1024.0) / elapsed.TotalSeconds 
                                    : 0,
                                CurrentAlbum = $"{item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}",
                                Phase = "Downloading",
                                Elapsed = elapsed,
                                EstimatedRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error downloading album: {0} - {1}", 
                        item.LidarrAlbum.Artist?.ArtistName, item.LidarrAlbum.Title);
                    
                    lock (resultLock)
                    {
                        result.FailedDownloads++;
                        result.FailureItems.Add(new DownloadFailureItem
                        {
                            OriginalItem = item,
                            LastException = ex,
                            AttemptCount = 1,
                            LastAttemptAt = DateTime.UtcNow,
                            FailureReason = ex.Message
                        });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            // Wait for all downloads to complete
            await Task.WhenAll(tasks).ConfigureAwait(false);

            stopwatch.Stop();
            result.TotalDuration = stopwatch.Elapsed;
            
            // Calculate average download speed
            if (stopwatch.Elapsed.TotalSeconds > 0)
            {
                result.AverageDownloadSpeed = (result.TotalBytesDownloaded / 1024.0 / 1024.0) / stopwatch.Elapsed.TotalSeconds;
            }

            // Update final statistics
            _statisticsCollector.RecordBatchComplete(stopwatch.Elapsed);

            _logger.Info("Batch download complete: {0} successful, {1} failed, {2} skipped in {3:F1}s (avg {4:F2} MB/s)",
                result.SuccessfulDownloads, result.FailedDownloads, result.SkippedDownloads, 
                stopwatch.Elapsed.TotalSeconds, result.AverageDownloadSpeed);

            return result;
        }

        /// <summary>
        /// Retries failed album downloads with exponential backoff.
        /// </summary>
        public async Task<DownloadBatchResult> RetryFailedDownloadsAsync(
            IEnumerable<DownloadFailureItem> failedItems,
            int maxRetries = DEFAULT_MAX_RETRIES,
            string outputPath = null,
            CancellationToken cancellationToken = default)
        {
            var items = failedItems?.Where(item => item.AttemptCount < maxRetries).ToList();
            
            if (items == null || !items.Any())
            {
                _logger.Info("No failed items to retry");
                return new DownloadBatchResult { TotalItems = 0 };
            }

            _logger.Info("Retrying {0} failed downloads (max retries: {1})", items.Count, maxRetries);

            var retryItems = items.Select(failedItem => 
            {
                // Set output path if provided, otherwise use original
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    failedItem.OriginalItem.OutputPath = outputPath;
                }
                return failedItem.OriginalItem;
            });

            // Use exponential backoff delay
            var retryDelay = CalculateRetryDelay(items.Max(i => i.AttemptCount));
            if (retryDelay > TimeSpan.Zero)
            {
                _logger.Info("Waiting {0:F1}s before retry attempt", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            // Retry with reduced concurrency to be more conservative
            var reducedConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
            return await DownloadLidarrAlbumsAsync(retryItems, outputPath, reducedConcurrency, null, cancellationToken).ConfigureAwait(false);
        }

        #region Private Helper Methods

        /// <summary>
        /// Downloads a single album with all its tracks.
        /// </summary>
        private async Task<SingleDownloadResult> DownloadSingleAlbumAsync(AlbumDownloadItem item, string outputPath, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.Info("Downloading album: {0} - {1}", 
                    item.LidarrAlbum.Artist?.ArtistName, item.LidarrAlbum.Title);

                // Create album directory
                var albumPath = CreateAlbumDirectory(item, outputPath);
                
                var tracksDownloaded = 0;
                var totalBytes = 0L;

                // Download all tracks in the album
                var tracks = item.QobuzAlbum.GetTracks();
                if (tracks.Any())
                {
                    foreach (var track in tracks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        try
                        {
                            var trackPath = await _trackDownloader.DownloadTrackAsync(
                                track, item.QobuzAlbum, albumPath, item.PreferredQuality, 
                                null, cancellationToken).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(trackPath) && File.Exists(trackPath))
                            {
                                var fileInfo = new FileInfo(trackPath);
                                totalBytes += fileInfo.Length;
                                tracksDownloaded++;
                                
                                _logger.Debug("Downloaded track: {0}", Path.GetFileName(trackPath));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                            // Continue with other tracks rather than failing entire album
                        }
                    }
                }

                stopwatch.Stop();

                if (tracksDownloaded > 0)
                {
                    return new SingleDownloadResult
                    {
                        Success = true,
                        SuccessItem = new DownloadSuccessItem
                        {
                            DownloadItem = item,
                            DownloadPath = albumPath,
                            DownloadDuration = stopwatch.Elapsed,
                            BytesDownloaded = totalBytes,
                            TracksDownloaded = tracksDownloaded,
                            CompletedAt = DateTime.UtcNow
                        }
                    };
                }
                else
                {
                    throw new InvalidOperationException("No tracks were successfully downloaded");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.Error(ex, "Failed to download album: {0} - {1}", 
                    item.LidarrAlbum.Artist?.ArtistName, item.LidarrAlbum.Title);

                return new SingleDownloadResult
                {
                    Success = false,
                    FailureItem = new DownloadFailureItem
                    {
                        OriginalItem = item,
                        LastException = ex,
                        AttemptCount = 1,
                        LastAttemptAt = DateTime.UtcNow,
                        FailureReason = ex.Message
                    }
                };
            }
        }

        private string CreateAlbumDirectory(AlbumDownloadItem item, string outputPath)
        {
            var artistName = item.LidarrAlbum.Artist?.ArtistName?.ToSafeFileName() ?? "Unknown Artist";
            var albumTitle = item.LidarrAlbum.Title?.ToSafeFileName() ?? "Unknown Album";
            
            var releaseYear = item.LidarrAlbum.ReleaseDate?.Year;
            var yearSuffix = releaseYear.HasValue && releaseYear > 1900 ? $" ({releaseYear})" : "";
            
            var albumDir = $"{albumTitle}{yearSuffix}";
            var fullPath = Path.Combine(outputPath, artistName, albumDir);
            
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private int GetEffectiveConcurrency(int requestedConcurrency)
        {
            if (requestedConcurrency <= 0)
                return Math.Max(MIN_CONCURRENCY, Environment.ProcessorCount);
            
            return Math.Min(MAX_CONCURRENCY, Math.Max(MIN_CONCURRENCY, requestedConcurrency));
        }

        private TimeSpan CalculateRetryDelay(int attemptCount)
        {
            if (attemptCount <= 1)
                return TimeSpan.Zero;
            
            var delay = BASE_RETRY_DELAY_MS * Math.Pow(RETRY_BACKOFF_MULTIPLIER, attemptCount - 1);
            return TimeSpan.FromMilliseconds(Math.Min(delay, 30000)); // Cap at 30 seconds
        }

        #endregion

        #region Helper Classes

        private class SingleDownloadResult
        {
            public bool Success { get; set; }
            public DownloadSuccessItem? SuccessItem { get; set; }
            public DownloadFailureItem? FailureItem { get; set; }
        }

        #endregion
    }
}