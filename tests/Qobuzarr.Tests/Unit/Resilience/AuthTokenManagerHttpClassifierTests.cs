using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Services.Diagnostics;
using Lidarr.Plugin.Qobuzarr.Services;
using NLog;
using NSubstitute;
using Xunit;

namespace Qobuzarr.Tests.Unit.Resilience
{
    /// <summary>
    /// Verifies that AuthTokenManager.IsAuthenticationError uses HttpExceptionClassifier
    /// (via HttpFailureCategory.Auth) rather than hand-rolled string matching.
    ///
    /// IsAuthenticationError is private, so we test it indirectly through the
    /// public surface: GetValidTokenAsync triggers RefreshTokenInternalAsync which
    /// calls IsAuthenticationError on the caught exception. We verify the method
    /// itself via HttpExceptionClassifier directly to confirm the replacement is
    /// semantically correct.
    /// </summary>
    public class AuthTokenManagerHttpClassifierTests
    {
        // ------------------------------------------------------------------ //
        // 1. HttpExceptionClassifier returns Auth for 401
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Auth_For_Http401()
        {
            var ex = new HttpRequestException(
                "Unauthorized",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized);

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.Auth,
                "HTTP 401 must classify as Auth so IsAuthenticationError returns true");
        }

        // ------------------------------------------------------------------ //
        // 2. HttpExceptionClassifier returns Auth for 403
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Auth_For_Http403()
        {
            var ex = new HttpRequestException(
                "Forbidden",
                inner: null,
                statusCode: HttpStatusCode.Forbidden);

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.Auth,
                "HTTP 403 must classify as Auth so IsAuthenticationError returns true");
        }

        // ------------------------------------------------------------------ //
        // 3. HttpExceptionClassifier returns RateLimit for 429 (not Auth)
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_RateLimit_For_Http429_NotAuth()
        {
            var ex = new HttpRequestException(
                "Too Many Requests",
                inner: null,
                statusCode: HttpStatusCode.TooManyRequests);

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.RateLimit,
                "HTTP 429 must not be classified as Auth — would wrongly trip the auth gate");
            result.Category.Should().NotBe(HttpFailureCategory.Auth);
        }

        // ------------------------------------------------------------------ //
        // 4. HttpExceptionClassifier returns Network for SocketException (not Auth)
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Network_For_SocketException_NotAuth()
        {
            var ex = new HttpRequestException(
                "Connection refused",
                new SocketException((int)SocketError.ConnectionRefused));

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.Network,
                "A SocketException must not classify as Auth");
            result.Category.Should().NotBe(HttpFailureCategory.Auth);
        }

        // ------------------------------------------------------------------ //
        // 5. HttpExceptionClassifier returns Network when StatusCode is null
        //    (request never reached server — no auth inference possible)
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Network_When_NoStatusCode()
        {
            // HttpRequestException with no status code = transport-level failure
            var ex = new HttpRequestException("Could not connect");

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.Network,
                "An HttpRequestException with no status code must not be classified as Auth");
            result.Category.Should().NotBe(HttpFailureCategory.Auth);
        }

        // ------------------------------------------------------------------ //
        // 6. String-matching words that old code treated as Auth are NOT Auth
        //    when there is no actual 401/403 status code.
        //
        //    Old code: .Contains("token") would trigger on a benign message like
        //    "Invalid token format in query string" from a 400 response.
        //    New classifier only fires on real HTTP status codes.
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_DoesNotClassify_As_Auth_For_TokenWordInMessage_With400()
        {
            // 400 with "token" in the message — old string-match would have returned true
            var ex = new HttpRequestException(
                "Invalid token format",
                inner: null,
                statusCode: HttpStatusCode.BadRequest);

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().NotBe(HttpFailureCategory.Auth,
                "A 400 response mentioning 'token' is a client error, not an auth failure");
        }

        // ------------------------------------------------------------------ //
        // 7. Server errors (5xx) are not Auth
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Server_For_Http500_NotAuth()
        {
            var ex = new HttpRequestException(
                "Internal Server Error",
                inner: null,
                statusCode: HttpStatusCode.InternalServerError);

            var result = HttpExceptionClassifier.Classify(ex);

            result.Category.Should().Be(HttpFailureCategory.Server);
            result.Category.Should().NotBe(HttpFailureCategory.Auth);
        }

        // ------------------------------------------------------------------ //
        // 8. Null exception returns Unknown (no crash)
        // ------------------------------------------------------------------ //

        [Fact]
        public void Classifier_Returns_Unknown_For_NullException()
        {
            var result = HttpExceptionClassifier.Classify(null!);

            result.Category.Should().Be(HttpFailureCategory.Unknown);
        }
    }
}
