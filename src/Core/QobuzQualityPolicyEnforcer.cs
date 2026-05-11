using System;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Gates the post-API quality selection against the user's
    /// "Enable Quality Fallback" preference.
    ///
    /// Background: Qobuz returns the best quality it can license to the user
    /// for a given track. Sometimes that's lower than what the user asked for
    /// — e.g. requested format-id 27 (FLAC 192/24) but the track is only
    /// available at format-id 6 (FLAC CD). Previously the download path
    /// silently accepted this and produced a CD-quality file with no warning.
    ///
    /// Users who pay for top-tier subscriptions have legitimate reasons to
    /// reject fallback ("I want HiRes or nothing"). This enforcer surfaces
    /// the mismatch as an InvalidOperationException when strict mode is on,
    /// so the download fails fast with an actionable message.
    /// </summary>
    public static class QobuzQualityPolicyEnforcer
    {
        /// <summary>
        /// Verify that the selected quality is acceptable under the user's policy.
        /// </summary>
        /// <param name="selectedQuality">Quality the Qobuz API actually returned.</param>
        /// <param name="preferredQuality">Quality the user requested.</param>
        /// <param name="allowFallback">When <c>true</c>, accept any quality the
        /// API returns (current default). When <c>false</c>, throw on mismatch.</param>
        public static void Enforce(int selectedQuality, int preferredQuality, bool allowFallback)
        {
            if (allowFallback) return;
            if (selectedQuality == preferredQuality) return;

            throw new InvalidOperationException(
                $"Strict quality mode: requested format {preferredQuality} but Qobuz returned {selectedQuality}. " +
                $"Enable 'Quality Fallback' in download settings to accept lower qualities, or pick a quality your subscription supports.");
        }
    }
}
