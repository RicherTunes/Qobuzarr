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
