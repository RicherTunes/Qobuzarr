using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Qobuzarr.Tests.Contracts;

/// <summary>
/// Catches version drift between sources of truth: VERSION file, plugin.json,
/// and assembly metadata.
///
/// Background: Qobuzarr's csproj already uses <c>&lt;GenerateAssemblyInfo&gt;true&lt;/GenerateAssemblyInfo&gt;</c>
/// and Directory.Build.props reads the top-level VERSION file. This contract test makes
/// sure those wirings stay healthy — sibling plugins have hit silent assembly-version
/// drift before (brainarr had a hardcoded "1.3.2.0" literal stay in AssemblyInfo.cs through
/// 1.4.x releases; tidalarr had a hardcoded "1.1.0" in TidalModule.Version). The
/// regression mode is invisible until /api/v1/system/plugins reports stale installedVersion.
/// </summary>
public class VersionContractTests
{
    [Fact]
    public void AssemblyVersion_MatchesPluginJsonVersion()
    {
        var pluginJsonPath = LocatePluginJson();
        Skip.If(pluginJsonPath is null, "plugin.json not found in baseDir or repo root");

        using var doc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var expected = doc.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expected), "plugin.json must declare a version");

        var asmVersion = typeof(Lidarr.Plugin.Qobuzarr.Integration.QobuzarrInstalledPlugin)
            .Assembly.GetName().Version?.ToString(3);

        Assert.Equal(expected, asmVersion);
    }

    [Fact]
    public void VersionFile_MatchesPluginJsonVersion()
    {
        var versionPath = LocateRepoFile("VERSION");
        var pluginJsonPath = LocatePluginJson();
        Skip.If(versionPath is null || pluginJsonPath is null,
            "VERSION or plugin.json not found — only enforced for repo-rooted runs");

        var versionFile = File.ReadAllText(versionPath!).Trim();
        using var doc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var pluginJson = doc.RootElement.GetProperty("version").GetString();

        Assert.Equal(versionFile, pluginJson);
    }

    /// <summary>
    /// Pins commonVersion drift across the three sources of truth that a Common
    /// submodule bump must update together:
    ///
    ///   1. plugin.json's "commonVersion" string (declared contract version)
    ///   2. ext/Lidarr.Plugin.Common/Directory.Build.props &lt;Version&gt; (what the
    ///      submodule actually IS at its currently-checked-out commit)
    ///   3. ext-common-sha.txt (the SHA tracker file — should match the submodule HEAD)
    ///
    /// Audit (Wave 17S, 2026-05-25) found qobuzarr declared commonVersion=1.15.0 in
    /// plugin.json but its ext-common-sha.txt pointed to a v1.11.0 SHA (38eda2c).
    /// The submodule itself was checked out at v1.15.0 (f90ecef), so the .txt file
    /// was the stale source. A future Common bump without touching the .txt would
    /// re-introduce the same divergence silently.
    ///
    /// Matches the pattern brainarr added in 441d655.
    /// </summary>
    [Fact]
    public void CommonSubmodule_Version_MatchesPluginJsonCommonVersion()
    {
        var pluginJsonPath = LocatePluginJson();
        var commonPropsPath = LocateRepoFile(Path.Combine("ext", "Lidarr.Plugin.Common", "Directory.Build.props"));
        Skip.If(pluginJsonPath is null || commonPropsPath is null,
            "plugin.json or ext/Lidarr.Plugin.Common/Directory.Build.props not found — submodule may not be initialized");

        using var pluginDoc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var pluginCommonVersion = pluginDoc.RootElement.GetProperty("commonVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(pluginCommonVersion), "plugin.json must declare commonVersion");

        // Extract <Version>X.Y.Z</Version> from props. Simple regex avoids pulling in a full XML parser.
        var props = File.ReadAllText(commonPropsPath!);
        var match = System.Text.RegularExpressions.Regex.Match(
            props,
            @"<Version>(?<v>[^<]+)</Version>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(match.Success, "ext/Lidarr.Plugin.Common/Directory.Build.props must declare <Version>");
        var submoduleVersion = match.Groups["v"].Value.Trim();

        Assert.Equal(pluginCommonVersion, submoduleVersion);
    }

    private static string? LocatePluginJson()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "plugin.json");
        if (File.Exists(candidate)) return candidate;
        return LocateRepoFile("plugin.json");
    }

    private static string? LocateRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
