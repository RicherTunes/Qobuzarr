using Lidarr.Plugin.Common.Services.Registration;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    // Minimal module to satisfy StreamingPlugin<TModule,TSettings>
    public sealed class QobuzModule : StreamingPluginModule
    {
        public override string ServiceName => "Qobuz";
        public override string Description => "Qobuz indexer + download client";
        public override string Author => "RicherTunes";

        protected override void RegisterCoreServices()
        {
            // Adapters are created by the plugin overrides; DI remains minimal for now
        }
    }
}

