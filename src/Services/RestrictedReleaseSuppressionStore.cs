using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Qobuz policy adapter over Common's durable terminal-release suppression store.
    /// Common owns persistence, TTL, bounds, and synchronous parser lookup; this type owns only the
    /// Qobuz-specific terminal reason classification.
    /// </summary>
    public sealed class RestrictedReleaseSuppressionStore : IRestrictedReleaseSuppressionStore
    {
        public const int DefaultMaxEntries = TerminalReleaseSuppressionStore.DefaultMaxEntries;

        public static readonly TimeSpan DefaultTtl = TerminalReleaseSuppressionStore.DefaultTtl;

        private readonly ITerminalReleaseSuppressionStore _inner;

        public RestrictedReleaseSuppressionStore(
            string filePath,
            TimeSpan? ttl = null,
            int? maxEntries = null,
            TimeProvider? clock = null,
            TimeSpan? refreshInterval = null)
            : this(new TerminalReleaseSuppressionStore(
                filePath,
                QobuzarrConstants.PluginName,
                ttl,
                maxEntries,
                clock,
                refreshInterval))
        {
        }

        internal RestrictedReleaseSuppressionStore(ITerminalReleaseSuppressionStore inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public static RestrictedReleaseSuppressionStore Shared => _shared.Value;

        public int Count => _inner is TerminalReleaseSuppressionStore store ? store.Count : 0;

        private static readonly Lazy<RestrictedReleaseSuppressionStore> _shared = new(
            () => new RestrictedReleaseSuppressionStore(
                TerminalReleaseSuppressionStore.ForPlugin(QobuzarrConstants.PluginName)),
            isThreadSafe: true);

        public bool IsSuppressed(string albumId) => _inner.IsSuppressed(albumId);

        public Task SuppressAsync(
            string albumId,
            string trackId,
            TrackUnavailableReason reason,
            CancellationToken cancellationToken = default)
        {
            if (!ShouldSuppress(reason))
            {
                return Task.CompletedTask;
            }

            return _inner.SuppressAsync(albumId, trackId, reason.ToString(), cancellationToken);
        }

        public static bool ShouldSuppress(TrackUnavailableReason reason)
            => reason.IsPermanentlyUnavailable();

        public Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default)
            => _inner.ClearAsync(albumId, cancellationToken);
    }

    public interface IRestrictedReleaseSuppressionStore
    {
        bool IsSuppressed(string albumId);

        Task SuppressAsync(string albumId, string trackId, TrackUnavailableReason reason, CancellationToken cancellationToken = default);

        Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default);
    }

    public sealed class NullRestrictedReleaseSuppressionStore : IRestrictedReleaseSuppressionStore
    {
        public static readonly NullRestrictedReleaseSuppressionStore Instance = new();

        private NullRestrictedReleaseSuppressionStore()
        {
        }

        public bool IsSuppressed(string albumId) => false;

        public Task SuppressAsync(string albumId, string trackId, TrackUnavailableReason reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
