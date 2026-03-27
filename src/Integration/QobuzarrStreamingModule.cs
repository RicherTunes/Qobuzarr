using System.Collections.Generic;
using Lidarr.Plugin.Common.Services.Registration;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Module descriptor for the Qobuzarr streaming bridge plugin.
/// Provides metadata and service registration hooks used by the <see cref="StreamingPlugin{TModule, TSettings}"/> base.
/// </summary>
public sealed class QobuzarrStreamingModule : StreamingPluginModule
{
    /// <inheritdoc />
    public override string ServiceName => "Qobuz";

    /// <inheritdoc />
    public override string Description => "Qobuz streaming service integration for Lidarr (lossless / hi-res).";

    /// <inheritdoc />
    public override string Author => "RicherTunes";

    /// <inheritdoc />
    protected override bool HasDownloadClient() => false; // Download client deferred to a later slice

    /// <inheritdoc />
    protected override List<string> GetRequiredSettings()
    {
        return ["Email", "Password", "DownloadPath"];
    }
}
