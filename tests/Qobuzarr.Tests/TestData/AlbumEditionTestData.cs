using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.TestData
{
    /// <summary>
    /// Specialized test data for album edition testing scenarios.
    /// Covers live albums, remastered editions, deluxe versions, and version field variations.
    /// </summary>
    public static class AlbumEditionTestData
    {
        #region Live Album Scenarios

        /// <summary>
        /// Live album test cases with different Version field patterns
        /// </summary>
        public static IEnumerable<object[]> LiveAlbumScenarios =>
            new List<object[]>
            {
                // Version field explicitly states venue
                new object[] 
                { 
                    "Live at Brixton Academy", 
                    "Artist - Album (Year) [Live at Brixton Academy] [FLAC WEB]",
                    "LiveAtVenue"
                },
                
                // Version field with date and venue
                new object[] 
                { 
                    "Live at Madison Square Garden, December 2019", 
                    "Artist - Album (Year) [Live at Madison Square Garden, December 2019] [FLAC WEB]",
                    "LiveAtVenueWithDate"
                },
                
                // Simple live designation
                new object[] 
                { 
                    "Live", 
                    "Artist - Album (Year) [Live] [FLAC WEB]",
                    "SimpleLive"
                },
                
                // Live with year only
                new object[] 
                { 
                    "Live 2020", 
                    "Artist - Album (Year) [Live 2020] [FLAC WEB]",
                    "LiveWithYear"
                },
                
                // Concert recording
                new object[] 
                { 
                    "Concert Recording", 
                    "Artist - Album (Year) [Concert Recording] [FLAC WEB]",
                    "ConcertRecording"
                },
                
                // Acoustic live version
                new object[] 
                { 
                    "Acoustic Live", 
                    "Artist - Album (Year) [Acoustic Live] [FLAC WEB]",
                    "AcousticLive"
                },
                
                // Radio session
                new object[] 
                { 
                    "BBC Radio 1 Live Session", 
                    "Artist - Album (Year) [BBC Radio 1 Live Session] [FLAC WEB]",
                    "RadioSession"
                },
                
                // TV performance
                new object[] 
                { 
                    "Live on Late Night TV", 
                    "Artist - Album (Year) [Live on Late Night TV] [FLAC WEB]",
                    "TVPerformance"
                }
            };

        #endregion

        #region Edition Variants

        /// <summary>
        /// Different album edition types and their expected title formatting
        /// </summary>
        public static IEnumerable<object[]> EditionVariants =>
            new List<object[]>
            {
                // Deluxe editions
                new object[] 
                { 
                    "Deluxe Edition", 
                    "Artist - Album (Year) [Deluxe Edition] [FLAC WEB]",
                    "DeluxeEdition"
                },
                
                new object[] 
                { 
                    "Deluxe", 
                    "Artist - Album (Year) [Deluxe] [FLAC WEB]",
                    "DeluxeShort"
                },
                
                // Remastered editions
                new object[] 
                { 
                    "Remastered", 
                    "Artist - Album (Year) [Remastered] [FLAC WEB]",
                    "Remastered"
                },
                
                new object[] 
                { 
                    "2023 Remaster", 
                    "Artist - Album (Year) [2023 Remaster] [FLAC WEB]",
                    "RemasterWithYear"
                },
                
                new object[] 
                { 
                    "Digital Remaster", 
                    "Artist - Album (Year) [Digital Remaster] [FLAC WEB]",
                    "DigitalRemaster"
                },
                
                // Anniversary editions
                new object[] 
                { 
                    "25th Anniversary Edition", 
                    "Artist - Album (Year) [25th Anniversary Edition] [FLAC WEB]",
                    "AnniversaryEdition"
                },
                
                new object[] 
                { 
                    "Anniversary Remaster", 
                    "Artist - Album (Year) [Anniversary Remaster] [FLAC WEB]",
                    "AnniversaryRemaster"
                },
                
                // Expanded editions
                new object[] 
                { 
                    "Expanded Edition", 
                    "Artist - Album (Year) [Expanded Edition] [FLAC WEB]",
                    "ExpandedEdition"
                },
                
                new object[] 
                { 
                    "Complete Sessions", 
                    "Artist - Album (Year) [Complete Sessions] [FLAC WEB]",
                    "CompleteSessions"
                },
                
                // Special editions
                new object[] 
                { 
                    "Special Edition", 
                    "Artist - Album (Year) [Special Edition] [FLAC WEB]",
                    "SpecialEdition"
                },
                
                new object[] 
                { 
                    "Limited Edition", 
                    "Artist - Album (Year) [Limited Edition] [FLAC WEB]",
                    "LimitedEdition"
                },
                
                // Collector's editions
                new object[] 
                { 
                    "Collector's Edition", 
                    "Artist - Album (Year) [Collector's Edition] [FLAC WEB]",
                    "CollectorsEdition"
                },
                
                // Box set editions
                new object[] 
                { 
                    "Box Set", 
                    "Artist - Album (Year) [Box Set] [FLAC WEB]",
                    "BoxSet"
                },
                
                new object[] 
                { 
                    "Complete Box Set", 
                    "Artist - Album (Year) [Complete Box Set] [FLAC WEB]",
                    "CompleteBoxSet"
                }
            };

        #endregion

        #region Edge Cases

        /// <summary>
        /// Edge cases for Version field handling
        /// </summary>
        public static IEnumerable<object[]> VersionFieldEdgeCases =>
            new List<object[]>
            {
                // Empty and null versions
                new object[] { null, "Artist - Album (Year) [FLAC WEB]", "NullVersion" },
                new object[] { "", "Artist - Album (Year) [FLAC WEB]", "EmptyVersion" },
                new object[] { "   ", "Artist - Album (Year) [FLAC WEB]", "WhitespaceVersion" },
                
                // Special characters in versions
                new object[] 
                { 
                    "Live @ The Forum", 
                    "Artist - Album (Year) [Live @ The Forum] [FLAC WEB]",
                    "VersionWithAtSymbol"
                },
                
                new object[] 
                { 
                    "Version 2.0", 
                    "Artist - Album (Year) [Version 2.0] [FLAC WEB]",
                    "VersionWithNumber"
                },
                
                new object[] 
                { 
                    "Re-Issue", 
                    "Artist - Album (Year) [Re-Issue] [FLAC WEB]",
                    "VersionWithHyphen"
                },
                
                new object[] 
                { 
                    "Director's Cut", 
                    "Artist - Album (Year) [Director's Cut] [FLAC WEB]",
                    "VersionWithApostrophe"
                },
                
                // Long version strings
                new object[] 
                { 
                    "25th Anniversary Deluxe Remastered Edition with Bonus Tracks", 
                    "Artist - Album (Year) [25th Anniversary Deluxe Remastered Edition with Bonus Tracks] [FLAC WEB]",
                    "VeryLongVersion"
                },
                
                // International/Unicode versions
                new object[] 
                { 
                    "Édition Spéciale", 
                    "Artist - Album (Year) [Édition Spéciale] [FLAC WEB]",
                    "FrenchVersion"
                },
                
                new object[] 
                { 
                    "特別版", 
                    "Artist - Album (Year) [特別版] [FLAC WEB]",
                    "JapaneseVersion"
                },
                
                // Version already in title edge cases
                new object[] 
                { 
                    "Live", 
                    "Artist - Album Live (Year) [FLAC WEB]", // Should not duplicate "Live"
                    "VersionAlreadyInTitle"
                },
                
                new object[] 
                { 
                    "Deluxe Edition", 
                    "Artist - Album Deluxe Edition (Year) [FLAC WEB]", // Should not duplicate
                    "DeluxeAlreadyInTitle"
                }
            };

        #endregion

        #region Complex Scenarios

        /// <summary>
        /// Complex real-world scenarios with multiple edition markers
        /// </summary>
        public static IEnumerable<object[]> ComplexEditionScenarios =>
            new List<object[]>
            {
                // Multiple edition indicators in version
                new object[] 
                { 
                    "Deluxe Remastered Edition", 
                    "Artist - Album (Year) [Deluxe Remastered Edition] [FLAC WEB]",
                    "MultipleEditionMarkers"
                },
                
                // Live deluxe edition
                new object[] 
                { 
                    "Live Deluxe Edition", 
                    "Artist - Album (Year) [Live Deluxe Edition] [FLAC WEB]",
                    "LiveDeluxeCombination"
                },
                
                // Anniversary remaster with venue
                new object[] 
                { 
                    "10th Anniversary Remaster - Live at Wembley", 
                    "Artist - Album (Year) [10th Anniversary Remaster - Live at Wembley] [FLAC WEB]",
                    "AnniversaryLiveCombination"
                },
                
                // Year in version that differs from album year
                new object[] 
                { 
                    "2020 Remaster", 
                    "Artist - Album (1990) [2020 Remaster] [FLAC WEB]",
                    "RemasterYearDifferentFromAlbumYear"
                },
                
                // Format information in version
                new object[] 
                { 
                    "Hi-Res Audio Edition", 
                    "Artist - Album (Year) [Hi-Res Audio Edition] [FLAC WEB]",
                    "FormatInVersion"
                }
            };

        #endregion

        #region Test Album Builders

        /// <summary>
        /// Creates a live album with specified venue and date
        /// </summary>
        public static QobuzAlbum CreateLiveAlbum(string venue, DateTime concertDate, string version = null)
        {
            return QobuzAlbumBuilder.New()
                .WithId($"live_album_{venue?.Replace(" ", "_").ToLower()}")
                .WithTitle($"Live at {venue}")
                .WithArtist("Test Artist")
                .WithReleaseDate(concertDate)
                .WithGenre("Rock")
                .AsFullAlbum()
                .AsHiResFlac()
                .Build();
        }

        /// <summary>
        /// Creates multiple studio/live album pairs for the same artist
        /// </summary>
        public static (QobuzAlbum StudioAlbum, QobuzAlbum LiveAlbum) CreateStudioLivePair(
            string artistName, 
            string baseAlbumTitle, 
            int releaseYear,
            string liveVenue = "Royal Albert Hall")
        {
            var studioAlbum = QobuzAlbumBuilder.New()
                .WithId($"studio_{baseAlbumTitle.Replace(" ", "_").ToLower()}")
                .WithTitle(baseAlbumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(releaseYear)
                .AsFullAlbum()
                .AsCdQualityFlac()
                .Build();

            var liveAlbum = QobuzAlbumBuilder.New()
                .WithId($"live_{baseAlbumTitle.Replace(" ", "_").ToLower()}")
                .WithTitle(baseAlbumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(releaseYear + 1)
                .AsFullAlbum()
                .AsHiResFlac()
                .Build();

            // Set the Version field for the live album
            liveAlbum.Version = $"Live at {liveVenue}";

            return (studioAlbum, liveAlbum);
        }

        /// <summary>
        /// Creates an album with multiple editions (standard, deluxe, remastered)
        /// </summary>
        public static QobuzAlbum[] CreateMultipleEditions(
            string artistName, 
            string albumTitle, 
            int releaseYear)
        {
            var standardEdition = QobuzAlbumBuilder.New()
                .WithId($"standard_{albumTitle.Replace(" ", "_").ToLower()}")
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(releaseYear)
                .AsFullAlbum()
                .AsCdQualityFlac()
                .Build();

            var deluxeEdition = QobuzAlbumBuilder.New()
                .WithId($"deluxe_{albumTitle.Replace(" ", "_").ToLower()}")
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(releaseYear)
                .WithTracks(15, 280) // More tracks for deluxe
                .AsHiResFlac()
                .Build();
            deluxeEdition.Version = "Deluxe Edition";

            var remasteredEdition = QobuzAlbumBuilder.New()
                .WithId($"remastered_{albumTitle.Replace(" ", "_").ToLower()}")
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(releaseYear + 10) // Released 10 years later
                .AsFullAlbum()
                .AsHiResFlac()
                .Build();
            remasteredEdition.Version = $"{releaseYear + 10} Remaster";

            return new[] { standardEdition, deluxeEdition, remasteredEdition };
        }

        #endregion
    }
}