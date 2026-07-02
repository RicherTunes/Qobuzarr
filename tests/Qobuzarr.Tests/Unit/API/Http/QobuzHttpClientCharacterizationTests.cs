using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.Http;
using Qobuzarr.Tests.Fixtures;
using Xunit;

namespace Qobuzarr.Tests.Unit.API.Http
{
    public class QobuzHttpClientCharacterizationTests : TestFixtureBase
    {
        [Fact]
        public async Task ExecuteAsync_With429AndRetryAfterZero_RetriesAndSucceeds()
        {
            var request = new HttpRequest("https://test.qobuz.com/api?user_auth_token=sample_auth_token_123456");

            var rateLimited = CreateResponse(request, HttpStatusCode.TooManyRequests, retryAfterSeconds: 0);
            var success = CreateResponse(request, HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(rateLimited)
                          .ReturnsAsync(success);

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteAsync_WarnLogs_ShouldNotLeakUserAuthToken()
        {
            var token = "sample_auth_token_123456";
            var request = new HttpRequest($"https://test.qobuz.com/api?user_auth_token={token}&app_id=app_123");

            var rateLimited = CreateResponse(request, HttpStatusCode.TooManyRequests, retryAfterSeconds: 0);
            var success = CreateResponse(request, HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(rateLimited)
                          .ReturnsAsync(success);

            var (logger, memory) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            await sut.ExecuteAsync(request);

            memory.Logs.Should().NotBeEmpty();
            string.Join("\n", memory.Logs).Should().NotContain(token);
            // Wave 17F: unified on Common.Scrub.Url which uses *** sentinel (was [redacted]).
            string.Join("\n", memory.Logs).Should().Contain("user_auth_token=***");
        }

        // #26 (LOOP-014): characterization safety net pinning the CURRENT QobuzHttpClient.ExecuteAsync retry
        // semantics BEFORE the GenericResilienceExecutor consolidation. Every behavior below must be preserved.

        [Fact]
        public async Task ExecuteAsync_SuccessFirstTry_CallsOnce_ReturnsOk()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.OK));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(1));
        }

        [Fact]
        public async Task ExecuteAsync_With500ThenOk_RetriesAndSucceeds()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.InternalServerError, retryAfterSeconds: 0))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.OK));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteAsync_NonRetryableErrorReturned_CallsOnce_ReturnsResponse_NoThrow()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.BadRequest));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "400 is not retryable; the response is returned, not thrown");
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(1));
        }

        [Fact]
        public async Task ExecuteAsync_Persistent429Returned_ExhaustsMaxRetries_ReturnsLastResponse_NoThrow()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.TooManyRequests, retryAfterSeconds: 0));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(3), "MaxRetries=3");
        }

        [Fact]
        public async Task ExecuteAsync_InnerThrowsHttpException429ThenOk_RetriesAndSucceeds()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ThrowsAsync(new HttpException(CreateResponse(request, HttpStatusCode.TooManyRequests, retryAfterSeconds: 0)))
                          .ReturnsAsync(CreateResponse(request, HttpStatusCode.OK));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var response = await sut.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK, "a thrown HttpException with a retryable status is retried");
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteAsync_InnerPersistentlyThrowsHttpException500_ExhaustsThenRethrowsHttpException()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ThrowsAsync(new HttpException(CreateResponse(request, HttpStatusCode.InternalServerError, retryAfterSeconds: 0)));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var act = async () => await sut.ExecuteAsync(request);

            await act.Should().ThrowAsync<HttpException>("on terminal exhaustion the original HttpException is rethrown");
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ExecuteAsync_InnerThrowsHttpException400_NonRetryable_RethrowsImmediately()
        {
            var request = new HttpRequest("https://test.qobuz.com/api");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                          .ThrowsAsync(new HttpException(CreateResponse(request, HttpStatusCode.BadRequest)));

            var (logger, _) = CreateIsolatedLogger();
            var sut = new QobuzHttpClient(MockHttpClient.Object, logger);

            var act = async () => await sut.ExecuteAsync(request);

            await act.Should().ThrowAsync<HttpException>("a non-retryable thrown HttpException is rethrown without retry");
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(1));
        }

        private static HttpResponse CreateResponse(HttpRequest request, HttpStatusCode statusCode, int? retryAfterSeconds = null)
        {
            var headers = new HttpHeader
            {
                ContentType = "application/json"
            };

            if (retryAfterSeconds.HasValue)
            {
                headers.Add("Retry-After", retryAfterSeconds.Value.ToString());
            }

            return new HttpResponse(request, headers, "{}", statusCode);
        }

        private static (Logger Logger, MemoryTarget Memory) CreateIsolatedLogger()
        {
            var memory = new MemoryTarget("memory")
            {
                Layout = "${level}|${message}"
            };

            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(memory, "*");

            var factory = new LogFactory(config);
            return (factory.GetLogger($"QobuzHttpClientCharacterizationTests-{System.Guid.NewGuid()}"), memory);
        }
    }
}
