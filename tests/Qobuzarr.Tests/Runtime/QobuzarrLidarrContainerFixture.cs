using System.IO;
using System.Linq;
using Lidarr.Plugin.Common.TestKit.Hosting;
using Xunit;

namespace Qobuzarr.Tests.Runtime;

/// <summary>
/// Qobuzarr-specific subclass that pre-fills the per-plugin
/// <see cref="LidarrContainerOptions"/> consumed by common's lifted
/// <see cref="Lidarr.Plugin.Common.TestKit.Hosting.LidarrContainerFixture"/>.
///
/// Wave 22b — mirrors tidalarr's wave-22a port. The orchestration logic
/// (container lifecycle, healthcheck, log capture, skip-when-no-Docker) lives
/// in TestKit; this file keeps only the per-plugin constants:
///   - container name           : qobuzarr-e2e
///   - host port                : 8692 (avoids tidalarr 8690 / applemusicarr 8691)
///   - Docker image             : pinned net8 plugins-branch tag
///   - plugin mount path        : /config/plugins/RicherTunes/Qobuzarr
///   - plugin DLL filename      : Lidarr.Plugin.Qobuzarr.dll
///   - schema-entry substring   : "Qobuz"
///   - plugin DLL discovery     : bin/Lidarr.Plugin.Qobuzarr.dll (ILRepacked merged output)
///
/// IMPORTANT (Phase 6 ILRepack interaction):
/// The fixture mounts the MERGED ILRepack output (where Lidarr.Plugin.Common
/// types are internalized into Lidarr.Plugin.Qobuzarr) — this is what Lidarr
/// actually loads in production. The test project itself is built with
/// PluginPackagingDisable=true so its references resolve against the
/// standalone Common assembly, but the artifact mounted into the container
/// must be the merged DLL produced by the normal `dotnet build` of the
/// solution. Do NOT point the resolver at bin-tests/ — that's the un-merged
/// test build.
/// </summary>
public sealed class QobuzarrLidarrContainerFixture
    : Lidarr.Plugin.Common.TestKit.Hosting.LidarrContainerFixture
{
    public QobuzarrLidarrContainerFixture()
        : base(BuildOptions())
    {
    }

    private static LidarrContainerOptions BuildOptions() => new(
        DockerImage: "ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913",
        ContainerName: "qobuzarr-e2e",
        LidarrPort: 8692,
        PluginMountPath: "/config/plugins/RicherTunes/Qobuzarr",
        PluginDllFileName: "Lidarr.Plugin.Qobuzarr.dll",
        FindPluginDll: FindQobuzarrPluginDll,
        PluginEntrySubstring: "Qobuz",
        RepoRootMarkerFile: "Qobuzarr.sln");

    private static string? FindQobuzarrPluginDll(string repoRoot)
    {
        // Qobuzarr's merged ILRepack output lives at <repoRoot>/bin/ (not
        // src/<plugin>/bin/ like tidalarr) — the csproj is at the repo root.
        // Explicitly avoid bin-tests/ which holds the un-merged test build
        // (see Qobuzarr.Tests.csproj OutputPath redirect).
        string[] candidates =
        [
            Path.Combine(repoRoot, "bin", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(repoRoot, "bin", "Release", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(repoRoot, "bin", "Debug", "Lidarr.Plugin.Qobuzarr.dll"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// xUnit collection definition that lets all E2E tests share the single
/// <see cref="QobuzarrLidarrContainerFixture"/> instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LidarrContainerCollection : ICollectionFixture<QobuzarrLidarrContainerFixture>
{
    public const string Name = "LidarrContainer";
}
