using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NLog;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for downloading Qobuz playlists
    /// </summary>
    public class PlaylistDownloadService
    {
        private readonly Logger _logger;
        private readonly QobuzApiClient _apiClient;
        private readonly QobuzTrackDownloader _trackDownloader;
        private readonly IQobuzCache _cache;

        public PlaylistDownloadService(
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
        /// Download a complete playlist
        /// </summary>
        public async Task<PlaylistDownloadResult> DownloadPlaylistAsync(
            string playlistId,
            string outputPath,
            int qualityId = 27, // Default to FLAC CD quality
            bool createM3u8 = true,
            bool skipExisting = true,
            IProgress<PlaylistDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new PlaylistDownloadResult
            {
                PlaylistId = playlistId,
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.Info("Starting playlist download: {0}", playlistId);

                // Fetch playlist metadata
                var playlist = await _apiClient.GetPlaylistAsync(playlistId, cancellationToken: cancellationToken);
                if (playlist == null)
                {
                    throw new InvalidOperationException($"Playlist {playlistId} not found");
                }

                result.PlaylistName = playlist.Name;
                result.TotalTracks = playlist.TracksCount;

                // Create playlist folder
                var playlistFolder = Path.Combine(outputPath, "Playlists", FileSystemUtilities.SanitizeFileName(playlist.Name));
                Directory.CreateDirectory(playlistFolder);

                // Save playlist metadata
                await SavePlaylistMetadataAsync(playlist, playlistFolder);

                // Get all tracks from playlist
                _logger.Info("Fetching {0} tracks from playlist: {1}", playlist.TracksCount, playlist.Name);
                var tracks = await _apiClient.GetPlaylistTracksAsync(playlistId, cancellationToken);

                var downloadedTracks = new List<TrackDownloadInfo>();
                var m3u8Lines = new List<string>();
                m3u8Lines.Add("#EXTM3U");
                m3u8Lines.Add($"#PLAYLIST:{playlist.Name}");
                
                int currentTrackIndex = 0;

                foreach (var track in tracks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentTrackIndex++;

                    var progressData = new PlaylistDownloadProgress
                    {
                        PlaylistName = playlist.Name,
                        TotalTracks = tracks.Count,
                        CurrentTrackIndex = currentTrackIndex,
                        CurrentTrackName = track.GetFullTitle(),
                        OverallProgress = (double)currentTrackIndex / tracks.Count * 100
                    };
                    
                    progress?.Report(progressData);

                    try
                    {
                        // Generate filename with position prefix
                        var position = currentTrackIndex.ToString("D3");
                        var artist = track.Performer?.Name ?? track.Album?.Artist?.Name ?? "Unknown Artist";
                        var trackTitle = track.GetFullTitle();
                        var fileName = FileSystemUtilities.SanitizeFileName($"{position} - {artist} - {trackTitle}");
                        var filePath = Path.Combine(playlistFolder, fileName);

                        // Check if track already exists
                        if (skipExisting && File.Exists(filePath + ".flac"))
                        {
                            _logger.Debug("Track already exists, skipping: {0}", fileName);
                            result.SkippedTracks++;
                            
                            // Add to M3U8 even if skipped
                            if (createM3u8)
                            {
                                m3u8Lines.Add($"#EXTINF:{track.Duration},{artist} - {trackTitle}");
                                m3u8Lines.Add($"{fileName}.flac");
                            }
                            
                            downloadedTracks.Add(new TrackDownloadInfo
                            {
                                TrackId = track.Id,
                                FilePath = filePath + ".flac",
                                Position = currentTrackIndex,
                                Skipped = true
                            });
                            continue;
                        }

                        // Download the track
                        _logger.Info("Downloading track {0}/{1}: {2}", currentTrackIndex, tracks.Count, trackTitle);
                        
                        // Need to fetch album info for metadata
                        var album = track.Album;
                        if (album == null && !string.IsNullOrEmpty(track.Album?.Id))
                        {
                            album = await _apiClient.GetAsync<QobuzAlbum>("album/get", 
                                new Dictionary<string, string> { ["album_id"] = track.Album.Id });
                        }
                        
                        // Create a nested progress reporter for individual track progress
                        IProgress<double> trackProgress = null;
                        if (progress != null)
                        {
                            trackProgress = new Progress<double>(trackPercentage =>
                            {
                                // Report combined progress: track position + download percentage
                                var overallPercentage = ((currentTrackIndex - 1) + trackPercentage) / playlist.TracksCount;
                                
                                progress.Report(new PlaylistDownloadProgress
                                {
                                    CurrentTrack = currentTrackIndex,
                                    TotalTracks = playlist.TracksCount,
                                    CurrentTrackName = $"{artist} - {trackTitle}",
                                    CurrentTrackProgress = (int)(trackPercentage * 100),
                                    OverallProgress = (int)(overallPercentage * 100),
                                    Message = $"Downloading track {currentTrackIndex}/{playlist.TracksCount}: {trackTitle}"
                                });
                            });
                        }

                        var downloadedPath = await _trackDownloader.DownloadTrackAsync(
                            track,
                            album,
                            filePath,
                            qualityId,
                            trackProgress,
                            cancellationToken);

                        if (!string.IsNullOrEmpty(downloadedPath))
                        {
                            result.SuccessfulTracks++;
                            
                            // Add to M3U8
                            if (createM3u8)
                            {
                                m3u8Lines.Add($"#EXTINF:{track.Duration},{artist} - {trackTitle}");
                                m3u8Lines.Add($"{fileName}.flac");
                            }
                            
                            downloadedTracks.Add(new TrackDownloadInfo
                            {
                                TrackId = track.Id,
                                FilePath = downloadedPath,
                                Position = currentTrackIndex,
                                Quality = qualityId.ToString()
                            });
                        }
                        else
                        {
                            _logger.Warn("Failed to download track: {0}", trackTitle);
                            result.FailedTracks++;
                            result.FailedTrackDetails.Add(new FailedTrackInfo
                            {
                                TrackId = track.Id,
                                TrackName = trackTitle,
                                Error = "Download failed"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error downloading track {0}", track.GetFullTitle());
                        result.FailedTracks++;
                        result.FailedTrackDetails.Add(new FailedTrackInfo
                        {
                            TrackId = track.Id,
                            TrackName = track.GetFullTitle(),
                            Error = ex.Message
                        });
                    }
                }

                // Save M3U8 playlist file
                if (createM3u8 && m3u8Lines.Count > 1)
                {
                    var m3u8Path = Path.Combine(playlistFolder, $"{FileSystemUtilities.SanitizeFileName(playlist.Name)}.m3u8");
                    await File.WriteAllLinesAsync(m3u8Path, m3u8Lines, Encoding.UTF8, cancellationToken);
                    result.M3u8FilePath = m3u8Path;
                    _logger.Info("Created M3U8 playlist file: {0}", m3u8Path);
                }

                result.DownloadedTracks = downloadedTracks;
                result.IsSuccessful = result.FailedTracks == 0;
                result.EndTime = DateTime.UtcNow;

                _logger.Info("Playlist download completed: {0}/{1} tracks successful, {2} skipped, {3} failed",
                    result.SuccessfulTracks, result.TotalTracks, result.SkippedTracks, result.FailedTracks);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download playlist {0}", playlistId);
                result.IsSuccessful = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        private async Task SavePlaylistMetadataAsync(QobuzPlaylist playlist, string outputFolder)
        {
            try
            {
                var metadata = new
                {
                    playlist.Id,
                    playlist.Name,
                    playlist.Description,
                    playlist.TracksCount,
                    playlist.Duration,
                    playlist.IsPublic,
                    playlist.CreatedAt,
                    playlist.UpdatedAt,
                    Owner = playlist.Owner?.GetDisplayName(),
                    ImageUrl = playlist.GetImageUrl(),
                    SavedAt = DateTime.UtcNow
                };

                var metadataPath = Path.Combine(outputFolder, "metadata.json");
                var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                await File.WriteAllTextAsync(metadataPath, json);
                
                _logger.Debug("Saved playlist metadata to {0}", metadataPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to save playlist metadata");
            }
        }
    }

    /// <summary>
    /// Result of a playlist download operation
    /// </summary>
    public class PlaylistDownloadResult
    {
        public string PlaylistId { get; set; }
        public string PlaylistName { get; set; }
        public bool IsSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        public int TotalTracks { get; set; }
        public int SuccessfulTracks { get; set; }
        public int SkippedTracks { get; set; }
        public int FailedTracks { get; set; }
        public List<TrackDownloadInfo> DownloadedTracks { get; set; } = new List<TrackDownloadInfo>();
        public List<FailedTrackInfo> FailedTrackDetails { get; set; } = new List<FailedTrackInfo>();
        public string M3u8FilePath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// Information about a downloaded track
    /// </summary>
    public class TrackDownloadInfo
    {
        public string TrackId { get; set; }
        public string FilePath { get; set; }
        public int Position { get; set; }
        public string Quality { get; set; }
        public bool Skipped { get; set; }
    }

    /// <summary>
    /// Information about a failed track download
    /// </summary>
    public class FailedTrackInfo
    {
        public string TrackId { get; set; }
        public string TrackName { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Progress information for playlist downloads
    /// </summary>
    public class PlaylistDownloadProgress
    {
        public string PlaylistName { get; set; }
        public int TotalTracks { get; set; }
        public int CurrentTrack { get; set; }
        public int CurrentTrackIndex { get; set; } // Deprecated, use CurrentTrack
        public string CurrentTrackName { get; set; }
        public int CurrentTrackProgress { get; set; } // 0-100 percentage for current track
        public double OverallProgress { get; set; }
        public string Message { get; set; } // Detailed status message
    }
}