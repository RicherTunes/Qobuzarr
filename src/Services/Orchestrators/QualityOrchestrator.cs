using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Core.Quality;
using Lidarr.Plugin.Qobuzarr.Services.Core.Streaming;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Orchestrators
{
    /// <summary>
    /// Implementation of quality orchestrator that coordinates all quality-related services.
    /// </summary>
    public class QualityOrchestrator : IQualityOrchestrator
    {
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly IQualityFallbackStrategy _fallbackStrategy;
        private readonly IQualityDetector _qualityDetector;
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IStreamUrlValidator _streamUrlValidator;
        private readonly IQobuzLogger _logger;

        public QualityOrchestrator(
            IQualityDefinitionService qualityDefinitionService,
            IQualityFallbackStrategy fallbackStrategy,
            IQualityDetector qualityDetector,
            IStreamUrlProvider streamUrlProvider,
            IStreamUrlValidator streamUrlValidator,
            IQobuzLogger logger)
        {
            _qualityDefinitionService = qualityDefinitionService ?? throw new ArgumentNullException(nameof(qualityDefinitionService));
            _fallbackStrategy = fallbackStrategy ?? throw new ArgumentNullException(nameof(fallbackStrategy));
            _qualityDetector = qualityDetector ?? throw new ArgumentNullException(nameof(qualityDetector));
            _streamUrlProvider = streamUrlProvider ?? throw new ArgumentNullException(nameof(streamUrlProvider));
            _streamUrlValidator = streamUrlValidator ?? throw new ArgumentNullException(nameof(streamUrlValidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QualitySelectionResult> SelectBestQualityAsync(string trackId, QualityFormat preferredQuality, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            preferredQuality ??= _qualityDefinitionService.GetQualityByIdLegacy(6); // CD quality default

            _logger.Debug("Starting quality selection for track {0} with preferred quality {1}", trackId, preferredQuality.Name);

            var result = new QualitySelectionResult
            {
                TrackId = trackId,
                PreferredQuality = preferredQuality,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // Create fallback chain
                var fallbackChain = _fallbackStrategy.CreateFallbackChain(preferredQuality);
                result.FallbackChainUsed = fallbackChain;

                // Attempt to get stream URL with fallback
                var streamResult = await _streamUrlProvider.GetStreamUrlWithFallbackAsync(trackId, fallbackChain, cancellationToken);

                if (streamResult.Success)
                {
                    result.Success = true;
                    result.SelectedQuality = streamResult.ActualQuality;
                    result.StreamUrl = streamResult.StreamUrl;
                    result.StreamExpiresAt = streamResult.ExpiresAt;
                    result.FallbackUsed = streamResult.FallbackUsed;
                    result.AttemptsCount = streamResult.AttemptsCount;

                    _logger.Info("Quality selection successful for track {0}: using {1}{2}", 
                        trackId, 
                        result.SelectedQuality.Name,
                        result.FallbackUsed ? $" (fallback from {preferredQuality.Name})" : "");
                }
                else
                {
                    result.Success = false;
                    result.Error = streamResult.Error;
                    result.FailureReason = streamResult.FailureReason.ToString();
                    result.AttemptsCount = streamResult.AttemptsCount;

                    _logger.Warn("Quality selection failed for track {0}: {1}", trackId, streamResult.Error);
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Error = "Quality selection was cancelled";
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                _logger.Error(ex, "Quality selection failed for track {0}", trackId);
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;

            return result;
        }

        public async Task<QualitySelectionResult> SelectBestQualityAsync(string trackId, LidarrQualityProfile profile, CancellationToken cancellationToken = default)
        {
            // Create fallback chain from Lidarr profile
            var fallbackChain = _fallbackStrategy.CreateFallbackChainFromProfile(profile);
            var preferredQuality = fallbackChain.Count > 0 ? fallbackChain[0] : _qualityDefinitionService.GetQualityByIdLegacy(6); // CD quality default

            _logger.Debug("Quality selection for track {0} using Lidarr profile '{1}' -> preferred quality {2}", 
                trackId, profile?.Name ?? "None", preferredQuality.Name);

            return await SelectBestQualityAsync(trackId, preferredQuality, cancellationToken);
        }

        public async Task<AlbumQualitySelectionResult> SelectAlbumQualityAsync(QobuzAlbum album, QualityFormat preferredQuality, CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                throw new ArgumentException("Album must have tracks for quality selection", nameof(album));
            }

            preferredQuality ??= _qualityDefinitionService.GetQualityByIdLegacy(6); // CD quality default

            _logger.Info("Starting album quality selection for '{0}' with preferred quality {1}", album.Title, preferredQuality.Name);

            var result = new AlbumQualitySelectionResult
            {
                AlbumId = album.Id,
                AlbumTitle = album.Title,
                PreferredQuality = preferredQuality,
                TotalTracks = album.TracksCount,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // First, detect album-level quality availability
                var qualityDetectionResult = await _qualityDetector.DetectAlbumQualityAsync(album, cancellationToken);
                result.QualityDetectionResult = qualityDetectionResult;

                if (qualityDetectionResult.Success && qualityDetectionResult.OptimizationApplied)
                {
                    // Album has consistent quality - optimize by using detected quality
                    result.OptimizationStrategy = "AlbumLevel";
                    result.SelectedQuality = qualityDetectionResult.DetectedQuality;
                    result.ConfidenceScore = qualityDetectionResult.ConfidenceScore;
                    result.ApiCallsSaved = qualityDetectionResult.ApiCallsSaved;

                    _logger.Info("Album '{0}' quality optimization: consistent {1} quality detected (confidence: {2:P1}), saved {3} API calls",
                        album.Title, result.SelectedQuality.Name, result.ConfidenceScore, result.ApiCallsSaved);
                }
                else
                {
                    // Mixed quality album - use track-by-track approach
                    result.OptimizationStrategy = "TrackLevel";
                    result.SelectedQuality = preferredQuality;
                    result.RequiresTrackLevelSelection = true;

                    _logger.Info("Album '{0}' requires track-level quality selection due to mixed quality availability", album.Title);
                }

                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Error = "Album quality selection was cancelled";
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                _logger.Error(ex, "Album quality selection failed for '{0}'", album.Title);
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;

            return result;
        }

        public async Task<T> ExecuteWithQualityFallbackAsync<T>(string trackId, QualityFormat preferredQuality, Func<string, QualityFormat, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            preferredQuality ??= _qualityDefinitionService.GetQualityByIdLegacy(6); // CD quality default
            
            var fallbackChain = _fallbackStrategy.CreateFallbackChain(preferredQuality);
            var exceptions = new List<Exception>();

            _logger.Debug("Executing operation with quality fallback for track {0}, chain: [{1}]",
                trackId, string.Join(", ", fallbackChain.Select(q => q.Name)));

            foreach (var quality in fallbackChain)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    _logger.Debug("Attempting operation with quality {0}", quality.Name);
                    var result = await operation(trackId, quality);

                    if (quality.Id != preferredQuality.Id)
                    {
                        _logger.Info("Operation succeeded with fallback quality {0} (requested {1})", quality.Name, preferredQuality.Name);
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    
                    if (!_fallbackStrategy.ShouldAttemptFallback(ex, quality))
                    {
                        _logger.Debug("Fallback not recommended for quality {0}, stopping attempts", quality.Name);
                        throw;
                    }

                    _logger.Debug("Operation failed with quality {0}: {1}, attempting fallback", quality.Name, ex.Message);
                }
            }

            // All qualities failed
            var aggregateEx = new AggregateException($"Operation failed for all quality levels on track {trackId}", exceptions);
            _logger.Error(aggregateEx, "All quality fallback attempts failed for track {0}", trackId);
            throw aggregateEx;
        }

        #region IQualityOrchestrator Interface Implementation

        /// <summary>
        /// Selects the best available quality for a track with fallback (interface implementation).
        /// </summary>
        public async Task<Services.Interfaces.QualitySelectionResult> SelectBestQualityAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default)
        {
            try
            {
                var qualityFormat = _qualityDefinitionService.GetQualityByIdLegacy(preferredQuality);
                var result = await SelectBestQualityAsync(trackId, qualityFormat, cancellationToken);
                
                return new Services.Interfaces.QualitySelectionResult
                {
                    Success = result.Success,
                    QualityId = result.SelectedQuality?.Id ?? 0,
                    QualityName = result.SelectedQuality?.Name ?? "Unknown",
                    StreamUrl = result.StreamUrl,
                    IsFallbackQuality = result.FallbackUsed,
                    OriginalPreferredQuality = preferredQuality,
                    Error = result.Error,
                    ProcessingTime = result.Duration
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error selecting quality for track {0}", trackId);
                return new Services.Interfaces.QualitySelectionResult
                {
                    Success = false,
                    QualityId = 0,
                    OriginalPreferredQuality = preferredQuality,
                    Error = ex.Message,
                    ProcessingTime = TimeSpan.Zero
                };
            }
        }

        /// <summary>
        /// Detects all available qualities for a track (interface implementation).
        /// </summary>
        public async Task<Services.Interfaces.QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default)
        {
            try
            {
                var availableQualities = await _qualityDetector.GetAvailableQualitiesAsync(trackId, cancellationToken);
                var qualityIds = availableQualities.Select(q => q.Id).ToList();
                var highestQuality = qualityIds.Any() ? qualityIds.Max() : (int?)null;
                
                return new Services.Interfaces.QualityDetectionResult
                {
                    Success = true,
                    AvailableQualities = qualityIds,
                    HighestAvailableQuality = highestQuality,
                    DetectionTime = TimeSpan.FromMilliseconds(100) // Approximate
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error detecting available qualities for track {0}", trackId);
                return new Services.Interfaces.QualityDetectionResult
                {
                    Success = false,
                    Error = ex.Message,
                    DetectionTime = TimeSpan.Zero
                };
            }
        }

        /// <summary>
        /// Processes quality selection for multiple tracks in batch (interface implementation).
        /// </summary>
        public async Task<Dictionary<string, Services.Interfaces.QualitySelectionResult>> ProcessBatchQualityAsync(
            IReadOnlyList<string> trackIds, 
            int preferredQuality, 
            int maxConcurrency = 5, 
            CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, Services.Interfaces.QualitySelectionResult>();
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            var tasks = trackIds.Select(async trackId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await SelectBestQualityAsync(trackId, preferredQuality, cancellationToken);
                    return new KeyValuePair<string, Services.Interfaces.QualitySelectionResult>(trackId, result);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            var completedTasks = await Task.WhenAll(tasks);
            foreach (var kvp in completedTasks)
            {
                results[kvp.Key] = kvp.Value;
            }
            
            return results;
        }

        /// <summary>
        /// Gets the quality fallback chain for a preferred quality (interface implementation).
        /// </summary>
        public List<int> GetFallbackChain(int preferredQuality)
        {
            try
            {
                var qualityFormat = _qualityDefinitionService.GetQualityByIdLegacy(preferredQuality);
                var fallbackChain = _fallbackStrategy.CreateFallbackChain(qualityFormat);
                return fallbackChain.Select(q => q.Id).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating fallback chain for quality {0}", preferredQuality);
                return new List<int> { preferredQuality };
            }
        }

        /// <summary>
        /// Maps a Lidarr quality to a Qobuz quality ID (interface implementation).
        /// </summary>
        public int MapLidarrQualityToQobuz(object lidarrQuality)
        {
            // This would need proper Lidarr quality mapping
            // For now, return a default CD quality
            return 6; // CD quality (FLAC 16/44.1)
        }

        /// <summary>
        /// Gets streaming information for a track with quality management (interface implementation).
        /// </summary>
        public async Task<Services.Interfaces.StreamInfo> GetStreamInfoAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default)
        {
            try
            {
                var qualityFormat = _qualityDefinitionService.GetQualityByIdLegacy(preferredQuality);
                var streamResult = await _streamUrlProvider.GetStreamUrlAsync(trackId, qualityFormat.Id, cancellationToken);
                
                return new Services.Interfaces.StreamInfo
                {
                    Url = streamResult.StreamUrl,
                    QualityId = qualityFormat.Id,
                    QualityName = qualityFormat.Name,
                    ExpiresAt = streamResult.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting stream info for track {0}", trackId);
                return new Services.Interfaces.StreamInfo
                {
                    QualityId = preferredQuality,
                    QualityName = "Unknown"
                };
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of quality selection for a single track.
    /// </summary>
    public class QualitySelectionResult
    {
        public string TrackId { get; set; }
        public QualityFormat PreferredQuality { get; set; }
        public QualityFormat SelectedQuality { get; set; }
        public bool Success { get; set; }
        public string StreamUrl { get; set; }
        public DateTime? StreamExpiresAt { get; set; }
        public bool FallbackUsed { get; set; }
        public int AttemptsCount { get; set; }
        public IReadOnlyList<QualityFormat> FallbackChainUsed { get; set; }
        public string Error { get; set; }
        public string FailureReason { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Result of album-level quality selection with optimization information.
    /// </summary>
    public class AlbumQualitySelectionResult
    {
        public string AlbumId { get; set; }
        public string AlbumTitle { get; set; }
        public QualityFormat PreferredQuality { get; set; }
        public QualityFormat SelectedQuality { get; set; }
        public bool Success { get; set; }
        public string OptimizationStrategy { get; set; }
        public bool RequiresTrackLevelSelection { get; set; }
        public double ConfidenceScore { get; set; }
        public int ApiCallsSaved { get; set; }
        public int TotalTracks { get; set; }
        public AlbumQualityResult QualityDetectionResult { get; set; }
        public string Error { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }
}