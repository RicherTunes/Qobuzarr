using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Implementation of stream availability validation service.
    /// Provides comprehensive pre-download checking to prevent "no quality available" errors.
    /// </summary>
    public class QobuzStreamAvailabilityService : IQobuzStreamAvailabilityService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IQobuzLogger _logger;

        public QobuzStreamAvailabilityService(IQobuzApiClient apiClient, IQobuzLogger logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<List<int>> GetAvailableQualitiesAsync(string trackId)
        {
            var availableQualities = new List<int>();
            
            // Test common quality format IDs
            var qualityFormats = new[] { 27, 7, 6, 5 }; // Hi-Res 192, Hi-Res 96, FLAC CD, MP3 320
            
            foreach (var formatId in qualityFormats)
            {
                try
                {
                    var result = await ValidateStreamAvailabilityAsync(trackId, formatId).ConfigureAwait(false);
                    if (result.IsAvailable)
                    {
                        availableQualities.Add(formatId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Error checking quality {0} for track {1}: {2}", formatId, trackId, ex.Message);
                }
            }
            
            return availableQualities;
        }

        public async Task<StreamAvailabilityResult> ValidateStreamAvailabilityAsync(string trackId, int qualityFormatId)
        {
            var result = new StreamAvailabilityResult
            {
                TrackId = trackId,
                QualityFormatId = qualityFormatId,
                IsAvailable = false
            };

            try
            {
                var parameters = new Dictionary<string, string>
                {
                    {"track_id", trackId},
                    {"format_id", qualityFormatId.ToString()},
                    {"intent", "stream"}
                };

                var streamResponse = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl", parameters).ConfigureAwait(false);
                
                if (streamResponse?.IsSuccess == true)
                {
                    // Check for restrictions
                    if (streamResponse.HasRestrictions())
                    {
                        var restrictionMessage = streamResponse.GetRestrictionMessage();
                        result.RestrictionMessage = restrictionMessage;
                        
                        // Categorize the restriction type
                        result.UnavailableReason = CategorizeRestriction(restrictionMessage);
                        
                        // Format restrictions are handleable with fallback
                        if (result.UnavailableReason == TrackUnavailableReason.NoQualityAvailable)
                        {
                            _logger.Debug("Format {0} not available for track {1}, checking alternatives", qualityFormatId, trackId);
                            result.AlternativeQualities = await GetAlternativeQualities(trackId, qualityFormatId).ConfigureAwait(false);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(streamResponse.Url))
                    {
                        // Check if this is a preview/sample URL
                        if (IsPreviewOrSampleUrl(streamResponse.Url))
                        {
                            result.IsPreviewOnly = true;
                            result.UnavailableReason = TrackUnavailableReason.PreviewOnly;
                            result.RestrictionMessage = "Only preview/sample version available";
                        }
                        else
                        {
                            // Valid full stream URL
                            result.IsAvailable = true;
                        }
                    }
                    else
                    {
                        result.UnavailableReason = TrackUnavailableReason.NotStreamable;
                        result.RestrictionMessage = "No stream URL returned";
                    }
                }
                else
                {
                    result.UnavailableReason = TrackUnavailableReason.Unknown;
                    result.RestrictionMessage = streamResponse?.Message ?? "API request failed";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating stream availability for track {0}, quality {1}", trackId, qualityFormatId);
                result.UnavailableReason = TrackUnavailableReason.Unknown;
                result.RestrictionMessage = ex.Message;
            }

            return result;
        }

        public async Task<AlbumAvailabilityResult> ValidateAlbumAvailabilityAsync(QobuzAlbum album, int preferredQuality)
        {
            var result = new AlbumAvailabilityResult
            {
                AlbumId = album.Id,
                RequestedQuality = preferredQuality,
                TotalTracks = album.TracksCount
            };

            var tracks = album.GetTracks();
            if (!tracks.Any())
            {
                _logger.Warn("Album {0} has no tracks to validate", album.Id);
                return result;
            }

            result.TotalTracks = tracks.Count;

            foreach (var track in tracks)
            {
                try
                {
                    var streamResult = await ValidateStreamAvailabilityAsync(track.Id, preferredQuality).ConfigureAwait(false);
                    
                    var trackInfo = new TrackAvailabilityInfo
                    {
                        TrackId = track.Id,
                        TrackTitle = track.GetFullTitle(),
                        TrackNumber = track.TrackNumber,
                        IsAvailable = streamResult.IsAvailable,
                        UnavailableReason = streamResult.UnavailableReason,
                        RestrictionMessage = streamResult.RestrictionMessage
                    };

                    // If not available in preferred quality, check what qualities are available
                    if (!streamResult.IsAvailable)
                    {
                        trackInfo.AvailableQualities = await GetAvailableQualitiesAsync(track.Id).ConfigureAwait(false);
                    }
                    else
                    {
                        trackInfo.AvailableQualities.Add(preferredQuality);
                    }

                    result.TrackResults.Add(trackInfo);

                    if (streamResult.IsAvailable)
                    {
                        result.AvailableTracks++;
                    }
                    else
                    {
                        result.UnavailableTracks++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating track {0} in album {1}", track.Id, album.Id);
                    
                    result.TrackResults.Add(new TrackAvailabilityInfo
                    {
                        TrackId = track.Id,
                        TrackTitle = track.GetFullTitle(),
                        TrackNumber = track.TrackNumber,
                        IsAvailable = false,
                        UnavailableReason = TrackUnavailableReason.Unknown,
                        RestrictionMessage = ex.Message
                    });
                    
                    result.UnavailableTracks++;
                }
            }

            _logger.Info("Album {0} availability: {1}/{2} tracks available in quality {3} ({4:F1}%)", 
                album.Id, result.AvailableTracks, result.TotalTracks, preferredQuality, result.AvailabilityPercentage);

            return result;
        }

        private async Task<List<int>> GetAlternativeQualities(string trackId, int excludeQuality)
        {
            var alternatives = new List<int>();
            var allQualities = new[] { 27, 7, 6, 5 }; // All possible qualities
            
            foreach (var quality in allQualities.Where(q => q != excludeQuality))
            {
                try
                {
                    var result = await ValidateStreamAvailabilityAsync(trackId, quality).ConfigureAwait(false);
                    if (result.IsAvailable)
                    {
                        alternatives.Add(quality);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Error checking alternative quality {0} for track {1}: {2}", quality, trackId, ex.Message);
                }
            }
            
            return alternatives;
        }

        private TrackUnavailableReason CategorizeRestriction(string restrictionMessage)
        {
            if (string.IsNullOrWhiteSpace(restrictionMessage))
                return TrackUnavailableReason.Unknown;

            var message = restrictionMessage.ToLower();

            if (message.Contains("format") && message.Contains("not available"))
                return TrackUnavailableReason.NoQualityAvailable;
            
            if (message.Contains("geo") || message.Contains("region") || message.Contains("country"))
                return TrackUnavailableReason.RegionalRestriction;
            
            if (message.Contains("subscription") || message.Contains("tier"))
                return TrackUnavailableReason.SubscriptionRestriction;
            
            if (message.Contains("preview") || message.Contains("sample"))
                return TrackUnavailableReason.PreviewOnly;

            return TrackUnavailableReason.Unknown;
        }

        private bool IsPreviewOrSampleUrl(string url)
        {
            return Utilities.PreviewDetectionUtility.IsPreviewOrSampleUrl(url);
        }
    }
}