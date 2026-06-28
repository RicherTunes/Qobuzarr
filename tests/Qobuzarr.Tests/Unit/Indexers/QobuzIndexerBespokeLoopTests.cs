using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Xunit;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Security;
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
}

/// <summary>
/// Test subclass that exposes the protected FetchReleases method and allows injecting
/// a controlled parser to avoid parsing real Qobuz JSON in loop-contract tests.
/// </summary>
internal sealed class ExposedQobuzIndexer : QobuzIndexer
{
    private IParseIndexerResponse? _testParser;

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

    public override IParseIndexerResponse GetParser()
        => _testParser ?? base.GetParser();

    public Task<IList<ReleaseInfo>> CallFetchReleases(
        Func<IIndexerRequestGenerator, IndexerPageableRequestChain> selector)
        => FetchReleases(selector);
}
