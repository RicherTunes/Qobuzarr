using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.Integration;
using Xunit;

namespace Qobuzarr.Tests.Contracts;

/// <summary>
/// Catches version drift between AssemblyVersion, plugin.json, and the top-level
/// VERSION file. Actual assertions live in <see cref="PluginVersionContract"/> in
/// Common.TestKit. Qobuzarr does not ship a separate manifest.json so the
/// AssertManifestMatchesPluginJson sibling is omitted.
/// </summary>
public class VersionContractTests
{
    [Fact]
    public void AssemblyVersion_MatchesPluginJsonVersion() =>
        PluginVersionContract.AssertAssemblyVersionMatchesPluginJson(typeof(QobuzarrInstalledPlugin));

    [Fact]
    public void VersionFile_MatchesPluginJsonVersion() =>
        PluginVersionContract.AssertVersionFileMatchesPluginJson(typeof(QobuzarrInstalledPlugin));
}
