using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Models;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Common.Services
{
    /// <summary>
    /// WORKING APPROACH: Mixin helpers that any streaming plugin can use via composition.
    /// Avoids inheritance complexity while providing shared functionality.
    /// Based on successful patterns from working Qobuzarr implementation.
    /// </summary>
    public class StreamingIndexerMixin
    {
        private readonly string _serviceName;
        private readonly StreamingCacheHelper _cache;
        private readonly object _rateLimitLock = new object();
        private DateTime _lastRequestTime = DateTime.MinValue;

        public StreamingIndexerMixin(string serviceName, StreamingCacheHelper cache = null)
        {
            _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
            _cache = cache;
        }

        /// <summary>
        /// Applies rate limiting using shared patterns.
        /// Call this before making API requests.
        /// </summary>
        public async Task ApplyRateLimitAsync(int requestsPerMinute)
        {
            if (requestsPerMinute <= 0) return;

            await Task.Run(() =>
            {
                lock (_rateLimitLock)
                {
                    var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                    var minInterval = TimeSpan.FromMinutes(1.0 / requestsPerMinute);

                    if (timeSinceLastRequest < minInterval)
                    {
                        var waitTime = minInterval - timeSinceLastRequest;
                        Task.Delay(waitTime).Wait();
                    }

                    _lastRequestTime = DateTime.UtcNow;
                }
            });
        }

        /// <summary>
        /// Gets cached search results if available.
        /// </summary>
        public List<StreamingSearchResult> GetCachedResults(string searchTerm, Dictionary<string, string> parameters = null)
        {
            if (_cache == null) return null;

            var cacheParams = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>())
            {
                ["searchTerm"] = searchTerm
            };

            return _cache.Get<List<StreamingSearchResult>>("search", cacheParams);
        }

        /// <summary>
        /// Caches search results for future use.
        /// </summary>
        public void CacheResults(string searchTerm, List<StreamingSearchResult> results, TimeSpan duration, Dictionary<string, string> parameters = null)
        {
            if (_cache == null || results == null) return;

            var cacheParams = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>())
            {
                ["searchTerm"] = searchTerm
            };

            _cache.Set("search", cacheParams, results, duration);
        }

        /// <summary>
        /// Converts streaming results to properties dictionaries for flexible ReleaseInfo creation.
        /// Avoids type dependency issues while providing all necessary data.
        /// </summary>
        public List<Dictionary<string, object>> ConvertToReleaseProperties(List<StreamingSearchResult> results)
        {
            return results?.Select(r => LidarrIntegrationHelpers.CreateReleaseProperties(r, _serviceName))
                          .ToList() ?? new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Validates search criteria before making API calls.
        /// </summary>
        public (bool isValid, string errorMessage) ValidateSearch(string artist, string album, string searchTerm)
        {
            return LidarrIntegrationHelpers.ValidateSearchRequest(artist, album, searchTerm);
        }

        /// <summary>
        /// Builds search URL with parameters using shared utilities.
        /// </summary>
        public string BuildSearchUrl(string baseUrl, string endpoint, Dictionary<string, string> parameters)
        {
            return StreamingIndexerHelpers.BuildSearchUrl(baseUrl, endpoint, parameters);
        }

        /// <summary>
        /// Creates standard headers for streaming service requests.
        /// </summary>
        public Dictionary<string, string> CreateHeaders(string userAgent, string authToken = null)
        {
            return StreamingIndexerHelpers.CreateStreamingHeaders(userAgent, authToken);
        }
    }

    /// <summary>
    /// Mixin helper for streaming download clients.
    /// Provides shared download patterns without inheritance complexity.
    /// </summary>
    public class StreamingDownloadMixin
    {
        private readonly string _serviceName;
        private readonly Dictionary<string, DownloadJobInfo> _activeJobs = new Dictionary<string, DownloadJobInfo>();
        private readonly object _jobsLock = new object();

        public StreamingDownloadMixin(string serviceName)
        {
            _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        }

        /// <summary>
        /// Creates a download job and tracks it.
        /// </summary>
        public string StartDownloadJob(StreamingAlbum album, string outputPath)
        {
            var jobId = $"{_serviceName}_{album.Id}_{DateTime.UtcNow.Ticks}";
            
            var job = new DownloadJobInfo
            {
                Id = jobId,
                Album = album,
                OutputPath = outputPath,
                StartTime = DateTime.UtcNow,
                Status = "Queued"
            };

            lock (_jobsLock)
            {
                _activeJobs[jobId] = job;
            }

            return jobId;
        }

        /// <summary>
        /// Gets the status of a download job.
        /// </summary>
        public DownloadJobInfo GetJobStatus(string jobId)
        {
            lock (_jobsLock)
            {
                return _activeJobs.GetValueOrDefault(jobId);
            }
        }

        /// <summary>
        /// Updates download job progress.
        /// </summary>
        public void UpdateJobProgress(string jobId, int completedTracks, int totalTracks, string status = "Downloading")
        {
            lock (_jobsLock)
            {
                if (_activeJobs.TryGetValue(jobId, out var job))
                {
                    job.CompletedTracks = completedTracks;
                    job.TotalTracks = totalTracks;
                    job.Status = status;
                    job.LastUpdated = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Completes a download job.
        /// </summary>
        public void CompleteJob(string jobId, bool success, string errorMessage = null)
        {
            lock (_jobsLock)
            {
                if (_activeJobs.TryGetValue(jobId, out var job))
                {
                    job.Status = success ? "Completed" : "Failed";
                    job.ErrorMessage = errorMessage;
                    job.CompletedTime = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Creates safe file path using shared utilities.
        /// </summary>
        public string CreateSafeFilePath(StreamingTrack track, string baseDirectory)
        {
            var artistName = FileNameSanitizer.SanitizeFileName(track.Artist?.Name ?? "Unknown Artist");
            var albumTitle = FileNameSanitizer.SanitizeFileName(track.Album?.Title ?? "Unknown Album");
            var trackTitle = FileNameSanitizer.SanitizeFileName(track.Title ?? "Unknown Track");

            var fileName = $"{track.TrackNumber:D2} - {trackTitle}";
            return System.IO.Path.Combine(baseDirectory, artistName, albumTitle, fileName);
        }

        /// <summary>
        /// Gets all active downloads.
        /// </summary>
        public List<DownloadJobInfo> GetActiveJobs()
        {
            lock (_jobsLock)
            {
                return _activeJobs.Values.ToList();
            }
        }
    }

    /// <summary>
    /// Information about a download job.
    /// </summary>
    public class DownloadJobInfo
    {
        public string Id { get; set; }
        public StreamingAlbum Album { get; set; }
        public string OutputPath { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime? CompletedTime { get; set; }
        public int CompletedTracks { get; set; }
        public int TotalTracks { get; set; }
        public string ErrorMessage { get; set; }

        public double ProgressPercent => TotalTracks > 0 ? (double)CompletedTracks / TotalTracks * 100 : 0;
        public TimeSpan Duration => (CompletedTime ?? DateTime.UtcNow) - StartTime;
    }

    /// <summary>
    /// WORKING PATTERN: Authentication mixin for streaming services.
    /// Provides shared session management without complex inheritance.
    /// </summary>
    public class StreamingAuthMixin
    {
        private readonly string _serviceName;
        private object _cachedSession;
        private readonly object _sessionLock = new object();

        public StreamingAuthMixin(string serviceName)
        {
            _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        }

        /// <summary>
        /// Gets cached session if available and valid.
        /// </summary>
        public T GetCachedSession<T>() where T : class
        {
            lock (_sessionLock)
            {
                return _cachedSession as T;
            }
        }

        /// <summary>
        /// Stores session in cache.
        /// </summary>
        public void StoreSession<T>(T session) where T : class
        {
            lock (_sessionLock)
            {
                _cachedSession = session;
            }
        }

        /// <summary>
        /// Clears cached session.
        /// </summary>
        public void ClearSession()
        {
            lock (_sessionLock)
            {
                _cachedSession = null;
            }
        }

        /// <summary>
        /// Validates session using shared patterns.
        /// </summary>
        public bool IsSessionValid<T>(T session, Func<T, bool> validator) where T : class
        {
            if (session == null) return false;
            
            try
            {
                return validator(session);
            }
            catch
            {
                return false;
            }
        }
    }
}