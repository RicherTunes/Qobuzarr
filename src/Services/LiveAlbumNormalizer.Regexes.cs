using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Services;

/// <summary>
/// Compile-time generated regex patterns for LiveAlbumNormalizer.
/// Each accessor returns a cached regex instance.
/// Note: CultureInvariant is used with IgnoreCase to avoid Turkish-I edge cases.
/// </summary>
internal static partial class LiveAlbumNormalizerRegexes
{
    #region Date Patterns

    /// <summary>ISO dates: 2023-12-31, 2023/12/31</summary>
    [GeneratedRegex(@"\b(\d{4})[\/\-](\d{1,2})[\/\-](\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex IsoDate();

    /// <summary>US dates: 12/31/2023, 12-31-2023</summary>
    [GeneratedRegex(@"\b(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex UsDate();

    /// <summary>Month names: December 31, 2023 | Dec 31 2023</summary>
    [GeneratedRegex(@"\b(january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\s+(\d{1,2}),?\s+(\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex MonthNameDate();

    /// <summary>Year ranges: 2023-2024, 2023/24</summary>
    [GeneratedRegex(@"\b(\d{4})[\/\-](\d{2,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex YearRange();

    /// <summary>Simple year: 2023</summary>
    [GeneratedRegex(@"\b(\d{4})\b", RegexOptions.CultureInvariant)]
    internal static partial Regex SimpleYear();

    /// <summary>Extract year 1950-2099</summary>
    [GeneratedRegex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    internal static partial Regex ExtractYear();

    #endregion

    #region Live Album Title Patterns

    /// <summary>"Album Title (Live at Venue, Date)"</summary>
    [GeneratedRegex(@"^(.+?)\s*[\(\[]\s*(?:live|recorded|concert|performance)\s+(?:at|from|in)\s+([^,\]\)]+)(?:,\s*([^\]\)]+))?\s*[\)\]]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveAtVenueParentheses();

    /// <summary>"Album Title - Live at Venue"</summary>
    [GeneratedRegex(@"^(.+?)\s*[-–—]\s*(?:live|recorded|concert)\s+(?:at|from|in)\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveAtVenueDash();

    /// <summary>"Live at Venue: Album Title"</summary>
    [GeneratedRegex(@"^(?:live|recorded|concert)\s+(?:at|from|in)\s+([^:]+):\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex VenueColonTitle();

    /// <summary>"Album Title Live"</summary>
    [GeneratedRegex(@"^(.+?)\s+(?:live|concert|unplugged|acoustic)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex TitleSuffixLive();

    /// <summary>"MTV Unplugged: Album Title"</summary>
    [GeneratedRegex(@"^(?:mtv\s+unplugged|bbc\s+session|live\s+session):\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex SpecialSessionPrefix();

    #endregion

    #region Title Extraction Patterns

    /// <summary>Remove "- live..." suffix from title</summary>
    [GeneratedRegex(@"\s*[-–—]\s*live.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveDashSuffix();

    /// <summary>Remove "(live...)" or "[live...]" from title</summary>
    [GeneratedRegex(@"\s*[\(\[]\s*live.*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveParenthesesSuffix();

    /// <summary>Remove trailing " live" from title</summary>
    [GeneratedRegex(@"\s+live$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LiveWordSuffix();

    #endregion

    #region Venue Normalization Patterns

    /// <summary>Remove leading "the " from venue names</summary>
    [GeneratedRegex(@"\b(the\s+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex LeadingThe();

    /// <summary>Normalize venue type suffixes</summary>
    [GeneratedRegex(@"\s+(arena|stadium|theater|theatre|hall|center|centre)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex VenueTypeSuffix();

    #endregion
}
