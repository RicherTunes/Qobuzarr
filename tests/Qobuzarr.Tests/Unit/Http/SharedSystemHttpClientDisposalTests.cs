using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Xunit;

namespace Qobuzarr.Tests.Unit.Http
{
    /// <summary>
    /// Verifies the socket-pool leak fix on <see cref="SharedSystemHttpClient"/>:
    /// static Dispose() releases the underlying HttpClient and is idempotent.
    /// </summary>
    [Collection("SharedSystemHttpClient")]
    public class SharedSystemHttpClientDisposalTests : IDisposable
    {
        public SharedSystemHttpClientDisposalTests()
        {
            // Reset disposed flag so each test starts with a clean slate.
            SharedSystemHttpClient.ResetForTesting();
        }

        void IDisposable.Dispose()
        {
            SharedSystemHttpClient.ResetForTesting();
        }

        // -----------------------------------------------------------------------
        // Dispose after use
        // -----------------------------------------------------------------------

        [Fact]
        public void Dispose_AfterUse_DisposesUnderlyingClient()
        {
            // Force creation of the Lazy<HttpClient> value.
            var client = SharedSystemHttpClient.Instance;
            client.Should().NotBeNull("Instance must be accessible before Dispose");

            // Dispose must not throw even though the HttpClient was used.
            var act = () => SharedSystemHttpClient.Dispose();
            act.Should().NotThrow("Dispose must complete cleanly after use");
        }

        // -----------------------------------------------------------------------
        // Dispose without prior use (Lazy not evaluated)
        // -----------------------------------------------------------------------

        [Fact]
        public void Dispose_WithoutUse_NoOp()
        {
            // Do NOT access Instance so the Lazy<HttpClient> is never evaluated.
            // Dispose must still complete without throwing.
            var act = () => SharedSystemHttpClient.Dispose();
            act.Should().NotThrow("Dispose must be safe when the Lazy has not been evaluated");
        }

        // -----------------------------------------------------------------------
        // Idempotency
        // -----------------------------------------------------------------------

        [Fact]
        public void Dispose_IsIdempotent()
        {
            // Access Instance to evaluate the Lazy, then call Dispose multiple times.
            _ = SharedSystemHttpClient.Instance;

            var act = () =>
            {
                SharedSystemHttpClient.Dispose();
                SharedSystemHttpClient.Dispose();
                SharedSystemHttpClient.Dispose();
            };
            act.Should().NotThrow("Dispose must be idempotent");
        }

        [Fact]
        public void ResetForTesting_RecreatesDisposedSingletons()
        {
            var apiClient = SharedSystemHttpClient.Instance;
            var mediaClient = SharedSystemHttpClient.MediaInstance;

            SharedSystemHttpClient.Dispose();
            SharedSystemHttpClient.ResetForTesting();

            SharedSystemHttpClient.Instance.Should().NotBeSameAs(apiClient,
                "tests must not reuse a disposed API singleton after resetting static state");
            SharedSystemHttpClient.MediaInstance.Should().NotBeSameAs(mediaClient,
                "tests must not reuse a disposed media singleton after resetting static state");
        }

        [Fact]
        public async Task CreateMediaClient_DoesNotAutoFollowRedirects()
        {
            using var target = new LoopbackHttpServer(_ =>
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            using var redirect = new LoopbackHttpServer(_ =>
                $"HTTP/1.1 302 Found\r\nLocation: {target.Url}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            using var client = SharedSystemHttpClient.CreateMediaClient();

            using var response = await client.GetAsync(redirect.Url, HttpCompletionOption.ResponseHeadersRead);

            response.StatusCode.Should().Be(HttpStatusCode.Found,
                "Common's media redirect validator must receive the 3xx before a redirected request is sent");
            target.RequestCount.Should().Be(0,
                "auto-following would request the redirected target before Common can apply its SSRF guard");
        }

        private sealed class LoopbackHttpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Func<Uri, string> _responseFactory;
            private readonly Task _acceptTask;
            private int _requestCount;

            public LoopbackHttpServer(Func<Uri, string> responseFactory)
            {
                _responseFactory = responseFactory;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                var endpoint = (IPEndPoint)_listener.LocalEndpoint;
                Url = new Uri($"http://127.0.0.1:{endpoint.Port}/");
                _acceptTask = Task.Run(AcceptOneAsync);
            }

            public Uri Url { get; }

            public int RequestCount => Volatile.Read(ref _requestCount);

            public void Dispose()
            {
                _listener.Stop();
                try { _acceptTask.Wait(TimeSpan.FromSeconds(1)); }
                catch { }
            }

            private async Task AcceptOneAsync()
            {
                try
                {
                    using var tcp = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref _requestCount);
                    await using var stream = tcp.GetStream();
                    var buffer = new byte[512];
                    _ = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                    var bytes = Encoding.ASCII.GetBytes(_responseFactory(Url));
                    await stream.WriteAsync(bytes.AsMemory(0, bytes.Length)).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
            }
        }
    }
}
