using System.Collections.Concurrent;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Thread-safe per-album accumulator that records the classified <see cref="TrackUnavailableReason"/>
    /// for every track that didn't land on disk for a known reason (preview-only, no-quality-available,
    /// or a classified rights restriction), as opposed to a genuinely unclassified/
    /// unknown hard failure.
    ///
    /// <para>Wave B routes downloads through Common's orchestrator, whose <c>TrackDownloadResult</c> carries
    /// only success/failure — not Qobuz's <see cref="TrackUnavailableReason"/>. The stream-resolution
    /// delegate (<c>getStream</c>) records every classified <see cref="TrackUnavailableException"/> reason
    /// here so the album summary, <c>AlbumDownloadException</c> reporting, and the skipped-vs-failed
    /// accounting stay faithful to the prior bespoke loop. A recorded track still leaves the album
    /// incomplete (it never lands on disk), so the completion policy treats it identically to a failure for
    /// the pass/fail decision — the distinction feeds terminal suppression after
    /// <c>TrackDownloadService.DownloadAlbumAsync</c> throws and improves reporting, not the underlying
    /// threshold math.</para>
    /// </summary>
    public sealed class QobuzTrackClassifier
    {
        private readonly ConcurrentDictionary<string, TrackUnavailableReason> _skipped = new();

        /// <summary>Records a track's classified unavailability reason.</summary>
        public void RecordSkipped(string trackId, TrackUnavailableReason reason)
        {
            if (!string.IsNullOrEmpty(trackId))
            {
                _skipped[trackId] = reason;
            }
        }

        /// <summary>True when the track was recorded as a skip (not a hard failure).</summary>
        public bool IsSkipped(string trackId)
            => !string.IsNullOrEmpty(trackId) && _skipped.ContainsKey(trackId);

        /// <summary>The skip reason for a track, or null if it wasn't skipped.</summary>
        public TrackUnavailableReason? GetReason(string trackId)
            => !string.IsNullOrEmpty(trackId) && _skipped.TryGetValue(trackId, out var reason)
                ? reason
                : (TrackUnavailableReason?)null;

        /// <summary>Number of distinct tracks recorded as skipped.</summary>
        public int SkippedCount => _skipped.Count;
    }
}
