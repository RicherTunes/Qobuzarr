using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using QobuzCLI.Services.Adapters;
using Xunit;
using FakeTimeProvider = Lidarr.Plugin.Common.TestKit.Testing.FakeTimeProvider;

namespace QobuzCLI.Tests.Services;

public sealed class CliHttpClientAdapterTimeoutTests
{
    private const string Endpoint = "https://adapter-timeout.test/catalog";
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("json")]
    [InlineData("string")]
    [InlineData("bytes")]
    public async Task UsedClient_WhenJsonHasCustomTimeout_RemainsReusable(string warmup)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var adapter = new CliHttpClientAdapter(client);
        if (warmup == "json") await adapter.GetJsonAsync<Payload>(Endpoint);
        else if (warmup == "string") await adapter.GetStringAsync(Endpoint);
        else await adapter.GetBytesAsync(Endpoint);

        var result = await adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromSeconds(2));

        Assert.Equal("retained", result.Name);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        Assert.Equal(2, handler.Sends);
        Assert.False(handler.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomTimeout_WhenFirstRequestRuns_DoesNotChangeClientPolicy(bool infinite)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var adapter = new CliHttpClientAdapter(client);
        var timeout = infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(2);

        var result = await adapter.GetJsonAsync<Payload>(Endpoint, timeout);

        Assert.Equal("retained", result.Name);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint)).Name);
        Assert.Equal(2, handler.Sends);
    }

    [Fact]
    public async Task SequentialCustomTimeouts_DoNotFreezeOrCarryOverPerCallState()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new CliHttpClientAdapter(client);

        foreach (var timeout in new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan })
        {
            Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint, timeout)).Name);
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        }
        Assert.Equal(3, handler.Sends);
    }

    [Fact]
    public async Task OverlappingCustomTimeouts_BothReachTheSharedTransport()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return Json();
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new CliHttpClientAdapter(client);
        var first = adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromHours(1));
        var second = adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromHours(2));
        try
        {
            Assert.Equal(2, handler.Sends);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
            release.TrySetResult(true);
            Assert.Equal("retained", (await first.WaitAsync(Watchdog)).Name);
            Assert.Equal("retained", (await second.WaitAsync(Watchdog)).Name);
        }
        finally
        {
            release.TrySetResult(true);
            client.CancelPendingRequests();
            await DrainAsync(first, second);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestDeadline_CancelsOnlyThatCall_AndTheClientStaysUsable(bool otherHasTimeout)
    {
        var clock = new FakeTimeProvider();
        var otherRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/slow")
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            else if (request.RequestUri.AbsolutePath == "/other")
            {
                otherEntered.TrySetResult(true);
                await otherRelease.Task.WaitAsync(token);
            }
            return Json();
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new CliHttpClientAdapter(client, clock);
        var other = adapter.GetJsonAsync<Payload>("https://adapter-timeout.test/other",
            otherHasTimeout ? TimeSpan.FromHours(1) : null);
        Task<Payload>? expired = null;
        try
        {
            await otherEntered.Task.WaitAsync(Watchdog);
            expired = adapter.GetJsonAsync<Payload>("https://adapter-timeout.test/slow", TimeSpan.FromSeconds(2));
            await entered.Task.WaitAsync(Watchdog);
            Assert.False(other.IsCompleted);
            Assert.False(expired.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expired.WaitAsync(Watchdog));
            Assert.False(other.IsCompleted);
            otherRelease.TrySetResult(true);
            Assert.Equal("retained", (await other.WaitAsync(Watchdog)).Name);
            Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromSeconds(2))).Name);
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
            Assert.Equal(3, handler.Sends);
        }
        finally
        {
            otherRelease.TrySetResult(true);
            client.CancelPendingRequests();
            await DrainAsync(expired, other);
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-9999L)]
    [InlineData(-10001L)]
    [InlineData(long.MinValue)]
    [InlineData(21474836470001L)]
    [InlineData(long.MaxValue)]
    public async Task InvalidTimeout_IsRejectedBeforeSending_WithoutChangingTheClient(long ticks)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var adapter = new CliHttpClientAdapter(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromTicks(ticks)));

        Assert.Equal(0, handler.Sends);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint)).Name);
    }

    [Fact]
    public async Task MaximumSupportedTimeout_IsAcceptedWithoutExtendingTheClientPolicy()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var adapter = new CliHttpClientAdapter(client);
        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromMilliseconds(int.MaxValue))).Name);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongerOrInfiniteRequestTimeout_DoesNotDisableTheOwningClientDeadline(bool infinite)
    {
        using var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json();
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var adapter = new CliHttpClientAdapter(client);
        var pending = adapter.GetJsonAsync<Payload>(Endpoint, infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromHours(1));
        try
        {
            Assert.Equal(TimeSpan.FromMilliseconds(100), client.Timeout);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Watchdog));
        }
        finally
        {
            client.CancelPendingRequests();
            await DrainAsync(pending);
        }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task HttpFailure_IsNotRetried_AndTheNextCallCanSetItsOwnTimeout(int status)
    {
        var responseNumber = 0;
        using var handler = new Handler((_, _) => Task.FromResult(
            Interlocked.Increment(ref responseNumber) == 1 ? new HttpResponseMessage((HttpStatusCode)status) : Json()));
        using var client = new HttpClient(handler);
        var originalTimeout = client.Timeout;
        var adapter = new CliHttpClientAdapter(client);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromSeconds(2)));

        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal(1, handler.Sends);
        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromSeconds(3))).Name);
        Assert.Equal(originalTimeout, client.Timeout);
    }

    [Fact]
    public async Task JsonContract_PreservesNewtonsoftPropertyNamesAndNull_AndInvalidJsonStillFails()
    {
        var call = 0;
        using var handler = new Handler((_, _) => Task.FromResult(Json(
            Interlocked.Increment(ref call) switch { 1 => "{\"catalog_name\":\"retained\"}", 2 => "null", _ => "{" })));
        using var client = new HttpClient(handler);
        var adapter = new CliHttpClientAdapter(client);

        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint)).Name);
        Assert.Null(await adapter.GetJsonAsync<Payload?>(Endpoint));
        await Assert.ThrowsAsync<JsonSerializationException>(() => adapter.GetJsonAsync<Payload>(Endpoint));
        Assert.Equal(3, handler.Sends);
        Assert.False(handler.Disposed);
    }

    [Fact]
    public async Task Deadline_RemainsActiveDuringResponseBodyReading()
    {
        var clock = new FakeTimeProvider();
        using var content = new BlockedStream();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(content) }));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new CliHttpClientAdapter(client, clock);
        var pending = adapter.GetJsonAsync<Payload>(Endpoint, TimeSpan.FromSeconds(2));
        try
        {
            await content.Entered.Task.WaitAsync(Watchdog);
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Watchdog));
            Assert.Equal(1, content.Reads);
            Assert.True(content.Disposed);
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        }
        finally
        {
            content.Release();
            client.CancelPendingRequests();
            await DrainAsync(pending);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrInfiniteTimeout_DoesNotScheduleAnAdditionalDeadline(bool infinite)
    {
        var clock = new FakeTimeProvider();
        using var handler = new Handler((_, _) => Task.FromResult(Json()));
        using var client = new HttpClient(handler);
        var adapter = new CliHttpClientAdapter(client, clock);
        Assert.Equal("retained", (await adapter.GetJsonAsync<Payload>(Endpoint, infinite ? Timeout.InfiniteTimeSpan : null)).Name);
        Assert.Equal(0, clock.PendingDelayCount);
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        Assert.Equal("httpClient", Assert.Throws<ArgumentNullException>(() => new CliHttpClientAdapter(null!)).ParamName);
        using var client = new HttpClient();
        Assert.Equal("timeProvider", Assert.Throws<ArgumentNullException>(() => new CliHttpClientAdapter(client, null!)).ParamName);
    }

    private static HttpResponseMessage Json(string body = "{\"catalog_name\":\"retained\"}")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task DrainAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null) continue;
            try { await task.WaitAsync(Watchdog); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { } // Original implementation faults when reconfiguring a used client.
        }
    }

    private sealed class Payload
    {
        [JsonProperty("catalog_name")]
        public string? Name { get; set; }
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private int _sends;
        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        public int Sends => Volatile.Read(ref _sends);
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sends);
            return _send(request, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockedStream : MemoryStream
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public void Release() => _release.TrySetResult(true);
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            Reads++;
            Entered.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                Release();
            }
            base.Dispose(disposing);
        }
    }
}
