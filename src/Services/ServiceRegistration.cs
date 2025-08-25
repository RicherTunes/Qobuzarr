using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using ApiClient = Lidarr.Plugin.Qobuzarr.API;
using ServiceInterfaces = Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using OrchestratorInterfaces = Lidarr.Plugin.Qobuzarr.Services.Orchestrators;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Core.Api;
using Lidarr.Plugin.Qobuzarr.Services.Core.Auth;
using Lidarr.Plugin.Qobuzarr.Services.Core.Quality;
using Lidarr.Plugin.Qobuzarr.Services.Core.Streaming;
using Lidarr.Plugin.Qobuzarr.Services.Observability;
using Lidarr.Plugin.Qobuzarr.Services.Orchestrators;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service registration for the new decomposed architecture.
    /// Replaces the mega-service pattern with focused, single-responsibility services.
    /// </summary>
    public static class ServiceRegistration
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Registers all decomposed services with the DI container.
        /// This is the single point of service registration for the entire plugin.
        /// </summary>
        public static void RegisterServices(IContainer container)
        {
            Logger.Info("Starting decomposed service registration for Qobuzarr plugin");

            try
            {
                // Core Quality Services
                RegisterQualityServices(container);

                // Core API Services
                RegisterApiServices(container);

                // Core Authentication Services
                RegisterAuthenticationServices(container);

                // Core Streaming Services
                RegisterStreamingServices(container);

                // Orchestration Services
                RegisterOrchestrators(container);

                // Observability Services
                RegisterObservabilityServices(container);

                // Legacy Service Compatibility (temporary during migration)
                RegisterLegacyCompatibility(container);

                Logger.Info("Successfully registered all decomposed services");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to register services");
                throw;
            }
        }

        private static void RegisterQualityServices(IContainer container)
        {
            Logger.Debug("Registering Quality domain services");

            // Quality Definition Service - Single source of truth for quality formats
            container.Register<ServiceInterfaces.IQualityDefinitionService, QualityDefinitionService>(
                Reuse.Singleton,
                Made.Of(() => new QualityDefinitionService(
                    Arg.Of<IQobuzLogger>())),
                setup: Setup.With(condition: r => r.IsResolutionRoot));

            // Quality Fallback Strategy - Handles fallback chain logic
            container.Register<ServiceInterfaces.IQualityFallbackStrategy, QualityFallbackStrategy>(
                Reuse.Singleton,
                Made.Of(() => new QualityFallbackStrategy(
                    Arg.Of<ServiceInterfaces.IQualityDefinitionService>(),
                    Arg.Of<IQobuzLogger>())));

            // Quality Detector - Detects available qualities
            container.Register<ServiceInterfaces.IQualityDetector, QualityDetector>(
                Reuse.Singleton,
                Made.Of(() => new QualityDetector(
                    Arg.Of<ServiceInterfaces.IQobuzApiClient>(),
                    Arg.Of<ServiceInterfaces.IQualityDefinitionService>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Quality services registered successfully");
        }

        private static void RegisterApiServices(IContainer container)
        {
            Logger.Debug("Registering API domain services");

            // Standard API Client with rate limiting and caching
            container.Register<IQobuzApiClient, QobuzApiClient>(
                Reuse.Singleton,
                Made.Of(() => new QobuzApiClient(
                    Arg.Of<IHttpClient>(),
                    Arg.Of<ISessionManager>(),
                    Arg.Of<IQobuzLogger>())),
                setup: Setup.With(condition: r => !r.Parent.ServiceType.Name.Contains("Diagnostic")));

            // Diagnostic API Client without rate limiting (for testing)
            container.Register<IQobuzDiagnosticApiClient, QobuzDiagnosticApiClient>(
                Reuse.Singleton,
                Made.Of(() => new QobuzDiagnosticApiClient(
                    Arg.Of<IHttpClient>(),
                    Arg.Of<ISessionManager>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("API services registered successfully");
        }

        private static void RegisterAuthenticationServices(IContainer container)
        {
            Logger.Debug("Registering Authentication domain services");

            // Session Manager - Manages session lifecycle
            container.Register<ISessionManager, SessionManager>(
                Reuse.Singleton,
                Made.Of(() => new SessionManager(
                    Arg.Of<IDatabase>(),
                    Arg.Of<IQobuzLogger>())));

            // Credential Validator - Validates and sanitizes credentials
            container.Register<ICredentialValidator, CredentialValidator>(
                Reuse.Singleton,
                Made.Of(() => new CredentialValidator(
                    Arg.Of<IQobuzLogger>())));

            // Token Refresher - Handles token refresh logic
            container.Register<ITokenRefresher, TokenRefresher>(
                Reuse.Singleton,
                Made.Of(() => new TokenRefresher(
                    Arg.Of<IQobuzApiClient>(),
                    Arg.Of<ISessionManager>(),
                    Arg.Of<IQobuzLogger>())));

            // Keep existing authentication service for now
            container.RegisterDelegate<IQobuzAuthenticationService>(
                r => r.Resolve<QobuzAuthenticationService>(),
                Reuse.Singleton);

            Logger.Debug("Authentication services registered successfully");
        }

        private static void RegisterStreamingServices(IContainer container)
        {
            Logger.Debug("Registering Streaming domain services");

            // Stream URL Validator - Validates stream URLs
            container.Register<IStreamUrlValidator, StreamUrlValidator>(
                Reuse.Singleton,
                Made.Of(() => new StreamUrlValidator(
                    Arg.Of<IQobuzLogger>())));

            // Stream URL Provider - Provides validated stream URLs
            container.Register<IStreamUrlProvider, StreamUrlProvider>(
                Reuse.Singleton,
                Made.Of(() => new StreamUrlProvider(
                    Arg.Of<IQobuzApiClient>(),
                    Arg.Of<IStreamUrlValidator>(),
                    Arg.Of<IQualityFallbackStrategy>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Streaming services registered successfully");
        }

        private static void RegisterOrchestrators(IContainer container)
        {
            Logger.Debug("Registering Orchestrator services");

            // Quality Orchestrator - Coordinates quality-related services
            container.Register<IQualityOrchestrator, QualityOrchestrator>(
                Reuse.Singleton,
                Made.Of(() => new QualityOrchestrator(
                    Arg.Of<IQualityDefinitionService>(),
                    Arg.Of<IQualityFallbackStrategy>(),
                    Arg.Of<IQualityDetector>(),
                    Arg.Of<IStreamUrlProvider>(),
                    Arg.Of<IQobuzLogger>())));

            // Authentication Orchestrator - Coordinates auth services
            container.Register<IAuthenticationOrchestrator, AuthenticationOrchestrator>(
                Reuse.Singleton,
                Made.Of(() => new AuthenticationOrchestrator(
                    Arg.Of<ISessionManager>(),
                    Arg.Of<ICredentialValidator>(),
                    Arg.Of<ITokenRefresher>(),
                    Arg.Of<IQobuzAuthenticationService>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Orchestrator services registered successfully");
        }

        private static void RegisterObservabilityServices(IContainer container)
        {
            Logger.Debug("Registering Observability services");

            // Metrics Collector - Prometheus metrics
            container.Register<IMetricsCollector, MetricsCollector>(
                Reuse.Singleton,
                Made.Of(() => new MetricsCollector(
                    Arg.Of<IQobuzLogger>())));

            // Health Check Service - Service health monitoring
            container.Register<IHealthCheckService, HealthCheckService>(
                Reuse.Singleton,
                Made.Of(() => new HealthCheckService(
                    Arg.Of<IQobuzApiClient>(),
                    Arg.Of<ISessionManager>(),
                    Arg.Of<IDiskProvider>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Observability services registered successfully");
        }

        private static void RegisterLegacyCompatibility(IContainer container)
        {
            Logger.Debug("Registering legacy compatibility layer");

            // Map IQobuzQualityManager to QualityOrchestrator
            // This maintains compatibility while we update all references
            container.RegisterDelegate<Consolidated.IQobuzQualityManager>(
                resolver => 
                {
                    var orchestrator = resolver.Resolve<IQualityOrchestrator>();
                    var apiClient = resolver.Resolve<IQobuzApiClient>();
                    var logger = resolver.Resolve<IQobuzLogger>();
                    
                    // Create adapter that implements old interface using new services
                    return new QualityManagerAdapter(orchestrator, apiClient, logger);
                },
                Reuse.Singleton);

            Logger.Debug("Legacy compatibility registered");
        }

        /// <summary>
        /// Verifies that all required services are properly registered.
        /// Called after registration to ensure consistency.
        /// </summary>
        public static bool ValidateRegistration(IContainer container)
        {
            Logger.Debug("Validating service registration");

            var requiredServices = new[]
            {
                typeof(IQualityDefinitionService),
                typeof(IQualityFallbackStrategy),
                typeof(IQualityDetector),
                typeof(IQobuzApiClient),
                typeof(ISessionManager),
                typeof(ICredentialValidator),
                typeof(ITokenRefresher),
                typeof(IStreamUrlValidator),
                typeof(IStreamUrlProvider),
                typeof(IQualityOrchestrator),
                typeof(IAuthenticationOrchestrator),
                typeof(IMetricsCollector),
                typeof(IHealthCheckService)
            };

            foreach (var serviceType in requiredServices)
            {
                try
                {
                    var service = container.Resolve(serviceType);
                    if (service == null)
                    {
                        Logger.Error("Failed to resolve service: {0}", serviceType.Name);
                        return false;
                    }
                    Logger.Trace("Successfully resolved: {0}", serviceType.Name);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Exception resolving service: {0}", serviceType.Name);
                    return false;
                }
            }

            Logger.Info("All services validated successfully");
            return true;
        }
    }

    /// <summary>
    /// Temporary adapter to maintain compatibility with IQobuzQualityManager
    /// while migrating to the new decomposed architecture.
    /// </summary>
    internal class QualityManagerAdapter : Consolidated.IQobuzQualityManager
    {
        private readonly ServiceInterfaces.IQualityOrchestrator _orchestrator;
        private readonly ApiClient.IQobuzApiClient _apiClient;
        private readonly IQobuzLogger _logger;

        public QualityManagerAdapter(
            ServiceInterfaces.IQualityOrchestrator orchestrator,
            ApiClient.IQobuzApiClient apiClient,
            IQobuzLogger logger)
        {
            _orchestrator = orchestrator;
            _apiClient = apiClient;
            _logger = logger;
        }

        // Implementation of IQobuzQualityManager interface methods
        
        public async Task<Consolidated.QualityDetectionResult> DetectAvailableQualitiesAsync(
            string trackId, 
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: DetectAvailableQualitiesAsync for track {0}", trackId);
            var result = await _orchestrator.DetectAvailableQualitiesAsync(trackId, cancellationToken);
            
            return new Consolidated.QualityDetectionResult
            {
                Success = result.Success,
                AvailableQualities = result.AvailableQualities.Select(id => new Consolidated.QobuzQuality { Id = id }).ToList(),
                Error = result.Error
            };
        }

        public async Task<Consolidated.AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality, 
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: DetectAlbumQualityAsync for album {0}", album.Id);
            
            // For now, return a simple implementation - could be enhanced later
            return new Consolidated.AlbumQualityResult
            {
                Success = true,
                AlbumId = album.Id,
                RecommendedQuality = new Consolidated.QobuzQuality { Id = preferredQuality },
                UniformQuality = true,
                SampledTracks = 0,
                TotalTracks = album.Tracks?.Count ?? 0
            };
        }

        public Consolidated.QobuzQuality MapLidarrQuality(LidarrQualityProfile profile)
        {
            _logger.Debug("Adapter: MapLidarrQuality");
            var qualityId = _orchestrator.MapLidarrQualityToQobuz(profile);
            return new Consolidated.QobuzQuality { Id = qualityId };
        }

        public List<Consolidated.QobuzQuality> GetQualityFallbackChain(Consolidated.QobuzQuality preferred)
        {
            _logger.Debug("Adapter: GetQualityFallbackChain for quality {0}", preferred.Id);
            var fallbackIds = _orchestrator.GetFallbackChain(preferred.Id);
            return fallbackIds.Select(id => new Consolidated.QobuzQuality { Id = id }).ToList();
        }

        public async Task<Consolidated.QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            Consolidated.QobuzQuality preferred,
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: SelectBestQualityAsync for track {0}", trackId);
            
            var result = await _orchestrator.SelectBestQualityAsync(trackId, preferred.Id, cancellationToken);
            
            return new Consolidated.QualitySelectionResult
            {
                Success = result.Success,
                SelectedQuality = new Consolidated.QobuzQuality { Id = result.QualityId },
                StreamInfo = result.StreamUrl != null ? new Consolidated.StreamInfo 
                { 
                    Url = result.StreamUrl,
                    QualityId = result.QualityId
                } : null,
                Error = result.Error
            };
        }

        public async Task<T> ExecuteWithQualityFallbackAsync<T>(
            Func<Consolidated.QobuzQuality, Task<T>> operation, 
            Consolidated.QobuzQuality preferred = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: ExecuteWithQualityFallbackAsync");
            
            var preferredId = preferred?.Id ?? 27; // Default to highest quality
            var fallbackChain = _orchestrator.GetFallbackChain(preferredId);
            
            foreach (var qualityId in fallbackChain)
            {
                try
                {
                    var quality = new Consolidated.QobuzQuality { Id = qualityId };
                    return await operation(quality);
                }
                catch (Exception ex)
                {
                    _logger.Debug("Quality {0} failed, trying next: {1}", qualityId, ex.Message);
                    continue;
                }
            }
            
            throw new InvalidOperationException("All quality fallback options failed");
        }

        public async Task<Consolidated.StreamInfo> GetStreamInfoAsync(
            string trackId, 
            Consolidated.QobuzQuality quality, 
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: GetStreamInfoAsync for track {0}", trackId);
            
            var streamInfo = await _orchestrator.GetStreamInfoAsync(trackId, quality.Id, cancellationToken);
            
            return new Consolidated.StreamInfo
            {
                Url = streamInfo.Url,
                QualityId = streamInfo.QualityId,
                FileSizeBytes = streamInfo.FileSizeBytes,
                Duration = streamInfo.Duration,
                ExpiresAt = streamInfo.ExpiresAt
            };
        }

        public async Task<Consolidated.BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            Consolidated.QobuzQuality quality, 
            CancellationToken cancellationToken = default)
        {
            _logger.Debug("Adapter: GetBatchStreamInfoAsync for {0} tracks", trackIds.Count);
            
            var results = await _orchestrator.ProcessBatchQualityAsync(trackIds, quality.Id, 5, cancellationToken);
            
            return new Consolidated.BatchStreamResult
            {
                Success = results.Values.All(r => r.Success),
                TotalRequested = trackIds.Count,
                SuccessCount = results.Values.Count(r => r.Success),
                FailureCount = results.Values.Count(r => !r.Success),
                StreamInfos = results.Where(kvp => kvp.Value.Success)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new Consolidated.StreamInfo
                        {
                            Url = kvp.Value.StreamUrl,
                            QualityId = kvp.Value.QualityId
                        })
            };
        }
    }
}