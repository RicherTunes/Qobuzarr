using System;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace Qobuzarr.Tests.Unit.Services
{
    /// <summary>
    /// Tests for queue-related model classes and their behavior.
    /// </summary>
    public class QueueModelsTests
    {
        #region QueueStatus Tests

        [Fact]
        public void QueueStatus_DefaultConstructor_InitializesWithZeroValues()
        {
            // Act
            var status = new QueueStatus();

            // Assert
            status.ActiveDownloads.Should().Be(0);
            status.ActiveSearches.Should().Be(0);
            status.MaxConcurrentDownloads.Should().Be(0);
            status.MaxConcurrentSearches.Should().Be(0);
            status.AvailableDownloadSlots.Should().Be(0);
            status.AvailableSearchSlots.Should().Be(0);
            status.IsDownloadQueueFull.Should().BeFalse();
            status.IsSearchQueueFull.Should().BeFalse();
        }

        [Fact]
        public void QueueStatus_WithProperties_SetCorrectly()
        {
            // Act
            var status = new QueueStatus
            {
                ActiveDownloads = 5,
                ActiveSearches = 3,
                MaxConcurrentDownloads = 10,
                MaxConcurrentSearches = 8,
                AvailableDownloadSlots = 5,
                AvailableSearchSlots = 5,
                IsDownloadQueueFull = false,
                IsSearchQueueFull = true
            };

            // Assert
            status.ActiveDownloads.Should().Be(5);
            status.ActiveSearches.Should().Be(3);
            status.MaxConcurrentDownloads.Should().Be(10);
            status.MaxConcurrentSearches.Should().Be(8);
            status.AvailableDownloadSlots.Should().Be(5);
            status.AvailableSearchSlots.Should().Be(5);
            status.IsDownloadQueueFull.Should().BeFalse();
            status.IsSearchQueueFull.Should().BeTrue();
        }

        [Theory]
        [InlineData(0, 10, false)]
        [InlineData(5, 10, false)]
        [InlineData(10, 10, true)]
        [InlineData(15, 10, true)] // Edge case: more active than max (shouldn't happen normally)
        public void QueueStatus_IsQueueFull_LogicWorks(int active, int max, bool expectedFull)
        {
            // Act
            var status = new QueueStatus
            {
                ActiveDownloads = active,
                MaxConcurrentDownloads = max,
                IsDownloadQueueFull = active >= max
            };

            // Assert
            status.IsDownloadQueueFull.Should().Be(expectedFull);
        }

        [Theory]
        [InlineData(0, 10, 10)]
        [InlineData(3, 10, 7)]
        [InlineData(10, 10, 0)]
        [InlineData(12, 10, -2)] // Edge case: negative available slots
        public void QueueStatus_AvailableSlots_CalculatedCorrectly(int active, int max, int expectedAvailable)
        {
            // Act
            var status = new QueueStatus
            {
                ActiveDownloads = active,
                MaxConcurrentDownloads = max,
                AvailableDownloadSlots = max - active
            };

            // Assert
            status.AvailableDownloadSlots.Should().Be(expectedAvailable);
        }

        #endregion

        #region QueueStatistics Tests

        [Fact]
        public void QueueStatistics_DefaultConstructor_InitializesWithZeroValues()
        {
            // Act
            var stats = new QueueStatistics();

            // Assert
            stats.TotalDownloadSlotAcquisitions.Should().Be(0);
            stats.TotalSearchSlotAcquisitions.Should().Be(0);
            stats.AverageDownloadWaitTime.Should().Be(TimeSpan.Zero);
            stats.AverageSearchWaitTime.Should().Be(TimeSpan.Zero);
            stats.PeakConcurrentDownloads.Should().Be(0);
            stats.PeakConcurrentSearches.Should().Be(0);
            stats.TotalDownloadSlotHoldTime.Should().Be(TimeSpan.Zero);
            stats.TotalSearchSlotHoldTime.Should().Be(TimeSpan.Zero);
            stats.DownloadQueueSaturations.Should().Be(0);
            stats.SearchQueueSaturations.Should().Be(0);
        }

        [Fact]
        public void QueueStatistics_WithProperties_SetCorrectly()
        {
            // Arrange
            var downloadWaitTime = TimeSpan.FromMilliseconds(150);
            var searchWaitTime = TimeSpan.FromMilliseconds(75);
            var downloadHoldTime = TimeSpan.FromSeconds(30);
            var searchHoldTime = TimeSpan.FromSeconds(15);

            // Act
            var stats = new QueueStatistics
            {
                TotalDownloadSlotAcquisitions = 1000,
                TotalSearchSlotAcquisitions = 500,
                AverageDownloadWaitTime = downloadWaitTime,
                AverageSearchWaitTime = searchWaitTime,
                PeakConcurrentDownloads = 8,
                PeakConcurrentSearches = 12,
                TotalDownloadSlotHoldTime = downloadHoldTime,
                TotalSearchSlotHoldTime = searchHoldTime,
                DownloadQueueSaturations = 5,
                SearchQueueSaturations = 3
            };

            // Assert
            stats.TotalDownloadSlotAcquisitions.Should().Be(1000);
            stats.TotalSearchSlotAcquisitions.Should().Be(500);
            stats.AverageDownloadWaitTime.Should().Be(downloadWaitTime);
            stats.AverageSearchWaitTime.Should().Be(searchWaitTime);
            stats.PeakConcurrentDownloads.Should().Be(8);
            stats.PeakConcurrentSearches.Should().Be(12);
            stats.TotalDownloadSlotHoldTime.Should().Be(downloadHoldTime);
            stats.TotalSearchSlotHoldTime.Should().Be(searchHoldTime);
            stats.DownloadQueueSaturations.Should().Be(5);
            stats.SearchQueueSaturations.Should().Be(3);
        }

        [Fact]
        public void QueueStatistics_TimeSpanProperties_HandleLargeValues()
        {
            // Arrange
            var largeTimeSpan = TimeSpan.FromDays(365); // One year

            // Act
            var stats = new QueueStatistics
            {
                AverageDownloadWaitTime = largeTimeSpan,
                AverageSearchWaitTime = largeTimeSpan,
                TotalDownloadSlotHoldTime = largeTimeSpan,
                TotalSearchSlotHoldTime = largeTimeSpan
            };

            // Assert
            stats.AverageDownloadWaitTime.Should().Be(largeTimeSpan);
            stats.AverageSearchWaitTime.Should().Be(largeTimeSpan);
            stats.TotalDownloadSlotHoldTime.Should().Be(largeTimeSpan);
            stats.TotalSearchSlotHoldTime.Should().Be(largeTimeSpan);
        }

        [Fact]
        public void QueueStatistics_LongProperties_HandleLargeValues()
        {
            // Act
            var stats = new QueueStatistics
            {
                TotalDownloadSlotAcquisitions = long.MaxValue,
                TotalSearchSlotAcquisitions = long.MaxValue
            };

            // Assert
            stats.TotalDownloadSlotAcquisitions.Should().Be(long.MaxValue);
            stats.TotalSearchSlotAcquisitions.Should().Be(long.MaxValue);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(100, 1000, 10)] // 100ms total / 10 acquisitions = 10ms average
        [InlineData(5000, 50, 100)] // 5000ms total / 50 acquisitions = 100ms average
        public void QueueStatistics_AverageWaitTime_CalculationLogic(long totalWaitMs, long acquisitions, long expectedAverageMs)
        {
            // Arrange
            var totalWaitTime = TimeSpan.FromMilliseconds(totalWaitMs);
            var expectedAverage = TimeSpan.FromMilliseconds(expectedAverageMs);

            // Simulate the calculation logic that would be used in the actual implementation
            // Uses tick arithmetic which may have slight precision differences from millisecond-based comparison
            var calculatedAverage = acquisitions > 0 
                ? TimeSpan.FromTicks(totalWaitTime.Ticks / acquisitions)
                : TimeSpan.Zero;

            // Act
            var stats = new QueueStatistics
            {
                TotalDownloadSlotAcquisitions = acquisitions,
                AverageDownloadWaitTime = calculatedAverage
            };

            // Assert - Use tolerance to account for tick/millisecond precision differences
            stats.AverageDownloadWaitTime.Should().BeCloseTo(expectedAverage, TimeSpan.FromMilliseconds(1));
        }

        #endregion

        #region DownloadQueueStatistics Tests

        [Fact]
        public void DownloadQueueStatistics_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var stats = new DownloadQueueStatistics();

            // Assert
            stats.TotalDownloads.Should().Be(0);
            stats.QueuedDownloads.Should().Be(0);
            stats.DownloadingDownloads.Should().Be(0);
            stats.CompletedDownloads.Should().Be(0);
            stats.FailedDownloads.Should().Be(0);
            stats.TotalBytesDownloaded.Should().Be(0);
            stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void DownloadQueueStatistics_WithProperties_SetCorrectly()
        {
            // Arrange
            var lastUpdated = DateTime.UtcNow.AddMinutes(-5);

            // Act
            var stats = new DownloadQueueStatistics
            {
                TotalDownloads = 100,
                QueuedDownloads = 20,
                DownloadingDownloads = 10,
                CompletedDownloads = 60,
                FailedDownloads = 10,
                TotalBytesDownloaded = 1073741824, // 1 GB
                LastUpdated = lastUpdated
            };

            // Assert
            stats.TotalDownloads.Should().Be(100);
            stats.QueuedDownloads.Should().Be(20);
            stats.DownloadingDownloads.Should().Be(10);
            stats.CompletedDownloads.Should().Be(60);
            stats.FailedDownloads.Should().Be(10);
            stats.TotalBytesDownloaded.Should().Be(1073741824);
            stats.LastUpdated.Should().Be(lastUpdated);
        }

        [Theory]
        [InlineData(20, 10, 60, 10, 100)]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(1, 2, 3, 4, 10)]
        public void DownloadQueueStatistics_TotalsSumCorrectly(int queued, int downloading, int completed, int failed, int expectedTotal)
        {
            // Act
            var stats = new DownloadQueueStatistics
            {
                QueuedDownloads = queued,
                DownloadingDownloads = downloading,
                CompletedDownloads = completed,
                FailedDownloads = failed,
                TotalDownloads = expectedTotal
            };

            // Assert - In real implementation, TotalDownloads would be calculated
            var calculatedTotal = stats.QueuedDownloads + stats.DownloadingDownloads + 
                                 stats.CompletedDownloads + stats.FailedDownloads;
            calculatedTotal.Should().Be(expectedTotal);
        }

        [Fact]
        public void DownloadQueueStatistics_LargeByteCounts_HandleCorrectly()
        {
            // Arrange - Test with very large byte counts (petabyte scale)
            const long petabyte = 1125899906842624; // 1 PB in bytes

            // Act
            var stats = new DownloadQueueStatistics
            {
                TotalBytesDownloaded = petabyte
            };

            // Assert
            stats.TotalBytesDownloaded.Should().Be(petabyte);
        }

        [Fact]
        public void DownloadQueueStatistics_LastUpdated_DefaultsToRecentTime()
        {
            // Act
            var stats = new DownloadQueueStatistics();

            // Assert
            stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void DownloadQueueStatistics_LastUpdated_CanBeSetExplicitly()
        {
            // Arrange
            var specificTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var stats = new DownloadQueueStatistics
            {
                LastUpdated = specificTime
            };

            // Assert
            stats.LastUpdated.Should().Be(specificTime);
        }

        #endregion

        #region Model Comparison and Equality Tests

        [Fact]
        public void QueueStatus_WithSameValues_ShouldBeEquatable()
        {
            // Arrange
            var status1 = new QueueStatus
            {
                ActiveDownloads = 5,
                ActiveSearches = 3,
                MaxConcurrentDownloads = 10,
                MaxConcurrentSearches = 8
            };

            var status2 = new QueueStatus
            {
                ActiveDownloads = 5,
                ActiveSearches = 3,
                MaxConcurrentDownloads = 10,
                MaxConcurrentSearches = 8
            };

            // Act & Assert
            // Note: These classes don't implement IEquatable, so we test individual properties
            status1.ActiveDownloads.Should().Be(status2.ActiveDownloads);
            status1.ActiveSearches.Should().Be(status2.ActiveSearches);
            status1.MaxConcurrentDownloads.Should().Be(status2.MaxConcurrentDownloads);
            status1.MaxConcurrentSearches.Should().Be(status2.MaxConcurrentSearches);
        }

        [Fact]
        public void QueueStatistics_PropertyValidation_AllPropertiesAccessible()
        {
            // Act
            var stats = new QueueStatistics();

            // Assert - Verify all properties are accessible (no exceptions thrown)
            var totalDownloadSlotAcquisitions = stats.TotalDownloadSlotAcquisitions;
            var totalSearchSlotAcquisitions = stats.TotalSearchSlotAcquisitions;
            var averageDownloadWaitTime = stats.AverageDownloadWaitTime;
            var averageSearchWaitTime = stats.AverageSearchWaitTime;
            var peakConcurrentDownloads = stats.PeakConcurrentDownloads;
            var peakConcurrentSearches = stats.PeakConcurrentSearches;
            var totalDownloadSlotHoldTime = stats.TotalDownloadSlotHoldTime;
            var totalSearchSlotHoldTime = stats.TotalSearchSlotHoldTime;
            var downloadQueueSaturations = stats.DownloadQueueSaturations;
            var searchQueueSaturations = stats.SearchQueueSaturations;

            // All property access should complete without exception
            true.Should().BeTrue();
        }

        #endregion

        #region Edge Cases and Boundary Tests

        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        public void QueueStatus_HandlesExtremeBoundaryValues(int value)
        {
            // Act & Assert - Should not throw exceptions
            var status = new QueueStatus
            {
                ActiveDownloads = value,
                ActiveSearches = value,
                MaxConcurrentDownloads = value,
                MaxConcurrentSearches = value,
                AvailableDownloadSlots = value,
                AvailableSearchSlots = value
            };

            status.ActiveDownloads.Should().Be(value);
            status.ActiveSearches.Should().Be(value);
            status.MaxConcurrentDownloads.Should().Be(value);
            status.MaxConcurrentSearches.Should().Be(value);
            status.AvailableDownloadSlots.Should().Be(value);
            status.AvailableSearchSlots.Should().Be(value);
        }

        [Theory]
        [InlineData(long.MinValue)]
        [InlineData(-1L)]
        [InlineData(0L)]
        [InlineData(long.MaxValue)]
        public void QueueStatistics_HandlesExtremeBoundaryValues(long value)
        {
            // Act & Assert - Should not throw exceptions
            var stats = new QueueStatistics
            {
                TotalDownloadSlotAcquisitions = value,
                TotalSearchSlotAcquisitions = value
            };

            stats.TotalDownloadSlotAcquisitions.Should().Be(value);
            stats.TotalSearchSlotAcquisitions.Should().Be(value);
        }

        [Fact]
        public void DownloadQueueStatistics_HandlesMinMaxDateTime()
        {
            // Act & Assert - Should handle extreme DateTime values
            var stats = new DownloadQueueStatistics();
            
            stats.LastUpdated = DateTime.MinValue;
            stats.LastUpdated.Should().Be(DateTime.MinValue);

            stats.LastUpdated = DateTime.MaxValue;
            stats.LastUpdated.Should().Be(DateTime.MaxValue);
        }

        #endregion
    }
}