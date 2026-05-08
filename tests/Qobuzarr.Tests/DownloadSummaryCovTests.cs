using System.Threading;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for DownloadSummary (src/Download/DownloadSummary.cs).
    /// Wave 12 baseline: 0/103 lines covered.
    /// </summary>
    public class DownloadSummaryCovTests
    {
        [Fact]
        public void NewSummary_HasZeroAlbumsAndBytes()
        {
            var s = new DownloadSummary();
            s.GetTotalAlbums().Should().Be(0);
            s.GetTotalBytesDownloaded().Should().Be(0);
            s.GetAverageSpeed().Should().Be(0);
        }

        [Fact]
        public void RecordAlbumResult_AccumulatesAlbumsAndBytes()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("Artist", "Album1", 10, 0, 0, 10, 5_000_000);
            s.RecordAlbumResult("Artist2", "Album2", 5, 1, 1, 7, 3_000_000);

            s.GetTotalAlbums().Should().Be(2);
            s.GetTotalBytesDownloaded().Should().Be(8_000_000);
        }

        [Fact]
        public void RecordSpeed_FeedsAverageSpeed()
        {
            var s = new DownloadSummary();
            s.RecordSpeed(1000);
            s.RecordSpeed(3000);
            s.GetAverageSpeed().Should().Be(2000);
        }

        [Fact]
        public void GetAverageSpeed_NoSpeeds_FallsBackToBytesOverElapsed()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 1, 0, 0, 1, 1_000_000);
            // Sleep briefly so elapsed > 0
            Thread.Sleep(20);
            s.GetAverageSpeed().Should().BeGreaterThan(0);
        }

        [Fact]
        public void Reset_ClearsAllAccumulatedState()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 1, 0, 0, 1, 100);
            s.RecordSpeed(500);

            s.Reset();

            s.GetTotalAlbums().Should().Be(0);
            s.GetTotalBytesDownloaded().Should().Be(0);
            s.GetAverageSpeed().Should().Be(0);
        }

        [Fact]
        public void GenerateReport_EmptySummary_ProducesHeaderAndZeroes()
        {
            var s = new DownloadSummary();
            var report = s.GenerateReport();

            report.Should().Contain("Download Summary");
            report.Should().Contain("Completed: 0 albums");
            report.Should().Contain("Track Statistics");
            report.Should().Contain("Downloaded: 0 tracks");
        }

        [Fact]
        public void GenerateReport_AllSuccessful_ShowsCompletedAndSuccessRate()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 10, 0, 0, 10, 1024);

            var report = s.GenerateReport();
            report.Should().Contain("Completed: 1 album ");
            report.Should().Contain("Success rate: 100.0%");
            report.Should().NotContain("Issues encountered");
        }

        [Fact]
        public void GenerateReport_PartialAlbum_ShowsPartialAndIssues()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 5, 0, 5, 10, 1024);

            var report = s.GenerateReport();
            report.Should().Contain("Partial: 1 album");
            report.Should().Contain("Issues encountered");
            report.Should().Contain("A - B");
            report.Should().Contain("Failed: 5/10 tracks");
        }

        [Fact]
        public void GenerateReport_FullyFailedAlbum_ShowsFailedSection()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 0, 0, 5, 5, 0);

            var report = s.GenerateReport();
            report.Should().Contain("Failed: 1 album");
        }

        [Fact]
        public void GenerateReport_SkippedTracks_AppearsInTrackStatistics()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 4, 6, 0, 10, 100);

            var report = s.GenerateReport();
            report.Should().Contain("Skipped: 6 tracks");
        }

        [Fact]
        public void GenerateReport_WithSpeedMeasurements_ShowsAvgAndPeak()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 1, 0, 0, 1, 1024);
            s.RecordSpeed(1_000_000);
            s.RecordSpeed(2_000_000);

            var report = s.GenerateReport();
            report.Should().Contain("Avg speed");
            report.Should().Contain("Peak speed");
        }

        [Fact]
        public void GenerateReport_MoreThanFiveProblemAlbums_ShowsTruncationLine()
        {
            var s = new DownloadSummary();
            for (var i = 0; i < 7; i++)
            {
                s.RecordAlbumResult($"A{i}", $"B{i}", 1, 0, 1, 2, 100);
            }

            var report = s.GenerateReport();
            report.Should().Contain("... and 2 more album");
        }

        [Fact]
        public void GenerateReport_FormatsBytesAcrossMagnitudes()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 1, 0, 0, 1, 500); // bytes
            var report1 = s.GenerateReport();
            report1.Should().Contain("500 B");

            var s2 = new DownloadSummary();
            s2.RecordAlbumResult("A", "B", 1, 0, 0, 1, 5_000); // KB
            s2.GenerateReport().Should().Contain("KB");

            var s3 = new DownloadSummary();
            s3.RecordAlbumResult("A", "B", 1, 0, 0, 1, 5_000_000); // MB
            s3.GenerateReport().Should().Contain("MB");

            var s4 = new DownloadSummary();
            s4.RecordAlbumResult("A", "B", 1, 0, 0, 1, 5_000_000_000); // GB
            s4.GenerateReport().Should().Contain("GB");
        }

        [Fact]
        public void GetBriefSummary_ContainsRatioAndDuration()
        {
            var s = new DownloadSummary();
            s.RecordAlbumResult("A", "B", 5, 0, 0, 5, 2048);
            s.RecordAlbumResult("C", "D", 0, 0, 3, 3, 0);

            var brief = s.GetBriefSummary();
            brief.Should().Contain("Downloaded 1/2 albums");
            brief.Should().Contain("KB"); // 2048 bytes -> 2.0 KB
        }
    }
}
