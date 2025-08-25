using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Core.Quality;
using IStreamUrlProviderInterface = Lidarr.Plugin.Qobuzarr.Services.Interfaces.IStreamUrlProvider;
using IStreamUrlValidatorInterface = Lidarr.Plugin.Qobuzarr.Services.Interfaces.IStreamUrlValidator;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Streaming
{
    /// <summary>
    /// Implementation of stream URL provider with validation and error handling.
    /// Implements the centralized IStreamUrlProvider interface.
    /// </summary>
    public class StreamUrlProvider : IStreamUrlProviderInterface
    {
        private readonly Lidarr.Plugin.Qobuzarr.API.IQobuzApiClient _apiClient;
        private readonly IStreamUrlValidatorInterface _validator;
        private readonly IQobuzLogger _logger;

        // Configuration constants
        private const int STREAM_REQUEST_TIMEOUT_MS = 15000;
        private const int MAX_CONCURRENT_REQUESTS = 5;
        private const int MAX_RETRY_ATTEMPTS = 2;

        public StreamUrlProvider(
            Lidarr.Plugin.Qobuzarr.API.IQobuzApiClient apiClient,
            IStreamUrlValidatorInterface validator,
            IQobuzLogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Implement centralized interface methods
        public async Task<StreamUrlResult> GetStreamUrlAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var result = new StreamUrlResult
            {
                OriginalPreferredQuality = preferredQuality,
                Success = false
            };

            try
            {
                // Convert quality ID to QualityFormat for legacy method
                var qualityFormat = new QualityFormat { Id = preferredQuality, Name = $"Quality{preferredQuality}" };
                var legacyResult = await GetStreamUrlLegacyAsync(trackId, qualityFormat, cancellationToken);

                result.Success = legacyResult.Success;
                result.StreamUrl = legacyResult.StreamUrl;
                result.QualityId = legacyResult.ActualQuality?.Id ?? preferredQuality;
                result.QualityName = legacyResult.ActualQuality?.Name ?? $"Quality{preferredQuality}";
                // FileSizeBytes not available in StreamAcquisitionResult
                result.FileSizeBytes = null;
                result.ExpiresAt = legacyResult.ExpiresAt;
                result.Error = legacyResult.Error;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            result.GenerationTime = DateTime.UtcNow - startTime;
            return result;
        }

        public async Task<Dictionary<string, StreamUrlResult>> GetBatchStreamUrlsAsync(IReadOnlyList<string> trackIds, int preferredQuality, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, StreamUrlResult>();
            
            foreach (var trackId in trackIds)
            {
                results[trackId] = await GetStreamUrlAsync(trackId, preferredQuality, cancellationToken);
            }
            
            return results;
        }

        public async Task<StreamUrlResult?> GetExactQualityStreamUrlAsync(string trackId, int qualityId, CancellationToken cancellationToken = default)
        {
            return await GetStreamUrlAsync(trackId, qualityId, cancellationToken);
        }

        public async Task<StreamUrlResult?> RefreshStreamUrlAsync(string url, string trackId, int qualityId, CancellationToken cancellationToken = default)
        {
            // For now, just get a new URL - in practice this might try to refresh an existing URL
            return await GetStreamUrlAsync(trackId, qualityId, cancellationToken);
        }

        public async Task<StreamAcquisitionResult> GetStreamUrlLegacyAsync(string trackId, QualityFormat quality, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            if (quality == null)
            {
                throw new ArgumentNullException(nameof(quality));
            }

            _logger.Debug("Acquiring stream URL for track {0} with quality {1}", trackId, quality.Name);

            var result = new StreamAcquisitionResult
            {
                TrackId = trackId,
                RequestedQuality = quality,
                RequestedAt = DateTime.UtcNow
            };

            try
            {
                var streamResponse = await RequestStreamUrlAsync(trackId, quality, cancellationToken);
                
                // Validate the response
                var validation = _validator.ValidateStreamResponse(streamResponse);
                result.ValidationResult = validation;

                if (validation.IsValid)
                {
                    result.Success = true;
                    result.StreamUrl = streamResponse.Url;
                    result.ActualQuality = quality;
                    result.ExpiresAt = CalculateExpirationTime(streamResponse);
                    
                    _logger.Debug("Stream URL acquired successfully for track {0}", trackId);
                }
                else
                {
                    result.Success = false;
                    result.Error = validation.Message;
                    result.FailureReason = MapValidationIssueToFailureReason(validation.Issue);
                    
                    _logger.Debug("Stream URL validation failed for track {0}: {1}", trackId, validation.Message);
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Error = "Stream URL request was cancelled";
                result.FailureReason = StreamAcquisitionFailureReason.Cancelled;
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.FailureReason = StreamAcquisitionFailureReason.ApiError;
                
                _logger.Error(ex, "Failed to acquire stream URL for track {0}", trackId);
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.RequestedAt;

            return result;
        }

        public async Task<BatchStreamAcquisitionResult> GetBatchStreamUrlsLegacyAsync(IEnumerable<string> trackIds, QualityFormat quality, CancellationToken cancellationToken = default)
        {
            var trackIdList = trackIds?.ToList() ?? throw new ArgumentNullException(nameof(trackIds));
            
            if (!trackIdList.Any())
            {
                throw new ArgumentException("Track IDs cannot be empty", nameof(trackIds));
            }

            if (quality == null)
            {
                throw new ArgumentNullException(nameof(quality));
            }

            _logger.Info("Starting batch stream URL acquisition for {0} tracks with quality {1}", trackIdList.Count, quality.Name);

            var result = new BatchStreamAcquisitionResult
            {
                RequestedQuality = quality,
                TrackResults = new Dictionary<string, StreamAcquisitionResult>(),
                StartedAt = DateTime.UtcNow
            };

            // Process tracks in parallel with concurrency limit
            var semaphore = new SemaphoreSlim(MAX_CONCURRENT_REQUESTS, MAX_CONCURRENT_REQUESTS);
            var tasks = trackIdList.Select(async trackId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var acquisitionResult = await GetStreamUrlAsync(trackId, quality.Id, cancellationToken);
                    return (trackId, acquisitionResult);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            
            foreach (var (trackId, acquisitionResult) in results)
            {
                result.TrackResults[trackId] = acquisitionResult;
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;
            result.SuccessCount = result.TrackResults.Values.Count(r => r.Success);
            result.FailureCount = result.TrackResults.Count - result.SuccessCount;

            _logger.Info("Batch stream URL acquisition completed: {0} successful, {1} failed in {2:F1}s",
                result.SuccessCount, result.FailureCount, result.Duration.TotalSeconds);

            return result;
        }

        public async Task<StreamAcquisitionResult> GetStreamUrlWithFallbackLegacyAsync(string trackId, IReadOnlyList<QualityFormat> qualityChain, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            if (qualityChain?.Any() != true)
            {
                throw new ArgumentException("Quality chain cannot be null or empty", nameof(qualityChain));
            }

            _logger.Debug("Acquiring stream URL for track {0} with fallback chain: [{1}]", 
                trackId, string.Join(", ", qualityChain.Select(q => q.Name)));

            var preferredQuality = qualityChain.First();
            var allErrors = new List<string>();

            foreach (var quality in qualityChain)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await GetStreamUrlAsync(trackId, quality.Id, cancellationToken);
                    
                    if (result.Success)
                    {
                        result.FallbackUsed = quality.Id != preferredQuality.Id;
                        result.AttemptsCount = qualityChain.ToList().IndexOf(quality) + 1;

                        if (result.FallbackUsed)
                        {
                            _logger.Info("Stream URL acquired with fallback: track {0}, requested {1}, using {2}", 
                                trackId, preferredQuality.Name, quality.Name);
                        }

                        return result;
                    }
                    else
                    {
                        allErrors.Add($"{quality.Name}: {result.Error}");
                        _logger.Debug("Quality {0} failed for track {1}: {2}", quality.Name, trackId, result.Error);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    allErrors.Add($"{quality.Name}: {ex.Message}");
                    _logger.Debug("Quality {0} failed for track {1}: {2}", quality.Name, trackId, ex.Message);
                }
            }

            // All qualities failed
            var finalResult = new StreamAcquisitionResult
            {
                TrackId = trackId,
                RequestedQuality = preferredQuality,
                Success = false,
                Error = $"All qualities failed: {string.Join("; ", allErrors)}",
                FailureReason = StreamAcquisitionFailureReason.NoQualityAvailable,
                AttemptsCount = qualityChain.Count,
                RequestedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            finalResult.Duration = finalResult.CompletedAt - finalResult.RequestedAt;
            
            _logger.Warn("Stream URL acquisition failed for track {0} in all {1} quality attempts", trackId, qualityChain.Count);
            
            return finalResult;
        }

        private async Task<QobuzStreamResponse> RequestStreamUrlAsync(string trackId, QualityFormat quality, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(STREAM_REQUEST_TIMEOUT_MS));

            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId,
                ["format_id"] = quality.Id.ToString(),
                ["intent"] = "stream"
            };

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var response = await _apiClient.GetAsync<QobuzStreamResponse>(
                        "track/getFileUrl", 
                        parameters);

                    return response;
                }
                catch (Exception ex) when (attempt < MAX_RETRY_ATTEMPTS && !(ex is OperationCanceledException))
                {
                    _logger.Debug("Stream URL request attempt {0} failed for track {1}: {2}, retrying...", 
                        attempt, trackId, ex.Message);
                    
                    // Brief delay before retry
                    await Task.Delay(1000 * attempt, timeoutCts.Token);
                }
            }

            // This should not be reached due to the retry logic, but included for completeness
            throw new InvalidOperationException($"Failed to get stream URL after {MAX_RETRY_ATTEMPTS} attempts");
        }

        private DateTime? CalculateExpirationTime(QobuzStreamResponse response)
        {
            // Qobuz stream URLs typically expire after 1 hour
            return DateTime.UtcNow.AddHours(1);
        }

        private StreamAcquisitionFailureReason MapValidationIssueToFailureReason(StreamValidationIssue issue)
        {
            return issue switch
            {
                StreamValidationIssue.PreviewOnly => StreamAcquisitionFailureReason.PreviewOnly,
                StreamValidationIssue.SubscriptionRestriction => StreamAcquisitionFailureReason.SubscriptionRestriction,
                StreamValidationIssue.RegionalRestriction => StreamAcquisitionFailureReason.RegionalRestriction,
                StreamValidationIssue.QualityUnavailable => StreamAcquisitionFailureReason.QualityUnavailable,
                StreamValidationIssue.TrackNotFound => StreamAcquisitionFailureReason.TrackNotFound,
                StreamValidationIssue.ServerError => StreamAcquisitionFailureReason.ServerError,
                StreamValidationIssue.InvalidFormat => StreamAcquisitionFailureReason.InvalidUrl,
                StreamValidationIssue.EmptyUrl => StreamAcquisitionFailureReason.InvalidUrl,
                _ => StreamAcquisitionFailureReason.Unknown
            };
        }
    }

    /// <summary>
    /// Result of stream URL acquisition.
    /// </summary>
    public class StreamAcquisitionResult
    {
        public string TrackId { get; set; }
        public QualityFormat RequestedQuality { get; set; }
        public QualityFormat ActualQuality { get; set; }
        public bool Success { get; set; }
        public string StreamUrl { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool FallbackUsed { get; set; }
        public int AttemptsCount { get; set; }
        public string Error { get; set; }
        public StreamAcquisitionFailureReason FailureReason { get; set; }
        public StreamValidationResult ValidationResult { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Result of batch stream URL acquisition.
    /// </summary>
    public class BatchStreamAcquisitionResult
    {
        public QualityFormat RequestedQuality { get; set; }
        public Dictionary<string, StreamAcquisitionResult> TrackResults { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Reasons for stream acquisition failure.
    /// </summary>
    public enum StreamAcquisitionFailureReason
    {
        Unknown = 0,
        ApiError,
        PreviewOnly,
        SubscriptionRestriction,
        RegionalRestriction,
        QualityUnavailable,
        TrackNotFound,
        ServerError,
        InvalidUrl,
        NoQualityAvailable,
        Cancelled
    }
}