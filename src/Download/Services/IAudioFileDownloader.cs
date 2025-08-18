using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for downloading audio files from streaming URLs
    /// </summary>
    public interface IAudioFileDownloader
    {
        /// <summary>
        /// Downloads an audio file from a streaming URL with progress reporting
        /// </summary>
        /// <param name="streamUrl">The streaming URL to download from</param>
        /// <param name="outputPath">The full path where the file should be saved</param>
        /// <param name="progress">Progress reporter (0-100%)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the download operation</returns>
        Task DownloadAudioFileAsync(
            string streamUrl,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Validates that a downloaded file is not corrupted and has expected properties
        /// </summary>
        /// <param name="filePath">Path to the downloaded file</param>
        /// <returns>True if file is valid, false otherwise</returns>
        bool ValidateDownloadedFile(string filePath);
    }
}