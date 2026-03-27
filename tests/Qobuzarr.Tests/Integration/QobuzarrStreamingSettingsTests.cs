using System;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Integration;

namespace Qobuzarr.Tests.Integration;

/// <summary>
/// Tests for <see cref="QobuzarrStreamingSettings"/> validation logic.
/// </summary>
public class QobuzarrStreamingSettingsTests
{
    private static QobuzarrStreamingSettings CreateValid() => new()
    {
        Email = "user@example.com",
        Password = "s3cret",
        DownloadPath = "/music/downloads",
        PreferredQuality = 6,
        CountryCode = "US",
        SearchLimit = 100
    };

    // ---- IsValid: happy path ----

    [Fact]
    public void IsValid_WithValidSettings_ReturnsTrue()
    {
        var settings = CreateValid();

        var result = settings.IsValid(out var error);

        Assert.True(result);
        Assert.True(error is null || error == string.Empty, $"Expected null or empty error but got: '{error}'");
    }

    // ---- Email ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_WithEmptyEmail_ReturnsFalse(string email)
    {
        var settings = CreateValid();
        settings.Email = email;

        var result = settings.IsValid(out var error);

        Assert.False(result);
        Assert.Contains("Email", error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Password ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_WithEmptyPassword_ReturnsFalse(string password)
    {
        var settings = CreateValid();
        settings.Password = password;

        var result = settings.IsValid(out var error);

        Assert.False(result);
        Assert.Contains("Password", error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- DownloadPath ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_WithEmptyDownloadPath_ReturnsFalse(string path)
    {
        var settings = CreateValid();
        settings.DownloadPath = path;

        var result = settings.IsValid(out var error);

        Assert.False(result);
        Assert.Contains("Download path", error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- PreferredQuality ----

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(27)]
    public void IsValid_WithValidQuality_ReturnsTrue(int quality)
    {
        var settings = CreateValid();
        settings.PreferredQuality = quality;

        var result = settings.IsValid(out _);

        Assert.True(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(26)]
    [InlineData(28)]
    [InlineData(100)]
    [InlineData(-1)]
    public void IsValid_WithInvalidQuality_ReturnsFalse(int quality)
    {
        var settings = CreateValid();
        settings.PreferredQuality = quality;

        var result = settings.IsValid(out var error);

        Assert.False(result);
        Assert.Contains("quality", error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- SearchLimit ----

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void IsValid_WithValidSearchLimit_ReturnsTrue(int limit)
    {
        var settings = CreateValid();
        settings.SearchLimit = limit;

        var result = settings.IsValid(out _);

        Assert.True(result);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    [InlineData(1000)]
    public void IsValid_WithInvalidSearchLimit_ReturnsFalse(int limit)
    {
        var settings = CreateValid();
        settings.SearchLimit = limit;

        var result = settings.IsValid(out var error);

        Assert.False(result);
        Assert.Contains("Search limit", error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Defaults ----

    [Fact]
    public void Constructor_SetsExpectedDefaults()
    {
        var settings = new QobuzarrStreamingSettings();

        Assert.Equal("https://www.qobuz.com/api.json/0.2", settings.BaseUrl);
        Assert.Equal(string.Empty, settings.Email);
        Assert.Equal(string.Empty, settings.Password);
        Assert.Equal(string.Empty, settings.DownloadPath);
        Assert.Equal(6, settings.PreferredQuality);
        Assert.Equal("US", settings.CountryCode);
        Assert.Equal(100, settings.SearchLimit);
    }
}
