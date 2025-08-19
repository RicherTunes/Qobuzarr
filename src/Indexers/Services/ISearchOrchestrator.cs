using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Services
{
    /// <summary>
    /// Orchestrates search operations including rate limiting and result processing
    /// </summary>
    public interface ISearchOrchestrator
    {
        /// <summary>
        /// Fetches releases from Qobuz with ML optimization and rate limiting
        /// </summary>
        Task<IList<ReleaseInfo>> FetchReleasesAsync(
            Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector,
            IIndexerRequestGenerator requestGenerator,
            IParseIndexerResponse parser,
            bool isRecent = false);
        
        /// <summary>
        /// Applies rate limiting based on configuration
        /// </summary>
        Task ApplyRateLimitAsync(int apiRateLimit);
        
        /// <summary>
        /// Estimates API calls saved through optimization
        /// </summary>
        int EstimateApiCallsSaved(string queryUrl, int resultCount);
        
        /// <summary>
        /// Records ML performance metrics
        /// </summary>
        void RecordMLPerformance(string requestUrl, int resultCount);
    }
}