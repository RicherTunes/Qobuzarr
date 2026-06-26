using Lidarr.Plugin.Common.Services.Intelligence;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Xunit;

namespace Qobuzarr.Parity.Tests;

/// <summary>
/// Qobuzarr adopts the <c>search-query-sanitizer</c> parity axis. The indexer search path
/// (QueryBuilder.BuildAlbumSearchQueries / CleanQuery and RequestFactory.CreateSearchRequest)
/// routes every term through Common's canonical <see cref="SearchQuerySanitizer"/>, so this
/// runs the full tricky-character corpus through that same entrypoint and asserts the
/// cross-plugin invariants hold (and fails qobuz CI if a future Common re-pin regresses them).
/// </summary>
[Trait("Category", "Parity")]
public sealed class QobuzarrSearchQuerySanitizerParityTests : SearchQuerySanitizerParityTestBase
{
    protected override SanitizedQuery SanitizeViaPlugin(string? raw) =>
        SearchQuerySanitizer.Sanitize(raw);

    protected override string ToQueryParameterValue(string variant) =>
        SearchQuerySanitizer.ToQueryParameterValue(variant);
}
