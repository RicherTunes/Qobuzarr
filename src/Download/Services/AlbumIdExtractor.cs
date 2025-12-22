using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Extracts Qobuz album IDs from release URLs and GUIDs.
    /// 
    /// Supported formats:
    /// - DownloadUrl: "qobuz://album/{albumId}" or "qobuz://album/{albumId}/{quality}"
    /// - Guid: "qobuz-{albumId}" or "qobuz-{albumId}-{quality}"
    /// </summary>
    public static class AlbumIdExtractor
    {
        /// <summary>
        /// Extracts the Qobuz album ID from a release's DownloadUrl or Guid.
        /// </summary>
        /// <param name="release">The release info containing URL/GUID</param>
        /// <returns>The album ID if found, null otherwise</returns>
        public static string? ExtractAlbumId(ReleaseInfo? release)
        {
            if (release == null)
                return null;

            // Try DownloadUrl first: "qobuz://album/{albumId}" or "qobuz://album/{albumId}/{quality}"
            if (!string.IsNullOrEmpty(release.DownloadUrl) && 
                release.DownloadUrl.StartsWith("qobuz://album/"))
            {
                var urlPart = release.DownloadUrl.Substring("qobuz://album/".Length);
                
                // Split by last slash to separate album ID from quality
                var lastSlashIndex = urlPart.LastIndexOf('/');
                if (lastSlashIndex > 0)
                {
                    return urlPart.Substring(0, lastSlashIndex);
                }
                
                // No slash - entire part is album ID
                return urlPart;
            }

            // Try GUID: "qobuz-{albumId}" or "qobuz-{albumId}-{quality}"
            if (!string.IsNullOrEmpty(release.Guid) && 
                release.Guid.StartsWith("qobuz-"))
            {
                var guidPart = release.Guid.Substring("qobuz-".Length);
                
                // GUID format is "qobuz-{albumId}-{quality}", extract just album ID
                var lastDashIndex = guidPart.LastIndexOf('-');
                if (lastDashIndex > 0)
                {
                    return guidPart.Substring(0, lastDashIndex);
                }
                
                return guidPart;
            }

            return null;
        }
    }
}
