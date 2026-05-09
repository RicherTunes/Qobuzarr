using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Utilities;

/// <summary>
/// Coverage for LidarrInputValidator — security-critical input sanitizer for
/// album/artist names that flow into file paths and HTTP requests.
/// </summary>
public sealed class LidarrInputValidatorTests
{
    // ─── SanitizeAlbumTitle ───────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Unknown Album")]
    [InlineData("", "Unknown Album")]
    [InlineData("   ", "Unknown Album")]
    public void SanitizeAlbumTitle_NullOrWhitespace_ReturnsDefault(string? input, string expected)
    {
        Assert.Equal(expected, LidarrInputValidator.SanitizeAlbumTitle(input));
    }

    [Theory]
    // Path traversal
    [InlineData("../etc/shadow", "__etc_shadow")]
    [InlineData("..\\windows\\system32", "__windows_system32")]
    // Forward / back slash
    [InlineData("Some/Album", "Some_Album")]
    [InlineData("Some\\Album", "Some_Album")]
    // Windows-invalid chars
    [InlineData("Album:Name", "Album-Name")]
    [InlineData("a*b?c\"d<e>f|g", "a_b_c'd_e_f_g")]
    // Clean input passes through
    [InlineData("Kind of Blue", "Kind of Blue")]
    [InlineData("Album (1959)", "Album (1959)")]
    public void SanitizeAlbumTitle_DangerousChars_Replaced(string input, string expected)
    {
        Assert.Equal(expected, LidarrInputValidator.SanitizeAlbumTitle(input));
    }

    [Fact]
    public void SanitizeAlbumTitle_OverlyLong_TruncatedTo100Chars()
    {
        var input = new string('a', 250);
        var result = LidarrInputValidator.SanitizeAlbumTitle(input);
        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void SanitizeAlbumTitle_AllDangerousCharsAfterSanitization_ReturnsFallback()
    {
        // After replacements an all-whitespace result triggers the fallback.
        var result = LidarrInputValidator.SanitizeAlbumTitle("   ");
        Assert.Equal("Unknown Album", result);
    }

    [Fact]
    public void SanitizeArtistName_DelegatesToAlbumTitleLogic()
    {
        // Artist sanitization uses the same code path; verify the artist-default differs.
        Assert.Equal("Unknown Artist", LidarrInputValidator.SanitizeArtistName(null));
        Assert.Equal("Test_Artist", LidarrInputValidator.SanitizeArtistName("Test/Artist"));
    }

    // ─── IsInputSafe ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Miles Davis", true)]
    [InlineData("Album with spaces and (parens)", true)]
    public void IsInputSafe_BenignInput_True(string? input, bool expected)
    {
        Assert.Equal(expected, LidarrInputValidator.IsInputSafe(input));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Hello <SCRIPT> evil")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]   // case-insensitive
    [InlineData("vbscript:msgbox()")]
    [InlineData("img onload=evil")]
    [InlineData("img onerror=evil")]
    public void IsInputSafe_ScriptInjectionPatterns_False(string input)
    {
        Assert.False(LidarrInputValidator.IsInputSafe(input));
    }

    [Theory]
    [InlineData("../etc/shadow")]
    [InlineData("..\\sensitive\\path")]
    public void IsInputSafe_PathTraversal_False(string input)
    {
        Assert.False(LidarrInputValidator.IsInputSafe(input));
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.bat")]
    [InlineData("evil.cmd")]
    [InlineData("malware.PS1")]   // case-insensitive
    [InlineData("trojan.scr")]
    public void IsInputSafe_DangerousExtensions_False(string input)
    {
        Assert.False(LidarrInputValidator.IsInputSafe(input));
    }

    // ─── IsUrlSafe ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsUrlSafe_NullOrWhitespace_False(string? input)
    {
        Assert.False(LidarrInputValidator.IsUrlSafe(input));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://api.qobuz.com/0.2/album/get")]
    [InlineData("https://example.com:8443/path?query=1")]
    public void IsUrlSafe_ValidHttpsHttp_True(string url)
    {
        Assert.True(LidarrInputValidator.IsUrlSafe(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("data:text/html,<script>")]
    public void IsUrlSafe_DangerousSchemes_False(string url)
    {
        Assert.False(LidarrInputValidator.IsUrlSafe(url));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("http://")]
    [InlineData(":///bare")]
    public void IsUrlSafe_Malformed_False(string url)
    {
        Assert.False(LidarrInputValidator.IsUrlSafe(url));
    }

    // ─── IsApiKeyFormatValid ──────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("short", false)]                                       // < 20 chars
    [InlineData("abcdefghijklmnopqrst", true)]                         // exactly 20 chars
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", true)]         // 36 chars
    [InlineData("has spaces in the key here ", false)]                 // contains space
    [InlineData("has-dashes-in-the-key-here-12345678", false)]         // contains dash
    public void IsApiKeyFormatValid_HandlesEdgeCases(string? apiKey, bool expected)
    {
        Assert.Equal(expected, LidarrInputValidator.IsApiKeyFormatValid(apiKey));
    }

    [Fact]
    public void IsApiKeyFormatValid_OverlyLongKey_False()
    {
        var tooLong = new string('a', 101);
        Assert.False(LidarrInputValidator.IsApiKeyFormatValid(tooLong));
    }

    // ─── IsResponseSizeAcceptable ─────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]                          // unknown size = OK (rely on stream limits elsewhere)
    [InlineData(0L, true)]
    [InlineData(50L * 1024 * 1024, true)]             // exactly at limit
    [InlineData(50L * 1024 * 1024 + 1, false)]        // just over
    [InlineData(100L * 1024 * 1024, false)]
    public void IsResponseSizeAcceptable_50MBLimit(long? size, bool expected)
    {
        Assert.Equal(expected, LidarrInputValidator.IsResponseSizeAcceptable(size));
    }

    // ─── LevenshteinDistance ──────────────────────────────────────────────

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "xyz", 3)]
    [InlineData("kitten", "sitting", 3)]              // classic example
    [InlineData("identical", "identical", 0)]
    [InlineData("Miles", "miles", 1)]                 // case-sensitive
    public void LevenshteinDistance_KnownCases(string s1, string s2, int expected)
    {
        Assert.Equal(expected, LidarrInputValidator.LevenshteinDistance(s1, s2));
    }
}
