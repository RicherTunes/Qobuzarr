using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for downloading files from HTTP URLs with resume support and validation.
    /// Handles partial downloads, content validation, and atomic file operations.
    /// </summary>
    public interface IHttpFileDownloadService
    {
        /// <summary>
        /// Downloads a file from the specified URL to the given file path.
        /// Supports resume via Range headers, partial file handling, and content validation.
        /// </summary>
        /// <param name="url">The URL to download from</param>
        /// <param name="filePath">The destination file path</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Total number of bytes written to the file</returns>
        Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken);
    }
}
