using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles obtaining streaming URLs from Qobuz API with quality fallback
    /// </summary>
    public class StreamUrlProvider : IStreamUrlProvider
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IQobuzLogger _logger;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;

        public StreamUrlProvider(IQobuzApiClient apiClient, IQobuzLogger logger, IQualityFallbackProvider qualityFallbackProvider)
        {
            _apiClient = Guard.NotNull(apiClient, nameof(apiClient));
            _logger = Guard.NotNull(logger, nameof(logger));
            _qualityFallbackProvider = Guard.NotNull(qualityFallbackProvider, nameof(qualityFallbackProvider));
        }

        public async Task<string> GetStreamUrlAsync(string trackId, int preferredQuality)
        {
            return await GetStreamUrlInternalAsync(trackId, null, null, preferredQuality).ConfigureAwait(false);
        }

        public async Task<string> GetStreamUrlAsync(QobuzTrack track, QobuzAlbum album, int preferredQuality)
        {
            return await GetStreamUrlInternalAsync(track?.Id, track, album, preferredQuality).ConfigureAwait(false);
        }

        private async Task<string> GetStreamUrlInternalAsync(string trackId, QobuzTrack track, QobuzAlbum album, int preferredQuality)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    {"track_id", trackId},
                    {"format_id", preferredQuality.ToString()},
                    {"intent", "stream"}
                };

                var streamResponse = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl", parameters).ConfigureAwait(false);
                var lastRestrictionMessage = string.Empty;
                
                // Simple logging for failed attempts
                if (streamResponse?.IsSuccess != true || string.IsNullOrWhiteSpace(streamResponse.Url))
                {
                    _logger.Debug("Quality {0} not available for track {1}", preferredQuality, trackId);
                }
                
                // Try preferred quality first
                if (streamResponse?.IsSuccess == true && streamResponse.Url.IsNotNullOrWhiteSpace())
                {
                    // Check for restrictions that would prevent actual downloading
                    if (streamResponse.HasRestrictions())
                    {
                        var restrictionMessage = streamResponse.GetRestrictionMessage();
                        lastRestrictionMessage = restrictionMessage;
                        
                        // Only log format restrictions as debug - these are handled by fallback
                        if (restrictionMessage?.Contains("format not available") == true)
                        {
                            _logger.Debug("Preferred quality {0} not available for track {1}: {2}", preferredQuality, trackId, restrictionMessage);
                        }
                        else
                        {
                            // Check if this is a preview-only restriction
                            if (IsPreviewOrSampleUrl(streamResponse.Url))
                            {
                                _logger.Debug("Track {0} only available as preview/sample in quality {1}", trackId, preferredQuality);
                                // Continue to try fallback qualities
                            }
                            else
                            {
                                // Other restrictions (geo, subscription) are real blockers
                                var reason = _qualityFallbackProvider.DetermineUnavailableReason(restrictionMessage);
                                throw new TrackUnavailableException(trackId, restrictionMessage, reason);
                            }
                        }
                    }
                    else
                    {
                        // Check if this is a preview/sample URL even without explicit restrictions
                        if (IsPreviewOrSampleUrl(streamResponse.Url))
                        {
                            _logger.Debug("Track {0} appears to be preview/sample only in quality {1}", trackId, preferredQuality);
                            lastRestrictionMessage = "Preview/sample only";
                            // Continue to try fallback qualities
                        }
                        else
                        {
                            // No restrictions, preferred quality works
                            _logger.Debug("Successfully obtained stream URL for track {0} in preferred quality {1}", trackId, preferredQuality);
                            return streamResponse.Url;
                        }
                    }
                }

                // Try fallback qualities if preferred quality failed or had format restrictions
                var fallbackQualities = _qualityFallbackProvider.GetFallbackQualities(preferredQuality);
                
                foreach (var fallbackQuality in fallbackQualities)
                {
                    _logger.Debug("Trying fallback quality {0} for track {1}", fallbackQuality, trackId);
                    
                    parameters["format_id"] = fallbackQuality.ToString();
                    streamResponse = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl", parameters).ConfigureAwait(false);
                    
                    if (streamResponse?.IsSuccess == true && streamResponse.Url.IsNotNullOrWhiteSpace())
                    {
                        // Check restrictions for fallback quality
                        if (streamResponse.HasRestrictions())
                        {
                            var restrictionMessage = streamResponse.GetRestrictionMessage();
                            lastRestrictionMessage = restrictionMessage;
                            
                            // Skip format-related restrictions for fallback (expected)
                            if (restrictionMessage?.Contains("format not available") == true)
                            {
                                _logger.Debug("Fallback quality {0} has format restriction for track {1}, continuing fallback", fallbackQuality, trackId);
                                continue;
                            }
                            else if (IsPreviewOrSampleUrl(streamResponse.Url))
                            {
                                _logger.Debug("Fallback quality {0} is preview/sample only for track {1}, continuing fallback", fallbackQuality, trackId);
                                continue;
                            }
                            else
                            {
                                // Other restrictions are real blockers
                                var reason = _qualityFallbackProvider.DetermineUnavailableReason(restrictionMessage);
                                throw new TrackUnavailableException(trackId, restrictionMessage, reason);
                            }
                        }
                        else
                        {
                            // Check if this fallback URL is a preview/sample
                            if (IsPreviewOrSampleUrl(streamResponse.Url))
                            {
                                _logger.Debug("Fallback quality {0} is preview/sample only for track {1}, continuing fallback", fallbackQuality, trackId);
                                lastRestrictionMessage = "Preview/sample only";
                                continue;
                            }
                        }
                        
                        // Enhanced logging with Smart Quality Badges format
                        LogQualitySelection(track, album, fallbackQuality, preferredQuality);
                        return streamResponse.Url;
                    }
                }

                // All qualities failed - log detailed information about why
                LogTrackUnavailable(track, album, lastRestrictionMessage);
                
                // Determine the most appropriate exception based on what we learned
                var finalReason = _qualityFallbackProvider.DetermineUnavailableReason(lastRestrictionMessage);
                var detailedMessage = string.IsNullOrEmpty(lastRestrictionMessage) 
                    ? "No stream URL available in any quality (track may be removed or region-locked)"
                    : lastRestrictionMessage;
                    
                throw new TrackUnavailableException(trackId, detailedMessage, finalReason);
            }
            catch (TrackUnavailableException)
            {
                // Re-throw our custom exceptions
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get stream URL for track: {0}", trackId);
                throw new TrackUnavailableException(trackId, ex.Message, TrackUnavailableReason.ApiError);
            }
        }

        private void LogQualitySelection(QobuzTrack track, QobuzAlbum album, int actualQuality, int preferredQuality)
        {
            if (track != null && album != null)
            {
                // Smart Quality Badges format: 🎵 Artist - "Track Name" [01/14] │ Album Title (Year) │ [🟡 FLAC-96] ↓ (req. FLAC-192)
                var artistName = album.GetArtistName() ?? "Unknown Artist";
                var trackTitle = track.GetFullTitle() ?? "Unknown Track";
                var trackPosition = $"{track.TrackNumber:D2}/{album.TracksCount:D2}";
                var albumTitle = album.GetFullTitle() ?? "Unknown Album";
                var albumYear = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "";
                var albumInfo = !string.IsNullOrEmpty(albumYear) ? $"{albumTitle} ({albumYear})" : albumTitle;
                
                var qualityBadge = CreateQualityBadge(actualQuality, preferredQuality);
                
                _logger.Info("🎵 {0} - \"{1}\" [{2}] │ {3} │ {4}", 
                    artistName, trackTitle, trackPosition, albumInfo, qualityBadge);
            }
            else
            {
                // Fallback to original format for legacy calls
                var qualityMessage = QualityFormatter.FormatQualityFallback(actualQuality, preferredQuality);
                _logger.Info("Track {0}: Using {1}", track?.Id ?? "Unknown", qualityMessage);
            }
        }

        private void LogTrackUnavailable(QobuzTrack track, QobuzAlbum album, string errorMessage)
        {
            if (track != null && album != null)
            {
                var artistName = album.GetArtistName() ?? "Unknown Artist";
                var trackTitle = track.GetFullTitle() ?? "Unknown Track";
                var trackPosition = $"{track.TrackNumber:D2}/{album.TracksCount:D2}";
                var albumTitle = album.GetFullTitle() ?? "Unknown Album";
                var albumYear = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "";
                var albumInfo = !string.IsNullOrEmpty(albumYear) ? $"{albumTitle} ({albumYear})" : albumTitle;
                
                _logger.Warn("⚠️ 🎵 {0} - \"{1}\" [{2}] │ {3} │ Unavailable in all qualities. Last error: {4}", 
                    artistName, trackTitle, trackPosition, albumInfo, errorMessage ?? "No stream URL returned");
            }
            else
            {
                _logger.Warn("Track {0} unavailable in ALL qualities. Last error: {1}", 
                    track?.Id ?? "Unknown", errorMessage ?? "No stream URL returned");
            }
        }

        private string CreateQualityBadge(int actualQuality, int preferredQuality)
        {
            var actualName = GetQualityShortName(actualQuality);
            var preferredName = GetQualityShortName(preferredQuality);
            
            if (actualQuality == preferredQuality)
            {
                return $"[🟢 {actualName}] ✓";
            }
            else if (actualQuality < preferredQuality)
            {
                return $"[🟡 {actualName}] ↓ (req. {preferredName})";
            }
            else
            {
                return $"[🔵 {actualName}] ↑ (req. {preferredName})";
            }
        }

        private string GetQualityShortName(int qualityId)
        {
            return qualityId switch
            {
                5 => "MP3-320",
                6 => "FLAC-CD",
                7 => "FLAC-96",
                27 => "FLAC-192",
                _ => $"Q{qualityId}"
            };
        }

        public bool IsPreviewOrSampleUrl(string url)
        {
            return PreviewDetectionUtility.IsPreviewOrSampleUrl(url);
        }
    }
}