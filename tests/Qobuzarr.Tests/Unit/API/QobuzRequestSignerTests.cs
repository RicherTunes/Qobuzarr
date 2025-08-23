using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API.Signing;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.API
{
    /// <summary>
    /// Comprehensive tests for QobuzRequestSigner covering signature generation,
    /// endpoint validation, and edge cases for API request signing.
    /// </summary>
    public class QobuzRequestSignerTests : TestFixtureBase
    {
        private readonly QobuzRequestSigner _signer;
        private const string TEST_APP_ID = "test_app_id_123";
        private const string TEST_APP_SECRET = "test_app_secret_456";

        public QobuzRequestSignerTests()
        {
            _signer = new QobuzRequestSigner(MockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzRequestSigner(null);
            act.Should().Throw<ArgumentNullException>().WithMessage("*logger*");
        }

        #endregion

        #region RequiresSigning Tests

        [Theory]
        [InlineData("track/getFileUrl", true)]
        [InlineData("/track/getFileUrl", true)]
        [InlineData("album/get", false)]
        [InlineData("artist/search", false)]
        [InlineData("user/login", false)]
        [InlineData("", false)]
        public void RequiresSigning_WithVariousEndpoints_ShouldReturnCorrectResults(string endpoint, bool expected)
        {
            // Act
            var result = _signer.RequiresSigning(endpoint);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region Track URL Signature Tests

        [Fact]
        public void GenerateTrackUrlSignature_WithValidParameters_ShouldReturnConsistentHash()
        {
            // Arrange
            var trackId = "123456789";
            var formatId = "27";
            var timestamp = "1234567890";
            var appSecret = "test_secret";

            // Act
            var signature1 = _signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, appSecret);
            var signature2 = _signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, appSecret);

            // Assert
            signature1.Should().NotBeNullOrWhiteSpace();
            signature1.Should().Be(signature2); // Should be deterministic
            signature1.Should().HaveLength(32); // MD5 hash length
            signature1.Should().MatchRegex("^[a-f0-9]{32}$"); // Valid MD5 hex format
        }

        [Fact]
        public void GenerateTrackUrlSignature_WithDifferentInputs_ShouldReturnDifferentHashes()
        {
            // Arrange
            var timestamp = "1234567890";
            var appSecret = "test_secret";

            // Act
            var sig1 = _signer.GenerateTrackUrlSignature("123", "27", timestamp, appSecret);
            var sig2 = _signer.GenerateTrackUrlSignature("456", "27", timestamp, appSecret);
            var sig3 = _signer.GenerateTrackUrlSignature("123", "6", timestamp, appSecret);

            // Assert
            sig1.Should().NotBe(sig2);
            sig1.Should().NotBe(sig3);
            sig2.Should().NotBe(sig3);
        }

        [Theory]
        [InlineData("", "27", "1234567890")]
        [InlineData("123", "", "1234567890")]
        [InlineData("123", "27", "")]
        public void GenerateTrackUrlSignature_WithEmptyParameters_ShouldStillGenerateHash(string trackId, string formatId, string timestamp)
        {
            // Act
            var signature = _signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, "secret");

            // Assert
            signature.Should().NotBeNullOrWhiteSpace();
            signature.Should().HaveLength(32);
        }

        [Fact]
        public void GenerateTrackUrlSignature_WithKnownValues_ShouldMatchExpectedPattern()
        {
            // Arrange - Use TrevTV's exact pattern for verification
            var trackId = "12345678";
            var formatId = "27";
            var timestamp = "1609459200"; // Fixed timestamp for consistency
            var appSecret = "test_secret";

            // Expected signature string: "trackgetFileUrlformat_id27intentstreamtrack_id123456781609459200test_secret"

            // Act
            var signature = _signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, appSecret);

            // Assert
            signature.Should().NotBeNullOrWhiteSpace();
            signature.Should().HaveLength(32);
            
            // Test with same inputs again to verify consistency
            var signature2 = _signer.GenerateTrackUrlSignature(trackId, formatId, timestamp, appSecret);
            signature2.Should().Be(signature);
        }

        #endregion

        #region Generic Signature Tests

        [Fact]
        public void GenerateGenericSignature_WithValidParameters_ShouldReturnConsistentHash()
        {
            // Arrange
            var endpoint = "album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" },
                { "extra", "tracks" }
            };

            // Act
            var signature1 = _signer.GenerateGenericSignature(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);
            var signature2 = _signer.GenerateGenericSignature(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            signature1.Should().NotBeNullOrWhiteSpace();
            signature1.Should().Be(signature2);
            signature1.Should().HaveLength(32);
            signature1.Should().MatchRegex("^[a-f0-9]{32}$");
        }

        [Fact]
        public void GenerateGenericSignature_ShouldExcludeSystemParameters()
        {
            // Arrange
            var endpoint = "album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" },
                { "app_id", "should_be_excluded" },
                { "user_auth_token", "should_be_excluded" },
                { "request_ts", "should_be_excluded" },
                { "request_sig", "should_be_excluded" }
            };

            var cleanParameters = new Dictionary<string, string>
            {
                { "album_id", "123456" }
            };

            // Act
            var signatureWithSystemParams = _signer.GenerateGenericSignature(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);
            var signatureClean = _signer.GenerateGenericSignature(endpoint, cleanParameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            signatureWithSystemParams.Should().Be(signatureClean);
        }

        [Fact]
        public void GenerateGenericSignature_ShouldSortParametersAlphabetically()
        {
            // Arrange
            var endpoint = "album/search";
            var parameters1 = new Dictionary<string, string>
            {
                { "query", "test" },
                { "limit", "50" },
                { "offset", "0" }
            };

            var parameters2 = new Dictionary<string, string>
            {
                { "offset", "0" },
                { "query", "test" },
                { "limit", "50" }
            };

            // Act
            var signature1 = _signer.GenerateGenericSignature(endpoint, parameters1, TEST_APP_ID, TEST_APP_SECRET);
            var signature2 = _signer.GenerateGenericSignature(endpoint, parameters2, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            signature1.Should().Be(signature2);
        }

        [Fact]
        public void GenerateGenericSignature_WithDifferentAppIds_ShouldReturnDifferentHashes()
        {
            // Arrange
            var endpoint = "album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123" } };

            // Act
            var sig1 = _signer.GenerateGenericSignature(endpoint, parameters, "app1", TEST_APP_SECRET);
            var sig2 = _signer.GenerateGenericSignature(endpoint, parameters, "app2", TEST_APP_SECRET);

            // Assert
            sig1.Should().NotBe(sig2);
        }

        [Fact]
        public void GenerateGenericSignature_WithComplexEndpoint_ShouldParseCorrectly()
        {
            // Arrange
            var endpoint = "/artist/getSimilar";
            var parameters = new Dictionary<string, string>
            {
                { "artist_id", "123456" },
                { "limit", "10" }
            };

            // Act
            var signature = _signer.GenerateGenericSignature(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            signature.Should().NotBeNullOrWhiteSpace();
            signature.Should().HaveLength(32);
        }

        #endregion

        #region SignRequest Tests

        [Fact]
        public void SignRequest_WithTrackGetFileUrlEndpoint_ShouldAddTimestampAndSignature()
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" },
                { "format_id", "27" },
                { "app_id", TEST_APP_ID }
            };

            // Act
            _signer.SignRequest(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            parameters.Should().ContainKey("request_ts");
            parameters.Should().ContainKey("request_sig");
            parameters["request_ts"].Should().NotBeNullOrWhiteSpace();
            parameters["request_sig"].Should().NotBeNullOrWhiteSpace();
            parameters["request_sig"].Should().HaveLength(32);
        }

        [Fact]
        public void SignRequest_WithNonSignedEndpoint_ShouldNotAddSignature()
        {
            // Arrange
            var endpoint = "album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" },
                { "app_id", TEST_APP_ID }
            };
            var originalParameterCount = parameters.Count;

            // Act
            _signer.SignRequest(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            parameters.Should().NotContainKey("request_ts");
            parameters.Should().NotContainKey("request_sig");
            parameters.Should().HaveCount(originalParameterCount);
        }

        [Fact]
        public void SignRequest_WithNullAppSecret_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" },
                { "format_id", "27" }
            };

            // Act & Assert
            _signer.Invoking(x => x.SignRequest(endpoint, parameters, TEST_APP_ID, null))
                   .Should().Throw<InvalidOperationException>()
                   .WithMessage("*App Secret is required*");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void SignRequest_WithEmptyAppSecret_ShouldThrowInvalidOperationException(string appSecret)
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" },
                { "format_id", "27" }
            };

            // Act & Assert
            _signer.Invoking(x => x.SignRequest(endpoint, parameters, TEST_APP_ID, appSecret))
                   .Should().Throw<InvalidOperationException>()
                   .WithMessage("*App Secret is required*");
        }

        [Fact]
        public void SignRequest_WithMissingTrackParameters_ShouldUseEmptyStrings()
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "app_id", TEST_APP_ID }
                // Missing track_id and format_id
            };

            // Act
            _signer.SignRequest(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            parameters.Should().ContainKey("request_ts");
            parameters.Should().ContainKey("request_sig");
            parameters["request_sig"].Should().NotBeNullOrWhiteSpace();
        }

        #endregion

        #region Timestamp Tests

        [Fact]
        public void SignRequest_ShouldAddValidUnixTimestamp()
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" },
                { "format_id", "27" }
            };
            var beforeTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Act
            _signer.SignRequest(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            var afterTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timestamp = long.Parse(parameters["request_ts"]);
            timestamp.Should().BeGreaterOrEqualTo(beforeTime);
            timestamp.Should().BeLessOrEqualTo(afterTime);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void SignRequest_WithNullParameters_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            _signer.Invoking(x => x.SignRequest("track/getFileUrl", null, TEST_APP_ID, TEST_APP_SECRET))
                   .Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SignRequest_WithEmptyEndpoint_ShouldHandleGracefully()
        {
            // Arrange
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" }
            };

            // Act & Assert - Should not throw
            _signer.SignRequest("", parameters, TEST_APP_ID, TEST_APP_SECRET);
        }

        [Fact]
        public void SignRequest_WithParametersContainingSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123-456@789" },
                { "format_id", "27" },
                { "test_param", "value with spaces & symbols!" }
            };

            // Act
            _signer.SignRequest(endpoint, parameters, TEST_APP_ID, TEST_APP_SECRET);

            // Assert
            parameters.Should().ContainKey("request_sig");
            parameters["request_sig"].Should().HaveLength(32);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void SignRequest_EndToEnd_WithRealScenario_ShouldProduceValidSignature()
        {
            // Arrange - Simulate real API call scenario
            var endpoint = "track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "49807357" },
                { "format_id", "27" },
                { "app_id", "285473059" },
                { "user_auth_token", "sample_token_12345" }
            };

            // Act
            _signer.SignRequest(endpoint, parameters, "285473059", "D83CFDC8BDF5F3B6C9C5DA37E5B2B0DF");

            // Assert
            parameters.Should().ContainKey("request_ts");
            parameters.Should().ContainKey("request_sig");
            parameters["request_sig"].Should().HaveLength(32);
            parameters["request_sig"].Should().MatchRegex("^[a-f0-9]{32}$");

            // Verify signature is deterministic for same timestamp
            var timestamp = parameters["request_ts"];
            var trackId = parameters["track_id"];
            var formatId = parameters["format_id"];
            
            var expectedSignature = _signer.GenerateTrackUrlSignature(
                trackId, formatId, timestamp, "D83CFDC8BDF5F3B6C9C5DA37E5B2B0DF");
            
            parameters["request_sig"].Should().Be(expectedSignature);
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}