using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Models;
using Newtonsoft.Json.Linq;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    /// <summary>
    /// Minimal Qobuz API shim using plain HttpClient and auth token from settings.
    /// Only implements the calls needed by the download orchestrator.
    /// </summary>
    internal sealed class QobuzApiShim
    {
        private readonly HttpClient _http;
        private readonly string _appId;
        private readonly string _userId;
        private readonly string _token;
        private readonly string _countryCode;
        private readonly string? _locale;

        public QobuzApiShim(HttpClient http, string appId, string userId, string token, string countryCode = "US", string? locale = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _appId = string.IsNullOrWhiteSpace(appId) ? QobuzConstants.Api.DefaultAppId : appId;
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _countryCode = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode;
            _locale = string.IsNullOrWhiteSpace(locale) ? null : locale;
        }

        public async Task<StreamingAlbum> GetAlbumAsync(string albumId)
        {
            var json = await _http.GetStringAsync(BuildUrl($"/album/get?album_id={Uri.EscapeDataString(albumId)}"));
            var album = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzAlbum>(json) ?? new QobuzAlbum { Id = albumId, Title = "Unknown" };
            return MapAlbum(album);
        }

        public async Task<StreamingTrack> GetTrackAsync(string trackId)
        {
            var json = await _http.GetStringAsync(BuildUrl($"/track/get?track_id={Uri.EscapeDataString(trackId)}"));
            var track = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzTrack>(json) ?? new QobuzTrack { Id = trackId, Title = "Unknown" };
            return MapTrack(track);
        }

        public async Task<IReadOnlyList<StreamingAlbum>> SearchAlbumsAsync(string query, int limit = 50)
        {
            var json = await _http.GetStringAsync(BuildUrl($"/album/search?query={Uri.EscapeDataString(query)}&limit={limit}"));
            var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzSearchResponse>(json);
            if (resp?.Albums?.Items == null || resp.Albums.Items.Count == 0)
            {
                return Array.Empty<StreamingAlbum>();
            }
            return resp.Albums.Items.Select(MapAlbum).ToList();
        }

        public async Task<IReadOnlyList<StreamingTrack>> SearchTracksAsync(string query, int limit = 50)
        {
            var json = await _http.GetStringAsync(BuildUrl($"/track/search?query={Uri.EscapeDataString(query)}&limit={limit}"));
            var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzSearchResponse>(json);
            if (resp?.Tracks?.Items == null || resp.Tracks.Items.Count == 0)
            {
                return Array.Empty<StreamingTrack>();
            }
            return resp.Tracks.Items.Select(MapTrack).ToList();
        }

        public async Task<IReadOnlyList<string>> GetAlbumTrackIdsAsync(string albumId)
        {
            var json = await _http.GetStringAsync(BuildUrl($"/album/get?album_id={Uri.EscapeDataString(albumId)}&extra=tracks"));
            var album = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzAlbum>(json);
            if (album?.TracksContainer?.Items == null || album.TracksContainer.Items.Count == 0)
            {
                return Array.Empty<string>();
            }
            return album.TracksContainer.Items.Where(t => !string.IsNullOrWhiteSpace(t.Id)).Select(t => t.Id).ToList();
        }

        public async Task<(string Url, string Extension)> GetStreamAsync(string trackId, int formatId)
        {
            // track/getFileUrl returns { url: "..." }
            var endpoint = $"/track/getFileUrl?track_id={Uri.EscapeDataString(trackId)}&format_id={formatId}&intent=stream";
            var json = await _http.GetStringAsync(BuildUrl(endpoint));
            var obj = JObject.Parse(json);
            var url = obj["url"]?.ToString() ?? string.Empty;
            var ext = formatId == QobuzPluginConstants.QualityFormats.Mp3320 ? ".mp3" : ".flac";
            return (url, ext);
        }

        private string BuildUrl(string pathAndQuery)
        {
            var locale = _locale != null ? $"&locale={Uri.EscapeDataString(_locale)}" : string.Empty;
            // Include both app_id and user_auth_token on every request
            return $"{QobuzConstants.Api.BaseUrl}{pathAndQuery}&app_id={_appId}&user_auth_token={_token}&user_id={_userId}&country_code={_countryCode}{locale}";
        }

        private static StreamingAlbum MapAlbum(QobuzAlbum a)
        {
            var album = new StreamingAlbum
            {
                Id = a.Id ?? string.Empty,
                Title = a.GetFullTitle(),
                Artist = new StreamingArtist { Id = a.Artist?.Id ?? string.Empty, Name = a.Artist?.Name ?? "Various Artists" },
                TrackCount = a.TracksCount > 0 ? a.TracksCount : (a.TracksContainer?.Items?.Count ?? 0),
                Label = a.Label?.Name ?? string.Empty,
                Upc = a.UPC ?? string.Empty,
                ReleaseDate = a.ReleaseDate == default ? (DateTime?)null : a.ReleaseDate,
                Genres = a.GenresList ?? new List<string>()
            };
            return album;
        }

        private static StreamingTrack MapTrack(QobuzTrack t)
        {
            var st = new StreamingTrack
            {
                Id = t.Id ?? string.Empty,
                Title = t.GetFullTitle(),
                TrackNumber = t.TrackNumber,
                DiscNumber = t.DiscNumber,
                Duration = t.Duration,
                Album = t.Album != null ? MapAlbum(t.Album) : new StreamingAlbum { Id = t.Album?.Id ?? string.Empty, Title = t.Album?.Title ?? string.Empty, Artist = new StreamingArtist { Id = t.Album?.Artist?.Id ?? string.Empty, Name = t.Album?.Artist?.Name ?? "" } },
                Artist = new StreamingArtist { Id = t.Album?.Artist?.Id ?? string.Empty, Name = t.AlbumArtistName ?? "Various Artists" },
                Isrc = t.ISRC ?? string.Empty,
                AvailableQualities = new List<StreamingQuality>
                {
                    new StreamingQuality { Id = "5", Name = "MP3 320kbps", Format = "MP3", Bitrate = 320 },
                    new StreamingQuality { Id = "6", Name = "FLAC CD 16/44.1", Format = "FLAC", BitDepth = 16, SampleRate = 44100 },
                    new StreamingQuality { Id = "7", Name = "FLAC Hi-Res 24/96", Format = "FLAC", BitDepth = 24, SampleRate = 96000 },
                }
            };
            return st;
        }
    }
}
