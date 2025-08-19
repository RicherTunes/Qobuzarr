using System;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Models
{
    public class QobuzTrackTests
    {
        [Fact]
        public void Deserialize_WithValidJson_ShouldParseCorrectly()
        {
            // Arrange
            var albumJson = SampleQobuzResponses.SampleAlbumResponse;
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);

            // Act
            var track = album.GetTracks()[0];

            // Assert
            track.Should().NotBeNull();
            track.Id.Should().Be("23374053");
            track.Title.Should().Be("Give Life Back to Music");
            track.TrackNumber.Should().Be(1);
            track.DiscNumber.Should().Be(1);
            track.Duration.TotalSeconds.Should().BeApproximately(274, 1);
            track.ISRC.Should().Be("USSM11300001");
            track.Streamable.Should().BeTrue();
        }

        [Fact]
        public void GetFullTitle_WithVersionSuffix_ShouldIncludeVersion()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Title = "Test Song",
                Version = "Remix"
            };

            // Act
            var fullTitle = track.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Test Song (Remix)");
        }

        [Fact]
        public void GetFullTitle_WithoutVersion_ShouldReturnTitleOnly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Title = "Test Song",
                Version = null
            };

            // Act
            var fullTitle = track.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Test Song");
        }

        [Fact]
        public void GetSafeFileName_WithSpecialCharacters_ShouldSanitize()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Title = "Test: Song? <Name> \"Special\"",
                TrackNumber = 1
            };

            // Act
            var safeFileName = track.GetSafeFileName();

            // Assert
            safeFileName.Should().Be("01 - Test Song Name Special.flac");
            safeFileName.Should().NotContain(":");
            safeFileName.Should().NotContain("?");
            safeFileName.Should().NotContain("<");
            safeFileName.Should().NotContain(">");
            safeFileName.Should().NotContain("\"");
        }

        [Fact]
        public void GetSafeFileName_WithDoubleDigitTrackNumber_ShouldPadCorrectly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Title = "Test Song",
                TrackNumber = 15
            };

            // Act
            var safeFileName = track.GetSafeFileName("mp3");

            // Assert
            safeFileName.Should().Be("15 - Test Song.mp3");
        }

        [Fact]
        public void GetEstimatedFileSize_WithMP3Quality_ShouldCalculateCorrectly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                DurationSeconds = 240 // 4 minutes
            };

            // Act
            var size = track.GetEstimatedFileSize(5); // MP3 320kbps

            // Assert
            // 240 seconds * 320kbps / 8 (bits to bytes)
            var expectedSize = (long)(240 * 320 * 1000 / 8);
            size.Should().BeCloseTo(expectedSize, (ulong)(expectedSize * 0.1)); // 10% tolerance
        }

        [Fact]
        public void GetEstimatedFileSize_WithFLACQuality_ShouldCalculateCorrectly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                DurationSeconds = 180 // 3 minutes
            };

            // Act
            var size = track.GetEstimatedFileSize(6); // FLAC CD

            // Assert
            // FLAC typically 800-1000kbps for CD quality
            var expectedSize = (long)(180 * 900 * 1000 / 8);
            size.Should().BeCloseTo(expectedSize, (ulong)(expectedSize * 0.2)); // 20% tolerance for FLAC
        }

        [Theory]
        [InlineData("flac")]
        [InlineData("mp3")]
        [InlineData("wav")]
        public void GetSafeFileName_WithDifferentExtensions_ShouldUseCorrectExtension(string extension)
        {
            // Arrange
            var track = new QobuzTrack
            {
                Title = "Test Song",
                TrackNumber = 5
            };

            // Act
            var fileName = track.GetSafeFileName(extension);

            // Assert
            fileName.Should().EndWith($".{extension}");
            fileName.Should().StartWith("05 - Test Song");
        }

        [Fact]
        public void GetDurationString_WithValidDuration_ShouldFormatCorrectly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                DurationSeconds = 274 // 4:34
            };

            // Act
            var durationString = track.Duration.ToString(@"m\:ss");

            // Assert
            durationString.Should().Be("4:34");
        }

        [Fact]
        public void GetDurationString_WithZeroDuration_ShouldReturnDefault()
        {
            // Arrange
            var track = new QobuzTrack
            {
                DurationSeconds = 0
            };

            // Act
            var durationString = track.Duration.ToString(@"m\:ss");

            // Assert
            durationString.Should().Be("0:00");
        }

        [Fact]
        public void GetDurationString_WithLongDuration_ShouldFormatCorrectly()
        {
            // Arrange
            var track = new QobuzTrack
            {
                DurationSeconds = 3661 // 1:01:01
            };

            // Act
            var durationString = track.Duration.ToString(@"m\:ss");

            // Assert
            durationString.Should().Be("61:01"); // Minutes format, not hours
        }

        [Fact]
        public void GetQualityInfo_WithValidTrack_ShouldReturnQualityData()
        {
            // Arrange
            var albumJson = SampleQobuzResponses.SampleAlbumResponse;
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
            var track = album.GetTracks()[0];

            // Act & Assert
            track.MaximumBitDepth.Should().Be(16);
            track.MaximumSampleRate.Should().Be(44.1);
            track.MaximumChannelCount.Should().Be(2);
        }

        [Theory]
        [InlineData("USSM11300001", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void HasValidISRC_ShouldValidateISRCFormat(string isrc, bool expectedValid)
        {
            // Arrange
            var track = new QobuzTrack
            {
                ISRC = isrc
            };

            // Act
            var hasValidISRC = !string.IsNullOrWhiteSpace(track.ISRC);

            // Assert
            hasValidISRC.Should().Be(expectedValid);
        }

        [Fact]
        public void IsValid_WithCompleteTrack_ShouldReturnTrue()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Id = "12345",
                Title = "Test Song",
                TrackNumber = 1,
                DurationSeconds = 180
            };

            // Act
            var isValid = !string.IsNullOrWhiteSpace(track.Id) && 
                         !string.IsNullOrWhiteSpace(track.Title) && 
                         track.TrackNumber > 0 && 
                         track.Duration.TotalSeconds > 0;

            // Assert
            isValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(null, "Test Song", 1, 180, false)] // Invalid ID
        [InlineData("123", "", 1, 180, false)] // Empty title
        [InlineData("123", "Test Song", 0, 180, false)] // Invalid track number
        [InlineData("123", "Test Song", 1, 0, false)] // Invalid duration
        public void IsValid_WithIncompleteTrack_ShouldReturnFalse(string id, string title, int trackNumber, int duration, bool expectedValid)
        {
            // Arrange
            var track = new QobuzTrack
            {
                Id = id,
                Title = title,
                TrackNumber = trackNumber,
                DurationSeconds = duration
            };

            // Act
            var isValid = !string.IsNullOrWhiteSpace(track.Id) && 
                         !string.IsNullOrWhiteSpace(track.Title) && 
                         track.TrackNumber > 0 && 
                         track.Duration.TotalSeconds > 0;

            // Assert
            isValid.Should().Be(expectedValid);
        }

        [Fact]
        public void GetPerformerName_WithValidPerformer_ShouldReturnName()
        {
            // Arrange
            var albumJson = SampleQobuzResponses.SampleAlbumResponse;
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
            var track = album.GetTracks()[0];

            // Act
            var performerName = track.Performer?.Name;

            // Assert
            performerName.Should().Be("Daft Punk");
        }

        [Fact]
        public void GetPerformerName_WithNullPerformer_ShouldReturnNull()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Performer = null
            };

            // Act
            var performerName = track.Performer?.Name;

            // Assert
            performerName.Should().BeNull();
        }
    }
}