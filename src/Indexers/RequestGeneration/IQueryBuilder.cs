using System.Collections.Generic;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Builds search queries from Lidarr search criteria.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public interface IQueryBuilder
    {
        /// <summary>
        /// Builds search queries for album search criteria.
        /// </summary>
        List<string> BuildAlbumSearchQueries(AlbumSearchCriteria searchCriteria);

        /// <summary>
        /// Builds search queries for artist search criteria.
        /// </summary>
        List<string> BuildArtistSearchQueries(ArtistSearchCriteria searchCriteria);

        /// <summary>
        /// Cleans and normalizes a search query.
        /// </summary>
        string CleanQuery(string query);

        /// <summary>
        /// Applies title case formatting to text.
        /// </summary>
        string ApplyTitleCase(string text);

        /// <summary>
        /// Extracts the core album title by removing common suffixes.
        /// </summary>
        string ExtractCoreAlbumTitle(string albumTitle);

        /// <summary>
        /// Tries to extract year from a query string.
        /// </summary>
        bool TryExtractYear(string query, out int year);
    }
}
