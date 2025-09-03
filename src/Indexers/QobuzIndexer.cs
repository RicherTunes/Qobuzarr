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
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers.Core;
using Lidarr.Plugin.Common.Base;
using Lidarr.Plugin.Qobuzarr.Download;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Common.Services;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Refactored QobuzIndexer with decomposed responsibilities.
    /// Reduced from 1,121 lines to ~200 lines by extracting service classes.
    /// Follows Single Responsibility Principle with dedicated managers for:
    /// - Authentication (IndexerAuthenticationManager)
    /// - Rate Limiting (IndexerRateLimitManager) 
    /// - ML Optimization (IndexerMLManager)
    /// </summary>
    public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>, IDisposable
    {
        public override string Name => QobuzarrConstants.PluginName;
        // Protocol identifier
        public override string Protocol => nameof(QobuzarrDownloadProtocol);
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;

        // Decomposed service dependencies
        private readonly IIndexerAuthenticationManager _authManager;
        private readonly IIndexerRateLimitManager _rateLimitManager;
        private readonly IIndexerMLManager _mlManager;
        private readonly IQobuzApiClient _apiClient;
        private readonly StreamingIndexerMixin _mixin;
        
        // Cached instances for context sharing
        private QobuzRequestGenerator _requestGenerator;
        private QobuzParser _parser;
        private readonly Lazy<IPatternLearningEngine> _patternLearningEngine;

        public QobuzIndexer(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            ISecureMLModelLoader secureModelLoader,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            
            // Initialize decomposed service managers
            _authManager = new IndexerAuthenticationManager(authService, Settings, logger);
            _rateLimitManager = new IndexerRateLimitManager(logger);
            _mlManager = new IndexerMLManager(secureModelLoader, Settings, logger);
            
            // Shared mixin for incremental adoption of common features
            _mixin = new StreamingIndexerMixin(QobuzarrConstants.ServiceName);

            // Initialize ML optimizer lazily
            _patternLearningEngine = new Lazy<IPatternLearningEngine>(() => 
                _mlManager.CreateMLOptimizer(logger));

            // Wire API client with auth service and a credentials provider for re-auth (no behavior change for Qobuz)
            try
            {
                if (_apiClient is API.QobuzApiClient concrete)
                {
                    concrete.SetAuthenticationService(authService);
                    concrete.SetCredentialsProvider(async () => await Task.FromResult(BuildFallbackCredentialsFromSettings()));
                    // Wire pre-request pipeline: ensure session + auth params + signing
                    var preHandler = new Lidarr.Plugin.Qobuzarr.API.PreRequest.QobuzPreRequestHandler(
                        authService,
                        new Lidarr.Plugin.Qobuzarr.API.Signing.QobuzRequestSigner(logger),
                        async () => await Task.FromResult(BuildFallbackCredentialsFromSettings()),
                        logger);
                    concrete.SetPreRequestHandler(preHandler);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Non-fatal: Failed to wire auth service/credentials provider to API client");
            }
        }

        #region Lidarr Integration Points

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            if (_requestGenerator == null)
            {
                _requestGenerator = new QobuzRequestGenerator(
                    Settings, 
                    _logger, 
                    () => _authManager.GetCachedSession(), 
                    _patternLearningEngine.Value);
            }
            return _requestGenerator;
        }

        private Models.Authentication.QobuzCredentials BuildFallbackCredentialsFromSettings()
        {
            var creds = new Models.Authentication.QobuzCredentials();

            // Prefer email + password if provided (hash to MD5 as required by Qobuz)
            if (!string.IsNullOrWhiteSpace(Settings?.Email) && !string.IsNullOrWhiteSpace(Settings?.Password))
            {
                creds.Email = Settings.Email;
                creds.MD5Password = Utilities.HashingUtility.ComputePasswordMD5Hash(Settings.Password);
            }
            else if (!string.IsNullOrWhiteSpace(Settings?.UserId) && !string.IsNullOrWhiteSpace(Settings?.AuthToken))
            {
                // Fallback to UserId + AuthToken
                creds.UserId = Settings.UserId;
                creds.AuthToken = Settings.AuthToken;
            }

            // Optional app credentials (used if configured)
            if (!string.IsNullOrWhiteSpace(Settings?.AppId)) creds.AppId = Settings.AppId;
            if (!string.IsNullOrWhiteSpace(Settings?.AppSecret)) creds.AppSecret = Settings.AppSecret;

            return creds;
        }

        public override IParseIndexerResponse GetParser()
        {
            if (_parser == null)
            {
                _parser = new QobuzParser(Settings, _logger);
            }
            
            // Update context from request generator if available
            if (_requestGenerator != null)
            {
                var currentCriteria = _requestGenerator.GetCurrentSearchCriteria();
                if (currentCriteria != null)
                {
                    _parser.SetSearchContext(currentCriteria);
                }
            }
            
            return _parser;
        }

        protected override async Task<IList<ReleaseInfo>> FetchReleases(
            Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector, 
            bool isRecent = false)
        {
            var releases = new List<ReleaseInfo>();

            try
            {
                // Ensure authentication via delegated manager
                await _authManager.EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Apply rate limiting via delegated manager
                await _rateLimitManager.ApplyRateLimitAsync().ConfigureAwait(false);
                _rateLimitManager.RecordRequest();

                // Get request chain from generator
                var requestGenerator = GetRequestGenerator();
                var requestChain = pageableRequestChainSelector(requestGenerator);

                // Process each request in the chain
                foreach (var tier in requestChain.GetAllTiers())
                {
                    foreach (var request in tier)
                    {
                        try
                        {
                            // Rate limiting is handled centrally by the HTTP layer's adaptive limiter.
                            // Avoid stacking per-indexer pacing to reduce over-throttling.

                            var response = await _httpClient.ExecuteAsync(request.HttpRequest).ConfigureAwait(false);
                            var indexerResponse = new IndexerResponse(request, response);
                            
                            var parser = GetParser();
                            var parsedReleases = parser.ParseResponse(indexerResponse);
                            
                            if (parsedReleases?.Any() == true)
                            {
                                releases.AddRange(parsedReleases);
                                
                                // Log ML optimization metrics via delegated manager
                                _mlManager.LogMLPerformanceSummary();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error processing request: {0}", request.HttpRequest?.Url?.ToString() ?? "Unknown");
                        }
                    }
                }

                _logger.Info("🎵 Retrieved {0} releases from Qobuz", releases.Count);
                return releases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to fetch releases from Qobuz");
                throw;
            }
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            try
            {
                // Test authentication via delegated manager
                var (authSuccess, authError) = await _authManager.TestAuthenticationAsync().ConfigureAwait(false);
                if (!authSuccess)
                {
                    failures.Add(new ValidationFailure("Authentication", authError));
                    return;
                }

                // Test API connectivity with rate limiting
                await _rateLimitManager.ApplyRateLimitAsync().ConfigureAwait(false);
                
                // Use recent requests generator to validate request pipeline without relying on read-only criteria
                var testGenerator = GetRequestGenerator();
                var testRequests = testGenerator.GetRecentRequests();

                if (testRequests == null || !testRequests.GetAllTiers().Any())
                {
                    failures.Add(new ValidationFailure("Search", "No search requests generated"));
                    return;
                }

                _logger.Info("✅ Qobuzarr indexer test completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Indexer test failed");
                failures.Add(new ValidationFailure("Test", $"Test failed: {ex.Message}"));
            }
        }

        #endregion

        #region API Actions

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            switch (action?.ToLowerInvariant())
            {
                case "getavailablegenres":
                    return GetAvailableGenres();

                case "testauthentication":
                    return TestAuthentication();

                case "getmlperformance":
                    return _mlManager.GetMLPerformanceMetrics();

                case "getmlhealth":
                    return _mlManager.GetMLHealthStatus();

                case "getmlreport":
                    return _mlManager.GetMLDiagnosticReport();

                default:
                    return new { error = $"Unknown action: {action}" };
            }
        }

        private object GetAvailableGenres()
        {
            return new
            {
                genres = new[]
                {
                    "Jazz", "Classical", "Rock", "Pop", "Electronic", "Hip-Hop",
                    "Folk", "Blues", "Country", "Reggae", "World", "New Age"
                }
            };
        }

        private async Task<object> TestAuthentication()
        {
            try
            {
                var (success, error) = await _authManager.TestAuthenticationAsync().ConfigureAwait(false);
                return new
                {
                    success = success,
                    message = success ? "Authentication successful" : error
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    message = $"Authentication test failed: {ex.Message}"
                };
            }
        }

        #endregion

        #region Disposal

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_patternLearningEngine?.Value is IDisposable disposableEngine)
                {
                    disposableEngine.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
