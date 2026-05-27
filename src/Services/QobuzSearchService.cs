using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for searching albums, tracks, and artists on Qobuz
    /// </summary>
    public class QobuzSearchService
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly IQobuzAuthenticationService _authService;
        private readonly Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexerSettings? _settings;
        private const string API_BASE = "https://www.qobuz.com/api.json/0.2";

        public QobuzSearchService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            IQobuzAuthenticationService authService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _settings = null;
        }

        // Overload to accept settings for locale/country threading
        public QobuzSearchService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            IQobuzAuthenticationService authService,
            Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexerSettings settings)
            : this(httpClient, logger, authService)
        {
            _settings = settings;
        }

        /// <summary>
        /// Search for playlists on Qobuz
        /// </summary>
        public async Task<List<QobuzPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
        {
            // Use minimal normalization; rely on URL encoding when building requests
            query = (query ?? string.Empty).Trim();

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var url = $"{API_BASE}/playlist/search?query={Uri.EscapeDataString(query)}&limit={limit}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzPlaylistSearchResponse>(url).ConfigureAwait(false);
                return response?.Playlists?.Items ?? new List<QobuzPlaylist>();
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"Network error during playlist search for '{query}': {ex.Message}");
                throw new QobuzSearchException($"Network error searching playlists: {ex.Message}", ex, SearchType.Playlist);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warn($"Playlist search timeout for '{query}'");
                throw new QobuzSearchException($"Search timeout for playlists", ex, SearchType.Playlist);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during playlist search for '{query}': {ex.Message}");
                throw new QobuzSearchException($"Failed to search playlists: {ex.Message}", ex, SearchType.Playlist);
            }
        }

        /// <summary>
        /// Get playlist details by ID
        /// </summary>
        public async Task<QobuzPlaylist> GetPlaylistAsync(string playlistId)
        {
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var url = $"{API_BASE}/playlist/get?playlist_id={playlistId}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}&extra=tracks";

            try
            {
                var playlist = await _httpClient.GetJsonAsync<QobuzPlaylist>(url).ConfigureAwait(false);
                if (playlist == null)
                {
                    throw new QobuzSearchException($"Playlist {playlistId} not found", null, SearchType.Playlist);
                }
                return playlist;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"Network error getting playlist {playlistId}: {ex.Message}");
                throw new QobuzSearchException($"Network error getting playlist: {ex.Message}", ex, SearchType.Playlist);
            }
            catch (Exception ex) when (!(ex is QobuzSearchException))
            {
                _logger.Error($"Failed to get playlist {playlistId}: {ex.Message}");
                throw new QobuzSearchException($"Failed to get playlist details: {ex.Message}", ex, SearchType.Playlist);
            }
        }

        /// <summary>
        /// Get all tracks from a playlist
        /// </summary>
        public async Task<List<QobuzTrack>> GetPlaylistTracksAsync(string playlistId)
        {
            var allTracks = new List<QobuzTrack>();
            const int pageSize = 500;
            int offset = 0;

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            while (true)
            {
                var url = $"{API_BASE}/playlist/get?playlist_id={playlistId}" +
                         $"&app_id={session.AppId}&user_auth_token={session.AuthToken}" +
                         $"&extra=tracks&limit={pageSize}&offset={offset}";

                try
                {
                    var playlist = await _httpClient.GetJsonAsync<QobuzPlaylist>(url).ConfigureAwait(false);

                    if (playlist?.Tracks?.Items == null || playlist.Tracks.Items.Count == 0)
                        break;

                    // Extract tracks from playlist track items
                    foreach (var item in playlist.Tracks.Items)
                    {
                        if (item.Track != null)
                            allTracks.Add(item.Track);
                    }

                    offset += pageSize;

                    // Check if we've fetched all tracks
                    if (allTracks.Count >= playlist.Tracks.Total)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get playlist tracks page at offset {offset} for {playlistId}: {ex.Message}");
                    // Continue with partial results rather than failing completely
                    if (allTracks.Count == 0)
                    {
                        throw new QobuzSearchException($"Failed to get any playlist tracks: {ex.Message}", ex, SearchType.Playlist);
                    }
                    break;
                }
            }

            return allTracks;
        }

        /// <summary>
        /// Search for labels on Qobuz
        /// </summary>
        public async Task<List<QobuzLabel>> SearchLabelsAsync(string query, int limit = 20)
        {
            // Use minimal normalization; rely on URL encoding when building requests
            query = (query ?? string.Empty).Trim();

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var url = $"{API_BASE}/label/search?query={Uri.EscapeDataString(query)}&limit={limit}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzLabelSearchResponse>(url).ConfigureAwait(false);
                return response?.Labels?.Items ?? new List<QobuzLabel>();
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"Network error during label search for '{query}': {ex.Message}");
                throw new QobuzSearchException($"Network error searching labels: {ex.Message}", ex, SearchType.Label, isRetryable: true, query: query);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warn($"Label search timeout for '{query}'");
                throw new QobuzSearchException($"Search timeout for labels", ex, SearchType.Label, isRetryable: true, query: query);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during label search for '{query}': {ex.Message}");
                throw new QobuzSearchException($"Failed to search labels: {ex.Message}", ex, SearchType.Label, query: query);
            }
        }

        /// <summary>
        /// Get label details by ID
        /// </summary>
        public async Task<QobuzLabel> GetLabelAsync(string labelId)
        {
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var url = $"{API_BASE}/label/get?label_id={labelId}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}";

            try
            {
                return await _httpClient.GetJsonAsync<QobuzLabel>(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get label {labelId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all albums from a label
        /// </summary>
        public async Task<List<QobuzAlbum>> GetLabelAlbumsAsync(string labelId)
        {
            var allAlbums = new List<QobuzAlbum>();
            const int pageSize = 500;
            int offset = 0;

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            while (true)
            {
                var url = $"{API_BASE}/label/getAlbums?label_id={labelId}" +
                         $"&app_id={session.AppId}&user_auth_token={session.AuthToken}" +
                         $"&limit={pageSize}&offset={offset}";

                try
                {
                    var response = await _httpClient.GetJsonAsync<QobuzAlbumSearchResponse>(url).ConfigureAwait(false);

                    if (response?.Albums?.Items == null || response.Albums.Items.Count == 0)
                        break;

                    allAlbums.AddRange(response.Albums.Items);
                    offset += pageSize;

                    // Check if we've fetched all albums
                    if (allAlbums.Count >= response.Albums.Total)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get label albums for {labelId}: {ex.Message}");
                    break;
                }
            }

            return allAlbums;
        }

        /// <summary>
        /// Get artist details by ID
        /// </summary>
        public async Task<QobuzArtist> GetArtistAsync(string artistId)
        {
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var url = $"{API_BASE}/artist/get?artist_id={artistId}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}";

            try
            {
                return await _httpClient.GetJsonAsync<QobuzArtist>(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get artist {artistId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get all albums from an artist
        /// </summary>
        public async Task<List<QobuzAlbum>> GetArtistAlbumsAsync(string artistId)
        {
            var allAlbums = new List<QobuzAlbum>();
            const int pageSize = 500;
            int offset = 0;

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            while (true)
            {
                var url = $"{API_BASE}/artist/getAlbums?artist_id={artistId}" +
                         $"&app_id={session.AppId}&user_auth_token={session.AuthToken}" +
                         $"&limit={pageSize}&offset={offset}";

                try
                {
                    var response = await _httpClient.GetJsonAsync<QobuzAlbumSearchResponse>(url).ConfigureAwait(false);

                    if (response?.Albums?.Items == null || response.Albums.Items.Count == 0)
                        break;

                    allAlbums.AddRange(response.Albums.Items);
                    offset += pageSize;

                    // Check if we've fetched all albums
                    if (allAlbums.Count >= response.Albums.Total)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get artist albums for {artistId}: {ex.Message}");
                    break;
                }
            }

            return allAlbums;
        }

        /// <summary>
        /// Search for albums on Qobuz
        /// </summary>
        public async Task<List<QobuzAlbum>> SearchAlbumsAsync(string query, int limit = 20)
        {
            // Sanitize the search query
            query = InputSanitizer.SanitizeSearchQuery(query);

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var ccA = _settings?.GetCountryCode() ?? "US";
            var localeParamA = string.IsNullOrWhiteSpace(_settings?.Locale) ? string.Empty : $"&locale={Uri.EscapeDataString(_settings.Locale)}";
            var url = $"{API_BASE}/album/search?query={Uri.EscapeDataString(query)}&limit={limit}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}&country_code={ccA}{localeParamA}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzSearchResponse>(url).ConfigureAwait(false);
                return response?.Albums?.Items ?? new List<QobuzAlbum>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Album search failed for query: {0}", query);
                return new List<QobuzAlbum>();
            }
        }

        /// <summary>
        /// Search for tracks on Qobuz
        /// </summary>
        public async Task<List<QobuzTrack>> SearchTracksAsync(string query, int limit = 20)
        {
            // Sanitize the search query
            query = InputSanitizer.SanitizeSearchQuery(query);

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var ccT = _settings?.GetCountryCode() ?? "US";
            var localeParamT = string.IsNullOrWhiteSpace(_settings?.Locale) ? string.Empty : $"&locale={Uri.EscapeDataString(_settings.Locale)}";
            var url = $"{API_BASE}/track/search?query={Uri.EscapeDataString(query)}&limit={limit}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}&country_code={ccT}{localeParamT}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzSearchResponse>(url).ConfigureAwait(false);
                return response?.Tracks?.Items ?? new List<QobuzTrack>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Track search failed for query: {0}", query);
                return new List<QobuzTrack>();
            }
        }

        /// <summary>
        /// Search for artists on Qobuz
        /// </summary>
        public async Task<List<QobuzArtist>> SearchArtistsAsync(string query, int limit = 20)
        {
            // Sanitize the search query
            query = InputSanitizer.SanitizeSearchQuery(query);

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var cc = _settings?.GetCountryCode() ?? "US";
            var localeParam = string.IsNullOrWhiteSpace(_settings?.Locale) ? string.Empty : $"&locale={Uri.EscapeDataString(_settings.Locale)}";
            var url = $"{API_BASE}/artist/search?query={Uri.EscapeDataString(query)}&limit={limit}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}&country_code={cc}{localeParam}";

            try
            {
                var response = await _httpClient.GetJsonAsync<QobuzArtistSearchResponse>(url).ConfigureAwait(false);
                return response?.Artists?.Items ?? new List<QobuzArtist>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Artist search failed for query: {0}", query);
                return new List<QobuzArtist>();
            }
        }

        /// <summary>
        /// Get album details by ID
        /// </summary>
        public async Task<QobuzAlbum?> GetAlbumAsync(string albumId)
        {
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var cc = _settings?.GetCountryCode() ?? "US";
            var localeParam = string.IsNullOrWhiteSpace(_settings?.Locale) ? string.Empty : $"&locale={Uri.EscapeDataString(_settings.Locale)}";
            var url = $"{API_BASE}/album/get?album_id={albumId}" +
                     $"&app_id={session.AppId}&user_auth_token={session.AuthToken}&country_code={cc}{localeParam}";

            try
            {
                return await _httpClient.GetJsonAsync<QobuzAlbum>(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get album {0}", albumId);
                return null;
            }
        }

        /// <summary>
        /// Get tracks for an album
        /// </summary>
        public async Task<List<QobuzTrack>> GetAlbumTracksAsync(string albumId)
        {
            var album = await GetAlbumAsync(albumId).ConfigureAwait(false);
            return album?.TracksContainer?.Items ?? new List<QobuzTrack>();
        }

        /// <summary>
        /// Clean search query by removing venue and date information
        /// </summary>
        public string CleanSearchQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var cleaned = query;

            // Remove venue/location information patterns
            cleaned = Regex.Replace(cleaned,
                @"\s*\(live\s+at\s+[^,)]+,\s*[^)]+\)\s*", " ",
                RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(cleaned,
                @"\s*\(live\s+at\s+[^)]+\)\s*", " ",
                RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(cleaned,
                @"\s*-\s*live\s+at\s+.+$", "",
                RegexOptions.IgnoreCase);

            // Remove recording/performance date patterns
            cleaned = Regex.Replace(cleaned,
                @"\s*\(recorded\s+[^)]+\)\s*", " ",
                RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(cleaned,
                @"\s*\(live\s+[^)]+\)\s*", " ",
                RegexOptions.IgnoreCase);

            // Remove year patterns
            cleaned = Regex.Replace(cleaned,
                @"\s*[\(\[]?\d{1,2}[\./]\d{1,2}[\./]\d{4}[\)\]]?\s*", " ");

            cleaned = Regex.Replace(cleaned,
                @"\s*\d{1,2}\.\d{1,2}\.\d{4}\s*", " ");

            cleaned = Regex.Replace(cleaned,
                @"\s*[\(\[]?\d{4}[\)\]]?\s*", " ");

            // Remove explicit version/performance indicators
            var performancePatterns = new[]
            {
                @"\s*\(live\s*version\)\s*",
                @"\s*\(acoustic\s*version\)\s*",
                @"\s*\(studio\s*version\)\s*",
                @"\s*\(radio\s*edit\)\s*",
                @"\s*\(single\s*version\)\s*",
                @"\s*\(demo\)\s*",
                @"\s*\(rehearsal\)\s*",
                @"\s*\(performance\)\s*"
            };

            foreach (var pattern in performancePatterns)
            {
                cleaned = Regex.Replace(cleaned, pattern, " ", RegexOptions.IgnoreCase);
            }

            // Clean up multiple spaces and trim
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            _logger.Debug("Query cleaning: '{0}' -> '{1}'", query, cleaned);
            return cleaned;
        }

        /// <summary>
        /// Generate multiple search variations with progressive query cleaning
        /// </summary>
        public List<string> GenerateSearchVariations(string originalQuery)
        {
            var variations = new List<string>();

            // 1. Original query (unchanged)
            if (!string.IsNullOrWhiteSpace(originalQuery))
            {
                variations.Add(originalQuery);
            }

            // 2. Cleaned query (remove venue/date info)
            var cleaned = CleanSearchQuery(originalQuery);
            if (!string.IsNullOrWhiteSpace(cleaned) && cleaned != originalQuery)
            {
                variations.Add(cleaned);
            }

            // 3. Further simplified (remove extra descriptors)
            var simplified = SimplifyQuery(cleaned);
            if (!string.IsNullOrWhiteSpace(simplified) && simplified != cleaned)
            {
                variations.Add(simplified);
            }

            // Remove duplicates while preserving order
            return variations.Distinct().ToList();
        }

        private string SimplifyQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var simplified = query;

            // Remove featuring information more aggressively
            simplified = Regex.Replace(simplified,
                @"\s+(feat\.?|ft\.?|featuring|with)\s+.+$", "",
                RegexOptions.IgnoreCase);

            // Remove remix/version information
            simplified = Regex.Replace(simplified,
                @"\s*\([^)]*remix[^)]*\)\s*", " ",
                RegexOptions.IgnoreCase);

            simplified = Regex.Replace(simplified,
                @"\s*\([^)]*version[^)]*\)\s*", " ",
                RegexOptions.IgnoreCase);

            // Clean up and trim
            simplified = Regex.Replace(simplified, @"\s+", " ").Trim();

            return simplified;
        }
    }

    /// <summary>
    /// Qobuz search response structure
    /// </summary>
    public class QobuzSearchResponse
    {
        public QobuzPagedResult<QobuzAlbum>? Albums { get; set; }
        public QobuzPagedResult<QobuzTrack>? Tracks { get; set; }
    }

    /// <summary>
    /// Qobuz artist search response structure
    /// </summary>
    public class QobuzArtistSearchResponse
    {
        public QobuzPagedResult<QobuzArtist>? Artists { get; set; }
    }

    /// <summary>
    /// Paged result container for Qobuz API responses
    /// </summary>
    public class QobuzPagedResult<T>
    {
        public List<T>? Items { get; set; }
        public int Total { get; set; }
    }
}
