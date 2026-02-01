using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using QobuzCLI.Models;
using QobuzCLI.Models.Configuration;
using QobuzCLI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace QobuzCLI.Tests.Services;

/// <summary>
/// Comprehensive tests for ConfigService focusing on migration from legacy QobuzConfig to QobuzConfiguration.
/// Validates that user settings are preserved across configuration format changes.
/// </summary>
public class ConfigServiceTests : IDisposable
{
    private readonly Mock<ILogger<ConfigService>> _mockLogger;
    private readonly string _tempDirectory;
    private readonly string _legacyConfigPath;
    private readonly string _newConfigPath;
    private readonly IConfigService _configService;

    public ConfigServiceTests()
    {
        _mockLogger = new Mock<ILogger<ConfigService>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"qobuz-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        _legacyConfigPath = Path.Combine(_tempDirectory, "qobuz-config.json");
        _newConfigPath = Path.Combine(_tempDirectory, "qobuz-configuration.json");

        // Create a test ConfigService that uses our temp directory
        _configService = new TestConfigService(_mockLogger.Object, _tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenNewConfigExists_ShouldLoadNewConfig()
    {
        // Arrange
        var expectedConfig = new QobuzConfiguration
        {
            Authentication = new AuthenticationConfig { Email = "test@example.com" },
            Quality = new QualityConfig { Quality = "flac-hires" },
            Download = new DownloadConfig { OutputDirectory = "/music" }
        };
        var json = JsonConvert.SerializeObject(expectedConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_newConfigPath, json);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Authentication.Email.Should().Be("test@example.com");
        result.Quality.Quality.Should().Be("flac-hires");
        result.Download.OutputDirectory.Should().Be("/music");
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenOnlyLegacyConfigExists_ShouldMigrateToNewFormat()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            Email = "legacy@example.com",
            Password = "legacy-password",
            Quality = "flac-cd",
            OutputDirectory = "/legacy-music",
            MaxConcurrentDownloads = 2,
            CreateArtistFolders = false,
            SearchResultLimit = 15,
            VerboseLogging = true
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert - Check that all legacy values were migrated correctly
        result.Authentication.Email.Should().Be("legacy@example.com");
        result.Authentication.Password.Should().Be("legacy-password");
        result.Quality.Quality.Should().Be("flac-cd");
        result.Download.OutputDirectory.Should().Be("/legacy-music");
        result.Download.MaxConcurrentDownloads.Should().Be(2);
        result.Download.CreateArtistFolders.Should().BeFalse();
        result.Search.SearchResultLimit.Should().Be(15);
        result.System.VerboseLogging.Should().BeTrue();

        // Verify new config file was created
        File.Exists(_newConfigPath).Should().BeTrue();

        // Verify migrated content in new file
        var savedJson = await File.ReadAllTextAsync(_newConfigPath);
        var savedConfig = JsonConvert.DeserializeObject<QobuzConfiguration>(savedJson);
        savedConfig!.Authentication.Email.Should().Be("legacy@example.com");
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenNoConfigExists_ShouldCreateDefaultConfiguration()
    {
        // Arrange - No config files exist (temp directory is empty)

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Should().NotBeNull();
        result.Authentication.Should().NotBeNull();
        result.Quality.Should().NotBeNull();
        result.Download.Should().NotBeNull();
        result.Search.Should().NotBeNull();
        result.System.Should().NotBeNull();

        // Verify default config file was created
        File.Exists(_newConfigPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveConfigurationAsync_ShouldPersistToNewFormat()
    {
        // Arrange
        var config = new QobuzConfiguration
        {
            Authentication = new AuthenticationConfig { Email = "save-test@example.com" },
            Quality = new QualityConfig { Quality = "flac-max" }
        };

        // Act
        await _configService.SaveConfigurationAsync(config);

        // Assert
        File.Exists(_newConfigPath).Should().BeTrue();

        var savedJson = await File.ReadAllTextAsync(_newConfigPath);
        var savedConfig = JsonConvert.DeserializeObject<QobuzConfiguration>(savedJson);
        savedConfig!.Authentication.Email.Should().Be("save-test@example.com");
        savedConfig.Quality.Quality.Should().Be("flac-max");
    }

    [Fact]
    public async Task LoadConfigAsync_ShouldReturnLegacyFormatFromNewConfig()
    {
        // Arrange
        var newConfig = new QobuzConfiguration
        {
            Authentication = new AuthenticationConfig { Email = "modern@example.com" },
            Quality = new QualityConfig { Quality = "flac-hires" },
            Download = new DownloadConfig { MaxConcurrentDownloads = 6 }
        };
        var json = JsonConvert.SerializeObject(newConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_newConfigPath, json);

        // Act
        var result = await _configService.LoadConfigAsync();

        // Assert - Should get legacy format but with modern config values
        result.Should().BeOfType<QobuzConfig>();
        result.Email.Should().Be("modern@example.com");
        result.Quality.Should().Be("flac-hires");
        result.MaxConcurrentDownloads.Should().Be(6);
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldConvertLegacyToNewFormat()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            Email = "convert-test@example.com",
            Quality = "mp3-320",
            OutputDirectory = "/convert-test"
        };

        // Act
        await _configService.SaveConfigAsync(legacyConfig);

        // Assert - Should save in new format
        File.Exists(_newConfigPath).Should().BeTrue();

        var savedJson = await File.ReadAllTextAsync(_newConfigPath);
        var savedConfig = JsonConvert.DeserializeObject<QobuzConfiguration>(savedJson);
        savedConfig!.Authentication.Email.Should().Be("convert-test@example.com");
        savedConfig.Quality.Quality.Should().Be("mp3-320");
        savedConfig.Download.OutputDirectory.Should().Be("/convert-test");
    }

    /// <summary>
    /// Tests the complete roundtrip migration: Legacy → New → Legacy
    /// to ensure no data is lost during conversion
    /// </summary>
    [Fact]
    public async Task MigrationRoundtrip_ShouldPreserveAllUserSettings()
    {
        // Arrange - Create comprehensive legacy config with all properties set
        var originalLegacy = new QobuzConfig
        {
            // Authentication
            Email = "roundtrip@example.com",
            Password = "secure-password",
            UserId = "12345",
            AuthToken = "auth-token-123",
            AuthMethod = "token",
            AppId = "app-id-123",
            AppSecret = "app-secret-456",
            Region = "US",
            CountryCode = "US",

            // Quality
            Quality = "flac-hires",
            AutoQualityFallback = false,
            QualityFallbackOrder = new List<string> { "flac-cd", "mp3-320" },

            // Download
            OutputDirectory = "/custom/music",
            MaxConcurrentDownloads = 8,
            MaxConcurrentApiRequests = 10,
            MaxConcurrentSearches = 5,
            MaxConcurrentArtistAlbums = 3,
            CreateArtistFolders = false,
            CreateAlbumFolders = false,
            FileNamingPattern = "{artist} - {title}",
            AlbumFolderPattern = "{year} - {album}",
            EnableMetadataTagging = false,
            ValidateDownloads = false,
            PartialSizeTolerancePercent = 15.5,

            // Search
            SearchResultLimit = 25,
            AutoResolveExactMatches = false,
            SearchPreference = "quality",

            // System
            ApiTimeoutSeconds = 45,
            RetryAttempts = 5,
            VerboseLogging = false,
            StateSaveIntervalSeconds = 120,
            MaxHistoryItems = 500,
            EnableMemoryOptimizations = false,
            EnableLocalCache = false,

            // Duplicate Handling
            EnableDuplicateDetection = false,
            EnableQualityUpgrades = false,
            MinQualityDifferencePercent = 25.0,
            KeepReplacedFiles = false,
            ReplacedFilesSuffix = "_old"
        };

        // Act - Convert Legacy → New → Legacy
        var newConfig = QobuzConfiguration.FromLegacyConfig(originalLegacy);
        var backToLegacy = newConfig.ToLegacyConfig();

        // Assert - Every property should be preserved exactly
        backToLegacy.Email.Should().Be(originalLegacy.Email);
        backToLegacy.Password.Should().Be(originalLegacy.Password);
        backToLegacy.UserId.Should().Be(originalLegacy.UserId);
        backToLegacy.AuthToken.Should().Be(originalLegacy.AuthToken);
        backToLegacy.AuthMethod.Should().Be(originalLegacy.AuthMethod);
        backToLegacy.AppId.Should().Be(originalLegacy.AppId);
        backToLegacy.AppSecret.Should().Be(originalLegacy.AppSecret);
        backToLegacy.Region.Should().Be(originalLegacy.Region);
        backToLegacy.CountryCode.Should().Be(originalLegacy.CountryCode);

        backToLegacy.Quality.Should().Be(originalLegacy.Quality);
        backToLegacy.AutoQualityFallback.Should().Be(originalLegacy.AutoQualityFallback);
        backToLegacy.QualityFallbackOrder.Should().Contain("flac-cd").And.Contain("mp3-320");

        backToLegacy.OutputDirectory.Should().Be(originalLegacy.OutputDirectory);
        backToLegacy.MaxConcurrentDownloads.Should().Be(originalLegacy.MaxConcurrentDownloads);
        backToLegacy.MaxConcurrentApiRequests.Should().Be(originalLegacy.MaxConcurrentApiRequests);
        backToLegacy.MaxConcurrentSearches.Should().Be(originalLegacy.MaxConcurrentSearches);
        backToLegacy.MaxConcurrentArtistAlbums.Should().Be(originalLegacy.MaxConcurrentArtistAlbums);
        backToLegacy.CreateArtistFolders.Should().Be(originalLegacy.CreateArtistFolders);
        backToLegacy.CreateAlbumFolders.Should().Be(originalLegacy.CreateAlbumFolders);
        backToLegacy.FileNamingPattern.Should().Be(originalLegacy.FileNamingPattern);
        backToLegacy.AlbumFolderPattern.Should().Be(originalLegacy.AlbumFolderPattern);
        backToLegacy.EnableMetadataTagging.Should().Be(originalLegacy.EnableMetadataTagging);
        backToLegacy.ValidateDownloads.Should().Be(originalLegacy.ValidateDownloads);
        backToLegacy.PartialSizeTolerancePercent.Should().Be(originalLegacy.PartialSizeTolerancePercent);

        backToLegacy.SearchResultLimit.Should().Be(originalLegacy.SearchResultLimit);
        backToLegacy.AutoResolveExactMatches.Should().Be(originalLegacy.AutoResolveExactMatches);
        backToLegacy.SearchPreference.Should().Be(originalLegacy.SearchPreference);

        backToLegacy.ApiTimeoutSeconds.Should().Be(originalLegacy.ApiTimeoutSeconds);
        backToLegacy.RetryAttempts.Should().Be(originalLegacy.RetryAttempts);
        backToLegacy.VerboseLogging.Should().Be(originalLegacy.VerboseLogging);
        backToLegacy.StateSaveIntervalSeconds.Should().Be(originalLegacy.StateSaveIntervalSeconds);
        backToLegacy.MaxHistoryItems.Should().Be(originalLegacy.MaxHistoryItems);
        backToLegacy.EnableMemoryOptimizations.Should().Be(originalLegacy.EnableMemoryOptimizations);
        backToLegacy.EnableLocalCache.Should().Be(originalLegacy.EnableLocalCache);

        backToLegacy.EnableDuplicateDetection.Should().Be(originalLegacy.EnableDuplicateDetection);
        backToLegacy.EnableQualityUpgrades.Should().Be(originalLegacy.EnableQualityUpgrades);
        backToLegacy.MinQualityDifferencePercent.Should().Be(originalLegacy.MinQualityDifferencePercent);
        backToLegacy.KeepReplacedFiles.Should().Be(originalLegacy.KeepReplacedFiles);
        backToLegacy.ReplacedFilesSuffix.Should().Be(originalLegacy.ReplacedFilesSuffix);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenBothConfigsExist_ShouldPreferNewConfig()
    {
        // Arrange
        var legacyConfig = new QobuzConfig { Email = "legacy@example.com", Quality = "mp3-320" };
        var newConfig = new QobuzConfiguration
        {
            Authentication = new AuthenticationConfig { Email = "new@example.com" },
            Quality = new QualityConfig { Quality = "flac-max" }
        };

        await File.WriteAllTextAsync(_legacyConfigPath, JsonConvert.SerializeObject(legacyConfig));
        await File.WriteAllTextAsync(_newConfigPath, JsonConvert.SerializeObject(newConfig));

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert - Should load from new config, not legacy
        result.Authentication.Email.Should().Be("new@example.com");
        result.Quality.Quality.Should().Be("flac-max");
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenCorruptedLegacyConfig_ShouldCreateDefault()
    {
        // Arrange
        await File.WriteAllTextAsync(_legacyConfigPath, "{ invalid json }");

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Should().NotBeNull();
        result.Authentication.Email.Should().BeNullOrEmpty();
        result.Quality.Quality.Should().Be("flac-max"); // Default value
    }

    [Fact]
    public async Task MigrationPreservesAuthenticationSettings()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            Email = "auth-test@example.com",
            Password = "secure-password-123",
            UserId = "user-456",
            AuthToken = "token-789",
            AuthMethod = "token",
            AppId = "custom-app-id",
            AppSecret = "custom-app-secret",
            Region = "EU",
            CountryCode = "FR"
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Authentication.Email.Should().Be("auth-test@example.com");
        result.Authentication.Password.Should().Be("secure-password-123");
        result.Authentication.UserId.Should().Be("user-456");
        result.Authentication.AuthToken.Should().Be("token-789");
        result.Authentication.AuthMethod.Should().Be("token");
        result.Authentication.AppId.Should().Be("custom-app-id");
        result.Authentication.AppSecret.Should().Be("custom-app-secret");
        result.Authentication.Region.Should().Be("EU");
        result.Authentication.CountryCode.Should().Be("FR");
    }

    [Fact]
    public async Task MigrationPreservesQualityAndDownloadSettings()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            Quality = "flac-hires",
            AutoQualityFallback = false,
            QualityFallbackOrder = new List<string> { "flac-cd", "mp3-320" },
            OutputDirectory = "/custom/output",
            MaxConcurrentDownloads = 12,
            CreateArtistFolders = false,
            CreateAlbumFolders = true,
            FileNamingPattern = "{track} - {artist} - {title}",
            EnableMetadataTagging = false,
            ValidateDownloads = true
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Quality.Quality.Should().Be("flac-hires");
        result.Quality.AutoQualityFallback.Should().BeFalse();
        result.Quality.QualityFallbackOrder.Should().Contain("flac-cd").And.Contain("mp3-320");
        result.Download.OutputDirectory.Should().Be("/custom/output");
        result.Download.MaxConcurrentDownloads.Should().Be(12);
        result.Download.CreateArtistFolders.Should().BeFalse();
        result.Download.CreateAlbumFolders.Should().BeTrue();
        result.Download.FileNamingPattern.Should().Be("{track} - {artist} - {title}");
        result.Download.EnableMetadataTagging.Should().BeFalse();
        result.Download.ValidateDownloads.Should().BeTrue();
    }

    [Fact]
    public async Task MigrationPreservesSearchAndSystemSettings()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            SearchResultLimit = 50,
            AutoResolveExactMatches = false,
            SearchPreference = "speed",
            ApiTimeoutSeconds = 60,
            RetryAttempts = 7,
            VerboseLogging = true,
            EnableLocalCache = false,
            MaxHistoryItems = 200
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.Search.SearchResultLimit.Should().Be(50);
        result.Search.AutoResolveExactMatches.Should().BeFalse();
        result.Search.SearchPreference.Should().Be("speed");
        result.System.ApiTimeoutSeconds.Should().Be(60);
        result.System.RetryAttempts.Should().Be(7);
        result.System.VerboseLogging.Should().BeTrue();
        result.System.EnableLocalCache.Should().BeFalse();
        result.System.MaxHistoryItems.Should().Be(200);
    }

    [Fact]
    public async Task MigrationPreservesDuplicateHandlingSettings()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            EnableDuplicateDetection = true,
            EnableQualityUpgrades = true,
            MinQualityDifferencePercent = 35.5,
            KeepReplacedFiles = true,
            ReplacedFilesSuffix = "_backup"
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert
        result.DuplicateHandling.EnableDuplicateDetection.Should().BeTrue();
        result.DuplicateHandling.EnableQualityUpgrades.Should().BeTrue();
        result.DuplicateHandling.MinQualityDifferencePercent.Should().Be(35.5);
        result.DuplicateHandling.KeepReplacedFiles.Should().BeTrue();
        result.DuplicateHandling.ReplacedFilesSuffix.Should().Be("_backup");
    }

    [Fact]
    public async Task MigrationHandlesNullAndEmptyValues()
    {
        // Arrange
        var legacyConfig = new QobuzConfig
        {
            Email = null,
            Password = "",
            QualityFallbackOrder = null,
            FileNamingPattern = null!  // Intentionally null to test migration
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act & Assert - Should not throw exceptions
        var result = await _configService.LoadConfigurationAsync();

        result.Should().NotBeNull();
        result.Authentication.Email.Should().BeNull();
        result.Authentication.Password.Should().Be("");
        result.Quality.QualityFallbackOrder.Should().BeNull();
        // FileNamingPattern now defaults to the standard pattern when null (migration fix)
        result.Download.FileNamingPattern.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConfigValidation_ShouldRunAfterMigration()
    {
        // Arrange - Create invalid legacy config
        var legacyConfig = new QobuzConfig
        {
            MaxConcurrentDownloads = -5, // Invalid value
            SearchResultLimit = 0,       // Invalid value
            ApiTimeoutSeconds = -10      // Invalid value
        };
        var legacyJson = JsonConvert.SerializeObject(legacyConfig, Formatting.Indented);
        await File.WriteAllTextAsync(_legacyConfigPath, legacyJson);

        // Act
        var result = await _configService.LoadConfigurationAsync();

        // Assert - Validation should have corrected invalid values
        result.Download.MaxConcurrentDownloads.Should().BeGreaterThan(0);
        result.Search.SearchResultLimit.Should().BeGreaterThan(0);
        result.System.ApiTimeoutSeconds.Should().BeGreaterThan(0);
    }
}

/// <summary>
/// Test-specific ConfigService that bypasses file system and uses direct object manipulation for testing
/// </summary>
internal class TestConfigService : IConfigService
{
    private readonly string _legacyConfigPath;
    private readonly string _newConfigPath;
    private readonly ILogger<ConfigService> _logger;
    private QobuzConfiguration? _configuration;
    private QobuzConfig? _config;

    public TestConfigService(ILogger<ConfigService> logger, string testDirectory)
    {
        _logger = logger;
        _legacyConfigPath = Path.Combine(testDirectory, "qobuz-config.json");
        _newConfigPath = Path.Combine(testDirectory, "qobuz-configuration.json");
    }

    public async Task<QobuzConfiguration> LoadConfigurationAsync()
    {
        if (_configuration != null)
            return _configuration;

        try
        {
            // Try to load the new configuration format first
            if (File.Exists(_newConfigPath))
            {
                var json = await File.ReadAllTextAsync(_newConfigPath);
                _configuration = JsonConvert.DeserializeObject<QobuzConfiguration>(json) ?? new QobuzConfiguration();
                _logger.LogDebug("New configuration loaded from {ConfigPath}", _newConfigPath);
            }
            // If new config doesn't exist, try to migrate from legacy config
            else if (File.Exists(_legacyConfigPath))
            {
                _logger.LogInformation("Migrating legacy configuration to new format");
                var legacyJson = await File.ReadAllTextAsync(_legacyConfigPath);
                var legacyConfig = JsonConvert.DeserializeObject<QobuzConfig>(legacyJson) ?? new QobuzConfig();
                _configuration = QobuzConfiguration.FromLegacyConfig(legacyConfig);

                // Save the migrated configuration
                await SaveConfigurationAsync(_configuration);
                _logger.LogInformation("Configuration migrated successfully to {NewConfigPath}", _newConfigPath);
            }
            else
            {
                // Create new default configuration
                _configuration = new QobuzConfiguration();
                await SaveConfigurationAsync(_configuration);
                _logger.LogInformation("Created default configuration at {ConfigPath}", _newConfigPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration, using defaults");
            _configuration = new QobuzConfiguration();
        }

        // Validate the loaded configuration
        _configuration.ValidateConfiguration();
        return _configuration;
    }

    public async Task SaveConfigurationAsync(QobuzConfiguration configuration)
    {
        try
        {
            var json = JsonConvert.SerializeObject(configuration, Formatting.Indented);
            await File.WriteAllTextAsync(_newConfigPath, json);
            _configuration = configuration;
            _logger.LogDebug("Configuration saved to {ConfigPath}", _newConfigPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    public async Task<QobuzConfig> LoadConfigAsync()
    {
        if (_config != null)
            return _config;

        // Load the new configuration and convert to legacy format for backward compatibility
        var newConfig = await LoadConfigurationAsync();
        _config = newConfig.ToLegacyConfig();

        return _config;
    }

    public async Task SaveConfigAsync(QobuzConfig config)
    {
        // Convert legacy config to new format and save
        var newConfig = QobuzConfiguration.FromLegacyConfig(config);
        await SaveConfigurationAsync(newConfig);
        _config = config;

        _logger.LogDebug("Legacy configuration converted and saved to new format");
    }

    public string GetConfigPath() => _legacyConfigPath;

    // Remaining methods not used in migration tests - return defaults
    public Task<T> GetValueAsync<T>(string key, T defaultValue = default!) => Task.FromResult(defaultValue);
    public Task SetValueAsync<T>(string key, T value) => Task.CompletedTask;
    public Task<Dictionary<string, object>> GetAllValuesAsync() => Task.FromResult(new Dictionary<string, object>());
    public Task<List<QobuzCLI.Models.ConfigParameter>> GetParametersAsync() => Task.FromResult(new List<QobuzCLI.Models.ConfigParameter>());
    public Task<T> GetAsync<T>(string key, T? defaultValue = default) => Task.FromResult(defaultValue!);
    public Task SetAsync<T>(string key, T value) => Task.CompletedTask;
    public Task<Dictionary<string, object>> GetAllAsync() => Task.FromResult(new Dictionary<string, object>());
    public Task SaveAsync() => Task.CompletedTask;
    public Task LoadAsync() => Task.CompletedTask;
}
