using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Generates formatted release titles from album and quality data.
    /// Extracted from QobuzParser to follow Single Responsibility Principle.
    /// </summary>
    public interface ITitleGenerator
    {
        /// <summary>
        /// Generates a quality-specific title for a release.
        /// </summary>
        string GenerateQualitySpecificTitle(QobuzAlbum album, QobuzAudioQuality quality, int year);

        /// <summary>
        /// Generates a title in hyphen format (Artist - Album - Version - Format - Year).
        /// </summary>
        string GenerateHyphenFormatTitle(string artist, string albumTitle, string version, string formatStr, int year);

        /// <summary>
        /// Sanitizes text for use in hyphen format titles.
        /// </summary>
        string SanitizeForHyphenFormat(string text);

        /// <summary>
        /// Validates hyphen format string.
        /// </summary>
        string ValidateHyphenFormat(string format);

        /// <summary>
        /// Extracts version information from album title.
        /// </summary>
        string ExtractVersionFromTitle(string title);

        /// <summary>
        /// Checks if title contains edition keywords.
        /// </summary>
        bool ContainsEditionKeywords(string text);

        /// <summary>
        /// Determines if an album is a live recording.
        /// </summary>
        bool IsLiveAlbum(string albumTitle);
    }
}