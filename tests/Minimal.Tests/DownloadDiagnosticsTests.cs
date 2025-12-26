using System.Text;
using Lidarr.Plugin.Common.Utilities;
using Xunit;

namespace Minimal.Tests;

/// <summary>
/// Tests validating the common DownloadDiagnostics utilities work correctly
/// for Qobuzarr's "safe snippet" error reporting paths.
/// </summary>
public class DownloadDiagnosticsTests
{
    [Theory]
    [InlineData("https://example.com/callback?token=abc123")]
    [InlineData("user_auth_token=secret")]
    [InlineData("apikey=12345")]
    [InlineData("api_key=12345")]
    [InlineData("password=hunter2")]
    [InlineData("app_secret=xyz")]
    [InlineData("bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9")]
    [InlineData("Authorization: Bearer abc")]
    public void ShouldRedactSnippet_WithSensitiveContent_ReturnsTrue(string sensitiveContent)
    {
        // Act
        var shouldRedact = DownloadDiagnostics.ShouldRedactSnippet(sensitiveContent);

        // Assert
        Assert.True(shouldRedact, $"Expected '{sensitiveContent}' to be redacted");
    }

    [Theory]
    [InlineData("Error 403: Access Denied")]
    [InlineData("Track not available in your region")]
    [InlineData("<html><body>Service Unavailable</body></html>")]
    [InlineData("{\"error\": \"rate_limited\"}")]
    public void ShouldRedactSnippet_WithSafeContent_ReturnsFalse(string safeContent)
    {
        // Act
        var shouldRedact = DownloadDiagnostics.ShouldRedactSnippet(safeContent);

        // Assert
        Assert.False(shouldRedact, $"Expected '{safeContent}' to NOT be redacted");
    }

    [Fact]
    public void CreateSafeSnippet_WithTokenInPayload_ReturnsRedacted()
    {
        // Arrange: Payload containing a token (common Qobuz error response)
        var payloadWithToken = Encoding.UTF8.GetBytes("Error: Invalid token=abc123 for request");

        // Act
        var snippet = DownloadDiagnostics.CreateSafeSnippet(payloadWithToken, payloadWithToken.Length);

        // Assert
        Assert.Equal("[redacted]", snippet);
    }

    [Fact]
    public void CreateSafeSnippet_WithSafeError_ReturnsContent()
    {
        // Arrange: Safe error message
        var safePayload = Encoding.UTF8.GetBytes("Track unavailable");

        // Act
        var snippet = DownloadDiagnostics.CreateSafeSnippet(safePayload, safePayload.Length);

        // Assert
        Assert.Equal("Track unavailable", snippet);
    }

    [Fact]
    public void LooksLikeTextPayload_WithHtmlResponse_ReturnsTrue()
    {
        // Arrange: HTML error page
        var htmlPayload = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Error</body></html>");

        // Act
        var isText = DownloadDiagnostics.LooksLikeTextPayload(htmlPayload, htmlPayload.Length);

        // Assert
        Assert.True(isText);
    }

    [Fact]
    public void LooksLikeTextPayload_WithJsonObject_ReturnsTrue()
    {
        // Arrange: JSON error response
        var jsonPayload = Encoding.UTF8.GetBytes("{\"error\": \"unauthorized\"}");

        // Act
        var isText = DownloadDiagnostics.LooksLikeTextPayload(jsonPayload, jsonPayload.Length);

        // Assert
        Assert.True(isText);
    }

    [Fact]
    public void LooksLikeTextPayload_WithFlacMagic_ReturnsFalse()
    {
        // Arrange: FLAC magic bytes
        var flacHeader = new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22 };

        // Act
        var isText = DownloadDiagnostics.LooksLikeTextPayload(flacHeader, flacHeader.Length);

        // Assert
        Assert.False(isText);
    }

    [Fact]
    public void IsTextLikeContentType_WithAudioContentType_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(DownloadDiagnostics.IsTextLikeContentType("audio/flac"));
        Assert.False(DownloadDiagnostics.IsTextLikeContentType("audio/mpeg"));
        Assert.False(DownloadDiagnostics.IsTextLikeContentType("application/octet-stream"));
    }

    [Fact]
    public void IsTextLikeContentType_WithTextContentType_ReturnsTrue()
    {
        // Act & Assert
        Assert.True(DownloadDiagnostics.IsTextLikeContentType("text/html"));
        Assert.True(DownloadDiagnostics.IsTextLikeContentType("application/json"));
        Assert.True(DownloadDiagnostics.IsTextLikeContentType("text/xml"));
    }
}
