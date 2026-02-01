using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services.Adapters;
using QobuzCLI.Services.Logging;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Spectre.Console;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Orchestrates the complete download process for a single album or artist.
    /// Handles state management, progress tracking, and plugin host coordination.
    /// Extracted from DownloadCommand to follow Single Responsibility Principle.
    /// </summary>
    public class DownloadOrchestrator
    {
        private readonly IConfigService _configService;
        private readonly IPluginHost _pluginHost;
        private readonly IStateService _stateService;
        private readonly IDashboardLogger _logger;

        public DownloadOrchestrator(
            IConfigService configService,
            IPluginHost pluginHost,
            IStateService stateService,
            IDashboardLogger logger)
        {
            _configService = configService;
            _pluginHost = pluginHost;
            _stateService = stateService;
            _logger = logger;
        }

        /// <summary>
        /// Executes a complete download workflow for a search result.
        /// Handles directory creation, existence checking, state management, and progress tracking.
        /// </summary>
        /// <param name="downloadId">Unique identifier for tracking this download.</param>
        /// <param name="result">The search result to download (album or artist).</param>
        /// <param name="outputDir">Base output directory for downloads.</param>
        /// <param name="quality">Quality preference (can be null to use config default).</param>
        /// <param name="progressTask">Optional progress task for UI updates.</param>
        /// <returns>Result indicating success/failure and details about the download.</returns>
        public async Task<CliDownloadResult> ExecuteDownloadAsync(
            string downloadId,
            SearchResult result,
            string outputDir,
            string? quality,
            ProgressTask? progressTask)
        {
            _logger.LogInformation("Starting download: {Title} by {Artist}", result.Title, result.Artist);
            _logger.LogToDashboard($"📥 Starting download: {result.Title} by {result.Artist}", LogLevel.Information);

            // Create output directory structure
            var artistDir = Path.Combine(outputDir, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.SanitizeFileName(result.Artist));
            var albumDir = Path.Combine(artistDir, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.CreateAlbumDirectoryName(result.Title, result.Year));

            // Check if album already exists with adequate quality
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            if (config.EnableLocalCache && result.Type.ToLower() != "artist")
            {
                var (alreadyExists, existingTrackCount, reason) = await _pluginHost.CheckExistingAlbumAsync(
                    result.Id, albumDir, quality ?? config.Quality).ConfigureAwait(false);

                if (alreadyExists)
                {
                    _logger.LogInformation("Album already exists with adequate quality: {Reason}", reason);
                    _logger.LogToDashboard($"⚠️ Skipped: {result.Title} - {reason}", LogLevel.Warning);
                    if (progressTask != null) progressTask.Value = 100;
                    await _stateService.UpdateDownloadProgressAsync(downloadId, 100, $"Skipped - {reason}").ConfigureAwait(false);

                    var skipResult = new CliDownloadResult
                    {
                        Success = false,
                        Message = $"Skipped - {reason}",
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        TrackDownloads = new List<TrackDownloadInfo>(),
                        MetadataStrategy = "Skipped - Already Exists",
                        ApiCallsSaved = 0,
                        AdditionalApiCalls = 0
                    };
                    return skipResult;
                }
            }

            Directory.CreateDirectory(albumDir);

            // Use the plugin host for actual download - check type
            CliDownloadResult downloadResult;
            if (result.Type.ToLower() == "artist")
            {
                _logger.LogInformation("Downloading artist: {Artist}", result.Artist);
                _logger.LogToDashboard($"🎤 Downloading artist: {result.Artist}", LogLevel.Information);
                downloadResult = await _pluginHost.DownloadArtistAsync(result.Id, artistDir).ConfigureAwait(false);
            }
            else
            {
                _logger.LogToDashboard($"💿 Downloading album: {result.Title}", LogLevel.Information);
                downloadResult = await _pluginHost.DownloadAlbumAsync(result.Id, albumDir, quality).ConfigureAwait(false);
            }

            if (downloadResult.IsSuccessful())
            {
                _logger.LogToDashboard($"✅ Completed: {result.Title} by {result.Artist}", LogLevel.Information);
                if (progressTask != null) progressTask.Value = 100;
                await _stateService.UpdateDownloadProgressAsync(downloadId, 100, "Download completed").ConfigureAwait(false);
            }
            else
            {
                if (progressTask != null) progressTask.Value = 0;
                await _stateService.UpdateDownloadProgressAsync(downloadId, 0, $"Download failed: {downloadResult.GetSummaryMessage()}").ConfigureAwait(false);
            }

            return downloadResult;
        }
    }
}
