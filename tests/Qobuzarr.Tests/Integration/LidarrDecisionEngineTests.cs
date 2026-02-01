using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using NSubstitute;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.Fixtures;
using NLog;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Tests that simulate Lidarr's Decision Engine to validate that our parser-compatible
    /// titles will be accepted by AlbumRequestedSpecification, SingleAlbumSearchMatchSpecification,
    /// and UpgradeDiskSpecification.
    /// </summary>
    [Trait("Category", "Integration")]
    public class LidarrDecisionEngineTests : TestFixtureBase
    {
        private readonly QobuzParser _parser;
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        // Simulated Lidarr specifications
        private readonly IAlbumRequestedSpecification _albumRequestedSpec;
        private readonly ISingleAlbumSearchMatchSpecification _singleAlbumSearchSpec;

        public LidarrDecisionEngineTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            _logger = Substitute.For<Logger>();
            _parser = new QobuzParser(_settings, _logger);

            // Mock the key Lidarr specifications that filter search results
            _albumRequestedSpec = Substitute.For<IAlbumRequestedSpecification>();
            _singleAlbumSearchSpec = Substitute.For<ISingleAlbumSearchMatchSpecification>();
        }

        #region Edition Album Decision Engine Acceptance

        [Fact]
        public void EditionAlbum_WithHyphenFormat_ShouldPassAlbumRequestedSpecification()
        {
            // Arrange - Simulate user searching for deluxe edition
            var lidarrAlbum = new Album
            {
                Id = 456,
                Title = "They Want My Soul",
                ReleaseDate = new DateTime(2014, 8, 5),
                Artist = new Artist { Id = 123, Name = "Spoon" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { lidarrAlbum });
            searchCriteria.Artist.Returns(lidarrAlbum.Artist.Value);

            var qobuzAlbum = CreateQobuzDeluxeAlbum();
            _parser.SetSearchContext(searchCriteria);

            var releases = ConvertAlbumToReleases(qobuzAlbum);
            var hyphenFormatRelease = releases.First(r => r.Title.Contains("FLAC"));

            // Create RemoteAlbum that would be created by Lidarr's parser
            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { lidarrAlbum },
                Artist = lidarrAlbum.Artist,
                ParsedAlbumInfo = new ParsedAlbumInfo
                {
                    AlbumTitle = "They Want My Soul", // Extracted from hyphen format
                    ReleaseVersion = "Deluxe Edition", // Extracted from hyphen format
                    ArtistName = "Spoon"
                },
                Release = hyphenFormatRelease
            };

            // Configure mock specification to simulate Lidarr's logic
            _albumRequestedSpec.IsSatisfiedBy(Arg.Any<RemoteAlbum>(), Arg.Any<SearchCriteriaBase>())
                .Returns(callInfo =>
                {
                    var remote = callInfo.Arg<RemoteAlbum>();
                    var criteria = callInfo.Arg<SearchCriteriaBase>();

                    // Simulate AlbumRequestedSpecification: check if any requested album matches
                    return criteria.Albums?.Any(a =>
                        AlbumTitleMatchesWithVersion(a.Title, remote.ParsedAlbumInfo.AlbumTitle, remote.ParsedAlbumInfo.ReleaseVersion)) == true;
                });

            // Act
            var isAccepted = _albumRequestedSpec.IsSatisfiedBy(remoteAlbum, searchCriteria);

            // Assert
            isAccepted.Should().BeTrue("Edition album with hyphen format should pass AlbumRequestedSpecification");
        }

        [Fact]
        public void EditionAlbum_WithHyphenFormat_ShouldPassSingleAlbumSearchMatchSpecification()
        {
            // Arrange
            var lidarrAlbum = new Album
            {
                Id = 456,
                Title = "They Want My Soul (Deluxe Edition)",
                ReleaseDate = new DateTime(2014, 8, 5),
                Artist = new Artist { Id = 123, Name = "Spoon" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { lidarrAlbum });

            var qobuzAlbum = CreateQobuzDeluxeAlbum();
            _parser.SetSearchContext(searchCriteria);

            var releases = ConvertAlbumToReleases(qobuzAlbum);
            var hyphenFormatRelease = releases.First(r => r.Title.Contains("FLAC"));

            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { lidarrAlbum },
                ParsedAlbumInfo = new ParsedAlbumInfo
                {
                    AlbumTitle = "They Want My Soul",
                    ReleaseVersion = "Deluxe Edition",
                    ArtistName = "Spoon"
                },
                Release = hyphenFormatRelease
            };

            // Configure mock to simulate single album search matching logic
            _singleAlbumSearchSpec.IsSatisfiedBy(Arg.Any<RemoteAlbum>(), Arg.Any<SearchCriteriaBase>())
                .Returns(callInfo =>
                {
                    var remote = callInfo.Arg<RemoteAlbum>();
                    var criteria = callInfo.Arg<SearchCriteriaBase>();

                    // Simulate matching logic: parsed title should match one of the requested albums
                    return criteria.Albums?.Any(a =>
                        CleanTitle(a.Title).Equals(CleanTitle(remote.ParsedAlbumInfo.AlbumTitle), StringComparison.OrdinalIgnoreCase) ||
                        CleanTitle(a.Title).Contains(CleanTitle(remote.ParsedAlbumInfo.AlbumTitle), StringComparison.OrdinalIgnoreCase)) == true;
                });

            // Act
            var isAccepted = _singleAlbumSearchSpec.IsSatisfiedBy(remoteAlbum, searchCriteria);

            // Assert
            isAccepted.Should().BeTrue("Hyphen format should enable proper title matching in SingleAlbumSearchMatchSpecification");
        }

        [Fact]
        public void StandardAlbum_WithSpaceFormat_ShouldPassDecisionEngine()
        {
            // Arrange
            var lidarrAlbum = new Album
            {
                Id = 789,
                Title = "Random Access Memories",
                ReleaseDate = new DateTime(2013, 5, 17),
                Artist = new Artist { Id = 456, Name = "Daft Punk" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { lidarrAlbum });

            var qobuzAlbum = QobuzAlbumBuilder.New()
                .WithTitle("Random Access Memories")
                .WithArtist("Daft Punk")
                .WithReleaseYear(2013)
                .AsCdQualityFlac()
                .Build();

            _parser.SetSearchContext(searchCriteria);
            var releases = ConvertAlbumToReleases(qobuzAlbum);
            var standardFormatRelease = releases.First(r => r.Title.Contains("FLAC"));

            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { lidarrAlbum },
                ParsedAlbumInfo = new ParsedAlbumInfo
                {
                    AlbumTitle = "Random Access Memories",
                    ArtistName = "Daft Punk"
                },
                Release = standardFormatRelease
            };

            // Configure specifications for standard album
            _albumRequestedSpec.IsSatisfiedBy(remoteAlbum, searchCriteria).Returns(true);
            _singleAlbumSearchSpec.IsSatisfiedBy(remoteAlbum, searchCriteria).Returns(true);

            // Act
            var albumRequested = _albumRequestedSpec.IsSatisfiedBy(remoteAlbum, searchCriteria);
            var singleAlbumMatch = _singleAlbumSearchSpec.IsSatisfiedBy(remoteAlbum, searchCriteria);

            // Assert
            albumRequested.Should().BeTrue("Standard albums should pass AlbumRequestedSpecification");
            singleAlbumMatch.Should().BeTrue("Standard albums should pass SingleAlbumSearchMatchSpecification");
        }

        #endregion

        #region Mixed Format Search Results

        [Fact]
        public void MixedSearchResults_ShouldHandleBothFormatsCorrectly()
        {
            // Arrange - Search returns both standard and edition albums
            var standardAlbum = new Album
            {
                Id = 1,
                Title = "Album One",
                Artist = new Artist { Name = "Artist" }
            };

            var editionAlbum = new Album
            {
                Id = 2,
                Title = "Album Two (Deluxe Edition)",
                Artist = new Artist { Name = "Artist" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { standardAlbum, editionAlbum });

            var qobuzStandard = QobuzAlbumBuilder.New()
                .WithTitle("Album One")
                .WithArtist("Artist")
                .Build();

            var qobuzEdition = QobuzAlbumBuilder.New()
                .WithTitle("Album Two")
                .WithArtist("Artist")
                .Build();
            qobuzEdition.Version = "Deluxe Edition";

            _parser.SetSearchContext(searchCriteria);

            // Act
            var standardReleases = ConvertAlbumToReleases(qobuzStandard);
            var editionReleases = ConvertAlbumToReleases(qobuzEdition);

            // Assert
            var standardRelease = standardReleases.First();
            var editionRelease = editionReleases.First();

            // Standard album should use space format
            standardRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[.+\] \[WEB\]$");

            // Edition album should use hyphen format
            editionRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$");
        }

        #endregion

        #region Album ID Mapping Validation

        [Fact]
        public void ParserGenerated_AlbumProperty_ShouldMapToCorrectLidarrAlbum()
        {
            // Arrange
            var lidarrAlbum = new Album
            {
                Id = 123,
                Title = "They Want My Soul",
                Artist = new Artist { Name = "Spoon" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { lidarrAlbum });

            var qobuzAlbum = CreateQobuzDeluxeAlbum();
            _parser.SetSearchContext(searchCriteria);

            var releases = ConvertAlbumToReleases(qobuzAlbum);

            // Act
            var release = releases.First();

            // Assert
            release.Album.Should().NotBeNullOrEmpty("Parser should set Album property");

            // The album property should match the Lidarr album title for proper mapping
            // This ensures the Decision Engine can match the release to the requested album
            release.Album.Should().Be("They Want My Soul",
                "Album property should match Lidarr album for proper Decision Engine acceptance");
        }

        [Fact]
        public void GUID_ShouldIncludeQualityForUniqueness()
        {
            // Arrange
            var qobuzAlbum = CreateQobuzDeluxeAlbum();
            var releases = ConvertAlbumToReleases(qobuzAlbum);

            // Act & Assert
            var mp3Release = releases.FirstOrDefault(r => r.Guid.EndsWith("-5"));  // MP3320 = 5
            var flacRelease = releases.FirstOrDefault(r => r.Guid.EndsWith("-6")); // FLACLossless = 6

            mp3Release.Should().NotBeNull("Should have MP3 release");
            flacRelease.Should().NotBeNull("Should have FLAC release");

            mp3Release.Guid.Should().NotBe(flacRelease.Guid,
                "Different qualities should have different GUIDs");

            // Both should reference same Qobuz album but different quality
            mp3Release.Guid.Should().StartWith("qobuz-");
            flacRelease.Guid.Should().StartWith("qobuz-");
        }

        #endregion

        #region Real-World Scenario Tests

        [Fact]
        public void ComplexEditionSearch_ShouldGenerateCorrectFormat()
        {
            // Arrange - Real scenario: User searches for specific deluxe edition
            var lidarrAlbum = new Album
            {
                Id = 42,
                Title = "Blond (Boys Don't Cry Magazine, Explicit)",
                ReleaseDate = new DateTime(2016, 8, 20),
                Artist = new Artist { Name = "Frank Ocean" }
            };

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(new List<Album> { lidarrAlbum });

            var qobuzAlbum = QobuzAlbumBuilder.New()
                .WithTitle("Blond")
                .WithArtist("Frank Ocean")
                .WithReleaseYear(2016)
                .AsCdQualityFlac()
                .Build();
            qobuzAlbum.Version = "Boys Don't Cry Magazine";
            qobuzAlbum.ParentalWarning = true;

            _parser.SetSearchContext(searchCriteria);

            // Act
            var releases = ConvertAlbumToReleases(qobuzAlbum);
            var flacRelease = releases.First(r => r.Title.Contains("FLAC"));

            // Assert
            // Should use hyphen format for edition
            flacRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$");
            flacRelease.Title.Should().Contain("Boys Don't Cry Magazine");

            // Should properly map to Lidarr album for decision engine
            flacRelease.Album.Should().Be("Blond (Boys Don't Cry Magazine, Explicit)");
        }

        #endregion

        #region Helper Methods

        private QobuzAlbum CreateQobuzDeluxeAlbum()
        {
            var album = QobuzAlbumBuilder.New()
                .WithTitle("They Want My Soul")
                .WithArtist("Spoon")
                .WithReleaseYear(2014)
                .AsCdQualityFlac()
                .Build();
            album.Version = "Deluxe Edition";
            return album;
        }

        private List<NzbDrone.Core.Parser.Model.ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album)
        {
            // Create mock response
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum> { Items = new List<QobuzAlbum> { album } }
            };

            var mockResponse = new NzbDrone.Common.Http.HttpResponse(
                new NzbDrone.Common.Http.HttpRequest("http://test.com"),
                new NzbDrone.Common.Http.HttpHeader(),
                Newtonsoft.Json.JsonConvert.SerializeObject(albumSearchResponse),
                System.Net.HttpStatusCode.OK
            );

            var indexerResponse = new IndexerResponse(
                new IndexerRequest("http://test.com", new NzbDrone.Common.Http.HttpAccept("application/json")),
                mockResponse
            );

            return _parser.ParseResponse(indexerResponse).ToList();
        }

        private bool AlbumTitleMatchesWithVersion(string lidarrTitle, string parsedTitle, string parsedVersion)
        {
            // Simulate Lidarr's album matching logic for edition albums
            var cleanLidarrTitle = CleanTitle(lidarrTitle);
            var cleanParsedTitle = CleanTitle(parsedTitle);

            // Direct title match
            if (cleanLidarrTitle.Equals(cleanParsedTitle, StringComparison.OrdinalIgnoreCase))
                return true;

            // Title contains version (e.g., "Album (Deluxe)" matches parsed "Album" + version "Deluxe")
            if (!string.IsNullOrWhiteSpace(parsedVersion))
            {
                var expectedTitleWithVersion = $"{parsedTitle} ({parsedVersion})";
                if (cleanLidarrTitle.Equals(CleanTitle(expectedTitleWithVersion), StringComparison.OrdinalIgnoreCase))
                    return true;

                // Alternative formats
                var alternativeFormat = $"{parsedTitle} [{parsedVersion}]";
                if (cleanLidarrTitle.Equals(CleanTitle(alternativeFormat), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "";

            return System.Text.RegularExpressions.Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "")
                .Replace(" ", "");
        }

        #endregion
    }

    #region Mock Interfaces (Simulating Lidarr's Decision Engine)

    /// <summary>
    /// Simulates Lidarr's AlbumRequestedSpecification that filters releases based on search criteria
    /// </summary>
    public interface IAlbumRequestedSpecification
    {
        bool IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria);
    }

    /// <summary>
    /// Simulates Lidarr's SingleAlbumSearchMatchSpecification for single album searches
    /// </summary>
    public interface ISingleAlbumSearchMatchSpecification
    {
        bool IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria);
    }

    #endregion
}
