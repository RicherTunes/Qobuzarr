using System.Collections.Generic;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Creates HTTP requests for Qobuz API endpoints.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public interface IRequestFactory
    {
        /// <summary>
        /// Creates a search request for the given query and criteria.
        /// </summary>
        IndexerRequest CreateSearchRequest(string query, SearchCriteriaBase searchCriteria, QobuzSession session);

        /// <summary>
        /// Creates paged requests for pagination.
        /// </summary>
        IEnumerable<IndexerRequest> GetPagedRequests(IndexerRequest request);

        /// <summary>
        /// Clones a request with a different offset for pagination.
        /// </summary>
        IndexerRequest CloneRequestWithOffset(IndexerRequest originalRequest, int offset);

        /// <summary>
        /// Creates a mock search request for cached results.
        /// </summary>
        IndexerRequest CreateMockSearchRequest(AlbumSearchCriteria searchCriteria);
    }
}
