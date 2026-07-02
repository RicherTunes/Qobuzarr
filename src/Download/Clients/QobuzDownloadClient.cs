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
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Services;
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
        private readonly IDownloadFileService _fileService;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadSummary _downloadSummary;
        private readonly IBatchProcessor _batchProcessor;
        private readonly Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService _trackDownloadService;
        // Process-wide tracker store — survives Lidarr's client re-instantiation cycles.
        // Static so a single store is shared across all QobuzDownloadClient instances
        // that DryIoc may create over the plugin's lifetime (matching the Tidalarr pattern).
        // Exposed as a protected virtual property so test subclasses can inject a fresh
        // per-test store without the static accumulation contaminating test isolation.
        private static readonly HostBridgeDownloadTrackerStore<QobuzDownloadItem> _staticTracker =
            HostBridgeDownloadTrackerStore<QobuzDownloadItem>.ForPlugin(
                "Qobuzarr",
                completedRetention: TimeSpan.FromMinutes(30),
                itemFactory: QobuzDownloadItem.FromHostBridgeDto);

        protected virtual HostBridgeDownloadTrackerStore<QobuzDownloadItem> Tracker => _staticTracker;

        protected virtual IRestrictedReleaseSuppressionStore ReleaseSuppressionStore
            => RestrictedReleaseSuppressionStore.Shared;

        protected virtual TimeSpan GracefulShutdownTimeout => TimeSpan.FromSeconds(30);

        protected virtual Task StabilizeBeforeCleanupDeleteAsync()
            => Task.Delay(QobuzConstants.Timing.FileOperations.FileSystemStabilizationDelayMs);

        protected virtual Task BeforeDownloadWorkerSideEffectsAsync(
            QobuzDownloadItem downloadItem,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected virtual Task BeforeCleanupDeleteInsideLifecycleGateAsync(QobuzDownloadItem removed)
            => Task.CompletedTask;

        // Centralised fire-and-forget download enqueue (Wave A). Replaces the bespoke
        // snapshot → Guid → tracker-insert → Task.Run pattern with Common's orchestrator,
        // shared by every RicherTunes streaming plugin. logger:null — the plugin's own
        // PluginLogContext already covers the surrounding scope. Static so it is shared
        // across Lidarr's client re-instantiation cycles (mirrors the Tidalarr pattern).
        private static readonly HostBridgeDownloadOrchestrator _downloadOrchestrator = new(logger: null);

        // Serializes tracker enqueue with cleanup's final same-path guard + contained delete
        // per normalized output path. This closes the check/delete window for replacement grabs
        // without making unrelated album grabs wait behind a slow recursive delete.
        private sealed class DownloadPathLifecycleGate
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);

            public int ReferenceCount { get; set; }
        }

        private sealed class DownloadPathLifecycleLease : IDisposable
        {
            private readonly string _key;
            private readonly DownloadPathLifecycleGate _gate;
            private int _disposed;

            public DownloadPathLifecycleLease(string key, DownloadPathLifecycleGate gate)
            {
                _key = key;
                _gate = gate;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    ReleaseDownloadPathLifecycleGate(_key, _gate, releaseSemaphore: true);
                }
            }
        }

        private static readonly object _downloadPathLifecycleGateLock = new();
        private static readonly Dictionary<string, DownloadPathLifecycleGate> _downloadPathLifecycleGates = new(StringComparer.Ordinal);

        private static async Task<DownloadPathLifecycleLease> AcquireDownloadPathLifecycleGateAsync(string outputPath)
        {
            var key = NormalizeDownloadPathLifecycleKey(outputPath);
            DownloadPathLifecycleGate gate;
            lock (_downloadPathLifecycleGateLock)
            {
                if (!_downloadPathLifecycleGates.TryGetValue(key, out gate!))
                {
                    gate = new DownloadPathLifecycleGate();
                    _downloadPathLifecycleGates.Add(key, gate);
                }

                gate.ReferenceCount++;
            }

            try
            {
                await gate.Semaphore.WaitAsync().ConfigureAwait(false);
                return new DownloadPathLifecycleLease(key, gate);
            }
            catch
            {
                ReleaseDownloadPathLifecycleGate(key, gate, releaseSemaphore: false);
                throw;
            }
        }

        private static void ReleaseDownloadPathLifecycleGate(
            string key,
            DownloadPathLifecycleGate gate,
            bool releaseSemaphore)
        {
            if (releaseSemaphore)
            {
                gate.Semaphore.Release();
            }

            lock (_downloadPathLifecycleGateLock)
            {
                if (gate.ReferenceCount > 0)
                {
                    gate.ReferenceCount--;
                }

                if (gate.ReferenceCount == 0 &&
                    _downloadPathLifecycleGates.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, gate))
                {
                    _downloadPathLifecycleGates.Remove(key);
                }
            }
        }

        private static string NormalizeDownloadPathLifecycleKey(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return string.Empty;
            }

            try
            {
                var normalized = NormalizeOutputDirectory(outputPath);
                return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? normalized.ToUpperInvariant()
                    : normalized;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
            {
                var fallback = Path.TrimEndingDirectorySeparator(outputPath.Trim());
                return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? fallback.ToUpperInvariant()
                    : fallback;
            }
        }

        // Single-flight gate for download-path re-authentication. Qobuz has no refresh token, so
        // renewal is a full re-login (login-rate-limited + scrapes the web player). Serialize renewals
        // so N concurrent downloads that all find the session stale trigger ONE re-auth, not N — mirrors
        // QobuzPreRequestHandler._renewGate on the indexer path.
        private readonly SemaphoreSlim _reauthGate = new SemaphoreSlim(1, 1);
        private string? _authenticatedCredentialFingerprint;
        private string? _authenticatedSessionFingerprint;

        // Negative-result re-auth cooldown. After a FAILED download-path re-login, subsequent queued
        // items within this window fail fast (with the existing actionable error) instead of each
        // performing another full re-login. Read/written only while _reauthGate is held. The window is
        // aligned with the AuthFailureGate probe interval (60s) used on the indexer path for parity.
        private static readonly TimeSpan ReauthFailureCooldown = TimeSpan.FromSeconds(60);
        private DateTime? _lastReauthFailureUtc;

        private QobuzDownloadItem? _lastQueuedItem;

        // Test seam for the download-path re-auth cooldown clock. Production uses the wall clock.
        protected virtual DateTime UtcNow => DateTime.UtcNow;

        public override string Name => QobuzarrConstants.PluginName;

        // Lidarr plugins branch host expects string protocol identifier
        public override string Protocol => nameof(QobuzarrDownloadProtocol);

        public QobuzDownloadClient(IQobuzAuthenticationService authService,
                                  IQobuzApiClient apiClient,
                                  IHttpClient httpClient,
                                  IDownloadFileService fileService,
                                  IConcurrencyManager concurrencyManager,
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
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
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
                // ── Pre-flight (synchronous; any failure throws BEFORE the download is enqueued
                //    so the host sees the error instead of a phantom queued item) ──

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

                // Resolve everything the background work needs BEFORE enqueue.
                var outputPath = BuildOutputPath(remoteAlbum);
                var effectiveSettings = GetEffectiveSettings();
                // Capture the configured download root for root-contained failed-download cleanup (F-10).
                // base.Settings can rely on Definition (not set in some unit-test paths), so resolve defensively.
                string? downloadRoot = null;
                try { downloadRoot = effectiveSettings?.DownloadPath; }
                catch (Exception ex) { _logger.Debug(ex, "Could not resolve download root for cleanup containment"); }
                var artist = remoteAlbum.Artist?.Name ?? "Unknown Artist";
                var reauthCredentials = BuildReauthCredentialsFromIndexer(indexer);

                // The orchestrator owns the fire-and-forget Task.Run, so we expose the in-flight
                // work as a TaskCompletionSource. QobuzDownloadItem.DownloadTask must stay set:
                // RemoveItem(deleteData:true) awaits it before cleanup (the Race-1 guard against
                // deleting in-flight .partial files → infinite re-grab loop) and DisposeAsync
                // awaits it for graceful shutdown.
                var workCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // HostBridgeDownloadOrchestrator (Wave A): snapshot → generate downloadId →
                // itemFactory → insert into tracker → fire-and-forget doWork → return id.
                string downloadId;
                using (await AcquireDownloadPathLifecycleGateAsync(outputPath).ConfigureAwait(false))
                {
                    downloadId = await _downloadOrchestrator.StartTrackedDownloadAsync<QobuzDownloadItem, QobuzDownloadSettings>(
                            settings: effectiveSettings,
                            tracker: Tracker,
                            // Identity snapshotter: PerformDownloadAsync reads its own settings via
                            // GetEffectiveSettings() internally rather than the passed snapshot, so there is
                            // no live-settings field to isolate here — a pass-through is correct. (Wave A.)
                            snapshotter: static s => s,
                            itemFactory: (_, id) =>
                            {
                                var downloadItem = new QobuzDownloadItem
                                {
                                    DownloadId = id,
                                    AlbumId = albumId,
                                    Title = albumTitle,
                                    Artist = artist,
                                    StartedAt = DateTime.UtcNow,
                                    OutputPath = outputPath,
                                    DownloadRoot = downloadRoot,
                                    CancellationTokenSource = new CancellationTokenSource(),
                                    ReauthCredentials = reauthCredentials,
                                    DownloadTask = workCompletion.Task
                                };
                                // Status defaults to Queued (HostBridgeDownloadItem initial state = 0 = Queued)

                                // The orchestrator inserts the item into the process-wide tracker
                                // (Tracker.AddOrReplace) before scheduling the background work, so the
                                // tracker is the single source of truth (Wave C). We only remember it as
                                // the last queued item for the GetItems() fallback. Run inside the factory
                                // so the assignment happens before Task.Run.
                                _lastQueuedItem = downloadItem;
                                return downloadItem;
                            },
                            doWork: async (_, _, item, cancellationToken) =>
                            {
                                try
                                {
                                    await BeforeDownloadWorkerSideEffectsAsync(item, cancellationToken)
                                        .ConfigureAwait(false);
                                    cancellationToken.ThrowIfCancellationRequested();
                                    await PerformDownloadAsync(item, cancellationToken).ConfigureAwait(false);
                                }
                                finally
                                {
                                    // Signal DownloadTask completion. The orchestrator persists the tracker
                                    // itself (see its finally → tracker.PersistSnapshot()), so we don't.
                                    workCompletion.TrySetResult();
                                }
                            },
                            new HostBridgeDownloadStartOptions<QobuzDownloadItem>
                            {
                                // Cancellation source of truth stays the item's own CancellationTokenSource:
                                // RemoveItem(downloadId) → trackerItem.Cancel() → CTS.Cancel(), and
                                // PerformDownloadAsync observes item.CancellationTokenSource.Token. Registering
                                // it here links the orchestrator's effective token to the same CTS so the
                                // orchestrator also observes cancellation. The item owns CTS disposal
                                // (QobuzDownloadItem.Dispose), so the registration carries no dispose action.
                                RegisterCancellation = (_, item) =>
                                    new HostBridgeDownloadCancellationRegistration(
                                        item.CancellationTokenSource?.Token ?? CancellationToken.None)
                            }).ConfigureAwait(false);
                }

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
                // Tracker.GetSnapshot() runs the 30-min retention sweep on completed/failed
                // items as a side-effect, then returns all still-live entries. Wave C: the
                // process-wide tracker is the SINGLE source of truth — the bespoke queue
                // service (and its separate active-downloads list) was removed.
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

                // Dedup by downloadId. The tracker is keyed by downloadId so the snapshot cannot
                // itself contain duplicates, but the dedup is retained defensively: reporting the
                // same downloadId twice gives Lidarr two queue entries for one download, which
                // wedges CompletedDownloadService at importPending (the completed download never
                // imports).
                var capturedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in snapshot)
                {
                    var ci = item.ToDownloadClientItem(clientId, clientName);
                    if (string.IsNullOrEmpty(ci.DownloadId) || capturedIds.Add(ci.DownloadId))
                    {
                        result.Add(ci);
                    }
                }

                if (result.Count == 0 && _lastQueuedItem != null)
                {
                    if (!string.IsNullOrWhiteSpace(_lastQueuedItem.DownloadId) &&
                        Tracker.TryGet(_lastQueuedItem.DownloadId, out var liveFallback))
                    {
                        result.Add(liveFallback.ToDownloadClientItem(clientId, clientName));
                    }
                    else
                    {
                        _lastQueuedItem = null;
                    }
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

        // Test seam: the most-recently-scheduled deferred-cleanup task (deleteData:true path).
        // Production never awaits it (cleanup is fire-and-forget); tests await it to make the
        // two-phase cleanup-race behaviour deterministic instead of polling on wall-clock time.
        internal Task? LastCleanupTask { get; private set; }

        private void RemoveItem(string downloadId, bool deleteData)
        {
            try
            {
                // Snapshot the tracked item BEFORE removing it: deferred cleanup needs its
                // DownloadTask (Race-1) and OutputPath, while GetItems() must keep seeing the item
                // until cleanup has either deleted or intentionally preserved its data.
                Tracker.TryGet(downloadId, out var trackerItem);

                // Clear _lastQueuedItem sentinel if it matches.
                if (_lastQueuedItem?.DownloadId == downloadId)
                {
                    _lastQueuedItem = null;
                }

                // Cancel active work if it's queued or downloading. The item's own
                // CancellationTokenSource is the cancel source of truth that PerformDownloadAsync
                // observes — signalling it lets the in-flight .partial writes unwind/finish so the
                // Phase-1 await below completes promptly.
                if (trackerItem != null && IsActiveTrackerItem(trackerItem))
                {
                    trackerItem.Cancel();
                }

                if (!deleteData)
                {
                    // No filesystem work — remove synchronously so the item disappears from
                    // GetItems() immediately. Common's Remove() with deleteData:false performs no
                    // Directory.Delete.
                    Tracker.Remove(downloadId, deleteData: false, out _,
                        ex => _logger.Warn(ex, "Error removing tracker entry for {0}", downloadId));
                    _logger.Debug("Removed download item: {0}", downloadId);
                    return;
                }

                // deleteData == true: defer the actual delete behind the in-flight download task so
                // we never delete in-flight .partial files (Race-1: deleting them mid-write makes
                // File.Move(partial, final) throw FileNotFoundException across every concurrent
                // track → AlbumDownloadException → Lidarr re-grabs → perpetual loop). After the
                // task settles, RemoveAndCleanupAsync removes the tracker entry, applies the
                // same-output active-download guard (Race-2), then deletes through Qobuz's
                // root-contained cleanup service. Fire-and-forget on the thread pool.
                LastCleanupTask = Task.Run(() => RemoveAndCleanupAsync(downloadId, trackerItem));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing download item: {0}", downloadId);
            }
        }

        /// <summary>
        /// Deferred two-phase cleanup for <c>RemoveItem(deleteData:true)</c>.
        /// Phase 1 (Race-1, qobuz-owned): await the removed item's <see cref="QobuzDownloadItem.DownloadTask"/>
        /// (30s budget) so in-flight <c>.partial</c> writes complete or observe cancellation before any
        /// filesystem delete; on timeout the tracker entry is removed but the data is left intact.
        /// Phase 2 (Race-2 + delete): remove the tracker entry, skip the delete when another tracked
        /// download is still active at the same <c>OutputPath</c>, then delete through Qobuz's
        /// root-contained cleanup service.
        /// </summary>
        internal async Task RemoveAndCleanupAsync(string downloadId, QobuzDownloadItem? trackerItem)
        {
            try
            {
                // Phase 1 — wait for THIS item's own download task to finish.
                var task = trackerItem?.DownloadTask;
                if (task != null && !task.IsCompleted)
                {
                    try
                    {
                        await task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        _logger.Warn(
                            "Timed out (30s) waiting for download task to complete before cleanup: {0}. " +
                            "Removing the tracker entry but skipping data deletion to avoid nuking " +
                            "potentially in-flight files.",
                            downloadId);
                        Tracker.Remove(downloadId, deleteData: false, out _,
                            ex => _logger.Warn(ex, "Error removing tracker entry for {0}", downloadId));
                        return;
                    }
                    catch (Exception)
                    {
                        // Task ended faulted/cancelled (expected for failed/cancelled downloads).
                        // The .partial writes have stopped, so it is safe to proceed to the delete.
                    }
                }

                // Phase 2 — remove the tracker entry, keep Common's same-path active-download
                // guard locally, then delete through Qobuz's root-contained cleanup service.
                // Do not call Tracker.Remove(deleteData:true): Common's generic delete cannot know
                // Qobuz's configured download root and would bypass the F-10 containment guard.
                var removedFromTracker = Tracker.Remove(downloadId, deleteData: false, out var removed,
                    ex => _logger.Warn(ex, "Error removing tracker entry for {0}", downloadId));
                if (!removedFromTracker || removed is null)
                {
                    _logger.Debug("Download item was already removed before cleanup: {0}", downloadId);
                    return;
                }

                if (string.IsNullOrWhiteSpace(removed.OutputPath))
                {
                    _logger.Debug("Download item had no output path to clean up: {0}", downloadId);
                    return;
                }

                if (HasActiveDownloadAtSameOutputPath(downloadId, removed.OutputPath))
                {
                    _logger.Debug(
                        "Skipping cleanup for {0}; another active download owns output path {1}",
                        downloadId,
                        removed.OutputPath);
                    return;
                }

                var cleanupRoot = ResolveCleanupRoot(removed);
                if (string.IsNullOrWhiteSpace(cleanupRoot))
                {
                    _logger.Warn(
                        "Skipping cleanup for {0}; no download root is available for output path {1}",
                        downloadId,
                        removed.OutputPath);
                    return;
                }

                await StabilizeBeforeCleanupDeleteAsync().ConfigureAwait(false);

                using (await AcquireDownloadPathLifecycleGateAsync(removed.OutputPath).ConfigureAwait(false))
                {
                    if (HasActiveDownloadAtSameOutputPath(downloadId, removed.OutputPath))
                    {
                        _logger.Debug(
                            "Skipping cleanup for {0}; another active download took ownership of output path {1}",
                            downloadId,
                            removed.OutputPath);
                        return;
                    }

                    await BeforeCleanupDeleteInsideLifecycleGateAsync(removed).ConfigureAwait(false);

                    if (HasActiveDownloadAtSameOutputPath(downloadId, removed.OutputPath))
                    {
                        _logger.Debug(
                            "Skipping cleanup for {0}; another active download took ownership of output path {1}",
                            downloadId,
                            removed.OutputPath);
                        return;
                    }

                    await _fileService.CleanupFailedDownloadAsync(removed.OutputPath, cleanupRoot)
                        .ConfigureAwait(false);
                    _logger.Debug("Removed download item and cleaned up data: {0}", downloadId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clean up download data for: {0}", downloadId);
            }
        }

        private string? ResolveCleanupRoot(QobuzDownloadItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.DownloadRoot))
            {
                return item.DownloadRoot;
            }

            return null;
        }

        private bool HasActiveDownloadAtSameOutputPath(string removedDownloadId, string outputPath)
        {
            foreach (var other in Tracker.GetSnapshot())
            {
                if (other is null ||
                    string.Equals(other.DownloadId, removedDownloadId, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(other.OutputPath) ||
                    !IsActiveTrackerItem(other))
                {
                    continue;
                }

                if (SameOutputDirectory(other.OutputPath, outputPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActiveTrackerItem(QobuzDownloadItem item)
            => item.GetStatus() is HostBridgeDownloadItemStatus.Queued
                or HostBridgeDownloadItemStatus.Downloading;

        private static bool SameOutputDirectory(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return string.Equals(NormalizeOutputDirectory(left), NormalizeOutputDirectory(right), comparison);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static string NormalizeOutputDirectory(string path)
            => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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

        private async Task PerformDownloadAsync(QobuzDownloadItem downloadItem, CancellationToken cancellationToken)
        {
            try
            {
                downloadItem.SetHostStatus(DownloadItemStatus.Downloading);
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Info("🎵 Starting download: {0} - {1}", downloadItem.Artist, downloadItem.Title);

                // Get effective settings (supports test subclasses)
                var settings = GetEffectiveSettings();

                // Ensure we have authentication
                try
                {
                    await EnsureAuthenticatedAsync(
                        downloadItem.ReauthCredentials,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    downloadItem.ReauthCredentials = null;
                }

                // Get album details
                cancellationToken.ThrowIfCancellationRequested();
                var album = await GetAlbumDetailsAsync(downloadItem.AlbumId).ConfigureAwait(false);
                if (album == null)
                {
                    throw new InvalidOperationException("Could not retrieve album details");
                }
                cancellationToken.ThrowIfCancellationRequested();

                downloadItem.Album = album;
                downloadItem.TotalSize = album.GetEstimatedTotalSize(settings.PreferredQuality);


                // Create output directory using file service
                _fileService.EnsureOutputDirectory(downloadItem.OutputPath);
                cancellationToken.ThrowIfCancellationRequested();

                // Download tracks (delegated to service)
                await _trackDownloadService.DownloadAlbumAsync(downloadItem, album, settings, cancellationToken).ConfigureAwait(false);

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
            catch (AlbumDownloadException ex)
            {
                await TryRecordTerminalReleaseSuppressionAsync(downloadItem, ex).ConfigureAwait(false);
                downloadItem.SetFailed($"Download failed: {ex.Message}");
                _logger.Error(ex, "Download failed: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
            catch (Exception ex)
            {
                downloadItem.SetFailed($"Download failed: {ex.Message}");
                _logger.Error(ex, "Download failed: {0} - {1}", downloadItem.Artist, downloadItem.Title);
            }
        }

        private async Task TryRecordTerminalReleaseSuppressionAsync(
            QobuzDownloadItem downloadItem,
            AlbumDownloadException exception)
        {
            var terminal = exception.TrackResults.FirstOrDefault(result =>
                !result.Success &&
                result.Reason.HasValue &&
                result.Reason.Value.IsPermanentlyUnavailable());

            if (terminal?.Reason == null)
            {
                return;
            }

            var albumId = string.IsNullOrWhiteSpace(exception.AlbumId)
                ? downloadItem.AlbumId
                : exception.AlbumId;

            if (string.IsNullOrWhiteSpace(albumId))
            {
                return;
            }

            try
            {
                await ReleaseSuppressionStore.SuppressAsync(
                    albumId,
                    terminal.TrackId ?? string.Empty,
                    terminal.Reason.Value,
                    CancellationToken.None).ConfigureAwait(false);

                _logger.Warn(
                    "Suppressed Qobuz album {0} from future searches after terminal track restriction ({1}: {2})",
                    albumId,
                    terminal.TrackId,
                    terminal.Reason.Value);
            }
            catch (Exception storeException)
            {
                _logger.Warn(
                    storeException,
                    "Failed to record terminal Qobuz release suppression for album {0}; preserving original download failure",
                    albumId);
            }
        }

        internal Task EnsureAuthenticatedAsync()
        {
            return EnsureAuthenticatedAsync(null, CancellationToken.None);
        }

        internal Task EnsureAuthenticatedAsync(QobuzCredentials? reauthCredentials)
        {
            return EnsureAuthenticatedAsync(reauthCredentials, CancellationToken.None);
        }

        internal async Task EnsureAuthenticatedAsync(QobuzCredentials? reauthCredentials, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _authService.GetCachedSession();
            var credentials = ResolveReauthCredentials(session, reauthCredentials);

            // Self-heal a missing or about-to-expire session by re-authenticating, rather than
            // throwing. NeedsRefresh() trips within 30 minutes of the synthetic 24h ExpiresAt, so
            // every album grabbed in the last half-hour of the window would otherwise fail and never
            // recover on the download path — unlike the indexer, which re-logs-in via
            // QobuzPreRequestHandler. Single-flighted so concurrent downloads cause ONE re-login.
            if (!CanUseCachedSession(session, credentials))
            {
                session = await ReauthenticateAsync(session, credentials, cancellationToken).ConfigureAwait(false);

                if (!CanUseCachedSession(session, credentials))
                {
                    throw new InvalidOperationException(
                        "No valid Qobuz authentication session available and automatic re-authentication " +
                        "failed (no usable credentials). Re-enter your Qobuz credentials in " +
                        "Settings -> Indexers -> Qobuzarr and save.");
                }
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

        /// <summary>
        /// Single-flighted re-authentication for the download path. Resolves credentials, performs a
        /// full re-login via the shared auth service (Qobuz has no refresh token), and returns the
        /// freshly cached session. Returns the original session unchanged when no credentials are
        /// available so the caller can surface an actionable error.
        /// </summary>
        private async Task<QobuzSession?> ReauthenticateAsync(QobuzSession? staleSession, QobuzCredentials? credentials, CancellationToken cancellationToken)
        {
            if (credentials == null || !credentials.IsValid())
            {
                // No credentials to re-auth with — caller decides how to surface this.
                return staleSession;
            }

            // Thread the download's cancellation token so a cancelled item short-circuits the gate
            // wait promptly instead of serialize-stalling behind an in-flight (possibly hung) re-login.
            await _reauthGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Recheck under the gate: another concurrent download may have already renewed it.
                var current = _authService.GetCachedSession();
                if (CanUseCachedSession(current, credentials))
                {
                    _lastReauthFailureUtc = null; // session recovered out-of-band — clear the cooldown
                    return current;
                }

                // Negative-result cooldown. Single-flight prevents SIMULTANEOUS re-logins, but with a
                // deep queue each item still enters here serially and would re-attempt a full re-login
                // (email auth re-scrapes the web player). On a persistent failure (bad password / 429)
                // that is N serialized logins → login-rate-limit / ban pressure. After a recent FAILED
                // attempt, fail fast for a short window with the same actionable error instead.
                if (_lastReauthFailureUtc is { } failedAt && (UtcNow - failedAt) < ReauthFailureCooldown)
                {
                    _logger.Debug(
                        "Skipping download-path re-authentication: an attempt failed {0:n0}s ago, within the {1:n0}s cooldown. " +
                        "Returning the stale session so the caller surfaces the actionable error rather than re-logging-in per queued item.",
                        (UtcNow - failedAt).TotalSeconds, ReauthFailureCooldown.TotalSeconds);
                    return current;
                }

                _logger.Debug("Download path re-authenticating with Qobuz (session was missing or about to expire)");

                QobuzSession? newSession;
                try
                {
                    newSession = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Arm the cooldown so the rest of the queue fails fast rather than each re-attempting.
                    _lastReauthFailureUtc = UtcNow;
                    var classification = HttpExceptionClassifier.Classify(ex);
                    _logger.Warn("Download path re-authentication failed: {0}", classification.Hint);

                    var afterFailure = _authService.GetCachedSession();
                    return CanUseCachedSession(afterFailure, credentials) ? afterFailure : staleSession;
                }

                if (newSession != null)
                {
                    // AuthenticateAsync already stores the session; re-read the cached copy so we
                    // observe exactly what subsequent calls will use.
                    var cached = _authService.GetCachedSession() ?? newSession;
                    RememberAuthenticatedSession(credentials, cached);
                    _lastReauthFailureUtc = null; // success clears the cooldown
                    return cached;
                }

                // A null result is also a failed attempt — arm the cooldown.
                _lastReauthFailureUtc = UtcNow;
                return CanUseCachedSession(current, credentials) ? current : staleSession;
            }
            finally
            {
                _reauthGate.Release();
            }
        }

        /// <summary>
        /// Resolves the credentials to use for download-path re-authentication. Prefers credentials
        /// built from settings (see <see cref="BuildReauthCredentialsFromSettings"/>); when none are
        /// available it falls back to rebuilding token-auth credentials from the stale session, which
        /// already carries UserId + AuthToken + AppId + AppSecret.
        /// </summary>
        private QobuzCredentials? ResolveReauthCredentials(QobuzSession? staleSession, QobuzCredentials? reauthCredentials)
        {
            var fromIndexer = QobuzCredentialFactory.Clone(reauthCredentials);
            if (fromIndexer != null && fromIndexer.IsValid())
            {
                return fromIndexer;
            }

            var fromSettings = BuildReauthCredentialsFromSettings();
            if (fromSettings != null && fromSettings.IsValid())
            {
                return QobuzCredentialFactory.Clone(fromSettings);
            }

            if (staleSession != null &&
                !string.IsNullOrWhiteSpace(staleSession.UserId) &&
                !string.IsNullOrWhiteSpace(staleSession.AuthToken))
            {
                return new QobuzCredentials
                {
                    UserId = staleSession.UserId,
                    AuthToken = staleSession.AuthToken,
                    AppId = staleSession.AppId,
                    AppSecret = staleSession.AppSecret
                };
            }

            return null;
        }

        private bool CanUseCachedSession(QobuzSession? session, QobuzCredentials? requestedCredentials)
        {
            if (session == null || session.NeedsRefresh())
            {
                return false;
            }

            if (requestedCredentials == null || !requestedCredentials.IsValid())
            {
                return true;
            }

            if (QobuzCredentialFactory.SessionMatchesCredentials(session, requestedCredentials))
            {
                RememberAuthenticatedSession(requestedCredentials, session);
                return true;
            }

            var requestedFingerprint = QobuzCredentialFactory.CreateCredentialFingerprint(requestedCredentials);
            var sessionFingerprint = QobuzCredentialFactory.CreateSessionFingerprint(session);
            return requestedFingerprint != null &&
                   sessionFingerprint != null &&
                   string.Equals(Volatile.Read(ref _authenticatedCredentialFingerprint), requestedFingerprint, StringComparison.Ordinal) &&
                   string.Equals(Volatile.Read(ref _authenticatedSessionFingerprint), sessionFingerprint, StringComparison.Ordinal);
        }

        private void RememberAuthenticatedSession(QobuzCredentials credentials, QobuzSession session)
        {
            var credentialFingerprint = QobuzCredentialFactory.CreateCredentialFingerprint(credentials);
            var sessionFingerprint = QobuzCredentialFactory.CreateSessionFingerprint(session);
            if (credentialFingerprint == null || sessionFingerprint == null)
            {
                return;
            }

            Volatile.Write(ref _authenticatedCredentialFingerprint, credentialFingerprint);
            Volatile.Write(ref _authenticatedSessionFingerprint, sessionFingerprint);
        }

        /// <summary>
        /// Builds re-authentication credentials from the effective download settings. The download
        /// settings do not currently expose credential fields (authentication is configured on the
        /// indexer), so the base implementation returns null; the re-auth path then falls back to the
        /// cached session's own token credentials. Exposed as a virtual seam for tests and for forward
        /// compatibility should credential fields ever be added to the download settings.
        /// </summary>
        protected virtual QobuzCredentials? BuildReauthCredentialsFromSettings() => null;

        private static QobuzCredentials? BuildReauthCredentialsFromIndexer(IIndexer indexer)
        {
            return QobuzCredentialFactory.TryFromIndexerSettings(
                indexer?.Definition?.Settings as QobuzIndexerSettings);
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

                // Cancel active downloads only. The tracker also retains terminal items briefly so
                // Lidarr can import them; mutating those to Failed during disposal loses that signal.
                var activeDownloads = Tracker.GetSnapshot()
                    .Where(IsActiveTrackerItem)
                    .ToList();
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
                    try
                    {
                        await Task.WhenAll(downloadTasks)
                            .WaitAsync(GracefulShutdownTimeout)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
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
                _reauthGate.Dispose();

                _logger.Debug("QobuzDownloadClient shutdown completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during QobuzDownloadClient disposal");
            }
        }
    }
}
