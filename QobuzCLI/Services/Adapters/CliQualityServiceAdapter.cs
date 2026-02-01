using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Microsoft.Extensions.Caching.Memory;
using NLog;
using NzbDrone.Core.Qualities;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Adapter that extends the plugin's IQualityService with CLI-specific methods.
    /// This maintains plugin-first architecture while providing CLI needs.
    /// </summary>
    public class CliQualityServiceAdapter : IQualityService
    {
        private readonly IQualityService _pluginQualityService;
        private readonly IQobuzApiClient _apiClient;
        private readonly Logger _logger;

        public CliQualityServiceAdapter(
            IQualityService pluginQualityService,
            IQobuzApiClient apiClient,
            Logger logger)
        {
            _pluginQualityService = pluginQualityService ?? throw new ArgumentNullException(nameof(pluginQualityService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Delegate all base interface methods to the plugin service
        public Quality MapQualityFromFormatId(int formatId)
            => _pluginQualityService.MapQualityFromFormatId(formatId);

        public int GetBestAvailableFormatId(QobuzTrack track, int maxFormatId = 27)
            => _pluginQualityService.GetBestAvailableFormatId(track, maxFormatId);

        public Task<QualityDetectionResult> DetectQualityAsync(QobuzTrack track, QobuzAlbum? album = null)
            => _pluginQualityService.DetectQualityAsync(track, album ?? new QobuzAlbum());

        public string GetQualityLabel(int formatId)
            => _pluginQualityService.GetQualityLabel(formatId);

        public bool IsQualityAvailable(QobuzTrack track, int requestedFormatId)
            => _pluginQualityService.IsQualityAvailable(track, requestedFormatId);

        public QualityStatistics GetStatistics()
            => _pluginQualityService.GetStatistics();

        public void ClearCache()
            => _pluginQualityService.ClearCache();

        public QobuzQuality MapLidarrQuality(object qualityProfile)
            => _pluginQualityService.MapLidarrQuality(qualityProfile);

        public List<QobuzQuality> GetQualityFallbackChain(QobuzQuality mappedQuality)
            => _pluginQualityService.GetQualityFallbackChain(mappedQuality);

        // CLI-specific extension methods

        /// <summary>
        /// Gets available qualities for a track (CLI-specific method)
        /// </summary>
        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            try
            {
                // Get track details from API
                var parameters = new Dictionary<string, string> { { "track_id", trackId } };
                var response = await _apiClient.GetAsync<dynamic>("/track/get", parameters);
                if (response == null || response?.track == null)
                {
                    _logger.Warn($"Track {trackId} not found");
                    return new List<int>();
                }

                // Convert dynamic to QobuzTrack
                var trackObj = response!.track!;
                var track = new QobuzTrack
                {
                    Id = trackId,
                    Streamable = trackObj.streamable ?? false,
                    MaximumBitDepth = trackObj.maximum_bit_depth,
                    MaximumSampleRate = trackObj.maximum_sampling_rate
                };

                // Detect available qualities
                var detection = await DetectQualityAsync(track);

                // Return list of available format IDs
                return detection.AvailableQualities
                    .Select(q => q.Id)
                    .Distinct()
                    .OrderByDescending(id => id)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting available qualities for track {trackId}");
                return new List<int> { 6 }; // Default to CD quality
            }
        }

        /// <summary>
        /// Gets best available stream with quality fallback (CLI-specific method)
        /// </summary>
        public async Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(string trackId, int preferredQuality)
        {
            try
            {
                // Get track details from API
                var parameters = new Dictionary<string, string> { { "track_id", trackId } };
                var response = await _apiClient.GetAsync<dynamic>("/track/get", parameters);
                if (response == null || response?.track == null)
                {
                    throw new Exception($"Track {trackId} not found");
                }

                // Convert dynamic to QobuzTrack
                var track = new QobuzTrack
                {
                    Id = trackId,
                    Streamable = response!.track!.streamable ?? false,
                    MaximumBitDepth = response!.track!.maximum_bit_depth,
                    MaximumSampleRate = response!.track!.maximum_sampling_rate
                };

                // Get best available format
                var bestFormat = GetBestAvailableFormatId(track, preferredQuality);

                // Get stream info for that format
                // Prefer plugin API client for streaming URL
                var url = await _apiClient.GetStreamingUrlAsync(trackId, bestFormat).ConfigureAwait(false);
                QobuzStreamInfo? streamInfo = null;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    streamInfo = new QobuzStreamInfo { Url = url, FormatId = bestFormat };
                }
                if (streamInfo == null)
                {
                    // Try fallback to CD quality
                    _logger.Warn($"Could not get stream for format {bestFormat}, trying CD quality");
                    bestFormat = 6;
                    url = await _apiClient.GetStreamingUrlAsync(trackId, bestFormat).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        streamInfo = new QobuzStreamInfo { Url = url, FormatId = bestFormat };
                    }
                }

                if (streamInfo == null)
                {
                    throw new Exception($"Could not get stream URL for track {trackId}");
                }

                return (bestFormat, streamInfo);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting best available stream for track {trackId}");
                throw;
            }
        }
    }

    /// <summary>
    /// Extension interface for CLI-specific quality methods
    /// </summary>
    public interface ICliQualityService : IQualityService
    {
        Task<List<int>> GetAvailableQualitiesAsync(string trackId);
        Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(string trackId, int preferredQuality);
    }
}
