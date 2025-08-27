using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Creates ReleaseInfo objects from Qobuz album and quality data.
    /// Extracted from QobuzParser to follow Single Responsibility Principle.
    /// </summary>
    public interface IReleaseInfoFactory
    {
        /// <summary>
        /// Creates a ReleaseInfo object for a specific quality of an album.
        /// </summary>
        ReleaseInfo CreateReleaseInfoForQuality(QobuzAlbum album, QobuzAudioQuality quality, string originalQuery);

        /// <summary>
        /// Generates a download URL for the album and quality.
        /// </summary>
        string GenerateDownloadUrl(QobuzAlbum album, QobuzAudioQuality quality);

        /// <summary>
        /// Calculates estimated file size for the quality.
        /// </summary>
        long CalculateSizeForQuality(QobuzAlbum album, QobuzAudioQuality quality);

        /// <summary>
        /// Calculates reliable duration from track information.
        /// </summary>
        double CalculateReliableDuration(QobuzAlbum album);

        /// <summary>
        /// Generates info URL for the album.
        /// </summary>
        string GenerateInfoUrl(QobuzAlbum album);

        /// <summary>
        /// Gets categories for the album.
        /// </summary>
        System.Collections.Generic.List<int> GetCategories(QobuzAlbum album);
    }
}