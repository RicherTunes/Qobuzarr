using FluentAssertions;
using Moq;
using QobuzCLI.Models;
using QobuzCLI.Services;
using QobuzCLI.Commands;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QobuzCLI.Tests.Services;

/// <summary>
/// Tests for QueueMonitoringService that was extracted from DownloadCommand.
/// This demonstrates the improved separation of concerns.
/// </summary>
public class QueueMonitoringServiceTests
{
    private readonly Mock<IQueueService> _mockQueueService;
    private readonly QueueMonitoringService _queueMonitoringService;

    public QueueMonitoringServiceTests()
    {
        _mockQueueService = new Mock<IQueueService>();
        _queueMonitoringService = new QueueMonitoringService(_mockQueueService.Object);
    }

    [Fact]
    public void Constructor_ShouldAcceptQueueService()
    {
        // Arrange & Act
        var service = new QueueMonitoringService(_mockQueueService.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task MonitorQueueProgressSimpleAsync_ShouldHandleNullQueue()
    {
        // Arrange
        _mockQueueService.Setup(x => x.GetQueue("invalid-queue-id"))
            .Returns((DownloadQueue?)null);

        // Act & Assert - Should not throw
        await _queueMonitoringService.MonitorQueueProgressSimpleAsync("invalid-queue-id", new List<string>());
    }

    [Fact]
    public async Task MonitorQueueProgressSimpleAsync_ShouldCompleteWhenNoRemaining()
    {
        // Arrange
        var queueId = "test-queue";
        var queue = new DownloadQueue { Id = queueId, Name = "Test Queue" };
        var stats = new DownloadQueueStatistics
        {
            PendingItems = 0,
            ActiveDownloads = 0,
            CompletedItems = 5,
            FailedItems = 0
        };

        _mockQueueService.Setup(x => x.GetQueue(queueId)).Returns(queue);
        _mockQueueService.Setup(x => x.GetQueueStatistics(queueId)).Returns(stats);

        // Act & Assert - Should complete quickly without timeout
        var startTime = DateTime.UtcNow;
        await _queueMonitoringService.MonitorQueueProgressSimpleAsync(queueId, new List<string>());
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// Tests for the improved architecture where CLI properly uses plugin services.
/// </summary>
public class ArchitectureValidationTests
{
    [Fact]
    public void DownloadCommand_ShouldDependOnPluginHost()
    {
        // This test validates that the refactored CLI properly depends on plugin services
        // rather than reimplementing functionality

        // Arrange
        var downloadCommandType = typeof(DownloadCommand);
        var constructorParams = downloadCommandType.GetConstructors()[0].GetParameters();

        // Act & Assert
        var hasPluginHost = constructorParams.Any(p => p.ParameterType == typeof(IPluginHost));
        hasPluginHost.Should().BeTrue("DownloadCommand should depend on IPluginHost for core functionality");

        var hasExcessiveDependencies = constructorParams.Length > 10;
        hasExcessiveDependencies.Should().BeFalse("DownloadCommand should have reasonable dependency count after refactoring");
    }

    [Fact]
    public void QueueMonitoringService_ShouldBeSmallAndFocused()
    {
        // Validate that extracted service is focused and testable

        // Arrange
        var serviceType = typeof(QueueMonitoringService);
        var methods = serviceType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == serviceType).ToList();

        // Act & Assert
        methods.Count.Should().BeLessOrEqualTo(3, "Extracted service should be focused and small");

        var constructorParams = serviceType.GetConstructors()[0].GetParameters();
        constructorParams.Length.Should().BeLessOrEqualTo(2, "Service should have minimal dependencies");
    }
}
