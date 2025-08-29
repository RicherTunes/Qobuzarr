using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// DryIoc removed - Lidarr handles DI registration automatically
using NzbDrone.Core.Qualities;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using API = Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Consolidated = Lidarr.Plugin.Qobuzarr.Services.Consolidated;
// Temporarily disabled namespaces - see *.disabled folders
// using CoreApi = Lidarr.Plugin.Qobuzarr.Services.Core.Api;
// using CoreAuth = Lidarr.Plugin.Qobuzarr.Services.Core.Auth;
// using CoreQuality = Lidarr.Plugin.Qobuzarr.Services.Core.Quality;
// using CoreStreaming = Lidarr.Plugin.Qobuzarr.Services.Core.Streaming;
// using ServiceObservability = Lidarr.Plugin.Qobuzarr.Services.Observability;
// using ServiceOrchestrators = Lidarr.Plugin.Qobuzarr.Services.Orchestrators;
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
    /// Service registration helpers for Lidarr auto-discovery.
    /// Lidarr automatically registers services implementing interfaces.
    /// This class provides documentation and validation helpers only.
    /// </summary>
    public static class ServiceRegistration
    {
#if !CLI_BUILD
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Documents the services that Lidarr will auto-register.
        /// NOTE: Lidarr's DryIoC container automatically discovers and registers:
        /// - Classes implementing IIndexer (QobuzIndexer)
        /// - Classes implementing IDownloadClient (QobuzDownloadClient)  
        /// - Services implementing interfaces as singletons
        /// Manual registration is NOT required or supported for plugins.
        /// </summary>
        public static void DocumentAutoRegisteredServices()
        {
            Logger.Info("Qobuzarr plugin services will be auto-registered by Lidarr:");
            Logger.Info("  - QobuzIndexer (implements IIndexer)");
            Logger.Info("  - QobuzDownloadClient (implements IDownloadClient)");
            Logger.Info("  - All services implementing interfaces will be registered as singletons");
            
            // The following services are auto-registered by Lidarr through interface discovery:
            // - API.QobuzApiClient : IQobuzApiClient
            // - Authentication.QobuzAuthenticationService : IQobuzAuthenticationService
            // - Authentication.SessionManager : ISessionManager
            // - API.Signing.QobuzRequestSigner : IQobuzRequestSigner
            // - API.Caching.QobuzResponseCache : IQobuzResponseCache
            // - Services.UnifiedQualityService : IQualityService
        }

        /// <summary>
        /// Lists the expected auto-registered services for documentation.
        /// These services follow Lidarr's convention: classes implementing interfaces
        /// are automatically discovered and registered as singletons.
        /// </summary>
        public static List<(Type Interface, Type Implementation)> GetAutoRegisteredServices()
        {
            return new List<(Type, Type)>
            {
                // API client components
                (typeof(API.Http.IQobuzHttpClient), typeof(API.Http.QobuzHttpClient)),
                // Authentication services
                (typeof(Authentication.IQobuzAuthenticationService), typeof(Authentication.QobuzAuthenticationService)),
                // Session management
                (typeof(Interfaces.ISessionManager), typeof(Authentication.SessionManager)),
                // Request signing
                (typeof(API.Signing.IQobuzRequestSigner), typeof(API.Signing.QobuzRequestSigner)),
                // Response caching
                (typeof(API.Caching.IQobuzResponseCache), typeof(API.Caching.QobuzResponseCache)),
                // Main API client
                (typeof(API.IQobuzApiClient), typeof(API.QobuzApiClient)),
                // Quality service
                (typeof(IQualityService), typeof(UnifiedQualityService))
            };
        }

        /// <summary>
        /// Logs information about the services that will be auto-registered.
        /// This is for informational purposes only - actual registration
        /// is handled by Lidarr's DryIoC container automatically.
        /// </summary>
        public static void LogServiceDiscovery()
        {
            Logger.Debug("Qobuzarr plugin service discovery:");
            
            foreach (var (interfaceType, implementationType) in GetAutoRegisteredServices())
            {
                Logger.Debug("  - {0} -> {1}", interfaceType.Name, implementationType.Name);
            }
            
            Logger.Info("All services will be auto-discovered and registered by Lidarr");
        }
#endif
    }

}