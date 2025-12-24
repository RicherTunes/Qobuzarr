using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Indexers;

/// <summary>
/// Compile-time generated regex patterns for QueryComplexityClassifier.
/// Each accessor returns a cached regex instance.
/// </summary>
internal static partial class QueryComplexityClassifierRegexes
{
    [GeneratedRegex(@"[&+/\-:'""()]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex SpecialCharacters();

    [GeneratedRegex(@"[^\x00-\x7F]", RegexOptions.CultureInvariant)]
    internal static partial Regex NonAscii();

    [GeneratedRegex(@"\b(various\s+artists|compilation|v\.?a\.?|soundtrack)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex VariousArtistsKeywords();

    [GeneratedRegex(@"\b(featuring|feat\.?|ft\.?|with|vs\.?|versus|and|&)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex ComplexWordsKeywords();

    [GeneratedRegex(@"\b\d+\b", RegexOptions.CultureInvariant)]
    internal static partial Regex StandaloneNumbers();

    [GeneratedRegex(@"\b(live|unplugged|acoustic|concert|session)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveRecordingKeywords();
}
