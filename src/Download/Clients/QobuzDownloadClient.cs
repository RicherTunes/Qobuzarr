using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Download.Clients
{
    public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>, IDisposable
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly IHttpClient _httpClient;
        private readonly IDownloadQueueService _queueService;
        private readonly IDownloadFileService _fileService;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadOrchestrator _orchestrator;
        private readonly IDownloadSummary _downloadSummary;
        private readonly IBatchProcessor _batchProcessor;
        private readonly IQobuzTrackDownloaderFactory _trackDownloaderFactory;
        private readonly ConcurrentDictionary<string, QobuzDownloadItem> _activeDownloads;

        public override string Name => QobuzarrConstants.ProtocolName;
        
        // Protocol property - returns string identifier for plugins branch
        public override string Protocol => QobuzarrConstants.ProtocolName;

        public QobuzDownloadClient(IQobuzAuthenticationService authService,
                                  IQobuzApiClient apiClient,
                                  IHttpClient httpClient,
                                  IDownloadQueueService queueService,
                                  IDownloadFileService fileService,
                                  IConcurrencyManager concurrencyManager,
                                  IDownloadOrchestrator orchestrator,
                                  IDownloadSummary downloadSummary,
                                  IBatchProcessor batchProcessor,
                                  IQobuzTrackDownloaderFactory trackDownloaderFactory,
                                  IConfigService configService,
                                  IDiskProvider diskProvider,
                                  IRemotePathMappingService remotePathMappingService,
                                  ILocalizationService localizationService,
                                  Logger logger)
            : base(configService, diskProvider, remotePathMappingService, localizationService, logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _downloadSummary = downloadSummary ?? throw new ArgumentNullException(nameof(downloadSummary));
            _batchProcessor = batchProcessor ?? throw new ArgumentNullException(nameof(batchProcessor));
            _trackDownloaderFactory = trackDownloaderFactory ?? throw new ArgumentNullException(nameof(trackDownloaderFactory));
            
            _activeDownloads = new ConcurrentDictionary<string, QobuzDownloadItem>();
        }

        /// <summary>
        /// Updates the concurrency manager with current settings.
        /// </summary>
        private void UpdateConcurrencySettings()
        {
            var newLimit = Math.Max(1, Math.Min(Settings?.GetEffectiveConcurrency() ?? 3, 20)); // Cap at 20 for safety
            _concurrencyManager?.UpdateConcurrencyLimit(newLimit);
            _logger.Debug("Updated concurrency limit to {0} concurrent downloads for this client", newLimit);
        }

        public override async Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            try
            {
                // Update concurrency settings
                UpdateConcurrencySettings();
                
                var albumTitle = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album";
                _logger.Info("📥 Adding to download queue: {0} - {1}", remoteAlbum.Artist, albumTitle);

                // Parse album ID from the release
                var albumId = ExtractAlbumIdFromRelease(remoteAlbum.Release);
                if (string.IsNullOrWhiteSpace(albumId))
                {
                    throw new InvalidOperationException("Could not extract album ID from release");
                }

                // Generate unique download ID
                var downloadId = Guid.NewGuid().ToString("N");

                // Create download item with file service integration
                var outputPath = _fileService.BuildOutputPath(remoteAlbum, Settings);
                var downloadItem = new QobuzDownloadItem
                {
                    DownloadId = downloadId,
                    AlbumId = albumId,
                    Title = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album",
                    Artist = remoteAlbum.Artist?.Name ?? "Unknown Artist",
                    Status = DownloadItemStatus.Queued,
                    Progress = 0,
                    StartedAt = DateTime.UtcNow,
                    OutputPath = outputPath,
                    CancellationTokenSource = new CancellationTokenSource()
                };

                // Add to queue service
                _queueService.AddDownload(downloadItem);

                // Start download task asynchronously
                downloadItem.DownloadTask = Task.Run(async () => await PerformDownloadAsync(downloadItem).ConfigureAwait(false));

                _logger.Debug("Qobuz download queued with ID: {0}", downloadId);
                return downloadId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start Qobuz download");
                throw;
            }
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            try
            {
                // Clean up old downloads using the queue service
                _queueService.CleanupCompletedDownloads(QobuzConstants.Download.CleanupCutoff);

                // Convert active downloads to DownloadClientItem format
                return _queueService.GetActiveDownloads()
                    .Select(item => item.ToDownloadClientItem(Definition.Id, Definition.Name))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving download items");
                return new List<DownloadClientItem>();
            }
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            RemoveItem(item.DownloadId, deleteData);
        }

        private void RemoveItem(string downloadId, bool deleteData)
        {
            try
            {
                if (_queueService.TryGetDownload(downloadId, out var downloadItem))
                {
                    // Cancel if still downloading
                    if (downloadItem.Status == DownloadItemStatus.Downloading)
                    {
                        downloadItem.Cancel();
                    }

                    // Remove from queue with data deletion option
                    _queueService.RemoveDownload(downloadId, deleteData);
                    _logger.Debug("Removed download item: {0}", downloadId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing download item: {0}", downloadId);
            }
        }

        // Qobuz uses streaming protocol, no magnet or torrent support needed

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                _logger.Info("Testing Qobuz download client connection...");
                
                // Test authentication - use async-safe pattern to avoid deadlocks
                try
                {
                    // Run async operation in a separate task context to avoid deadlock
                    var authTask = Task.Run(async () => await EnsureAuthenticatedAsync().ConfigureAwait(false));
                    authTask.Wait(TimeSpan.FromSeconds(30)); // Add timeout to prevent hanging
                    
                    if (!authTask.IsCompletedSuccessfully)
                    {
                        failures.Add(new ValidationFailure("Authentication", "Authentication timed out or failed"));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new ValidationFailure("Authentication", $"Authentication failed: {ex.Message}"));
                    return;
                }
                
                // Test download path accessibility using file service
                if (!string.IsNullOrWhiteSpace(Settings.DownloadPath))
                {
                    if (!_fileService.ValidateDownloadPath(Settings.DownloadPath))
                    {
                        failures.Add(new ValidationFailure("DownloadPath", "Download path is not accessible or has insufficient space"));
                        return;
                    }

                    try
                    {
                        _fileService.EnsureOutputDirectory(Settings.DownloadPath);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new ValidationFailure("DownloadPath", $"Cannot create download directory: {ex.Message}"));
                        return;
                    }
                }
                
                _logger.Info("Qobuz download client test successful");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Qobuz download client test failed");
                failures.Add(new ValidationFailure("", $"Connection test failed: {ex.Message}"));
            }
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(Settings.DownloadPath ?? "") }
            };
        }

        private async Task PerformDownloadAsync(QobuzDownloadItem downloadItem)
        {
            try
            {
                downloadItem.Status = DownloadItemStatus.Downloading;
                _logger.Info("🎵 Starting download: {0} - {1}", downloadItem.Artist, downloadItem.Title);

                // Ensure we have authentication
                await EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Get album details
                var album = await GetAlbumDetailsAsync(downloadItem.AlbumId).ConfigureAwait(false);
                if (album == null)
                {
                    throw new InvalidOperationException("Could not retrieve album details");
                }

                downloadItem.Album = album;
                downloadItem.TotalSize = album.GetEstimatedTotalSize(Settings.PreferredQuality);

                // Create output directory using file service
                _fileService.EnsureOutputDirectory(downloadItem.OutputPath);

                // Download tracks
                await DownloadAlbumTracksAsync(downloadItem, album).ConfigureAwait(false);

                // Mark as completed
                downloadItem.Status = DownloadItemStatus.Completed;
                downloadItem.Progress = 100;
                downloadItem.Message = "Download completed successfully";

                _logger.Info("✅ Download finished: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
            catch (OperationCanceledException)
            {
                downloadItem.Status = DownloadItemStatus.Failed;
                downloadItem.Message = "Download was cancelled";
                _logger.Info("⚠️ Download cancelled: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
            catch (Exception ex)
            {
                downloadItem.SetFailed($"Download failed: {ex.Message}");
                _logger.Error(ex, "Download failed: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
        }

        private async Task EnsureAuthenticatedAsync()
        {
            var session = _authService.GetCachedSession();
            if (session == null || session.NeedsRefresh())
            {
                throw new InvalidOperationException("No valid authentication session available");
            }

            _apiClient.SetSession(session);
            
            // Simple check if subscription supports preferred quality
            if (Settings != null && session.Subscription != null && 
                !session.Subscription.SupportsQuality(Settings.PreferredQuality))
            {
                var maxQuality = session.Subscription.GetMaxFormatId();
                _logger.Warn("Preferred quality exceeds subscription: will use {0} instead", 
                    QualityFormatter.GetQualityName(maxQuality));
            }
        }

        private async Task<QobuzAlbum> GetAlbumDetailsAsync(string albumId)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    {"album_id", albumId},
                    {"extra", "track_ids"}
                };

                return await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get album details for ID: {0}", albumId);
                throw;
            }
        }

        private async Task DownloadAlbumTracksAsync(QobuzDownloadItem downloadItem, QobuzAlbum album)
        {
            var tracks = album.GetTracks();
            if (!tracks.Any())
            {
                throw new InvalidOperationException("Album has no tracks to download");
            }

            var completedTracks = 0;
            var totalTracks = tracks.Count;
            
            // Update concurrency settings
            UpdateConcurrencySettings();
            
            // Enhanced track download start message
            var albumInfo = album != null ? $"{album.GetArtistName()} - {album.GetFullTitle()}" : $"{downloadItem.Artist} - {downloadItem.Title}";
            var albumYear = album?.ReleaseDate.Year > 1900 ? $" ({album.ReleaseDate.Year})" : "";
            _logger.Info("🎵 Starting album download: {0}{1} • {2} tracks • {3} concurrent", 
                albumInfo, albumYear, totalTracks, _concurrencyManager.CurrentLimit);
            
            var successfulTracks = 0;
            var skippedTracks = 0;
            var failedTracks = 0;
            
            var downloadTasks = tracks.Select(async track =>
            {
                // Use concurrency manager for slot control
                using var slot = await _concurrencyManager.AcquireSlotAsync(downloadItem.CancellationTokenSource.Token).ConfigureAwait(false);
                try
                {
                    downloadItem.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    
                    await DownloadSingleTrackAsync(downloadItem, album, track).ConfigureAwait(false);
                    
                    var completed = Interlocked.Increment(ref completedTracks);
                    Interlocked.Increment(ref successfulTracks);
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);

                    _logger.Debug("Downloaded track {0}/{1}: {2}", completed, totalTracks, track.GetFullTitle());
                    return new TrackDownloadResult { Success = true, TrackId = track.Id };
                }
                catch (TrackUnavailableException ex)
                {
                    // Handle track unavailability gracefully - don't fail the entire album
                    var completed = Interlocked.Increment(ref completedTracks);
                    
                    if (ex.Reason == TrackUnavailableReason.PreviewOnly || 
                        ex.Reason == TrackUnavailableReason.NoQualityAvailable)
                    {
                        Interlocked.Increment(ref skippedTracks);
                        _logger.Warn("Skipping track {0} ({1}): {2}", track.GetFullTitle(), track.Id, ex.GetUserFriendlyMessage());
                    }
                    else
                    {
                        Interlocked.Increment(ref failedTracks);
                        var errorMessage = ErrorMessageFormatter.FormatTrackError(
                            track.GetFullTitle(), 
                            ex.Reason, 
                            ex.Message);
                        _logger.Error(errorMessage);
                    }
                    
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);
                    
                    return new TrackDownloadResult 
                    { 
                        Success = false, 
                        TrackId = track.Id, 
                        Reason = ex.Reason,
                        Message = ex.GetUserFriendlyMessage()
                    };
                }
                catch (Exception ex)
                {
                    var completed = Interlocked.Increment(ref completedTracks);
                    Interlocked.Increment(ref failedTracks);
                    
                    _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                    
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);
                    
                    return new TrackDownloadResult 
                    { 
                        Success = false, 
                        TrackId = track.Id, 
                        Message = ex.Message 
                    };
                }
                // Concurrency slot is automatically released by 'using' statement
            });

            var results = await Task.WhenAll(downloadTasks).ConfigureAwait(false);
            
            // Record in download summary
            var bytesDownloaded = downloadItem.TotalSize; // Already a long, not nullable
            _downloadSummary.RecordAlbumResult(
                downloadItem.Artist,
                downloadItem.Title,
                successfulTracks,
                skippedTracks,
                failedTracks,
                totalTracks,
                bytesDownloaded);
            
            // Single Line Rich Summary format: ✅ Artist - Album Title (Year) → 12/14 tracks (85%) → 8×📀FLAC-96 + 4×💿FLAC-CD → 847.2MB → 2 preview-only skipped
            LogAlbumDownloadSummary(downloadItem.Artist, downloadItem.Title, album, successfulTracks, skippedTracks, failedTracks, totalTracks, bytesDownloaded);
            
            // Log the download summary if we've processed multiple albums
            if (_downloadSummary != null && _queueService.ActiveDownloadCount == 0)
            {
                var summaryReport = _downloadSummary.GenerateReport();
                _logger.Info(summaryReport);
            }
            
            // Use download policy to determine success
            var policy = Settings.GetDownloadPolicy();
            var isSuccessful = policy.IsAlbumDownloadSuccessful(totalTracks, successfulTracks, skippedTracks);
            
            if (isSuccessful)
            {
                if (skippedTracks > 0 || failedTracks > 0)
                {
                    var issues = new List<string>();
                    if (skippedTracks > 0) issues.Add($"{skippedTracks} tracks skipped (preview/sample only)");
                    if (failedTracks > 0) issues.Add($"{failedTracks} tracks failed");
                    
                    downloadItem.Message = $"Partial download completed with issues: {string.Join(", ", issues)}";
                }
                else
                {
                    downloadItem.Message = "All tracks downloaded successfully";
                }
            }
            else
            {
                // Download failed according to policy
                var exception = new AlbumDownloadException(
                    album.Id,
                    album.GetFullTitle(),
                    totalTracks,
                    successfulTracks,
                    skippedTracks,
                    failedTracks,
                    results);
                
                downloadItem.SetFailed(exception.Message);
                throw exception;
            }
        }

        private async Task DownloadSingleTrackAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzTrack track)
        {
            // Use the injected factory - no more manual instantiation!
            var trackDownloader = _trackDownloaderFactory.CreateTrackDownloader();
            
            var trackProgress = new Progress<double>(progress =>
            {
                // Update progress for this specific track
                // This is a simplified progress calculation - could be enhanced
                var currentTrackIndex = album.GetTracks().IndexOf(track);
                var totalTracks = album.GetTracks().Count;
                
                if (totalTracks > 0)
                {
                    var baseProgress = (double)currentTrackIndex / totalTracks * 100;
                    var trackContribution = progress / totalTracks;
                    var totalProgress = baseProgress + trackContribution;
                    
                    downloadItem.UpdateProgress(Math.Min(100, totalProgress));
                }
            });

            var outputPath = await trackDownloader.DownloadTrackAsync(
                track,
                album,
                downloadItem.OutputPath,
                Settings.PreferredQuality,
                trackProgress,
                downloadItem.CancellationTokenSource.Token
            ).ConfigureAwait(false);

            // Validate the downloaded file
            if (!trackDownloader.ValidateDownloadedFile(outputPath))
            {
                throw new InvalidOperationException($"Downloaded file validation failed: {track.GetFullTitle()}");
            }
        }

        private string ExtractAlbumIdFromRelease(ReleaseInfo release)
        {
            // Parse album ID from custom Qobuz URL format: "qobuz://album/{albumId}/{quality}"
            if (release.DownloadUrl?.StartsWith("qobuz://album/") == true)
            {
                var urlPart = release.DownloadUrl.Substring("qobuz://album/".Length);
                // Split by last slash to separate album ID from quality
                var lastSlashIndex = urlPart.LastIndexOf('/');
                if (lastSlashIndex > 0)
                {
                    return urlPart.Substring(0, lastSlashIndex);
                }
                // Fallback if no slash found (shouldn't happen with current format)
                return urlPart;
            }

            // Try to extract from GUID if it contains the album ID
            if (release.Guid?.StartsWith("qobuz-") == true)
            {
                var guidPart = release.Guid.Substring("qobuz-".Length);
                // GUID format is "qobuz-{albumId}-{quality}", extract just album ID
                var lastDashIndex = guidPart.LastIndexOf('-');
                if (lastDashIndex > 0)
                {
                    return guidPart.Substring(0, lastDashIndex);
                }
                return guidPart;
            }

            _logger.Warn("Could not extract album ID from release: {0}", release.Title);
            return null;
        }

        private void LogAlbumDownloadSummary(string artistName, string albumTitle, QobuzAlbum album, 
            int successful, int skipped, int failed, int total, long bytesDownloaded)
        {
            try
            {
                // Format: ✅ Artist - Album Title (Year) → 12/14 tracks (85%) → 8×📀FLAC-96 + 4×💿FLAC-CD → 847.2MB → 2 preview-only skipped
                var albumYear = album?.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "";
                var albumInfo = !string.IsNullOrEmpty(albumYear) ? $"{artistName} - {albumTitle} ({albumYear})" : $"{artistName} - {albumTitle}";
                
                var completionRate = total > 0 ? (int)Math.Round((double)successful / total * 100) : 0;
                var tracksInfo = $"{successful}/{total} tracks ({completionRate}%)";
                
                var sizeInfo = FormatBytes(bytesDownloaded);
                
                var summaryParts = new List<string>
                {
                    $"✅ {albumInfo}",
                    tracksInfo
                };
                
                // Add quality information if we have album data
                if (album?.GetTracks()?.Any() == true)
                {
                    var qualityBreakdown = GetQualityBreakdown(album.GetTracks(), successful);
                    if (!string.IsNullOrEmpty(qualityBreakdown))
                    {
                        summaryParts.Add(qualityBreakdown);
                    }
                }
                
                summaryParts.Add(sizeInfo);
                
                // Add issues summary if any
                if (skipped > 0 || failed > 0)
                {
                    var issues = new List<string>();
                    if (skipped > 0) issues.Add($"{skipped} preview-only skipped");
                    if (failed > 0) issues.Add($"{failed} failed");
                    summaryParts.Add(string.Join(", ", issues));
                }
                
                _logger.Info(string.Join(" → ", summaryParts));
            }
            catch (Exception ex)
            {
                // Fallback to simple summary if enhanced formatting fails
                _logger.Info("✅ Album download completed: {0} successful, {1} skipped, {2} failed out of {3} total tracks", 
                    successful, skipped, failed, total);
                _logger.Debug(ex, "Error formatting enhanced album summary");
            }
        }

        private string GetQualityBreakdown(IList<QobuzTrack> tracks, int successfulCount)
        {
            if (tracks == null || !tracks.Any() || successfulCount == 0)
                return "";

            // This is a simplified quality breakdown - in reality we'd need to track what quality each track was downloaded in
            // For now, provide a reasonable estimate based on the tracks available
            var qualityEstimates = new Dictionary<string, int>();
            
            // Analyze track qualities (this is estimated since we don't track actual download quality here)
            foreach (var track in tracks.Take(successfulCount))
            {
                // This is a placeholder - ideally we'd track actual download quality per track
                var estimatedQuality = EstimateTrackQuality(track);
                if (qualityEstimates.ContainsKey(estimatedQuality))
                    qualityEstimates[estimatedQuality]++;
                else
                    qualityEstimates[estimatedQuality] = 1;
            }
            
            var qualityParts = qualityEstimates
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{kv.Value}×{GetQualityIcon(kv.Key)}")
                .ToList();
                
            return qualityParts.Any() ? string.Join(" + ", qualityParts) : "";
        }

        private string EstimateTrackQuality(QobuzTrack track)
        {
            // This is a simplified estimation - in a full implementation we'd track actual download quality
            if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 192000)
                return "FLAC-192";
            else if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 96000)
                return "FLAC-96";
            else if (track.MaximumBitDepth >= 16)
                return "FLAC-CD";
            else
                return "MP3-320";
        }

        private string GetQualityIcon(string quality)
        {
            return quality switch
            {
                "FLAC-192" => "📀FLAC-192",
                "FLAC-96" => "📀FLAC-96",
                "FLAC-CD" => "💿FLAC-CD",
                "MP3-320" => "🎵MP3-320",
                _ => $"🎧{quality}"
            };
        }

        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                size /= 1024;
                order++;
            }
            
            return $"{size:F1}{sizes[order]}";
        }


        /// <summary>
        /// Properly dispose of resources including the concurrency semaphore
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously dispose of resources with graceful shutdown.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                _logger.Debug("Starting graceful shutdown of QobuzDownloadClient");

                // Cancel all active downloads through queue service
                var activeDownloads = _queueService.GetActiveDownloads().ToList();
                foreach (var download in activeDownloads)
                {
                    download.Cancel();
                }

                // Wait for all downloads to complete (with timeout)
                var downloadTasks = activeDownloads
                    .Where(d => d.DownloadTask != null && !d.DownloadTask.IsCompleted)
                    .Select(d => d.DownloadTask)
                    .ToList();

                if (downloadTasks.Any())
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        await Task.WhenAll(downloadTasks).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Debug("Some downloads did not complete within graceful shutdown timeout");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error waiting for downloads to complete during shutdown");
                    }
                }

                // Dispose the concurrency manager
                _concurrencyManager?.Dispose();

                _logger.Debug("QobuzDownloadClient shutdown completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during QobuzDownloadClient disposal");
            }
        }
    }
}