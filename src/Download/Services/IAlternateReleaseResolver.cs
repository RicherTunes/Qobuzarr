using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    public interface IAlternateReleaseResolver
    {
        /// <summary>
        /// Attempts to find an alternative playable track id for the given track (e.g., from another release/compilation).
        /// Returns null if no viable alternative is found.
        /// </summary>
        Task<string?> ResolvePlayableTrackIdAsync(QobuzTrack original, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to find an alternative playable track id with explicit limits and cache TTL.
        /// </summary>
        Task<string?> ResolvePlayableTrackIdAsync(
            QobuzTrack original,
            Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason originalReason,
            int maxCandidates,
            System.TimeSpan negativeCacheTtl,
            CancellationToken cancellationToken);
    }
}
