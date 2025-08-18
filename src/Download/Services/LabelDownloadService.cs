using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NLog;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for downloading all albums from a record label
    /// </summary>
    public class LabelDownloadService
    {
        private readonly Logger _logger;
        private readonly QobuzApiClient _apiClient;
        private readonly QobuzTrackDownloader _trackDownloader;
        private readonly IQobuzCache _cache;

        public LabelDownloadService(
            QobuzApiClient apiClient,
            QobuzTrackDownloader trackDownloader,
            IQobuzCache cache,
            Logger logger = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _trackDownloader = trackDownloader ?? throw new ArgumentNullException(nameof(trackDownloader));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Download all albums from a label
        /// </summary>
        public async Task<LabelDownloadResult> DownloadLabelAsync(
            string labelId,
            string outputPath,
            int qualityId = 27, // Default to FLAC CD quality
            int maxAlbums = 100,
            bool skipExisting = true,
            IProgress<LabelDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new LabelDownloadResult
            {
                LabelId = labelId,
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.Info("Starting label download: {0}", labelId);

                // Fetch label metadata
                var label = await _apiClient.GetLabelAsync(labelId, cancellationToken);
                if (label == null)
                {
                    throw new InvalidOperationException($"Label {labelId} not found");
                }

                result.LabelName = label.Name;

                // Create label folder
                var labelFolder = Path.Combine(outputPath, "Labels", FileSystemUtilities.SanitizeFileName(label.Name));
                Directory.CreateDirectory(labelFolder);

                // Save label metadata
                await SaveLabelMetadataAsync(label, labelFolder);

                // Get all albums from label
                _logger.Info("Fetching albums from label: {0}", label.Name);
                var albums = await _apiClient.GetLabelAlbumsAsync(labelId, cancellationToken);
                
                // Limit to maxAlbums
                if (albums.Count > maxAlbums)
                {
                    _logger.Info("Limiting download to {0} albums out of {1} total", maxAlbums, albums.Count);
                    albums = albums.Take(maxAlbums).ToList();
                }

                result.TotalAlbums = albums.Count;

                var downloadedAlbums = new List<AlbumDownloadInfo>();
                int currentAlbumIndex = 0;

                foreach (var album in albums)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentAlbumIndex++;

                    var progressData = new LabelDownloadProgress
                    {
                        LabelName = label.Name,
                        TotalAlbums = albums.Count,
                        CurrentAlbumIndex = currentAlbumIndex,
                        CurrentAlbumName = album.GetFullTitle(),
                        CurrentArtistName = album.GetArtistName(),
                        OverallProgress = (double)currentAlbumIndex / albums.Count * 100
                    };
                    
                    progress?.Report(progressData);

                    try
                    {
                        // Create artist subfolder within label folder
                        var artistFolder = Path.Combine(labelFolder, FileSystemUtilities.SanitizeFileName(album.GetArtistName()));
                        Directory.CreateDirectory(artistFolder);

                        var albumFolder = Path.Combine(artistFolder, album.GetSafeFolderName());

                        // Check if album already exists
                        if (skipExisting && Directory.Exists(albumFolder))
                        {
                            var existingTracks = Directory.GetFiles(albumFolder, "*.flac", SearchOption.TopDirectoryOnly);
                            if (existingTracks.Length > 0)
                            {
                                _logger.Debug("Album already exists, skipping: {0}", album.GetFullTitle());
                                result.SkippedAlbums++;
                                
                                downloadedAlbums.Add(new AlbumDownloadInfo
                                {
                                    AlbumId = album.Id,
                                    AlbumName = album.GetFullTitle(),
                                    ArtistName = album.GetArtistName(),
                                    FolderPath = albumFolder,
                                    Skipped = true
                                });
                                continue;
                            }
                        }

                        // Download the album tracks
                        _logger.Info("Downloading album {0}/{1}: {2} - {3}", 
                            currentAlbumIndex, albums.Count, album.GetArtistName(), album.GetFullTitle());
                        
                        // Fetch complete album data with tracks
                        var fullAlbum = await _apiClient.GetAsync<QobuzAlbum>("album/get", 
                            new Dictionary<string, string> { ["album_id"] = album.Id });
                        
                        if (fullAlbum == null || fullAlbum.TracksContainer?.Items == null)
                        {
                            _logger.Warn("Could not fetch album details for: {0}", album.GetFullTitle());
                            result.FailedAlbums++;
                            result.FailedAlbumDetails.Add(new FailedAlbumInfo
                            {
                                AlbumId = album.Id,
                                AlbumName = album.GetFullTitle(),
                                ArtistName = album.GetArtistName(),
                                Error = "Could not fetch album details"
                            });
                            continue;
                        }

                        Directory.CreateDirectory(albumFolder);
                        var successfulTracks = 0;
                        var tracks = fullAlbum.GetTracks();

                        // Download each track
                        foreach (var track in tracks)
                        {
                            try
                            {
                                var trackFileName = FileSystemUtilities.SanitizeFileName(
                                    $"{track.TrackNumber:D2} - {track.GetFullTitle()}");
                                var trackPath = Path.Combine(albumFolder, trackFileName);

                                var downloadedPath = await _trackDownloader.DownloadTrackAsync(
                                    track,
                                    fullAlbum,
                                    trackPath,
                                    qualityId,
                                    null,
                                    cancellationToken);

                                if (!string.IsNullOrEmpty(downloadedPath))
                                {
                                    successfulTracks++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error downloading track {0}", track.GetFullTitle());
                            }
                        }

                        if (successfulTracks > 0)
                        {
                            result.SuccessfulAlbums++;
                            
                            downloadedAlbums.Add(new AlbumDownloadInfo
                            {
                                AlbumId = album.Id,
                                AlbumName = album.GetFullTitle(),
                                ArtistName = album.GetArtistName(),
                                FolderPath = albumFolder,
                                TrackCount = successfulTracks,
                                Quality = qualityId.ToString()
                            });
                        }
                        else
                        {
                            _logger.Warn("Failed to download any tracks for album: {0} - {1}", album.GetArtistName(), album.GetFullTitle());
                            result.FailedAlbums++;
                            result.FailedAlbumDetails.Add(new FailedAlbumInfo
                            {
                                AlbumId = album.Id,
                                AlbumName = album.GetFullTitle(),
                                ArtistName = album.GetArtistName(),
                                Error = "No tracks downloaded successfully"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error downloading album {0}", album.GetFullTitle());
                        result.FailedAlbums++;
                        result.FailedAlbumDetails.Add(new FailedAlbumInfo
                        {
                            AlbumId = album.Id,
                            AlbumName = album.GetFullTitle(),
                            ArtistName = album.GetArtistName(),
                            Error = ex.Message
                        });
                    }
                }

                result.DownloadedAlbums = downloadedAlbums;
                result.IsSuccessful = result.FailedAlbums == 0;
                result.EndTime = DateTime.UtcNow;

                _logger.Info("Label download completed: {0}/{1} albums successful, {2} skipped, {3} failed",
                    result.SuccessfulAlbums, result.TotalAlbums, result.SkippedAlbums, result.FailedAlbums);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download label {0}", labelId);
                result.IsSuccessful = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        private async Task SaveLabelMetadataAsync(QobuzLabel label, string outputFolder)
        {
            try
            {
                var metadata = new
                {
                    label.Id,
                    label.Name,
                    label.AlbumsCount,
                    label.SupplierId,
                    SavedAt = DateTime.UtcNow
                };

                var metadataPath = Path.Combine(outputFolder, "metadata.json");
                var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                await File.WriteAllTextAsync(metadataPath, json);
                
                _logger.Debug("Saved label metadata to {0}", metadataPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to save label metadata");
            }
        }
    }

    /// <summary>
    /// Result of a label download operation
    /// </summary>
    public class LabelDownloadResult
    {
        public string LabelId { get; set; }
        public string LabelName { get; set; }
        public bool IsSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        public int TotalAlbums { get; set; }
        public int SuccessfulAlbums { get; set; }
        public int SkippedAlbums { get; set; }
        public int FailedAlbums { get; set; }
        public List<AlbumDownloadInfo> DownloadedAlbums { get; set; } = new List<AlbumDownloadInfo>();
        public List<FailedAlbumInfo> FailedAlbumDetails { get; set; } = new List<FailedAlbumInfo>();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// Information about a downloaded album
    /// </summary>
    public class AlbumDownloadInfo
    {
        public string AlbumId { get; set; }
        public string AlbumName { get; set; }
        public string ArtistName { get; set; }
        public string FolderPath { get; set; }
        public int TrackCount { get; set; }
        public string Quality { get; set; }
        public bool Skipped { get; set; }
    }

    /// <summary>
    /// Information about a failed album download
    /// </summary>
    public class FailedAlbumInfo
    {
        public string AlbumId { get; set; }
        public string AlbumName { get; set; }
        public string ArtistName { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Progress information for label downloads
    /// </summary>
    public class LabelDownloadProgress
    {
        public string LabelName { get; set; }
        public int TotalAlbums { get; set; }
        public int CurrentAlbumIndex { get; set; }
        public string CurrentAlbumName { get; set; }
        public string CurrentArtistName { get; set; }
        public double OverallProgress { get; set; }
    }
}