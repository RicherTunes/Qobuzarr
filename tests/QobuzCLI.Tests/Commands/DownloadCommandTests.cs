using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QobuzCLI.Commands;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace QobuzCLI.Tests.Commands;

/// <summary>
/// Tests for the refactored DownloadCommand that now properly delegates to plugin functionality.
/// These tests verify the CLI's UI/UX behavior while ensuring it uses plugin services correctly.
/// </summary>
public class DownloadCommandTests
{
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly Mock<IPluginHost> _mockPluginHost;
    private readonly Mock<ISearchService> _mockSearchService;
    private readonly Mock<IQueueService> _mockQueueService;
    private readonly Mock<ILogger<DownloadCommand>> _mockLogger;
    private readonly Mock<ILogger<Dashboard>> _mockDashboardLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IBatchDownloadService> _mockBatchDownloadService;
    private readonly Mock<IInteractiveSelectionService> _mockSelectionService;
    private readonly QueueMonitoringService _queueMonitoring;
    private readonly DownloadCommand _downloadCommand;

    public DownloadCommandTests()
    {
        _mockConfigService = new Mock<IConfigService>();
        _mockPluginHost = new Mock<IPluginHost>();
        _mockSearchService = new Mock<ISearchService>();
        _mockQueueService = new Mock<IQueueService>();
        _mockLogger = new Mock<ILogger<DownloadCommand>>();
        _mockDashboardLogger = new Mock<ILogger<Dashboard>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockBatchDownloadService = new Mock<IBatchDownloadService>();
        _mockSelectionService = new Mock<IInteractiveSelectionService>();
        _queueMonitoring = new QueueMonitoringService(_mockQueueService.Object);

        _downloadCommand = new DownloadCommand(
            _mockConfigService.Object,
            _mockPluginHost.Object,
            _mockSearchService.Object,
            _mockQueueService.Object,
            _mockLogger.Object,
            new Mock<IDashboard>().Object,
            _mockBatchDownloadService.Object,
            _queueMonitoring,
            _mockSelectionService.Object
        );
    }

    [Fact]
    public void Constructor_ShouldCreateCommand()
    {
        // Arrange & Act
        var command = _downloadCommand.Command;

        // Assert
        command.Should().NotBeNull();
        command.Name.Should().Be("download");
        command.Description.Should().Be("Add music to download queue");
    }

    [Fact]
    public void Command_ShouldHaveExpectedOptions()
    {
        // Arrange & Act
        var command = _downloadCommand.Command;

        // Assert
        // System.CommandLine's Option.Name may not include dashes; check aliases instead
        var optionNames = command.Options.SelectMany(o => o.Aliases).ToList();
        
        optionNames.Should().Contain("--from-file");
        optionNames.Should().Contain("--immediate");
        optionNames.Should().Contain("--output");
        optionNames.Should().Contain("--quality");
        optionNames.Should().Contain("--select");
        optionNames.Should().Contain("--all");
        optionNames.Should().Contain("--type");
        optionNames.Should().Contain("--priority");
        optionNames.Should().Contain("--queue");
        optionNames.Should().Contain("--concurrency");
    }

    [Theory]
    [InlineData("mp3-320")]
    [InlineData("flac-cd")]
    [InlineData("flac-hires")]
    [InlineData("flac-max")]
    public void ApplyDownloadOverrides_ShouldHandleQualityOverrides(string quality)
    {
        // This test verifies the refactored CLI correctly applies quality overrides
        // while delegating actual download logic to the plugin
        
        // Arrange
        var originalConfig = new QobuzConfig
        {
            Quality = "flac-cd",
            OutputDirectory = "/original/path",
            MaxConcurrentDownloads = 5
        };

        // Act - Use reflection to test the private method
        var method = typeof(DownloadCommand).GetMethod("ApplyDownloadOverrides", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (QobuzConfig)method!.Invoke(_downloadCommand, new object[] { originalConfig, null, quality })!;

        // Assert
        result.Quality.Should().Be(quality);
        result.OutputDirectory.Should().Be(originalConfig.OutputDirectory);
        result.MaxConcurrentDownloads.Should().Be(originalConfig.MaxConcurrentDownloads);
    }

    [Fact]
    public void ParseSearchType_ShouldHandleAllValidTypes()
    {
        // Arrange & Act - Use reflection to test the private method
        var method = typeof(DownloadCommand).GetMethod("ParseSearchType", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var albumResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { "album" })!;
        var artistResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { "artist" })!;
        var trackResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { "track" })!;
        var autoResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { "auto" })!;
        var nullResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { null! })!;
        var invalidResult = (SearchType)method!.Invoke(_downloadCommand, new object[] { "invalid" })!;

        // Assert
        albumResult.Should().Be(SearchType.Album);
        artistResult.Should().Be(SearchType.Artist);
        trackResult.Should().Be(SearchType.Track);
        autoResult.Should().Be(SearchType.Auto);
        nullResult.Should().Be(SearchType.Auto);
        invalidResult.Should().Be(SearchType.Auto);
    }
}

/// <summary>
/// Tests for quality ID mapping functionality.
/// </summary>
public class QualityMappingTests
{
    private readonly DownloadCommand _downloadCommand;

    public QualityMappingTests()
    {
        var mockConfigService = new Mock<IConfigService>();
        var mockPluginHost = new Mock<IPluginHost>();
        var mockSearchService = new Mock<ISearchService>();
        var mockQueueService = new Mock<IQueueService>();
        var mockLogger = new Mock<ILogger<DownloadCommand>>();
        var mockDashboard = new Mock<IDashboard>();
        var mockBatchDownloadService = new Mock<IBatchDownloadService>();
        var queueMonitoring = new QueueMonitoringService(mockQueueService.Object);

        _downloadCommand = new DownloadCommand(
            mockConfigService.Object,
            mockPluginHost.Object,
            mockSearchService.Object,
            mockQueueService.Object,
            mockLogger.Object,
            mockDashboard.Object,
            mockBatchDownloadService.Object,
            queueMonitoring,
            new Mock<IInteractiveSelectionService>().Object
        );
    }

    [Theory]
    [InlineData("mp3-320", 5)]
    [InlineData("flac-cd", 6)]
    [InlineData("flac-hires", 7)]
    [InlineData("flac-max", 27)]
    [InlineData("unknown", 27)] // Should default to highest quality
    public void GetQualityId_ShouldReturnCorrectMappings(string quality, int expectedId)
    {
        // Arrange & Act - Use reflection to test the private method
        var method = typeof(DownloadCommand).GetMethod("GetQualityId", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (int)method!.Invoke(_downloadCommand, new object[] { quality })!;

        // Assert
        result.Should().Be(expectedId);
    }
}
