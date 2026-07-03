using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Integration.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Qobuzarr.Tests.Unit.Resilience
{
    /// <summary>
    /// Verifies that <see cref="BridgeQobuzApiClient"/> (the live HTTP consumer in the
    /// bridge/indexer path) correctly:
    ///   - Consults AuthFailureGate before issuing a request (EnsureCanProceed)
    ///   - Signals HandleFailureAsync on HTTP 401
    ///   - Does not signal HandleFailureAsync on generic HTTP 403 resource denial
    ///   - Signals HandleSuccessAsync on 2xx
    ///   - Does not signal HandleFailureAsync on non-auth HTTP errors (e.g. 429, 500)
    /// </summary>
    public class QobuzIndexerAuthGateTests
    {
        // ------------------------------------------------------------------ //
        // Helper infrastructure
        // ------------------------------------------------------------------ //

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;

            public int CallCount { get; private set; }

            public FakeHandler(HttpStatusCode statusCode, string body = "{}")
            {
                _statusCode = statusCode;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body)
                });
            }
        }

        private sealed class SpyAuthFailureHandler : IAuthFailureHandler
        {
            public AuthStatus Status { get; private set; } = AuthStatus.Unknown;
            public AuthFailure? LastFailure { get; private set; }
            public int FailureCallCount { get; private set; }
            public int SuccessCallCount { get; private set; }

            public ValueTask HandleFailureAsync(AuthFailure failure, CancellationToken cancellationToken = default)
            {
                FailureCallCount++;
                LastFailure = failure;
                Status = AuthStatus.Failed;
                return ValueTask.CompletedTask;
            }

            public ValueTask HandleSuccessAsync(CancellationToken cancellationToken = default)
            {
                SuccessCallCount++;
                Status = AuthStatus.Authenticated;
                return ValueTask.CompletedTask;
            }

            public ValueTask RequestReauthenticationAsync(string reason, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;
        }

        private static (BridgeQobuzApiClient client, SpyAuthFailureHandler handler, FakeHandler httpHandler)
            CreateSut(HttpStatusCode statusCode, string body = "{\"albums\":{\"items\":[]}}")
        {
            var spy = new SpyAuthFailureHandler();
            var gate = new AuthFailureGate(spy);
            var fakeHttp = new FakeHandler(statusCode, body);
            var httpClient = new HttpClient(fakeHttp);
            var logger = NullLogger<BridgeQobuzApiClient>.Instance;
            var client = new BridgeQobuzApiClient(httpClient, logger, gate);
            return (client, spy, fakeHttp);
        }

        // ------------------------------------------------------------------ //
        // 1. Gate tripped → pre-flight throws, HTTP call never made
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_WhenGateTripped_ThrowsWithoutHttpCall()
        {
            var spy = new SpyAuthFailureHandler();
            var gate = new AuthFailureGate(spy);

            // Trip the gate manually
            await gate.HandleFailureAsync(new AuthFailure
            {
                ErrorCode = "401",
                Message = "Auth bad"
            });

            var fakeHttp = new FakeHandler(HttpStatusCode.OK);
            var client = new BridgeQobuzApiClient(new HttpClient(fakeHttp), NullLogger<BridgeQobuzApiClient>.Instance, gate);

            Func<Task> act = () => client.GetAsync<object>("/album/search");

            await act.Should().ThrowAsync<AuthGatedException>(
                "gate.EnsureCanProceed() must throw when auth is latched bad");

            fakeHttp.CallCount.Should().Be(0,
                "no HTTP call must be issued when the gate is tripped");
        }

        // ------------------------------------------------------------------ //
        // 2. HTTP 401 → HandleFailureAsync called, gate trips
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_On401Response_SignalsHandleFailure()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.Unauthorized);

            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(1,
                "HandleFailureAsync must be called once on a 401 response");
            spy.LastFailure!.ErrorCode.Should().Be("401");
            spy.Status.Should().Be(AuthStatus.Failed);
        }

        // ------------------------------------------------------------------ //
        // 3. HTTP 403 on resource endpoint → no auth latch
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_On403ResourceResponse_DoesNotSignalHandleFailure()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.Forbidden);

            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(0,
                "a generic 403 can be subscription/resource denial and must not trip the auth gate");
            spy.Status.Should().Be(AuthStatus.Unknown);
        }

        // ------------------------------------------------------------------ //
        // 4. HTTP 200 → HandleSuccessAsync called, gate stays open
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_On200Response_SignalsHandleSuccess()
        {
            // Return valid JSON for the generic type; use object which deserialises from {}
            var (client, spy, _) = CreateSut(HttpStatusCode.OK, "{}");

            // GetAsync<object> — Newtonsoft will deserialize {} to a JObject (non-null)
            var result = await client.GetAsync<object>("/album/search");

            spy.SuccessCallCount.Should().Be(1,
                "HandleSuccessAsync must be called on a successful 2xx response");
            spy.FailureCallCount.Should().Be(0,
                "HandleFailureAsync must NOT be called on a 2xx response");
        }

        // ------------------------------------------------------------------ //
        // 5. HTTP 429 (rate limit) does NOT signal HandleFailureAsync
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_On429Response_DoesNotSignalAuthGate()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.TooManyRequests);

            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(0,
                "HTTP 429 is a rate-limit error, not an auth failure — must not trip the auth gate");
        }

        // ------------------------------------------------------------------ //
        // 6. HTTP 500 does NOT signal HandleFailureAsync
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_On500Response_DoesNotSignalAuthGate()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.InternalServerError);

            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(0,
                "HTTP 500 is a server error, not an auth failure — must not trip the auth gate");
        }

        // ------------------------------------------------------------------ //
        // 7. Successive 401s → only first call signals HandleFailureAsync;
        //    subsequent calls are pre-flight blocked (gate already tripped)
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetAsync_SecondCallAfter401_ThrowsFromGateNotFromHttp()
        {
            var (client, spy, fakeHttp) = CreateSut(HttpStatusCode.Unauthorized);

            // First call: gets 401, trips gate
            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(1);
            fakeHttp.CallCount.Should().Be(1, "first call must reach the HTTP layer");

            // Second call: gate is tripped → pre-flight blocks before HTTP
            await client.Invoking(c => c.GetAsync<object>("/album/search"))
                        .Should().ThrowAsync<AuthGatedException>("gate must short-circuit second call");

            fakeHttp.CallCount.Should().Be(1, "second call must not reach the HTTP layer");
        }
    }
}
