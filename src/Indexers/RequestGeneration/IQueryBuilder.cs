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
        /// Builds the ordered fallback query ladder for an album search
        /// (combined -> artist-only -> album-only), best-first.
        /// </summary>
        List<string> BuildAlbumSearchQueries(AlbumSearchCriteria searchCriteria);

        /// <summary>
        /// Builds search queries for artist search criteria.
        /// </summary>
        List<string> BuildArtistSearchQueries(ArtistSearchCriteria searchCriteria);

        /// <summary>
        /// Builds the artist-only catalogue fallback tier for capped album searches.
        /// </summary>
        IReadOnlyList<string> BuildArtistFallbackQueries(string artistName);

        /// <summary>
        /// Canonicalizes a single search term to its raw (NFC, control/symbol-aware) form.
        /// </summary>
        string CleanQuery(string query);
    }
}
