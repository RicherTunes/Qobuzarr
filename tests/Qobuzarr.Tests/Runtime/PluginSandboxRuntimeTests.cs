using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Manifest;
using Lidarr.Plugin.Common.TestKit.Fixtures;
using Lidarr.Plugin.Common.TestKit.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Qobuzarr.Tests.Runtime;

/// <summary>
/// Loads Lidarr.Plugin.Qobuzarr.dll in an isolated AssemblyLoadContext and exercises the
/// real plugin lifecycle. This proves the built artifact works at runtime, not just that
/// the source code compiles and unit tests pass.
///
/// What this proves that unit tests cannot:
/// - The plugin DLL loads without assembly resolution failures
/// - IPlugin type is discoverable via reflection
/// - DI container builds without missing service registrations
/// - Settings provider contract works through the real plugin
/// - Dispose lifecycle completes without leaks
///
/// IMPORTANT: this test loads the **un-merged** test build at <c>bin-tests/</c>, not the
/// ILRepack-merged production DLL at <c>bin/</c>. The reason: PluginSandbox discovers IPlugin
/// implementations via <c>typeof(IPlugin).IsAssignableFrom(t)</c>, where <c>IPlugin</c> is
/// resolved against the TestKit's external <see cref="Lidarr.Plugin.Abstractions.Contracts.IPlugin"/>.
/// In the merged production DLL, <c>Lidarr.Plugin.Abstractions</c> is internalized via
/// ILRepack (deliberately — see <c>ext/Lidarr.Plugin.Common/build/PluginPackaging.targets</c>
/// — so multi-plugin co-existence doesn't trigger COR_E_INVALIDOPERATION). That makes the
/// internalized <c>IPlugin</c> a distinct type from the TestKit's external one, so
/// reflection finds zero matches and throws "Assembly does not contain a concrete IPlugin
/// implementation".
///
/// The merged DLL's behavior in a real Lidarr host is covered by <see cref="DockerE2ETests"/>,
/// which mounts <c>bin/Lidarr.Plugin.Qobuzarr.dll</c> in a container and asserts via Lidarr's
/// REST API. The un-merged build at <c>bin-tests/</c> is produced by Qobuzarr.Tests.csproj's
/// &lt;ProjectReference&gt;<c>...PluginPackagingDisable=true;OutputPath=bin-tests\</c>
/// override, exactly to make PluginSandbox-style runtime-proof tests viable.
/// </summary>
public class PluginSandboxRuntimeTests
{
    private static string FindPluginDll()
    {
        // Prefer the un-merged test build (bin-tests/) — see class doc above.
        // Fall back to merged production paths only when running outside the
        // test project's build graph (e.g. ad-hoc dotnet test in CI without
        // a fresh build), so the failure message is informative.
        string[] candidates =
        [
            Path.Combine(TestContext.RepoRoot, "bin-tests", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(TestContext.RepoRoot, "bin-tests", "Release", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(TestContext.RepoRoot, "bin-tests", "Debug", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(TestContext.RepoRoot, "bin", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(TestContext.RepoRoot, "bin", "Release", "Lidarr.Plugin.Qobuzarr.dll"),
            Path.Combine(TestContext.RepoRoot, "bin", "Debug", "Lidarr.Plugin.Qobuzarr.dll"),
        ];

        string? found = candidates.FirstOrDefault(File.Exists);
        return found ?? throw new SkipException(
            $"Plugin DLL not found. Build the test project first (which produces bin-tests/Lidarr.Plugin.Qobuzarr.dll): " +
            $"dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj -c Release. Tried: {string.Join(", ", candidates)}");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Loads_In_Isolated_ALC()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        Assert.NotNull(sandbox.Plugin);
        Assert.NotNull(sandbox.Plugin.Manifest);
        Assert.Equal("qobuzarr", sandbox.Plugin.Manifest.Id);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Describe_Returns_All_Fields()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        IReadOnlyCollection<SettingDefinition> defs = sandbox.Plugin.SettingsProvider.Describe();

        Assert.NotNull(defs);
        Assert.True(defs.Count >= 4, $"Expected at least 4 setting definitions, got {defs.Count}");

        HashSet<string> keys = [.. defs.Select(d => d.Key)];
        Assert.Contains("Email", keys);
        Assert.Contains("Password", keys);
        Assert.Contains("DownloadPath", keys);
        Assert.Contains("PreferredQuality", keys);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_GetDefaults_Returns_Dictionary()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        IReadOnlyDictionary<string, object?> defaults = sandbox.Plugin.SettingsProvider.GetDefaults();

        Assert.NotNull(defaults);
        Assert.True(defaults.Count >= 4);
        Assert.True(defaults.ContainsKey("PreferredQuality"));
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Validate_Works_Through_Merged_DLL()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // Valid settings - Qobuzarr requires Email, Password, and DownloadPath
        Dictionary<string, object?> valid = new()
        {
            ["Email"] = "test@example.com",
            ["Password"] = "secret123",
            ["DownloadPath"] = "/tmp/downloads",
            ["PreferredQuality"] = 6
        };

        PluginValidationResult result = sandbox.Plugin.SettingsProvider.Validate(valid);
        Assert.True(result.IsValid, $"Validation failed: {string.Join(", ", result.Errors)}");

        // Invalid settings - missing required fields
        Dictionary<string, object?> invalid = new()
        {
            ["Email"] = "",
            ["Password"] = "",
            ["DownloadPath"] = ""
        };

        PluginValidationResult invalidResult = sandbox.Plugin.SettingsProvider.Validate(invalid);
        Assert.False(invalidResult.IsValid);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_SettingsProvider_Apply_Accepts_Valid_Settings()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        Dictionary<string, object?> settings = new()
        {
            ["Email"] = "test@example.com",
            ["Password"] = "secret123",
            ["DownloadPath"] = "/tmp/downloads",
            ["PreferredQuality"] = 6
        };

        PluginValidationResult result = sandbox.Plugin.SettingsProvider.Apply(settings);
        Assert.True(result.IsValid, $"Apply failed: {string.Join(", ", result.Errors)}");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Dispose_Completes_Without_Error()
    {
        string dllPath = FindPluginDll();

        PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // Should not throw
        await sandbox.DisposeAsync();
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Manifest_Has_Required_Fields()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        PluginManifest manifest = sandbox.Plugin.Manifest;
        Assert.False(string.IsNullOrWhiteSpace(manifest.Id));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Name));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Version));
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Captures_Logs_During_Initialization()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // The sandbox's PluginTestContext captures logs
        var logs = sandbox.Context.LogEntries.Snapshot();
        // Plugin may or may not emit logs during init -- we just verify the
        // log pipeline is wired (no NullReferenceException from missing ILoggerFactory)
        Assert.NotNull(logs);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_Is_QobuzarrStreamingPlugin()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // The sandbox must load QobuzarrStreamingPlugin (the bridge entry point),
        // not the old QobuzarrPlugin stub. This guards against nondeterministic
        // FirstOrDefault picking the wrong IPlugin when multiple concrete types exist.
        Assert.Equal("QobuzarrStreamingPlugin", sandbox.Plugin.GetType().Name);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_CreateIndexerAsync_ReturnsIndexer_InBridgeContext()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // BridgeQobuzApiClient is registered in DI during ConfigureServices,
        // so CreateIndexerAsync returns a real QobuzIndexerAdapter instance.
        // The adapter won't have an authenticated session, but the object is non-null.
        IIndexer? indexer = await sandbox.CreateIndexerAsync();
        Assert.NotNull(indexer);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    public async Task Plugin_CreateDownloadClientAsync_ReturnsNull()
    {
        string dllPath = FindPluginDll();

        // LoaderMode=Permissive: tolerate types that fail to load in the isolated ALC
        // because they extend Lidarr host base classes (DownloadClientBase, IndexerBase)
        // whose Lidarr.Core dependency the sandbox can't resolve. The IPlugin type
        // (QobuzarrStreamingPlugin) doesn't have that problem — it only references
        // Common/Abstractions — so the surviving-types fallback finds it.
        await using PluginSandbox sandbox = await PluginSandbox.CreateAsync(
            dllPath,
            new PluginSandboxOptions { LoaderMode = SandboxLoaderMode.Permissive });

        // Download client creation is deferred to a future bridge slice.
        // Verify it returns null without throwing.
        IDownloadClient? client = await sandbox.CreateDownloadClientAsync();
        Assert.Null(client);
    }

    /// <summary>Helpers to find repo root.</summary>
    private static class TestContext
    {
        public static string RepoRoot { get; } = FindRepoRoot();

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir, "Qobuzarr.sln")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return AppContext.BaseDirectory;
        }
    }
}
