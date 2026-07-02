using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Qobuzarr.Tests.Builders;
using Xunit;

namespace Qobuzarr.Tests.Unit.Indexers
{
    public sealed class QobuzParserSuppressionTests
    {
        [Fact]
        public void ParseResponse_SuppressedAlbumId_EmitsNoReleasesForAnyQualityTier()
        {
            var suppression = new FakeSuppressionStore("suppressed-album");
            var parser = new QobuzParser(Settings(), LogManager.GetCurrentClassLogger(), suppression);
            var suppressed = Album("suppressed-album").AsHiResFlac().Build();

            var releases = parser.ParseResponse(Response(suppressed));

            releases.Should().BeEmpty(
                "terminal suppression is keyed by album id and must remove every quality-tier release");
        }

        [Fact]
        public void ParseResponse_MixedResponse_SuppressesOnlyMatchingAlbumId()
        {
            var suppression = new FakeSuppressionStore("suppressed-album");
            var parser = new QobuzParser(Settings(), LogManager.GetCurrentClassLogger(), suppression);
            var suppressed = Album("suppressed-album").AsHiResFlac().Build();
            var allowed = Album("allowed-album").AsCdQualityFlac().Build();

            var releases = parser.ParseResponse(Response(suppressed, allowed)).ToList();

            releases.Should().NotBeEmpty();
            releases.Should().OnlyContain(r => r.Guid.Contains("allowed-album"));
        }

        [Fact]
        public void ParseResponse_NoSuppressionStoreProvided_BehavesExactlyAsBefore()
        {
            // Every pre-existing `new QobuzParser(settings, logger)` call site (25+ across the test suite,
            // plus QobuzIndexer.GetParser() before it opts in) must keep compiling and behaving
            // identically — the suppression store parameter is optional and defaults to a no-op.
            var parser = new QobuzParser(Settings(), LogManager.GetCurrentClassLogger());
            var album = Album("any-album").AsCdQualityFlac().Build();

            var releases = parser.ParseResponse(Response(album));

            releases.Should().NotBeEmpty("without an explicit suppression store, nothing is ever suppressed");
        }

        [Fact]
        public async Task ParseResponse_RealCommonBackedSuppressionStore_PersistsAndSuppressesFreshParser()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qobuz-parser-suppression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var storePath = Path.Combine(tempDir, "terminal-release-suppressions.json");
                var writer = new RestrictedReleaseSuppressionStore(storePath);
                await writer.SuppressAsync("suppressed-album", "restricted-track", TrackUnavailableReason.Restricted);

                var freshReader = new RestrictedReleaseSuppressionStore(storePath);
                var parser = new QobuzParser(Settings(), LogManager.GetCurrentClassLogger(), freshReader);
                var suppressed = Album("suppressed-album").AsHiResFlac().Build();

                var releases = parser.ParseResponse(Response(suppressed));

                releases.Should().BeEmpty(
                    "the production adapter and Common store must round-trip before the parser can stop a re-grab loop");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        private static QobuzIndexerSettings Settings() => new()
        {
            IncludeSingles = true,
            IncludeCompilations = true,
        };

        private static QobuzAlbumBuilder Album(string id) => new QobuzAlbumBuilder()
            .WithId(id)
            .WithTitle("Suppression Test Album")
            .WithArtist("Suppression Artist", "suppression-artist");

        private static IndexerResponse Response(params QobuzAlbum[] albums)
        {
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = albums.ToList(),
                },
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("http://test.qobuz.com/api"),
                new HttpHeader(),
                JsonConvert.SerializeObject(albumSearchResponse),
                HttpStatusCode.OK);

            return new IndexerResponse(
                new IndexerRequest("http://test.qobuz.com/api", new HttpAccept("application/json")),
                httpResponse);
        }

        private sealed class FakeSuppressionStore : IRestrictedReleaseSuppressionStore
        {
            private readonly HashSet<string> _suppressed;

            public FakeSuppressionStore(params string[] suppressed)
            {
                _suppressed = new HashSet<string>(suppressed, System.StringComparer.OrdinalIgnoreCase);
            }

            public bool IsSuppressed(string albumId) => _suppressed.Contains(albumId);

            public Task SuppressAsync(
                string albumId,
                string trackId,
                TrackUnavailableReason reason,
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default)
                => Task.FromResult(false);
        }
    }
}
