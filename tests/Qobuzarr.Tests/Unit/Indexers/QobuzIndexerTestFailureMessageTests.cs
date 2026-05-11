using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Xunit;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Regression guards for QobuzIndexer's Test() failure message.
    ///
    /// Previously Test() emitted "Test failed ({ex.GetType().Name}): {ex.Message}.
    /// Full details in Lidarr logs." which leaked CLR type names
    /// ("SocketException: No such host is known") that aren't actionable for
    /// end users. The new helper QobuzIndexer.BuildTestFailureMessage()
    /// delegates to common's HttpExceptionClassifier so users see categorised
    /// hints like "The server rejected the credentials..." or "Could not reach
    /// the service over the network..." with the CLR type names suppressed.
    /// </summary>
    public sealed class QobuzIndexerTestFailureMessageTests
    {
        [Fact]
        public void BuildTestFailureMessage_401_HintMentionsCredentials()
        {
            var ex = new HttpRequestException("Unauthorized", inner: null, statusCode: HttpStatusCode.Unauthorized);
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            Assert.Contains("credentials", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildTestFailureMessage_429_HintMentionsRateLimit()
        {
            var ex = new HttpRequestException("Too Many Requests", inner: null, statusCode: HttpStatusCode.TooManyRequests);
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            Assert.Contains("rate", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildTestFailureMessage_503_HintMentionsServerError()
        {
            var ex = new HttpRequestException("Service Unavailable", inner: null, statusCode: HttpStatusCode.ServiceUnavailable);
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            // The 5xx hint includes "server error" or "try again".
            Assert.True(
                message.Contains("server", StringComparison.OrdinalIgnoreCase)
                || message.Contains("try again", StringComparison.OrdinalIgnoreCase),
                $"expected server-error hint, got: {message}");
        }

        [Fact]
        public void BuildTestFailureMessage_TaskCanceled_HintMentionsTimeout()
        {
            var ex = new TaskCanceledException("A task was canceled.");
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            Assert.Contains("timed out", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildTestFailureMessage_SocketException_HintMentionsNetwork()
        {
            var ex = new SocketException((int)SocketError.HostNotFound);
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            Assert.Contains("network", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildTestFailureMessage_DoesNotLeakClrTypeOrExceptionWord()
        {
            // The whole point of the swap — the old format put "SocketException"
            // and "HttpRequestException" in user-visible text. Lock this contract
            // down so a future refactor doesn't accidentally re-introduce
            // ex.GetType().Name in the message.
            var inputs = new Exception[]
            {
                new HttpRequestException("x", null, HttpStatusCode.Unauthorized),
                new HttpRequestException("x", null, HttpStatusCode.TooManyRequests),
                new HttpRequestException("x", null, HttpStatusCode.InternalServerError),
                new TaskCanceledException("timeout"),
                new SocketException(11001),
                new InvalidOperationException("opaque"),
                new IOException("boom")
            };
            foreach (var ex in inputs)
            {
                var message = QobuzIndexer.BuildTestFailureMessage(ex);
                Assert.False(message.Contains("System.", StringComparison.Ordinal),
                    $"leaked CLR namespace: '{message}'");
                Assert.False(message.Contains("Exception", StringComparison.Ordinal),
                    $"leaked 'Exception': '{message}'");
            }
        }

        [Fact]
        public void BuildTestFailureMessage_MentionsLogsForOperatorDeepDive()
        {
            // Hints are short on purpose; for operators, point them at the log
            // for the full exception. Preserves the existing "Full details in
            // Lidarr logs." UX hint.
            var ex = new InvalidOperationException("opaque");
            var message = QobuzIndexer.BuildTestFailureMessage(ex);
            Assert.Contains("log", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
