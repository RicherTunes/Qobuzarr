using System.Collections.Generic;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Parses Qobuz API responses into release information.
    /// Extracted from QobuzParser to follow Single Responsibility Principle.
    /// </summary>
    public interface IResponseParser
    {
        /// <summary>
        /// Parses album search response into releases.
        /// </summary>
        IEnumerable<ReleaseInfo> ParseAlbumSearchResponse(QobuzAlbumSearchResponse response, string originalQuery);

        /// <summary>
        /// Parses general search response into releases.
        /// </summary>
        IEnumerable<ReleaseInfo> ParseGeneralSearchResponse(QobuzSearchResponse response, string originalQuery);

        /// <summary>
        /// Converts a single Qobuz album into multiple release entries (one per quality).
        /// </summary>
        IEnumerable<ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album, string originalQuery);
    }
}