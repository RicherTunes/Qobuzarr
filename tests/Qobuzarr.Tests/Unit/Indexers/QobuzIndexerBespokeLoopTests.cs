using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Xunit;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Security;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.Indexers;

/// <summary>
/// Characterization tests for the bespoke AccumulateAll loop in QobuzIndexer.FetchReleases.
/// The loop intentionally avoids Common's SearchPlanExecutor.ExecuteAsync for per-request
/// adaptive rate-limit accounting, ML metrics, and IndexerId stamping (see the comment block
/// at line ~207 in QobuzIndexer.cs). These tests pin its four observable contracts.
/// </summary>
public sealed class QobuzIndexerBespokeLoopTests
{
    private readonly Mock<IHttpClient> _httpClientMock;
    private readonly Mock<IIndexerStatusService> _statusServiceMock;
    private readonly Mock<IConfigService> _configServiceMock;
    private readonly Mock<IParsingService> _parsingServiceMock;
    private readonly Mock<IQobuzAuthenticationService> _authServiceMock;
    private readonly Mock<IQobuzApiClient> _apiClientMock;
    private readonly Mock<ISecureMLModelLoader> _mlLoaderMock;
    private readonly Logger _logger;
    private readonly QobuzSession _validSession;

    public QobuzIndexerBespokeLoopTests()
    {
        _httpClientMock = new Mock<IHttpClient>();
        _statusServiceMock = new Mock<IIndexerStatusService>();
        _configServiceMock = new Mock<IConfigService>();
        _parsingServiceMock = new Mock<IParsingService>();
        _authServiceMock = new Mock<IQobuzAuthenticationService>();
        _apiClientMock = new Mock<IQobuzApiClient>();
        _mlLoaderMock = new Mock<ISecureMLModelLoader>();
        _logger = LogManager.CreateNullLogger();

        // Null gate → IsAuthShortCircuited returns false (healthy path, no short-circuit)
        _apiClientMock.Setup(x => x.Gate).Returns((AuthFailureGate?)null);

        // Valid cached session → EnsureAuthenticatedAsync skips network auth call
        _validSession = new QobuzSession
        {
            UserId = "loop-test-user",
            AuthToken = "loop-test-token",
            AppId = "app-id",
            AppSecret = "app-secret",
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow,
        };
        _authServiceMock.Setup(x => x.GetCachedSession()).Returns(_validSession);
    }

    // ── contract 1 ──────────────────────────────────────────────────────────
    // When every tier throws, the loop must surface the failure as
    // InvalidOperationException (via SearchPlanExecutor.ThrowAllFailed) instead
    // of silently returning an empty list, so Lidarr can distinguish "all requests
    // failed" from "genuine no-match."
    [Fact]
    public async Task FetchReleases_AllRequestsThrow_ThrowsInvalidOperationException()
    {
        // Arrange
        _httpClientMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ThrowsAsync(new InvalidOperationException("simulated API failure"));

        var indexer = CreateIndexer();

        // Act + Assert
        var act = () => indexer.CallFetchReleases(_ => ChainWith(MakeDummyRequest()));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All*request(s) failed*");
    }

    // ── contract 2 ──────────────────────────────────────────────────────────
    // Partial-success: some tiers throw, others return results.
    // The loop must accumulate results from the successful tiers and NOT throw,
    // even though at least one request failed.
    [Fact]
    public async Task FetchReleases_SomeRequestsThrow_ReturnResultsFromSuccessfulOnes()
    {
        // Arrange – two requests in the same tier: first throws, second succeeds
        var req1 = MakeDummyRequest();
        var req2 = MakeDummyRequest();

        _httpClientMock
            .SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ThrowsAsync(new InvalidOperationException("first fails"))
            .ReturnsAsync(HttpTestHelpers.CreateResponse("{}", request: req2.HttpRequest));

        var expectedRelease = new ReleaseInfo { Title = "Partial Result Album" };
        var mockParser = new Mock<IParseIndexerResponse>();
        mockParser
            .Setup(x => x.ParseResponse(It.IsAny<IndexerResponse>()))
            .Returns(new List<ReleaseInfo> { expectedRelease });

        var indexer = CreateIndexer();
        indexer.SetTestParser(mockParser.Object);

        // Act
        var result = await indexer.CallFetchReleases(_ => ChainWith(req1, req2));

        // Assert – one result from the successful tier, no exception
        result.Should().ContainSingle()
            .Which.Title.Should().Be("Partial Result Album");
    }

