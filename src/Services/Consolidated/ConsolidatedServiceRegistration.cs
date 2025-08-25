using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Service registration helper for consolidated services.
    /// All legacy services have been successfully migrated to the consolidated architecture.
    /// </summary>
    /// <remarks>
    /// Lidarr automatically registers classes that implement interfaces in the plugin assembly.
    /// The consolidated QobuzQualityManager is now the single source of truth for all quality operations.
    /// </remarks>
    public static class ConsolidatedServiceRegistration
    {
        /// <summary>
        /// Creates the consolidated QobuzQualityManager instance.
        /// This will be automatically registered as a singleton by Lidarr's DI container.
        /// </summary>
        public static IQobuzQualityManager CreateQualityManager(
            IQobuzApiClient apiClient,
            IQobuzLogger logger)
        {
            return new QobuzQualityManager(apiClient, logger);
        }
    }
}