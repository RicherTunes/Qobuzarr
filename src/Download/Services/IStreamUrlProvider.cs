using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for obtaining streaming URLs from Qobuz API with quality fallback
    /// </summary>
    public interface IStreamUrlProvider
    {
        /// <summary>
        /// Gets streaming URL for a track with automatic quality fallback
        /// </summary>
        /// <param name="trackId">Qobuz track ID</param>
        /// <param name="preferredQuality">Preferred quality format ID</param>
        /// <returns>Streaming URL or null if track is unavailable</returns>
        Task<string> GetStreamUrlAsync(string trackId, int preferredQuality);

        /// <summary>
        /// Gets streaming URL for a track with enhanced logging using track and album context
        /// </summary>
        /// <param name="track">Full track object for enhanced logging</param>
        /// <param name="album">Album context for enhanced logging</param>
        /// <param name="preferredQuality">Preferred quality format ID</param>
        /// <returns>Streaming URL or null if track is unavailable</returns>
        Task<string> GetStreamUrlAsync(QobuzTrack track, QobuzAlbum album, int preferredQuality);

        /// <summary>
        /// Determines if a URL is a preview/sample (not full track)
        /// </summary>
        bool IsPreviewOrSampleUrl(string url);
    }
}
