using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for exporting Lidarr wanted albums in various formats.
    /// Part of the plugin-first architecture where business logic resides in the plugin.
    /// </summary>
    public interface ILidarrExportService
    {
        /// <summary>
        /// Exports wanted albums to specified format.
        /// </summary>
        /// <param name="albums">Albums to export.</param>
        /// <param name="format">Export format (json, csv, txt).</param>
        /// <param name="includeMetadata">Whether to include detailed metadata.</param>
        /// <returns>Serialized export data.</returns>
        Task<string> ExportAlbumsAsync(
            IEnumerable<LidarrAlbum> albums,
            ExportFormat format,
            bool includeMetadata = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Optimizes album order for efficient processing.
        /// </summary>
        /// <param name="albums">Albums to optimize.</param>
        /// <returns>Optimized album list.</returns>
        IEnumerable<LidarrAlbum> OptimizeAlbumOrder(IEnumerable<LidarrAlbum> albums);

        /// <summary>
        /// Creates export metadata for an album.
        /// </summary>
        /// <param name="album">Album to export.</param>
        /// <param name="includeMetadata">Whether to include detailed metadata.</param>
        /// <returns>Export metadata dictionary.</returns>
        Dictionary<string, object?> CreateAlbumExportData(LidarrAlbum album, bool includeMetadata);
    }

    /// <summary>
    /// Supported export formats.
    /// </summary>
    public enum ExportFormat
    {
        Json,
        Csv,
        Txt
    }
}