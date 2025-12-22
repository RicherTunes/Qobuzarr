using FluentAssertions;
using NzbDrone.Core.Parser.Model;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Tests for AlbumIdExtractor - the public API for extracting Qobuz album IDs.
    /// Replaces reflection-based tests that were in QobuzDownloadClientTests.
    /// </summary>
    public class AlbumIdExtractorTests
    {

        #region DownloadUrl Extraction

        [Theory]
        [InlineData("qobuz://album/0060254788359", "0060254788359")]
        [InlineData("qobuz://album/1234567890123", "1234567890123")]
        [InlineData("qobuz://album/abc123def456", "abc123def456")]
        public void ExtractAlbumId_WithValidQobuzUrl_ShouldReturnAlbumId(string url, string expectedId)
        {
            // Arrange
            var release = new ReleaseInfo { DownloadUrl = url };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().Be(expectedId);
        }

        [Theory]
        [InlineData("qobuz://album/0060254788359/5", "0060254788359")]
        [InlineData("qobuz://album/1234567890123/27", "1234567890123")]
        [InlineData("qobuz://album/abc123/6", "abc123")]
        public void ExtractAlbumId_WithQobuzUrlAndQuality_ShouldReturnAlbumIdWithoutQuality(
            string url, string expectedId)
        {
            // Arrange
            var release = new ReleaseInfo { DownloadUrl = url };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().Be(expectedId);
        }

        #endregion

        #region GUID Extraction

        [Theory]
        [InlineData("qobuz-0060254788359", "0060254788359")]
        [InlineData("qobuz-1234567890123", "1234567890123")]
        public void ExtractAlbumId_WithValidGuid_ShouldReturnAlbumId(string releaseGuid, string expectedId)
        {
            // Arrange
            var release = new ReleaseInfo { Guid = releaseGuid };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().Be(expectedId);
        }

        [Theory]
        [InlineData("qobuz-0060254788359-5", "0060254788359")]
        [InlineData("qobuz-1234567890123-27", "1234567890123")]
        public void ExtractAlbumId_WithGuidAndQuality_ShouldReturnAlbumIdWithoutQuality(
            string releaseGuid, string expectedId)
        {
            // Arrange
            var release = new ReleaseInfo { Guid = releaseGuid };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().Be(expectedId);
        }

        #endregion

        #region Priority: DownloadUrl over GUID

        [Fact]
        public void ExtractAlbumId_WithBothUrlAndGuid_ShouldPreferUrl()
        {
            // Arrange
            var release = new ReleaseInfo
            {
                DownloadUrl = "qobuz://album/url-album-id",
                Guid = "qobuz-guid-album-id"
            };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().Be("url-album-id");
        }

        #endregion

        #region Invalid/Missing Data

        [Fact]
        public void ExtractAlbumId_WithNullRelease_ShouldReturnNull()
        {
            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(null);

            // Assert
            albumId.Should().BeNull();
        }

        [Fact]
        public void ExtractAlbumId_WithEmptyRelease_ShouldReturnNull()
        {
            // Arrange
            var release = new ReleaseInfo();

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().BeNull();
        }

        [Theory]
        [InlineData("https://www.qobuz.com/album/1234")]
        [InlineData("http://example.com/download")]
        [InlineData("invalid://url")]
        [InlineData("")]
        public void ExtractAlbumId_WithInvalidUrl_ShouldReturnNull(string url)
        {
            // Arrange
            var release = new ReleaseInfo { DownloadUrl = url };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().BeNull();
        }

        [Theory]
        [InlineData("some-random-guid")]
        [InlineData("1234567890")]
        [InlineData("")]
        public void ExtractAlbumId_WithInvalidGuid_ShouldReturnNull(string releaseGuid)
        {
            // Arrange
            var release = new ReleaseInfo { Guid = releaseGuid };

            // Act
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);

            // Assert
            albumId.Should().BeNull();
        }

        #endregion
    }
}
