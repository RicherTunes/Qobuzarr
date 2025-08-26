using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Models;
using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Common.Testing
{
    /// <summary>
    /// Factory methods for creating mock streaming service data for testing.
    /// Provides realistic test data that can be used across different streaming service plugins.
    /// </summary>
    public static class MockFactories
    {
        private static readonly Random Random = new Random(42); // Fixed seed for reproducible tests

        /// <summary>
        /// Common artist names for test data.
        /// </summary>
        private static readonly string[] ArtistNames = 
        {
            "The Test Band", "Mock Artist", "Sample Orchestra", "Digital Musicians",
            "Virtual Ensemble", "Test Case Collective", "Mock Symphony", "Sample Artists"
        };

        /// <summary>
        /// Common album titles for test data.
        /// </summary>
        private static readonly string[] AlbumTitles =
        {
            "Greatest Hits", "Test Album", "Sample Collection", "Mock Sessions",
            "Digital Anthology", "Virtual Concert", "Test Suite", "Sample Tracks"
        };

        /// <summary>
        /// Common track titles for test data.
        /// </summary>
        private static readonly string[] TrackTitles =
        {
            "Test Track", "Sample Song", "Mock Audio", "Digital Melody",
            "Virtual Sound", "Example Music", "Demo Track", "Test Case Audio"
        };

        /// <summary>
        /// Common genre names for test data.
        /// </summary>
        private static readonly string[] Genres =
        {
            "Rock", "Pop", "Jazz", "Classical", "Electronic", "Hip-Hop", "Blues", "Country"
        };

        /// <summary>
        /// Creates a mock streaming artist with realistic data.
        /// </summary>
        public static StreamingArtist CreateMockArtist(string id = null, string name = null)
        {
            var artistId = id ?? $"artist_{Random.Next(1000, 9999)}";
            var artistName = name ?? ArtistNames[Random.Next(ArtistNames.Length)];

            return new StreamingArtist
            {
                Id = artistId,
                Name = artistName,
                Biography = $"Biography for {artistName}. This is a test artist created for unit testing purposes.",
                Genres = GetRandomGenres(1, 3),
                Country = GetRandomCountry(),
                ImageUrls = new Dictionary<string, string>
                {
                    ["small"] = $"https://example.com/images/{artistId}_small.jpg",
                    ["medium"] = $"https://example.com/images/{artistId}_medium.jpg",
                    ["large"] = $"https://example.com/images/{artistId}_large.jpg"
                },
                ExternalUrls = new Dictionary<string, string>
                {
                    ["website"] = $"https://example.com/artist/{artistId}",
                    ["spotify"] = $"https://open.spotify.com/artist/{artistId}"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["testArtist"] = true,
                    ["mockId"] = artistId
                }
            };
        }

        /// <summary>
        /// Creates a mock streaming album with realistic data.
        /// </summary>
        public static StreamingAlbum CreateMockAlbum(string id = null, string title = null, StreamingArtist artist = null)
        {
            var albumId = id ?? $"album_{Random.Next(1000, 9999)}";
            var albumTitle = title ?? AlbumTitles[Random.Next(AlbumTitles.Length)];
            var albumArtist = artist ?? CreateMockArtist();

            return new StreamingAlbum
            {
                Id = albumId,
                Title = albumTitle,
                Artist = albumArtist,
                ReleaseDate = GetRandomReleaseDate(),
                Type = GetRandomAlbumType(),
                TrackCount = Random.Next(8, 15),
                Duration = TimeSpan.FromMinutes(Random.Next(30, 80)),
                Genres = GetRandomGenres(1, 2),
                Label = $"Test Records {Random.Next(1, 10)}",
                Upc = GenerateRandomUpc(),
                AvailableQualities = CreateMockQualities(),
                CoverArtUrls = new Dictionary<string, string>
                {
                    ["small"] = $"https://example.com/covers/{albumId}_150.jpg",
                    ["medium"] = $"https://example.com/covers/{albumId}_300.jpg", 
                    ["large"] = $"https://example.com/covers/{albumId}_600.jpg"
                },
                ExternalUrls = new Dictionary<string, string>
                {
                    ["service"] = $"https://example.com/album/{albumId}",
                    ["allmusic"] = $"https://allmusic.com/album/{albumId}"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["testAlbum"] = true,
                    ["mockId"] = albumId
                }
            };
        }

        /// <summary>
        /// Creates a mock streaming track with realistic data.
        /// </summary>
        public static StreamingTrack CreateMockTrack(string id = null, string title = null, StreamingAlbum album = null, int? trackNumber = null)
        {
            var trackId = id ?? $"track_{Random.Next(10000, 99999)}";
            var trackTitle = title ?? TrackTitles[Random.Next(TrackTitles.Length)];
            var trackAlbum = album ?? CreateMockAlbum();
            var trackNum = trackNumber ?? Random.Next(1, 12);

            return new StreamingTrack
            {
                Id = trackId,
                Title = trackTitle,
                Artist = trackAlbum.Artist,
                Album = trackAlbum,
                TrackNumber = trackNum,
                DiscNumber = 1,
                Duration = TimeSpan.FromSeconds(Random.Next(120, 360)),
                IsExplicit = Random.NextDouble() < 0.2, // 20% chance
                Isrc = GenerateRandomIsrc(),
                FeaturedArtists = Random.NextDouble() < 0.3 ? new List<StreamingArtist> { CreateMockArtist() } : new List<StreamingArtist>(),
                AvailableQualities = CreateMockQualities(),
                PreviewUrl = $"https://example.com/preview/{trackId}.mp3",
                Popularity = Random.Next(0, 100),
                Metadata = new Dictionary<string, object>
                {
                    ["testTrack"] = true,
                    ["mockId"] = trackId
                }
            };
        }

        /// <summary>
        /// Creates a mock streaming quality with realistic specifications.
        /// </summary>
        public static StreamingQuality CreateMockQuality(string id = null, string format = null, int? bitrate = null, int? sampleRate = null, int? bitDepth = null)
        {
            var qualityId = id ?? $"quality_{Random.Next(1, 10)}";
            
            // If no format specified, choose randomly
            if (format == null)
            {
                var formats = new[] { "MP3", "FLAC", "AAC" };
                format = formats[Random.Next(formats.Length)];
            }

            var quality = new StreamingQuality
            {
                Id = qualityId,
                Format = format
            };

            // Set realistic specs based on format
            if (format.Equals("MP3", StringComparison.OrdinalIgnoreCase))
            {
                quality.Bitrate = bitrate ?? (Random.NextDouble() < 0.5 ? 320 : 256);
                quality.Name = $"MP3 {quality.Bitrate}kbps";
            }
            else if (format.Equals("FLAC", StringComparison.OrdinalIgnoreCase))
            {
                quality.SampleRate = sampleRate ?? (Random.NextDouble() < 0.7 ? 44100 : 96000);
                quality.BitDepth = bitDepth ?? (quality.SampleRate > 44100 ? 24 : 16);
                quality.Name = $"FLAC {quality.SampleRate / 1000.0:F1}kHz/{quality.BitDepth}bit";
            }
            else if (format.Equals("AAC", StringComparison.OrdinalIgnoreCase))
            {
                quality.Bitrate = bitrate ?? 256;
                quality.Name = $"AAC {quality.Bitrate}kbps";
            }

            return quality;
        }

        /// <summary>
        /// Creates a list of mock qualities representing typical streaming service offerings.
        /// </summary>
        public static List<StreamingQuality> CreateMockQualities()
        {
            return new List<StreamingQuality>
            {
                CreateMockQuality("1", "MP3", 320),
                CreateMockQuality("2", "FLAC", null, 44100, 16),
                CreateMockQuality("3", "FLAC", null, 96000, 24)
            };
        }

        /// <summary>
        /// Creates a mock album with a full track listing.
        /// </summary>
        public static StreamingAlbum CreateMockAlbumWithTracks(int trackCount = 10)
        {
            var album = CreateMockAlbum();
            var tracks = new List<StreamingTrack>();

            for (int i = 1; i <= trackCount; i++)
            {
                var track = CreateMockTrack(trackNumber: i, album: album);
                tracks.Add(track);
            }

            album.TrackCount = trackCount;
            album.Duration = TimeSpan.FromSeconds(tracks.Sum(t => t.Duration?.TotalSeconds ?? 0));

            return album;
        }

        /// <summary>
        /// Creates a mock streaming settings object for testing.
        /// </summary>
        public static TSettings CreateMockSettings<TSettings>() where TSettings : BaseStreamingSettings, new()
        {
            return new TSettings
            {
                BaseUrl = "https://api.example-streaming.com",
                Email = "test@example.com",
                Password = "test_password",
                CountryCode = "US",
                SearchLimit = 50,
                IncludeSingles = true,
                IncludeCompilations = false,
                ApiRateLimit = 60,
                SearchCacheDuration = 5,
                ConnectionTimeout = 30,
                EarlyReleaseDayLimit = 0
            };
        }

        /// <summary>
        /// Creates a collection of mock search results.
        /// </summary>
        public static IEnumerable<StreamingSearchResult> CreateMockSearchResults(int count = 5)
        {
            var results = new List<StreamingSearchResult>();

            for (int i = 0; i < count; i++)
            {
                var album = CreateMockAlbum();
                results.Add(new StreamingSearchResult
                {
                    Id = album.Id,
                    Title = album.Title,
                    Artist = album.GetPrimaryArtistName(),
                    Album = album.Title,
                    Type = StreamingSearchType.Album,
                    ReleaseDate = album.ReleaseDate,
                    Genre = album.Genres?.FirstOrDefault(),
                    Label = album.Label,
                    CoverArtUrl = album.GetBestCoverArtUrl(),
                    TrackCount = album.TrackCount,
                    Duration = album.Duration
                });
            }

            return results;
        }

        // Helper methods
        private static List<string> GetRandomGenres(int min, int max)
        {
            var count = Random.Next(min, max + 1);
            return Genres.OrderBy(x => Random.Next()).Take(count).ToList();
        }

        private static string GetRandomCountry()
        {
            var countries = new[] { "US", "GB", "FR", "DE", "JP", "CA", "AU", "BR" };
            return countries[Random.Next(countries.Length)];
        }

        private static DateTime? GetRandomReleaseDate()
        {
            var start = new DateTime(1950, 1, 1);
            var end = DateTime.Now;
            var range = end - start;
            return start.AddDays(Random.NextDouble() * range.TotalDays);
        }

        private static StreamingAlbumType GetRandomAlbumType()
        {
            var types = Enum.GetValues<StreamingAlbumType>();
            return types[Random.Next(types.Length)];
        }

        private static string GenerateRandomUpc()
        {
            return Random.Next(100000000, 999999999).ToString() + Random.Next(1000, 9999).ToString();
        }

        private static string GenerateRandomIsrc()
        {
            var countryCode = GetRandomCountry();
            var registrant = Random.Next(100, 999).ToString();
            var year = Random.Next(50, 99).ToString();
            var designation = Random.Next(10000, 99999).ToString();
            return $"{countryCode}{registrant}{year}{designation}";
        }
    }

    /// <summary>
    /// Test data sets for common streaming service testing scenarios.
    /// </summary>
    public static class TestDataSets
    {
        /// <summary>
        /// Creates a realistic jazz album for testing.
        /// </summary>
        public static StreamingAlbum CreateJazzAlbum()
        {
            var artist = MockFactories.CreateMockArtist("jazz_artist_1", "Miles Davis Test");
            var album = MockFactories.CreateMockAlbum("jazz_album_1", "Kind of Blue (Test)", artist);
            
            album.Genres = new List<string> { "Jazz" };
            album.ReleaseDate = new DateTime(1959, 8, 17);
            album.TrackCount = 5;
            album.Duration = TimeSpan.FromMinutes(45);
            
            return album;
        }

        /// <summary>
        /// Creates a high-resolution classical album for quality testing.
        /// </summary>
        public static StreamingAlbum CreateClassicalHiResAlbum()
        {
            var artist = MockFactories.CreateMockArtist("classical_artist_1", "Berlin Philharmonic Test");
            var album = MockFactories.CreateMockAlbum("classical_album_1", "Symphony No. 9 (Test)", artist);
            
            album.Genres = new List<string> { "Classical" };
            album.TrackCount = 4;
            album.Duration = TimeSpan.FromMinutes(65);
            album.AvailableQualities = new List<StreamingQuality>
            {
                MockFactories.CreateMockQuality("hires_1", "FLAC", null, 192000, 24),
                MockFactories.CreateMockQuality("hires_2", "FLAC", null, 96000, 24),
                MockFactories.CreateMockQuality("cd_quality", "FLAC", null, 44100, 16)
            };
            
            return album;
        }

        /// <summary>
        /// Creates an edge case album with special characters for testing file naming.
        /// </summary>
        public static StreamingAlbum CreateEdgeCaseAlbum()
        {
            var artist = MockFactories.CreateMockArtist("edge_artist_1", "Test Artist: With/Special\\Characters?");
            var album = MockFactories.CreateMockAlbum("edge_album_1", "Album with \"Quotes\" & <Symbols>", artist);
            
            return album;
        }
    }
}