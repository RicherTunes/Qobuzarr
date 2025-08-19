using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Services
{
    /// <summary>
    /// Orchestrates search operations including rate limiting and result processing
    /// </summary>
    public class SearchOrchestrator : ISearchOrchestrator
    {
        private readonly IHttpClient _httpClient;
        private readonly IQobuzAuthenticationService _authService;
        private readonly IMLModelManager _mlModelManager;
        private readonly Logger _logger;
        private readonly object _rateLimitLock = new object();
        private DateTime _lastRequestTime = DateTime.MinValue;
        private IPatternLearningEngine _currentMLEngine;

        public SearchOrchestrator(
            IHttpClient httpClient,
            IQobuzAuthenticationService authService,
            IMLModelManager mlModelManager,
            Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _mlModelManager = mlModelManager ?? throw new ArgumentNullException(nameof(mlModelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IList<ReleaseInfo>> FetchReleasesAsync(
            Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector,
            IIndexerRequestGenerator requestGenerator,
            IParseIndexerResponse parser,
            bool isRecent = false)
        {
            var releases = new List<ReleaseInfo>();
            const int TARGET_RESULTS = 50;
            const int PAGE_SIZE = 100;

            try
            {
                var requests = pageableRequestChainSelector(requestGenerator);
                var allTiers = requests.GetAllTiers().ToList();
                _logger.Debug("Processing {0} search query tiers", allTiers.Count);

                var tierTasks = allTiers.Select(async tier =>
                {
                    var tierReleases = new List<ReleaseInfo>();
                    var pageRequests = tier.ToList();
                    
                    foreach (var pageableRequest in pageRequests)
                    {
                        if (tierReleases.Count >= TARGET_RESULTS)
                        {
                            _logger.Debug("Early termination: Already have {0} releases for this query", tierReleases.Count);
                            break;
                        }

                        var stopwatch = Stopwatch.StartNew();

                        try
                        {
                            _logger.Debug("Qobuz search: {0}", pageableRequest.Url);

                            var response = await FetchIndexerResponseAsync(pageableRequest).ConfigureAwait(false);
                            
                            stopwatch.Stop();
                            _logger.Debug("Qobuz search completed in {0}ms", stopwatch.ElapsedMilliseconds);

                            if (response.Content.IsNotNullOrWhiteSpace())
                            {
                                var parsedReleases = parser.ParseResponse(response);
                                
                                _logger.Debug("Parsed {0} releases from Qobuz", parsedReleases.Count);
                                
                                RecordMLPerformance(pageableRequest.Url.ToString(), parsedReleases.Count);
                                
                                if (parsedReleases.Count < PAGE_SIZE)
                                {
                                    tierReleases.AddRange(parsedReleases);
                                    _logger.Debug("Last page reached with {0} results", parsedReleases.Count);
                                    break;
                                }
                                
                                tierReleases.AddRange(parsedReleases);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error fetching releases from Qobuz");
                            
                            if (ex.Message.Contains("401") || ex.Message.Contains("Authentication"))
                            {
                                _logger.Warn("Authentication error, clearing session");
                                _authService.ClearSession();
                                throw;
                            }
                        }
                    }
                    
                    return tierReleases;
                }).ToList();

                var tierResults = await Task.WhenAll(tierTasks).ConfigureAwait(false);
                
                foreach (var tierReleases in tierResults)
                {
                    releases.AddRange(tierReleases);
                }

                var uniqueReleases = releases
                    .GroupBy(r => r.Guid)
                    .Select(g => g.First())
                    .OrderByDescending(r => r.PublishDate)
                    .ThenBy(r => r.Title)
                    .ToList();

                _logger.Info("Qobuz search returned {0} total releases ({1} unique after deduplication)", 
                    releases.Count, uniqueReleases.Count);
                    
                if (_currentMLEngine != null && uniqueReleases.Count > 0)
                {
                    LogMLPerformanceSummary();
                }
                    
                return CleanupReleases(uniqueReleases);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Qobuz search");
                return new List<ReleaseInfo>();
            }
        }

        public async Task ApplyRateLimitAsync(int apiRateLimit)
        {
            if (apiRateLimit <= 0)
                return;
                
            var minDelayMs = 60000.0 / apiRateLimit;
            
            TimeSpan? delayTime = null;
            
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastRequest = (now - _lastRequestTime).TotalMilliseconds;
                
                if (timeSinceLastRequest < minDelayMs)
                {
                    var delayMs = (int)(minDelayMs - timeSinceLastRequest);
                    delayTime = TimeSpan.FromMilliseconds(delayMs);
                    _logger.Debug("Rate limiting: Waiting {0}ms (configured for {1} req/min)", delayMs, apiRateLimit);
                }
                
                _lastRequestTime = DateTime.UtcNow;
            }
            
            if (delayTime.HasValue)
            {
                await Task.Delay(delayTime.Value).ConfigureAwait(false);
            }
        }

        public int EstimateApiCallsSaved(string queryUrl, int resultCount)
        {
            if (resultCount > 20)
                return 2;
            else if (resultCount > 5)
                return 1;
            else
                return 0;
        }

        public void RecordMLPerformance(string requestUrl, int resultCount)
        {
            if (_currentMLEngine == null)
                return;

            try
            {
                var estimatedSavedCalls = EstimateApiCallsSaved(requestUrl, resultCount);
                
                if (_currentMLEngine is CompiledMLQueryOptimizer compiledOptimizer)
                {
                    compiledOptimizer.RecordApiOptimization(estimatedSavedCalls, estimatedSavedCalls + 1);
                    
                    if (resultCount > 0)
                        compiledOptimizer.RecordCacheHit();
                    else
                        compiledOptimizer.RecordCacheMiss();
                }
                else if (_currentMLEngine is HybridMLQueryOptimizer hybridOptimizer)
                {
                    hybridOptimizer.RecordApiOptimization(estimatedSavedCalls, estimatedSavedCalls + 1);
                    
                    if (resultCount > 0)
                        hybridOptimizer.RecordCacheHit();
                    else
                        hybridOptimizer.RecordCacheMiss();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error recording ML performance metrics");
            }
        }

        public void SetMLEngine(IPatternLearningEngine engine)
        {
            _currentMLEngine = engine;
        }

        private async Task<IndexerResponse> FetchIndexerResponseAsync(IndexerRequest request)
        {
            var httpRequest = new HttpRequest(request.Url.ToString())
            {
                Headers = request.HttpRequest.Headers,
                Method = request.HttpRequest.Method
            };

            var response = await _httpClient.ExecuteAsync(httpRequest).ConfigureAwait(false);
            
            return new IndexerResponse(request, response);
        }

        private void LogMLPerformanceSummary()
        {
            try
            {
                if (_currentMLEngine != null)
                {
                    var stats = _currentMLEngine.GetStatistics();
                    
                    _logger.Debug("ML Performance Summary - Accuracy: {0:P1}, Total Predictions: {1}", 
                        stats.Accuracy, stats.TotalPredictions);
                    
                    if (stats.TotalPredictions % 100 == 0 && stats.TotalPredictions > 0)
                    {
                        if (_currentMLEngine is CompiledMLQueryOptimizer compiledOptimizer)
                        {
                            var healthStatus = compiledOptimizer.GetPerformanceHealth();
                            _logger.Info("ML Health Status: {0} (Score: {1}/100) - {2} issues", 
                                healthStatus.Status, healthStatus.Score, healthStatus.Issues.Count);
                                
                            if (healthStatus.Issues.Count > 0)
                            {
                                _logger.Warn("ML Performance Issues: {0}", string.Join(", ", healthStatus.Issues));
                            }
                        }
                        else if (_currentMLEngine is HybridMLQueryOptimizer hybridOptimizer)
                        {
                            var healthStatus = hybridOptimizer.GetPerformanceHealth();
                            _logger.Info("Hybrid ML Health Status: {0} (Score: {1}/100) - {2} issues", 
                                healthStatus.Status, healthStatus.Score, healthStatus.Issues.Count);
                                
                            if (healthStatus.Issues.Count > 0)
                            {
                                _logger.Warn("Hybrid ML Performance Issues: {0}", string.Join(", ", healthStatus.Issues));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error logging ML performance summary");
            }
        }

        private IList<ReleaseInfo> CleanupReleases(IList<ReleaseInfo> releases)
        {
            // Implement any additional cleanup logic here
            return releases;
        }
    }
}