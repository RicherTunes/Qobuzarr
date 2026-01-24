using Lidarr.Plugin.Common.Services.Download;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Qobuz-specific telemetry sink interface.
/// Extends IDownloadTelemetrySink for DryIoC auto-registration compatibility.
/// </summary>
public interface IQobuzDownloadTelemetrySink : IDownloadTelemetrySink
{
}
