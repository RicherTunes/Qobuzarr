using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Quality
{
    /// <summary>
    /// Handles quality availability detection for tracks and albums.
    /// Single responsibility: Detect which quality formats are available for given tracks.
    /// </summary>
    public interface IQualityDetector
    {
        /// <summary>
        /// Detects available qualities for a single track.
        /// </summary>
        Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects available qualities for multiple tracks in batch.
        /// </summary>
        Task<BatchQualityDetectionResult> DetectBatchQualitiesAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Intelligently detects album-level quality using representative sampling.
        /// </summary>
        Task<AlbumQualityResult> DetectAlbumQualityAsync(QobuzAlbum album, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of quality detector with intelligent sampling and caching strategies.
    /// </summary>
    public class QualityDetector : IQualityDetector
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly IQobuzLogger _logger;

        // Detection constants
        private const int QUALITY_CHECK_TIMEOUT_MS = 10000;
        private const int SAMPLE_TRACK_COUNT = 3;
        private const double CONSISTENCY_THRESHOLD = 0.8;
        private const int MAX_CONCURRENT_CHECKS = 5;

        public QualityDetector(
            IQobuzApiClient apiClient,
            IQualityDefinitionService qualityDefinitionService,
            IQobuzLogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _qualityDefinitionService = qualityDefinitionService ?? throw new ArgumentNullException(nameof(qualityDefinitionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            _logger.Debug("Detecting available qualities for track {0}", trackId);
            
            var result = new QualityDetectionResult
            {
                TrackId = trackId,
                AvailableQualities = new List<QualityFormat>(),
                CheckedAt = DateTime.UtcNow
            };

            var supportedQualities = _qualityDefinitionService.GetSupportedQualities();
            
            foreach (var quality in supportedQualities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var isAvailable = await CheckQualityAvailabilityAsync(trackId, quality, cancellationToken);
                    
                    if (isAvailable)
                    {
                        result.AvailableQualities.Add(quality);
                        _logger.Debug("Quality {0} available for track {1}", quality.Name, trackId);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} not available for track {1}: {2}", quality.Name, trackId, ex.Message);
                }
            }

            result.HighestAvailableQuality = result.AvailableQualities.FirstOrDefault();
            result.Success = result.AvailableQualities.Any();
            
            _logger.Info("Track {0} quality detection: {1} formats available, highest: {2}", 
                trackId, result.AvailableQualities.Count, result.HighestAvailableQuality?.Name ?? "None");
            
            return result;
        }

        public async Task<BatchQualityDetectionResult> DetectBatchQualitiesAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default)
        {
            var trackIdList = trackIds?.ToList() ?? throw new ArgumentNullException(nameof(trackIds));
            
            if (!trackIdList.Any())
            {
                throw new ArgumentException("Track IDs cannot be empty", nameof(trackIds));
            }

            _logger.Info("Starting batch quality detection for {0} tracks", trackIdList.Count);
            
            var result = new BatchQualityDetectionResult
            {
                TrackResults = new Dictionary<string, QualityDetectionResult>(),
                StartedAt = DateTime.UtcNow
            };

            // Process tracks in parallel with concurrency limit
            var semaphore = new SemaphoreSlim(MAX_CONCURRENT_CHECKS, MAX_CONCURRENT_CHECKS);
            var tasks = trackIdList.Select(async trackId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var detectionResult = await DetectAvailableQualitiesAsync(trackId, cancellationToken);
                    return (trackId, detectionResult);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            
            foreach (var (trackId, detectionResult) in results)
            {
                result.TrackResults[trackId] = detectionResult;
            }
            
            result.CompletedAt = DateTime.UtcNow;
            result.SuccessCount = result.TrackResults.Values.Count(r => r.Success);
            result.FailureCount = result.TrackResults.Count - result.SuccessCount;
            result.Duration = result.CompletedAt - result.StartedAt;
            
            _logger.Info("Batch quality detection completed: {0} successful, {1} failed in {2:F1}s", 
                result.SuccessCount, result.FailureCount, result.Duration.TotalSeconds);
            
            return result;
        }

        public async Task<AlbumQualityResult> DetectAlbumQualityAsync(QobuzAlbum album, CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                return AlbumQualityResult.Failed("Album has no tracks for quality detection");
            }

            _logger.Info("Detecting album-level quality for '{0}' ({1} tracks)", album.Title, album.TracksCount);
            
            var tracks = album.GetTracks().ToList();
            var sampleTracks = SelectSampleTracks(tracks);
            
            _logger.Debug("Selected {0} sample tracks: {1}", 
                sampleTracks.Count, 
                string.Join(", ", sampleTracks.Select(t => $"#{t.TrackNumber}")));

            // Detect qualities for sample tracks
            var sampleResults = new List<QualityDetectionResult>();
            foreach (var track in sampleTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trackResult = await DetectAvailableQualitiesAsync(track.Id.ToString(), cancellationToken);
                sampleResults.Add(trackResult);
            }

            // Analyze consistency across samples
            var analysis = AnalyzeQualityConsistency(sampleResults);
            
            var result = new AlbumQualityResult
            {
                AlbumId = album.Id,
                AlbumTitle = album.Title,
                TotalTracks = album.TracksCount,
                SampleSize = sampleTracks.Count,
                DetectedQuality = analysis.BestQuality,
                AvailableQualities = analysis.AllQualities,
                ConsistentQuality = analysis.IsConsistent,
                ConfidenceScore = analysis.Confidence,
                Success = analysis.IsConsistent || analysis.AllQualities.Any(),
                OptimizationApplied = analysis.IsConsistent,
                ApiCallsSaved = analysis.IsConsistent ? album.TracksCount - sampleTracks.Count : 0,
                CheckedAt = DateTime.UtcNow
            };
            
            if (result.OptimizationApplied)
            {
                _logger.Info("Album '{0}' has consistent quality {1} (confidence: {2:P1}), saved {3} API calls",
                    album.Title, result.DetectedQuality?.Name ?? "None", analysis.Confidence, result.ApiCallsSaved);
            }
            else
            {
                _logger.Info("Album '{0}' has mixed quality, individual track checks recommended", album.Title);
            }
            
            return result;
        }

        private async Task<bool> CheckQualityAvailabilityAsync(string trackId, QualityFormat quality, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(QUALITY_CHECK_TIMEOUT_MS));

            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["track_id"] = trackId,
                    ["format_id"] = quality.Id.ToString(),
                    ["intent"] = "stream"
                };

                var response = await _apiClient.GetAsync<Dictionary<string, object>>(
                    "track/getFileUrl", 
                    parameters, 
                    timeoutCts.Token);

                if (response != null && response.TryGetValue("url", out var urlObj))
                {
                    var url = urlObj?.ToString();
                    return IsValidStreamUrl(url);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Quality check timed out for track {0} quality {1}", trackId, quality.Name);
            }
            catch (Exception ex)
            {
                _logger.Debug("Quality check failed for track {0} quality {1}: {2}", trackId, quality.Name, ex.Message);
            }

            return false;
        }

        private bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var urlLower = url.ToLower();

            // Check for preview/sample indicators
            var invalidPatterns = new[]
            {
                "_preview", "preview_", "/preview/", "preview=true",
                "_sample", "sample_", "/sample/", "sample=true",
                "_demo", "demo_", "_30sec", "_30s", "duration=30",
                "_clip", "clip_", "_short"
            };

            return !invalidPatterns.Any(pattern => urlLower.Contains(pattern));
        }

        private List<QobuzTrack> SelectSampleTracks(List<QobuzTrack> allTracks)
        {
            if (allTracks.Count <= SAMPLE_TRACK_COUNT)
            {
                return allTracks;
            }

            var sampleTracks = new List<QobuzTrack>();

            // Always include first track
            sampleTracks.Add(allTracks.First());

            // Include middle track if we need more samples
            if (sampleTracks.Count < SAMPLE_TRACK_COUNT && allTracks.Count > 2)
            {
                sampleTracks.Add(allTracks[allTracks.Count / 2]);
            }

            // Include last track if we need more samples
            if (sampleTracks.Count < SAMPLE_TRACK_COUNT && allTracks.Count > 1)
            {
                sampleTracks.Add(allTracks.Last());
            }

            return sampleTracks;
        }

        private QualityConsistencyAnalysis AnalyzeQualityConsistency(List<QualityDetectionResult> sampleResults)
        {
            var analysis = new QualityConsistencyAnalysis();

            if (!sampleResults.Any(r => r.Success))
            {
                return analysis; // No successful detections
            }

            // Find most common highest quality
            var highestQualities = sampleResults
                .Where(r => r.HighestAvailableQuality != null)
                .Select(r => r.HighestAvailableQuality)
                .ToList();

            if (highestQualities.Any())
            {
                var mostCommonQuality = highestQualities
                    .GroupBy(q => q.Id)
                    .OrderByDescending(g => g.Count())
                    .First();

                analysis.BestQuality = mostCommonQuality.First();
                analysis.Confidence = (double)mostCommonQuality.Count() / highestQualities.Count;
                analysis.IsConsistent = analysis.Confidence >= CONSISTENCY_THRESHOLD;
            }

            // Collect all unique qualities found
            analysis.AllQualities = sampleResults
                .Where(r => r.Success)
                .SelectMany(r => r.AvailableQualities)
                .GroupBy(q => q.Id)
                .Select(g => g.First())
                .OrderByDescending(q => q.Priority)
                .ToList();

            return analysis;
        }

        private class QualityConsistencyAnalysis
        {
            public QualityFormat BestQuality { get; set; }
            public List<QualityFormat> AllQualities { get; set; } = new();
            public bool IsConsistent { get; set; }
            public double Confidence { get; set; }
        }
    }

    /// <summary>
    /// Result of quality detection for a single track.
    /// </summary>
    public class QualityDetectionResult
    {
        public string TrackId { get; set; }
        public List<QualityFormat> AvailableQualities { get; set; } = new();
        public QualityFormat HighestAvailableQuality { get; set; }
        public bool Success { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    /// <summary>
    /// Result of batch quality detection.
    /// </summary>
    public class BatchQualityDetectionResult
    {
        public Dictionary<string, QualityDetectionResult> TrackResults { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Result of album-level quality detection with optimization information.
    /// </summary>
    public class AlbumQualityResult
    {
        public string AlbumId { get; set; }
        public string AlbumTitle { get; set; }
        public int TotalTracks { get; set; }
        public int SampleSize { get; set; }
        public QualityFormat DetectedQuality { get; set; }
        public List<QualityFormat> AvailableQualities { get; set; } = new();
        public bool ConsistentQuality { get; set; }
        public double ConfidenceScore { get; set; }
        public bool Success { get; set; }
        public bool OptimizationApplied { get; set; }
        public int ApiCallsSaved { get; set; }
        public DateTime CheckedAt { get; set; }
        public string Error { get; set; }

        public static AlbumQualityResult Failed(string error)
        {
            return new AlbumQualityResult
            {
                Success = false,
                Error = error,
                CheckedAt = DateTime.UtcNow
            };
        }
    }
}