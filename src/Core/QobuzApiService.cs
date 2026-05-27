using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Core API service that orchestrates focused services
    /// </summary>
    public class QobuzApiService
    {
        private readonly QobuzStreamUrlService _streamUrlService;
        private readonly QobuzSearchService _searchService;
        private readonly IQobuzQualityManager _qualityManager;
        private readonly QobuzValidationService _validationService;
        private readonly IQobuzLogger _logger;

        public QobuzApiService(
            QobuzStreamUrlService streamUrlService,
            QobuzSearchService searchService,
            IQobuzQualityManager qualityManager,
            QobuzValidationService validationService,
            IQobuzLogger logger)
        {
            _streamUrlService = streamUrlService ?? throw new ArgumentNullException(nameof(streamUrlService));
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _qualityManager = qualityManager ?? throw new ArgumentNullException(nameof(qualityManager));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QobuzStreamInfo?> GetStreamUrlAsync(string trackId, int formatId)
        {
            return await _streamUrlService.GetStreamInfoAsync(trackId, formatId).ConfigureAwait(false);
        }

        public async Task<QobuzAlbum?> GetAlbumAsync(string albumId)
        {
            return await _searchService.GetAlbumAsync(albumId).ConfigureAwait(false);
        }

        public async Task<List<QobuzTrack>> GetAlbumTracksAsync(string albumId)
        {
            return await _searchService.GetAlbumTracksAsync(albumId).ConfigureAwait(false);
        }

        public async Task<List<QobuzAlbum>> SearchAlbumsAsync(string query, int limit = 20)
        {
            return await _searchService.SearchAlbumsAsync(query, limit).ConfigureAwait(false);
        }

        public async Task<List<QobuzTrack>> SearchTracksAsync(string query, int limit = 20)
        {
            return await _searchService.SearchTracksAsync(query, limit).ConfigureAwait(false);
        }

        public async Task<List<QobuzArtist>> SearchArtistsAsync(string query, int limit = 20)
        {
            return await _searchService.SearchArtistsAsync(query, limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Search for playlists on Qobuz
        /// </summary>
        public async Task<List<QobuzPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
        {
            return await _searchService.SearchPlaylistsAsync(query, limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Get playlist details by ID
        /// </summary>
        public async Task<QobuzPlaylist> GetPlaylistAsync(string playlistId)
        {
            return await _searchService.GetPlaylistAsync(playlistId).ConfigureAwait(false);
        }

        /// <summary>
        /// Get all tracks from a playlist
        /// </summary>
        public async Task<List<QobuzTrack>> GetPlaylistTracksAsync(string playlistId)
        {
            return await _searchService.GetPlaylistTracksAsync(playlistId).ConfigureAwait(false);
        }

        /// <summary>
        /// Search for labels on Qobuz
        /// </summary>
        public async Task<List<QobuzLabel>> SearchLabelsAsync(string query, int limit = 20)
        {
            return await _searchService.SearchLabelsAsync(query, limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Get label details by ID
        /// </summary>
        public async Task<QobuzLabel> GetLabelAsync(string labelId)
        {
            return await _searchService.GetLabelAsync(labelId).ConfigureAwait(false);
        }

        /// <summary>
        /// Get all albums from a label
        /// </summary>
        public async Task<List<QobuzAlbum>> GetLabelAlbumsAsync(string labelId)
        {
            return await _searchService.GetLabelAlbumsAsync(labelId).ConfigureAwait(false);
        }

        /// <summary>
        /// Get artist details by ID
        /// </summary>
        public async Task<QobuzArtist> GetArtistAsync(string artistId)
        {
            return await _searchService.GetArtistAsync(artistId).ConfigureAwait(false);
        }

        /// <summary>
        /// Get all albums from an artist
        /// </summary>
        public async Task<List<QobuzAlbum>> GetArtistAlbumsAsync(string artistId)
        {
            return await _searchService.GetArtistAlbumsAsync(artistId).ConfigureAwait(false);
        }

        // Implementation moved to QobuzStreamUrlService

        /// <summary>
        /// Check which audio qualities are available for a track
        /// </summary>
        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            var result = await _qualityManager.DetectAvailableQualitiesAsync(trackId).ConfigureAwait(false);
            return result.AvailableQualities?.Select(q => q.Id).ToList() ?? new List<int>();
        }

        /// <summary>
        /// Get the best available stream URL with automatic quality fallback
        /// </summary>
        public async Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(string trackId, int preferredQuality)
        {
            // Create QobuzQuality from int (following migration adapter pattern)
            var quality = Models.QobuzQuality.FromId(preferredQuality);

            // Call new consolidated API
            var result = await _qualityManager.SelectBestQualityAsync(trackId, quality).ConfigureAwait(false);

            // Handle failure case
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.Error ?? $"No available quality found for track {trackId}");
            }

            // Convert new StreamInfo to legacy QobuzStreamInfo
            var legacyStreamInfo = new QobuzStreamInfo
            {
                Url = result.StreamInfo.Url,
                FormatId = result.StreamInfo.QualityId
            };

            return (result.SelectedQuality.Id, legacyStreamInfo);
        }

        // Quality fallback implementation moved to QobuzQualityService

        /// <summary>
        /// Validate that an album is actually downloadable before queuing
        /// </summary>
        public async Task<bool> ValidateAlbumDownloadabilityAsync(string albumId, int preferredQuality = 27)
        {
            return await _validationService.ValidateAlbumDownloadabilityAsync(albumId, preferredQuality).ConfigureAwait(false);
        }

        /// <summary>
        /// Batch validate multiple albums efficiently
        /// </summary>
        public async Task<Dictionary<string, bool>> BatchValidateAlbumsAsync(List<string> albumIds, int preferredQuality = 27, int maxConcurrency = 3)
        {
            return await _validationService.BatchValidateAlbumsAsync(albumIds, preferredQuality, maxConcurrency).ConfigureAwait(false);
        }

        /// <summary>
        /// Clean search query by removing venue and date information
        /// </summary>
        public string CleanSearchQuery(string query)
        {
            return _searchService.CleanSearchQuery(query);
        }

        /// <summary>
        /// Generate multiple search variations with progressive query cleaning
        /// </summary>
        public List<string> GenerateSearchVariations(string originalQuery)
        {
            return _searchService.GenerateSearchVariations(originalQuery);
        }

        // Implementation moved to focused services
    }
}
