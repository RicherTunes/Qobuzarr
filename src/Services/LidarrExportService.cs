using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Implementation of the Lidarr export service.
    /// Handles album export operations with support for multiple formats and optimization.
    /// </summary>
    public class LidarrExportService : ILidarrExportService
    {
        private readonly Logger _logger;
        private readonly string[] _suffixesToRemove = new[] 
        { 
            " (Deluxe Edition)", " (Remastered)", " (Expanded Edition)", 
            " (Special Edition)", " (Anniversary Edition)", " (Collector's Edition)"
        };

        public LidarrExportService(Logger logger)
        {
            _logger = Guard.NotNull(logger, nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<string> ExportAlbumsAsync(
            IEnumerable<LidarrAlbum> albums,
            ExportFormat format,
            bool includeMetadata = true,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(albums, nameof(albums));
            
            var albumList = albums.ToList();
            _logger.Info("Exporting {0} albums to {1} format", albumList.Count, format);

            var exportData = CreateExportData(albumList, includeMetadata);
            
            var result = format switch
            {
                ExportFormat.Json => await SerializeToJsonAsync(exportData, cancellationToken),
                ExportFormat.Csv => await SerializeToCsvAsync(exportData, cancellationToken),
                ExportFormat.Txt => await SerializeToTxtAsync(exportData, cancellationToken),
                _ => throw new ArgumentException($"Unsupported format: {format}", nameof(format))
            };

            _logger.Info("Export completed successfully");
            return result;
        }

        /// <inheritdoc/>
        public IEnumerable<LidarrAlbum> OptimizeAlbumOrder(IEnumerable<LidarrAlbum> albums)
        {
            Guard.NotNull(albums, nameof(albums));
            
            _logger.Debug("Optimizing album order for efficient processing");
            
            return albums.OrderBy(album =>
            {
                // Priority order:
                // 1. Release date (newer first - more likely to be available)
                // 2. Album type (albums before singles/EPs)
                // 3. Artist name (alphabetical for consistency)
                
                var releaseYear = album.ReleaseDate?.Year ?? 1900;
                var dateScore = 3000 - releaseYear; // Invert so newer = lower
                
                var albumType = album.AlbumType?.ToLowerInvariant() ?? "unknown";
                var typeScore = albumType switch
                {
                    "album" => 0,
                    "ep" => 100,
                    "single" => 200,
                    "broadcast" => 300,
                    _ => 400
                };
                
                var combinedScore = (dateScore * 1000) + typeScore;
                
                return (combinedScore, album.Artist?.ArtistName?.ToLowerInvariant() ?? "");
            });
        }

        /// <inheritdoc/>
        public Dictionary<string, object?> CreateAlbumExportData(LidarrAlbum album, bool includeMetadata)
        {
            Guard.NotNull(album, nameof(album));
            
            var artist = album.Artist;
            var artistName = artist?.ArtistName ?? "Unknown Artist";
            var albumTitle = album.Title ?? "Unknown Album";
            
            // Clean up common suffixes that might confuse search
            var albumTitleClean = CleanAlbumTitle(albumTitle);

            var basicData = new Dictionary<string, object?>
            {
                ["lidarr_id"] = album.Id,
                ["artist_name"] = artistName,
                ["artist_id"] = artist?.Id,
                ["album_title"] = albumTitle,
                ["album_title_clean"] = albumTitleClean,
                ["album_type"] = album.AlbumType?.ToLowerInvariant() ?? "album",
                ["release_date"] = album.ReleaseDate?.ToString("yyyy-MM-dd"),
                ["release_year"] = album.ReleaseDate?.Year.ToString(),
                ["track_count"] = album.Statistics?.TrackCount ?? 0,
                ["monitored"] = album.Monitored,
                ["search_query"] = $"\"{artistName}\" \"{albumTitleClean}\"",
            };

            if (includeMetadata)
            {
                basicData["disambiguation"] = album.Disambiguation;
                basicData["foreign_album_id"] = album.ForeignAlbumId;
                basicData["genres"] = album.Genres?.ToList() ?? new List<string>();
                basicData["ratings"] = album.Ratings;
                basicData["overview"] = album.Overview;
                basicData["album_id"] = album.Id;
                basicData["artist_metadata_id"] = artist?.ArtistMetadataId;
            }

            return basicData;
        }

        private Dictionary<string, object> CreateExportData(List<LidarrAlbum> albums, bool includeMetadata)
        {
            return new Dictionary<string, object>
            {
                ["version"] = "1.0",
                ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["source"] = "lidarr",
                ["total_albums"] = albums.Count,
                ["albums"] = albums.Select(album => CreateAlbumExportData(album, includeMetadata)).ToList()
            };
        }

        private string CleanAlbumTitle(string albumTitle)
        {
            var cleanTitle = albumTitle;
            foreach (var suffix in _suffixesToRemove)
            {
                cleanTitle = cleanTitle.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
            }
            return cleanTitle.Trim();
        }

        private async Task<string> SerializeToJsonAsync(Dictionary<string, object> exportData, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // Async for consistency
            return JsonConvert.SerializeObject(exportData, Formatting.Indented);
        }

        private async Task<string> SerializeToCsvAsync(Dictionary<string, object> exportData, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine("search_query,artist_name,album_title,album_type,release_year,track_count,lidarr_id");

            if (exportData["albums"] is List<object> albums)
            {
                foreach (var albumObj in albums)
                {
                    if (albumObj is Dictionary<string, object?> album)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        sb.AppendLine(string.Join(",", new[]
                        {
                            EscapeCsvField(album["search_query"]?.ToString() ?? ""),
                            EscapeCsvField(album["artist_name"]?.ToString() ?? ""),
                            EscapeCsvField(album["album_title"]?.ToString() ?? ""),
                            EscapeCsvField(album["album_type"]?.ToString() ?? ""),
                            EscapeCsvField(album["release_year"]?.ToString() ?? ""),
                            EscapeCsvField(album["track_count"]?.ToString() ?? "0"),
                            EscapeCsvField(album["lidarr_id"]?.ToString() ?? "")
                        }));
                    }
                }
            }

            await Task.CompletedTask;
            return sb.ToString();
        }

        private async Task<string> SerializeToTxtAsync(Dictionary<string, object> exportData, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Lidarr Wanted Albums Export");
            sb.AppendLine($"# Created: {exportData["created_at"]}");
            sb.AppendLine($"# Total albums: {exportData["total_albums"]}");
            sb.AppendLine("#");
            sb.AppendLine("# Format: search_query | artist | album | type | year");
            sb.AppendLine("#" + new string('=', 70));
            sb.AppendLine();

            if (exportData["albums"] is List<object> albums)
            {
                foreach (var albumObj in albums)
                {
                    if (albumObj is Dictionary<string, object?> album)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        sb.AppendLine($"{album["search_query"]} | " +
                                     $"{album["artist_name"]} | " +
                                     $"{album["album_title"]} | " +
                                     $"{album["album_type"]} | " +
                                     $"{album["release_year"] ?? "Unknown"}");
                    }
                }
            }

            await Task.CompletedTask;
            return sb.ToString();
        }

        private string EscapeCsvField(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }
    }
}