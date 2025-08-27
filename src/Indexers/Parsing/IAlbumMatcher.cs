using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Matches Qobuz albums with Lidarr albums and filters results.
    /// Extracted from QobuzParser to follow Single Responsibility Principle.
    /// </summary>
    public interface IAlbumMatcher
    {
        /// <summary>
        /// Finds the best matching Lidarr album for a Qobuz album.
        /// </summary>
        NzbDrone.Core.Music.Album FindBestMatchingAlbum(QobuzAlbum qobuzAlbum, List<NzbDrone.Core.Music.Album> lidarrAlbums, int qobuzYear);

        /// <summary>
        /// Calculates similarity between two titles.
        /// </summary>
        double CalculateTitleSimilarity(string title1, string title2);

        /// <summary>
        /// Cleans title for comparison purposes.
        /// </summary>
        string CleanTitleForComparison(string title);

        /// <summary>
        /// Determines if an album should be included in results.
        /// </summary>
        bool ShouldIncludeAlbum(QobuzAlbum album, QobuzIndexerSettings settings);

        /// <summary>
        /// Checks if an album is likely a single.
        /// </summary>
        bool IsLikelySingle(QobuzAlbum album);

        /// <summary>
        /// Checks if an album is likely a compilation.
        /// </summary>
        bool IsLikelyCompilation(QobuzAlbum album);
    }
}