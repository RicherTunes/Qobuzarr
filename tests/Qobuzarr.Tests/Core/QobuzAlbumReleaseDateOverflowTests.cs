using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Core
{
    /// <summary>
    /// QobuzAlbum.ReleaseDate converts the API's ReleasedAtTimestamp (a long) via
    /// DateTimeOffset.FromUnixTimeSeconds, guarded only by `> 0`. FromUnixTimeSeconds throws
    /// ArgumentOutOfRangeException for values past DateTimeOffset.MaxValue (~year 9999,
    /// 253402300799 unix-seconds), and a long can carry far larger values. An out-of-range
    /// timestamp must NOT throw out of a property getter — it should fall through to the ISO
    /// release-date strings (or DateTime.MinValue). Same bug CLASS as the amazon
    /// AmazonMusicLookupResponse epoch/duration overflow fix.
    /// </summary>
    public class QobuzAlbumReleaseDateOverflowTests
    {
        // 9_999_999_999_999_999 fits in int64 but is far past FromUnixTimeSeconds' valid range.
        private const long OutOfRangeUnixSeconds = 9_999_999_999_999_999;

        [Fact]
        public void ReleaseDate_TimestampBeyondDateRange_DoesNotThrow_FallsBackToIsoString()
        {
            var album = new QobuzAlbum
            {
                ReleasedAtTimestamp = OutOfRangeUnixSeconds,
                ReleaseDateOriginal = "2021-05-14",
            };

            album.ReleaseDate.Year.Should().Be(2021,
                "an un-representable epoch must fall through to the ISO release-date string, not throw");
        }

        [Fact]
        public void ReleaseDate_TimestampBeyondDateRange_NoStringFallback_ReturnsMinValue()
        {
            var album = new QobuzAlbum
            {
                ReleasedAtTimestamp = OutOfRangeUnixSeconds,
            };

            album.ReleaseDate.Should().Be(DateTime.MinValue,
                "with no parseable string fallback an un-representable epoch yields MinValue, never throws");
        }
    }
}
