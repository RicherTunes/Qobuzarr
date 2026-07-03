using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Exceptions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Provides enhanced error messages with detailed explanations and suggestions
    /// </summary>
    public static class ErrorMessageFormatter
    {
        /// <summary>
        /// Short, queue-friendly labels for each <see cref="TrackUnavailableReason"/>, used by
        /// <see cref="FormatGroupedFailureReasons"/> to turn a bare failure count (Lidarr's default
        /// "1 failed") into an actionable "1 restricted (subscription tier)"-style summary. Deliberately
        /// terser than <see cref="GetDetailedReason"/> (full sentences with emoji), which is aimed at a
        /// single-track detail view rather than a comma-joined multi-reason queue line.
        /// </summary>
        private static readonly IReadOnlyDictionary<TrackUnavailableReason, string> GroupedFailureLabels =
            new Dictionary<TrackUnavailableReason, string>
            {
                [TrackUnavailableReason.RegionalRestriction] = "region-locked",
                [TrackUnavailableReason.SubscriptionRestriction] = "restricted (subscription tier)",
                [TrackUnavailableReason.PreviewOnly] = "preview-only",
                [TrackUnavailableReason.NoQualityAvailable] = "no suitable quality",
                [TrackUnavailableReason.NotStreamable] = "not available for streaming",
                [TrackUnavailableReason.Restricted] = "restricted (rights holder)",
                [TrackUnavailableReason.ApiError] = "technical error",
                [TrackUnavailableReason.Unknown] = "unknown restriction",
            };

        /// <summary>
        /// Formats an <see cref="AlbumDownloadException"/>'s failed tracks as a reason-grouped,
        /// human-readable summary suitable for Lidarr's download queue (e.g. "2 restricted (subscription
        /// tier), 1 region-locked") instead of a bare count. Purely a presentation helper — it reads
        /// <see cref="AlbumDownloadException.GetIssuesSummary"/> and never touches completion/suppression
        /// decisions.
        /// </summary>
        /// <param name="exception">The album download failure to summarize.</param>
        /// <returns>
        /// A comma-separated "&lt;count&gt; &lt;reason label&gt;" summary, largest group first; or
        /// <c>null</c> when no track carries a classified <see cref="TrackUnavailableReason"/> (e.g. an
        /// unmapped/unexpected failure), so callers can gracefully fall back to a generic message instead
        /// of fabricating a misleading reason.
        /// </returns>
        public static string FormatGroupedFailureReasons(AlbumDownloadException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var issuesByReason = exception.GetIssuesSummary();
            if (issuesByReason.Count == 0)
            {
                return null;
            }

            var parts = issuesByReason
                .OrderByDescending(kvp => kvp.Value.Count)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Value.Count} {GetGroupedFailureLabel(kvp.Key)}")
                .ToList();

            // Deficit tracks with no classified reason are a distinct, smaller pool than the classified
            // buckets above — surface their count too rather than silently dropping them from the total.
            var unclassifiedCount = exception.TrackResults.Count(r => !r.Success && !r.Reason.HasValue);
            if (unclassifiedCount > 0)
            {
                parts.Add($"{unclassifiedCount} unspecified");
            }

            return string.Join(", ", parts);
        }

        private static string GetGroupedFailureLabel(TrackUnavailableReason reason) =>
            GroupedFailureLabels.TryGetValue(reason, out var label) ? label : "unavailable";

        /// <summary>
        /// Formats a detailed error message for track unavailability
        /// </summary>
        public static string FormatTrackError(string trackTitle, TrackUnavailableReason reason, string additionalContext = null)
        {
            var sb = new StringBuilder();

            // Header with error icon
            sb.AppendLine($"❌ Track unavailable: '{trackTitle}'");

            // Reason with appropriate icon
            sb.Append(" ↳ Reason: ");
            sb.AppendLine(GetDetailedReason(reason, additionalContext));

            // Add suggestion if applicable
            var suggestion = GetSuggestion(reason);
            if (!string.IsNullOrEmpty(suggestion))
            {
                sb.Append(" ↳ Suggestion: ");
                sb.AppendLine(suggestion);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets a detailed, user-friendly reason description
        /// </summary>
        private static string GetDetailedReason(TrackUnavailableReason reason, string context)
        {
            return reason switch
            {
                TrackUnavailableReason.RegionalRestriction =>
                    "🌍 Geographic restriction - Not available in your region" +
                    (context != null ? $" ({context})" : ""),

                TrackUnavailableReason.SubscriptionRestriction =>
                    "💎 Subscription tier limitation - Requires Qobuz Studio/Sublime" +
                    (context != null ? $" (Current: {context})" : ""),

                TrackUnavailableReason.PreviewOnly =>
                    "🎧 Preview/sample only - Full track not available for streaming",

                TrackUnavailableReason.NoQualityAvailable =>
                    "🎵 No suitable quality available" +
                    (context != null ? $" (Requested: {context})" : ""),

                TrackUnavailableReason.NotStreamable =>
                    "🚫 Not available for streaming - Purchase-only or exclusive content",

                TrackUnavailableReason.Restricted =>
                    "🔒 Download restricted by rights holder" +
                    (context != null ? $" ({context})" : ""),

                TrackUnavailableReason.ApiError =>
                    "⚠️ Technical error accessing Qobuz API" +
                    (context != null ? $" ({context})" : ""),

                TrackUnavailableReason.Unknown =>
                    "❓ Unknown restriction" +
                    (context != null ? $" - {context}" : ""),

                _ => "Track currently unavailable"
            };
        }

        /// <summary>
        /// Gets actionable suggestions for resolving the issue
        /// </summary>
        private static string GetSuggestion(TrackUnavailableReason reason)
        {
            return reason switch
            {
                TrackUnavailableReason.RegionalRestriction =>
                    "Check if track is available in your Qobuz account region settings",

                TrackUnavailableReason.SubscriptionRestriction =>
                    "Upgrade to Qobuz Studio/Sublime for Hi-Res access, or lower quality preference in settings",

                TrackUnavailableReason.PreviewOnly =>
                    "This may be a pre-release or exclusive track. Check back later for full availability",

                TrackUnavailableReason.NoQualityAvailable =>
                    "Try enabling quality fallback in settings to use best available quality",

                TrackUnavailableReason.NotStreamable =>
                    "This track may only be available for purchase, not streaming",

                TrackUnavailableReason.Restricted =>
                    "Contact Qobuz support if you believe you should have access",

                TrackUnavailableReason.ApiError =>
                    "Retry the download - this may be a temporary issue",

                _ => null
            };
        }

        /// <summary>
        /// Formats an album error with track-level details
        /// </summary>
        public static string FormatAlbumError(string albumTitle, string artist, int failedTracks, int totalTracks, string primaryReason = null)
        {
            var sb = new StringBuilder();

            if (failedTracks == totalTracks)
            {
                sb.AppendLine($"❌ Album completely unavailable: '{artist} - {albumTitle}'");
                sb.AppendLine($" ↳ All {totalTracks} tracks failed");
            }
            else
            {
                sb.AppendLine($"⚠️ Album partially available: '{artist} - {albumTitle}'");
                sb.AppendLine($" ↳ {failedTracks} of {totalTracks} tracks unavailable");
            }

            if (!string.IsNullOrEmpty(primaryReason))
            {
                sb.AppendLine($" ↳ Primary issue: {primaryReason}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats a quality fallback message with context
        /// </summary>
        public static string FormatQualityFallback(string trackTitle, string actualQuality, string requestedQuality, string reason = null)
        {
            var sb = new StringBuilder();
            sb.Append($"⬇️ Quality downgrade for '{trackTitle}': {actualQuality}");

            if (requestedQuality != actualQuality)
            {
                sb.Append($" (requested {requestedQuality})");
            }

            if (!string.IsNullOrEmpty(reason))
            {
                sb.AppendLine();
                sb.Append($" ↳ Reason: {reason}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a network error with retry information
        /// </summary>
        public static string FormatNetworkError(string operation, Exception ex, int attemptNumber = 0, int maxAttempts = 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🌐 Network error during {operation}");
            sb.AppendLine($" ↳ Error: {ex.Message}");

            if (attemptNumber > 0 && maxAttempts > 0)
            {
                sb.AppendLine($" ↳ Attempt: {attemptNumber}/{maxAttempts}");

                if (attemptNumber < maxAttempts)
                {
                    var retryDelay = Math.Pow(2, attemptNumber - 1) * 5; // Exponential backoff
                    sb.AppendLine($" ↳ Retrying in {retryDelay} seconds...");
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
