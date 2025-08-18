using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using TagLib;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Handles the complete download process for Qobuz music tracks including streaming URL acquisition,
    /// file downloading with progress tracking, and comprehensive metadata embedding.
    /// This is the authoritative implementation used by both the plugin and CLI for track downloads.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Automatic quality fallback when preferred quality is unavailable
    /// - Progress reporting during downloads
    /// - Comprehensive metadata embedding using TagLibSharp
    /// - File validation and error handling
    /// - Support for all Qobuz quality formats (MP3 320, FLAC CD, FLAC Hi-Res)
    /// - Restriction checking for geo-blocked or subscription-limited content
    /// </remarks>
    public class QobuzTrackDownloader : ITrackDownloadOrchestrator
    {
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IAudioFileDownloader _audioFileDownloader;
        private readonly IMetadataProcessor _metadataProcessor;
        private readonly IFilePathGenerator _filePathGenerator;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;
        private readonly IQobuzLogger _logger;
        private readonly ISafeMetadataOptimizer _metadataOptimizer;

        /// <summary>
        /// Initializes a new instance of QobuzTrackDownloader with proper dependency injection.
        /// All dependencies are provided through constructor injection for testability and maintainability.
        /// </summary>
        /// <param name="streamUrlProvider">Service for obtaining streaming URLs from Qobuz API</param>
        /// <param name="audioFileDownloader">Service for downloading audio files</param>
        /// <param name="metadataProcessor">Service for processing and embedding metadata</param>
        /// <param name="filePathGenerator">Service for generating file paths and names</param>
        /// <param name="qualityFallbackProvider">Service for handling quality fallback logic</param>
        /// <param name="logger">Logger for recording operations and debugging</param>
        /// <param name="metadataOptimizer">Optional metadata optimizer for advanced metadata handling</param>
        public QobuzTrackDownloader(
            IStreamUrlProvider streamUrlProvider,
            IAudioFileDownloader audioFileDownloader,
            IMetadataProcessor metadataProcessor,
            IFilePathGenerator filePathGenerator,
            IQualityFallbackProvider qualityFallbackProvider,
            IQobuzLogger logger,
            ISafeMetadataOptimizer metadataOptimizer = null)
        {
            _streamUrlProvider = streamUrlProvider ?? throw new ArgumentNullException(nameof(streamUrlProvider));
            _audioFileDownloader = audioFileDownloader ?? throw new ArgumentNullException(nameof(audioFileDownloader));
            _metadataProcessor = metadataProcessor ?? throw new ArgumentNullException(nameof(metadataProcessor));
            _filePathGenerator = filePathGenerator ?? throw new ArgumentNullException(nameof(filePathGenerator));
            _qualityFallbackProvider = qualityFallbackProvider ?? throw new ArgumentNullException(nameof(qualityFallbackProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metadataOptimizer = metadataOptimizer;
        }

        /// <summary>
        /// Downloads a complete track from Qobuz with metadata embedding and quality fallback.
        /// This is the main public API used by both the plugin and CLI applications.
        /// </summary>
        /// <param name="track">The track metadata from Qobuz API containing track information.</param>
        /// <param name="album">The album metadata containing album-level information for metadata embedding.</param>
        /// <param name="outputPath">The directory where the track file should be saved.</param>
        /// <param name="preferredQuality">The preferred quality format ID (5=MP3 320, 6=FLAC CD, 7=FLAC 24/96, 27=FLAC 24/192).</param>
        /// <param name="progress">Optional progress reporter for download progress (0-100%).</param>
        /// <param name="cancellationToken">Token to cancel the download operation.</param>
        /// <returns>The full path to the downloaded and tagged audio file.</returns>
        /// <exception cref="InvalidOperationException">Thrown when streaming URL cannot be obtained or track is restricted.</exception>
        /// <exception cref="HttpException">Thrown when the audio file download fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
        public async Task<string> DownloadTrackAsync(
            QobuzTrack track, 
            QobuzAlbum album,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                // Check cancellation at the start
                cancellationToken.ThrowIfCancellationRequested();
                
                _logger.Debug("Starting download of track: {0}", track.GetFullTitle());

                // Get stream URL from Qobuz API
                var streamUrl = await _streamUrlProvider.GetStreamUrlAsync(track.Id, preferredQuality).ConfigureAwait(false);
                    
                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    throw new InvalidOperationException("Could not obtain stream URL for track");
                }

                // Generate output filename
                var fileName = _filePathGenerator.GenerateFileName(track, album, preferredQuality);
                var fullPath = Path.Combine(outputPath, fileName);

                // Check if file already exists and is valid
                if (System.IO.File.Exists(fullPath))
                {
                    var isValid = _audioFileDownloader.ValidateDownloadedFile(fullPath);
                        
                    if (isValid)
                    {
                        _logger.Debug("Track already exists and is valid, skipping download: {0}", fullPath);
                        progress?.Report(100); // Report complete
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
                            _logger.Warn(deleteEx, "Failed to delete invalid existing file: {0}", fullPath);
                        }
                    }
                }

                // Download the audio file
                await _audioFileDownloader.DownloadAudioFileAsync(streamUrl, fullPath, progress, cancellationToken).ConfigureAwait(false);

                // Apply basic metadata
                _metadataProcessor.ApplyBasicMetadata(fullPath, track, album);
                
                // Create JSON metadata file
                await _metadataProcessor.CreateMetadataFileAsync(fullPath, track, album, preferredQuality).ConfigureAwait(false);
                
                // Download cover art (once per album)
                await _metadataProcessor.DownloadCoverArtAsync(outputPath, album).ConfigureAwait(false);

                _logger.Debug("Successfully downloaded track: {0}", track.GetFullTitle());
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                throw;
            }
        }

        /// <summary>
        /// Downloads a complete album using intelligent metadata optimization when available.
        /// This method provides the best balance of performance and data integrity.
        /// </summary>
        /// <param name="qobuzAlbum">The album from Qobuz with track and streaming information</param>
        /// <param name="lidarrAlbum">Optional album from Lidarr with MusicBrainz metadata</param>
        /// <param name="outputPath">The directory where album files should be saved</param>
        /// <param name="preferredQuality">The preferred quality format ID</param>
        /// <param name="albumProgress">Optional progress reporter for overall album download</param>
        /// <param name="cancellationToken">Token to cancel the download operation</param>
        /// <returns>List of successfully downloaded track file paths</returns>
        public async Task<List<string>> DownloadAlbumWithIntelligentMetadataAsync(
            QobuzAlbum qobuzAlbum,
            LidarrAlbum lidarrAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> albumProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (qobuzAlbum == null)
                throw new ArgumentNullException(nameof(qobuzAlbum));

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));

            _logger.Info("Starting intelligent album download: '{0}' by '{1}' ({2} tracks)", 
                        qobuzAlbum.Title, qobuzAlbum.GetArtistName(), qobuzAlbum.TracksCount);

            var downloadedFiles = new List<string>();

            try
            {
                // Use intelligent metadata optimization if available
                if (_metadataOptimizer != null)
                {
                    _logger.Debug("Using SafeMetadataOptimizer for metadata strategy selection");
                    var downloadResult = await _metadataOptimizer.DownloadAlbumSafelyAsync(qobuzAlbum, lidarrAlbum);
                    
                    if (downloadResult?.TrackDownloads?.Any() == true)
                    {
                        _logger.Info("🎯 INTELLIGENT METADATA: Using {0} strategy, saved {1} API calls", 
                                    downloadResult.MetadataStrategy, downloadResult.ApiCallsSaved);

                        // Download tracks using optimized metadata
                        var trackCount = 0;
                        foreach (var trackDownload in downloadResult.TrackDownloads)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var trackProgress = new Progress<double>(percent =>
                            {
                                var overallProgress = ((trackCount + percent / 100.0) / downloadResult.TrackDownloads.Count) * 100.0;
                                albumProgress?.Report(overallProgress);
                            });

                            var filePath = await DownloadTrackWithOptimizedMetadataAsync(
                                trackDownload, outputPath, preferredQuality, trackProgress, cancellationToken);

                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                downloadedFiles.Add(filePath);
                                _logger.Debug("✅ Downloaded track {0}/{1}: {2}", 
                                             trackCount + 1, downloadResult.TrackDownloads.Count, 
                                             Path.GetFileName(filePath));
                            }

                            trackCount++;
                        }

                        // Download album cover art once
                        await DownloadCoverArtAsync(outputPath, qobuzAlbum);
                        
                        albumProgress?.Report(100);
                        
                        _logger.Info("✅ INTELLIGENT ALBUM DOWNLOAD COMPLETE: {0} tracks downloaded using {1} metadata strategy", 
                                    downloadedFiles.Count, downloadResult.MetadataStrategy);

                        // Log optimization statistics periodically
                        if (downloadedFiles.Count > 0)
                        {
                            _metadataOptimizer.LogOptimizationStatistics();
                        }

                        return downloadedFiles;
                    }
                }

                // Fallback to standard track-by-track download
                _logger.Debug("Using standard track-by-track download approach");
                return await DownloadAlbumTrackByTrackAsync(qobuzAlbum, outputPath, preferredQuality, albumProgress, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download album: {0}", qobuzAlbum.GetFullTitle());
                throw;
            }
        }

        /// <summary>
        /// Downloads a single track using pre-optimized metadata from the intelligent system
        /// </summary>
        private async Task<string> DownloadTrackWithOptimizedMetadataAsync(
            TrackDownload trackDownload,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.Debug("Downloading optimized track: {0}", trackDownload.Title);

                // Generate filename using optimized metadata
                var fileName = GenerateOptimizedFileName(trackDownload, preferredQuality);
                var fullPath = Path.Combine(outputPath, fileName);

                // Check if file already exists and is valid
                if (System.IO.File.Exists(fullPath) && ValidateDownloadedFile(fullPath))
                {
                    _logger.Debug("Track already exists and is valid: {0}", fullPath);
                    progress?.Report(100);
                    return fullPath;
                }

                // Download audio file using streaming URL from optimization result
                await DownloadAudioFileAsync(trackDownload.StreamingUrl, fullPath, progress, cancellationToken);

                // Apply optimized metadata (already enriched with Lidarr/Qobuz data)
                ApplyOptimizedMetadata(fullPath, trackDownload);

                // Create optimized metadata JSON file
                await CreateOptimizedMetadataFileAsync(fullPath, trackDownload);

                _logger.Debug("Successfully downloaded optimized track: {0}", trackDownload.Title);
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download optimized track: {0}", trackDownload.Title);
                throw;
            }
        }

        /// <summary>
        /// Downloads album using standard track-by-track approach (fallback)
        /// </summary>
        private async Task<List<string>> DownloadAlbumTrackByTrackAsync(
            QobuzAlbum qobuzAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> albumProgress,
            CancellationToken cancellationToken)
        {
            var downloadedFiles = new List<string>();
            var trackCount = 0;

            foreach (var track in qobuzAlbum.GetTracks())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trackProgress = new Progress<double>(percent =>
                {
                    var overallProgress = ((trackCount + percent / 100.0) / qobuzAlbum.TracksCount) * 100.0;
                    albumProgress?.Report(overallProgress);
                });

                try
                {
                    var filePath = await DownloadTrackAsync(track, qobuzAlbum, outputPath, preferredQuality, trackProgress, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        downloadedFiles.Add(filePath);
                        _logger.Debug("✅ Downloaded track {0}/{1}: {2}", 
                                     trackCount + 1, qobuzAlbum.TracksCount, Path.GetFileName(filePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to download track {0}: {1}", trackCount + 1, track.GetFullTitle());
                    // Continue with other tracks
                }

                trackCount++;
            }

            albumProgress?.Report(100);
            _logger.Info("📀 STANDARD ALBUM DOWNLOAD COMPLETE: {0} of {1} tracks downloaded", 
                        downloadedFiles.Count, qobuzAlbum.TracksCount);

            return downloadedFiles;
        }

        private async Task<string> GetStreamUrlAsync(string trackId, int preferredQuality)
        {
            // CRITICAL: Always delegate to the service to avoid null reference exceptions
            // Both constructors now create the service, so this is safe
            return await _streamUrlProvider.GetStreamUrlAsync(trackId, preferredQuality).ConfigureAwait(false);
        }




        private async Task DownloadAudioFileAsync(
            string streamUrl, 
            string outputPath, 
            IProgress<double> progress, 
            CancellationToken cancellationToken)
        {
            // CRITICAL: Always delegate to the service to avoid null reference exceptions
            // Both constructors now create the service, so this is safe
            await _audioFileDownloader.DownloadAudioFileAsync(streamUrl, outputPath, progress, cancellationToken).ConfigureAwait(false);
        }


        private void ApplyBasicMetadata(string filePath, QobuzTrack track, QobuzAlbum album)
        {
            // Delegate to the service for consistency
            _metadataProcessor.ApplyBasicMetadata(filePath, track, album);
        }

        private string GetQualityDescription(QobuzTrack track)
        {
            // Delegate to the service for consistency
            return _filePathGenerator.GetQualityDescription(track);
        }

        private string GenerateFileName(QobuzTrack track, QobuzAlbum album, int formatId)
        {
            // Delegate to the service for consistency
            return _filePathGenerator.GenerateFileName(track, album, formatId);
        }

        private string GetFileExtension(int formatId)
        {
            // Delegate to the service for consistency
            return _filePathGenerator.GetFileExtension(formatId);
        }

        /// <summary>
        /// Validate that a downloaded file is not corrupted
        /// </summary>
        public bool ValidateDownloadedFile(string filePath)
        {
            // Delegate to the service for consistency
            return _audioFileDownloader.ValidateDownloadedFile(filePath);
        }

        /// <summary>
        /// Creates a JSON metadata file alongside the downloaded track
        /// </summary>
        private async Task CreateMetadataFileAsync(string trackFilePath, QobuzTrack track, QobuzAlbum album, int formatId)
        {
            // Delegate to the service for consistency
            await _metadataProcessor.CreateMetadataFileAsync(trackFilePath, track, album, formatId).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads cover art for the album (once per album)
        /// </summary>
        private async Task DownloadCoverArtAsync(string albumPath, QobuzAlbum album)
        {
            // CRITICAL: Always delegate to the service to avoid null reference exceptions
            // Both constructors now create the service, so this is safe
            await _metadataProcessor.DownloadCoverArtAsync(albumPath, album).ConfigureAwait(false);
        }

        #region Optimized Metadata Methods

        /// <summary>
        /// Generates filename using optimized metadata from the intelligent system
        /// </summary>
        private string GenerateOptimizedFileName(TrackDownload trackDownload, int quality)
        {
            var extension = GetFileExtension(quality);
            var sanitizedArtist = FileNameUtility.SanitizeFileName(trackDownload.Artist ?? "Unknown Artist");
            var sanitizedTitle = FileNameUtility.SanitizeFileName(trackDownload.Title ?? "Unknown Track");
            var trackNumber = trackDownload.TrackNumber?.ToString("D2") ?? "00";

            return $"{trackNumber}. {sanitizedArtist} - {sanitizedTitle}.{extension}";
        }

        /// <summary>
        /// Applies optimized metadata to the downloaded audio file using TagLib
        /// </summary>
        private void ApplyOptimizedMetadata(string filePath, TrackDownload trackDownload)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                var tag = file.Tag;

                // Basic track information (always available)
                tag.Title = trackDownload.Title;
                tag.AlbumArtists = new[] { trackDownload.AlbumArtist ?? trackDownload.Artist };
                tag.Performers = new[] { trackDownload.Artist };
                tag.Album = trackDownload.Album;
                
                // Track/disc numbers
                if (trackDownload.TrackNumber.HasValue)
                    tag.Track = (uint)trackDownload.TrackNumber.Value;
                if (trackDownload.DiscNumber.HasValue)
                    tag.Disc = (uint)trackDownload.DiscNumber.Value;

                // Release information
                if (trackDownload.ReleaseDate.HasValue)
                    tag.Year = (uint)trackDownload.ReleaseDate.Value.Year;

                // Genre information
                if (!string.IsNullOrWhiteSpace(trackDownload.Genre))
                    tag.Genres = trackDownload.Genre.Split(',').Select(g => g.Trim()).ToArray();

                // MusicBrainz identifiers (when available from Lidarr)
                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzTrackId))
                    tag.MusicBrainzTrackId = trackDownload.MusicBrainzTrackId;
                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzAlbumId))
                    tag.MusicBrainzReleaseId = trackDownload.MusicBrainzAlbumId;
                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzArtistId))
                    tag.MusicBrainzArtistId = trackDownload.MusicBrainzArtistId;
                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzReleaseGroupId))
                    tag.MusicBrainzReleaseGroupId = trackDownload.MusicBrainzReleaseGroupId;

                // Additional metadata (when available)
                if (!string.IsNullOrWhiteSpace(trackDownload.Composer))
                    tag.Composers = new[] { trackDownload.Composer };
                if (!string.IsNullOrWhiteSpace(trackDownload.Label))
                    tag.Publisher = trackDownload.Label;
                if (!string.IsNullOrWhiteSpace(trackDownload.Country))
                    tag.Comment = $"Country: {trackDownload.Country}";

                // Quality information as custom field
                var qualityInfo = $"Source: {trackDownload.MetadataSource}";
                if (!string.IsNullOrWhiteSpace(trackDownload.Quality))
                    qualityInfo += $", Quality: {trackDownload.Quality}";
                if (trackDownload.BitRate.HasValue)
                    qualityInfo += $", BitRate: {trackDownload.BitRate}kbps";
                if (trackDownload.SampleRate.HasValue)
                    qualityInfo += $", SampleRate: {trackDownload.SampleRate}Hz";
                
                tag.Comment = string.IsNullOrWhiteSpace(tag.Comment) 
                    ? qualityInfo 
                    : $"{tag.Comment}; {qualityInfo}";

                file.Save();

                _logger.Debug("Applied optimized metadata from {0} to: {1}", 
                             trackDownload.MetadataSource, Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply optimized metadata to: {0}", filePath);
                // Don't throw - file is downloaded, metadata is nice to have but not critical
            }
        }

        /// <summary>
        /// Creates optimized JSON metadata file with rich information from the intelligent system
        /// </summary>
        private async Task CreateOptimizedMetadataFileAsync(string trackFilePath, TrackDownload trackDownload)
        {
            try
            {
                var metadata = new QobuzTrackMetadata
                {
                    Title = trackDownload.Title,
                    Artist = trackDownload.Artist,
                    Album = trackDownload.Album,
                    AlbumArtist = trackDownload.AlbumArtist ?? trackDownload.Artist,
                    TrackNumber = trackDownload.TrackNumber ?? 0,
                    DiscNumber = trackDownload.DiscNumber ?? 1,
                    Duration = trackDownload.Duration,
                    Year = trackDownload.ReleaseDate?.Year,
                    Genre = trackDownload.Genre,
                    Composer = trackDownload.Composer ?? string.Empty,
                    Label = trackDownload.Label ?? string.Empty,
                    Comment = $"Source: {trackDownload.MetadataSource}, Quality: {trackDownload.Quality}"
                };

                var metadataFilePath = Path.ChangeExtension(trackFilePath, ".json");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(metadata, Newtonsoft.Json.Formatting.Indented);
                await System.IO.File.WriteAllTextAsync(metadataFilePath, json).ConfigureAwait(false);
                
                _logger.Debug("Created optimized metadata file with {0} data for: {1}", 
                             trackDownload.MetadataSource, Path.GetFileName(trackFilePath));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to create optimized metadata file for: {0}", Path.GetFileName(trackFilePath));
                // Don't throw - metadata is nice to have but not essential
            }
        }

        #endregion
    }
}