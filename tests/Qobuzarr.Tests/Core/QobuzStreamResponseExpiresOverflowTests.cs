using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Core
{
    /// <summary>
    /// QobuzStreamResponse.ExpiresAt converts ExpiresTimestamp (a long?) via
    /// DateTimeOffset.FromUnixTimeSeconds, guarded only by `> 0`. FromUnixTimeSeconds throws
    /// ArgumentOutOfRangeException past DateTimeOffset.MaxValue (~year 9999, 253402300799 unix-seconds),
    /// and a long can carry far larger values — so an out-of-range token-expiry timestamp threw straight
    /// out of this getter on the auth path. Same epoch-overflow CLASS as QobuzAlbum.ReleaseDate (bb9e693)
    /// and amazon AmazonMusicLookupResponse (b3de7c0). Out-of-range must yield null, never throw.
    /// </summary>
    public class QobuzStreamResponseExpiresOverflowTests
    {
        [Fact]
        public void ExpiresAt_TimestampBeyondDateRange_DoesNotThrow_ReturnsNull()
        {
            var response = new QobuzStreamResponse
            {
                ExpiresTimestamp = 9_999_999_999_999_999, // fits int64, far past FromUnixTimeSeconds' range
            };

            response.ExpiresAt.Should().BeNull(
                "an un-representable expiry epoch must yield null, never throw out of the getter");
        }

        [Fact]
        public void ExpiresAt_TimestampInRange_ReturnsExpectedUtc()
        {
            var response = new QobuzStreamResponse
            {
                ExpiresTimestamp = 1_620_000_000, // 2021-05-03T00:00:00Z
            };

            response.ExpiresAt.Should().NotBeNull();
            response.ExpiresAt!.Value.Year.Should().Be(2021, "an in-range epoch still converts normally");
        }
    }
}
