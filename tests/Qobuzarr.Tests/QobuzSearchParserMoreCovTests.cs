using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Newtonsoft.Json;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Http;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Additional coverage tests for QobuzParser - covering edge cases not in QobuzSearchParserCovTests.
    /// Target: src/Indexers/QobuzParser.cs
    ///
    /// Uncovered paths tested:
    /// - IsLikelyCompilation with "compilation" keyword (line 481)
    /// - IsLikelyCompilation with "best of" keyword (line 481)
    /// - IsLikelyCompilation with "collection" keyword (line 481)
    /// - CalculateRelevanceScore with default PublishDate (line 567)
    /// </summary>
    public class QobuzSearchParserMoreCovTests
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public QobuzSearchParserMoreCovTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            _logger = LogManager.GetCurrentClassLogger();
        }

        #region Compilation Filtering - Additional Keywords

        /// <summary>
        /// Albums with "compilation" in title should be filtered when IncludeCompilations is false.
        /// Source: QobuzParser.cs line 481 - "compilation" keyword
        /// </summary>
        [Fact]
        public void ParseResponse_WithCompilationKeyword_ShouldFilterAsCompilation()
        {
            // Arrange
            var settingsNoComps = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = false
            };
            var parser = new QobuzParser(settingsNoComps, _logger);

            var compilationAlbum = new QobuzAlbumBuilder()
                .WithId("compkw123")
                .WithTitle("Jazz Compilation Vol. 1")
                .WithArtist("Various Artists", "various")
                .Build();

            // Act
            var releases = ParseAlbum(parser, compilationAlbum);

            // Assert - Source: QobuzParser.cs line 481
            releases.Should().BeEmpty("'compilation' keyword should filter as compilation");
        }

        /// <summary>
        /// Albums with "best of" in title should be filtered when IncludeCompilations is false.
        /// Source: QobuzParser.cs line 481 - "best of" keyword
        /// </summary>
        [Fact]
        public void ParseResponse_WithBestOfKeyword_ShouldFilterAsCompilation()
        {
            // Arrange
            var settingsNoComps = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = false
            };
            var parser = new QobuzParser(settingsNoComps, _logger);

            var bestOfAlbum = new QobuzAlbumBuilder()
                .WithId("bestof123")
                .WithTitle("Best of the 80s")
                .WithArtist("Various Artists", "various")
                .Build();

            // Act
            var releases = ParseAlbum(parser, bestOfAlbum);

            // Assert - Source: QobuzParser.cs line 481
            releases.Should().BeEmpty("'best of' keyword should filter as compilation");
        }

        /// <summary>
        /// Albums with "collection" in title should be filtered when IncludeCompilations is false.
        /// Source: QobuzParser.cs line 481 - "collection" keyword
        /// </summary>
        [Fact]
        public void ParseResponse_WithCollectionKeyword_ShouldFilterAsCompilation()
        {
            // Arrange
            var settingsNoComps = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = false
            };
            var parser = new QobuzParser(settingsNoComps, _logger);

            var collectionAlbum = new QobuzAlbumBuilder()
                .WithId("collect123")
                .WithTitle("The Collection")
                .WithArtist("Various Artists", "various")
                .Build();

            // Act
            var releases = ParseAlbum(parser, collectionAlbum);

            // Assert - Source: QobuzParser.cs line 481
            releases.Should().BeEmpty("'collection' keyword should filter as compilation");
        }

        /// <summary>
        /// Albums with "collection" should pass when IncludeCompilations is true.
        /// Source: QobuzParser.cs line 462-467
        /// </summary>
        [Fact]
        public void ParseResponse_WithCollectionKeyword_WhenIncluded_ShouldNotFilter()
        {
            // Arrange
            var settingsWithComps = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            var parser = new QobuzParser(settingsWithComps, _logger);

            var collectionAlbum = new QobuzAlbumBuilder()
                .WithId("collectpass123")
                .WithTitle("The Collection")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, collectionAlbum);

            // Assert
            releases.Should().NotBeEmpty("Collection albums should pass when IncludeCompilations is true");
        }

        #endregion

        #region CalculateRelevanceScore - Default PublishDate

        /// <summary>
        /// CalculateRelevanceScore with default PublishDate should not add recency bonus.
        /// Source: QobuzParser.cs line 567 - "PublishDate != default" check
        /// </summary>
        [Fact]
        public void CalculateRelevanceScore_WithDefaultPublishDate_ShouldNotAddRecencyBonus()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var release = new ReleaseInfo
            {
                Title = "UniqueDefaultDateTitle",
                Artist = "Artist",
                Album = "DifferentAlbum",
                PublishDate = default(DateTime) // Line 567: PublishDate != default check
            };

            // Act
            var score = parser.CalculateRelevanceScore(release, "UniqueDefaultDateTitle");

            // Assert - Source: QobuzParser.cs line 567
            // Title match: +100, No recency bonus for default date
            score.Should().Be(100, "Default PublishDate should not add recency bonus");
        }

        /// <summary>
        /// Verify recency bonus is added for recent releases with valid PublishDate.
        /// Source: QobuzParser.cs line 567-569
        /// </summary>
        [Fact]
        public void CalculateRelevanceScore_WithValidRecentPublishDate_ShouldAddRecencyBonus()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var recentDate = DateTime.Now.AddMonths(-6); // Within 2 years
            var release = new ReleaseInfo
            {
                Title = "UniqueRecentValidTitle",
                Artist = "Artist",
                Album = "DifferentAlbum",
                PublishDate = recentDate
            };

            // Act
            var score = parser.CalculateRelevanceScore(release, "UniqueRecentValidTitle");

            // Assert - Source: QobuzParser.cs line 567-569
            // Title match: +100, Recent bonus: +5 = 105
            score.Should().Be(105, "Valid recent PublishDate should add 5 bonus points");
        }

        #endregion

        #region Helper Methods

        private List<ReleaseInfo> ParseAlbum(QobuzParser parser, QobuzAlbum album)
        {
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = new List<QobuzAlbum> { album }
                }
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("http://test.qobuz.com/api"),
                new HttpHeader(),
                JsonConvert.SerializeObject(albumSearchResponse),
                HttpStatusCode.OK
            );

            var response = new IndexerResponse(
                new IndexerRequest("http://test.qobuz.com/api", new HttpAccept("application/json")),
                httpResponse
            );

            return parser.ParseResponse(response).ToList();
        }

        #endregion
    }
}
