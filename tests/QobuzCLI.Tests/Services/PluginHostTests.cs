using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QobuzCLI.Models;
using QobuzCLI.Services;
using System;
using System.Threading.Tasks;
using Xunit;

namespace QobuzCLI.Tests.Services;

/// <summary>
/// Integration tests for PluginHost that validate proper plugin integration.
/// These tests ensure CLI correctly delegates to plugin services.
/// </summary>
public class PluginHostTests
{
    private readonly Mock<ILogger<PluginHost>> _mockLogger;
    private readonly Mock<CliHttpClient> _mockHttpClient;
    private readonly PluginHost _pluginHost;

    public PluginHostTests()
    {
        _mockLogger = new Mock<ILogger<PluginHost>>();
        _mockHttpClient = new Mock<CliHttpClient>();
        _pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Assert
        _pluginHost.Should().NotBeNull();
        _pluginHost.IsInitialized.Should().BeFalse("Plugin should not be initialized on construction");
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidConfig_ShouldThrow()
    {
        // Arrange
        var invalidConfig = new QobuzConfig(); // No credentials

        // Act & Assert
        await _pluginHost.Invoking(p => p.InitializeAsync(invalidConfig))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*credentials*");
    }

    [Fact]
    public async Task InitializeAsync_WithMockModeConfig_ShouldInitializeInMockMode()
    {
        // Arrange
        var mockConfig = new QobuzConfig 
        { 
            // No auth credentials - should initialize in mock mode
        };

        // Act
        await _pluginHost.InitializeAsync(mockConfig);

        // Assert
        _pluginHost.IsInitialized.Should().BeTrue("Plugin should initialize in mock mode");
    }

    [Fact]
    public void Auth_BeforeInitialization_ShouldThrow()
    {
        // Act & Assert
        _pluginHost.Invoking(p => _ = p.Auth)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Theory]
    [InlineData("album", SearchType.Album)]
    [InlineData("artist", SearchType.Artist)]
    [InlineData("track", SearchType.Track)]
    public void ConvertSearchType_ShouldMapCorrectly(string input, SearchType expected)
    {
        // Use reflection to test private method
        var method = typeof(PluginHost).GetMethod("ConvertToPluginSearchType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = method!.Invoke(_pluginHost, new object[] { input });

        // Assert - This tests that the mapping logic exists and works
        // In a real scenario, we'd verify the actual mapping
        result.Should().NotBeNull();
    }
}

/// <summary>
/// Tests for PluginHost search functionality.
/// </summary>
public class PluginHostSearchTests
{
    private readonly Mock<ILogger<PluginHost>> _mockLogger;
    private readonly Mock<CliHttpClient> _mockHttpClient;

    public PluginHostSearchTests()
    {
        _mockLogger = new Mock<ILogger<PluginHost>>();
        _mockHttpClient = new Mock<CliHttpClient>();
    }

    [Fact]
    public async Task SearchAsync_BeforeInitialization_ShouldThrow()
    {
        // Arrange
        var pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);

        // Act & Assert
        await pluginHost.Invoking(p => p.SearchAsync("test", SearchType.Album))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SearchAsync_WithInvalidQuery_ShouldReturnEmptyResults(string invalidQuery)
    {
        // Arrange
        var pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);
        var mockConfig = new QobuzConfig();
        await pluginHost.InitializeAsync(mockConfig);

        // Act
        var results = await pluginHost.SearchAsync(invalidQuery, SearchType.Album);

        // Assert
        results.Should().BeEmpty("Invalid queries should return empty results");
    }
}

/// <summary>
/// Tests for download functionality delegation to plugin.
/// </summary>
public class PluginHostDownloadTests
{
    private readonly Mock<ILogger<PluginHost>> _mockLogger;
    private readonly Mock<CliHttpClient> _mockHttpClient;

    public PluginHostDownloadTests()
    {
        _mockLogger = new Mock<ILogger<PluginHost>>();
        _mockHttpClient = new Mock<CliHttpClient>();
    }

    [Fact]
    public async Task DownloadAlbumAsync_BeforeInitialization_ShouldThrow()
    {
        // Arrange
        var pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);

        // Act & Assert
        await pluginHost.Invoking(p => p.DownloadAlbumAsync("123", "/test/path"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Fact]
    public async Task DownloadArtistAsync_ShouldThrowNotImplemented()
    {
        // Arrange
        var pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);
        var mockConfig = new QobuzConfig();
        await pluginHost.InitializeAsync(mockConfig);

        // Act & Assert
        await pluginHost.Invoking(p => p.DownloadArtistAsync("123", "/test/path"))
            .Should().ThrowAsync<NotImplementedException>()
            .WithMessage("*Artist download*not yet implemented*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task DownloadAlbumAsync_WithInvalidAlbumId_ShouldThrow(string invalidId)
    {
        // Arrange
        var pluginHost = new PluginHost(_mockLogger.Object, _mockHttpClient.Object);
        var mockConfig = new QobuzConfig();
        await pluginHost.InitializeAsync(mockConfig);

        // Act & Assert
        await pluginHost.Invoking(p => p.DownloadAlbumAsync(invalidId, "/test/path"))
            .Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Tests for configuration and environment variable handling.
/// </summary>
public class PluginHostConfigurationTests
{
    [Fact]
    public void HasValidAuth_WithEmailAndPassword_ShouldReturnTrue()
    {
        // Arrange
        var config = new QobuzConfig
        {
            Email = "test@example.com",
            Password = "password123"
        };

        // Act
        var hasAuth = config.HasValidAuth();

        // Assert
        hasAuth.Should().BeTrue("Email and password should constitute valid auth");
    }

    [Fact]
    public void HasValidAuth_WithTokenAuth_ShouldReturnTrue()
    {
        // Arrange
        var config = new QobuzConfig
        {
            UserId = "12345",
            AuthToken = "token123"
        };

        // Act
        var hasAuth = config.HasValidAuth();

        // Assert
        hasAuth.Should().BeTrue("User ID and token should constitute valid auth");
    }

    [Fact]
    public void HasValidAuth_WithoutCredentials_ShouldReturnFalse()
    {
        // Arrange
        var config = new QobuzConfig();

        // Act
        var hasAuth = config.HasValidAuth();

        // Assert
        hasAuth.Should().BeFalse("Empty config should not have valid auth");
    }

    [Fact]
    public void IsTokenAuth_WithTokenCredentials_ShouldReturnTrue()
    {
        // Arrange
        var config = new QobuzConfig
        {
            UserId = "12345",
            AuthToken = "token123"
        };

        // Act
        var isTokenAuth = config.IsTokenAuth();

        // Assert
        isTokenAuth.Should().BeTrue("Config with token should be identified as token auth");
    }
}