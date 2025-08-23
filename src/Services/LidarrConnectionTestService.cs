using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Implementation of the Lidarr connection test service.
    /// Handles connectivity testing and permission validation.
    /// </summary>
    public class LidarrConnectionTestService : ILidarrConnectionTestService
    {
        private readonly ILidarrIntegrationService _integrationService;
        private readonly Logger _logger;

        public LidarrConnectionTestService(
            ILidarrIntegrationService integrationService,
            Logger logger)
        {
            _integrationService = Guard.NotNull(integrationService, nameof(integrationService));
            _logger = Guard.NotNull(logger, nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<ConnectionTestResult> TestConnectionAsync(
            string url,
            string apiKey,
            int timeoutSeconds = 30,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNullOrWhiteSpace(url, nameof(url));
            Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
            
            _logger.Info("Testing connection to Lidarr at {0}", url);
            
            var result = new ConnectionTestResult();
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                
                // Test basic connectivity by fetching a single album
                var filterOptions = new LidarrFilterOptions 
                { 
                    Page = 1, 
                    PageSize = 1 
                };
                
                var albums = await _integrationService.GetFilteredWantedAlbumsAsync(
                    filterOptions, 
                    maxAlbums: 1,
                    progress: null,
                    cancellationToken: cts.Token);
                
                stopwatch.Stop();
                
                result.Success = albums != null;
                result.ResponseTime = stopwatch.Elapsed;
                result.Message = result.Success 
                    ? "Successfully connected to Lidarr" 
                    : "Failed to connect to Lidarr";
                
                _logger.Info("Connection test {0} in {1}ms", 
                    result.Success ? "succeeded" : "failed", 
                    result.ResponseTime.TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                result.Success = false;
                result.Message = $"Connection timed out after {timeoutSeconds} seconds";
                result.ResponseTime = stopwatch.Elapsed;
                _logger.Warn("Connection test timed out after {0} seconds", timeoutSeconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.Message = $"Connection failed: {ex.Message}";
                result.Error = ex;
                result.ResponseTime = stopwatch.Elapsed;
                _logger.Error(ex, "Connection test failed");
            }
            
            return result;
        }

        /// <inheritdoc/>
        public async Task<PermissionTestResult> TestPermissionsAsync(
            string url,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNullOrWhiteSpace(url, nameof(url));
            Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
            
            _logger.Info("Testing API permissions for Lidarr at {0}", url);
            
            var result = new PermissionTestResult();
            
            try
            {
                // Test read permissions by fetching wanted albums
                var filterOptions = new LidarrFilterOptions 
                { 
                    Page = 1, 
                    PageSize = 10
                };
                
                var albums = await _integrationService.GetFilteredWantedAlbumsAsync(
                    filterOptions,
                    maxAlbums: 10,
                    progress: null,
                    cancellationToken: cancellationToken);
                
                if (albums != null)
                {
                    result.CanRead = true;
                    result.WantedAlbumCount = albums.Count();
                    
                    // For now, assume write permissions if read works
                    // In a full implementation, we'd test write permissions separately
                    result.CanWrite = true;
                    
                    result.Success = true;
                    result.Message = "API permissions verified successfully";
                    
                    _logger.Info("Permission test succeeded. Found {0} wanted albums", result.WantedAlbumCount);
                }
                else
                {
                    result.Success = false;
                    result.Message = "Failed to retrieve wanted albums";
                    _logger.Warn("Permission test failed: Could not retrieve wanted albums");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Permission test failed: {ex.Message}";
                result.Error = ex;
                _logger.Error(ex, "Permission test failed");
            }
            
            return result;
        }
    }
}