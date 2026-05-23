using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Qobuzarr.Tests.Integration;

/// <summary>
/// Wave 3: proves the AuthFailureDelegatingHandler is wired into the Qobuz
/// bridge HTTP client. Before this wave, qobuzarr only short-circuited at
/// the indexer adapter — downstream call sites (or future bridge slices)
/// still hammered the upstream on 401. With the handler in the HttpClient
/// pipeline, every outbound request through the bridge client is gated.
/// </summary>
public sealed class QobuzAuthFailureDelegatingHandlerWireUpTests
{
    private sealed class StubPrimaryHandler : HttpMessageHandler
    {
        public HttpStatusCode NextStatus { get; set; } = HttpStatusCode.OK;
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(NextStatus));
        }
    }

    private static (HttpClient Client, DefaultAuthFailureHandler Handler, AuthFailureGate Gate, StubPrimaryHandler Stub)
        BuildPipelineFromPluginDi()
    {
        // Replay the slice of QobuzarrStreamingPlugin.ConfigureServices that
        // sets up the auth gate + delegating handler, without dragging in the
        // full Qobuz API client (which needs a real Lidarr host context).
        var services = new ServiceCollection();
        services.AddSingleton<IAuthFailureHandler>(new DefaultAuthFailureHandler(NullLogger<DefaultAuthFailureHandler>.Instance));
        services.AddSingleton(sp => new AuthFailureGate(
            sp.GetRequiredService<IAuthFailureHandler>(),
            TimeProvider.System,
            TimeSpan.FromSeconds(60),
            NullLogger<AuthFailureGate>.Instance));
        services.AddTransient<AuthFailureDelegatingHandler>();

        var stub = new StubPrimaryHandler();
        services.AddHttpClient("Qobuz", c => c.BaseAddress = new Uri("https://test.invalid/"))
            .AddHttpMessageHandler<AuthFailureDelegatingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => stub);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("Qobuz");
        var handler = (DefaultAuthFailureHandler)provider.GetRequiredService<IAuthFailureHandler>();
        var gate = provider.GetRequiredService<AuthFailureGate>();
        return (client, handler, gate, stub);
    }

    [Fact]
    public async Task BridgePipeline_On401_LatchesAuthHandler()
    {
        var (client, handler, _, stub) = BuildPipelineFromPluginDi();
        stub.NextStatus = HttpStatusCode.Unauthorized;

        using var resp = await client.GetAsync("/album/search?query=x");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(AuthStatus.Failed, handler.Status);
    }

    [Fact]
    public async Task BridgePipeline_AfterLatch_ShortCircuitsSubsequentRequests()
    {
        // The wave-3 fix: with the handler in the pipeline, 20 sequential
        // requests after a 401 hit the upstream EXACTLY ONCE — the probe slot
        // consumed by the first 401-returning request. This is the per-request
        // amplification stop for any caller using the bridge HttpClient.
        var (client, handler, _, stub) = BuildPipelineFromPluginDi();
        await handler.HandleFailureAsync(new AuthFailure { Message = "session expired" });
        stub.NextStatus = HttpStatusCode.Unauthorized;

        // First call uses the probe slot.
        using (var probe = await client.GetAsync("/album/search?query=x"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, probe.StatusCode);
        }
        Assert.Equal(1, stub.CallCount);

        // Subsequent calls must short-circuit with AuthGatedException.
        for (var i = 0; i < 20; i++)
        {
            await Assert.ThrowsAsync<AuthGatedException>(() => client.GetAsync("/album/search?query=x"));
        }
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task BridgePipeline_OnRecovery_ResumesNormalTraffic()
    {
        var (client, handler, _, stub) = BuildPipelineFromPluginDi();
        await handler.HandleFailureAsync(new AuthFailure { Message = "bad" });

        // Probe returns 200 (user re-credentialed). Handler clears the latch.
        stub.NextStatus = HttpStatusCode.OK;
        using (var probe = await client.GetAsync("/album/search?query=x"))
        {
            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        }
        Assert.Equal(AuthStatus.Authenticated, handler.Status);

        // Next 5 calls go through without short-circuit.
        for (var i = 0; i < 5; i++)
        {
            using var r = await client.GetAsync("/album/search?query=x");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        Assert.Equal(6, stub.CallCount); // probe + 5 recovered
    }
}