    // ── contract 3 ──────────────────────────────────────────────────────────
    // Genuine-empty: all requests succeed (HTTP 200) but the parser returns no
    // releases. Must return an empty list, not throw.
    [Fact]
    public async Task FetchReleases_AllSucceedButParserReturnsEmpty_ReturnsEmptyNoThrow()
    {
        // Arrange
        var req = MakeDummyRequest();
        _httpClientMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ReturnsAsync(HttpTestHelpers.CreateResponse("{}", request: req.HttpRequest));

        var mockParser = new Mock<IParseIndexerResponse>();
        mockParser
            .Setup(x => x.ParseResponse(It.IsAny<IndexerResponse>()))
            .Returns(new List<ReleaseInfo>());

        var indexer = CreateIndexer();
        indexer.SetTestParser(mockParser.Object);

        // Act
        var result = await indexer.CallFetchReleases(_ => ChainWith(req));

        // Assert
        result.Should().BeEmpty();
    }

    // ── contract 4 ──────────────────────────────────────────────────────────
    // Cancellation: an OperationCanceledException thrown by the HTTP client must
    // propagate out of FetchReleases instead of being swallowed by the general
    // catch and re-wrapped as InvalidOperationException.
    //
    // RED: before the fix, the inner catch (Exception ex) swallowed OCE, and
    // ThrowAllFailed then raised InvalidOperationException(inner=OCE).
    // GREEN: after adding catch (OperationCanceledException) { throw; } before the
    // general catch in the inner loop, OCE propagates correctly.
    [Fact]
    public async Task FetchReleases_CancellationThrownByHttpClient_PropagatesOperationCanceledException()
    {
        // Arrange
        _httpClientMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ThrowsAsync(new OperationCanceledException("search cancelled"));

        var indexer = CreateIndexer();

        // Act + Assert – must propagate OCE, not wrap it in InvalidOperationException
        var act = () => indexer.CallFetchReleases(_ => ChainWith(MakeDummyRequest()));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchReleases_BaseParserPathUsesSuppressionStore()
    {
        // Arrange: no test parser override. This exercises FetchReleases -> base GetParser()
        // -> QobuzParser.ParseResponse, proving the production parser path is suppression-aware.
        var suppressed = QobuzAlbumBuilder.New()
            .WithId("suppressed-album")
            .WithTitle("Suppression Test Album")
            .WithArtist("Suppression Artist", "suppression-artist")
            .AsHiResFlac()
            .Build();

        var allowed = QobuzAlbumBuilder.New()
            .WithId("allowed-album")
            .WithTitle("Allowed Album")
            .WithArtist("Allowed Artist", "allowed-artist")
            .AsCdQualityFlac()
            .Build();

        var req = MakeDummyRequest();
        _httpClientMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ReturnsAsync(HttpTestHelpers.CreateResponse(SearchResponseJson(suppressed, allowed), request: req.HttpRequest));

        var indexer = CreateIndexer();
        indexer.SetSuppressionStore(new FakeSuppressionStore("suppressed-album"));

        // Act
        var result = await indexer.CallFetchReleases(_ => ChainWith(req));

        // Assert
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(r => r.Guid.Contains("allowed-album"));
    }

    [Fact]
    public async Task Test_WhenGateLatchedAndProbeExhausted_AttemptsAuthAndClearsGateOnSuccess()
    {
        var handler = new DefaultAuthFailureHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultAuthFailureHandler>.Instance);
        await handler.HandleFailureAsync(new Lidarr.Plugin.Abstractions.Contracts.AuthFailure
        {
            ErrorCode = "401",
            Message = "expired credentials"
        });
        var gate = new AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromHours(24));
        gate.TryAcquireProbeSlot(); // exhaust the background-loop probe budget

        _apiClientMock.Setup(x => x.Gate).Returns(gate);
        _authServiceMock
            .Setup(x => x.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
            .ReturnsAsync(_validSession);

        var indexer = CreateIndexer();
        indexer.Definition = new IndexerDefinition
        {
            Name = "Qobuzarr",
            Settings = new QobuzIndexerSettings
            {
                AuthMethod = (int)AuthenticationMethod.Token,
                UserId = "loop-test-user",
                AuthToken = "loop-test-token"
            }
        };
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        await indexer.CallTest(failures);

        failures.Should().BeEmpty();
        _authServiceMock.Verify(x => x.AuthenticateAsync(It.IsAny<QobuzCredentials>()), Times.Once);
        gate.IsHealthy.Should().BeTrue(
            "a successful explicit Test is the operator's remediation path and must clear the health warning");
    }

