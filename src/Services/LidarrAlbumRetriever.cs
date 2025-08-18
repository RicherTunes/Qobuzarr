using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for retrieving albums from Lidarr with filtering and Qobuz searching capabilities.
    /// Implements comprehensive parallel processing and quality profile management optimized for the *arr ecosystem.
    /// </summary>
    public class LidarrAlbumRetriever : ILidarrAlbumRetriever
    {
        private readonly ILidarrApiClient _lidarrApiClient;
        private readonly IQobuzApiClient _qobuzApiClient;
        private readonly IAdaptiveRateLimiter _rateLimiter;
        private readonly IQualityMappingService _qualityMappingService;
        private readonly ILidarrStatisticsCollector _statisticsCollector;
        private readonly Logger _logger;

        // Quality profile cache to avoid repeated API calls
        private readonly Dictionary<int, LidarrQualityProfile> _qualityProfileCache = new();
        private readonly object _qualityProfileCacheLock = new();

        // Configuration constants
        private const int MAX_ALBUMS_PER_REQUEST = 500;
        private const int DEFAULT_MAX_CONCURRENCY = 0; // Will use Environment.ProcessorCount
        private const int MIN_CONCURRENCY = 1;
        private const int MAX_CONCURRENCY = 20;

        /// <summary>
        /// Initializes a new instance of the LidarrAlbumRetriever with required dependencies.
        /// </summary>
        /// <param name="lidarrApiClient">Client for communicating with Lidarr API.</param>
        /// <param name="qobuzApiClient">Client for communicating with Qobuz API.</param>
        /// <param name="rateLimiter">Adaptive rate limiter for API throttling.</param>
        /// <param name="qualityMappingService">Service for mapping Lidarr quality profiles to Qobuz quality levels.</param>
        /// <param name="statisticsCollector">Service for collecting statistics.</param>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        public LidarrAlbumRetriever(
            ILidarrApiClient lidarrApiClient,
            IQobuzApiClient qobuzApiClient,
            IAdaptiveRateLimiter rateLimiter,
            IQualityMappingService qualityMappingService,
            ILidarrStatisticsCollector statisticsCollector,
            Logger logger)
        {
            _lidarrApiClient = Guard.NotNull(lidarrApiClient, nameof(lidarrApiClient));
            _qobuzApiClient = Guard.NotNull(qobuzApiClient, nameof(qobuzApiClient));
            _rateLimiter = Guard.NotNull(rateLimiter, nameof(rateLimiter));
            _qualityMappingService = Guard.NotNull(qualityMappingService, nameof(qualityMappingService));
            _statisticsCollector = Guard.NotNull(statisticsCollector, nameof(statisticsCollector));
            _logger = Guard.NotNull(logger, nameof(logger));

            _logger.Info("LidarrAlbumRetriever initialized");
        }

        /// <summary>
        /// Retrieves wanted albums from Lidarr with filtering and resource limits.
        /// </summary>
        public async Task<IEnumerable<LidarrAlbum>> GetFilteredWantedAlbumsAsync(
            LidarrFilterOptions filterOptions = null,
            int maxAlbums = MAX_ALBUMS_PER_REQUEST,
            System.IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.Debug("Retrieving wanted albums from Lidarr (max: {0})", maxAlbums);
                
                // Apply resource limits
                var clampedMaxAlbums = Math.Min(maxAlbums, MAX_ALBUMS_PER_REQUEST);
                if (maxAlbums != clampedMaxAlbums)
                {
                    _logger.Warn("Requested album count {0} exceeds limit, clamped to {1}", maxAlbums, clampedMaxAlbums);
                }

                // Set page size if not specified
                if (filterOptions == null)
                {
                    filterOptions = new LidarrFilterOptions();
                }
                
                if (filterOptions.PageSize == 0)
                {
                    filterOptions.PageSize = Math.Min(clampedMaxAlbums, 100); // Reasonable page size
                }

                var allAlbums = new List<LidarrAlbum>();
                var page = 1;
                
                while (allAlbums.Count < clampedMaxAlbums && !cancellationToken.IsCancellationRequested)
                {
                    filterOptions.Page = page;
                    
                    var response = await _lidarrApiClient.GetWantedAlbumsAsync(filterOptions).ConfigureAwait(false);
                    
                    if (response?.Records == null || !response.Records.Any())
                    {
                        _logger.Debug("No more albums found on page {0}", page);
                        break;
                    }

                    var albumsToAdd = response.Records.Take(clampedMaxAlbums - allAlbums.Count);
                    allAlbums.AddRange(albumsToAdd);
                    
                    _logger.Debug("Retrieved {0} albums from page {1}, total: {2}", response.Records.Count, page, allAlbums.Count);
                    
                    // Check if we've reached the end
                    if (response.Records.Count < filterOptions.PageSize || allAlbums.Count >= clampedMaxAlbums)
                    {
                        break;
                    }
                    
                    page++;
                }

                _logger.Info("Retrieved {0} wanted albums from Lidarr", allAlbums.Count);
                return allAlbums;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve wanted albums from Lidarr");
                throw;
            }
        }

        /// <summary>
        /// Searches Qobuz in parallel for multiple Lidarr albums with intelligent concurrency control.
        /// </summary>
        public async Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzParallelAsync(
            IEnumerable<LidarrAlbum> lidarrAlbums,
            int maxConcurrency = DEFAULT_MAX_CONCURRENCY,
            System.IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            var albumList = lidarrAlbums?.ToList() ?? throw new ArgumentNullException(nameof(lidarrAlbums));
            
            if (!albumList.Any())
            {
                _logger.Info("No albums provided for Qobuz search");
                return new Dictionary<LidarrAlbum, QobuzAlbum>();
            }

            // Determine effective concurrency
            var effectiveConcurrency = GetEffectiveConcurrency(maxConcurrency);
            _logger.Info("Starting parallel Qobuz search for {0} albums with concurrency {1}", albumList.Count, effectiveConcurrency);

            var results = new Dictionary<LidarrAlbum, QobuzAlbum>();
            var resultLock = new object();
            var completed = 0;

            // Create semaphore for this operation
            using var semaphore = new SemaphoreSlim(effectiveConcurrency, effectiveConcurrency);
            
            // Create tasks for parallel execution
            var tasks = albumList.Select(async album =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _rateLimiter.WaitIfNeededAsync("/album/search", cancellationToken).ConfigureAwait(false);
                    
                    var qobuzAlbum = await SearchSingleAlbumAsync(album, cancellationToken).ConfigureAwait(false);
                    
                    lock (resultLock)
                    {
                        if (qobuzAlbum != null)
                        {
                            results[album] = qobuzAlbum;
                        }
                        
                        completed++;
                        
                        // Update statistics
                        _statisticsCollector.RecordSearchAttempt(qobuzAlbum != null, effectiveConcurrency);
                    }

                    // Report progress
                    progress?.Report(new ProgressReport
                    {
                        Completed = completed,
                        Total = albumList.Count,
                        CurrentItem = $"{album.Artist?.ArtistName} - {album.Title}",
                        Phase = "Searching Qobuz"
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to search for album: {0} - {1}", album.Artist?.ArtistName, album.Title);
                    _statisticsCollector.RecordError(ex, "Search");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            // Wait for all searches to complete
            await Task.WhenAll(tasks).ConfigureAwait(false);
            
            _logger.Info("Completed parallel Qobuz search: {0}/{1} albums found", results.Count, albumList.Count);
            return results;
        }

        /// <summary>
        /// Validates albums before download to check availability, quality, and restrictions.
        /// Now uses quality profiles to determine appropriate quality levels for each album.
        /// </summary>
        public async Task<IEnumerable<AlbumDownloadItem>> ValidateAlbumsAsync(
            Dictionary<LidarrAlbum, QobuzAlbum> albumMatches,
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            if (albumMatches == null || !albumMatches.Any())
            {
                _logger.Info("No album matches provided for validation");
                return Enumerable.Empty<AlbumDownloadItem>();
            }

            _logger.Info("Validating {0} album matches for download", albumMatches.Count);
            
            var validatedItems = new List<AlbumDownloadItem>();

            foreach (var match in albumMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var validationMessages = new List<string>();
                    var isValid = true;

                    // Validate Qobuz album has tracks
                    if (match.Value.TracksCount <= 0)
                    {
                        validationMessages.Add("Album has no tracks");
                        isValid = false;
                    }

                    // Validate album is streamable (not just for purchase)
                    if (!match.Value.Streamable)
                    {
                        validationMessages.Add("Album is not streamable");
                        isValid = false;
                    }

                    // Check if album has sufficient track information
                    var sampleTrack = match.Value.GetTracks().FirstOrDefault();
                    if (sampleTrack == null)
                    {
                        validationMessages.Add("No track information available");
                        isValid = false;
                    }

                    // Get quality profile for this album
                    var qualityProfile = await GetQualityProfileForAlbumAsync(match.Key, cancellationToken).ConfigureAwait(false);
                    var qualityRecommendation = _qualityMappingService.GetQualityRecommendation(match.Key, qualityProfile);

                    // Determine available Qobuz qualities for this album
                    var availableQualities = GetAvailableQobuzQualities(match.Value);
                    var selectedQuality = _qualityMappingService.SelectBestAvailableQuality(qualityProfile, availableQualities);

                    if (string.IsNullOrEmpty(selectedQuality))
                    {
                        validationMessages.Add("No suitable quality available for quality profile requirements");
                        isValid = false;
                    }
                    else
                    {
                        validationMessages.Add($"Selected quality: {selectedQuality} (Profile: {qualityProfile?.Name ?? "Default"})");
                    }

                    // Quality validation based on technical specs and profile requirements
                    if (sampleTrack != null && qualityProfile != null)
                    {
                        var hasHiRes = sampleTrack.HasHiResQuality();
                        var maxBitDepth = sampleTrack.MaximumBitDepth ?? 16;
                        var maxSampleRate = sampleTrack.MaximumSampleRate ?? 44100;
                        
                        // Check if the selected quality meets profile requirements
                        if (!string.IsNullOrEmpty(selectedQuality) && 
                            !_qualityMappingService.DoesQualityMeetProfileRequirements(qualityProfile, selectedQuality))
                        {
                            validationMessages.Add($"Selected quality {selectedQuality} does not meet profile requirements");
                            // This is a warning, not a failure unless profile is very strict
                        }

                        // Log quality information for debugging
                        _logger.Debug("Album quality info - Bit Depth: {0}, Sample Rate: {1}, Hi-Res: {2}, Selected: {3}",
                            maxBitDepth, maxSampleRate, hasHiRes, selectedQuality);
                    }

                    if (isValid)
                    {
                        validatedItems.Add(new AlbumDownloadItem
                        {
                            LidarrAlbum = match.Key,
                            QobuzAlbum = match.Value,
                            PreferredQuality = ConvertQobuzQualityToInt(selectedQuality ?? _qualityMappingService.GetDefaultQobuzQuality()),
                            ValidatedAt = DateTime.UtcNow,
                            ValidationMessages = validationMessages,
                            QualityProfile = qualityProfile,
                            SelectedQobuzQuality = selectedQuality,
                            QualityRecommendation = qualityRecommendation
                        });
                    }
                    else
                    {
                        _logger.Warn("Album validation failed for {0} - {1}: {2}", 
                            match.Key.Artist?.ArtistName, match.Key.Title, string.Join("; ", validationMessages));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating album: {0} - {1}", 
                        match.Key.Artist?.ArtistName, match.Key.Title);
                }
            }

            _logger.Info("Validation complete: {0}/{1} albums ready for download", validatedItems.Count, albumMatches.Count);
            return validatedItems;
        }

        /// <summary>
        /// Clears the quality profile cache to force fresh data on next request.
        /// </summary>
        public void ClearQualityProfileCache()
        {
            lock (_qualityProfileCacheLock)
            {
                _qualityProfileCache.Clear();
            }
            _logger.Debug("Quality profile cache cleared");
        }

        #region Private Helper Methods

        /// <summary>
        /// Retrieves the quality profile for a specific album, with caching for performance.
        /// </summary>
        private async Task<LidarrQualityProfile> GetQualityProfileForAlbumAsync(LidarrAlbum album, CancellationToken cancellationToken)
        {
            var profileId = album.QualityProfileId > 0 ? album.QualityProfileId : album.ProfileId;
            
            if (profileId <= 0)
            {
                _logger.Debug("No quality profile ID found for album {0}, using default", album.GetFullTitle());
                return null;
            }

            // Check cache first
            lock (_qualityProfileCacheLock)
            {
                if (_qualityProfileCache.TryGetValue(profileId, out var cachedProfile))
                {
                    return cachedProfile;
                }
            }

            // Fetch from API
            try
            {
                var profile = await _lidarrApiClient.GetQualityProfileAsync(profileId).ConfigureAwait(false);
                
                // Cache the result
                lock (_qualityProfileCacheLock)
                {
                    _qualityProfileCache[profileId] = profile;
                }

                _logger.Debug("Retrieved quality profile '{0}' (ID: {1}) for album {2}", 
                    profile?.Name, profileId, album.GetFullTitle());
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve quality profile {0} for album {1}", profileId, album.GetFullTitle());
                return null;
            }
        }

        /// <summary>
        /// Determines available Qobuz qualities for an album based on its technical specifications.
        /// </summary>
        private List<string> GetAvailableQobuzQualities(QobuzAlbum album)
        {
            var availableQualities = new List<string>();
            var sampleTrack = album.GetTracks().FirstOrDefault();
            
            if (sampleTrack == null)
            {
                // If no track info, assume basic MP3 availability
                return new List<string> { "mp3-320" };
            }

            var maxBitDepth = sampleTrack.MaximumBitDepth ?? 16;
            var maxSampleRate = sampleTrack.MaximumSampleRate ?? 44100;
            var hasHiRes = sampleTrack.HasHiResQuality();

            // Determine available qualities based on technical specifications
            if (hasHiRes && maxBitDepth >= 24 && maxSampleRate >= 96000)
            {
                availableQualities.Add("flac-hires");
            }

            if (maxBitDepth >= 16)
            {
                availableQualities.Add("flac-cd");
            }

            // MP3 is generally always available
            availableQualities.Add("mp3-320");

            _logger.Debug("Available qualities for album {0}: {1}", album.Title, string.Join(", ", availableQualities));
            return availableQualities;
        }

        /// <summary>
        /// Converts a Qobuz quality string to the integer format used by the download system.
        /// </summary>
        private int ConvertQobuzQualityToInt(string qobuzQuality)
        {
            return qobuzQuality?.ToLower() switch
            {
                "flac-hires" => 27,  // Hi-Res FLAC
                "flac-cd" => 6,      // CD Quality FLAC
                "mp3-320" => 5,      // 320kbps MP3
                _ => 6               // Default to CD quality
            };
        }

        /// <summary>
        /// Searches for a single album on Qobuz with fuzzy matching.
        /// </summary>
        private async Task<QobuzAlbum> SearchSingleAlbumAsync(LidarrAlbum lidarrAlbum, CancellationToken cancellationToken)
        {
            try
            {
                var searchTerm = BuildSearchTerm(lidarrAlbum);
                _logger.Debug("Searching Qobuz for: {0}", searchTerm);

                // Use the generic API client to search for albums
                var parameters = new Dictionary<string, string>
                {
                    {"query", searchTerm},
                    {"type", "albums"},
                    {"limit", "5"}
                };
                
                var searchResponse = await _qobuzApiClient.GetAsync<QobuzSearchResponse>("/catalog/search", parameters).ConfigureAwait(false);
                
                if (searchResponse?.Albums?.Items == null || !searchResponse.Albums.Items.Any())
                {
                    _logger.Debug("No Qobuz results found for: {0}", searchTerm);
                    return null;
                }

                // Find best match using fuzzy matching
                var bestMatch = FindBestAlbumMatch(lidarrAlbum, searchResponse.Albums.Items);
                
                if (bestMatch != null)
                {
                    _logger.Debug("Found Qobuz match: {0} - {1} (ID: {2})", 
                        bestMatch.Artist?.Name, bestMatch.Title, bestMatch.Id);
                }

                return bestMatch;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error searching for album: {0} - {1}", 
                    lidarrAlbum.Artist?.ArtistName, lidarrAlbum.Title);
                return null;
            }
        }

        private string BuildSearchTerm(LidarrAlbum album)
        {
            var artist = album.Artist?.ArtistName?.Trim();
            var title = album.Title?.Trim();
            
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
            {
                return title ?? artist ?? "Unknown";
            }
            
            return $"{artist} {title}";
        }

        private QobuzAlbum FindBestAlbumMatch(LidarrAlbum lidarrAlbum, IEnumerable<QobuzAlbum> qobuzAlbums)
        {
            if (!qobuzAlbums.Any())
                return null;

            var targetArtist = lidarrAlbum.Artist?.ArtistName?.ToLowerInvariant().Trim() ?? "";
            var targetTitle = lidarrAlbum.Title?.ToLowerInvariant().Trim() ?? "";

            var bestMatch = qobuzAlbums
                .Select(album => new
                {
                    Album = album,
                    ArtistScore = CalculateSimilarity(targetArtist, album.Artist?.Name?.ToLowerInvariant().Trim() ?? ""),
                    TitleScore = CalculateSimilarity(targetTitle, album.Title?.ToLowerInvariant().Trim() ?? "")
                })
                .Where(match => match.ArtistScore > 0.7 && match.TitleScore > 0.7) // Require decent similarity
                .OrderByDescending(match => (match.ArtistScore + match.TitleScore) / 2)
                .FirstOrDefault();

            return bestMatch?.Album;
        }

        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            if (s1 == s2)
                return 1.0;

            // Simple similarity based on Levenshtein distance
            var distance = LidarrInputValidator.LevenshteinDistance(s1, s2);
            var maxLength = Math.Max(s1.Length, s2.Length);
            
            return maxLength > 0 ? 1.0 - (double)distance / maxLength : 0;
        }

        private int GetEffectiveConcurrency(int requestedConcurrency)
        {
            if (requestedConcurrency <= 0)
                return Math.Max(MIN_CONCURRENCY, Environment.ProcessorCount);
            
            return Math.Min(MAX_CONCURRENCY, Math.Max(MIN_CONCURRENCY, requestedConcurrency));
        }

        #endregion
    }
}