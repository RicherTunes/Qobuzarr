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
    /// Verifies AuthFailureGate wiring for the download-client path.
    ///
    /// The download-client path in the bridge context also uses <see cref="BridgeQobuzApiClient"/>
    /// (via IQobuzApiClient injection into the download orchestration layer). These tests verify
    /// that POST requests (the main path for download-related API calls) respect the gate in the
    /// same way GET requests do — same gate, same rules.
    ///
    /// NOTE: The full <see cref="QobuzDownloadClient"/> is a Lidarr-native class (DryIoC
    /// auto-discovered). It does not directly call BridgeQobuzApiClient. The gate is wired at the
    /// bridge layer. These tests confirm the gate contract holds for POST operations.
    /// </summary>
    public class QobuzDownloadClientAuthGateTests
    {
        // ------------------------------------------------------------------ //
        // Helpers (same as QobuzIndexerAuthGateTests — shared pattern)
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
            CreateSut(HttpStatusCode statusCode, string body = "{}")
        {
            var spy = new SpyAuthFailureHandler();
            var gate = new AuthFailureGate(spy);
            var fakeHttp = new FakeHandler(statusCode, body);
            var httpClient = new HttpClient(fakeHttp);
            var client = new BridgeQobuzApiClient(httpClient, NullLogger<BridgeQobuzApiClient>.Instance, gate);
            return (client, spy, fakeHttp);
        }

        // ------------------------------------------------------------------ //
        // 1. Gate tripped → PostAsync pre-flight throws without HTTP call
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_WhenGateTripped_ThrowsWithoutHttpCall()
        {
            var spy = new SpyAuthFailureHandler();
            var gate = new AuthFailureGate(spy);

            await gate.HandleFailureAsync(new AuthFailure
            {
                ErrorCode = "401",
                Message = "Auth bad"
            });

            var fakeHttp = new FakeHandler(HttpStatusCode.OK);
            var client = new BridgeQobuzApiClient(new HttpClient(fakeHttp), NullLogger<BridgeQobuzApiClient>.Instance, gate);

            Func<Task> act = () => client.PostAsync<object>("/user/login", new { email = "test@example.com" });

            await act.Should().ThrowAsync<AuthGatedException>(
                "gate.EnsureCanProceed() must throw before any HTTP call is issued");

            fakeHttp.CallCount.Should().Be(0,
                "no HTTP call must be issued when the gate is tripped");
        }

        // ------------------------------------------------------------------ //
        // 2. HTTP 401 on POST → HandleFailureAsync called
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_On401Response_SignalsHandleFailure()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.Unauthorized);

            await client.Invoking(c => c.PostAsync<object>("/user/login"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(1,
                "HandleFailureAsync must be called once on a 401 response from a POST");
            spy.LastFailure!.ErrorCode.Should().Be("401");
        }

        // ------------------------------------------------------------------ //
        // 3. HTTP 403 on POST → HandleFailureAsync called
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_On403Response_SignalsHandleFailure()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.Forbidden);

            await client.Invoking(c => c.PostAsync<object>("/user/login"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(1,
                "HandleFailureAsync must be called once on a 403 response from a POST");
            spy.LastFailure!.ErrorCode.Should().Be("403");
        }

        // ------------------------------------------------------------------ //
        // 4. HTTP 200 on POST → HandleSuccessAsync called
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_On200Response_SignalsHandleSuccess()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.OK, "{}");

            var result = await client.PostAsync<object>("/user/login", null);

            spy.SuccessCallCount.Should().Be(1,
                "HandleSuccessAsync must be called on a successful 2xx POST response");
            spy.FailureCallCount.Should().Be(0);
        }

        // ------------------------------------------------------------------ //
        // 5. HTTP 429 on POST does NOT signal HandleFailureAsync
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_On429Response_DoesNotSignalAuthGate()
        {
            var (client, spy, _) = CreateSut(HttpStatusCode.TooManyRequests);

            await client.Invoking(c => c.PostAsync<object>("/user/login"))
                        .Should().ThrowAsync<QobuzApiException>();

            spy.FailureCallCount.Should().Be(0,
                "HTTP 429 on a POST must not signal the auth failure gate");
        }

        // ------------------------------------------------------------------ //
        // 6. Gate recovers: after HandleSuccessAsync the gate opens again
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task PostAsync_AfterSuccessFollowing401_GateOpensAgain()
        {
            var spy = new SpyAuthFailureHandler();
            var gate = new AuthFailureGate(spy);

            // First: trip the gate with a 401
            var failHttp = new FakeHandler(HttpStatusCode.Unauthorized);
            var failClient = new BridgeQobuzApiClient(new HttpClient(failHttp), NullLogger<BridgeQobuzApiClient>.Instance, gate);

            await failClient.Invoking(c => c.PostAsync<object>("/user/login"))
                            .Should().ThrowAsync<QobuzApiException>();
            spy.FailureCallCount.Should().Be(1);

            // Manually recover (simulates user re-credentialing out-of-band)
            await gate.HandleSuccessAsync();

            // Now the gate is open — a new request should reach HTTP
            var successHttp = new FakeHandler(HttpStatusCode.OK, "{}");
            var successClient = new BridgeQobuzApiClient(new HttpClient(successHttp), NullLogger<BridgeQobuzApiClient>.Instance, gate);

            var result = await successClient.PostAsync<object>("/user/login", null);
            successHttp.CallCount.Should().Be(1, "gate is open; HTTP call must reach the handler");
        }
    }
}
