using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using Xunit;
using NzbDrone.Core.Music;
using NzbDrone.Core.IndexerSearch.Definitions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Pins the request-generation half of the search-hardening fix: the artist-only catalogue
    /// fallback must reach the issued request chain even when the combined/over-specific queries
    /// fill the per-search cap. The shipped bug ("Bleu Jeans Bleu - Record n°V" returning 0
    /// results) was CreateIndexerRequests doing queries.Take(3), which truncated the artist-only
    /// fallback away so a special-char album query had no degrade path.
    /// </summary>
    public class QobuzRequestGeneratorFallbackTests
    {
        private static QobuzRequestGenerator NewGenerator()
        {
            var settings = new QobuzIndexerSettings();
            var session = new QobuzSession { AppId = "test-app-id", AuthToken = "test-auth-token" };
            return new QobuzRequestGenerator(settings, LogManager.GetCurrentClassLogger(), () => session);
        }

        private static AlbumSearchCriteria Criteria(string artist, string album) =>
            new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                Albums = new List<Album> { new Album { Title = album } }
            };

        private static List<string> IssuedQueries(QobuzRequestGenerator generator, AlbumSearchCriteria criteria)
        {
            var chain = generator.GetSearchRequests(criteria);
            var queries = new List<string>();

            foreach (var tier in chain.GetAllTiers())
            {
                foreach (var request in tier)
                {
                    var url = request.HttpRequest?.Url?.ToString();
                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    var queryIndex = url.IndexOf('?');
                    if (queryIndex < 0)
                    {
                        continue;
                    }

                    foreach (var part in url.Substring(queryIndex + 1).Split('&'))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length == 2 && kv[0] == "query")
                        {
                            queries.Add(Uri.UnescapeDataString(kv[1]));
                        }
                    }
                }
            }

            return queries;
        }

        [Fact]
        public void SpecialCharAlbum_IssuesArtistOnlyFallback()
        {
            var queries = IssuedQueries(NewGenerator(), Criteria("Bleu Jeans Bleu", "Record n°V"));

            queries.Should().Contain(q => q.Trim().Equals("Bleu Jeans Bleu", StringComparison.OrdinalIgnoreCase),
                "the artist-only catalogue fallback must reach the request chain even though the combined " +
                "special-char queries fill the per-search cap (the truncated-away Bleu-Jeans bug)");
        }

        [Fact]
        public void SpecialCharArtist_IssuesArtistOnlyFallbackVariants()
        {
            var queries = IssuedQueries(NewGenerator(), Criteria("AC/DC", "Back in Black"));

            queries.Should().Contain(q => q.Trim().Equals("AC/DC", StringComparison.OrdinalIgnoreCase),
                "the exact artist-only catalogue fallback should survive the over-specific cap");
            queries.Should().Contain(q => q.Trim().Equals("AC DC", StringComparison.OrdinalIgnoreCase),
                "the spaced artist-only catalogue fallback should survive the over-specific cap for slash-separated artists");
            queries.Should().Contain(q => q.Trim().Equals("ACDC", StringComparison.OrdinalIgnoreCase),
                "the joined artist-only catalogue fallback should survive the over-specific cap for slash-separated artists");
        }

        [Fact]
        public void PlainAlbum_IssuesBothCombinedAndArtistOnly()
        {
            var queries = IssuedQueries(NewGenerator(), Criteria("Daft Punk", "Discovery"));

            queries.Should().Contain(q => q.Contains("Daft Punk") && q.Contains("Discovery"),
                "the combined artist+album query is still issued for the normal case");
            queries.Should().Contain(q => q.Trim().Equals("Daft Punk", StringComparison.OrdinalIgnoreCase),
                "the artist-only fallback is always issued");
        }

        // Architecture-appropriate equivalent of the cross-plugin chain-completeness guard: qobuz
        // intentionally CAPS the over-specific queries (Take(MaxOverSpecificRequests)) and appends the
        // canonical artist-only fallback, so it does NOT issue every BuildPlan variant. The sanitizer also
        // deliberately emits BOTH a cleaned variant AND a raw-preserving one (so an exact-match catalogue
        // can still hit), so we do NOT require every query to be clean — we require the CLEAN degraded form
        // to SURVIVE the cap. That clean form (plus the artist-only fallback above) is what stopped
        // "Record n°V" returning zero.
        [Fact]
        public void SpecialCharAlbum_CapPreservesTheCleanSanitizedCombinedForm()
        {
            var queries = IssuedQueries(NewGenerator(), Criteria("Bleu Jeans Bleu", "Record n°V"));

            queries.Should().NotBeEmpty();

            queries.Should().Contain(
                q => q.Replace(" ", string.Empty).Equals("BleuJeansBleuRecordnV", StringComparison.OrdinalIgnoreCase),
                "the sanitized COMBINED form (degree sign dropped → 'Bleu Jeans Bleu Record nV') must survive the " +
                "per-search cap so the special-char search has a clean degrade path — the Take(N) must not keep " +
                "only the raw-preserving variant");
        }

        // codex adversarial review (Med): the per-search cap interacts with SmartQueryStrategy, which can
        // mark a query "Simple" (QueryVariants=1) when it has NO QueryComplexityClassifier 'special' char
        // ([&+/\-:'"()]) and no non-ASCII. ASCII punctuation OUTSIDE that set (e.g. ? ! . #) is still
        // sanitizer-cleanable, so we must prove the cap+optimizer never strips the clean combined form to
        // zero for those cases. Drives the REAL generator end-to-end.
        [Theory]
        [InlineData("Blur", "Music Is My Radar?", "Blur Music Is My Radar")]
        [InlineData("Beatles", "Help!", "Beatles Help")]
        [InlineData("Kendrick", "good.kid", "Kendrick good kid")]
        [InlineData("Artist", "Song #1", "Artist Song 1")]
        public void AsciiPunctuationAlbum_CapPreservesACleanCombinedForm(string artist, string album, string expectedCleanCombined)
        {
            var queries = IssuedQueries(NewGenerator(), Criteria(artist, album));

            queries.Should().NotBeEmpty();

            var wantCollapsed = expectedCleanCombined.Replace(" ", string.Empty);
            queries.Should().Contain(
                q => q.Replace(" ", string.Empty).Equals(wantCollapsed, StringComparison.OrdinalIgnoreCase),
                $"the sanitized combined form ('{expectedCleanCombined}') must survive the cap + SmartQueryStrategy " +
                $"optimization for ASCII-punctuation album '{album}' — otherwise a Simple-classified special-char " +
                "search degrades to only the raw/over-specific query and can return zero");
        }
    }
}
