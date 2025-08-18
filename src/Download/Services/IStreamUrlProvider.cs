using System.Threading.Tasks;

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
        /// Determines if a URL is a preview/sample (not full track)
        /// </summary>
        bool IsPreviewOrSampleUrl(string url);
    }
}