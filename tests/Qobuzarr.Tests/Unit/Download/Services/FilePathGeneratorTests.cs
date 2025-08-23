using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Comprehensive tests for FilePathGenerator covering file naming,
    /// path generation, quality descriptions, and edge cases.
    /// </summary>
    public class FilePathGeneratorTests : TestFixtureBase
    {
        private readonly FilePathGenerator _generator;

        public FilePathGeneratorTests()
        {
            _generator = new FilePathGenerator();
        }

        #region GenerateFileName Tests

        [Fact]
        public void GenerateFileName_WithValidTrackAndAlbum_ShouldGenerateCorrectFileName()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 5;
            track.Title = "Test Track Title";
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().Be("05 - Test Track Title.flac");
        }

        [Fact]
        public void GenerateFileName_WithSpecialCharactersInTitle_ShouldSanitize()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 1;
            track.Title = "Track/Title:With*Special<Characters>|\"?";
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().StartWith("01 - ");
            fileName.Should().EndWith(".flac");
            fileName.Should().NotContain("/");
            fileName.Should().NotContain(":");
            fileName.Should().NotContain("*");
            fileName.Should().NotContain("<");
            fileName.Should().NotContain(">");
            fileName.Should().NotContain("|");
            fileName.Should().NotContain("\"");
            fileName.Should().NotContain("?");
        }

        [Fact]
        public void GenerateFileName_WithHighTrackNumber_ShouldFormatCorrectly()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 15;
            track.Title = "Track Fifteen";
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.Mp3320;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().Be("15 - Track Fifteen.mp3");
        }

        [Fact]
        public void GenerateFileName_WithNullTrack_ShouldThrowArgumentNullException()
        {
            // Arrange
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act & Assert
            _generator.Invoking(x => x.GenerateFileName(null, album, formatId))
                     .Should().Throw<ArgumentNullException>()
                     .WithMessage("*track*");
        }

        [Fact]
        public void GenerateFileName_WithNullAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange
            var track = CreateSampleTrack();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act & Assert
            _generator.Invoking(x => x.GenerateFileName(track, null, formatId))
                     .Should().Throw<ArgumentNullException>()
                     .WithMessage("*album*");
        }

        [Theory]
        [InlineData(5, "MP3 320")]
        [InlineData(6, "FLAC CD")]  
        [InlineData(7, "FLAC Hi-Res")]
        [InlineData(27, "FLAC Max")]
        public void GenerateFileName_WithDifferentFormats_ShouldUseCorrectExtension(int formatId, string qualityName)
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 1;
            track.Title = "Test Track";
            var album = CreateSampleAlbum();

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            var expectedExtension = formatId == QobuzPluginConstants.QualityFormats.Mp3320 ? ".mp3" : ".flac";
            fileName.Should().EndWith(expectedExtension);
        }

        #endregion

        #region GenerateOptimizedFileName Tests

        [Fact]
        public void GenerateOptimizedFileName_WithValidTrackDownload_ShouldGenerateCorrectFileName()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = 3;
            trackDownload.Artist = "Test Artist";
            trackDownload.Title = "Test Track";
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().Be("03. Test Artist - Test Track.flac");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithSpecialCharacters_ShouldSanitize()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = 1;
            trackDownload.Artist = "Artist/Name:With*Special<Chars>";
            trackDownload.Title = "Track|Title\"With?Issues";
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().StartWith("01. ");
            fileName.Should().EndWith(".flac");
            fileName.Should().NotContain("/");
            fileName.Should().NotContain(":");
            fileName.Should().NotContain("*");
            fileName.Should().NotContain("<");
            fileName.Should().NotContain(">");
            fileName.Should().NotContain("|");
            fileName.Should().NotContain("\"");
            fileName.Should().NotContain("?");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithNullArtist_ShouldUseUnknownArtist()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = 1;
            trackDownload.Artist = null;
            trackDownload.Title = "Test Track";
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().Be("01. Unknown Artist - Test Track.flac");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithNullTitle_ShouldUseUnknownTrack()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = 1;
            trackDownload.Artist = "Test Artist";
            trackDownload.Title = null;
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().Be("01. Test Artist - Unknown Track.flac");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithNullTrackNumber_ShouldUse00()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = null;
            trackDownload.Artist = "Test Artist";
            trackDownload.Title = "Test Track";
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().Be("00. Test Artist - Test Track.flac");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithNullTrackDownload_ShouldThrowArgumentNullException()
        {
            // Arrange
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act & Assert
            _generator.Invoking(x => x.GenerateOptimizedFileName(null, quality))
                     .Should().Throw<ArgumentNullException>()
                     .WithMessage("*trackDownload*");
        }

        #endregion

        #region GetFileExtension Tests

        [Theory]
        [InlineData(5, ".mp3")]   // MP3 320
        [InlineData(6, ".flac")]  // FLAC CD
        [InlineData(7, ".flac")]  // FLAC 24/96
        [InlineData(27, ".flac")] // FLAC 24/192
        [InlineData(999, ".flac")] // Unknown format defaults to FLAC
        public void GetFileExtension_WithVariousFormats_ShouldReturnCorrectExtension(int formatId, string expectedExtension)
        {
            // Act
            var extension = _generator.GetFileExtension(formatId);

            // Assert
            extension.Should().Be(expectedExtension);
        }

        [Fact]
        public void GetFileExtension_WithNegativeFormatId_ShouldReturnDefaultFLAC()
        {
            // Act
            var extension = _generator.GetFileExtension(-1);

            // Assert
            extension.Should().Be(".flac");
        }

        [Fact]
        public void GetFileExtension_WithZeroFormatId_ShouldReturnDefaultFLAC()
        {
            // Act
            var extension = _generator.GetFileExtension(0);

            // Assert
            extension.Should().Be(".flac");
        }

        #endregion

        #region GetQualityDescription Tests

        [Fact]
        public void GetQualityDescription_WithHiResTrack_ShouldReturnHiResDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 24;
            track.MaximumSampleRate = 192000;

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be("Hi-Res FLAC 24bit/192kHz");
        }

        [Fact]
        public void GetQualityDescription_WithCDQualityTrack_ShouldReturnFLACDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 16;
            track.MaximumSampleRate = 44100;

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be("FLAC 16bit/44kHz");
        }

        [Fact]
        public void GetQualityDescription_WithLowQualityTrack_ShouldReturnMP3Description()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 0;
            track.MaximumSampleRate = 0;

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be("MP3 320kbps");
        }

        [Theory]
        [InlineData(24, 96000, "Hi-Res FLAC 24bit/96kHz")]
        [InlineData(24, 48000, "FLAC 24bit/48kHz")]
        [InlineData(16, 48000, "FLAC 16bit/48kHz")]
        [InlineData(16, 44100, "FLAC 16bit/44kHz")]
        [InlineData(8, 22050, "MP3 320kbps")]
        [InlineData(0, 0, "MP3 320kbps")]
        public void GetQualityDescription_WithVariousQualities_ShouldReturnCorrectDescriptions(
            int bitDepth, int sampleRate, string expectedDescription)
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = bitDepth;
            track.MaximumSampleRate = sampleRate;

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be(expectedDescription);
        }

        [Fact]
        public void GetQualityDescription_WithNullTrack_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            _generator.Invoking(x => x.GetQualityDescription(null))
                     .Should().Throw<ArgumentNullException>()
                     .WithMessage("*track*");
        }

        [Fact]
        public void GetQualityDescription_With24BitLowSampleRate_ShouldReturnFLACDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 24;
            track.MaximumSampleRate = 48000; // Less than 96kHz

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be("FLAC 24bit/48kHz");
        }

        [Fact]
        public void GetQualityDescription_With16BitHighSampleRate_ShouldReturnFLACDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 16;
            track.MaximumSampleRate = 96000; // High sample rate but 16-bit

            // Act
            var description = _generator.GetQualityDescription(track);

            // Assert
            description.Should().Be("FLAC 16bit/96kHz");
        }

        #endregion

        #region Edge Cases Tests

        [Fact]
        public void GenerateFileName_WithVeryLongTitle_ShouldHandleGracefully()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 1;
            track.Title = new string('A', 200); // Very long title
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().StartWith("01 - ");
            fileName.Should().EndWith(".flac");
            fileName.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void GenerateFileName_WithEmptyTitle_ShouldHandleGracefully()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 1;
            track.Title = "";
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().StartWith("01 - ");
            fileName.Should().EndWith(".flac");
        }

        [Fact]
        public void GenerateOptimizedFileName_WithEmptyArtistAndTitle_ShouldUseDefaults()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();
            trackDownload.TrackNumber = 1;
            trackDownload.Artist = "";
            trackDownload.Title = "";
            var quality = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateOptimizedFileName(trackDownload, quality);

            // Assert
            fileName.Should().Be("01. Unknown Artist - Unknown Track.flac");
        }

        [Fact]
        public void GenerateFileName_WithZeroTrackNumber_ShouldFormatAsZero()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.TrackNumber = 0;
            track.Title = "Track Zero";
            var album = CreateSampleAlbum();
            var formatId = QobuzPluginConstants.QualityFormats.FlacCd;

            // Act
            var fileName = _generator.GenerateFileName(track, album, formatId);

            // Assert
            fileName.Should().StartWith("00 - ");
        }

        #endregion

        #region Helper Methods

        private QobuzTrack CreateSampleTrack()
        {
            return new QobuzTrack
            {
                Id = "87654321",
                Title = "Test Track",
                TrackNumber = 1,
                DiscNumber = 1,
                MaximumBitDepth = 16,
                MaximumSampleRate = 44100,
                Performer = new QobuzArtist { Name = "Test Artist" }
            };
        }

        private QobuzAlbum CreateSampleAlbum()
        {
            return new QobuzAlbum
            {
                Id = "12345678",
                Title = "Test Album",
                Artist = new QobuzArtist { Name = "Test Album Artist" }
            };
        }

        private TrackDownload CreateSampleTrackDownload()
        {
            return new TrackDownload
            {
                QobuzTrackId = "87654321",
                Title = "Test Track",
                Artist = "Test Artist",
                AlbumArtist = "Test Album Artist",
                Album = "Test Album",
                TrackNumber = 1,
                Quality = "FLAC 16bit/44kHz",
                MetadataSource = "Qobuz"
            };
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}