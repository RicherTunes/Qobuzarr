using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using QobuzCLI.Services.Adapters;

namespace QobuzCLI.Services
{
    /// <summary>
    /// CLI-specific API service that provides a simplified interface
    /// without the complexity of the full consolidated plugin architecture
    /// </summary>
    public class CliApiService
    {
        private readonly QobuzStreamUrlService _streamUrlService;
        private readonly QobuzSearchService _searchService;
        private readonly CliQualityServiceAdapter _qualityService;
        private readonly CliQobuzValidationService _validationService;
        private readonly IQobuzLogger _logger;

        public CliApiService(
            QobuzStreamUrlService streamUrlService,
            QobuzSearchService searchService,
            CliQualityServiceAdapter qualityService,
            CliQobuzValidationService validationService,
            IQobuzLogger logger)
        {
            _streamUrlService = streamUrlService ?? throw new ArgumentNullException(nameof(streamUrlService));
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _qualityService = qualityService ?? throw new ArgumentNullException(nameof(qualityService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get stream URL for a track
        /// </summary>
        public async Task<QobuzStreamInfo?> GetStreamUrlAsync(string trackId, int formatId)
        {
            return await _streamUrlService.GetStreamInfoAsync(trackId, formatId);
        }

        /// <summary>
        /// Search for albums
        /// </summary>
        public async Task<List<QobuzAlbum>> SearchAlbumsAsync(string query, int limit = 25)
        {
            return await _searchService.SearchAlbumsAsync(query, limit);
        }

        /// <summary>
        /// Search for tracks
        /// </summary>
        public async Task<List<QobuzTrack>> SearchTracksAsync(string query, int limit = 25)
        {
            return await _searchService.SearchTracksAsync(query, limit);
        }

        /// <summary>
        /// Get album details
        /// </summary>
        public async Task<QobuzAlbum?> GetAlbumAsync(string albumId)
        {
            return await _searchService.GetAlbumAsync(albumId);
        }

        /// <summary>
        /// Check available qualities for a track
        /// </summary>
        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            return await _qualityService.GetAvailableQualitiesAsync(trackId);
        }

        /// <summary>
        /// Get best available stream with quality fallback
        /// </summary>
        public async Task<(int selectedQuality, QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(string trackId, int preferredQuality)
        {
            return await _qualityService.GetBestAvailableStreamAsync(trackId, preferredQuality);
        }

        /// <summary>
        /// Validate album downloadability
        /// </summary>
        public async Task<bool> ValidateAlbumDownloadabilityAsync(string albumId, int preferredQuality = 27)
        {
            return await _validationService.ValidateAlbumDownloadabilityAsync(albumId, preferredQuality);
        }

        /// <summary>
        /// Generate search variations for query optimization
        /// </summary>
        public List<string> GenerateSearchVariations(string query)
        {
            // Simple implementation for CLI
            return new List<string> { query };
        }

        /// <summary>
        /// Search for artists
        /// </summary>
        public async Task<List<QobuzArtist>> SearchArtistsAsync(string query, int limit = 25)
        {
            return await _searchService.SearchArtistsAsync(query, limit);
        }

        /// <summary>
        /// Search for playlists
        /// </summary>
        public async Task<List<QobuzPlaylist>> SearchPlaylistsAsync(string query, int limit = 25)
        {
            return await _searchService.SearchPlaylistsAsync(query, limit);
        }

        /// <summary>
        /// Search for labels
        /// </summary>
        public async Task<List<QobuzLabel>> SearchLabelsAsync(string query, int limit = 25)
        {
            // Simplified for CLI - labels search not fully implemented  
            return new List<QobuzLabel>();
        }

        /// <summary>
        /// Get album tracks
        /// </summary>
        public async Task<List<QobuzTrack>> GetAlbumTracksAsync(string albumId)
        {
            var album = await _searchService.GetAlbumAsync(albumId);
            return album?.GetTracks() ?? new List<QobuzTrack>();
        }

        /// <summary>
        /// Get artist details
        /// </summary>
        public async Task<QobuzArtist?> GetArtistAsync(string artistId)
        {
            return await _searchService.GetArtistAsync(artistId);
        }

        /// <summary>
        /// Get artist albums
        /// </summary>
        public async Task<List<QobuzAlbum>> GetArtistAlbumsAsync(string artistId, int limit = 25)
        {
            return await _searchService.GetArtistAlbumsAsync(artistId);
        }

        /// <summary>
        /// Get playlist details
        /// </summary>
        public async Task<QobuzPlaylist?> GetPlaylistAsync(string playlistId)
        {
            return await _searchService.GetPlaylistAsync(playlistId);
        }

        /// <summary>
        /// Get playlist tracks
        /// </summary>
        public async Task<List<QobuzTrack>> GetPlaylistTracksAsync(string playlistId)
        {
            var playlist = await _searchService.GetPlaylistAsync(playlistId);
            // Extract the actual QobuzTrack from QobuzPlaylistTrack.Track property
            return playlist?.Tracks?.Items?.Select(pt => pt.Track).ToList() ?? new List<QobuzTrack>();
        }

        /// <summary>
        /// Get label details (simplified)
        /// </summary>
        public async Task<QobuzLabel?> GetLabelAsync(string labelId)
        {
            // Simplified implementation for CLI
            return null;
        }

        /// <summary>
        /// Get label albums (simplified)
        /// </summary>
        public async Task<List<QobuzAlbum>> GetLabelAlbumsAsync(string labelId, int limit = 100)
        {
            // Simplified implementation for CLI
            return new List<QobuzAlbum>();
        }
    }
}