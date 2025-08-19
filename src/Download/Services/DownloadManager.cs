using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Manages the download lifecycle for albums and tracks
    /// </summary>
    public class DownloadManager : IDownloadManager
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly IDownloadOrchestrator _orchestrator;
        private readonly IDownloadQueueService _queueService;
        private readonly IDownloadFileService _fileService;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IBatchProcessor _batchProcessor;
        private readonly IQobuzTrackDownloaderFactory _trackDownloaderFactory;
        private readonly IDownloadReporter _reporter;
        private readonly Logger _logger;

        public DownloadManager(
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            IDownloadOrchestrator orchestrator,
            IDownloadQueueService queueService,
            IDownloadFileService fileService,
            IConcurrencyManager concurrencyManager,
            IBatchProcessor batchProcessor,
            IQobuzTrackDownloaderFactory trackDownloaderFactory,
            IDownloadReporter reporter,
            Logger logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _batchProcessor = batchProcessor ?? throw new ArgumentNullException(nameof(batchProcessor));
            _trackDownloaderFactory = trackDownloaderFactory ?? throw new ArgumentNullException(nameof(trackDownloaderFactory));
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> DownloadAlbumAsync(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            var albumTitle = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album";
            var artistName = remoteAlbum.Artist?.Name ?? "Unknown Artist";
            
            _logger.Info("Starting download for album: {0} by {1}", albumTitle, artistName);

            var albumId = ExtractAlbumIdFromRelease(remoteAlbum.Release);
            if (string.IsNullOrEmpty(albumId))
            {
                throw new InvalidOperationException("Could not extract album ID from release");
            }

            await EnsureAuthenticatedAsync().ConfigureAwait(false);

            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = Guid.NewGuid().ToString(),
                AlbumId = albumId,
                AlbumTitle = albumTitle,
                ArtistName = artistName,
                Progress = 0,
                TotalTracks = 0,
                CompletedTracks = 0,
                StartTime = DateTime.UtcNow,
                CancellationTokenSource = new CancellationTokenSource()
            };
            
            downloadItem.SetStatus(QobuzDownloadStatus.Queued);

            _queueService.AddDownload(downloadItem);

            // Start async download
            _ = Task.Run(async () => await PerformDownloadAsync(downloadItem).ConfigureAwait(false));

            return downloadItem.DownloadId;
        }

        public async Task DownloadAlbumTracksAsync(QobuzDownloadItem downloadItem, QobuzAlbum album)
        {
            var startTime = DateTime.UtcNow;
            var successfulDownloads = 0;
            var totalBytes = 0L;

            try
            {
                downloadItem.TotalTracks = album.GetTracks()?.Count ?? 0;
                downloadItem.SetStatus(QobuzDownloadStatus.Downloading);

                if (album.GetTracks() == null || !album.GetTracks().Any())
                {
                    throw new AlbumDownloadException("No tracks found in album", album.Id);
                }

                _logger.Info("Starting batch download for {0} tracks from album: {1}", 
                    album.GetTracks().Count, album.Title);

                // Use batch processor for efficient downloading
                var batchResult = await _batchProcessor.ProcessTracksAsync(
                    album.GetTracks(),
                    async (track) =>
                    {
                        try
                        {
                            await DownloadSingleTrackAsync(downloadItem, album, track).ConfigureAwait(false);
                            Interlocked.Increment(ref successfulDownloads);
                            
                            // Estimate file size for progress tracking (using FLAC CD quality as default)
                            var estimatedSize = track.GetEstimatedFileSize(6);
                            if (estimatedSize > 0)
                            {
                                Interlocked.Add(ref totalBytes, estimatedSize);
                            }
                            
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to download track: {0}", track.Title);
                            return false;
                        }
                    },
                    null, // progress - not needed for this use case
                    downloadItem.CancellationTokenSource.Token
                ).ConfigureAwait(false);

                var elapsed = DateTime.UtcNow - startTime;
                
                _reporter.LogAlbumDownloadSummary(
                    album.Artist?.Name ?? "Unknown Artist",
                    album.Title,
                    album,
                    successfulDownloads,
                    album.GetTracks().Count,
                    totalBytes,
                    elapsed);

                downloadItem.SetStatus(successfulDownloads == album.GetTracks().Count 
                    ? QobuzDownloadStatus.Completed 
                    : QobuzDownloadStatus.Failed);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error downloading album tracks");
                downloadItem.SetStatus(QobuzDownloadStatus.Failed);
                throw;
            }
        }

        public async Task DownloadSingleTrackAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzTrack track)
        {
            try
            {
                var downloader = _trackDownloaderFactory.CreateTrackDownloader();
                
                var settings = downloadItem.Settings ?? new QobuzDownloadSettings();
                var outputPath = downloadItem.OutputPath ?? settings.DownloadPath ?? Path.GetTempPath();
                var preferredQuality = settings.PreferredQuality; // Already defaults to 6 in constructor
                
                var progress = new Progress<double>(percent => 
                {
                    downloadItem.UpdateProgress(percent);
                });
                
                var result = await downloader.DownloadTrackAsync(
                    track,
                    album,
                    outputPath,
                    preferredQuality,
                    progress,
                    downloadItem.CancellationTokenSource.Token
                ).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(result))
                {
                    downloadItem.CompletedTracks++;
                    downloadItem.Progress = (downloadItem.CompletedTracks * 100) / downloadItem.TotalTracks;
                    
                    _logger.Debug("Successfully downloaded track: {0} to {1} ({2}/{3})",
                        track.Title, result, downloadItem.CompletedTracks, downloadItem.TotalTracks);
                }
                else
                {
                    _logger.Warn("Failed to download track: {0} - No file path returned", track.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error downloading track: {0}", track.Title);
                throw;
            }
        }

        public async Task<QobuzAlbum> GetAlbumDetailsAsync(string albumId)
        {
            try
            {
                _logger.Debug("Fetching album details for ID: {0}", albumId);
                
                var album = await _apiClient.GetAlbumAsync(albumId).ConfigureAwait(false);
                
                if (album == null)
                {
                    throw new AlbumDownloadException($"Album not found: {albumId}", albumId);
                }
                
                _logger.Debug("Retrieved album: {0} with {1} tracks", album.Title, album.GetTracks()?.Count ?? 0);
                
                return album;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get album details for ID: {0}", albumId);
                throw;
            }
        }

        public string ExtractAlbumIdFromRelease(ReleaseInfo release)
        {
            if (release == null)
                return null;

            // Try to extract from GUID (format: qobuz-album-{id})
            if (!string.IsNullOrEmpty(release.Guid) && release.Guid.StartsWith("qobuz-album-"))
            {
                return release.Guid.Replace("qobuz-album-", "");
            }

            // Try to extract from comment URL
            if (!string.IsNullOrEmpty(release.CommentUrl))
            {
                var parts = release.CommentUrl.Split('/');
                if (parts.Length > 0)
                {
                    return parts.Last();
                }
            }

            // Try download URL
            if (!string.IsNullOrEmpty(release.DownloadUrl))
            {
                var parts = release.DownloadUrl.Split('/');
                if (parts.Length > 0)
                {
                    return parts.Last();
                }
            }

            return null;
        }

        private async Task EnsureAuthenticatedAsync()
        {
            var cachedSession = _authService.GetCachedSession();
            if (cachedSession != null && !cachedSession.NeedsRefresh())
            {
                _apiClient.SetSession(cachedSession);
                return;
            }

            // Re-authenticate if needed
            var credentials = new QobuzCredentials
            {
                // Credentials would be populated from settings
            };
            
            var session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
            _apiClient.SetSession(session);
        }

        private async Task PerformDownloadAsync(QobuzDownloadItem downloadItem)
        {
            try
            {
                downloadItem.SetStatus(QobuzDownloadStatus.Downloading);
                
                var album = await GetAlbumDetailsAsync(downloadItem.AlbumId).ConfigureAwait(false);
                
                if (album == null)
                {
                    throw new InvalidOperationException("Could not retrieve album details");
                }
                
                await DownloadAlbumTracksAsync(downloadItem, album).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Download cancelled for album: {0}", downloadItem.AlbumTitle);
                downloadItem.SetStatus(QobuzDownloadStatus.Failed);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error performing download for album: {0}", downloadItem.AlbumTitle);
                downloadItem.SetStatus(QobuzDownloadStatus.Failed);
            }
        }
    }
}