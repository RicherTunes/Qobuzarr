using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Xunit;

namespace Qobuzarr.Tests.Download.Clients
{
    /// <summary>
    /// The qobuz download paths (TrackDownloadService — the live path — and QobuzDownloadClient) resume an
    /// interrupted download by sending
    /// <c>Range: bytes={existing}-</c> when a <c>.partial</c> file is present. If a COMPLETE <c>.partial</c>
    /// persists (the process was killed in the stream→atomic-move window), <c>existing == total</c> and the
    /// server answers <c>416 Range Not Satisfiable</c> — which <c>EnsureSuccessStatusCode()</c> turned into a
    /// throw, leaving the track stuck failing on every retry until the <c>.partial</c> was deleted by hand.
    /// SendDownloadRequestAsync must recover: on 416 delete the stale partial and retry once without the range.
    /// </summary>
    public class QobuzDownloadClientResume416Tests
    {
        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;
            public int CallCount { get; private set; }

            public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
            {
                _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                var responder = _responders.Dequeue();
                return Task.FromResult(responder(request));
            }
        }

        [Fact]
        public async Task SendDownloadRequestAsync_CompletePartial_Server416_DeletesPartialAndRetriesWithoutRange()
        {
            var partialPath = Path.GetTempFileName();
            await File.WriteAllBytesAsync(partialPath, new byte[1024]);
            try
            {
                var handler = new SequenceHandler(
                    // 1st attempt carries the resume Range header → server says 416 (partial already complete).
                    req =>
                    {
                        req.Headers.Range.Should().NotBeNull("the first attempt resumes from the existing bytes");
                        return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                    },
                    // 2nd attempt must drop the Range header and succeed.
                    req =>
                    {
                        req.Headers.Range.Should().BeNull("after a 416 the download restarts fresh, no Range header");
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) };
                    });
                using var client = new HttpClient(handler);

                var resetNotified = false;
                var (response, existing) = await ResumeHttpDownloader.SendDownloadRequestAsync(
                    client, "https://example/track", partialPath, existing: 1024,
                    onRangeReset: _ => resetNotified = true, cancellationToken: CancellationToken.None);

                using var _ = response;
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                existing.Should().Be(0, "the resume offset is reset after a 416");
                resetNotified.Should().BeTrue();
                File.Exists(partialPath).Should().BeFalse("the stale/complete partial must be deleted");
                handler.CallCount.Should().Be(2);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
        }

        [Fact]
        public async Task SendDownloadRequestAsync_NoPartial_SendsWithoutRange_Succeeds()
        {
            var handler = new SequenceHandler(
                req =>
                {
                    req.Headers.Range.Should().BeNull("no partial → no resume range");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) };
                });
            using var client = new HttpClient(handler);

            var (response, existing) = await ResumeHttpDownloader.SendDownloadRequestAsync(
                client, "https://example/track", "does-not-exist.partial", existing: 0,
                onRangeReset: null, cancellationToken: CancellationToken.None);

            using var _ = response;
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            existing.Should().Be(0);
            handler.CallCount.Should().Be(1);
        }
    }
}
