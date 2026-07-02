using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Common.Services.Intelligence;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using Xunit;

namespace Qobuzarr.Tests.Compliance;

/// <summary>
/// Adopts <see cref="CappedSearchChainComplianceTestBase"/> for Qobuzarr: proves the plugin's
/// capped search chain (produced via <see cref="CappedSearchChain.Build"/> in
/// <see cref="QobuzRequestGenerator"/>) obeys both cap invariants:
/// <list type="number">
/// <item>over-specific queries ≤ <see cref="MaxOverSpecificQueries"/></item>
/// <item>the artist-only catalogue fallback is always present (never truncated by the cap)</item>
/// </list>
///
/// <para>The inherited <c>[Fact]</c>s in the base drive Qobuz's REAL query-building path
/// (<see cref="QueryBuilder.BuildAlbumSearchQueries"/> + <see cref="SmartQueryStrategy"/> +
/// <see cref="CappedSearchChain.Build"/>) against the same <c>Daft Punk / Discovery</c> sample
/// pair used by the base. The existing local fallback tests
/// (<see cref="QobuzRequestGeneratorFallbackTests"/>) are preserved and additive to this axis.
/// </para>
/// </summary>
[Trait("Category", "Compliance")]
public sealed class QobuzCappedSearchChainComplianceTests : CappedSearchChainComplianceTestBase
{
    /// <summary>
    /// The placeholder scheme used to encode queries as URIs for the compliance base.
    /// The Qobuz generator issues real HTTPS requests, not native bridge placeholder URIs;
    /// this scheme is a compliance-test-only label (never seen by the Qobuz API).
    /// </summary>
    protected override string PlaceholderScheme => "qobuz";

    /// <summary>
    /// The cap on over-specific (combined / album-specific) queries per search, as configured
    /// in <see cref="QobuzRequestGenerator"/> (<c>MaxOverSpecificRequests = 3</c>).
    /// </summary>
    protected override int MaxOverSpecificQueries => 3;

    /// <summary>
    /// The artist-only fallback query that <see cref="QobuzRequestGenerator"/> passes to
    /// <see cref="CappedSearchChain.Build"/> as <c>artistOnlyFallback</c>: the sanitized
    /// original form of the artist name via <see cref="QueryBuilder.CleanQuery"/>.
    /// For "Daft Punk" this is "Daft Punk" (no special chars to clean).
    /// </summary>
    protected override string GetExpectedArtistOnlyFallbackQuery(string artist, string album)
    {
        var queryBuilder = new QueryBuilder(LogManager.GetCurrentClassLogger());
        return queryBuilder.CleanQuery(artist);
    }

    /// <summary>
    /// Qobuz preserves the full artist-only sanitizer tier beyond the over-specific cap.
    /// </summary>
    protected override IReadOnlyList<string> GetExpectedArtistOnlyFallbackQueries(string artist, string album)
    {
        var queryBuilder = new QueryBuilder(LogManager.GetCurrentClassLogger());
        return queryBuilder.BuildArtistFallbackQueries(artist);
    }

    /// <summary>
    /// Drives Qobuz's real query-building + capping pipeline and returns the resulting queries
    /// encoded as <c>qobuz://search?query=...</c> placeholder URIs for the compliance base to
    /// decode.  The pipeline mirrors <see cref="QobuzRequestGenerator.CreateIndexerRequests"/>:
    /// <list type="number">
    /// <item><see cref="QueryBuilder.BuildAlbumSearchQueries"/> — plan variants</item>
    /// <item><see cref="SmartQueryStrategy.BuildOptimizedQueries"/> — complexity-driven trim (no ML in tests)</item>
    /// <item><see cref="CappedSearchChain.Build"/> — cap + guaranteed fallback</item>
    /// </list>
    /// The <see cref="RequestFactory"/> (which issues real HTTPS requests) is intentionally
    /// bypassed; we test the capping logic, not network I/O.
    /// </summary>
    protected override IReadOnlyList<string> GetSearchRequestUrls(string artist, string album)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var queryBuilder = new QueryBuilder(logger);

        var criteria = new AlbumSearchCriteria
        {
            Artist = new Artist { Name = artist },
            Albums = new System.Collections.Generic.List<NzbDrone.Core.Music.Album>
            {
                new NzbDrone.Core.Music.Album { Title = album }
            }
        };

        // Step 1: build the full query plan (same as generator)
        var queries = queryBuilder.BuildAlbumSearchQueries(criteria);

        // Step 2: apply SmartQueryStrategy with no ML engine (same as test-mode generator)
        var smartStrategy = new SmartQueryStrategy(logger);
        queries = smartStrategy.BuildOptimizedQueries(artist, album, queries);

        // Step 3: compute artist-only fallback and apply the cap (same as generator)
        var artistOnlyFallbacks = queryBuilder.BuildArtistFallbackQueries(artist);
        var selected = CappedSearchChain.Build(queries, artistOnlyFallbacks, MaxOverSpecificQueries);

        // Return as placeholder URIs so the compliance base can decode and assert the queries
        return selected.Select(q => PlaceholderSearchUri.Build(PlaceholderScheme, q)).ToList();
    }
}
