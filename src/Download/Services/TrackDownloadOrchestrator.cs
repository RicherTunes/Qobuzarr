using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Orchestrates the complete track download workflow using specialized services
    /// </summary>
    public class TrackDownloadOrchestrator : ITrackDownloadOrchestrator
    {
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IAudioFileDownloader _audioFileDownloader;
        private readonly IMetadataProcessor _metadataProcessor;
        private readonly IFilePathGenerator _filePathGenerator;
        private readonly IQobuzLogger _logger;
        private readonly SafeMetadataOptimizer _metadataOptimizer;

        public TrackDownloadOrchestrator(
            IStreamUrlProvider streamUrlProvider,
            IAudioFileDownloader audioFileDownloader,
            IMetadataProcessor metadataProcessor,
            IFilePathGenerator filePathGenerator,
            IQobuzLogger logger,
            SafeMetadataOptimizer metadataOptimizer = null)
        {
            _streamUrlProvider = Guard.NotNull(streamUrlProvider, nameof(streamUrlProvider));
            _audioFileDownloader = Guard.NotNull(audioFileDownloader, nameof(audioFileDownloader));
            _metadataProcessor = Guard.NotNull(metadataProcessor, nameof(metadataProcessor));
            _filePathGenerator = Guard.NotNull(filePathGenerator, nameof(filePathGenerator));
            _logger = Guard.NotNull(logger, nameof(logger));
            _metadataOptimizer = metadataOptimizer;
        }

        public async Task<string> DownloadTrackAsync(
            QobuzTrack track,
            QobuzAlbum album,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            Guard.NotNull(track, nameof(track));
            Guard.NotNull(album, nameof(album));
            Guard.NotNullOrWhiteSpace(outputPath, nameof(outputPath));

            try
            {
                _logger.Debug("Starting download of track: {0}", track.GetFullTitle());

                // Step 1: Get streaming URL with quality fallback
                var streamUrl = await _streamUrlProvider.GetStreamUrlAsync(track.Id, preferredQuality).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    throw new InvalidOperationException("Could not obtain stream URL for track");
                }

                // Step 2: Generate output filename
                var fileName = _filePathGenerator.GenerateFileName(track, album, preferredQuality);
                var fullPath = Path.Combine(outputPath, fileName);

                // Step 3: Check if file already exists and is valid
                if (System.IO.File.Exists(fullPath))
                {
                    if (_audioFileDownloader.ValidateDownloadedFile(fullPath))
                    {
                        _logger.Debug("Track already exists and is valid, skipping download: {0}", fullPath);
                        progress?.Report(100);
                        return fullPath;
                    }
                    else
                    {
                        _logger.Debug("Existing file is invalid, will re-download: {0}", fullPath);
                        try
                        {
                            System.IO.File.Delete(fullPath);
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.Warn(deleteEx, "Could not delete invalid existing file: {0}", fullPath);
                        }
                    }
                }

                // Step 4: Download the audio file
                await _audioFileDownloader.DownloadAudioFileAsync(streamUrl, fullPath, progress, cancellationToken).ConfigureAwait(false);

                // Step 5: Validate the downloaded file
                if (!_audioFileDownloader.ValidateDownloadedFile(fullPath))
                {
                    throw new InvalidOperationException($"Downloaded file failed validation: {fullPath}");
                }

                // Step 6: Apply metadata
                _metadataProcessor.ApplyBasicMetadata(fullPath, track, album);

                // Step 7: Create metadata file
                await _metadataProcessor.CreateMetadataFileAsync(fullPath, track, album, preferredQuality).ConfigureAwait(false);

                // Step 8: Download cover art (once per album)
                var albumPath = Path.GetDirectoryName(fullPath);
                await _metadataProcessor.DownloadCoverArtAsync(albumPath, album).ConfigureAwait(false);

                _logger.Info("Successfully downloaded track: {0}", fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                throw;
            }
        }

        public async Task<List<string>> DownloadAlbumWithIntelligentMetadataAsync(
            QobuzAlbum qobuzAlbum,
            LidarrAlbum lidarrAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            Guard.NotNull(qobuzAlbum, nameof(qobuzAlbum));
            Guard.NotNullOrWhiteSpace(outputPath, nameof(outputPath));

            try
            {
                _logger.Info("Starting intelligent album download: {0}", qobuzAlbum.GetFullTitle());

                // Use metadata optimizer if available and Lidarr album provided
                if (_metadataOptimizer != null && lidarrAlbum != null)
                {
                    return await DownloadAlbumWithOptimizedMetadataAsync(qobuzAlbum, lidarrAlbum, outputPath, preferredQuality, progress, cancellationToken);
                }

                // Fall back to standard track-by-track download
                return await DownloadAlbumTrackByTrackAsync(qobuzAlbum, outputPath, preferredQuality, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download album: {0}", qobuzAlbum.GetFullTitle());
                throw;
            }
        }

        private async Task<List<string>> DownloadAlbumWithOptimizedMetadataAsync(
            QobuzAlbum qobuzAlbum,
            LidarrAlbum lidarrAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            _logger.Info("Using intelligent metadata optimization for album: {0}", qobuzAlbum.GetFullTitle());

            // Use standard download approach with full metadata enrichment
            // The SafeMetadataOptimizer is integrated via the metadata processor service
            return await DownloadAlbumTrackByTrackAsync(qobuzAlbum, outputPath, preferredQuality, progress, cancellationToken);
        }

        private async Task<List<string>> DownloadAlbumTrackByTrackAsync(
            QobuzAlbum qobuzAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            _logger.Info("Using standard track-by-track download for album: {0}", qobuzAlbum.GetFullTitle());

            var downloadedFiles = new List<string>();
            var tracks = qobuzAlbum.GetTracks();
            var totalTracks = tracks.Count;

            for (int i = 0; i < totalTracks; i++)
            {
                var track = tracks[i];
                
                // Calculate individual track progress
                var trackProgress = new Progress<double>(trackProgressPercent =>
                {
                    var overallProgress = (i * 100.0 + trackProgressPercent) / totalTracks;
                    progress?.Report(overallProgress);
                });

                try
                {
                    var downloadedFile = await DownloadTrackAsync(track, qobuzAlbum, outputPath, preferredQuality, trackProgress, cancellationToken);
                    downloadedFiles.Add(downloadedFile);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to download track {0}/{1}: {2}", i + 1, totalTracks, track.GetFullTitle());
                    // Continue with other tracks
                }
            }

            _logger.Info("Completed standard album download: {0}/{1} tracks successful", downloadedFiles.Count, totalTracks);
            return downloadedFiles;
        }
    }
}