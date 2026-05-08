using System.Text;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for DownloadResponseDiagnostics (internal static).
    /// Source: src/Download/DownloadResponseDiagnostics.cs.
    /// Wave 12 baseline: 0/32 lines covered.
    /// </summary>
    public class DownloadResponseDiagnosticsCovTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryGetHost_NullOrEmpty_ReturnsUnknown(string url)
        {
            DownloadResponseDiagnostics.TryGetHost(url).Should().Be("unknown");
        }

        [Fact]
        public void TryGetHost_ValidUrl_ReturnsHost()
        {
            DownloadResponseDiagnostics.TryGetHost("https://example.com/path?x=1").Should().Be("example.com");
        }

        [Fact]
        public void TryGetHost_InvalidUrl_ReturnsUnknown()
        {
            DownloadResponseDiagnostics.TryGetHost("not a uri at all").Should().Be("unknown");
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("text/plain", true)]
        [InlineData("TEXT/HTML", true)]
        [InlineData("application/json", true)]
        [InlineData("application/xml", true)]
        [InlineData("text/html", true)]
        [InlineData("application/octet-stream", false)]
        [InlineData("audio/flac", false)]
        public void IsTextLikeContentType_HandlesVariousInputs(string ct, bool expected)
        {
            DownloadResponseDiagnostics.IsTextLikeContentType(ct).Should().Be(expected);
        }

        [Fact]
        public void LooksLikeTextPayload_StartsWithBrace_ReturnsTrue()
        {
            var bytes = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeTrue();
        }

        [Fact]
        public void LooksLikeTextPayload_StartsWithBracket_ReturnsTrue()
        {
            var bytes = Encoding.UTF8.GetBytes("[1,2,3]");
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeTrue();
        }

        [Fact]
        public void LooksLikeTextPayload_StartsWithLessThan_ReturnsTrue()
        {
            var bytes = Encoding.UTF8.GetBytes("<html>");
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeTrue();
        }

        [Fact]
        public void LooksLikeTextPayload_LeadingWhitespaceThenBrace_ReturnsTrue()
        {
            var bytes = Encoding.UTF8.GetBytes("   \r\n\t {\"k\":1}");
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeTrue();
        }

        [Fact]
        public void LooksLikeTextPayload_BinaryBytes_ReturnsFalse()
        {
            var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }; // JPEG header
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeFalse();
        }

        [Fact]
        public void LooksLikeTextPayload_AllWhitespace_ReturnsFalse()
        {
            var bytes = Encoding.UTF8.GetBytes("                                ");
            DownloadResponseDiagnostics.LooksLikeTextPayload(bytes, bytes.Length).Should().BeFalse();
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("plain content", false)]
        [InlineData("contains http link", true)]
        [InlineData("Token: abc", true)]
        [InlineData("password=foo", true)]
        [InlineData("secret value", true)]
        [InlineData("apikey=xyz", true)]
        [InlineData("api_key=xyz", true)]
        [InlineData("user_auth_token=...", true)]
        [InlineData("app_secret=...", true)]
        public void ShouldRedactSnippet_DetectsSecrets(string snippet, bool expected)
        {
            DownloadResponseDiagnostics.ShouldRedactSnippet(snippet).Should().Be(expected);
        }
    }
}
