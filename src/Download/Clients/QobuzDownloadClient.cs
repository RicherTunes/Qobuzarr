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
                // Capture the configured download root for root-contained failed-download cleanup (F-10).
                // base.Settings can rely on Definition (not set in some unit-test paths), so resolve defensively.
                string? downloadRoot = null;
                try { downloadRoot = GetEffectiveSettings()?.DownloadPath; }
                catch (Exception ex) { _logger.Debug(ex, "Could not resolve download root for cleanup containment"); }
                var downloadItem = new QobuzDownloadItem
                {
                    DownloadId = downloadId,
                    AlbumId = albumId,
                    Title = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album",
                    Artist = remoteAlbum.Artist?.Name ?? "Unknown Artist",
                    StartedAt = DateTime.UtcNow,
                    OutputPath = outputPath,
                    DownloadRoot = downloadRoot,
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

                // Stamp every reported item with the registered download-client id/name.
                // Lidarr's CompletedDownloadService resolves the owning client via
                // DownloadClientProvider.Get(DownloadClientInfo.Id) -> .Single(d => d.Definition.Id == id).
                // Hardcoding 0 made Get(0) throw "Sequence contains no matching element",
                // wedging every completed download. Definition is populated once the client
                // is registered; fall back to Name for the unregistered (e.g. Test()) path.
                var clientId = Definition?.Id ?? 0;
                var clientName = Definition?.Name ?? Name;

                foreach (var item in snapshot)
                {
                    result.Add(item.ToDownloadClientItem(clientId, clientName));
                }

                if (result.Count == 0 && _lastQueuedItem != null)
                {
                    result.Add(_lastQueuedItem.ToDownloadClientItem(clientId, clientName));
                }

                // Merge any queue-service items not already captured.
                var queued = _queueService.GetActiveDownloads() ?? Enumerable.Empty<QobuzDownloadItem>();
                foreach (var q in queued)
                {
                    result.Add(q.ToDownloadClientItem(clientId, clientName));
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
            => gate?.ShouldShortCircuit() ?? false;

        /// <summary>
        /// If <paramref name="ex"/> looks like a Qobuz auth failure (HTTP 401/403 or
        /// <see cref="Exceptions.QobuzApiException"/> with a 401/403 status), records
        /// a failure with <paramref name="gate"/>'s handler so the gate latches and subsequent
        /// calls short-circuit without touching the network.
        ///
        /// <para>Delegates to Common's <see cref="AuthFailureGate.RecordExceptionOutcome"/>,
        /// which owns the Category-A sync-over-async hop (a host-context deadlock trap) in one
        /// tested place; this method supplies only Qobuz's service-specific classifier.</para>
        /// </summary>
        public static void RecordAuthOutcomeFromException(AuthFailureGate? gate, Exception ex)
            => gate?.RecordExceptionOutcome(ex, e => LooksLikeAuthFailure(e)
                ? new Lidarr.Plugin.Abstractions.Contracts.AuthFailure
                {
                    ErrorCode = (e as HttpRequestException)?.StatusCode?.ToString()
                                ?? (e as Exceptions.QobuzApiException)?.StatusCode?.ToString(),
                    Message = e.Message,
                }
                : null);

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
