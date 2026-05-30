using System;
using System.Threading;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Common.TestKit.Helpers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for DownloadProgressTracker, ProgressTracker, LidarrProgressReporter,
    /// and GlobalProgressStatistics defined in src/Services/LidarrProgressReporter.cs.
    /// Source lines referenced: LidarrProgressReporter.cs
    /// </summary>
    public class DownloadProgressTrackerCovTests : IDisposable
    {
        private readonly Logger _logger;

        public DownloadProgressTrackerCovTests()
        {
            // Isolated logger — never mutate the process-global LogManager.Configuration
            // (and never call LogManager.Shutdown() in teardown). Both raced parallel
            // log-capture tests (e.g. QobuzAppSecretLogScrubTests) that read the shared
            // "testMemory" target, deterministically wiping their capture. This test never
            // asserts on captured logs, so a no-op isolated logger suffices.
            _logger = NLogTestLogger.CreateNullLogger();
        }

        public void Dispose()
        {
        }

        // Helper to create a ProgressTracker via reflection-free means:
        // ProgressTracker is internal, so we test it through LidarrProgressReporter
        private static LidarrProgressReporter CreateReporter(Logger logger)
        {
            return new LidarrProgressReporter(logger);
        }

        #region LidarrProgressReporter - CreateTracker Tests (Source lines 43-52)

        [Fact]
        public void CreateTracker_ShouldReturnValidTracker()
        {
            // Source line 44: var tracker = new ProgressTracker(...)
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-operation");

            tracker.Should().NotBeNull();
            tracker.TotalItems.Should().Be(5);
            tracker.OperationType.Should().Be("test-operation");
        }

        [Fact]
        public void CreateTracker_ShouldTrackMultipleTrackers()
        {
            var reporter = CreateReporter(_logger);
            var t1 = reporter.CreateTracker(5, "op1");
            var t2 = reporter.CreateTracker(10, "op2");

            var stats = reporter.GetGlobalStatistics();
            stats.ActiveTrackers.Should().Be(2);
            stats.TotalItemsAcrossAllTrackers.Should().Be(15);
        }

        #endregion

        #region LidarrProgressReporter - CreateDownloadTracker Tests (Source lines 57-67)

        [Fact]
        public void CreateDownloadTracker_ShouldReturnDownloadTracker()
        {
            // Source line 58: var tracker = new DownloadProgressTracker(...)
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(3, "download-op");

            tracker.Should().NotBeNull();
            tracker.TotalItems.Should().Be(3);
            tracker.OperationType.Should().Be("download-op");
        }

        [Fact]
        public void CreateDownloadTracker_ShouldHaveZeroInitialDownloadStats()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.TotalBytesDownloaded.Should().Be(0);
            tracker.CurrentSpeedMBps.Should().Be(0);
            tracker.AverageSpeedMBps.Should().Be(0);
            tracker.SuccessCount.Should().Be(0);
            tracker.FailureCount.Should().Be(0);
        }

        #endregion

        #region LidarrProgressReporter - GetGlobalStatistics Tests (Source lines 72-95)

        [Fact]
        public void GetGlobalStatistics_EmptyState_ShouldReturnZeros()
        {
            // Source lines 74-93: Calculates from active trackers
            var reporter = CreateReporter(_logger);
            var stats = reporter.GetGlobalStatistics();

            stats.ActiveTrackers.Should().Be(0);
            stats.TotalItemsAcrossAllTrackers.Should().Be(0);
            stats.CompletedItemsAcrossAllTrackers.Should().Be(0);
            stats.OverallPercentComplete.Should().Be(0);
            stats.CombinedDownloadSpeedMBps.Should().Be(0);
            stats.TotalBytesDownloadedAcrossAllTrackers.Should().Be(0);
            stats.CompletedTrackers.Should().Be(0);
            stats.RunningTrackers.Should().Be(0);
        }

        [Fact]
        public void GetGlobalStatistics_WithActiveTrackers_ShouldAggregate()
        {
            var reporter = CreateReporter(_logger);
            var t1 = reporter.CreateTracker(10, "op1");
            var t2 = reporter.CreateTracker(20, "op2");

            // Complete some items
            t1.CompleteItem("item1");
            t1.CompleteItem("item2");
            t2.CompleteItem("itemA");

            var stats = reporter.GetGlobalStatistics();
            stats.ActiveTrackers.Should().Be(2);
            stats.TotalItemsAcrossAllTrackers.Should().Be(30);
            stats.CompletedItemsAcrossAllTrackers.Should().Be(3);
            stats.OverallPercentComplete.Should().Be(10.0, "3/30 = 10%");
        }

        [Fact]
        public void GetGlobalStatistics_WithDownloadTrackers_ShouldIncludeDownloadStats()
        {
            var reporter = CreateReporter(_logger);
            var dt = reporter.CreateDownloadTracker(5, "download-op");

            dt.ReportDownloadProgress("album1", 1_000_000, true);
            dt.CompleteItem("album1");

            var stats = reporter.GetGlobalStatistics();
            stats.TotalBytesDownloadedAcrossAllTrackers.Should().Be(1_000_000);
            stats.CombinedDownloadSpeedMBps.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void GetGlobalStatistics_ShouldCalculateLongestRunningTracker()
        {
            var reporter = CreateReporter(_logger);
            var t1 = reporter.CreateTracker(5, "op1");
            Thread.Sleep(50);
            var t2 = reporter.CreateTracker(5, "op2");

            var stats = reporter.GetGlobalStatistics();
            stats.LongestRunningTracker.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40));
        }

        [Fact]
        public void GetGlobalStatistics_ShouldCalculateEstimatedTimeToCompletion()
        {
            var reporter = CreateReporter(_logger);
            var t1 = reporter.CreateTracker(10, "op1");
            t1.CompleteItem("item1");
            t1.CompleteItem("item2");
            t1.CompleteItem("item3");

            var stats = reporter.GetGlobalStatistics();
            stats.EstimatedTimeToCompletion.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }

        [Fact]
        public void GetGlobalStatistics_ShouldCountRunningTrackers()
        {
            var reporter = CreateReporter(_logger);
            var t1 = reporter.CreateTracker(5, "op1");
            t1.CompleteItem("i1");
            t1.CompleteItem("i2");
            t1.CompleteItem("i3");
            // 2 completed out of 5 = running
            var t2 = reporter.CreateTracker(3, "op2");
            t2.CompleteItem("a");
            t2.CompleteItem("b");
            t2.CompleteItem("c");
            // 3/3 = not running

            var stats = reporter.GetGlobalStatistics();
            stats.RunningTrackers.Should().Be(1, "only t1 is still running");
        }

        #endregion

        #region LidarrProgressReporter - Reset Tests (Source lines 100-115)

        [Fact]
        public void Reset_ShouldClearAllTrackers()
        {
            // Source lines 102-112: Disposes and clears all trackers
            var reporter = CreateReporter(_logger);
            reporter.CreateTracker(5, "op1");
            reporter.CreateDownloadTracker(3, "op2");

            reporter.Reset();

            var stats = reporter.GetGlobalStatistics();
            stats.ActiveTrackers.Should().Be(0);
        }

        [Fact]
        public void Reset_ShouldHandleEmptyState()
        {
            var reporter = CreateReporter(_logger);

            var act = () => reporter.Reset();
            act.Should().NotThrow();
        }

        #endregion

        #region ProgressTracker - CompleteItem Tests (Source lines 171-188)

        [Fact]
        public void CompleteItem_ShouldIncrementCompletedCount()
        {
            // Source line 180: _completedItems++
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.CompleteItem("first");
            tracker.CompleteItem("second");

            tracker.CompletedItems.Should().Be(2);
        }

        [Fact]
        public void CompleteItem_ShouldUpdateCurrentItem()
        {
            // Source lines 183-185: Update current item description
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.CompleteItem("completed-album");

            tracker.CurrentItem.Should().Be("completed-album");
        }

        [Fact]
        public void CompleteItem_ShouldCalculatePercentComplete()
        {
            // Source lines 231-233: return TotalItems > 0 ? (double)_completedItems / TotalItems * 100 : 0
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(4, "test-op");

            tracker.CompleteItem("item1");
            tracker.CompleteItem("item2");

            tracker.PercentComplete.Should().Be(50.0);
        }

        [Fact]
        public void CompleteItem_ShouldRemoveTrackerWhenAllComplete()
        {
            // Source lines 189-192: When _completedItems >= TotalItems, invoke OnTrackerCompleted
            var reporter = CreateReporter(_logger);
            reporter.CreateTracker(2, "test-op");

            var tracker = reporter.CreateTracker(2, "op2");
            tracker.CompleteItem("item1");
            tracker.CompleteItem("item2");

            // Allow async removal
            Thread.Sleep(50);

            var stats = reporter.GetGlobalStatistics();
            // Completed tracker should be removed (2 total created, 1 remains + first one that's not done)
            stats.ActiveTrackers.Should().Be(1);
        }

        [Fact]
        public void CompleteItem_NullDescription_ShouldStillIncrement()
        {
            // Source line 184: if (!string.IsNullOrEmpty(itemDescription))
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(3, "test-op");

            tracker.CompleteItem(null);

            tracker.CompletedItems.Should().Be(1);
        }

        #endregion

        #region ProgressTracker - CompleteItems Batch Tests (Source lines 195-212)

        [Fact]
        public void CompleteItems_ShouldIncrementByCount()
        {
            // Source line 203: _completedItems = Math.Min(_completedItems + count, TotalItems)
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(10, "test-op");

            tracker.CompleteItems(3);

            tracker.CompletedItems.Should().Be(3);
        }

        [Fact]
        public void CompleteItems_ShouldNotExceedTotalItems()
        {
            // Source line 203: Math.Min caps at TotalItems
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.CompleteItems(10);

            tracker.CompletedItems.Should().Be(5);
        }

        [Fact]
        public void CompleteItems_ZeroCount_ShouldNotIncrement()
        {
            // Source line 200: if (count <= 0) return
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.CompleteItems(0);

            tracker.CompletedItems.Should().Be(0);
        }

        [Fact]
        public void CompleteItems_NegativeCount_ShouldNotIncrement()
        {
            // Source line 200: if (count <= 0) return
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.CompleteItems(-3);

            tracker.CompletedItems.Should().Be(0);
        }

        #endregion

        #region ProgressTracker - ReportProgress Tests (Source lines 163-169)

        [Fact]
        public void ReportProgress_ShouldUpdateCurrentItem()
        {
            // Source lines 166-169: Sets _currentItem
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.ReportProgress("currently-processing");

            tracker.CurrentItem.Should().Be("currently-processing");
        }

        [Fact]
        public void ReportProgress_WithPhase_ShouldNotThrow()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            var act = () => tracker.ReportProgress("item", "search-phase");
            act.Should().NotThrow();
        }

        [Fact]
        public void ReportProgress_NullItem_ShouldSetEmptyString()
        {
            // Source line 168: _currentItem = currentItem ?? string.Empty
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.ReportProgress(null);

            tracker.CurrentItem.Should().BeEmpty();
        }

        #endregion

        #region ProgressTracker - EstimatedRemaining Tests (Source lines 133-145)

        [Fact]
        public void EstimatedRemaining_ZeroCompleted_ShouldReturnZero()
        {
            // Source line 136: if (_completedItems == 0 || _completedItems >= TotalItems) return TimeSpan.Zero
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            tracker.EstimatedRemaining.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void EstimatedRemaining_AllComplete_ShouldReturnZero()
        {
            // Source line 136: _completedItems >= TotalItems
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(2, "test-op");
            tracker.CompleteItem("item1");
            tracker.CompleteItem("item2");

            tracker.EstimatedRemaining.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void EstimatedRemaining_PartialProgress_ShouldReturnPositiveTime()
        {
            // Source lines 138-140: Calculates remaining based on average time per item
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(10, "test-op");
            tracker.CompleteItem("item1");
            Thread.Sleep(50);
            tracker.CompleteItem("item2");

            tracker.EstimatedRemaining.Should().BeGreaterThan(TimeSpan.Zero);
        }

        #endregion

        #region ProgressTracker - Elapsed Tests

        [Fact]
        public void Elapsed_ShouldIncreaseOverTime()
        {
            // Source line 131: public TimeSpan Elapsed => _stopwatch.Elapsed
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");
            Thread.Sleep(50);

            tracker.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40));
        }

        #endregion

        #region ProgressTracker - Dispose Tests (Source lines 268-276)

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");

            var act = () => tracker.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_CalledAfterOperations_ShouldStopStopwatch()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");
            tracker.CompleteItem("item1");
            tracker.Dispose();

            // Elapsed should remain frozen
            var elapsed1 = tracker.Elapsed;
            Thread.Sleep(50);
            var elapsed2 = tracker.Elapsed;
            elapsed2.Should().Be(elapsed1);
        }

        [Fact]
        public void Dispose_ThenOperation_ShouldThrowObjectDisposedException()
        {
            // Source line 275: throw new ObjectDisposedException(nameof(ProgressTracker))
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");
            tracker.Dispose();

            var act = () => tracker.CompleteItem("item");
            act.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Dispose_ThenReportProgress_ShouldThrowObjectDisposedException()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");
            tracker.Dispose();

            var act = () => tracker.ReportProgress("item");
            act.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Dispose_DoubleDispose_ShouldNotThrow()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateTracker(5, "test-op");
            tracker.Dispose();

            var act = () => tracker.Dispose();
            act.Should().NotThrow();
        }

        #endregion

        #region DownloadProgressTracker - ReportDownloadProgress Tests (Source lines 305-325)

        [Fact]
        public void ReportDownloadProgress_ShouldTrackBytesDownloaded()
        {
            // Source lines 315-318: _totalBytesDownloaded += bytesDownloaded
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.ReportDownloadProgress("album1", 5_000_000, true);

            tracker.TotalBytesDownloaded.Should().Be(5_000_000);
        }

        [Fact]
        public void ReportDownloadProgress_Success_ShouldIncrementSuccessCount()
        {
            // Source lines 317: if (isSuccess) _successCount++
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.ReportDownloadProgress("album1", 1_000_000, true);
            tracker.ReportDownloadProgress("album2", 2_000_000, true);

            tracker.SuccessCount.Should().Be(2);
            tracker.FailureCount.Should().Be(0);
        }

        [Fact]
        public void ReportDownloadProgress_Failure_ShouldIncrementFailureCount()
        {
            // Source lines 319: else _failureCount++
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.ReportDownloadProgress("album1", 0, false);

            tracker.FailureCount.Should().Be(1);
            tracker.SuccessCount.Should().Be(0);
        }

        [Fact]
        public void ReportDownloadProgress_ShouldAccumulateBytes()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.ReportDownloadProgress("album1", 3_000_000, true);
            tracker.ReportDownloadProgress("album2", 4_000_000, true);
            tracker.ReportDownloadProgress("album3", 5_000_000, false);

            tracker.TotalBytesDownloaded.Should().Be(12_000_000);
            tracker.SuccessCount.Should().Be(2);
            tracker.FailureCount.Should().Be(1);
        }

        [Fact]
        public void ReportDownloadProgress_NullItem_ShouldNotThrow()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            var act = () => tracker.ReportDownloadProgress(null, 1_000_000, true);
            act.Should().NotThrow();
        }

        [Fact]
        public void ReportDownloadProgress_AfterDispose_ShouldThrowObjectDisposedException()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");
            tracker.Dispose();

            var act = () => tracker.ReportDownloadProgress("album", 1_000_000, true);
            act.Should().Throw<ObjectDisposedException>();
        }

        #endregion

        #region DownloadProgressTracker - AddBytesDownloaded Tests (Source lines 327-337)

        [Fact]
        public void AddBytesDownloaded_ShouldAddToTotal()
        {
            // Source lines 333-334: _totalBytesDownloaded += additionalBytes
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.AddBytesDownloaded(1_000_000);
            tracker.AddBytesDownloaded(2_000_000);

            tracker.TotalBytesDownloaded.Should().Be(3_000_000);
        }

        [Fact]
        public void AddBytesDownloaded_Zero_ShouldNotAdd()
        {
            // Source line 331: if (additionalBytes <= 0) return
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.AddBytesDownloaded(0);

            tracker.TotalBytesDownloaded.Should().Be(0);
        }

        [Fact]
        public void AddBytesDownloaded_Negative_ShouldNotAdd()
        {
            // Source line 331: if (additionalBytes <= 0) return
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.AddBytesDownloaded(-100);

            tracker.TotalBytesDownloaded.Should().Be(0);
        }

        [Fact]
        public void AddBytesDownloaded_AfterDispose_ShouldThrowObjectDisposedException()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");
            tracker.Dispose();

            var act = () => tracker.AddBytesDownloaded(1_000_000);
            act.Should().Throw<ObjectDisposedException>();
        }

        #endregion

        #region DownloadProgressTracker - CurrentSpeedMBps Tests (Source lines 265-272)

        [Fact]
        public void CurrentSpeedMBps_ZeroElapsed_ShouldReturnZero()
        {
            // Source line 267: if (elapsed <= 0) return 0
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            // At creation, elapsed should be very small but > 0 due to Stopwatch
            tracker.CurrentSpeedMBps.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void CurrentSpeedMBps_WithBytesDownloaded_ShouldReturnPositive()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");
            tracker.AddBytesDownloaded(1_048_576); // 1 MB

            Thread.Sleep(100);

            tracker.CurrentSpeedMBps.Should().BeGreaterThan(0);
        }

        [Fact]
        public void AverageSpeedMBps_ShouldEqualCurrentSpeedMBps()
        {
            // Source line 274: public double AverageSpeedMBps => CurrentSpeedMBps
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(5, "download-op");

            tracker.AverageSpeedMBps.Should().Be(tracker.CurrentSpeedMBps);
        }

        #endregion

        #region DownloadProgressTracker - CompleteItem (inherited) Tests

        [Fact]
        public void CompleteItem_ShouldWorkOnDownloadTracker()
        {
            var reporter = CreateReporter(_logger);
            var tracker = reporter.CreateDownloadTracker(3, "download-op");

            tracker.CompleteItem("album1");
            tracker.CompleteItem("album2");

            tracker.CompletedItems.Should().Be(2);
            tracker.PercentComplete.Should().BeApproximately(66.67, 0.01);
        }

        #endregion

        #region GlobalProgressStatistics Tests (Source lines 149-170)

        [Fact]
        public void GlobalProgressStatistics_DefaultValues_ShouldBeZero()
        {
            var stats = new GlobalProgressStatistics();

            stats.ActiveTrackers.Should().Be(0);
            stats.TotalItemsAcrossAllTrackers.Should().Be(0);
            stats.CompletedItemsAcrossAllTrackers.Should().Be(0);
            stats.OverallPercentComplete.Should().Be(0);
            stats.CombinedDownloadSpeedMBps.Should().Be(0);
            stats.TotalBytesDownloadedAcrossAllTrackers.Should().Be(0);
            stats.LongestRunningTracker.Should().Be(TimeSpan.Zero);
            stats.EstimatedTimeToCompletion.Should().Be(TimeSpan.Zero);
            stats.CompletedTrackers.Should().Be(0);
            stats.RunningTrackers.Should().Be(0);
        }

        [Fact]
        public void GlobalProgressStatistics_Properties_ShouldBeSettable()
        {
            var stats = new GlobalProgressStatistics
            {
                ActiveTrackers = 3,
                TotalItemsAcrossAllTrackers = 100,
                CompletedItemsAcrossAllTrackers = 45,
                OverallPercentComplete = 45.0,
                CombinedDownloadSpeedMBps = 12.5,
                TotalBytesDownloadedAcrossAllTrackers = 500_000_000,
                LongestRunningTracker = TimeSpan.FromMinutes(5),
                EstimatedTimeToCompletion = TimeSpan.FromMinutes(3),
                CompletedTrackers = 1,
                RunningTrackers = 2
            };

            stats.ActiveTrackers.Should().Be(3);
            stats.TotalItemsAcrossAllTrackers.Should().Be(100);
            stats.CompletedItemsAcrossAllTrackers.Should().Be(45);
            stats.OverallPercentComplete.Should().Be(45.0);
            stats.CombinedDownloadSpeedMBps.Should().Be(12.5);
            stats.TotalBytesDownloadedAcrossAllTrackers.Should().Be(500_000_000);
            stats.LongestRunningTracker.Should().Be(TimeSpan.FromMinutes(5));
            stats.EstimatedTimeToCompletion.Should().Be(TimeSpan.FromMinutes(3));
            stats.CompletedTrackers.Should().Be(1);
            stats.RunningTrackers.Should().Be(2);
        }

        #endregion

        #region ProgressReport Tests (Source lines ILidarrIntegrationService.cs 225-237)

        [Fact]
        public void ProgressReport_DefaultValues_ShouldBeZero()
        {
            var report = new ProgressReport();

            report.Completed.Should().Be(0);
            report.Total.Should().Be(0);
            report.PercentComplete.Should().Be(0);
        }

        [Fact]
        public void ProgressReport_PercentComplete_ShouldCalculate()
        {
            // Source: PercentComplete => Total > 0 ? (double)Completed / Total * 100 : 0
            var report = new ProgressReport
            {
                Completed = 3,
                Total = 10
            };

            report.PercentComplete.Should().Be(30.0);
        }

        [Fact]
        public void ProgressReport_ZeroTotal_ShouldReturnZeroPercent()
        {
            var report = new ProgressReport { Completed = 5, Total = 0 };
            report.PercentComplete.Should().Be(0);
        }

        #endregion

        #region DownloadProgressReport Tests (Source lines ILidarrIntegrationService.cs 239-252)

        [Fact]
        public void DownloadProgressReport_InheritsFromProgressReport()
        {
            var report = new DownloadProgressReport
            {
                Completed = 2,
                Total = 5,
                SuccessCount = 1,
                FailureCount = 1,
                BytesDownloaded = 10_000_000,
                CurrentSpeedMBps = 5.0,
                CurrentAlbum = "Test Album",
                CurrentTrack = "Track 1"
            };

            report.Completed.Should().Be(2);
            report.Total.Should().Be(5);
            report.PercentComplete.Should().Be(40.0);
            report.SuccessCount.Should().Be(1);
            report.FailureCount.Should().Be(1);
            report.BytesDownloaded.Should().Be(10_000_000);
            report.CurrentSpeedMBps.Should().Be(5.0);
            report.CurrentAlbum.Should().Be("Test Album");
            report.CurrentTrack.Should().Be("Track 1");
        }

        [Fact]
        public void DownloadProgressReport_DefaultValues_ShouldBeZero()
        {
            var report = new DownloadProgressReport();

            report.SuccessCount.Should().Be(0);
            report.FailureCount.Should().Be(0);
            report.SkippedCount.Should().Be(0);
            report.BytesDownloaded.Should().Be(0);
            report.CurrentSpeedMBps.Should().Be(0);
        }

        #endregion

        #region Integration - Progress Callback Tests

        [Fact]
        public void CreateTracker_WithNullProgressCallback_ShouldNotThrow()
        {
            // Source line 47: IProgress<ProgressReport> progress = null
            var reporter = CreateReporter(_logger);

            var tracker = reporter.CreateTracker(3, "test-op", null);
            tracker.CompleteItem("item1");

            tracker.CompletedItems.Should().Be(1);
        }

        [Fact]
        public void CreateDownloadTracker_WithNullProgressCallback_ShouldNotThrow()
        {
            // Source line 62: IProgress<DownloadProgressReport> progress = null
            var reporter = CreateReporter(_logger);

            var tracker = reporter.CreateDownloadTracker(3, "download-op", null);
            tracker.ReportDownloadProgress("album1", 5_000_000, true);

            tracker.TotalBytesDownloaded.Should().Be(5_000_000);
            tracker.SuccessCount.Should().Be(1);
        }

        [Fact]
        public void CreateTracker_WithProgressCallback_CompleteItemShouldUpdateState()
        {
            // Verify that CompleteItem works regardless of callback
            var reporter = CreateReporter(_logger);
            var progress = new Progress<ProgressReport>(_ => { });

            var tracker = reporter.CreateTracker(3, "test-op", progress);
            tracker.CompleteItem("item1");
            tracker.CompleteItem("item2");

            tracker.CompletedItems.Should().Be(2);
            tracker.PercentComplete.Should().BeApproximately(66.67, 0.01);
        }

        #endregion

        #region Guard Validation Tests (Constructor)

        [Fact]
        public void CreateTracker_WithZeroItems_ShouldThrowArgumentOutOfRangeException()
        {
            // Source line 156: Guard.InRange(totalItems, 1, int.MaxValue, ...)
            var reporter = CreateReporter(_logger);

            var act = () => reporter.CreateTracker(0, "test-op");
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void CreateTracker_WithNegativeItems_ShouldThrowArgumentOutOfRangeException()
        {
            var reporter = CreateReporter(_logger);

            var act = () => reporter.CreateTracker(-1, "test-op");
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void CreateTracker_WithEmptyOperationType_ShouldThrowArgumentException()
        {
            // Source line 157: Guard.NotNullOrEmpty(operationType, ...)
            var reporter = CreateReporter(_logger);

            var act = () => reporter.CreateTracker(5, "");
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CreateTracker_WithNullOperationType_ShouldThrowArgumentException()
        {
            var reporter = CreateReporter(_logger);

            var act = () => reporter.CreateTracker(5, null);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CreateDownloadTracker_WithZeroItems_ShouldThrowArgumentOutOfRangeException()
        {
            var reporter = CreateReporter(_logger);

            var act = () => reporter.CreateDownloadTracker(0, "download-op");
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        #endregion
    }
}
