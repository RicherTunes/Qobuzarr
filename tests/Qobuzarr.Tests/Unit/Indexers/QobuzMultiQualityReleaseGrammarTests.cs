using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Qobuzarr.Tests.Builders;
using Xunit;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Pins the REAL GUID grammar that <see cref="QobuzParser"/> emits through its full
    /// <see cref="QobuzParser.ParseResponse"/> path, and (in the multi-quality region) that it
    /// emits exactly one <see cref="ReleaseInfo"/> per available Qobuz quality tier.
    ///
    /// <para>Grammar (QobuzParser.cs:241): <c>qobuz:album:{id}[:edition={edition}]:quality={q}</c>
    /// — the qobuz variant of Common's <c>{scheme}:album:{albumId}</c> grammar
    /// (<see cref="AlbumReleaseInfoBuilder"/>) read back by <see cref="PrefixedReleaseGuidParser"/>.
    /// Existing <c>AlbumEditionGuidTests</c> only exercises a local reimplementation of the legacy
    /// dash format, so nothing pinned the GUID the parser actually emits today — this closes that gap
    /// and proves each emitted GUID round-trips back to the album id through the download path's
    /// <see cref="PrefixedReleaseGuidParser"/>.</para>
    /// </summary>
    public class QobuzMultiQualityReleaseGrammarTests
    {
        private const string AlbumId = "12345";
        private const string Scheme = "qobuz";

        private readonly QobuzParser _parser;

        public QobuzMultiQualityReleaseGrammarTests()
        {
            var settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true,
            };
            _parser = new QobuzParser(settings, LogManager.GetCurrentClassLogger());
        }

        // ── GUID grammar ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void EmittedGuid_NoEdition_MatchesQobuzAlbumQualityGrammar()
        {
            var album = NewAlbum().AsCdQualityFlac().Build();
            album.Version = null;

            var releases = Parse(album);

            releases.Should().NotBeEmpty();
            releases.Select(r => r.Guid).Should().OnlyContain(
                g => Regex.IsMatch(g, $@"^{Scheme}:album:{AlbumId}:quality=\d+$"),
                "the parser emits qobuz:album:{{id}}:quality={{q}} for albums without an edition");
        }

        [Fact]
        public void EmittedGuid_WithEdition_IncludesNormalizedEditionSegment()
        {
            const string version = "Deluxe Edition";
            var normalized = TitleNormalizer.NormalizeEditionForGuid(version); // "deluxe-edition"

            var album = NewAlbum().AsHiResFlac().Build();
            album.Version = version;

            var releases = Parse(album);

            releases.Should().NotBeEmpty();
            releases.Select(r => r.Guid).Should().OnlyContain(
                g => Regex.IsMatch(g, $@"^{Scheme}:album:{AlbumId}:edition={Regex.Escape(normalized)}:quality=\d+$"),
                "the edition segment is normalized via TitleNormalizer.NormalizeEditionForGuid and " +
                "inserted between the album id and the quality segment");
        }

        [Fact]
        public void EmittedGuid_RoundTripsBackToAlbumId_ViaPrefixedReleaseGuidParser()
        {
            // The download path resolves the album id from the GUID with Common's
            // PrefixedReleaseGuidParser (QobuzDownloadClient/AlbumIdExtractor). Every GUID the
            // parser emits — with or without an edition segment — must round-trip to the album id.
            var standard = Parse(NewAlbumWithVersion(null).AsCdQualityFlac().Build());
            var deluxe = Parse(NewAlbumWithVersion("Deluxe Edition").AsHiResFlac().Build());

            standard.Should().NotBeEmpty();
            deluxe.Should().NotBeEmpty();

            foreach (var release in standard.Concat(deluxe))
            {
                PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, Scheme)
                    .Should().Be(AlbumId,
                        $"emitted GUID '{release.Guid}' must resolve back to album id {AlbumId} on the download path");
            }
        }

        // ── Multi-quality release builder (one ReleaseInfo per available quality) ────────────────

        [Fact]
        public void CdQualityAlbum_EmitsExactlyOneReleasePerNonHiResQuality()
        {
            var album = NewAlbum().AsCdQualityFlac().Build();
            album.Version = null;

            var releases = Parse(album);

            QualityIdsFrom(releases).Should().BeEquivalentTo(new[]
            {
                (int)QobuzAudioQuality.MP3320,       // 5
                (int)QobuzAudioQuality.FLACLossless, // 6
            }, "a CD-quality album offers MP3 320 + FLAC lossless and no hi-res tiers");

            releases.Should().HaveCount(2);
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems(
                "each quality tier must carry a distinct, quality-suffixed GUID");
        }

        [Fact]
        public void HiResAlbum_EmitsOneReleasePerQuality_IncludingHiResTiers()
        {
            var album = NewAlbum().AsHiResFlac().Build();
            album.Version = null;

            var releases = Parse(album);

            QualityIdsFrom(releases).Should().BeEquivalentTo(new[]
            {
                (int)QobuzAudioQuality.MP3320,               // 5
                (int)QobuzAudioQuality.FLACLossless,         // 6
                (int)QobuzAudioQuality.FLACHiRes24Bit96kHz,  // 7
                (int)QobuzAudioQuality.FLACHiRes24Bit192Khz, // 27
            }, "a hi-res album adds the two hi-res FLAC tiers on top of MP3 + lossless");

            releases.Should().HaveCount(4);
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────

        private static List<int> QualityIdsFrom(IEnumerable<ReleaseInfo> releases) =>
            releases
                .Select(r => Regex.Match(r.Guid, @"quality=(\d+)$"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToList();

        private static QobuzAlbumBuilder NewAlbum() =>
            QobuzAlbumBuilder.New()
                .WithId(AlbumId)
                .WithTitle("Discovery")
                .WithArtist("Daft Punk")
                .WithReleaseYear(2001);

        private static QobuzAlbumBuilder NewAlbumWithVersion(string version)
        {
            var b = NewAlbum();
            return version is null ? b : b.WithVersion(version);
        }

        /// <summary>
        /// Drives the album through the plugin's REAL parser path (serialize -> ParseResponse),
        /// exactly as the live indexer does — no production hooks, no shortcuts.
        /// </summary>
        private List<ReleaseInfo> Parse(QobuzAlbum album)
        {
            var searchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = new List<QobuzAlbum> { album },
                },
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("http://test.local"),
                new HttpHeader(),
                JsonConvert.SerializeObject(searchResponse),
                HttpStatusCode.OK);

            var indexerResponse = new IndexerResponse(
                new IndexerRequest("http://test.local", new HttpAccept("application/json")),
                httpResponse);

            return _parser.ParseResponse(indexerResponse).ToList();
        }
    }
}
