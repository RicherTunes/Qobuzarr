using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using Xunit;
using NzbDrone.Core.Music;
using NzbDrone.Core.IndexerSearch.Definitions;
using Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Search-hardening regression tests for the indexer query builder.
    ///
    /// Live root cause (2026-06-26): searching for "Bleu Jeans Bleu - Record n°V" returned
    /// 0 releases from qobuz even though the album exists (qobuz slug: record-<b>nv</b> — the
    /// service drops the ° entirely). The query builder replaced the ° with a SPACE
    /// ("Record n°V" → "Record n V"), splitting the token into "n" and "V" so qobuz's tokenizer
    /// couldn't match, and the artist-only fallback (which would have returned the band's catalog
    /// for Lidarr to match) was generated last and truncated away downstream.
    ///
    /// The fix: also emit a symbol-removed album variant where the symbol's neighbours stay
    /// adjacent ("Record nV"), in addition to the space variant, and always keep an artist-only
    /// fallback in the set. The canonical sanitizer lives in Lidarr.Plugin.Common; these assert
    /// qobuz's end behavior once it adopts it.
    /// </summary>
    public class QueryBuilderSearchHardeningTests
    {
        private readonly QueryBuilder _builder = new QueryBuilder(LogManager.GetCurrentClassLogger());

        private static AlbumSearchCriteria Criteria(string artist, string album) =>
            new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                Albums = new List<Album> { new Album { Title = album } }
            };

        [Fact]
        public void DegreeSign_EmitsAdjacentRemovedVariant()
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria("Bleu Jeans Bleu", "Record n°V"));

            queries.Should().Contain(q => q.Contains("nV"),
                "a symbol-removed variant ('Record nV') is required; the space-split 'Record n V' matches nothing");
        }

        [Fact]
        public void DegreeSign_StillIncludesArtistOnlyFallback()
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria("Bleu Jeans Bleu", "Record n°V"));

            queries.Should().Contain(q => q.Trim().Equals("Bleu Jeans Bleu", StringComparison.OrdinalIgnoreCase),
                "an artist-only fallback lets Lidarr match the wanted album from the band's catalog when " +
                "the album-specific query (mangled by a special char) returns nothing");
        }

        // A symbol sitting BETWEEN two non-space characters must yield a variant where those
        // characters stay adjacent after the symbol is removed (not split by a space).
        [Theory]
        [InlineData("Bleu Jeans Bleu", "Record n°V", "nV")]
        [InlineData("Foo", "Hello!World", "HelloWorld")]
        [InlineData("Bar", "a*b", "ab")]
        [InlineData("Baz", "Ke$ha", "Keha")]
        public void MidTokenSymbol_EmitsAdjacentRemovedVariant(string artist, string album, string expectedAdjacent)
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria(artist, album));

            queries.Should().Contain(q => q.Contains(expectedAdjacent),
                $"a variant of '{album}' with the symbol removed and neighbours kept adjacent ('{expectedAdjacent}') is required");
        }

        // Every album search — regardless of special characters — must include the artist-only
        // fallback so a failing album-specific query degrades to the band's catalog.
        [Theory]
        [InlineData("AC/DC", "Power Up")]
        [InlineData("Guns N' Roses", "Use Your Illusion I")]
        [InlineData("Sigur Rós", "Takk...")]
        [InlineData("Motörhead", "Ace of Spades")]
        [InlineData("Panic! at the Disco", "Pray for the Wicked")]
        [InlineData("!!!", "Wallop")]
        [InlineData("Beyoncé", "Renaissance")]
        [InlineData("Daft Punk", "Discovery")]
        public void ArtistOnlyFallback_PresentForAllSpecialCharCases(string artist, string album)
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria(artist, album));

            var artistOnly = _builder.CleanQuery(artist);
            queries.Should().Contain(q => q.Trim().Equals(artistOnly.Trim(), StringComparison.OrdinalIgnoreCase),
                "artist-only fallback must always be generated");
        }

        // Separators (a symbol with word characters on each side, like a slash) should also still
        // produce a spaced variant so "AC/DC" can match "AC DC" — we keep BOTH forms.
        [Fact]
        public void Separator_KeepsSpacedVariantToo()
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria("Test", "Rock/Roll"));

            queries.Should().Contain(q => q.Contains("Rock Roll"),
                "the spaced variant must remain for separator-style symbols");
        }

        [Fact]
        public void PlainAsciiAlbum_StillProducesPrimaryAndFallback_NoRegression()
        {
            var queries = _builder.BuildAlbumSearchQueries(Criteria("Daft Punk", "Discovery"));

            queries.Should().Contain(q => q.Contains("Daft Punk") && q.Contains("Discovery"),
                "the primary artist+album query must still be produced for the normal case");
            queries.Should().Contain(q => q.Trim().Equals("Daft Punk", StringComparison.OrdinalIgnoreCase),
                "artist-only fallback must still be present");
        }
    }
}
