using System;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Models;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QobuzTrack computed properties and methods.
    /// Reference: src/Models/QobuzTrack.cs
    /// </summary>
    public class QobuzTrackCovTests
    {
        #region AlbumArtistName - Line 66

        [Fact]
        public void AlbumArtistName_WithNullAlbum_ShouldReturnVariousArtists()
        {
            // Arrange
            var track = new QobuzTrack { Album = null };

            // Act
            var result = track.AlbumArtistName;

            // Assert - Line 66: Album?.Artist?.Name ?? "Various Artists"
            result.Should().Be("Various Artists");
        }

        [Fact]
        public void AlbumArtistName_WithAlbumArtist_ShouldReturnArtistName()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Album = new QobuzAlbum
                {
                    Artist = new QobuzArtist { Name = "Daft Punk" }
                }
            };

            // Act
            var result = track.AlbumArtistName;

            // Assert - Line 66: Album?.Artist?.Name
            result.Should().Be("Daft Punk");
        }

        #endregion

        #region AlbumTitle - Line 71

        [Fact]
        public void AlbumTitle_WithNullAlbum_ShouldReturnEmpty()
        {
            // Arrange
            var track = new QobuzTrack { Album = null };

            // Act
            var result = track.AlbumTitle;

            // Assert - Line 71: Album?.Title ?? string.Empty
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void AlbumTitle_WithAlbum_ShouldReturnTitle()
        {
            // Arrange
            var track = new QobuzTrack
            {
                Album = new QobuzAlbum { Title = "Random Access Memories" }
            };

            // Act
            var result = track.AlbumTitle;

            // Assert - Line 71
            result.Should().Be("Random Access Memories");
        }

        #endregion

        #region Quality - Lines 113-122

        [Fact]
        public void Quality_WithHiResSampleRate_ShouldReturnHiRes()
        {
            // Arrange - Line 115: MaximumSampleRate > 48000
            var track = new QobuzTrack
            {
                MaximumSampleRate = 96000,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.Quality;

            // Assert - Lines 115-116
            result.Should().Be("Hi-Res");
        }

        [Fact]
        public void Quality_WithHiResBitDepth_ShouldReturnHiRes()
        {
            // Arrange - Line 115: MaximumBitDepth > 16
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = 24
            };

            // Act
            var result = track.Quality;

            // Assert - Lines 115-116
            result.Should().Be("Hi-Res");
        }

        [Fact]
        public void Quality_WithLosslessBitDepth_ShouldReturnLossless()
        {
            // Arrange - Line 117: MaximumBitDepth > 0 (but not Hi-Res)
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.Quality;

            // Assert - Line 117
            result.Should().Be("Lossless");
        }

        [Fact]
        public void Quality_WithNoBitDepth_ShouldReturnLossy()
        {
            // Arrange - Line 118: fallback when MaximumBitDepth <= 0
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = null
            };

            // Act
            var result = track.Quality;

            // Assert - Line 118
            result.Should().Be("Lossy");
        }

        #endregion

        #region BitRate - Lines 126-138

        [Fact]
        public void BitRate_WithHiRes24Bit192kHz_ShouldReturn4608()
        {
            // Arrange - Lines 129-130: MaximumSampleRate > 48000 && MaximumBitDepth >= 24
            var track = new QobuzTrack
            {
                MaximumSampleRate = 192000,
                MaximumBitDepth = 24
            };

            // Act
            var result = track.BitRate;

            // Assert - Line 130
            result.Should().Be(4608);
        }

        [Fact]
        public void BitRate_WithHiResButLowBitDepth_ShouldReturnCdQuality()
        {
            // Arrange - Lines 129-130 condition fails (BitDepth < 24), falls to Line 131
            var track = new QobuzTrack
            {
                MaximumSampleRate = 96000,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.BitRate;

            // Assert - Line 132: MaximumBitDepth >= 16
            result.Should().Be(1411);
        }

        [Fact]
        public void BitRate_WithCdQuality_ShouldReturn1411()
        {
            // Arrange - Lines 131-132: MaximumBitDepth >= 16
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.BitRate;

            // Assert - Line 132
            result.Should().Be(1411);
        }

        [Fact]
        public void BitRate_WithLowQuality_ShouldReturn320()
        {
            // Arrange - Line 133: fallback (MP3)
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = null
            };

            // Act
            var result = track.BitRate;

            // Assert - Line 134
            result.Should().Be(320);
        }

        #endregion

        #region SampleRate/BitDepth Aliases - Lines 143-148

        [Fact]
        public void SampleRate_ShouldReturnMaximumSampleRate()
        {
            // Arrange - Line 144
            var track = new QobuzTrack { MaximumSampleRate = 96000 };

            // Act
            var result = track.SampleRate;

            // Assert - Line 144
            result.Should().Be(96000);
        }

        [Fact]
        public void BitDepth_ShouldReturnMaximumBitDepth()
        {
            // Arrange - Line 148
            var track = new QobuzTrack { MaximumBitDepth = 24 };

            // Act
            var result = track.BitDepth;

            // Assert - Line 148
            result.Should().Be(24);
        }

        #endregion

        #region GetFullTitle - Lines 153-167

        [Fact]
        public void GetFullTitle_WithNullTitle_ShouldReturnUnknownTrack()
        {
            // Arrange - Line 155: string.IsNullOrWhiteSpace(Title) -> "Unknown Track"
            var track = new QobuzTrack { Title = null, Version = null };

            // Act
            var result = track.GetFullTitle();

            // Assert - Line 155
            result.Should().Be("Unknown Track");
        }

        [Fact]
        public void GetFullTitle_WithEmptyTitle_ShouldReturnUnknownTrack()
        {
            // Arrange - Line 155
            var track = new QobuzTrack { Title = "", Version = null };

            // Act
            var result = track.GetFullTitle();

            // Assert - Line 155
            result.Should().Be("Unknown Track");
        }

        [Fact]
        public void GetFullTitle_WhenVersionAlreadyInTitle_ShouldNotDuplicate()
        {
            // Arrange - Line 163: title.Contains(sanitizedVersion)
            var track = new QobuzTrack
            {
                Title = "Test Song (Remix)",
                Version = "Remix"
            };

            // Act
            var result = track.GetFullTitle();

            // Assert - Line 163: should not append version again
            result.Should().Be("Test Song (Remix)");
        }

        #endregion

        #region GetPerformerName - Lines 172-181

        [Fact]
        public void GetPerformerName_WithPerformer_ShouldReturnPerformerName()
        {
            // Arrange - Line 174
            var track = new QobuzTrack
            {
                Performer = new QobuzArtist { Name = "Daft Punk" },
                Performers = null
            };

            // Act
            var result = track.GetPerformerName();

            // Assert - Line 175
            result.Should().Be("Daft Punk");
        }

        [Fact]
        public void GetPerformerName_WithPerformersString_ShouldReturnPerformers()
        {
            // Arrange - Lines 178-179: Performer null but Performers populated
            var track = new QobuzTrack
            {
                Performer = null,
                Performers = "Multiple Artists"
            };

            // Act
            var result = track.GetPerformerName();

            // Assert - Line 179
            result.Should().Be("Multiple Artists");
        }

        [Fact]
        public void GetPerformerName_WithNoPerformerInfo_ShouldReturnVariousArtists()
        {
            // Arrange - Line 181: fallback
            var track = new QobuzTrack
            {
                Performer = null,
                Performers = null
            };

            // Act
            var result = track.GetPerformerName();

            // Assert - Line 181
            result.Should().Be("Various Artists");
        }

        [Fact]
        public void GetPerformerName_WithEmptyPerformerName_ShouldFallBackToPerformers()
        {
            // Arrange - Performer with empty name
            var track = new QobuzTrack
            {
                Performer = new QobuzArtist { Name = "" },
                Performers = "Fallback Artist"
            };

            // Act
            var result = track.GetPerformerName();

            // Assert - Line 179
            result.Should().Be("Fallback Artist");
        }

        #endregion

        #region HasHiResQuality - Lines 191-194

        [Fact]
        public void HasHiResQuality_WithHighSampleRate_ShouldReturnTrue()
        {
            // Arrange - Line 193: MaximumSampleRate > 48000
            var track = new QobuzTrack
            {
                MaximumSampleRate = 96000,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.HasHiResQuality();

            // Assert - Line 193
            result.Should().BeTrue();
        }

        [Fact]
        public void HasHiResQuality_WithHighBitDepth_ShouldReturnTrue()
        {
            // Arrange - Line 193: MaximumBitDepth > 16
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = 24
            };

            // Act
            var result = track.HasHiResQuality();

            // Assert - Line 193
            result.Should().BeTrue();
        }

        [Fact]
        public void HasHiResQuality_WithStandardQuality_ShouldReturnFalse()
        {
            // Arrange - neither condition met
            var track = new QobuzTrack
            {
                MaximumSampleRate = 44100,
                MaximumBitDepth = 16
            };

            // Act
            var result = track.HasHiResQuality();

            // Assert - neither > 48000 nor > 16
            result.Should().BeFalse();
        }

        #endregion

        #region GetEstimatedFileSize - Lines 199-215

        [Fact]
        public void GetEstimatedFileSize_Format7_Flac24_96_ShouldCalculateCorrectly()
        {
            // Arrange - Line 204: formatId 7 -> 24.0 MB/min
            var track = new QobuzTrack { DurationSeconds = 120 }; // 2 minutes

            // Act
            var result = track.GetEstimatedFileSize(7);

            // Assert - Line 204: 24.0 * 2 * 1024 * 1024
            var expected = (long)(24.0 * 2 * 1024 * 1024);
            result.Should().Be(expected);
        }

        [Fact]
        public void GetEstimatedFileSize_Format27_Flac24_192_ShouldCalculateCorrectly()
        {
            // Arrange - Line 205: formatId 27 -> 36.0 MB/min
            var track = new QobuzTrack { DurationSeconds = 120 }; // 2 minutes

            // Act
            var result = track.GetEstimatedFileSize(27);

            // Assert - Line 205: 36.0 * 2 * 1024 * 1024
            var expected = (long)(36.0 * 2 * 1024 * 1024);
            result.Should().Be(expected);
        }

        [Fact]
        public void GetEstimatedFileSize_UnknownFormat_ShouldUseDefault()
        {
            // Arrange - Line 206: default -> 10.0 MB/min
            var track = new QobuzTrack { DurationSeconds = 60 }; // 1 minute

            // Act
            var result = track.GetEstimatedFileSize(999);

            // Assert - Line 207: 10.0 * 1 * 1024 * 1024
            var expected = (long)(10.0 * 1 * 1024 * 1024);
            result.Should().Be(expected);
        }

        #endregion

        #region IsExplicit - Lines 220-223

        [Fact]
        public void IsExplicit_WithParentalWarning_ShouldReturnTrue()
        {
            // Arrange - Line 222
            var track = new QobuzTrack { ParentalWarning = true };

            // Act
            var result = track.IsExplicit();

            // Assert - Line 222
            result.Should().BeTrue();
        }

        [Fact]
        public void IsExplicit_WithoutParentalWarning_ShouldReturnFalse()
        {
            // Arrange - Line 222
            var track = new QobuzTrack { ParentalWarning = false };

            // Act
            var result = track.IsExplicit();

            // Assert - Line 222
            result.Should().BeFalse();
        }

        #endregion

        #region GetComposerName - Lines 228-231

        [Fact]
        public void GetComposerName_WithComposer_ShouldReturnName()
        {
            // Arrange - Line 230
            var track = new QobuzTrack
            {
                Composer = new QobuzComposer { Name = "John Williams" }
            };

            // Act
            var result = track.GetComposerName();

            // Assert - Line 230
            result.Should().Be("John Williams");
        }

        [Fact]
        public void GetComposerName_WithNullComposer_ShouldReturnEmpty()
        {
            // Arrange - Line 230
            var track = new QobuzTrack { Composer = null };

            // Act
            var result = track.GetComposerName();

            // Assert - Line 230: Composer?.Name ?? string.Empty
            result.Should().Be(string.Empty);
        }

        #endregion

        #region Duration - Line 76

        [Fact]
        public void Duration_ShouldConvertSecondsToTimeSpan()
        {
            // Arrange - Line 76: TimeSpan.FromSeconds(DurationSeconds)
            var track = new QobuzTrack { DurationSeconds = 274 };

            // Act
            var result = track.Duration;

            // Assert - Line 76
            result.Should().Be(TimeSpan.FromSeconds(274));
            result.TotalSeconds.Should().Be(274);
        }

        #endregion

        #region DiscNumber Default - Line 31

        [Fact]
        public void DiscNumber_DefaultValue_ShouldBeOne()
        {
            // Arrange - Line 31: = 1
            var track = new QobuzTrack();

            // Act
            var result = track.DiscNumber;

            // Assert - Line 31
            result.Should().Be(1);
        }

        #endregion
    }
}
