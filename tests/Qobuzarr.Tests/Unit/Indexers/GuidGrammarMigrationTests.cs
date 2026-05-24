using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using Qobuzarr.Tests.Builders;
using Xunit;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Wave 2B: Verifies that QobuzParser now emits Common-grammar GUIDs and that
    /// QobuzDownloadClient (via AlbumIdExtractor) accepts BOTH the new format AND the
    /// legacy dash format for backward compatibility.
    /// </summary>
    public class GuidGrammarMigrationTests
    {
        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static QobuzParser CreateParser(bool includeSingles = true, bool includeCompilations = true)
        {
            var settings = new QobuzIndexerSettings
            {
                IncludeSingles = includeSingles,
                IncludeCompilations = includeCompilations
            };
            return new QobuzParser(settings, LogManager.GetCurrentClassLogger());
        }

        /// <summary>
        /// Wraps a serialized QobuzAlbum in an album-search response envelope
        /// and creates an IndexerResponse the parser can consume.
        /// </summary>
        private static IndexerResponse BuildIndexerResponse(QobuzAlbum album)
        {
            var envelope = new
            {
                albums = new
                {
                    items = new[] { album },
                    total = 1,
                    offset = 0,
                    limit = 10
                }
            };
            var json = JsonConvert.SerializeObject(envelope);
            var httpResponse = new HttpResponse(
                new HttpRequest("http://qobuz-test.invalid/album/search?query=test"),
                new HttpHeader(),
                json,
                HttpStatusCode.OK);
            var indexerRequest = new IndexerRequest("http://qobuz-test.invalid/album/search?query=test", HttpAccept.Json);
            return new IndexerResponse(indexerRequest, httpResponse);
        }

        // ------------------------------------------------------------------
        // 1. Indexer emits new GUID grammar
        // ------------------------------------------------------------------

        [Fact]
        public void Indexer_EmitsNewGuidGrammar()
        {
            var album = QobuzAlbumBuilder.New()
                .WithId("0060254788359")
                .WithTitle("Kind of Blue")
                .WithArtist("Miles Davis")
                .AsCdQualityFlac()
                .AsFullAlbum()
                .Build();

            var parser = CreateParser();
            var response = BuildIndexerResponse(album);
            var releases = parser.ParseResponse(response);

            releases.Should().NotBeEmpty();

            // Every GUID must match the new Common grammar: qobuz:album:{id}[...segments...]
            foreach (var release in releases)
            {
                release.Guid.Should().MatchRegex(
                    @"^qobuz:album:[^:]+",
                    because: "new GUID grammar must start with qobuz:album:{id}");

                // PrefixedReleaseGuidParser must resolve the album ID correctly
                var parsed = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, "qobuz");
                parsed.Should().Be("0060254788359",
                    because: "parser must extract the exact album ID from the new GUID");
            }
        }

        // ------------------------------------------------------------------
        // 2. Indexer emits new DownloadUrl grammar
        // ------------------------------------------------------------------

        [Fact]
        public void Indexer_EmitsNewDownloadUrlGrammar()
        {
            var album = QobuzAlbumBuilder.New()
                .WithId("0060254788359")
                .WithTitle("Kind of Blue")
                .WithArtist("Miles Davis")
                .AsCdQualityFlac()
                .AsFullAlbum()
                .Build();

            var parser = CreateParser();
            var response = BuildIndexerResponse(album);
            var releases = parser.ParseResponse(response);

            releases.Should().NotBeEmpty();

            // DownloadUrl must be the query-string form (not legacy path-segment form)
            foreach (var release in releases)
            {
                release.DownloadUrl.Should().StartWith("qobuz://album/0060254788359?quality=",
                    because: "new URL form puts quality in query string, not path segment");
                release.DownloadUrl.Should().NotContain(
                    "0060254788359/",
                    because: "legacy path-segment form should no longer be emitted");
            }
        }

        // ------------------------------------------------------------------
        // 3. DownloadClient side: parse new GUID
        // ------------------------------------------------------------------

        [Fact]
        public void DownloadClient_ParsesNewGuid()
        {
            // Simulate what QobuzDownloadClient.ExtractAlbumIdFromRelease does:
            // first try PrefixedReleaseGuidParser, then fall back to AlbumIdExtractor.
            var release = new ReleaseInfo
            {
                Guid = "qobuz:album:0060254788359:quality=6",
                DownloadUrl = "qobuz://album/0060254788359?quality=6"
            };

            // Primary path (PrefixedReleaseGuidParser)
            var fromGuid = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, "qobuz");
            fromGuid.Should().Be("0060254788359");

            // AlbumIdExtractor also handles it
            var viaExtractor = AlbumIdExtractor.ExtractAlbumId(release);
            viaExtractor.Should().Be("0060254788359");
        }

        // ------------------------------------------------------------------
        // 4. Legacy GUID fallback path still extracts album ID
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("qobuz-0060254788359-5", "0060254788359")]      // legacy with quality
        [InlineData("qobuz-0060254788359-27", "0060254788359")]     // hi-res quality
        [InlineData("qobuz-0060254788359", "0060254788359")]        // legacy without quality
        [InlineData("qobuz-abc123def456-6", "abc123def456")]        // alphanumeric id
        [InlineData("qobuz-0060254788359-deluxe-edition-6", "0060254788359-deluxe-edition")]  // with edition in old format
        public void DownloadClient_ParsesLegacyGuid_FallbackPath(string legacyGuid, string expectedId)
        {
            var release = new ReleaseInfo { Guid = legacyGuid };

            // New parser must NOT match (that's what makes it a fallback scenario)
            var fromNewParser = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(legacyGuid, "qobuz");
            fromNewParser.Should().BeNull(
                because: "PrefixedReleaseGuidParser should not match legacy dash-format GUIDs");

            // AlbumIdExtractor.ExtractAlbumId must still resolve it
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);
            albumId.Should().Be(expectedId,
                because: "legacy GUID fallback must continue to work for in-flight downloads");
        }

        // ------------------------------------------------------------------
        // 5. Legacy GUID fallback is observable via the Obsolete marker
        //    (compile-time verifiable — the method is [Obsolete])
        // ------------------------------------------------------------------

        [Fact]
        public void DownloadClient_LegacyGuidFallback_MarkedObsolete()
        {
            // Verify the fallback method carries the [Obsolete] attribute so operators
            // and future developers see a clear migration signal.
            var method = typeof(AlbumIdExtractor).GetMethod(
                "ExtractAlbumIdFromLegacyGuid",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            method.Should().NotBeNull("ExtractAlbumIdFromLegacyGuid must remain public for tests and traceability");

            var obsoleteAttr = method!.GetCustomAttributes(typeof(System.ObsoleteAttribute), false);
            obsoleteAttr.Should().NotBeEmpty(
                because: "ExtractAlbumIdFromLegacyGuid must be marked [Obsolete] to signal the migration drain");

            var msg = ((System.ObsoleteAttribute)obsoleteAttr[0]).Message;
            msg.Should().Contain("PrefixedReleaseGuidParser",
                because: "Obsolete message must point developers toward the Common parser");
        }

        // ------------------------------------------------------------------
        // 6. Quality survives emission → parsing round-trip
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(5, "5")]    // MP3-320
        [InlineData(6, "6")]    // FLAC-CD
        [InlineData(7, "7")]    // FLAC 24/96
        [InlineData(27, "27")]  // FLAC 24/192
        public void Indexer_QualityPreservation_RoundTrip(int qualityId, string expectedQualityStr)
        {
            var album = QobuzAlbumBuilder.New()
                .WithId("testalbum001")
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .AsHiResFlac()
                .AsFullAlbum()
                .Build();

            var parser = CreateParser();
            var response = BuildIndexerResponse(album);
            var releases = parser.ParseResponse(response);

            // Find the release matching the quality
            var release = releases.FirstOrDefault(r =>
                r.Guid.Contains($":quality={expectedQualityStr}", System.StringComparison.Ordinal));

            release.Should().NotBeNull(
                because: $"parser should emit a release with quality={expectedQualityStr} in the GUID");

            // Quality also visible in the DownloadUrl
            release!.DownloadUrl.Should().Contain(
                $"?quality={expectedQualityStr}",
                because: "DownloadUrl must encode quality as a query parameter");

            // Album ID is still parseable after quality is present in GUID
            var albumId = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, "qobuz");
            albumId.Should().Be("testalbum001",
                because: "quality extra-segment must not affect album ID extraction");
        }

        // ------------------------------------------------------------------
        // 7. Edition in GUID survives round-trip (album with Version field)
        // ------------------------------------------------------------------

        [Fact]
        public void Indexer_EditionPreservation_RoundTrip()
        {
            var album = QobuzAlbumBuilder.New()
                .WithId("0060254788359")
                .WithTitle("Kind of Blue")
                .WithVersion("Deluxe Edition")
                .WithArtist("Miles Davis")
                .AsCdQualityFlac()
                .AsFullAlbum()
                .Build();

            var parser = CreateParser();
            var response = BuildIndexerResponse(album);
            var releases = parser.ParseResponse(response);

            releases.Should().NotBeEmpty();

            // All releases should have the edition segment in the GUID
            foreach (var release in releases)
            {
                release.Guid.Should().Contain(":edition=deluxe-edition",
                    because: "edition/version must be embedded in the GUID as a colon segment");

                // Album ID extraction still works despite the edition segment
                var albumId = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, "qobuz");
                albumId.Should().Be("0060254788359",
                    because: "edition segment must not interfere with album ID extraction");
            }
        }

        // ------------------------------------------------------------------
        // 8. Legacy URL fallback (path-segment quality) still works
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("qobuz://album/0060254788359/5", "0060254788359")]
        [InlineData("qobuz://album/1234567890123/27", "1234567890123")]
        [InlineData("qobuz://album/abc123/6", "abc123")]
        public void AlbumIdExtractor_ParsesLegacyUrlWithPathSegmentQuality(string legacyUrl, string expectedId)
        {
            var release = new ReleaseInfo { DownloadUrl = legacyUrl };
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);
            albumId.Should().Be(expectedId,
                because: "legacy path-segment quality URL must still resolve the album ID");
        }

        // ------------------------------------------------------------------
        // 9. New URL format (query-string quality) is parsed correctly
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("qobuz://album/0060254788359?quality=5", "0060254788359")]
        [InlineData("qobuz://album/1234567890123?quality=27", "1234567890123")]
        [InlineData("qobuz://album/abc123?quality=6", "abc123")]
        public void AlbumIdExtractor_ParsesNewUrlWithQueryStringQuality(string newUrl, string expectedId)
        {
            var release = new ReleaseInfo { DownloadUrl = newUrl };
            var albumId = AlbumIdExtractor.ExtractAlbumId(release);
            albumId.Should().Be(expectedId,
                because: "new query-string quality URL must resolve the album ID");
        }
    }
}
