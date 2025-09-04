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
        private readonly QobuzStreamUrlService? _streamUrlService; // deprecated
        private readonly QobuzSearchService? _searchService; // deprecated
        private readonly CliQualityServiceAdapter _qualityService;
        private readonly CliQobuzValidationService _validationService;
        private readonly IQobuzLogger _logger;
        private readonly Lidarr.Plugin.Qobuzarr.API.IQobuzApiClient _apiClient;

        public CliApiService(
            QobuzStreamUrlService? streamUrlService,
            QobuzSearchService? searchService,
            CliQualityServiceAdapter qualityService,
            CliQobuzValidationService validationService,
            IQobuzLogger logger,
            Lidarr.Plugin.Qobuzarr.API.IQobuzApiClient apiClient)
        {
            _streamUrlService = streamUrlService;
            _searchService = searchService;
            _qualityService = qualityService ?? throw new ArgumentNullException(nameof(qualityService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Get stream URL for a track
        /// </summary>
        public async Task<QobuzStreamInfo?> GetStreamUrlAsync(string trackId, int formatId)
        {
            try
            {
                var url = await _apiClient.GetStreamingUrlAsync(trackId, formatId).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url)) return null;
                return new QobuzStreamInfo { Url = url, FormatId = formatId };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get streaming URL for track {trackId} with format {formatId}");
                return null;
            }
        }

        /// <summary>
        /// Search for albums
        /// </summary>
        public async Task<List<QobuzAlbum>> SearchAlbumsAsync(string query, int limit = 25)
        {
            // Try legacy first (most stable), then /catalog/search
            var legacyParams = new Dictionary<string, string> { { "query", query }, { "limit", limit.ToString() } };
            AddLocaleAndCountry(legacyParams);
            try
            {
                var legacy = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzAlbumSearchResponse>("/album/search", legacyParams).ConfigureAwait(false);
                if (legacy?.Albums?.Items != null && legacy.Albums.Items.Count > 0)
                {
                    return legacy.Albums.Items;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"album/search failed: {ex.Message}");
            }

            var parameters = new Dictionary<string, string>
            {
                { "query", query },
                { "type", "albums" },
                { "limit", limit.ToString() },
                { "offset", "0" }
            };
            AddLocaleAndCountry(parameters);
            try
            {
                var response = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzSearchResponse>("/catalog/search", parameters).ConfigureAwait(false);
                return response?.Albums?.Items ?? new List<QobuzAlbum>();
            }
            catch (Exception ex)
            {
                _logger.Error($"catalog/search(albums) failed: {ex.Message}");
                return new List<QobuzAlbum>();
            }
        }

        /// <summary>
        /// Search for tracks
        /// </summary>
        public async Task<List<QobuzTrack>> SearchTracksAsync(string query, int limit = 25)
        {
            var legacyParams = new Dictionary<string, string> { { "query", query }, { "limit", limit.ToString() } };
            AddLocaleAndCountry(legacyParams);
            try
            {
                var legacy = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzTrackSearchResponse>("/track/search", legacyParams).ConfigureAwait(false);
                if (legacy?.Tracks?.Items != null && legacy.Tracks.Items.Count > 0)
                {
                    return legacy.Tracks.Items;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"track/search failed: {ex.Message}");
            }

            var parameters = new Dictionary<string, string>
            {
                { "query", query },
                { "type", "tracks" },
                { "limit", limit.ToString() },
                { "offset", "0" }
            };
            AddLocaleAndCountry(parameters);
            try
            {
                var response = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzSearchResponse>("/catalog/search", parameters).ConfigureAwait(false);
                return response?.Tracks?.Items ?? new List<QobuzTrack>();
            }
            catch (Exception ex)
            {
                _logger.Error($"catalog/search(tracks) failed: {ex.Message}");
                return new List<QobuzTrack>();
            }
        }

        /// <summary>
        /// Get album details
        /// </summary>
        public async Task<QobuzAlbum?> GetAlbumAsync(string albumId)
        {
            var parameters = new Dictionary<string, string> { { "album_id", albumId } };
            AddLocaleAndCountry(parameters);
            AddLocaleAndCountry(parameters);
            try
            {
                return await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters).ConfigureAwait(false);
            }
            catch
            {
                if (_searchService != null)
                {
                    return await _searchService.GetAlbumAsync(albumId);
                }
                return null;
            }
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
            var parameters = new Dictionary<string, string>
            {
                { "query", query },
                { "type", "artists" },
                { "limit", limit.ToString() },
                { "offset", "0" }
            };
            AddLocaleAndCountry(parameters);
            try
            {
                var response = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzSearchResponse>("/catalog/search", parameters).ConfigureAwait(false);
                if (response?.Artists?.Items != null && response.Artists.Items.Count > 0)
                {
                    return response.Artists.Items;
                }
                // Fallback to legacy endpoint
                var legacyParams = new Dictionary<string, string> { { "query", query }, { "limit", limit.ToString() } };
                AddLocaleAndCountry(legacyParams);
                var legacy = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzArtistSearchResponse>("/artist/search", legacyParams).ConfigureAwait(false);
                return legacy?.Artists?.Items ?? new List<QobuzArtist>();
            }
            catch
            {
                return _searchService != null
                    ? await _searchService.SearchArtistsAsync(query, limit)
                    : new List<QobuzArtist>();
            }
        }

        /// <summary>
        /// Search for playlists
        /// </summary>
        public async Task<List<QobuzPlaylist>> SearchPlaylistsAsync(string query, int limit = 25)
        {
            var parameters = new Dictionary<string, string>
            {
                { "query", query },
                { "limit", limit.ToString() }
            };
            AddLocaleAndCountry(parameters);
            try
            {
                var response = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzPlaylistSearchResponse>("/playlist/search", parameters).ConfigureAwait(false);
                return response?.Playlists?.Items ?? new List<QobuzPlaylist>();
            }
            catch
            {
                return _searchService != null
                    ? await _searchService.SearchPlaylistsAsync(query, limit)
                    : new List<QobuzPlaylist>();
            }
        }

        /// <summary>
        /// Search for labels
        /// </summary>
        public async Task<List<QobuzLabel>> SearchLabelsAsync(string query, int limit = 25)
        {
            var parameters = new Dictionary<string, string>
            {
                { "query", query },
                { "limit", limit.ToString() }
            };
            AddLocaleAndCountry(parameters);
            try
            {
                var response = await _apiClient.GetAsync<Lidarr.Plugin.Qobuzarr.Models.QobuzLabelSearchResponse>("/label/search", parameters).ConfigureAwait(false);
                return response?.Labels?.Items ?? new List<QobuzLabel>();
            }
            catch
            {
                return new List<QobuzLabel>();
            }
        }

        /// <summary>
        /// Get album tracks
        /// </summary>
        public async Task<List<QobuzTrack>> GetAlbumTracksAsync(string albumId)
        {
            var album = await GetAlbumAsync(albumId).ConfigureAwait(false);
            return album?.GetTracks() ?? new List<QobuzTrack>();
        }

        private static void AddLocaleAndCountry(Dictionary<string, string> parameters)
        {
            var cc = Environment.GetEnvironmentVariable("QOBUZ_COUNTRY_CODE") ?? Environment.GetEnvironmentVariable("QOBUZ_COUNTRY") ?? "US";
            if (!string.IsNullOrWhiteSpace(cc)) parameters["country_code"] = cc;
            var locale = Environment.GetEnvironmentVariable("QOBUZ_LOCALE");
            if (!string.IsNullOrWhiteSpace(locale)) parameters["locale"] = locale;
        }

        /// <summary>
        /// Get artist details
        /// </summary>
        public async Task<QobuzArtist?> GetArtistAsync(string artistId)
        {
            return _searchService != null
                ? await _searchService.GetArtistAsync(artistId)
                : null;
        }

        /// <summary>
        /// Get artist albums
        /// </summary>
        public async Task<List<QobuzAlbum>> GetArtistAlbumsAsync(string artistId, int limit = 25)
        {
            return _searchService != null
                ? await _searchService.GetArtistAlbumsAsync(artistId)
                : new List<QobuzAlbum>();
        }

        /// <summary>
        /// Get playlist details
        /// </summary>
        public async Task<QobuzPlaylist?> GetPlaylistAsync(string playlistId)
        {
            var parameters = new Dictionary<string, string> { { "playlist_id", playlistId }, { "extra", "tracks" } };
            try
            {
                return await _apiClient.GetAsync<QobuzPlaylist>("/playlist/get", parameters).ConfigureAwait(false);
            }
            catch
            {
                return _searchService != null
                    ? await _searchService.GetPlaylistAsync(playlistId)
                    : null;
            }
        }

        /// <summary>
        /// Get playlist tracks
        /// </summary>
        public async Task<List<QobuzTrack>> GetPlaylistTracksAsync(string playlistId)
        {
            var playlist = await GetPlaylistAsync(playlistId).ConfigureAwait(false);
            return playlist?.Tracks?.Items?.Select(pt => pt.Track).ToList() ?? new List<QobuzTrack>();
        }

        /// <summary>
        /// Get label details (simplified)
        /// </summary>
        public Task<QobuzLabel?> GetLabelAsync(string labelId)
        {
            // Simplified implementation for CLI
            return Task.FromResult<QobuzLabel?>(null);
        }

        /// <summary>
        /// Get label albums (simplified)
        /// </summary>
        public Task<List<QobuzAlbum>> GetLabelAlbumsAsync(string labelId, int limit = 100)
        {
            // Simplified implementation for CLI
            return Task.FromResult(new List<QobuzAlbum>());
        }
    }
}
