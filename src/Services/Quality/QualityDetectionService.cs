using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service for detecting available qualities for tracks and albums.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public class QualityDetectionService : IQualityDetectionService
    {
        private readonly IStreamInfoService _streamInfoService;
        private readonly IQualityMappingService _qualityMappingService;
        private readonly IQobuzLogger _logger;

        // Quality detection settings
        private const int SAMPLE_TRACK_COUNT = 3;
        private const double CONSISTENCY_THRESHOLD = 0.8;

        public QualityDetectionService(
            IStreamInfoService streamInfoService,
            IQualityMappingService qualityMappingService,
            IQobuzLogger logger)
        {
            _streamInfoService = streamInfoService ?? throw new ArgumentNullException(nameof(streamInfoService));
            _qualityMappingService = qualityMappingService ?? throw new ArgumentNullException(nameof(qualityMappingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));
            }

            _logger.Debug("Detecting available qualities for track {0}", trackId);
            
            var result = new QualityDetectionResult
            {
                TrackId = trackId,
                AvailableQualities = new List<Models.QualityFormat>(),
                CheckedAt = DateTime.UtcNow
            };

            var qualityFormats = _qualityMappingService.GetQualityFormats();
            
            foreach (var qualityFormat in qualityFormats.Values.OrderByDescending(q => q.Priority))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var quality = QobuzQuality.FromId(qualityFormat.Id);
                    var streamInfo = await _streamInfoService.GetStreamInfoAsync(trackId, quality, cancellationToken);
                    
                    if (streamInfo != null && IsValidStreamUrl(streamInfo.Url))
                    {
                        result.AvailableQualities.Add(qualityFormat);
                        _logger.Debug("Quality {0} available for track {1}", qualityFormat.Name, trackId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} not available for track {1}: {2}", qualityFormat.Name, trackId, ex.Message);
                }
            }

            result.HighestAvailableQuality = result.AvailableQualities.FirstOrDefault();
            _logger.Info("Track {0} has {1} available qualities, highest: {2}", 
                trackId, result.AvailableQualities.Count, result.HighestAvailableQuality?.Name ?? "None");
            
            return result;
        }

        public async Task<Models.AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                return Models.AlbumQualityResult.Failed("Album has no tracks");
            }

            _logger.Info("Detecting album-level quality for '{0}' ({1} tracks)", album.Title, album.TracksCount);
            
            var tracks = album.GetTracks().ToList();
            var sampleTracks = SelectRepresentativeTracks(tracks, SAMPLE_TRACK_COUNT);
            
            // Check quality for sample tracks
            var sampleResults = new List<QualityDetectionResult>();
            foreach (var track in sampleTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trackResult = await DetectAvailableQualitiesAsync(track.Id.ToString(), cancellationToken);
                sampleResults.Add(trackResult);
            }

            // Analyze consistency
            return AnalyzeAlbumQuality(album, sampleResults, preferredQuality);
        }

        public bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var urlLower = url.ToLower();

            // Check for preview/sample indicators
            var invalidPatterns = new[]
            {
                "_preview", "preview_", "/preview/", "preview=true",
                "_sample", "sample_", "/sample/", "sample=true",
                "_demo", "demo_", "_30sec", "_30s", "duration=30",
                "_clip", "clip_", "_short"
            };

            return !invalidPatterns.Any(pattern => urlLower.Contains(pattern));
        }

        public List<QobuzTrack> SelectRepresentativeTracks(List<QobuzTrack> tracks, int count)
        {
            if (tracks.Count <= count)
                return tracks;

            var selected = new List<QobuzTrack>();
            
            // Always include first track
            selected.Add(tracks.First());
            
            // Include middle track
            if (count >= 2)
            {
                selected.Add(tracks[tracks.Count / 2]);
            }
            
            // Include last track
            if (count >= 3)
            {
                selected.Add(tracks.Last());
            }
            
            return selected;
        }

        private Models.AlbumQualityResult AnalyzeAlbumQuality(
            QobuzAlbum album, 
            List<QualityDetectionResult> sampleResults,
            int preferredQuality)
        {
            var result = new Models.AlbumQualityResult
            {
                AlbumId = album.Id,
                AlbumTitle = album.Title,
                PreferredQuality = preferredQuality,
                SampleSize = sampleResults.Count,
                TotalTracks = album.TracksCount
            };

            // Find most common highest quality
            var highestQualities = sampleResults
                .Where(r => r.HighestAvailableQuality != null)
                .Select(r => r.HighestAvailableQuality.Id)
                .ToList();

            if (!highestQualities.Any())
            {
                result.Success = false;
                result.Error = "No qualities available for sampled tracks";
                return result;
            }

            var mostCommonQuality = highestQualities
                .GroupBy(q => q)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            var consistency = (double)highestQualities.Count(q => q == mostCommonQuality) / highestQualities.Count;
            
            result.ConsistentQuality = consistency >= CONSISTENCY_THRESHOLD;
            result.DetectedQuality = mostCommonQuality;
            result.ConfidenceScore = consistency;
            result.Success = true;

            if (result.ConsistentQuality)
            {
                result.OptimizationApplied = true;
                result.ApiCallsSaved = album.TracksCount - sampleResults.Count;
                _logger.Info("Album '{0}' has consistent quality {1} (confidence: {2:P0}), saved {3} API calls",
                    album.Title, mostCommonQuality, consistency, result.ApiCallsSaved);
            }
            else
            {
                _logger.Info("Album '{0}' has mixed quality, fallback to individual track checks required",
                    album.Title);
            }

            return result;
        }
    }
}