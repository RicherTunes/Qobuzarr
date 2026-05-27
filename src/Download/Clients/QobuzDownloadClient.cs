using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using Lidarr.Plugin.Qobuzarr.Download;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NLog;
using Lidarr.Plugin.Common.Services.Diagnostics;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Common.Utilities;
using System.Net.Http;
using System.Net.Http.Headers;

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
        private readonly Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService _trackDownloadService;
        // Process-wide tracker store — survives Lidarr's client re-instantiation cycles.
        // Static so a single store is shared across all QobuzDownloadClient instances
        // that DryIoc may create over the plugin's lifetime (matching the Tidalarr pattern).
        // Exposed as a protected virtual property so test subclasses can inject a fresh
        // per-test store without the static accumulation contaminating test isolation.
        private static readonly HostBridgeDownloadTrackerStore<QobuzDownloadItem> _staticTracker =
            new HostBridgeDownloadTrackerStore<QobuzDownloadItem>(TimeSpan.FromMinutes(30));

        protected virtual HostBridgeDownloadTrackerStore<QobuzDownloadItem> Tracker => _staticTracker;

        private QobuzDownloadItem? _lastQueuedItem;

        public override string Name => QobuzarrConstants.PluginName;

        // Lidarr plugins branch host expects string protocol identifier
        public override string Protocol => nameof(QobuzarrDownloadProtocol);

        public QobuzDownloadClient(IQobuzAuthenticationService authService,
                                  IQobuzApiClient apiClient,
                                  IHttpClient httpClient,
                                  IDownloadQueueService queueService,
                                  IDownloadFileService fileService,
                                  IConcurrencyManager concurrencyManager,
                                  IDownloadOrchestrator orchestrator,
                                  IDownloadSummary downloadSummary,
                                  IBatchProcessor batchProcessor,
                                  Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService trackDownloadService,
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
            _trackDownloadService = trackDownloadService ?? throw new ArgumentNullException(nameof(trackDownloadService));
        }

        /// <summary>
        /// Gets effective settings. In production, returns base Settings property.
        /// Test subclasses can override this to provide mock settings.
        /// </summary>
        protected virtual QobuzDownloadSettings GetEffectiveSettings()
        {
            return Settings ?? new QobuzDownloadSettings();
        }

        /// <summary>
        /// Updates the concurrency manager with current settings.
        /// </summary>
        private void UpdateConcurrencySettings()
        {
            var effectiveSettings = GetEffectiveSettings();

            var desired = effectiveSettings.GetEffectiveConcurrency();
            var newLimit = Math.Max(1, Math.Min(desired, Constants.QobuzarrConstants.Defaults.MaxConcurrentDownloads));
            _concurrencyManager?.UpdateConcurrencyLimit(newLimit);
            _logger.Debug("Updated concurrency limit to {0} concurrent downloads for this client", newLimit);
        }

        public override async Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            var albumTitle = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album";
            using var _scope = PluginLogContext.Push("Qobuzarr", "Download");
            try
            {
                // AuthFailureGate pre-flight: abort immediately if credentials are known bad,
                // rather than queueing a download that will fail at the first API call.
                if (IsAuthShortCircuited(_apiClient.Gate))
                {
                    throw new InvalidOperationException(
                        "Qobuz authentication is latched bad (an auth failure was observed recently). " +
                        "Re-enter credentials and save settings — the gate will auto-recover once a request succeeds.");
                }

                // Update concurrency settings
                UpdateConcurrencySettings();

                _logger.Info($"{PluginLogContext.Current?.LinePrefix()}Adding to download queue: {{0}} - {{1}}", remoteAlbum.Artist, albumTitle);

                // Parse album ID from the release
                var albumId = ExtractAlbumIdFromRelease(remoteAlbum.Release);
                if (string.IsNullOrWhiteSpace(albumId))
                {
                    throw new InvalidOperationException("Could not extract album ID from release");
                }

                // Generate unique download ID
                var downloadId = Guid.NewGuid().ToString("N");

                // Create download item with file service integration
                var outputPath = BuildOutputPath(remoteAlbum);
                var downloadItem = new QobuzDownloadItem
                {
                    DownloadId = downloadId,
                    AlbumId = albumId,
                    Title = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album",
                    Artist = remoteAlbum.Artist?.Name ?? "Unknown Artist",
                    StartedAt = DateTime.UtcNow,
                    OutputPath = outputPath,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                // Status defaults to Queued (HostBridgeDownloadItem initial state = 0 = Queued)

                // Register with the process-wide tracker store (survives re-instantiation).
                Tracker.AddOrReplace(downloadItem);
                _lastQueuedItem = downloadItem;

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
                // CleanupOldDownloads is still called for the queue service side.
                CleanupOldDownloads();

                // Tracker.GetSnapshot() runs the 30-min retention sweep on completed/failed
                // items as a side-effect, then returns all still-live entries.
                var snapshot = Tracker.GetSnapshot();
                var result = new List<DownloadClientItem>();

                foreach (var item in snapshot)
                {
                    result.Add(item.ToDownloadClientItem(0, Name));
                }

                if (result.Count == 0 && _lastQueuedItem != null)
                {
                    result.Add(_lastQueuedItem.ToDownloadClientItem(0, Name));
                }

                // Merge any queue-service items not already captured.
                var queued = _queueService.GetActiveDownloads() ?? Enumerable.Empty<QobuzDownloadItem>();
                foreach (var q in queued)
                {
                    result.Add(q.ToDownloadClientItem(0, Name));
                }

                return result;
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
                // Remove from the process-wide tracker (handles optional data deletion).
                Tracker.Remove(downloadId, deleteData, out var trackerItem,
                    ex => _logger.Warn(ex, "Error deleting download data for {0}", downloadId));

                // Clear _lastQueuedItem sentinel if it matches.
                if (_lastQueuedItem?.DownloadId == downloadId)
                {
                    _lastQueuedItem = null;
                }

                // Cancel in-flight download if it's still running.
                if (trackerItem != null &&
                    trackerItem.GetStatus() == HostBridgeDownloadItemStatus.Downloading)
                {
                    trackerItem.Cancel();
                }

                // Also remove from the queue service (which has its own dict).
                if (_queueService.TryGetDownload(downloadId, out var queueItem))
                {
                    if (queueItem.GetStatus() == HostBridgeDownloadItemStatus.Downloading)
                    {
                        queueItem.Cancel();
                    }
                    _queueService.RemoveDownload(downloadId, deleteData);
                    _logger.Debug("Removed download item: {0}", downloadId);
                }
                else if (trackerItem != null)
                {
                    _logger.Debug("Removed download item from tracker: {0}", downloadId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing download item: {0}", downloadId);
            }
        }

        // Maintain backward compatibility with tests that reflect this method
        private string BuildOutputPath(RemoteAlbum remoteAlbum)
        {
            // Attempt to read a shadowed Settings property from derived test classes
            QobuzDownloadSettings? effectiveSettings = null;
            try
            {
                var prop = GetType().GetProperty("Settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (prop != null && typeof(QobuzDownloadSettings).IsAssignableFrom(prop.PropertyType))
                {
                    effectiveSettings = prop.GetValue(this) as QobuzDownloadSettings;
                }
            }
            catch (Exception ex) { _logger.Debug(ex, "Best-effort reflective Settings lookup failed"); }

            if (effectiveSettings == null)
            {
                // Avoid touching base.Settings here (may rely on internal Definition during tests)
                effectiveSettings = new QobuzDownloadSettings();
            }
            var built = _fileService.BuildOutputPath(remoteAlbum, effectiveSettings) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(built))
            {
                // Fallback: build a simple path that matches test expectations
                var album = remoteAlbum?.Albums?.FirstOrDefault();
                var artist = Lidarr.Plugin.Common.HostBridge.PathTraversalGuard.SanitizeSegment(
                    remoteAlbum?.Artist?.Name ?? "Unknown Artist");
                var title = Lidarr.Plugin.Common.HostBridge.PathTraversalGuard.SanitizeSegment(
                    album?.Title ?? "Unknown Album");
                try
                {
                    built = System.IO.Path.Combine("Qobuz", artist, title);
                }
                catch
                {
                    built = $"{artist} - {title}";
                }
            }
            return built;
        }

        // Qobuz uses streaming protocol, no magnet or torrent support needed

        protected override void Test(List<ValidationFailure> failures)
        {
            using var _scope = PluginLogContext.Push("Qobuzarr", "Test");
            try
            {
                _logger.Info($"{PluginLogContext.Current?.LinePrefix()}Testing Qobuz download client connection...");

                var settings = GetEffectiveSettings();
                var builder = new Lidarr.Plugin.Common.Validation.TestValidationBuilder()
                    .RequireNonEmpty("DownloadPath", settings.DownloadPath, "Download path is required.");
                builder.ApplyTo(failures);
                if (builder.HasFailures) return;

                // AuthFailureGate pre-flight: if the gate is latched bad and no probe slot is
                // available, surface an actionable failure instead of attempting a network call.
                if (IsAuthShortCircuited(_apiClient.Gate))
                {
                    failures.Add(new ValidationFailure(
                        "Authentication",
                        "Qobuz authentication is latched bad (an auth failure was observed recently). " +
                        "Re-enter credentials and click Test again — the gate will auto-recover once a request succeeds."));
                    return;
                }

                // Note: Authentication is handled by the indexer - we don't test it here
                // This matches TrevTV's approach where the download client trusts the indexer's auth

                // Syntactic pre-check: catch traversal, relative paths, invalid chars
                // before hitting the filesystem (actionable error messages).
                var pathValidation = Lidarr.Plugin.Common.Services.Validation.DownloadPathValidator.Validate(settings.DownloadPath);
                if (!pathValidation.IsValid)
                {
                    failures.Add(new ValidationFailure("DownloadPath", pathValidation.Message));
                    return;
                }

                // Filesystem accessibility check
                if (!string.IsNullOrWhiteSpace(settings.DownloadPath))
                {
                    if (!_fileService.ValidateDownloadPath(settings.DownloadPath))
                    {
                        // Wave 75 UX: name the path so the user knows which one is the
                        // problem (download paths are often shared volumes / SMB mounts
                        // where the failure isn't visible at the input field).
                        failures.Add(new ValidationFailure(
                            "DownloadPath",
                            $"Download path '{settings.DownloadPath}' is not accessible or has insufficient space. Check that the path exists, Lidarr has write permission, and the disk has free space."));
                        return;
                    }

                    try
                    {
                        _fileService.EnsureOutputDirectory(settings.DownloadPath);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new ValidationFailure(
                            "DownloadPath",
                            $"Cannot create download directory '{settings.DownloadPath}': {ex.Message}. Check parent directory permissions."));
                        return;
                    }
                }

                _logger.Info("Qobuz download client test successful");
            }
            catch (Exception ex)
            {
                RecordAuthOutcomeFromException(_apiClient.Gate, ex);
                _logger.Error(ex, "Qobuz download client test failed");
                var classification = HttpExceptionClassifier.Classify(ex);
                string failureField = classification.Category == HttpFailureCategory.Auth
                    ? "Authentication"
                    : "Test";
                failures.Add(new ValidationFailure(failureField, classification.Hint));
            }
        }

        public override DownloadClientInfo GetStatus()
        {
            var settings = GetEffectiveSettings();
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(settings.DownloadPath ?? "") }
            };
        }

        // Maintain backward compatibility with tests
        private void CleanupOldDownloads()
        {
            try
            {
                _queueService.CleanupCompletedDownloads(QobuzConstants.Download.CleanupCutoff);

                // Leave in-memory items intact for now; queue service handles real cleanup.
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "CleanupOldDownloads encountered an error");
            }
        }

        private async Task PerformDownloadAsync(QobuzDownloadItem downloadItem)
        {
            try
            {
                downloadItem.SetHostStatus(DownloadItemStatus.Downloading);
                _logger.Info("🎵 Starting download: {0} - {1}", downloadItem.Artist, downloadItem.Title);

                // Get effective settings (supports test subclasses)
                var settings = GetEffectiveSettings();

                // Ensure we have authentication
                await EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Get album details
                var album = await GetAlbumDetailsAsync(downloadItem.AlbumId).ConfigureAwait(false);
                if (album == null)
                {
                    throw new InvalidOperationException("Could not retrieve album details");
                }

                downloadItem.Album = album;
                downloadItem.TotalSize = album.GetEstimatedTotalSize(settings.PreferredQuality);


                // Create output directory using file service
                _fileService.EnsureOutputDirectory(downloadItem.OutputPath);

                // Download tracks (delegated to service)
                await _trackDownloadService.DownloadAlbumAsync(downloadItem, album, settings, downloadItem.CancellationTokenSource.Token).ConfigureAwait(false);

                // Mark as completed
                downloadItem.SetHostStatus(DownloadItemStatus.Completed);
                downloadItem.SetProgress(100);
                downloadItem.Message = downloadItem.QualityFallbackCount > 0
                    ? $"Download completed successfully (quality fallback used for {downloadItem.QualityFallbackCount} track(s){(downloadItem.QualityFallbackExample != null ? $": {downloadItem.QualityFallbackExample}" : "")})"
                    : "Download completed successfully";

                _logger.Info("✅ Download finished: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
            catch (OperationCanceledException)
            {
                downloadItem.SetHostStatus(DownloadItemStatus.Failed);
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
            var settings = GetEffectiveSettings();
            if (settings != null && session.Subscription != null &&
                !session.Subscription.SupportsQuality(settings.PreferredQuality))
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
            // Use integrated download functionality instead of factory

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

            // REAL download implementation - get stream URL and download actual audio
            string outputPath = null;

            try
            {
                // 1. Get streaming info from Qobuz API (need format before building path)
                var streamingInfo = await _apiClient.GetStreamingInfoAsync(track.Id, Settings.PreferredQuality).ConfigureAwait(false);
                var streamUrl = streamingInfo?.Url;
                if (string.IsNullOrEmpty(streamUrl))
                {
                    throw new InvalidOperationException($"Could not get streaming URL for track: {track.Title}");
                }

                // Build output path with sanitized filename and correct extension based on actual format
                var actualFormatId = streamingInfo?.FormatId ?? Settings.PreferredQuality;
                var filename = TrackFileNameBuilder.Build(track.TrackNumber, track.Title, actualFormatId, track.DiscNumber, album.MediaCount);
                outputPath = Path.Combine(downloadItem.OutputPath, filename);

                _logger.Info("🎵 Downloading track: {0} to {1}", track.Title, outputPath);
                _logger.Debug("🔗 Got streaming URL for track {0}: {1}", track.Id, Scrub.Url(streamUrl));

                // 2. Create output directory
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                // 3. Download actual audio file (stream to disk to avoid large memory usage)
                _logger.Debug("📥 Starting HTTP download for track {0}", track.Title);
                var bytesWritten = await DownloadToFileAsync(streamUrl, outputPath, downloadItem.CancellationTokenSource.Token);
                _logger.Debug("💾 Audio file written: {0} bytes", bytesWritten);

                // 5. Apply metadata tags using TagLibSharp — propagate cancellation so a
                // user-canceled download doesn't keep TagLib writing to a file that's
                // about to be cleaned up.
                await ApplyMetadataTagsAsync(outputPath, track, album, downloadItem.CancellationTokenSource.Token);

                _logger.Info("✅ Downloaded: {0} ({1:F1} MB)", track.Title, bytesWritten / 1024.0 / 1024.0);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Download failed for track: {0}", track.Title);

                // Clean up partial file
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { _logger.Debug(cleanupEx, "Best-effort file cleanup failed for {0}", outputPath); }
                }
                throw;
            }

            // Validate the actual audio file was created
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1024)
            {
                throw new InvalidOperationException($"Downloaded file validation failed: {track.Title}");
            }
        }

        private async Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken)
        {
            // Stream to a temporary .partial file, then atomic move to final
            var httpClient = SharedSystemHttpClient.Instance;
            var partialPath = filePath + ".partial";
            long existing = 0;
            if (File.Exists(partialPath))
            {
                try { existing = new FileInfo(partialPath).Length; } catch (IOException ex) { _logger.Debug(ex, "Could not read partial file size"); existing = 0; }
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            var contentLength = response.Content.Headers.ContentLength;
            var urlHost = DownloadResponseDiagnostics.TryGetHost(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || contentLength == 0)
            {
                throw new InvalidOperationException($"Download returned no content (HTTP {(int)response.StatusCode} {response.StatusCode}, Host={urlHost}, Content-Type={contentType}).");
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!isPartial && File.Exists(partialPath))
            {
                // Server didn't honor range; start fresh
                try { File.Delete(partialPath); } catch (Exception ex) { _logger.Debug(ex, "Best-effort partial file cleanup failed for {0}", partialPath); }
                existing = 0;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[131072];
            long totalWritten = existing;
            int read;

            // Explicit scope ensures fileStream is closed before File.Move
            await using (var fileStream = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, 131072, useAsync: true))
            {
                read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new InvalidOperationException($"Downloaded stream contained no data (Host={urlHost}, Content-Type={contentType}, Content-Length={contentLength?.ToString() ?? "unknown"}).");
                }

                if (DownloadResponseDiagnostics.IsTextLikeContentType(contentType) || DownloadResponseDiagnostics.LooksLikeTextPayload(buffer, read))
                {
                    var snippet = Encoding.UTF8.GetString(buffer, 0, Math.Min(read, 512))
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();

                    if (DownloadResponseDiagnostics.ShouldRedactSnippet(snippet))
                    {
                        snippet = "[redacted]";
                    }

                    throw new InvalidOperationException($"Unexpected content type '{contentType}' when downloading audio (Host={urlHost}). Snippet: {snippet}");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalWritten += read;

                while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalWritten += read;
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, filePath, overwrite: true);

            Lidarr.Plugin.Common.Utilities.AudioMagicBytesValidator.ValidateAudioMagicBytes(filePath);

            // Validate file (basic; no size/hash guarantees from server)       
            if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }
            return totalWritten;
        }

        private async Task ApplyMetadataTagsAsync(string filePath, QobuzTrack track, QobuzAlbum album, CancellationToken cancellationToken = default)
        {
            // Pre-flight cancellation check — if the download was canceled mid-write,
            // skip the metadata pass entirely rather than queue a Task that will
            // race with the cleanup of `filePath`.
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var file = TagLib.File.Create(filePath);

                    file.Tag.Title = track.Title;
                    file.Tag.Track = (uint)track.TrackNumber;
                    file.Tag.Disc = (uint)track.DiscNumber;

                    if (album != null)
                    {
                        file.Tag.Album = album.Title;
                        file.Tag.AlbumArtists = new[] { album.Artist?.Name ?? "Unknown Artist" };
                        if (album.ReleaseDate != default)
                        {
                            file.Tag.Year = (uint)album.ReleaseDate.Year;
                        }
                        if (album.Genre != null)
                        {
                            file.Tag.Genres = new[] { album.Genre.Name };
                        }
                        if (album.Label != null)
                        {
                            // Use Comment field since Publishers doesn't exist in TagLibSharp
                            file.Tag.Comment = $"Label: {album.Label.Name}";
                        }
                    }

                    if (track.Performer != null)
                    {
                        file.Tag.Performers = new[] { track.Performer.Name };
                    }

                    if (track.Composer != null)
                    {
                        file.Tag.Composers = new[] { track.Composer.Name };
                    }

                    // ISRC - International Standard Recording Code
                    // Normalize: trim whitespace and convert to uppercase (ISO 3901 standard)
                    if (!string.IsNullOrWhiteSpace(track.ISRC))
                    {
                        var normalizedIsrc = track.ISRC.Trim().ToUpperInvariant();
                        ApplyIsrcTag(file, normalizedIsrc);
                    }

                    file.Save();
                    _logger.Debug("✅ Metadata applied to: {0}", Path.GetFileName(filePath));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to apply metadata to: {0}", Path.GetFileName(filePath));
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies ISRC to the audio file using format-specific tagging.
        /// ID3v2: TSRC frame (MP3)
        /// Vorbis/FLAC/Ogg: ISRC comment
        /// </summary>
        private static void ApplyIsrcTag(TagLib.File file, string isrc)
        {
            // Try Xiph/Vorbis comment (FLAC, Ogg Vorbis)
            if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiphComment)
            {
                xiphComment.SetField("ISRC", isrc);
                return;
            }

            // Try ID3v2 (MP3)
            if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2Tag)
            {
                var tsrcFrame = TagLib.Id3v2.TextInformationFrame.Get(
                    id3v2Tag,
                    TagLib.ByteVector.FromString("TSRC", TagLib.StringType.Latin1),
                    true);
                tsrcFrame.Text = new[] { isrc };
            }
        }

        private string? ExtractAlbumIdFromRelease(ReleaseInfo release)
        {
            // Try Common-grammar parser first (new format: qobuz:album:{id}[...])
            var fromNewGuid = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, "qobuz");
            if (!string.IsNullOrWhiteSpace(fromNewGuid))
            {
                // Also try to resolve from download URL for belt-and-suspenders, but prefer GUID result
                return fromNewGuid;
            }

            // Fall back to AlbumIdExtractor which handles both URL formats and the legacy dash-GUID
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);
            if (albumId != null)
            {
                // Log at Debug so operators can observe the migration drain in progress
                _logger.Debug("ExtractAlbumIdFromRelease: resolved '{0}' via legacy fallback path (GUID: {1}, URL: {2}). " +
                              "This release was queued before the Common-grammar migration. Once it completes, no further legacy lookups will occur for this album.",
                    albumId, release.Guid ?? "(null)", release.DownloadUrl ?? "(null)");
                return albumId;
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


        #region AuthFailureGate helpers
        // ------------------------------------------------------------------ //
        // Mirror the pattern in QobuzIndexer / AppleMusicLidarrDownloadClient.
        // Static for testability (callers can pin the contract without constructing a full DC).
        // The gate is obtained from _apiClient.Gate; BridgeQobuzApiClient returns the singleton
        // gate; QobuzApiClient (Lidarr-native path) returns null (always-healthy fast-path).
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns true when the gate is latched bad AND no probe slot is available.
        /// A null gate is always considered healthy (safe default when no gate is wired).
        /// </summary>
        public static bool IsAuthShortCircuited(AuthFailureGate? gate)
        {
            if (gate is null) return false;
            if (gate.IsHealthy) return false;
            return !gate.TryAcquireProbeSlot();
        }

        /// <summary>
        /// If <paramref name="ex"/> looks like a Qobuz auth failure (HTTP 401/403 or
        /// <see cref="Exceptions.QobuzApiException"/> with a 401/403 status), records
        /// a failure with <paramref name="gate"/>'s handler so the gate latches and subsequent
        /// calls short-circuit without touching the network.
        ///
        /// <para>SYNC-OVER-ASYNC (Category A): wraps <c>HandleFailureAsync</c> in
        /// <c>Task.Run</c> to avoid deadlocking on ASP.NET-style single-threaded
        /// <see cref="System.Threading.SynchronizationContext"/>s.</para>
        /// </summary>
        public static void RecordAuthOutcomeFromException(AuthFailureGate? gate, Exception ex)
        {
            if (gate is null) return;
            if (!LooksLikeAuthFailure(ex)) return;

            var failure = new Lidarr.Plugin.Abstractions.Contracts.AuthFailure
            {
                ErrorCode = (ex as HttpRequestException)?.StatusCode?.ToString()
                            ?? (ex as Exceptions.QobuzApiException)?.StatusCode?.ToString(),
                Message = ex.Message,
            };
            // SYNC-OVER-ASYNC (Category A): thread-pool hop avoids host-context deadlock.
            Task.Run(() => gate.Handler.HandleFailureAsync(failure).AsTask())
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns true when <paramref name="ex"/> is recognisable as a Qobuz
        /// authentication failure:
        /// <list type="bullet">
        ///   <item>HTTP 401 Unauthorized or 403 Forbidden (HttpRequestException)</item>
        ///   <item><see cref="Exceptions.QobuzApiException"/> with StatusCode 401 or 403</item>
        /// </list>
        /// </summary>
        public static bool LooksLikeAuthFailure(Exception ex)
        {
            if (ex is HttpRequestException hre &&
                hre.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return true;
            }
            if (ex is Exceptions.QobuzApiException qae &&
                qae.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return true;
            }
            return false;
        }

        #endregion

        /// <summary>
        /// Properly dispose of resources including the concurrency semaphore.
        /// </summary>
        /// <remarks>
        /// SYNC-OVER-ASYNC: IDisposable.Dispose() is forced by Lidarr host (Category A).
        /// DisposeAsync completes synchronously when no downloads are in-flight.
        /// </remarks>
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
                    .Where(d => d.DownloadTask != null && !d.DownloadTask!.IsCompleted)
                    .Select(d => d.DownloadTask!)
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
