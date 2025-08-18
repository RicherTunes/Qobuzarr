using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Advanced batch download service with adaptive concurrency and intelligent scheduling
    /// </summary>
    public class AdaptiveBatchDownloadService
    {
        private readonly QobuzTrackDownloader _trackDownloader;
        private readonly AdaptiveConcurrencyManager _concurrencyManager;
        private readonly IQobuzLogger _logger;
        
        // Download queue and management
        private readonly ConcurrentQueue<DownloadRequest> _downloadQueue = new();
        private readonly ConcurrentDictionary<string, DownloadProgress> _activeDownloads = new();
        private readonly ConcurrentDictionary<string, AdaptiveDownloadResult> _completedDownloads = new();
        
        // Statistics
        private volatile int _totalRequests = 0;
        private volatile int _completedRequests = 0;
        private volatile int _failedRequests = 0;
        private long _totalBytesDownloaded = 0;

        public int QueuedItems => _downloadQueue.Count;
        public int ActiveDownloads => _activeDownloads.Count;
        public int CompletedDownloads => _completedRequests;
        public int FailedDownloads => _failedRequests;
        public double CompletionPercentage => _totalRequests > 0 ? (_completedRequests * 100.0) / _totalRequests : 0;

        public AdaptiveBatchDownloadService(
            QobuzTrackDownloader trackDownloader,
            IQobuzLogger logger,
            AdaptiveConcurrencyManager concurrencyManager = null)
        {
            _trackDownloader = trackDownloader ?? throw new ArgumentNullException(nameof(trackDownloader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _concurrencyManager = concurrencyManager ?? new AdaptiveConcurrencyManager(logger);
        }

        /// <summary>
        /// Adds multiple download requests to the queue
        /// </summary>
        public void EnqueueDownloads(IEnumerable<DownloadRequest> requests)
        {
            foreach (var request in requests)
            {
                _downloadQueue.Enqueue(request);
                Interlocked.Increment(ref _totalRequests);
            }
            
            _logger.Info("Enqueued {0} download requests. Total queued: {1}", 
                requests.Count(), _downloadQueue.Count);
        }

        /// <summary>
        /// Processes the download queue with adaptive concurrency
        /// </summary>
        public async Task<BatchDownloadResult> ProcessQueueAsync(
            IProgress<BatchDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var semaphore = _concurrencyManager.GetConcurrencySemaphore();
            var downloadTasks = new List<Task>();
            
            _logger.Info("Starting batch download processing. Queue size: {0}, Initial concurrency: {1}", 
                _downloadQueue.Count, _concurrencyManager.CurrentConcurrency);

            try
            {
                // Process all items in the queue
                while (!_downloadQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    // Adjust semaphore if concurrency changed
                    var currentConcurrency = _concurrencyManager.CurrentConcurrency;
                    if (semaphore.CurrentCount != currentConcurrency)
                    {
                        semaphore.Dispose();
                        semaphore = _concurrencyManager.GetConcurrencySemaphore();
                        _logger.Debug("Updated semaphore for new concurrency level: {0}", currentConcurrency);
                    }

                    // Try to dequeue and process
                    if (_downloadQueue.TryDequeue(out var request))
                    {
                        var downloadTask = ProcessSingleDownloadAsync(request, semaphore, cancellationToken);
                        downloadTasks.Add(downloadTask);
                        
                        // Report progress
                        progress?.Report(new BatchDownloadProgress
                        {
                            TotalItems = _totalRequests,
                            CompletedItems = _completedRequests,
                            FailedItems = _failedRequests,
                            ActiveDownloads = _activeDownloads.Count,
                            QueuedItems = _downloadQueue.Count,
                            CurrentConcurrency = currentConcurrency,
                            AverageLatency = _concurrencyManager.AverageLatency,
                            SuccessRate = _concurrencyManager.SuccessRate,
                            TotalBytesDownloaded = _totalBytesDownloaded
                        });
                    }
                    
                    // Clean up completed tasks to prevent memory issues
                    downloadTasks.RemoveAll(t => t.IsCompleted);
                    
                    // Brief pause to prevent tight loop
                    await Task.Delay(10, cancellationToken);
                }
                
                // Wait for all remaining downloads to complete
                _logger.Info("Waiting for {0} remaining downloads to complete...", downloadTasks.Count);
                await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("Batch download processing was cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during batch download processing");
            }
            finally
            {
                semaphore.Dispose();
            }

            var duration = DateTime.UtcNow - startTime;
            var result = CreateBatchResult(startTime, duration);
            
            _logger.Info("Batch download completed. Duration: {0:F1}s, Success rate: {1:P1}, Final concurrency: {2}",
                duration.TotalSeconds, result.SuccessRate, _concurrencyManager.CurrentConcurrency);
                
            return result;
        }

        /// <summary>
        /// Processes a single download with concurrency control and error handling
        /// </summary>
        private async Task ProcessSingleDownloadAsync(
            DownloadRequest request,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            var progress = new DownloadProgress
            {
                RequestId = request.Id,
                TrackId = request.Track.Id,
                Status = DownloadStatus.Starting,
                StartTime = DateTime.UtcNow
            };

            _activeDownloads.TryAdd(request.Id, progress);

            try
            {
                var result = await _concurrencyManager.ExecuteWithConcurrencyAsync(async () =>
                {
                    progress.Status = DownloadStatus.Downloading;
                    
                    var trackProgress = new Progress<double>(p => 
                    {
                        progress.ProgressPercentage = p;
                        progress.LastUpdate = DateTime.UtcNow;
                    });

                    // Create a dummy album for the download (required parameter)
                    var album = new QobuzAlbum 
                    { 
                        Id = request.Track.Album?.Id ?? "unknown",
                        Title = request.Track.Album?.Title ?? "Unknown Album",
                        Artist = request.Track.Album?.Artist ?? new QobuzArtist { Name = request.Track.GetPerformerName() }
                    };
                    
                    return await _trackDownloader.DownloadTrackAsync(
                        request.Track,
                        album,
                        request.OutputPath,
                        request.QualityId,
                        trackProgress,
                        cancellationToken);
                }, semaphore, cancellationToken);

                // Success
                progress.Status = DownloadStatus.Completed;
                progress.CompletedTime = DateTime.UtcNow;
                progress.FilePath = result;
                
                var downloadResult = new AdaptiveDownloadResult
                {
                    RequestId = request.Id,
                    Success = true,
                    FilePath = result,
                    Duration = progress.CompletedTime.Value - progress.StartTime
                };
                
                _completedDownloads.TryAdd(request.Id, downloadResult);
                Interlocked.Increment(ref _completedRequests);
                
                // Estimate file size (rough approximation)
                var estimatedSize = EstimateFileSize(request.Track, request.QualityId);
                Interlocked.Add(ref _totalBytesDownloaded, estimatedSize);
            }
            catch (Exception ex)
            {
                // Failure
                progress.Status = DownloadStatus.Failed;
                progress.Error = ex.Message;
                progress.CompletedTime = DateTime.UtcNow;
                
                var downloadResult = new AdaptiveDownloadResult
                {
                    RequestId = request.Id,
                    Success = false,
                    Error = ex.Message,
                    Duration = progress.CompletedTime.Value - progress.StartTime
                };
                
                _completedDownloads.TryAdd(request.Id, downloadResult);
                Interlocked.Increment(ref _failedRequests);
                
                _logger.Debug("Download failed for track {0}: {1}", request.Track.Id, ex.Message);
            }
            finally
            {
                _activeDownloads.TryRemove(request.Id, out _);
            }
        }

        /// <summary>
        /// Gets current download statistics
        /// </summary>
        public BatchDownloadStats GetStats()
        {
            return new BatchDownloadStats
            {
                TotalRequests = _totalRequests,
                CompletedRequests = _completedRequests,
                FailedRequests = _failedRequests,
                QueuedItems = _downloadQueue.Count,
                ActiveDownloads = _activeDownloads.Count,
                CompletionPercentage = CompletionPercentage,
                TotalBytesDownloaded = _totalBytesDownloaded,
                ConcurrencyStats = _concurrencyManager.GetStats()
            };
        }

        private BatchDownloadResult CreateBatchResult(DateTime startTime, TimeSpan duration)
        {
            var successCount = _completedRequests;
            var failureCount = _failedRequests;
            var totalCount = successCount + failureCount;
            
            return new BatchDownloadResult
            {
                TotalRequests = _totalRequests,
                SuccessfulDownloads = successCount,
                FailedDownloads = failureCount,
                SuccessRate = totalCount > 0 ? (double)successCount / totalCount : 0,
                TotalDuration = duration,
                AverageLatency = _concurrencyManager.AverageLatency,
                FinalConcurrency = _concurrencyManager.CurrentConcurrency,
                TotalBytesDownloaded = _totalBytesDownloaded,
                StartTime = startTime,
                EndTime = startTime + duration
            };
        }

        private long EstimateFileSize(QobuzTrack track, int qualityId)
        {
            // Rough estimation based on duration and quality
            var durationMinutes = track.Duration.TotalMinutes;
            var baseSizeMB = durationMinutes * (qualityId switch
            {
                5 => 1.2,   // MP3 320kbps
                6 => 5.0,   // FLAC CD
                7 => 8.0,   // FLAC Hi-Res 96kHz
                27 => 12.0, // FLAC Hi-Res 192kHz
                _ => 5.0
            });
            
            return (long)(baseSizeMB * 1024 * 1024); // Convert to bytes
        }
    }

    public class DownloadRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public QobuzTrack Track { get; set; }
        public string OutputPath { get; set; }
        public int QualityId { get; set; } = 6; // FLAC CD default
        public int Priority { get; set; } = 0;
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    }

    public class DownloadProgress
    {
        public string RequestId { get; set; }
        public string TrackId { get; set; }
        public DownloadStatus Status { get; set; }
        public double ProgressPercentage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public DateTime LastUpdate { get; set; }
        public string FilePath { get; set; }
        public string Error { get; set; }
    }

    public class AdaptiveDownloadResult
    {
        public string RequestId { get; set; }
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string Error { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class BatchDownloadProgress
    {
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public int FailedItems { get; set; }
        public int ActiveDownloads { get; set; }
        public int QueuedItems { get; set; }
        public int CurrentConcurrency { get; set; }
        public double AverageLatency { get; set; }
        public double SuccessRate { get; set; }
        public long TotalBytesDownloaded { get; set; }
    }

    public class BatchDownloadResult
    {
        public int TotalRequests { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public double AverageLatency { get; set; }
        public int FinalConcurrency { get; set; }
        public long TotalBytesDownloaded { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class BatchDownloadStats
    {
        public int TotalRequests { get; set; }
        public int CompletedRequests { get; set; }
        public int FailedRequests { get; set; }
        public int QueuedItems { get; set; }
        public int ActiveDownloads { get; set; }
        public double CompletionPercentage { get; set; }
        public long TotalBytesDownloaded { get; set; }
        public ConcurrencyStats ConcurrencyStats { get; set; }
    }

    public enum DownloadStatus
    {
        Queued,
        Starting,
        Downloading,
        Completed,
        Failed
    }
}