using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Intelligence;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using Xunit;

namespace Qobuzarr.Tests.Compliance;

/// <summary>
/// Proves that every query Qobuzarr's real indexer hands to the Qobuz API was produced by
/// <see cref="QueryBuilder.BuildPlanForTest"/> (i.e., by Common's
/// <see cref="SearchQuerySanitizer.BuildPlan"/>) — closing the loop that
/// <see cref="QobuzarrSearchQuerySanitizerParityTests"/> opens (which pins the plan SHAPE but
/// not what the live request-chain actually issues to the transport).
///
/// <para>The test drives <see cref="QobuzRequestGenerator.GetSearchRequests"/> (the real
/// Lidarr-host seam) with a stub session and captures the decoded <c>query=</c> params from
/// the issued URLs. No network calls are made.</para>
/// </summary>
[Trait("Category", "Compliance")]
public sealed class QobuzarrSearchTermProvenanceTests : SearchTermProvenanceComplianceTestBase
{
    // Default sample pair matches the base class (Daft Punk / Discovery) — inherited.

    /// <summary>
    /// Returns the SearchPlan produced by the plugin's REAL plan-construction path
    /// (same <see cref="QueryBuilder"/> that <see cref="QobuzRequestGenerator"/> uses).
    /// </summary>
    protected override SearchPlan BuildPlanViaPlugin(string artist, string album) =>
        QueryBuilder.BuildPlanForTest(artist, album);

    /// <summary>
    /// Drives the real <see cref="QobuzRequestGenerator"/> against a stub session and captures
    /// every decoded <c>query=</c> value from the issued HTTP request URLs, in issue order.
    /// </summary>
    protected override Task<IReadOnlyList<string>> CaptureIssuedQueriesAsync(string artist, string album)
    {
        var settings = new QobuzIndexerSettings();
        var session = new QobuzSession { AppId = "test-app-id", AuthToken = "test-auth-token" };
        var generator = new QobuzRequestGenerator(settings, LogManager.GetCurrentClassLogger(), () => session);

        var criteria = new AlbumSearchCriteria
        {
            Artist = new Artist { Name = artist },
            Albums = new System.Collections.Generic.List<NzbDrone.Core.Music.Album>
            {
                new NzbDrone.Core.Music.Album { Title = album }
            }
        };

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
                    if (kv.Length == 2 && string.Equals(kv[0], "query", StringComparison.OrdinalIgnoreCase))
                    {
                        queries.Add(Uri.UnescapeDataString(kv[1]));
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(queries);
    }
}
