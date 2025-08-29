using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Metadata
{
    /// <summary>
    /// Strategy that uses rich Lidarr metadata for maximum optimization
    /// </summary>
    public class LidarrMetadataStrategy : IMetadataStrategy
    {
        private readonly Logger _logger;
        private readonly QobuzApiClient _qobuzApiClient;

        public string StrategyName => "Lidarr";

        public LidarrMetadataStrategy(
            Logger logger,
            QobuzApiClient qobuzApiClient)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _qobuzApiClient = qobuzApiClient ?? throw new ArgumentNullException(nameof(qobuzApiClient));
        }

        public bool CanHandle(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum)
        {
            // Can only use this strategy if we have Lidarr data
            return lidarrAlbum != null;
        }

        public async Task<MetadataDownloadResult> DownloadAlbumAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null)
        {
            if (lidarrAlbum == null)
                throw new InvalidOperationException("LidarrMetadataStrategy requires Lidarr album data");

            var downloads = new List<TrackDownload>();

            _logger.Debug("Starting optimized download using Lidarr metadata for {0} tracks", lidarrAlbum.TrackCount);

            // Match Lidarr tracks to Qobuz tracks for streaming URLs
            foreach (var lidarrTrack in lidarrAlbum.Tracks)
            {
                var qobuzTrack = FindBestQobuzTrackMatch(lidarrTrack, qobuzAlbum.GetTracks());

                if (qobuzTrack != null)
                {
                    // Only get streaming URL from Qobuz - use Lidarr for everything else
                    var streamingUrl = await GetStreamingUrlAsync(int.Parse(qobuzTrack.Id));
                    
                    var trackDownload = CreateTrackDownloadFromLidarr(streamingUrl, lidarrTrack, lidarrAlbum, qobuzTrack);
                    downloads.Add(trackDownload);
                    
                    _logger.Debug("Optimized track {0}: '{1}' (no metadata API call needed)", 
                                 lidarrTrack.TrackNumber, lidarrTrack.Title);
                }
                else
                {
                    _logger.Warn("Could not match Lidarr track '{0}' to any Qobuz track", lidarrTrack.Title);
                }
            }

            var apiCallsSaved = EstimateApiCallsSaved(lidarrAlbum);

            _logger.Info("Lidarr optimization complete: Used MusicBrainz metadata, saved {0} API calls", apiCallsSaved);

            return new MetadataDownloadResult 
            { 
                TrackDownloads = downloads,
                MetadataStrategy = "Lidarr",
                ApiCallsSaved = apiCallsSaved,
                AdditionalApiCalls = 0
            };
        }

        private QobuzTrack FindBestQobuzTrackMatch(LidarrTrack lidarrTrack, List<QobuzTrack> qobuzTracks)
        {
            // First try exact track number match
            var exactNumberMatch = qobuzTracks.FirstOrDefault(qt => 
                qt.TrackNumber == lidarrTrack.TrackNumber &&
                qt.DiscNumber == lidarrTrack.DiscNumber);

            if (exactNumberMatch != null)
            {
                var titleSimilarity = Utilities.StringSimilarity.Calculate(lidarrTrack.Title, exactNumberMatch.Title);
                if (titleSimilarity >= 0.7) // 70% minimum for exact number matches
                {
                    return exactNumberMatch;
                }
            }

            // Find best match by title and duration similarity
            QobuzTrack? bestMatch = null;
            double bestScore = 0;

            foreach (var qobuzTrack in qobuzTracks)
            {
                var score = CalculateTrackMatchScore(lidarrTrack, qobuzTrack);
                if (score > bestScore && score >= 0.8) // High threshold for title-based matching
                {
                    bestScore = score;
                    bestMatch = qobuzTrack;
                }
            }

            return bestMatch;
        }

        private double CalculateTrackMatchScore(LidarrTrack lidarrTrack, QobuzTrack qobuzTrack)
        {
            double score = 0;

            // Title similarity (70% weight)
            var titleSimilarity = Utilities.StringSimilarity.Calculate(lidarrTrack.Title, qobuzTrack.Title);
            score += titleSimilarity * 0.7;

            // Track number match (20% weight)
            if (lidarrTrack.TrackNumber == qobuzTrack.TrackNumber)
            {
                score += 0.2;
            }

            // Duration similarity (10% weight)
            if (lidarrTrack.Duration != TimeSpan.Zero && qobuzTrack.Duration != TimeSpan.Zero)
            {
                var durationDiff = Math.Abs(lidarrTrack.Duration.TotalSeconds - qobuzTrack.Duration.TotalSeconds);
                if (durationDiff <= 10) // 10 second tolerance
                {
                    var durationScore = 1.0 - (durationDiff / 10.0);
                    score += durationScore * 0.1;
                }
            }

            return score;
        }

        private TrackDownload CreateTrackDownloadFromLidarr(
            string streamingUrl, 
            LidarrTrack lidarrTrack, 
            LidarrAlbum lidarrAlbum, 
            QobuzTrack qobuzTrack)
        {
            return new TrackDownload
            {
                StreamingUrl = streamingUrl,
                QobuzTrackId = int.Parse(qobuzTrack.Id),
                
                // Rich Lidarr metadata (no additional API calls needed)
                Title = lidarrTrack.Title,
                Artist = lidarrTrack.ArtistName,
                AlbumArtist = lidarrAlbum.ArtistName,
                Album = lidarrAlbum.Title,
                TrackNumber = lidarrTrack.TrackNumber,
                DiscNumber = lidarrTrack.DiscNumber,
                Duration = lidarrTrack.Duration,
                ReleaseDate = lidarrAlbum.ReleaseDate,
                
                // Genre information from Lidarr
                Genre = string.Join(", ", lidarrAlbum.Genres ?? new List<string>()),
                
                // MusicBrainz identifiers for maximum metadata richness
                MusicBrainzTrackId = lidarrTrack.ForeignTrackId,
                MusicBrainzAlbumId = lidarrAlbum.ForeignAlbumId,
                MusicBrainzArtistId = lidarrAlbum.ArtistForeignId,
                MusicBrainzReleaseGroupId = lidarrAlbum.ForeignReleaseId,
                
                // Additional Lidarr metadata
                AlbumType = lidarrAlbum.AlbumType,
                Label = lidarrAlbum.Label,
                Country = lidarrAlbum.Country,
                
                // Quality from Qobuz (still needed for streaming)
                Quality = qobuzTrack.Quality,
                BitRate = qobuzTrack.BitRate,
                SampleRate = (int?)qobuzTrack.SampleRate,
                BitDepth = qobuzTrack.BitDepth,
                
                MetadataSource = "Lidarr+Qobuz"
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

        private int EstimateApiCallsSaved(LidarrAlbum lidarrAlbum)
        {
            // Saves: album details (1) + track list (1) + individual track metadata (N)
            return 2 + lidarrAlbum.TrackCount;
        }
    }
}