    // ── contract 5 (P0-04, live audit 2026-07) ──────────────────────────────
    // An HTTP-200 response whose body is unparseable must count as a FAILED
    // request in the loop's accounting. Before the fix the real parser logged
    // the parse error and returned [], succeeded++ still ran, and an
    // all-malformed wave surfaced as a misleading empty-success instead of
    // tripping SearchPlanExecutor.ThrowAllFailed. Uses the REAL QobuzParser
    // (no SetTestParser) so the parser->loop accounting integration is what is
    // actually proven.
    [Fact]
    public async Task FetchReleases_AllRequestsReturnMalformed200_RealParser_ThrowsAllFailed()
    {
        // Arrange – both requests "succeed" at the HTTP layer but carry a non-JSON body
        var req = MakeDummyRequest();
        _httpClientMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ReturnsAsync(HttpTestHelpers.CreateResponse("<html><body>502 Bad Gateway</body></html>", request: req.HttpRequest));

        var indexer = CreateIndexer(); // no test parser override → real QobuzParser

        // Act + Assert – the all-failed contract must surface, not an empty success
        var act = () => indexer.CallFetchReleases(_ => ChainWith(req));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All*request(s) failed*");
    }

    // Mixed wave: one malformed request plus one genuinely-empty valid response.
    // succeeded==1, so the all-failed contract must NOT fire and the result is a
    // legitimate empty list. Pins the mixed-outcome policy explicitly.
    [Fact]
    public async Task FetchReleases_MixedMalformedAndValidEmpty_RealParser_ReturnsEmptyNoThrow()
    {
        // Arrange – first request malformed, second a well-formed zero-result search
        var req1 = MakeDummyRequest();
        var req2 = MakeDummyRequest();
        _httpClientMock
            .SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
            .ReturnsAsync(HttpTestHelpers.CreateResponse("<html>upstream error</html>", request: req1.HttpRequest))
            .ReturnsAsync(HttpTestHelpers.CreateResponse("{\"albums\":{\"items\":[],\"total\":0,\"limit\":50,\"offset\":0}}", request: req2.HttpRequest));

        var indexer = CreateIndexer(); // real QobuzParser

        // Act
        var result = await indexer.CallFetchReleases(_ => ChainWith(req1, req2));

        // Assert
        result.Should().BeEmpty("one valid zero-result response means the wave was not an all-failed wave");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private ExposedQobuzIndexer CreateIndexer()
        => new(
            _httpClientMock.Object,
            _statusServiceMock.Object,
            _configServiceMock.Object,
            _parsingServiceMock.Object,
            _authServiceMock.Object,
            _apiClientMock.Object,
            _mlLoaderMock.Object,
            _logger);

    private static IndexerPageableRequestChain ChainWith(params IndexerRequest[] requests)
    {
        var chain = new IndexerPageableRequestChain();
        if (requests.Length > 0)
            chain.Add(requests);
        return chain;
    }

    private static IndexerRequest MakeDummyRequest()
        => new(new HttpRequest("http://test.invalid/search"));

    private static string SearchResponseJson(params QobuzAlbum[] albums)
        => JsonConvert.SerializeObject(new QobuzAlbumSearchResponse
        {
            Albums = new QobuzSearchResultContainer<QobuzAlbum>
            {
                Items = albums.ToList(),
            },
        });
}

/// <summary>
/// Test subclass that exposes the protected FetchReleases method and allows injecting
/// a controlled parser to avoid parsing real Qobuz JSON in loop-contract tests.
/// </summary>
internal sealed class ExposedQobuzIndexer : QobuzIndexer
{
    private IParseIndexerResponse? _testParser;
    private IRestrictedReleaseSuppressionStore? _suppressionStore;

    public ExposedQobuzIndexer(
        IHttpClient httpClient,
        IIndexerStatusService statusService,
        IConfigService configService,
        IParsingService parsingService,
        IQobuzAuthenticationService authService,
        IQobuzApiClient apiClient,
        ISecureMLModelLoader mlLoader,
        Logger logger)
        : base(httpClient, statusService, configService, parsingService, authService, apiClient, mlLoader, logger)
    { }

    public void SetTestParser(IParseIndexerResponse parser) => _testParser = parser;

    public void SetSuppressionStore(IRestrictedReleaseSuppressionStore suppressionStore)
        => _suppressionStore = suppressionStore;

    protected override IRestrictedReleaseSuppressionStore ReleaseSuppressionStore
        => _suppressionStore ?? base.ReleaseSuppressionStore;

    public override IParseIndexerResponse GetParser()
        => _testParser ?? base.GetParser();

    public Task<IList<ReleaseInfo>> CallFetchReleases(
        Func<IIndexerRequestGenerator, IndexerPageableRequestChain> selector)
        => FetchReleases(selector);

    public Task CallTest(List<FluentValidation.Results.ValidationFailure> failures)
        => Test(failures);
}

internal sealed class FakeSuppressionStore : IRestrictedReleaseSuppressionStore
{
    private readonly HashSet<string> _suppressed;

    public FakeSuppressionStore(params string[] suppressed)
    {
        _suppressed = new HashSet<string>(suppressed, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSuppressed(string albumId) => _suppressed.Contains(albumId);

    public Task SuppressAsync(
        string albumId,
        string trackId,
        TrackUnavailableReason reason,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
