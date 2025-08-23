using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;

namespace Lidarr.Plugin.Qobuzarr.Services.Metadata
{
    /// <summary>
    /// Strategy that uses Qobuz metadata exclusively (fallback strategy)
    /// </summary>
    public class QobuzMetadataStrategy : IMetadataStrategy
    {
        private readonly Logger _logger;
        private readonly QobuzApiClient _qobuzApiClient;

        public string StrategyName => Constants.QobuzarrConstants.ServiceName;

        public QobuzMetadataStrategy(
            Logger logger,
            QobuzApiClient qobuzApiClient)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _qobuzApiClient = qobuzApiClient ?? throw new ArgumentNullException(nameof(qobuzApiClient));
        }

        public bool CanHandle(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum)
        {
            // Can always use Qobuz metadata as fallback
            return qobuzAlbum != null;
        }

        public async Task<MetadataDownloadResult> DownloadAlbumAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null)
        {
            if (qobuzAlbum == null)
                throw new ArgumentNullException(nameof(qobuzAlbum));

            var downloads = new List<TrackDownload>();

            _logger.Debug("Starting standard download using Qobuz metadata for {0} tracks", qobuzAlbum.TracksCount);

            foreach (var qobuzTrack in qobuzAlbum.GetTracks())
            {
                var streamingUrl = await GetStreamingUrlAsync(int.Parse(qobuzTrack.Id));
                var qobuzMetadata = await GetQobuzTrackAsync(int.Parse(qobuzTrack.Id));
                
                var trackDownload = CreateTrackDownloadFromQobuz(streamingUrl, qobuzTrack, qobuzMetadata);
                downloads.Add(trackDownload);
            }

            _logger.Info("Qobuz metadata: Standard download completed for {0} tracks", downloads.Count);

            return new MetadataDownloadResult 
            { 
                TrackDownloads = downloads,
                MetadataStrategy = Constants.QobuzarrConstants.ServiceName,
                ApiCallsSaved = 0,
                AdditionalApiCalls = qobuzAlbum.TracksCount + 2 // Track metadata + album details + tracklist
            };
        }

        private TrackDownload CreateTrackDownloadFromQobuz(
            string streamingUrl, 
            QobuzTrack qobuzTrack, 
            QobuzTrack metadata)
        {
            return new TrackDownload
            {
                StreamingUrl = streamingUrl,
                QobuzTrackId = int.Parse(qobuzTrack.Id),
                
                // Qobuz metadata
                Title = metadata.Title ?? qobuzTrack.Title,
                Artist = metadata.Performer?.Name ?? qobuzTrack.GetPerformerName(),
                AlbumArtist = metadata.Album?.Artist?.Name ?? qobuzTrack.AlbumArtistName,
                Album = metadata.Album?.Title ?? qobuzTrack.AlbumTitle,
                TrackNumber = qobuzTrack.TrackNumber,
                DiscNumber = qobuzTrack.DiscNumber,
                Duration = qobuzTrack.Duration,
                ReleaseDate = metadata.Album?.ReleaseDate,
                
                // Genre from Qobuz  
                Genre = metadata.Album?.GenresList?.FirstOrDefault(),
                
                // Composer and additional credits
                Composer = metadata.Composer?.Name,
                
                // Quality information
                Quality = qobuzTrack.Quality,
                BitRate = qobuzTrack.BitRate,
                SampleRate = (int?)qobuzTrack.SampleRate,
                BitDepth = qobuzTrack.BitDepth,
                
                // Label information
                Label = metadata.Album?.Label?.Name,
                
                MetadataSource = Constants.QobuzarrConstants.ServiceName
            };
        }

        private async Task<string> GetStreamingUrlAsync(int trackId)
        {
            try
            {
                return await _qobuzApiClient.GetStreamingUrlAsync(trackId.ToString(), 6); // Default to FLAC format
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get streaming URL for track {0}", trackId);
                throw;
            }
        }

        private async Task<QobuzTrack> GetQobuzTrackAsync(int trackId)
        {
            try
            {
                return await _qobuzApiClient.GetTrackMetadataAsync(trackId.ToString());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get track metadata for track {0}", trackId);
                throw;
            }
        }
    }
}