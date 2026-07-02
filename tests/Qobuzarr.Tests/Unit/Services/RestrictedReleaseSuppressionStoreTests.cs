using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Services;
using Xunit;

namespace Qobuzarr.Tests.Unit.Services
{
    public sealed class RestrictedReleaseSuppressionStoreTests : IDisposable
    {
        private readonly string _tempDir;

        public RestrictedReleaseSuppressionStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "qobuzarr-suppression-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private string NewFilePath([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
            => Path.Combine(_tempDir, testName + ".json");

        [Theory]
        [InlineData(TrackUnavailableReason.Restricted)]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction)]
        public async Task SuppressAsync_PermanentReason_SuppressesAndPersistsViaCommonStore(TrackUnavailableReason reason)
        {
            var path = NewFilePath();
            var first = new RestrictedReleaseSuppressionStore(path);

            await first.SuppressAsync("  ALBUM-123  ", "track-9", reason);

            var second = new RestrictedReleaseSuppressionStore(path);
            second.IsSuppressed("album-123").Should().BeTrue(
                "qobuz should delegate durable, normalized persistence to Common for terminal restrictions");
        }

        [Theory]
        [InlineData(TrackUnavailableReason.RegionalRestriction)]
        [InlineData(TrackUnavailableReason.PreviewOnly)]
        [InlineData(TrackUnavailableReason.NoQualityAvailable)]
        [InlineData(TrackUnavailableReason.NotStreamable)]
        [InlineData(TrackUnavailableReason.ApiError)]
        [InlineData(TrackUnavailableReason.Unknown)]
        public async Task SuppressAsync_NonPermanentReason_DoesNotSuppress(TrackUnavailableReason reason)
        {
            var sut = new RestrictedReleaseSuppressionStore(NewFilePath());

            await sut.SuppressAsync("album-soft", "track-9", reason);

            sut.IsSuppressed("album-soft").Should().BeFalse(
                "only purchase-only and subscription-tier restrictions are precise enough to hide a release");
        }

        [Fact]
        public async Task SuppressAsync_NullOrWhitespaceAlbumId_IsNoOp()
        {
            var sut = new RestrictedReleaseSuppressionStore(NewFilePath());

            await sut.SuppressAsync("", "track-9", TrackUnavailableReason.Restricted);
            await sut.SuppressAsync(null!, "track-9", TrackUnavailableReason.Restricted);

            sut.IsSuppressed("").Should().BeFalse();
            sut.Count.Should().Be(0);
        }

        [Fact]
        public async Task ClearAsync_SuppressedAlbum_RemovesSuppressionViaCommonStore()
        {
            var sut = new RestrictedReleaseSuppressionStore(NewFilePath());
            await sut.SuppressAsync("album-clear", "track-9", TrackUnavailableReason.Restricted);

            var removed = await sut.ClearAsync("album-clear");

            removed.Should().BeTrue();
            sut.IsSuppressed("album-clear").Should().BeFalse(
                "terminal release suppression must have an explicit clear path for operators and future tooling");
        }
    }
}
