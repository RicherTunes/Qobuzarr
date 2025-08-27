using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service for managing stream URLs and information.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public class StreamInfoService : IStreamInfoService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IQualityMappingService _qualityMappingService;
        private readonly IQobuzLogger _logger;

        public StreamInfoService(
            IQobuzApiClient apiClient,
            IQualityMappingService qualityMappingService,
            IQobuzLogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _qualityMappingService = qualityMappingService ?? throw new ArgumentNullException(nameof(qualityMappingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<StreamInfo> GetStreamInfoAsync(string trackId, QobuzQuality quality, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            if (quality == null)
            {
                throw new ArgumentNullException(nameof(quality));
            }

            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["track_id"] = trackId,
                    ["format_id"] = quality.Id.ToString()
                };

                var response = await _apiClient.GetAsync<Dictionary<string, object>>(
                    "track/getFileUrl", 
                    parameters);

                if (response != null && response.TryGetValue("url", out var urlObj))
                {
                    var url = urlObj?.ToString();
                    
                    if (IsValidStreamUrl(url))
                    {
                        return new StreamInfo
                        {
                            Url = url,
                            QualityId = quality.Id,
                            TrackId = trackId,
                            ExpiresAt = DateTime.UtcNow.AddHours(1) // URLs typically expire after 1 hour
                        };
                    }
                    else
                    {
                        _logger.Debug("Invalid or preview stream URL detected for track {0} at quality {1}", trackId, quality.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to get stream info for track {0} at quality {1}: {2}", 
                    trackId, quality.Name, ex.Message);
            }

            return null;
        }

        public async Task<BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default)
        {
            if (trackIds == null || !trackIds.Any())
            {
                throw new ArgumentException("Track IDs cannot be null or empty", nameof(trackIds));
            }

            if (quality == null)
            {
                throw new ArgumentNullException(nameof(quality));
            }

            _logger.Info("Getting batch stream info for {0} tracks with quality {1}", trackIds.Count, quality.Name);
            
            var result = new BatchStreamResult
            {
                RequestedQuality = quality,
                TrackResults = new Dictionary<string, StreamInfo>()
            };

            // Process in parallel with concurrency limit
            var semaphore = new SemaphoreSlim(5); // Max 5 concurrent requests
            var tasks = trackIds.Select(async trackId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var streamInfo = await GetStreamInfoAsync(trackId, quality, cancellationToken);
                    return (trackId, streamInfo);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            
            foreach (var (trackId, streamInfo) in results)
            {
                if (streamInfo != null)
                {
                    result.TrackResults[trackId] = streamInfo;
                }
            }
            
            result.SuccessCount = result.TrackResults.Count;
            result.FailureCount = trackIds.Count - result.SuccessCount;
            
            _logger.Info("Batch stream info complete: {0} successful, {1} failed", 
                result.SuccessCount, result.FailureCount);
            
            return result;
        }

        public async Task<QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            var fallbackChain = _qualityMappingService.GetQualityFallbackChain(preferred);
            
            _logger.Debug("Attempting quality selection for track {0}, preferred: {1}", trackId, preferred?.Name);
            
            foreach (var quality in fallbackChain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var streamInfo = await GetStreamInfoAsync(trackId, quality, cancellationToken);
                    
                    if (streamInfo != null && IsValidStreamUrl(streamInfo.Url))
                    {
                        return QualitySelectionResult.Successful(
                            quality, 
                            streamInfo, 
                            quality.Id != preferred?.Id, 
                            fallbackChain.IndexOf(quality) + 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} failed for track {1}: {2}", quality.Name, trackId, ex.Message);
                }
            }
            
            return QualitySelectionResult.Failed($"No available quality found for track {trackId}", fallbackChain.Count);
        }

        public async Task<T> ExecuteWithQualityFallbackAsync<T>(
            Func<QobuzQuality, Task<T>> operation,
            QobuzQuality preferred = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var fallbackChain = _qualityMappingService.GetQualityFallbackChain(preferred ?? _qualityMappingService.GetDefaultQuality());
            var exceptions = new List<Exception>();
            
            foreach (var quality in fallbackChain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    _logger.Debug("Attempting operation with quality: {0}", quality.Name);
                    var result = await operation(quality);
                    
                    if (quality.Id != preferred?.Id)
                    {
                        _logger.Info("Operation succeeded with fallback quality: {0}", quality.Name);
                    }
                    
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn("Operation failed with quality {0}: {1}", quality.Name, ex.Message);
                    exceptions.Add(ex);
                }
            }
            
            throw new AggregateException($"All quality levels failed for operation", exceptions);
        }

        private bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

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
    }
}