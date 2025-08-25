using System;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Services;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Factory implementation for creating QobuzTrackDownloader instances with proper dependency injection.
    /// This replaces the dual constructor anti-pattern and provides a clean way to manage dependencies.
    /// Uses property injection for optional metadata optimizer to avoid circular dependencies.
    /// </summary>
    public class QobuzTrackDownloaderFactory : IQobuzTrackDownloaderFactory
    {
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IAudioFileDownloader _audioFileDownloader;
        private readonly IMetadataProcessor _metadataProcessor;
        private readonly IFilePathGenerator _filePathGenerator;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;
        private readonly IQobuzLogger _logger;
        
        /// <summary>
        /// Optional metadata optimizer that can be set via property injection to avoid circular dependencies.
        /// When set, all subsequently created track downloaders will use this optimizer.
        /// </summary>
        public ISafeMetadataOptimizer MetadataOptimizer { get; set; }

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
            // Create track downloader with optional metadata optimizer
            // The optimizer is injected via property to break circular dependency chain
            return new QobuzTrackDownloader(
                _streamUrlProvider,
                _audioFileDownloader,
                _metadataProcessor,
                _filePathGenerator,
                _qualityFallbackProvider,
                _logger,
                MetadataOptimizer); // Uses property-injected optimizer if available
        }
    }
}