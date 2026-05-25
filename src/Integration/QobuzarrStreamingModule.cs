using System.Collections.Generic;
using System.Threading;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Registration;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Module descriptor for the Qobuzarr streaming bridge plugin.
/// Provides metadata and service registration hooks used by the <see cref="StreamingPlugin{TModule, TSettings}"/> base.
/// </summary>
public sealed class QobuzarrStreamingModule : StreamingPluginModule
{
    // CAS-guarded shutdown registration so reload cycles don't register the
    // SharedSystemHttpClient teardown delegate twice. Mirrors tidalarr +
    // applemusicarr; reset in Dispose() so a subsequent plugin instance
    // re-registers cleanly when Lidarr reloads the plugin assembly.
    private static int _hooksRegistered;

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

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Registers the SharedSystemHttpClient socket-pool teardown via Common's
    /// <see cref="PluginLifecycle.RegisterShutdown"/> instead of calling
    /// <c>QobuzarrModule.Dispose()</c> directly from <see cref="Dispose"/>. The
    /// canonical ecosystem pattern (apple <c>AppleMusicarrModule.RegisterCustomServices</c>,
    /// tidal <c>TidalModule.RegisterCustomServices</c>) is registration-then-shutdown:
    /// resources register their teardown delegate at plugin-load time; the host's
    /// plugin-unload path invokes them via <see cref="PluginLifecycle.Shutdown"/>.
    /// </para>
    /// <para>
    /// The behavioral guarantee is identical to the prior direct-call pattern —
    /// <c>SharedSystemHttpClient.Dispose</c> still runs on plugin unload — but the
    /// migration brings qobuz into uniform parity with the other three plugins
    /// (closes parity-matrix axis #4) and lets future static-cache teardowns plug
    /// into the same shutdown chain via additional <c>RegisterShutdown</c> calls.
    /// </para>
    /// </remarks>
    protected override void RegisterCustomServices()
    {
        base.RegisterCustomServices();

        if (Interlocked.CompareExchange(ref _hooksRegistered, 1, 0) != 0)
        {
            return;
        }

        PluginLifecycle.RegisterShutdown(
            "QobuzarrSharedSystemHttpClient",
            static () =>
            {
                try
                {
                    QobuzarrModule.Dispose();
                }
                catch
                {
                    // Teardown errors are not actionable from plugin unload; swallow.
                }
            });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposes process-static plugin resources whose lifetimes are tied to the
    /// plugin's AssemblyLoadContext. <see cref="StreamingPluginModule.Dispose"/>
    /// disposes the host gate timer; <see cref="PluginLifecycle.Shutdown"/>
    /// invokes every <see cref="PluginLifecycle.RegisterShutdown"/>-registered
    /// delegate (including the <c>SharedSystemHttpClient</c> socket pool teardown
    /// registered in <see cref="RegisterCustomServices"/>).
    /// </remarks>
    public override void Dispose()
    {
        base.Dispose();
        PluginLifecycle.Shutdown();
        // Reset the hook-registration guard so a subsequent module instance can re-register.
        Interlocked.Exchange(ref _hooksRegistered, 0);
    }
}
