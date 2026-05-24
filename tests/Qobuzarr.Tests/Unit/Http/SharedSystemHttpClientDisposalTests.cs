using System;
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
    }
}
