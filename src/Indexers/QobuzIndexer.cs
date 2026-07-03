using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Lidarr.Plugin.Common.Diagnostics;
using Lidarr.Plugin.Common.Services.Diagnostics;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Common.Services.Intelligence;
using Lidarr.Plugin.Qobuzarr.Download;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Common.Services;
using QobuzSuppressionServices = Lidarr.Plugin.Qobuzarr.Services;
using QobuzSuppressionStore = Lidarr.Plugin.Qobuzarr.Services.RestrictedReleaseSuppressionStore;

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

        // Decomposed service dependencies (lazy to avoid accessing Settings in constructor)
        private readonly Lazy<IIndexerAuthenticationManager> _authManager;
        private readonly IIndexerRateLimitManager _rateLimitManager;
        private readonly Lazy<IIndexerMLManager> _mlManager;
        private readonly IQobuzApiClient _apiClient;
        private readonly StreamingIndexerMixin _mixin;

        // Cached instances for context sharing
        private QobuzRequestGenerator? _requestGenerator;
        private QobuzParser? _parser;
        private readonly Lazy<IPatternLearningEngine> _patternLearningEngine;

        // Warn-once gate for constructor wire-up failures (process-global, single fixed key)
        private static readonly WarnOnce _wireWarn = new();

        protected virtual QobuzSuppressionServices.IRestrictedReleaseSuppressionStore ReleaseSuppressionStore
            => QobuzSuppressionStore.Shared;

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

            // CRITICAL: Do NOT access Settings property in constructor!
            // Settings requires Definition to be set first, which happens after DI construction.
            // Use GetSettingsSafe() or lazy initialization for anything that needs settings.

            // Initialize decomposed service managers with lazy evaluation to defer Settings access
            _authManager = new Lazy<IIndexerAuthenticationManager>(() =>
                new IndexerAuthenticationManager(authService, GetSettingsSafe(), logger));
            _rateLimitManager = new IndexerRateLimitManager(logger);
            _mlManager = new Lazy<IIndexerMLManager>(() =>
                new IndexerMLManager(secureModelLoader, GetSettingsSafe(), logger));

            // Shared mixin for incremental adoption of common features
            _mixin = new StreamingIndexerMixin(QobuzarrConstants.ServiceName);

            // Initialize ML optimizer lazily (depends on _mlManager which is also lazy)
            _patternLearningEngine = new Lazy<IPatternLearningEngine>(() =>
                _mlManager.Value.CreateMLOptimizer(logger));

            // Wire API client with auth service and a credentials provider for re-auth
            // Note: Credentials provider uses GetSettingsSafe() which is safe for deferred execution
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
                _wireWarn.TryWarn(
                    "wireup",
                    ex,
                    e => _logger.Warn(e, "Non-fatal: Failed to wire auth service/credentials provider to API client (this warning will not repeat for this process)"),
                    e => _logger.Debug(e, "Non-fatal: indexer wire-up failed again (warn-once already fired this process)"));
            }
        }

        /// <summary>
        /// Safely retrieves settings without throwing if Definition is not yet set.
        /// Use this instead of Settings property in any code path reachable during construction.
        /// </summary>
        private QobuzIndexerSettings GetSettingsSafe() =>
            Definition?.Settings as QobuzIndexerSettings ?? new QobuzIndexerSettings();

        #region Lidarr Integration Points

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            if (_requestGenerator == null)
            {
                _requestGenerator = new QobuzRequestGenerator(
                    GetSettingsSafe(),
                    _logger,
                    () => _authManager.Value.GetCachedSession(),
                    _patternLearningEngine.Value);
            }
            return _requestGenerator;
        }

        private Models.Authentication.QobuzCredentials BuildFallbackCredentialsFromSettings()
        {
            return QobuzCredentialFactory.TryFromIndexerSettings(GetSettingsSafe())
                ?? new Models.Authentication.QobuzCredentials();
        }

        public override IParseIndexerResponse GetParser()
        {
            if (_parser == null)
            {
                _parser = new QobuzParser(GetSettingsSafe(), _logger, ReleaseSuppressionStore);
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
            using var _scope = PluginLogContext.Push("Qobuzarr", "Search", provider: "qobuz:api");
            var releases = new List<ReleaseInfo>();

            _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}FetchReleases started");

            // AuthFailureGate pre-flight: when auth is latched bad, return empty immediately
            // rather than forwarding every Lidarr search-loop call to the Qobuz API. This
            // prevents the IP-ban amplification scenario (Lidarr drives fan-out searches;
            // each returns 401; each counts toward rate-limit / ban threshold).
            if (IsAuthShortCircuited(_apiClient.Gate))
            {
                _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}FetchReleases short-circuited: auth latched bad");
                return releases;
            }

            try
            {
                // Ensure authentication via delegated manager
                await _authManager.Value.EnsureAuthenticatedAsync().ConfigureAwait(false);

                // Apply rate limiting via delegated manager
                await _rateLimitManager.ApplyRateLimitAsync().ConfigureAwait(false);
                _rateLimitManager.RecordRequest();

                // Get request chain from generator
                var requestGenerator = GetRequestGenerator();
                var requestChain = pageableRequestChainSelector(requestGenerator);

                var attempted = 0;
                var succeeded = 0;
                Exception? lastError = null;

                // Why this loop is bespoke rather than Common's SearchPlanExecutor.ExecuteAsync delegate:
                // qobuz's per-request path interleaves concerns the generic delegate doesn't model —
                // adaptive HTTP rate-limit accounting, ML-optimization metric logging per successful tier,
                // the IndexerResponse/IParseIndexerResponse parser handshake, and per-release IndexerId
                // stamping. Semantically it is AccumulateAll (every tier/variant attempted, results merged;
                // no early stop), and it reuses the SAME all-failed contract as the executor via
                // SearchPlanExecutor.ThrowAllFailed below — so the "all requests failed ⇒ surface, don't
                // return a misleading empty" behavior stays identical to the consolidated plugins.
                foreach (var tier in requestChain.GetAllTiers())
                {
                    foreach (var request in tier)
                    {
                        attempted++;
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
                                var indexerId = Definition?.Id ?? 0;
                                if (indexerId == 0)
                                {
                                    _logger.Warn("Indexer Definition was not available; generated releases will have IndexerId=0 and grabs may fail.");
                                }

                                foreach (var release in parsedReleases)
                                {
                                    release.IndexerId = indexerId;
                                    if (string.IsNullOrWhiteSpace(release.Indexer))
                                    {
                                        release.Indexer = Definition?.Name;
                                    }
                                }

                                releases.AddRange(parsedReleases);

                                // Log ML optimization metrics via delegated manager
                                _mlManager.Value.LogMLPerformanceSummary();
                            }

                            succeeded++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            _logger.Error(ex, "Error processing request: {0}", request.HttpRequest?.Url?.ToString() ?? "Unknown");
                        }
                    }
                }

                // If EVERY attempted request threw, surface the failure instead of a misleading
                // empty result so Lidarr can distinguish "no matches" from "all requests failed".
                // (A request that parses to zero releases does not throw, so genuine empty results
                // are unaffected.) The outer catch rethrows it to Lidarr.
                SearchPlanExecutor.ThrowAllFailed(attempted, succeeded, lastError, "Qobuz search");

                _logger.Info($"{PluginLogContext.Current?.LinePrefix()}Retrieved {{0}} releases from Qobuz", releases.Count);
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
            using var _scope = PluginLogContext.Push("Qobuzarr", "Test");
            try
            {
                _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}Indexer test started");

                // Explicit Test is the operator's remediation path. Do not enforce the
                // background-loop probe budget here; a successful credential test must be
                // able to clear a latched health warning immediately after settings change.

                // Test authentication via delegated manager
                var (authSuccess, authError) = await _authManager.Value.TestAuthenticationAsync().ConfigureAwait(false);
                if (!authSuccess)
                {
                    failures.Add(new ValidationFailure("Authentication", authError));
                    return;
                }

                await RecordAuthSuccessAsync(_apiClient.Gate).ConfigureAwait(false);

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
                RecordAuthOutcomeFromException(_apiClient.Gate, ex);
                _logger.Error(ex, "❌ Indexer test failed");
                var classification = HttpExceptionClassifier.Classify(ex);
                string failureField = classification.Category == HttpFailureCategory.Auth
                    ? "Authentication"
                    : "Test";
                failures.Add(new ValidationFailure(failureField, classification.Hint));
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
                    return _mlManager.Value.GetMLPerformanceMetrics();

                case "getmlhealth":
                    return _mlManager.Value.GetMLHealthStatus();

                case "getmlreport":
                    return _mlManager.Value.GetMLDiagnosticReport();

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
                var (success, error) = await _authManager.Value.TestAuthenticationAsync().ConfigureAwait(false);
                if (success)
                {
                    await RecordAuthSuccessAsync(_apiClient.Gate).ConfigureAwait(false);
                }

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

        #region AuthFailureGate helpers
        // ------------------------------------------------------------------ //
        // Mirror the pattern in AppleMusicLidarrDownloadClient / AppleMusicIndexerAdapter.
        // Static for testability (callers can pin the contract without constructing a full indexer).
        // The gate is obtained from _apiClient.Gate; BridgeQobuzApiClient and the
        // Lidarr-native QobuzApiClient both expose plugin-local gates. Null is kept as
        // the defensive always-healthy convention for test fakes or unsupported adapters.
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns true when the gate is latched bad AND no probe slot is available.
        /// A null gate is always considered healthy (safe default when no gate is wired).
        /// </summary>
        public static bool IsAuthShortCircuited(AuthFailureGate? gate)
            => gate?.ShouldShortCircuit() ?? false;

        /// <summary>
        /// Record a successful explicit credential validation against the shared gate.
        /// </summary>
        public static ValueTask RecordAuthSuccessAsync(AuthFailureGate? gate)
            => gate?.HandleSuccessAsync() ?? ValueTask.CompletedTask;

        /// <summary>
        /// If <paramref name="ex"/> looks like a Qobuz auth failure (HTTP 401, or
        /// auth-endpoint HTTP 403), records
        /// a failure with <paramref name="gate"/>'s handler so the gate latches and subsequent
        /// calls short-circuit without touching the network.
        ///
        /// <para>Delegates to Common's <see cref="AuthFailureGate.RecordExceptionOutcome"/>,
        /// which owns the Category-A sync-over-async hop (a host-context deadlock trap) in one
        /// tested place; this method supplies only Qobuz's service-specific classifier.</para>
        /// </summary>
        public static void RecordAuthOutcomeFromException(AuthFailureGate? gate, Exception ex)
            => gate?.RecordExceptionOutcome(ex, e => LooksLikeAuthFailure(e)
                ? new Lidarr.Plugin.Abstractions.Contracts.AuthFailure
                {
                    ErrorCode = (e as HttpRequestException)?.StatusCode?.ToString()
                                ?? (e as Exceptions.QobuzApiException)?.StatusCode?.ToString(),
                    Message = e.Message,
                }
                : null);

        /// <summary>
        /// Returns true when <paramref name="ex"/> is recognisable as a Qobuz
        /// authentication failure:
        /// <list type="bullet">
        ///   <item>HTTP 401 Unauthorized</item>
        ///   <item>HTTP 403 Forbidden only when the exception identifies an authentication endpoint</item>
        /// </list>
        /// </summary>
        public static bool LooksLikeAuthFailure(Exception ex)
        {
            if (ex is HttpRequestException hre &&
                hre.StatusCode == HttpStatusCode.Unauthorized)
            {
                return true;
            }
            if (ex is Exceptions.QobuzApiException qae && qae.StatusCode is { } status)
            {
                return API.QobuzApiClient.ShouldRecordAuthFailure(status, qae.Endpoint);
            }
            return false;
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
                // Use IsValueCreated to avoid FORCING lazy construction of the pattern-learning
                // engine (CompiledMLQueryOptimizer + MLPerformanceMetrics, which starts a 1-min
                // Timer) purely to dispose it — e.g. on a Test()-only / schema-render indexer that
                // never searched. Mirrors the _mlManager guard below.
                if (_patternLearningEngine?.IsValueCreated == true && _patternLearningEngine.Value is IDisposable disposableEngine)
                {
                    disposableEngine.Dispose();
                }

                // Dispose the ML manager to release its metrics dictionary.
                // Use IsValueCreated to avoid forcing lazy evaluation during shutdown.
                if (_mlManager?.IsValueCreated == true && _mlManager.Value is IDisposable disposableMlManager)
                {
                    disposableMlManager.Dispose();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
