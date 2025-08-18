using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Metadata
{
    /// <summary>
    /// Strategy that combines Lidarr metadata for matched tracks and Qobuz metadata for unmatched tracks
    /// </summary>
    public class HybridMetadataStrategy : IMetadataStrategy
    {
        private readonly Logger _logger;
        private readonly QobuzApiClient _qobuzApiClient;
        private readonly IntelligentReleaseMapper _releaseMapper;

        public string StrategyName => "Hybrid";

        public HybridMetadataStrategy(
            Logger logger,
            QobuzApiClient qobuzApiClient,
            IntelligentReleaseMapper releaseMapper)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _qobuzApiClient = qobuzApiClient ?? throw new ArgumentNullException(nameof(qobuzApiClient));
            _releaseMapper = releaseMapper ?? throw new ArgumentNullException(nameof(releaseMapper));
        }

        public bool CanHandle(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum)
        {
            if (lidarrAlbum == null) return false;
            
            // Check if hybrid approach is recommended
            var matchResult = _releaseMapper.ValidateReleaseMatch(lidarrAlbum, qobuzAlbum);
            return matchResult.RequiresHybridApproach;
        }

        public async Task<MetadataDownloadResult> DownloadAlbumAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null)
        {
            if (lidarrAlbum == null)
                throw new InvalidOperationException("HybridMetadataStrategy requires Lidarr album data");

            var downloads = new List<TrackDownload>();
            var lidarrTracksUsed = 0;
            var qobuzApiCallsMade = 0;

            _logger.Debug("Starting hybrid metadata download for {0} Qobuz tracks, {1} Lidarr tracks", 
                         qobuzAlbum.TracksCount, lidarrAlbum.TrackCount);

            foreach (var qobuzTrack in qobuzAlbum.GetTracks())
            {
                // Get streaming URL (always required from Qobuz)
                var streamingUrl = await GetStreamingUrlAsync(int.Parse(qobuzTrack.Id));

                // Try to find matching Lidarr track
                var matchingLidarrTrack = FindBestLidarrTrackMatch(qobuzTrack, lidarrAlbum.Tracks);
                
                TrackDownload trackDownload;

                if (matchingLidarrTrack != null && IsGoodTrackMatch(qobuzTrack, matchingLidarrTrack))
                {
                    // Strategy A: Use rich Lidarr metadata for matched tracks
                    trackDownload = CreateTrackDownloadFromLidarr(streamingUrl, matchingLidarrTrack, lidarrAlbum, qobuzTrack);
                    lidarrTracksUsed++;
                    
                    _logger.Debug("Using Lidarr metadata for track {0}: '{1}'", 
                                 matchingLidarrTrack.TrackNumber, matchingLidarrTrack.Title);
                }
                else
                {
                    // Strategy B: Use Qobuz metadata for unmatched tracks (bonus content, etc.)
                    var qobuzMetadata = await GetQobuzTrackAsync(int.Parse(qobuzTrack.Id));
                    trackDownload = CreateTrackDownloadFromQobuz(streamingUrl, qobuzTrack, qobuzMetadata);
                    qobuzApiCallsMade++;
                    
                    _logger.Debug("Using Qobuz metadata for unmatched track {0}: '{1}' (likely bonus content)", 
                                 qobuzTrack.TrackNumber, qobuzTrack.Title);
                }

                downloads.Add(trackDownload);
            }

            var apiCallsSaved = lidarrTracksUsed; // Each Lidarr track used saves 1 metadata API call

            _logger.Info("Hybrid optimization complete: Used Lidarr metadata for {0} tracks, Qobuz for {1} tracks", 
                        lidarrTracksUsed, qobuzApiCallsMade);
            _logger.Info("API calls saved: {0}, additional calls made: {1}", 
                        apiCallsSaved, qobuzApiCallsMade);

            return new MetadataDownloadResult 
            { 
                TrackDownloads = downloads,
                MetadataStrategy = "Hybrid",
                ApiCallsSaved = apiCallsSaved,
                AdditionalApiCalls = qobuzApiCallsMade
            };
        }

        private LidarrTrack FindBestLidarrTrackMatch(QobuzTrack qobuzTrack, List<LidarrTrack> lidarrTracks)
        {
            // First try exact track number match
            var exactNumberMatch = lidarrTracks.FirstOrDefault(lt => 
                lt.TrackNumber == qobuzTrack.TrackNumber &&
                lt.DiscNumber == qobuzTrack.DiscNumber);

            if (exactNumberMatch != null)
            {
                var titleSimilarity = StringSimilarity.Calculate(qobuzTrack.Title, exactNumberMatch.Title);
                if (titleSimilarity >= 0.7) // 70% minimum for exact number matches
                {
                    return exactNumberMatch;
                }
            }

            // Find best match by title and duration similarity
            LidarrTrack? bestMatch = null;
            double bestScore = 0;

            foreach (var lidarrTrack in lidarrTracks)
            {
                var score = CalculateTrackMatchScore(lidarrTrack, qobuzTrack);
                if (score > bestScore && score >= 0.8) // High threshold for title-based matching
                {
                    bestScore = score;
                    bestMatch = lidarrTrack;
                }
            }

            return bestMatch;
        }

        private bool IsGoodTrackMatch(QobuzTrack qobuzTrack, LidarrTrack lidarrTrack)
        {
            var score = CalculateTrackMatchScore(lidarrTrack, qobuzTrack);
            return score >= 0.85; // High confidence threshold for optimization
        }

        private double CalculateTrackMatchScore(LidarrTrack lidarrTrack, QobuzTrack qobuzTrack)
        {
            double score = 0;

            // Title similarity (70% weight)
            var titleSimilarity = StringSimilarity.Calculate(lidarrTrack.Title, qobuzTrack.Title);
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
                
                // Rich Lidarr metadata
                Title = lidarrTrack.Title,
                Artist = lidarrTrack.ArtistName,
                AlbumArtist = lidarrAlbum.ArtistName,
                Album = lidarrAlbum.Title,
                TrackNumber = lidarrTrack.TrackNumber,
                DiscNumber = lidarrTrack.DiscNumber,
                Duration = lidarrTrack.Duration,
                ReleaseDate = lidarrAlbum.ReleaseDate,
                
                Genre = string.Join(", ", lidarrAlbum.Genres ?? new List<string>()),
                
                // MusicBrainz identifiers
                MusicBrainzTrackId = lidarrTrack.ForeignTrackId,
                MusicBrainzAlbumId = lidarrAlbum.ForeignAlbumId,
                MusicBrainzArtistId = lidarrAlbum.ArtistForeignId,
                MusicBrainzReleaseGroupId = lidarrAlbum.ForeignReleaseId,
                
                AlbumType = lidarrAlbum.AlbumType,
                Label = lidarrAlbum.Label,
                Country = lidarrAlbum.Country,
                
                // Quality from Qobuz
                Quality = qobuzTrack.Quality,
                BitRate = qobuzTrack.BitRate,
                SampleRate = (int?)qobuzTrack.SampleRate,
                BitDepth = qobuzTrack.BitDepth,
                
                MetadataSource = "Hybrid(Lidarr+Qobuz)"
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
                
                Title = metadata.Title ?? qobuzTrack.Title,
                Artist = metadata.Performer?.Name ?? qobuzTrack.GetPerformerName(),
                AlbumArtist = metadata.Album?.Artist?.Name ?? qobuzTrack.AlbumArtistName,
                Album = metadata.Album?.Title ?? qobuzTrack.AlbumTitle,
                TrackNumber = qobuzTrack.TrackNumber,
                DiscNumber = qobuzTrack.DiscNumber,
                Duration = qobuzTrack.Duration,
                ReleaseDate = metadata.Album?.ReleaseDate,
                
                Genre = metadata.Album?.GenresList?.FirstOrDefault(),
                Composer = metadata.Composer?.Name,
                
                Quality = qobuzTrack.Quality,
                BitRate = qobuzTrack.BitRate,
                SampleRate = (int?)qobuzTrack.SampleRate,
                BitDepth = qobuzTrack.BitDepth,
                
                Label = metadata.Album?.Label?.Name,
                
                MetadataSource = "Hybrid(Qobuz)"
            };
        }

        private async Task<string> GetStreamingUrlAsync(int trackId)
        {
            try
            {
                return await _qobuzApiClient.GetStreamingUrlAsync(trackId.ToString(), 6);
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