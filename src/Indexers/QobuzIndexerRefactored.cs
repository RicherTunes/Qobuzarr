using System;
using System.Collections.Generic;
using System.Linq;
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
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers.Services;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Refactored QobuzIndexer with proper separation of concerns
    /// </summary>
    public class QobuzIndexerRefactored : HttpIndexerBase<QobuzIndexerSettings>, IDisposable
    {
        public override string Name => "Qobuzarr";
        public override string Protocol => "Usenet";
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;

        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly IMLModelManager _mlModelManager;
        private readonly ISearchOrchestrator _searchOrchestrator;
        private IPatternLearningEngine _patternLearningEngine;
        private bool _disposed;

        public QobuzIndexerRefactored(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            IMLModelManager mlModelManager,
            ISearchOrchestrator searchOrchestrator,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _mlModelManager = mlModelManager ?? throw new ArgumentNullException(nameof(mlModelManager));
            _searchOrchestrator = searchOrchestrator ?? throw new ArgumentNullException(nameof(searchOrchestrator));
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            EnsureMLEngineInitialized();
            return new QobuzRequestGenerator(Settings, _logger, () => _authService.GetCachedSession(), _patternLearningEngine);
        }

        public override IParseIndexerResponse GetParser()
        {
            return new QobuzParser(Settings, _logger);
        }

        protected override async Task<IList<ReleaseInfo>> FetchReleases(
            Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector, 
            bool isRecent = false)
        {
            try
            {
                await EnsureAuthenticatedAsync().ConfigureAwait(false);
                
                var requestGenerator = GetRequestGenerator();
                var parser = GetParser();
                
                // Delegate to search orchestrator for complex search logic
                var releases = await _searchOrchestrator.FetchReleasesAsync(
                    pageableRequestChainSelector,
                    requestGenerator,
                    parser,
                    isRecent).ConfigureAwait(false);
                
                // Apply rate limiting if configured
                await _searchOrchestrator.ApplyRateLimitAsync(Settings.ApiRateLimit).ConfigureAwait(false);
                
                return releases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Qobuz search");
                return new List<ReleaseInfo>();
            }
        }

        private async Task EnsureAuthenticatedAsync()
        {
            try
            {
                var cachedSession = _authService.GetCachedSession();
                if (cachedSession != null && !cachedSession.NeedsRefresh())
                {
                    _apiClient.SetSession(cachedSession);
                    return;
                }

                var credentials = CreateCredentialsFromSettings();
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

        private QobuzCredentials CreateCredentialsFromSettings()
        {
            var appId = Settings.GetAppId();
            var appSecret = Settings.GetAppSecret();
            
            if (!string.IsNullOrWhiteSpace(Settings.AppId) && string.IsNullOrWhiteSpace(Settings.AppSecret))
            {
                _logger.Warn("Custom App ID provided without App Secret. Using default credentials.");
            }
            
            var credentials = new QobuzCredentials
            {
                AppId = appId,
                AppSecret = appSecret
            };

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

            return credentials;
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            try
            {
                _logger.Info("Testing Qobuz indexer connection...");

                await EnsureAuthenticatedAsync().ConfigureAwait(false);

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
                case "getMLPerformance":
                    return _mlModelManager.GetPerformanceStatistics();
                case "getMLHealth":
                    return _mlModelManager.GetHealthStatus();
                case "getMLReport":
                    return GetMLReportAction();
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

        private object GetMLReportAction()
        {
            try
            {
                var report = _mlModelManager.GetPerformanceReport();
                
                return new
                {
                    success = true,
                    message = "ML performance report generated successfully",
                    report = report,
                    generatedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating ML performance report");
                return new
                {
                    success = false,
                    message = ex.Message,
                    report = $"Error generating report: {ex.Message}",
                    generatedAt = DateTime.UtcNow
                };
            }
        }

        private void EnsureMLEngineInitialized()
        {
            if (_patternLearningEngine == null)
            {
                var modelType = (MLModelType)(Settings?.MLModelType ?? (int)MLModelType.Baseline);
                _patternLearningEngine = _mlModelManager.GetOptimizer(modelType);
                
                // Pass ML engine to search orchestrator for performance tracking
                if (_searchOrchestrator is SearchOrchestrator orchestrator)
                {
                    orchestrator.SetMLEngine(_patternLearningEngine);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        if (_mlModelManager is IDisposable disposableManager)
                        {
                            disposableManager.Dispose();
                        }
                        
                        _logger.Debug("QobuzIndexerRefactored disposed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error disposing QobuzIndexerRefactored resources");
                    }
                }
                
                _disposed = true;
            }
        }
    }
}