using System.Net;
using FluentAssertions;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// P0-04 (live audit 2026-07): a malformed / DTO-drifted / explicit-error HTTP-200 search
    /// response used to be logged and converted into an EMPTY release list. QobuzIndexer's
    /// bespoke loop counts any non-throwing parse as a successful request (succeeded++ is
    /// unconditional after ParseResponse), so an all-malformed wave never tripped
    /// SearchPlanExecutor.ThrowAllFailed and surfaced as a misleading "no results" instead of
    /// an indexer failure — defeating Lidarr's indexer-health signal.
    ///
    /// Corrected contract pinned here:
    ///  - Unparseable / wrong-shape / explicit-error 200 responses THROW
    ///    <see cref="QobuzInvalidSearchResponseException"/> (routed into per-request failure
    ///    accounting by the loop).
    ///  - Genuine zero-result responses (valid search shape, empty items) still return an
    ///    empty list without throwing.
    ///  - Non-OK statuses keep the historical return-empty behavior (the HTTP layer throws
    ///    before the parser on the live path; that branch is deliberately out of scope).
    /// </summary>
    public class QobuzParserFailureAccountingTests
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public QobuzParserFailureAccountingTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            _logger = LogManager.CreateNullLogger();
        }

        private QobuzParser CreateParser() => new QobuzParser(_settings, _logger);

        private static IndexerResponse ResponseWithBody(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var httpResponse = new HttpResponse(
                new HttpRequest("http://test.qobuz.com/api"),
                new HttpHeader(),
                body,
                status);

            return new IndexerResponse(
                new IndexerRequest("http://test.qobuz.com/api", new HttpAccept("application/json")),
                httpResponse);
        }

        [Fact]
        public void ParseResponse_WithNonJsonBody_ThrowsInvalidSearchResponse()
        {
            var parser = CreateParser();
            var response = ResponseWithBody("<html><body>502 Bad Gateway from upstream proxy</body></html>");

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "a 200 response whose body is not JSON is a failed request, not an empty search result");
        }

        [Fact]
        public void ParseResponse_WithEmptyBody_ThrowsInvalidSearchResponse()
        {
            var parser = CreateParser();
            var response = ResponseWithBody(string.Empty);

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "a genuine Qobuz zero-result search returns a JSON envelope, never a zero-byte body");
        }

        [Fact]
        public void ParseResponse_WithJsonMatchingNoKnownSearchShape_ThrowsInvalidSearchResponse()
        {
            // Valid JSON, but neither an album-search nor a general-search envelope (DTO drift /
            // API error object). Historically this deserialized into an all-null DTO whose
            // vacuous IsSuccess (null status => true) silently produced an empty result.
            var parser = CreateParser();
            var response = ResponseWithBody("{\"error\":\"service temporarily unavailable\",\"code\":503}");

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "JSON that matches no known Qobuz search shape is DTO drift or an error object, not an empty result");
        }

        [Fact]
        public void ParseResponse_WithExplicitErrorStatus_ThrowsInvalidSearchResponse()
        {
            var parser = CreateParser();
            var response = ResponseWithBody(
                "{\"status\":\"error\",\"message\":\"something broke\",\"albums\":{\"items\":[],\"total\":0}}");

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "an explicit error status from the API is a failed request even when the envelope shape is present");
        }

        [Fact]
        public void ParseResponse_WithTracksOnlyEnvelope_ThrowsInvalidSearchResponse()
        {
            // Adversarial-review finding (gpt-5.6-terra, 2026-07): the only production endpoint is
            // /album/search (RequestFactory.SEARCH_ENDPOINT), so a tracks-only envelope is never a
            // legitimate response for this parser. Accepting it as "recognized" would convert a
            // wrong-envelope wave back into successful empties — the exact masked-failure P0-04 removes.
            var parser = CreateParser();
            var response = ResponseWithBody("{\"status\":\"success\",\"tracks\":{\"items\":[],\"total\":1}}");

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "a tracks-only envelope cannot be a valid /album/search response");
        }

        [Fact]
        public void ParseResponse_WithArtistsOnlyEnvelope_ThrowsInvalidSearchResponse()
        {
            var parser = CreateParser();
            var response = ResponseWithBody("{\"artists\":{\"items\":[],\"total\":0}}");

            var act = () => parser.ParseResponse(response);

            act.Should().Throw<QobuzInvalidSearchResponseException>(
                "an artists-only envelope cannot be a valid /album/search response");
        }

        [Fact]
        public void ParseResponse_WithEmptyAlbumsContainerObject_ReturnsEmptyWithoutThrow()
        {
            // Deliberate ambiguity policy: {"albums":{}} deserializes with Items defaulting to an
            // empty list (container initializer), making it indistinguishable from a degenerate
            // zero-result page. Fail-open as a genuine empty — the albums node being present is the
            // recognition signal; drift waves without it are covered by the throwing cases above.
            var parser = CreateParser();
            var response = ResponseWithBody("{\"albums\":{}}");

            var releases = parser.ParseResponse(response);

            releases.Should().BeEmpty();
        }

        [Fact]
        public void ParseResponse_WithValidZeroResultAlbumSearch_ReturnsEmptyWithoutThrow()
        {
            var parser = CreateParser();
            var response = ResponseWithBody(
                "{\"albums\":{\"items\":[],\"total\":0,\"limit\":50,\"offset\":0}}");

            var releases = parser.ParseResponse(response);

            releases.Should().BeEmpty("a well-formed zero-result search is a genuine empty result, not a failure");
        }

        [Fact]
        public void ParseResponse_WithNonOkStatus_ReturnsEmptyWithoutThrow()
        {
            // Scope guard: non-OK statuses keep the historical return-empty behavior — on the
            // live path the HTTP layer throws before the parser ever sees a non-2xx response.
            var parser = CreateParser();
            var response = ResponseWithBody("<html>error page</html>", HttpStatusCode.InternalServerError);

            var releases = parser.ParseResponse(response);

            releases.Should().BeEmpty();
        }
    }
}
