using System;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Services;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Factory implementation for creating QobuzTrackDownloader instances with proper dependency injection
    /// This replaces the dual constructor anti-pattern and provides a clean way to manage dependencies
    /// </summary>
    public class QobuzTrackDownloaderFactory : IQobuzTrackDownloaderFactory
    {
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IAudioFileDownloader _audioFileDownloader;
        private readonly IMetadataProcessor _metadataProcessor;
        private readonly IFilePathGenerator _filePathGenerator;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;
        private readonly IQobuzLogger _logger;
        // NOTE: ISafeMetadataOptimizer removed from constructor to break circular dependency
        // It will be injected via setter or passed as parameter when needed

        public QobuzTrackDownloaderFactory(
            IStreamUrlProvider streamUrlProvider,
            IAudioFileDownloader audioFileDownloader,
            IMetadataProcessor metadataProcessor,
            IFilePathGenerator filePathGenerator,
            IQualityFallbackProvider qualityFallbackProvider,
            IQobuzLogger logger)
        {
            _streamUrlProvider = streamUrlProvider ?? throw new ArgumentNullException(nameof(streamUrlProvider));
            _audioFileDownloader = audioFileDownloader ?? throw new ArgumentNullException(nameof(audioFileDownloader));
            _metadataProcessor = metadataProcessor ?? throw new ArgumentNullException(nameof(metadataProcessor));
            _filePathGenerator = filePathGenerator ?? throw new ArgumentNullException(nameof(filePathGenerator));
            _qualityFallbackProvider = qualityFallbackProvider ?? throw new ArgumentNullException(nameof(qualityFallbackProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public QobuzTrackDownloader CreateTrackDownloader()
        {
            // For now, create without metadata optimizer until we implement proper injection
            // This prevents circular dependency issues
            return new QobuzTrackDownloader(
                _streamUrlProvider,
                _audioFileDownloader,
                _metadataProcessor,
                _filePathGenerator,
                _qualityFallbackProvider,
                _logger,
                null); // Metadata optimizer will be added via separate mechanism
        }

        public QobuzTrackDownloader CreateSimpleTrackDownloader()
        {
            // Create a downloader without metadata optimizer to avoid circular dependencies
            // This is used by IntelligentReleaseMapper which doesn't need optimization
            return new QobuzTrackDownloader(
                _streamUrlProvider,
                _audioFileDownloader,
                _metadataProcessor,
                _filePathGenerator,
                _qualityFallbackProvider,
                _logger,
                null); // No metadata optimizer to break circular dependency
        }

        // Legacy method removed - use CreateTrackDownloader() instead
    }
}