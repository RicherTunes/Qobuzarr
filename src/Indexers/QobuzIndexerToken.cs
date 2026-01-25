using System;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers.Core;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Qobuz Indexer for User ID/Token authentication.
    /// Provides a dedicated indexer entry for users who prefer token auth.
    /// All settings are the same as QobuzIndexer - only the name differs for clarity.
    /// </summary>
    public class QobuzIndexerToken : QobuzIndexer
    {
        public override string Name => "Qobuz (User ID & Token)";

        public QobuzIndexerToken(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            ISecureMLModelLoader secureModelLoader,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, authService, apiClient, secureModelLoader, logger)
        {
        }
    }
}
