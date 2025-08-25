using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using API = Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Consolidated = Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using CoreApi = Lidarr.Plugin.Qobuzarr.Services.Core.Api;
using CoreAuth = Lidarr.Plugin.Qobuzarr.Services.Core.Auth;
using CoreQuality = Lidarr.Plugin.Qobuzarr.Services.Core.Quality;
using CoreStreaming = Lidarr.Plugin.Qobuzarr.Services.Core.Streaming;
using ServiceObservability = Lidarr.Plugin.Qobuzarr.Services.Observability;
using ServiceOrchestrators = Lidarr.Plugin.Qobuzarr.Services.Orchestrators;
using ServiceInterfaces = Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Common.Cache;
using Lidarr.Plugin.Qobuzarr.Integration;

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
                // Register core adapters first
                RegisterCoreAdapters(container);

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

        private static void RegisterCoreAdapters(IContainer container)
        {
            Logger.Debug("Registering core adapters");

            // Register logger adapter to convert NLog.Logger to IQobuzLogger
            container.Register<IQobuzLogger, LidarrLoggerAdapter>(
                Reuse.Singleton,
                Made.Of(() => new LidarrLoggerAdapter(Arg.Of<Logger>())));

            Logger.Debug("Core adapters registered successfully");
        }

        private static void RegisterQualityServices(IContainer container)
        {
            Logger.Debug("Registering Quality domain services");

            // Quality Definition Service - Single source of truth for quality formats (no constructor parameters)
            container.Register<ServiceInterfaces.IQualityDefinitionService, CoreQuality.QualityDefinitionService>(
                Reuse.Singleton);

            // Quality Fallback Strategy - Handles fallback chain logic
            container.Register<ServiceInterfaces.IQualityFallbackStrategy, CoreQuality.QualityFallbackStrategy>(
                Reuse.Singleton,
                Made.Of(() => new CoreQuality.QualityFallbackStrategy(
                    Arg.Of<ServiceInterfaces.IQualityDefinitionService>(),
                    Arg.Of<IQobuzLogger>())));

            // Quality Detector - Detects available qualities
            container.Register<ServiceInterfaces.IQualityDetector, CoreQuality.QualityDetector>(
                Reuse.Singleton,
                Made.Of(() => new CoreQuality.QualityDetector(
                    Arg.Of<API.IQobuzApiClient>(),
                    Arg.Of<ServiceInterfaces.IQualityDefinitionService>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Quality services registered successfully");
        }

        private static void RegisterApiServices(IContainer container)
        {
            Logger.Debug("Registering API domain services");

            // Register the main API client implementation 
            container.Register<API.QobuzApiClient>(
                Reuse.Singleton,
                Made.Of(() => new API.QobuzApiClient(
                    Arg.Of<IHttpClient>(),
                    Arg.Of<ICacheManager>(),
                    Arg.Of<Logger>())));

            // Register both interfaces to point to the same singleton instance
            container.RegisterDelegate<API.IQobuzApiClient>(
                r => r.Resolve<API.QobuzApiClient>(),
                Reuse.Singleton);

            container.RegisterDelegate<ServiceInterfaces.IQobuzApiClient>(
                r => r.Resolve<API.QobuzApiClient>(),
                Reuse.Singleton);

            // Diagnostic API Client without rate limiting (for testing)
            container.Register<ServiceInterfaces.IQobuzDiagnosticApiClient, CoreApi.QobuzDiagnosticApiClient>(
                Reuse.Singleton,
                Made.Of(() => new CoreApi.QobuzDiagnosticApiClient(
                    Arg.Of<IHttpClient>(),
                    Arg.Of<Logger>())));

            Logger.Debug("API services registered successfully");
        }

        private static void RegisterAuthenticationServices(IContainer container)
        {
            Logger.Debug("Registering Authentication domain services");

            // Session Manager - Manages session lifecycle
            container.Register<ServiceInterfaces.ISessionManager, CoreAuth.SessionManager>(
                Reuse.Singleton,
                Made.Of(() => new CoreAuth.SessionManager(
                    Arg.Of<ICacheManager>(),
                    Arg.Of<Logger>())));

            // Credential Validator - Validates and sanitizes credentials
            container.Register<ServiceInterfaces.ICredentialValidator, CoreAuth.CredentialValidator>(
                Reuse.Singleton,
                Made.Of(() => new CoreAuth.CredentialValidator(
                    Arg.Of<Logger>())));

            // Token Refresher - Handles token refresh logic
            container.Register<ServiceInterfaces.ITokenRefresher, CoreAuth.TokenRefresher>(
                Reuse.Singleton,
                Made.Of(() => new CoreAuth.TokenRefresher(
                    Arg.Of<IQobuzAuthenticationService>(),
                    Arg.Of<Logger>())));

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
            container.Register<ServiceInterfaces.IStreamUrlValidator, CoreStreaming.StreamUrlValidator>(
                Reuse.Singleton,
                Made.Of(() => new CoreStreaming.StreamUrlValidator(
                    Arg.Of<IQobuzLogger>())));

            // Stream URL Provider - Provides validated stream URLs
            container.Register<ServiceInterfaces.IStreamUrlProvider, CoreStreaming.StreamUrlProvider>(
                Reuse.Singleton,
                Made.Of(() => new CoreStreaming.StreamUrlProvider(
                    Arg.Of<API.IQobuzApiClient>(),
                    Arg.Of<ServiceInterfaces.IStreamUrlValidator>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Streaming services registered successfully");
        }

        private static void RegisterOrchestrators(IContainer container)
        {
            Logger.Debug("Registering Orchestrator services");

            // Quality Orchestrator - Coordinates quality-related services
            container.Register<ServiceInterfaces.IQualityOrchestrator, ServiceOrchestrators.QualityOrchestrator>(
                Reuse.Singleton,
                Made.Of(() => new ServiceOrchestrators.QualityOrchestrator(
                    Arg.Of<ServiceInterfaces.IQualityDefinitionService>(),
                    Arg.Of<ServiceInterfaces.IQualityFallbackStrategy>(),
                    Arg.Of<ServiceInterfaces.IQualityDetector>(),
                    Arg.Of<ServiceInterfaces.IStreamUrlProvider>(),
                    Arg.Of<ServiceInterfaces.IStreamUrlValidator>(),
                    Arg.Of<IQobuzLogger>())));

            // Authentication Orchestrator - Coordinates auth services
            container.Register<ServiceInterfaces.IAuthenticationOrchestrator, ServiceOrchestrators.AuthenticationOrchestrator>(
                Reuse.Singleton,
                Made.Of(() => new ServiceOrchestrators.AuthenticationOrchestrator(
                    Arg.Of<ServiceInterfaces.ICredentialValidator>(),
                    Arg.Of<IQobuzAuthenticationService>(),
                    Arg.Of<ServiceInterfaces.ISessionManager>(),
                    Arg.Of<ServiceInterfaces.ITokenRefresher>(),
                    Arg.Of<IQobuzLogger>())));

            Logger.Debug("Orchestrator services registered successfully");
        }

        private static void RegisterObservabilityServices(IContainer container)
        {
            Logger.Debug("Registering Observability services");

            // Metrics Collector - Prometheus metrics
            container.Register<ServiceInterfaces.IMetricsCollector, ServiceObservability.MetricsCollector>(
                Reuse.Singleton,
                Made.Of(() => new ServiceObservability.MetricsCollector(
                    Arg.Of<IQobuzLogger>())));

            // Health Check Service - Service health monitoring
            container.Register<ServiceInterfaces.IHealthCheckService, ServiceObservability.HealthCheckService>(
                Reuse.Singleton,
                Made.Of(() => new ServiceObservability.HealthCheckService(
                    Arg.Of<IQobuzLogger>(),
                    Arg.Of<ServiceInterfaces.IQobuzApiClient>(),
                    Arg.Of<IQobuzAuthenticationService>(),
                    Arg.Of<ServiceInterfaces.IMetricsCollector>())));

            Logger.Debug("Observability services registered successfully");
        }

        private static void RegisterLegacyCompatibility(IContainer container)
        {
            Logger.Debug("Registering legacy compatibility layer");

            // Map IQobuzQualityManager to the actual consolidated implementation
            // This maintains compatibility while we update all references
            container.Register<Consolidated.IQobuzQualityManager, Consolidated.QobuzQualityManager>(
                Reuse.Singleton,
                Made.Of(() => new Consolidated.QobuzQualityManager(
                    Arg.Of<API.IQobuzApiClient>(),
                    Arg.Of<IQobuzLogger>())));

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
                typeof(ServiceInterfaces.IQualityDefinitionService),
                typeof(ServiceInterfaces.IQualityFallbackStrategy),
                typeof(ServiceInterfaces.IQualityDetector),
                typeof(ServiceInterfaces.IQobuzApiClient),
                typeof(ServiceInterfaces.ISessionManager),
                typeof(ServiceInterfaces.ICredentialValidator),
                typeof(ServiceInterfaces.ITokenRefresher),
                typeof(ServiceInterfaces.IStreamUrlValidator),
                typeof(ServiceInterfaces.IStreamUrlProvider),
                typeof(ServiceInterfaces.IQualityOrchestrator),
                typeof(ServiceInterfaces.IAuthenticationOrchestrator),
                typeof(ServiceInterfaces.IMetricsCollector),
                typeof(ServiceInterfaces.IHealthCheckService)
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

}