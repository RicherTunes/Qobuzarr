using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Cache;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

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
        private readonly QobuzIndexerSettings _settings;
        private readonly IMetricsCollector? _metrics;
        private readonly ICached<string>? _urlCache;
        private readonly ICached<TrackUnavailableReason>? _negativeCache;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _urlMem = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TrackUnavailableReason> _negMem = new();
        private static readonly SemaphoreSlim _getFileUrlGate = new SemaphoreSlim(3, 3);

        public StreamUrlProvider(
            IQobuzApiClient apiClient,
            IQobuzLogger logger,
            IQualityFallbackProvider qualityFallbackProvider,
            QobuzIndexerSettings settings = null,
            IMetricsCollector? metrics = null)
        {
            _apiClient = Guard.NotNull(apiClient, nameof(apiClient));
            _logger = Guard.NotNull(logger, nameof(logger));
            _qualityFallbackProvider = Guard.NotNull(qualityFallbackProvider, nameof(qualityFallbackProvider));
            _settings = settings; // Optional - may be null in some contexts
            _metrics = metrics;

            // Try to obtain ICacheManager when hosted under Lidarr, but fall back to local memory caches.
            try
            {
                var containerType = Type.GetType("NzbDrone.Common.Composition.ContainerBuilder, NzbDrone.Common");
                var containerProp = containerType?.GetProperty("Container", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var container = containerProp?.GetValue(null);
                var resolveMethod = container?.GetType().GetMethod("Resolve", Type.EmptyTypes);
                var cacheManager = container != null ? (NzbDrone.Common.Cache.ICacheManager)container.GetType().GetMethod("Resolve", new Type[] { typeof(Type) })!.Invoke(container, new object[] { typeof(NzbDrone.Common.Cache.ICacheManager) }) : null;
                _urlCache = cacheManager?.GetCache<string>(GetType(), "stream_urls");
                _negativeCache = cacheManager?.GetCache<TrackUnavailableReason>(GetType(), "stream_neg");
            }
            catch { /* no host cache available */ }
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
                // Detect preview-only tracks via duration (29–32s common ranges)
                if (track != null && track.DurationSeconds >= 29 && track.DurationSeconds <= 32)
                {
                    _logger.Warn("Skipping preview-only track by duration: {0}s - {1}", track.DurationSeconds, track.GetFullTitle());
                    throw new TrackUnavailableException(trackId, "Preview-only track duration detected (29–32s)", TrackUnavailableReason.PreviewOnly);
                }

                // Try preferred quality first
                var streamResponse = await TryGetStreamUrl(trackId, preferredQuality).ConfigureAwait(false);
                
                if (streamResponse != null && IsValidStreamUrl(streamResponse))
                {
                    return streamResponse.Url;
                }
                
                // Log why preferred quality failed
                var preferredFailureReason = GetFailureReason(streamResponse);
                
                // Try fallback qualities with smart handling for subscription issues
                var fallbackQualities = _qualityFallbackProvider.GetFallbackQualities(preferredQuality);
                
                // Optimize fallback based on known subscription tier
                if (_settings?.SubscriptionTier == (int)QobuzSubscriptionTier.Sublime)
                {
                    // Sublime users can't get Hi-Res, cap at CD quality
                    fallbackQualities = fallbackQualities.Where(q => q <= 6).ToList();
                    _logger.Debug("💿 Subscription tier caps at CD quality; limiting fallback candidates.");
                }
                else if (_settings?.SubscriptionTier == (int)QobuzSubscriptionTier.Free)
                {
                    // Free users only get samples, no point trying any quality
                    _logger.Warn("🆓 Account allows preview-only; skipping full-track probing.");
                    var reason = GetFailureReason(streamResponse);
                    throw new TrackUnavailableException(trackId, "Free tier - only 30-second samples available", TrackUnavailableReason.SubscriptionRestriction);
                }
                else if (IsSubscriptionIssue(streamResponse) && preferredQuality > 6)
                {
                    // Unknown tier but subscription issue detected, try CD quality
                    fallbackQualities = fallbackQualities.Where(q => q <= 6).ToList();
                    _logger.Debug("🔽 High-res not permitted by subscription; trying CD quality and below.");
                }
                
                foreach (var fallbackQuality in fallbackQualities)
                {
                    streamResponse = await TryGetStreamUrl(trackId, fallbackQuality).ConfigureAwait(false);
                    
                    if (streamResponse != null && IsValidStreamUrl(streamResponse))
                    {
                        LogQualitySelection(track, album, fallbackQuality, preferredQuality);
                        return streamResponse.Url;
                    }
                    
                    // If we hit subscription issues at CD quality or below, stop trying
                    if (IsSubscriptionIssue(streamResponse) && fallbackQuality <= 6)
                    {
                        var fallbackFailureReason = GetFailureReason(streamResponse);
                        LogTrackUnavailable(track, album, fallbackFailureReason);
                        var reason = DetermineUnavailableReason(fallbackFailureReason, streamResponse);
                        throw new TrackUnavailableException(trackId, fallbackFailureReason, reason);
                    }
                }

                // All qualities failed
                var finalFailureReason = GetFailureReason(streamResponse) ?? "No stream URL available in any quality";
                LogTrackUnavailable(track, album, finalFailureReason);
                var finalReason = DetermineUnavailableReason(finalFailureReason, streamResponse);
                throw new TrackUnavailableException(trackId, finalFailureReason, finalReason);
            }
            catch (TrackUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get stream URL for track: {0}", trackId);
                throw new TrackUnavailableException(trackId, ex.Message, TrackUnavailableReason.ApiError);
            }
        }

        private async Task<QobuzStreamResponse> TryGetStreamUrl(string trackId, int quality)
        {
            // Short-lived success cache to avoid probing the same (track,quality) in album bursts
            var cacheKey = $"{trackId}:{quality}";
            var cached = _urlCache?.Get(cacheKey) ?? (_urlMem.TryGetValue(cacheKey, out var v) ? v : null);
            if (cached.IsNotNullOrWhiteSpace())
            {
                return new QobuzStreamResponse { Url = cached, FormatId = quality };
            }

            await _getFileUrlGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var parameters = new Dictionary<string, string>
                {
                    {"track_id", trackId},
                    {"format_id", quality.ToString()},
                    {"intent", "stream"}
                };

                var resp = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl", parameters).ConfigureAwait(false);
                sw.Stop();
                try
                {
                    _metrics?.RecordHistogram("qobuz_getfileurl_latency_seconds", sw.Elapsed.TotalSeconds,
                        new System.Collections.Generic.Dictionary<string, string>
                        {
                            ["format"] = quality.ToString(),
                            ["success"] = (resp != null && resp.IsSuccess && !string.IsNullOrWhiteSpace(resp.Url)).ToString().ToLowerInvariant()
                        });
                }
                catch { }
                if (resp != null && resp.IsSuccess && !resp.Url.IsNullOrWhiteSpace())
                {
                    if (_urlCache != null) _urlCache.Set(cacheKey, resp.Url, TimeSpan.FromMinutes(5));
                    _urlMem[cacheKey] = resp.Url;
                }
                return resp;
            }
            finally
            {
                _getFileUrlGate.Release();
            }
        }

        private bool IsValidStreamUrl(QobuzStreamResponse response)
        {
            // Check basic response validity
            if (response?.IsSuccess != true || string.IsNullOrWhiteSpace(response.Url))
                return false;
                
            // Check for sample/preview - following QobuzApiSharp pattern
            if (response.Sample == true)
                return false;
                
            // Check if URL appears to be a sample/preview
            if (IsPreviewOrSampleUrl(response.Url))
                return false;
                
            return true;
        }

        private string GetFailureReason(QobuzStreamResponse response)
        {
            if (response == null)
                return "No response from Qobuz API";
                
            if (response.Sample == true)
                return "Track is only available as a sample/preview (subscription insufficient)";
                
            if (!response.IsSuccess)
                return GetDetailedErrorMessage(response);
                
            if (string.IsNullOrWhiteSpace(response.Url))
                return "Empty stream URL returned";
                
            if (IsPreviewOrSampleUrl(response.Url))
                return "Stream URL appears to be preview/sample only";
                
            return "Unknown failure";
        }

        private bool IsSubscriptionIssue(QobuzStreamResponse response)
        {
            // Trust the API response structure first
            if (response?.Sample == true)
                return true;
            
            // Check for forbidden status (subscription insufficient)
            if (response?.Code == 403)
                return true;
            
            // Check for explicit subscription restriction message
            if (response?.Message?.Contains("FormatRestrictedBySubscription") == true ||
                response?.Message?.Contains("TrackRestrictedByPurchaseCredentials") == true)
                return true;
            
            // Check restriction codes
            var restrictionMessage = response?.GetRestrictionMessage();
            if (!string.IsNullOrWhiteSpace(restrictionMessage))
            {
                var lowerRestriction = restrictionMessage.ToLowerInvariant();
                return lowerRestriction.Contains("subscription") ||
                       lowerRestriction.Contains("purchase") ||
                       lowerRestriction.Contains("credentials");
            }
            
            return false;
        }

        private TrackUnavailableReason DetermineUnavailableReason(string failureReason, QobuzStreamResponse response)
        {
            if (response?.Sample == true)
                return TrackUnavailableReason.SubscriptionRestriction;
                
            return AnalyzeErrorReason(failureReason);
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
            return Lidarr.Plugin.Common.Utilities.PreviewDetectionUtility.IsPreviewOrSampleUrl(url);
        }

        public async Task<StreamProbeResult> TryGetStreamUrlAsync(string trackId, IReadOnlyList<int> qualityChain, CancellationToken cancellationToken)
        {
            // Negative cache: if we already determined rights/territory, don't re-probe for a while
            var negKey = $"neg:{trackId}";
            var neg = _negativeCache?.Get(negKey) ?? (_negMem.TryGetValue(negKey, out var n) ? n : TrackUnavailableReason.Unknown);
            if (neg != default && neg != TrackUnavailableReason.Unknown)
            {
                return new StreamProbeResult { Success = false, Reason = neg, Detail = "Cached negative result" };
            }

            foreach (var fmt in qualityChain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resp = await TryGetStreamUrl(trackId, fmt).ConfigureAwait(false);
                if (resp != null && IsValidStreamUrl(resp))
                {
                    return new StreamProbeResult { Success = true, Url = resp.Url, FormatId = fmt };
                }

                var (reason, detail) = MapGetFileUrlFailure(resp);
                if (reason == TrackUnavailableReason.PreviewOnly ||
                    reason == TrackUnavailableReason.RegionalRestriction ||
                    reason == TrackUnavailableReason.NotStreamable)
                {
                    // Cache rights/territory as negative to avoid hammering
                    if (_negativeCache != null) _negativeCache.Set(negKey, reason, TimeSpan.FromHours(36));
                    _negMem[negKey] = reason;
                    return new StreamProbeResult { Success = false, Reason = reason, Detail = detail };
                }
            }

            return new StreamProbeResult
            {
                Success = false,
                Reason = TrackUnavailableReason.NoQualityAvailable,
                Detail = "No playable formats returned"
            };
        }

        private static (TrackUnavailableReason reason, string detail) MapGetFileUrlFailure(QobuzStreamResponse? response)
        {
            if (response == null)
                return (TrackUnavailableReason.Unknown, "No response");

            if (response.Sample == true)
                return (TrackUnavailableReason.PreviewOnly, response.Message ?? "Preview-only");

            var text = (response.Message ?? response.GetRestrictionMessage() ?? response.Status ?? string.Empty).ToLowerInvariant();

            if (text.Contains("sample") || text.Contains("preview"))
                return (TrackUnavailableReason.PreviewOnly, response.Message ?? "Preview-only");

            if (text.Contains("territor") || text.Contains("licensed") || text.Contains("not available") || text.Contains("geo"))
                return (TrackUnavailableReason.RegionalRestriction, response.Message ?? "Not licensed in this store");

            if (text.Contains("format") && text.Contains("not available"))
                return (TrackUnavailableReason.NoQualityAvailable, response.Message ?? "Format not available");

            if (text.Contains("device") && text.Contains("limit"))
                return (TrackUnavailableReason.Restricted, response.Message ?? "Device limit");

            if (text.Contains("auth") || text.Contains("token"))
                return (TrackUnavailableReason.ApiError, response.Message ?? "Authentication error");

            return (TrackUnavailableReason.Unknown, response.Message ?? string.Empty);
        }

        /// <summary>
        /// Extracts detailed error information from a failed Qobuz stream response
        /// </summary>
        private string GetDetailedErrorMessage(QobuzStreamResponse response, string fallbackMessage = "API request failed")
        {
            var errorParts = new List<string>();

            // Add HTTP status/code if available
            if (response.Code.HasValue && response.Code != 200)
            {
                errorParts.Add($"HTTP {response.Code}");
            }

            // Add API message if available
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                errorParts.Add(response.Message);
            }

            // Add restriction details if available
            var restrictionMessage = response.GetRestrictionMessage();
            if (!string.IsNullOrWhiteSpace(restrictionMessage))
            {
                errorParts.Add(restrictionMessage);
            }

            // Add status if it provides additional context
            if (!string.IsNullOrWhiteSpace(response.Status) && 
                !string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                errorParts.Add($"Status: {response.Status}");
            }

            return errorParts.Any() ? string.Join(" - ", errorParts) : fallbackMessage;
        }

        /// <summary>
        /// Analyzes error details to identify common issues like regional restrictions
        /// </summary>
        private TrackUnavailableReason AnalyzeErrorReason(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return TrackUnavailableReason.Unknown;

            var lowerError = errorMessage.ToLowerInvariant();

            if (lowerError.Contains("region") || lowerError.Contains("country") || lowerError.Contains("geographic"))
                return TrackUnavailableReason.RegionalRestriction;

            if (lowerError.Contains("subscription") || lowerError.Contains("premium") || lowerError.Contains("plan"))
                return TrackUnavailableReason.SubscriptionRestriction;

            if (lowerError.Contains("preview") || lowerError.Contains("sample"))
                return TrackUnavailableReason.PreviewOnly;

            if (lowerError.Contains("format") || lowerError.Contains("quality") || lowerError.Contains("bitrate"))
                return TrackUnavailableReason.NoQualityAvailable;

            if (lowerError.Contains("not streamable") || lowerError.Contains("not available"))
                return TrackUnavailableReason.NotStreamable;

            if (lowerError.Contains("restricted") || lowerError.Contains("forbidden"))
                return TrackUnavailableReason.Restricted;

            if (lowerError.Contains("http") || lowerError.Contains("timeout") || lowerError.Contains("network"))
                return TrackUnavailableReason.ApiError;

            return TrackUnavailableReason.Unknown;
        }
    }
}

