using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
    {
        public override string Name => "Qobuzarr";
        public override DownloadProtocol Protocol => DownloadProtocol.Usenet;
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;

        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly Lazy<IPatternLearningEngine> _patternLearningEngine;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly object _rateLimitLock = new object();

        public QobuzIndexer(IHttpClient httpClient,
                           IIndexerStatusService indexerStatusService,
                           IConfigService configService,
                           IParsingService parsingService,
                           IQobuzAuthenticationService authService,
                           IQobuzApiClient apiClient,
                           Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _authService = authService;
            _apiClient = apiClient;
            
            // Initialize compiled ML query optimizer (pre-trained model, no ML.NET dependency)
            _patternLearningEngine = new Lazy<IPatternLearningEngine>(() => new CompiledMLQueryOptimizer(logger));
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new QobuzRequestGenerator(Settings, _logger, () => _authService.GetCachedSession(), _patternLearningEngine.Value);
        }

        public override IParseIndexerResponse GetParser()
        {
            return new QobuzParser(Settings, _logger);
        }

        protected override async Task<IList<ReleaseInfo>> FetchReleases(Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector, bool isRecent = false)
        {
            var releases = new List<ReleaseInfo>();
            const int TARGET_RESULTS = 50; // Reasonable number of results for album search

            try
            {
                // Ensure we have a valid session
                await EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Get the request generator and create requests
                var requestGenerator = GetRequestGenerator();
                var requests = pageableRequestChainSelector(requestGenerator);
                
                var allTiers = requests.GetAllTiers().ToList();
                _logger.Debug("Processing {0} search query tiers", allTiers.Count);

                // Process each tier (different query formats) in parallel for better performance
                var tierTasks = allTiers.Select(async tier =>
                {
                    var tierReleases = new List<ReleaseInfo>();
                    var pageRequests = tier.ToList();
                    
                    // Process pages sequentially within each tier, but with early termination
                    foreach (var pageableRequest in pageRequests)
                    {
                        // Early termination if we already have enough unique results
                        if (tierReleases.Count >= TARGET_RESULTS)
                        {
                            _logger.Debug("Early termination: Already have {0} releases for this query", tierReleases.Count);
                            break;
                        }

                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                        try
                        {
                            // Apply rate limiting
                            await ApplyRateLimitAsync().ConfigureAwait(false);
                            
                            _logger.Debug("Qobuz search: {0}", pageableRequest.Url);

                            var response = await FetchIndexerResponse(pageableRequest).ConfigureAwait(false);
                            
                            stopwatch.Stop();
                            _logger.Debug("Qobuz search completed in {0}ms", stopwatch.ElapsedMilliseconds);

                            if (response.Content.IsNotNullOrWhiteSpace())
                            {
                                var parser = GetParser();
                                var parsedReleases = parser.ParseResponse(response);
                                
                                _logger.Debug("Parsed {0} releases from Qobuz", parsedReleases.Count);
                                
                                // Check if we got fewer results than page size (indicates last page)
                                if (parsedReleases.Count < PageSize)
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
                            
                            // Handle authentication errors
                            if (ex.Message.Contains("401") || ex.Message.Contains("Authentication"))
                            {
                                _logger.Warn("Authentication error, clearing session");
                                _authService.ClearSession();
                                throw; // Re-throw auth errors to stop processing
                            }
                        }
                    }
                    
                    return tierReleases;
                }).ToList();

                // Wait for all tiers to complete
                var tierResults = await Task.WhenAll(tierTasks).ConfigureAwait(false);
                
                // Combine all results
                foreach (var tierReleases in tierResults)
                {
                    releases.AddRange(tierReleases);
                }

                // Remove duplicates across all queries
                var uniqueReleases = releases
                    .GroupBy(r => r.Guid)
                    .Select(g => g.First())
                    .OrderByDescending(r => r.PublishDate)
                    .ThenBy(r => r.Title)
                    .ToList();

                _logger.Info("Qobuz search returned {0} total releases ({1} unique after deduplication)", 
                    releases.Count, uniqueReleases.Count);
                    
                releases = uniqueReleases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Qobuz search");
            }

            return CleanupReleases(releases);
        }

        private async Task EnsureAuthenticatedAsync()
        {
            try
            {
                // Check if we have a cached valid session
                var cachedSession = _authService.GetCachedSession();
                if (cachedSession != null && !cachedSession.NeedsRefresh())
                {
                    _apiClient.SetSession(cachedSession);
                    return;
                }

                // Create credentials from settings
                var appId = Settings.GetAppId();
                var appSecret = Settings.GetAppSecret();
                
                // Log warning if user provided partial credentials (will fallback to defaults)
                if (!string.IsNullOrWhiteSpace(Settings.AppId) && string.IsNullOrWhiteSpace(Settings.AppSecret))
                {
                    _logger.Warn("Custom App ID provided without App Secret. Using default credentials to avoid authentication failures. " +
                                "To use custom credentials, provide both App ID and App Secret as a matching pair.");
                }
                
                var credentials = new QobuzCredentials
                {
                    AppId = appId,
                    AppSecret = appSecret
                };

                // Set authentication method specific fields
                if (Settings.IsEmailAuth())
                {
                    credentials.Email = Settings.Email;
                    credentials.MD5Password = QobuzAuthenticationService.HashPassword(Settings.Password);
                }
                else if (Settings.IsTokenAuth())
                {
                    credentials.UserId = Settings.UserId;
                    credentials.AuthToken = Settings.AuthToken;
                }
                else
                {
                    throw new InvalidOperationException("No valid authentication method configured");
                }

                // Authenticate and set session
                var session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
                _apiClient.SetSession(session);

                _logger.Info("Successfully authenticated with Qobuz as user {0}", session.UserId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to authenticate with Qobuz");
                throw;
            }
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            try
            {
                _logger.Info("Testing Qobuz indexer connection...");

                await EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Perform a simple search to test the connection
                var testSearchCriteria = new AlbumSearchCriteria
                {
                    Artist = new NzbDrone.Core.Music.Artist { Name = "test" },
                    AlbumTitle = "test"
                };

                var requestGenerator = GetRequestGenerator();
                var requests = requestGenerator.GetSearchRequests(testSearchCriteria);
                
                if (requests.GetAllTiers().Any())
                {
                    var firstRequest = requests.GetAllTiers().First().First();
                    var response = await FetchIndexerResponse(firstRequest).ConfigureAwait(false);

                    if (response.Content.IsNullOrWhiteSpace())
                    {
                        failures.Add(new ValidationFailure("", "No response from Qobuz API"));
                    }
                    else
                    {
                        _logger.Info("Qobuz connection test successful");
                    }
                }
                else
                {
                    failures.Add(new ValidationFailure("", "Failed to generate test request"));
                }
            }
            catch (QobuzAuthenticationException ex)
            {
                _logger.Error(ex, "Qobuz authentication test failed");
                failures.Add(new ValidationFailure("", $"Authentication failed: {ex.Message}"));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Qobuz connection test failed");
                failures.Add(new ValidationFailure("", $"Connection test failed: {ex.Message}"));
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            switch (action)
            {
                case "getGenres":
                    return GetAvailableGenres();
                case "testAuth":
                    return TestAuthentication();
                default:
                    return base.RequestAction(action, query);
            }
        }

        private object GetAvailableGenres()
        {
            return new[]
            {
                new { id = "", name = "All Genres" },
                new { id = "jazz", name = "Jazz" },
                new { id = "classical", name = "Classical" },
                new { id = "rock", name = "Rock" },
                new { id = "pop", name = "Pop" },
                new { id = "electronic", name = "Electronic" },
                new { id = "hip-hop", name = "Hip-Hop" },
                new { id = "folk", name = "Folk" },
                new { id = "blues", name = "Blues" },
                new { id = "country", name = "Country" },
                new { id = "world", name = "World Music" }
            };
        }

        private async Task<object> TestAuthentication()
        {
            try
            {
                await EnsureAuthenticatedAsync().ConfigureAwait(false);
                var session = _authService.GetCachedSession();
                
                return new
                {
                    success = true,
                    message = "Authentication successful",
                    userId = session?.UserId,
                    subscription = session?.Subscription?.GetTierDescription(),
                    expiresAt = session?.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    message = ex.Message
                };
            }
        }

        // Qobuz uses streaming protocol, no torrent support needed
        // Capabilities are defined through interface implementation
        
        /// <summary>
        /// Apply rate limiting based on configured requests per minute
        /// </summary>
        private async Task ApplyRateLimitAsync()
        {
            if (Settings.ApiRateLimit <= 0)
                return; // No rate limiting configured
                
            var minDelayMs = 60000.0 / Settings.ApiRateLimit; // Convert to minimum milliseconds between requests
            
            TimeSpan? delayTime = null;
            
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastRequest = (now - _lastRequestTime).TotalMilliseconds;
                
                if (timeSinceLastRequest < minDelayMs)
                {
                    var delayMs = (int)(minDelayMs - timeSinceLastRequest);
                    delayTime = TimeSpan.FromMilliseconds(delayMs);
                    _logger.Debug("Rate limiting: Waiting {0}ms (configured for {1} req/min)", delayMs, Settings.ApiRateLimit);
                }
                
                _lastRequestTime = DateTime.UtcNow;
            }
            
            // Perform delay outside the lock to avoid blocking other operations
            if (delayTime.HasValue)
            {
                await Task.Delay(delayTime.Value).ConfigureAwait(false);
            }
        }
    }
}