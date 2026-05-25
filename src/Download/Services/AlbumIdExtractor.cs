using System;
using Lidarr.Plugin.Common.HostBridge;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Extracts Qobuz album IDs from release URLs and GUIDs.
    ///
    /// <para>Supported formats (primary — Common grammar, emitted since vX.Y.Z):</para>
    /// <list type="bullet">
    ///   <item>GUID: <c>qobuz:album:{albumId}[:edition={edition}][:quality={q}]</c></item>
    ///   <item>DownloadUrl: <c>qobuz://album/{albumId}?quality={q}</c></item>
    /// </list>
    ///
    /// <para>Legacy formats (still parsed as fallback for in-flight downloads queued before
    /// the migration, where the old GUID survives in Lidarr's database):</para>
    /// <list type="bullet">
    ///   <item>DownloadUrl: <c>qobuz://album/{albumId}/{quality}</c> (path-segment quality)</item>
    ///   <item>GUID: <c>qobuz-{albumId}-{quality}</c> (dash-delimited)</item>
    /// </list>
    /// </summary>
    public static class AlbumIdExtractor
    {
        private const string QobuzSchemePrefix = "qobuz://album/";
        private const string QobuzGuidDashPrefix = "qobuz-";
        private const string QobuzScheme = "qobuz";

        /// <summary>
        /// Extracts the Qobuz album ID from a release's DownloadUrl or Guid.
        ///
        /// <para>Strategy: try DownloadUrl first (primary), then GUID.
        /// Each leg tries the new Common-grammar format first, falls back to the legacy format
        /// so in-flight downloads from pre-migration releases continue to work.</para>
        /// </summary>
        /// <param name="release">The release info containing URL/GUID</param>
        /// <returns>The album ID if found, null otherwise</returns>
        public static string? ExtractAlbumId(ReleaseInfo? release)
        {
            if (release == null)
                return null;

            // --- DownloadUrl leg ---
            if (!string.IsNullOrEmpty(release.DownloadUrl) &&
                release.DownloadUrl.StartsWith(QobuzSchemePrefix, StringComparison.Ordinal))
            {
                var id = ExtractAlbumIdFromDownloadUrl(release.DownloadUrl);
                if (id != null) return id;
            }

            // --- GUID leg ---
            if (!string.IsNullOrEmpty(release.Guid))
            {
                // 1. Try Common-grammar parser first (qobuz:album:{id}[:extra])
                var fromNewGuid = PrefixedReleaseGuidParser.ExtractAlbumIdFromGuid(release.Guid, QobuzScheme);
                if (fromNewGuid != null) return fromNewGuid;

                // 2. Legacy dash-format fallback (qobuz-{id} or qobuz-{id}-{quality})
#pragma warning disable CS0618 // Type or member is obsolete — intentional internal fallback
                var fromLegacyGuid = ExtractAlbumIdFromLegacyGuid(release.Guid);
#pragma warning restore CS0618
                if (fromLegacyGuid != null) return fromLegacyGuid;
            }

            return null;
        }

        // -----------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Parses a <c>qobuz://album/{id}</c> or <c>qobuz://album/{id}?quality={q}</c> URL
        /// (new format) or the legacy path-segment quality form <c>qobuz://album/{id}/{quality}</c>.
        /// </summary>
        private static string? ExtractAlbumIdFromDownloadUrl(string downloadUrl)
        {
            return AlbumDownloadUri.TryExtractAlbumId(downloadUrl, QobuzScheme, out var id)
                ? id
                : null;
        }

        /// <summary>
        /// Legacy GUID parser for the pre-migration <c>qobuz-{albumId}</c> and
        /// <c>qobuz-{albumId}-{quality}</c> formats.
        /// </summary>
        /// <remarks>
        /// Marked Obsolete — new releases emit the Common-grammar GUID
        /// <c>qobuz:album:{id}[:edition={e}]:quality={q}</c>. This method is retained so
        /// Lidarr's database records from pre-vX.Y.Z releases continue to resolve correctly.
        /// It will be removed once in-flight downloads from the old format have drained
        /// (scheduled for removal in a future major version).
        /// </remarks>
        [Obsolete("Legacy GUID format (qobuz-{id}-{quality}). Use Common's PrefixedReleaseGuidParser for new GUIDs. This fallback remains for in-flight downloads from pre-migration releases and will be removed in a future major version.")]
        public static string? ExtractAlbumIdFromLegacyGuid(string? guid)
        {
            if (string.IsNullOrEmpty(guid) || !guid.StartsWith(QobuzGuidDashPrefix, StringComparison.Ordinal))
                return null;

            var guidPart = guid.Substring(QobuzGuidDashPrefix.Length);

            // Format is "qobuz-{albumId}-{quality}" — extract just the album ID by stripping the last dash segment.
            var lastDashIndex = guidPart.LastIndexOf('-');
            if (lastDashIndex > 0)
            {
                return guidPart.Substring(0, lastDashIndex);
            }

            // No dash after prefix — entire remainder is the album ID (bare "qobuz-{albumId}" form)
            return string.IsNullOrWhiteSpace(guidPart) ? null : guidPart;
        }
    }
}
