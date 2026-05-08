using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests
{
    public class QobuzModelMapperCovTests
    {
        [Fact]
        public void GetAllArtistNames_WithPrimaryArtistOnly_ShouldReturnSingleName()
        {
            var album = QobuzAlbumBuilder.New().WithArtist("Daft Punk").Build();
            var artistNames = album.GetAllArtistNames();
            artistNames.Should().HaveCount(1);
            artistNames[0].Should().Be("Daft Punk");
        }

        [Fact]
        public void GetAllArtistNames_WithAdditionalArtists_ShouldReturnDistinctNames()
        {
            var album = new QobuzAlbum
            {
                Id = "test123", Title = "Test Album",
                Artist = new QobuzArtist { Id = "artist1", Name = "Main Artist" },
                Artists = new System.Collections.Generic.List<QobuzArtist>
                {
                    new QobuzArtist { Id = "artist2", Name = "Featured Artist 1" },
                    new QobuzArtist { Id = "artist3", Name = "Featured Artist 2" },
                    new QobuzArtist { Id = "artist1", Name = "Main Artist" }
                }
            };
            var artistNames = album.GetAllArtistNames();
            artistNames.Should().HaveCount(3, "duplicates should be removed");
            artistNames.Should().Contain("Main Artist");
            artistNames.Should().Contain("Featured Artist 1");
            artistNames.Should().Contain("Featured Artist 2");
        }

        [Fact]
        public void GetAllArtistNames_WithNullPrimaryArtist_ShouldReturnAdditionalArtists()
        {
            var album = new QobuzAlbum
            {
                Id = "test123", Title = "Test Album", Artist = null,
                Artists = new System.Collections.Generic.List<QobuzArtist>
                { new QobuzArtist { Id = "artist1", Name = "Solo Artist" } }
            };
            var artistNames = album.GetAllArtistNames();
            artistNames.Should().HaveCount(1);
            artistNames[0].Should().Be("Solo Artist");
        }

        [Fact]
        public void GetAllArtistNames_WithEmptyNames_ShouldFilterOutWhitespaceNames()
        {
            var album = new QobuzAlbum
            {
                Id = "test123", Title = "Test Album",
                Artist = new QobuzArtist { Id = "artist1", Name = "   " },
                Artists = new System.Collections.Generic.List<QobuzArtist>
                {
                    new QobuzArtist { Id = "artist2", Name = "" },
                    new QobuzArtist { Id = "artist3", Name = "Valid Artist" }
                }
            };
            var artistNames = album.GetAllArtistNames();
            artistNames.Should().HaveCount(1);
            artistNames[0].Should().Be("Valid Artist");
        }

        [Fact]
        public void GetLabelName_WithValidLabel_ShouldReturnLabelName()
        {
            var album = QobuzAlbumBuilder.New().WithLabel("Columbia Records").Build();
            album.GetLabelName().Should().Be("Columbia Records");
        }

        [Fact]
        public void GetLabelName_WithNullLabel_ShouldReturnUnknownLabel()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", Label = null };
            album.GetLabelName().Should().Be("Unknown Label");
        }

        [Fact]
        public void GetLabelName_WithNullLabelName_ShouldReturnUnknownLabel()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", Label = new QobuzLabel { Name = null } };
            album.GetLabelName().Should().Be("Unknown Label");
        }

        [Fact]
        public void HasHiResQuality_With24Bit96kHz_ShouldReturnTrue()
        {
            var album = QobuzAlbumBuilder.New().WithQuality(24, 96000).Build();
            album.HasHiResQuality().Should().BeTrue();
        }

        [Fact]
        public void HasHiResQuality_With24Bit48kHz_ShouldReturnTrue_DueToBitDepth()
        {
            var album = QobuzAlbumBuilder.New().WithQuality(24, 48000).Build();
            album.HasHiResQuality().Should().BeTrue("24-bit qualifies as Hi-Res");
        }

        [Fact]
        public void HasHiResQuality_With16Bit48kHz_ShouldReturnFalse()
        {
            var album = QobuzAlbumBuilder.New().WithQuality(16, 48000).Build();
            album.HasHiResQuality().Should().BeFalse("16-bit/48kHz is not Hi-Res");
        }

        [Fact]
        public void HasHiResQuality_WithNullQualitySpecs_ShouldReturnFalse()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", MaximumBitDepth = null, MaximumSampleRate = null };
            album.HasHiResQuality().Should().BeFalse();
        }

        [Fact]
        public void HasHiResQuality_With192kHz_ShouldReturnTrue_DueToSampleRate()
        {
            var album = QobuzAlbumBuilder.New().WithQuality(16, 192000).Build();
            album.HasHiResQuality().Should().BeTrue("192kHz qualifies as Hi-Res");
        }

        [Fact]
        public void IsExplicit_WithAlbumParentalWarningTrue_ShouldReturnTrue()
        {
            var album = QobuzAlbumBuilder.New().AsExplicit().Build();
            album.IsExplicit().Should().BeTrue();
        }

        [Fact]
        public void IsExplicit_WithNoExplicitContent_ShouldReturnFalse()
        {
            var album = QobuzAlbumBuilder.New().Build();
            album.IsExplicit().Should().BeFalse();
        }

        [Fact]
        public void IsExplicit_WithNullTracks_ShouldReturnFalse()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", ParentalWarning = false, TracksContainer = null };
            album.IsExplicit().Should().BeFalse();
        }

        [Fact]
        public void GetArtistName_WithNullArtist_ShouldReturnVariousArtists()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", Artist = null };
            album.GetArtistName().Should().Be("Various Artists");
        }

        [Fact]
        public void GetArtistName_WithNullArtistName_ShouldReturnVariousArtists()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", Artist = new QobuzArtist { Name = null } };
            album.GetArtistName().Should().Be("Various Artists");
        }

        [Fact]
        public void GetArtistName_WithEmptyArtistName_ShouldReturnEmpty()
        {
            // Source uses ?? not IsNullOrWhiteSpace, so empty string passes through
            var album = new QobuzAlbum { Id = "t", Title = "T", Artist = new QobuzArtist { Name = "" } };
            album.GetArtistName().Should().BeEmpty();
        }

        [Fact]
        public void Track_HasHiResQuality_With24Bit96kHz_ShouldReturnTrue()
        {
            var track = QobuzTrackBuilder.New().AsHiResFlac().Build();
            track.HasHiResQuality().Should().BeTrue();
        }

        [Fact]
        public void Track_HasHiResQuality_With16Bit44kHz_ShouldReturnFalse()
        {
            var track = QobuzTrackBuilder.New().AsCdQualityFlac().Build();
            track.HasHiResQuality().Should().BeFalse();
        }

        [Fact]
        public void Track_HasHiResQuality_With32Bit48kHz_ShouldReturnTrue()
        {
            var track = QobuzTrackBuilder.New().WithQuality(32, 48000).Build();
            track.HasHiResQuality().Should().BeTrue();
        }

        [Fact]
        public void Track_HasHiResQuality_With96kHz_ShouldReturnTrue()
        {
            var track = QobuzTrackBuilder.New().WithQuality(16, 96000).Build();
            track.HasHiResQuality().Should().BeTrue();
        }

        [Fact]
        public void Track_IsExplicit_WithParentalWarningTrue_ShouldReturnTrue()
        {
            var track = new QobuzTrack { Id = "1", Title = "E", ParentalWarning = true };
            track.IsExplicit().Should().BeTrue();
        }

        [Fact]
        public void Track_IsExplicit_WithParentalWarningFalse_ShouldReturnFalse()
        {
            var track = QobuzTrackBuilder.New().Build();
            track.IsExplicit().Should().BeFalse();
        }

        [Fact]
        public void Track_GetComposerName_WithValidComposer_ShouldReturnComposerName()
        {
            var track = QobuzTrackBuilder.New().WithComposer("Johann Sebastian Bach", "c1").Build();
            track.GetComposerName().Should().Be("Johann Sebastian Bach");
        }

        [Fact]
        public void Track_GetComposerName_WithNullComposer_ShouldReturnEmptyString()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Composer = null };
            track.GetComposerName().Should().BeEmpty();
        }

        [Fact]
        public void Track_GetComposerName_WithNullComposerName_ShouldReturnEmptyString()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Composer = new QobuzComposer { Name = null } };
            track.GetComposerName().Should().BeEmpty();
        }

        [Fact]
        public void Track_GetPerformerName_WithNullPerformer_ShouldReturnPerformersString()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Performer = null, Performers = "Alt Performer" };
            track.GetPerformerName().Should().Be("Alt Performer");
        }

        [Fact]
        public void Track_GetPerformerName_WithBothNull_ShouldReturnVariousArtists()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Performer = null, Performers = null };
            track.GetPerformerName().Should().Be("Various Artists");
        }

        [Fact]
        public void Track_GetPerformerName_WithEmptyPerformers_ShouldReturnVariousArtists()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Performer = null, Performers = "" };
            track.GetPerformerName().Should().Be("Various Artists");
        }

        [Fact]
        public void Track_GetPerformerName_WithWhitespacePerformers_ShouldReturnVariousArtists()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Performer = null, Performers = "   " };
            track.GetPerformerName().Should().Be("Various Artists");
        }

        [Fact]
        public void Track_GetPerformerName_WithValidPerformer_ShouldReturnPerformerName()
        {
            var track = QobuzTrackBuilder.New().WithPerformer("Daft Punk").Build();
            track.GetPerformerName().Should().Be("Daft Punk");
        }

        [Fact]
        public void GetGenre_WithPrimaryGenre_ShouldReturnGenreName()
        {
            var album = QobuzAlbumBuilder.New().WithGenre("Electronic").Build();
            album.GetGenre().Should().Be("Electronic");
        }

        [Fact]
        public void GetGenre_WithNullGenre_ShouldReturnFirstGenresListEntry()
        {
            var album = new QobuzAlbum
            {
                Id = "t", Title = "T", Genre = null,
                GenresList = new System.Collections.Generic.List<string> { "Rock", "Alternative", "Indie" }
            };
            album.GetGenre().Should().Be("Rock");
        }

        [Fact]
        public void GetGenre_WithBothNull_ShouldReturnNull()
        {
            var album = new QobuzAlbum { Id = "t", Title = "T", Genre = null, GenresList = null };
            album.GetGenre().Should().BeNull();
        }

        [Fact]
        public void GetGenre_WithEmptyGenresList_ShouldReturnNull()
        {
            var album = new QobuzAlbum
            {
                Id = "t", Title = "T", Genre = null,
                GenresList = new System.Collections.Generic.List<string>()
            };
            album.GetGenre().Should().BeNull();
        }

        [Fact]
        public void GetSafeFolderName_WithVeryLongTitle_ShouldTruncateTo200Characters()
        {
            var longTitle = new string((char)65, 250);
            var album = QobuzAlbumBuilder.New().WithTitle(longTitle).WithArtist("Test Artist").WithReleaseYear(2023).Build();
            var folderName = album.GetSafeFolderName();
            folderName.Length.Should().BeLessOrEqualTo(200);
            folderName.Should().StartWith("Test Artist");
        }

        [Fact]
        public void GetSafeFolderName_WithMultipleIllegalCharacters_ShouldReplaceAll()
        {
            var album = QobuzAlbumBuilder.New().WithTitle("Album<>:Name?/With|Special*Chars").WithArtist("Artist").Build();
            var folderName = album.GetSafeFolderName();
            folderName.Should().NotContain("<");
            folderName.Should().NotContain(">");
            folderName.Should().NotContain(":");
            folderName.Should().NotContain("/");
            folderName.Should().NotContain("\\");
            folderName.Should().NotContain("|");
            folderName.Should().NotContain("?");
            folderName.Should().NotContain("*");
        }

        [Fact]
        public void Track_GetFullTitle_WithVersionAlreadyInTitle_ShouldNotDuplicate()
        {
            var track = new QobuzTrack { Title = "Song (Remastered)", Version = "Remastered" };
            track.GetFullTitle().Should().Be("Song (Remastered)");
        }

        [Fact]
        public void Track_AlbumArtistName_WithNullAlbum_ShouldReturnVariousArtists()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Album = null };
            track.AlbumArtistName.Should().Be("Various Artists");
        }

        [Fact]
        public void Track_AlbumArtistName_WithNestedNulls_ShouldReturnVariousArtists()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Album = new QobuzAlbum { Id = "a", Artist = null } };
            track.AlbumArtistName.Should().Be("Various Artists");
        }

        [Fact]
        public void Track_AlbumTitle_WithNullAlbum_ShouldReturnEmptyString()
        {
            var track = new QobuzTrack { Id = "1", Title = "T", Album = null };
            track.AlbumTitle.Should().BeEmpty();
        }

        [Fact]
        public void Track_GetSafeFileName_WithTrackNumber9_ShouldStartWithZeroPadded9Prefix()
        {
            // GetSafeFileName zero-pads track numbers to at least 2 digits
            var track = QobuzTrackBuilder.New().WithTitle("Test Song").WithTrackNumber(9).Build();
            track.GetSafeFileName().Should().StartWith("09 - ");
        }

        [Fact]
        public void Track_GetSafeFileName_WithTrackNumber99_ShouldStartWith99Prefix()
        {
            var track = QobuzTrackBuilder.New().WithTitle("Test Song").WithTrackNumber(99).Build();
            track.GetSafeFileName().Should().StartWith("99 - ");
        }
    }
}
