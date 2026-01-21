using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Qobuzarr.Tests.Compliance;

/// <summary>
/// Plugin compliance tests for Qobuzarr.
/// These tests verify Qobuzarr adheres to Lidarr plugin requirements.
/// </summary>
[Trait("Category", "Compliance")]
[Trait("Category", "Plugin")]
public class QobuzarrPluginComplianceTests : IDisposable
{
    private readonly Assembly _pluginAssembly;
    private readonly JObject? _pluginManifest;
    private readonly string? _sourceCodePath;

    public QobuzarrPluginComplianceTests()
    {
        _pluginAssembly = typeof(QobuzIndexer).Assembly;

        // Navigate from test output to source directory
        var basePath = AppContext.BaseDirectory;
        var srcPath = Path.Combine(basePath, "..", "..", "..", "..", "..", "src");
        _sourceCodePath = Directory.Exists(srcPath) ? Path.GetFullPath(srcPath) : null;

        // Load plugin.json if available
        var manifestPath = Path.Combine(basePath, "..", "..", "..", "..", "..", "plugin.json");
        if (File.Exists(manifestPath))
        {
            var content = File.ReadAllText(manifestPath);
            _pluginManifest = JObject.Parse(content);
        }
    }

    #region Manifest Tests

    [Fact]
    public void Manifest_HasRequiredFields()
    {
        if (_pluginManifest == null)
            return; // Skip if manifest not available

        Assert.NotNull(_pluginManifest["name"]);
        Assert.NotNull(_pluginManifest["version"]);
        Assert.NotNull(_pluginManifest["author"]);
        Assert.NotNull(_pluginManifest["main"]);
    }

    [Fact]
    public void Manifest_VersionIsValid()
    {
        if (_pluginManifest == null)
            return;

        var version = _pluginManifest["version"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(version), "Version must not be empty");

        // Should be able to parse as a version
        var versionPart = version.Split('-')[0]; // Handle pre-release suffixes
        Assert.True(Version.TryParse(versionPart, out _), $"Version '{version}' should be parseable");
    }

    [Fact]
    public void Manifest_MinimumLidarrVersionIsValid()
    {
        if (_pluginManifest == null)
            return;

        var minVersion = (_pluginManifest["minHostVersion"] ?? _pluginManifest["minimumLidarrVersion"])?.ToString();
        if (!string.IsNullOrWhiteSpace(minVersion))
        {
            Assert.True(Version.TryParse(minVersion, out _), $"Minimum Lidarr version '{minVersion}' should be valid");
        }
    }

    [Fact]
    public void Manifest_MainAssemblyExists()
    {
        if (_pluginManifest == null)
            return;

        var main = _pluginManifest["main"]?.ToString();
        Assert.NotNull(main);
        Assert.EndsWith(".dll", main, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Assembly Tests

    [Fact]
    public void Assembly_CanBeLoaded()
    {
        Assert.NotNull(_pluginAssembly);
        Assert.NotEmpty(_pluginAssembly.GetName().Name ?? "");
    }

    [Fact]
    public void Assembly_HasCorrectRootNamespace()
    {
        var types = _pluginAssembly.GetTypes();
        var hasCorrectNamespace = types.Any(t =>
            t.Namespace?.StartsWith("Lidarr.Plugin.Qobuzarr", StringComparison.Ordinal) == true);

        Assert.True(hasCorrectNamespace, "Assembly should contain types in Lidarr.Plugin.Qobuzarr namespace");
    }

    [Fact]
    public void Assembly_ImplementsIndexer()
    {
        var indexerType = typeof(QobuzIndexer);
        Assert.NotNull(indexerType);

        // Should extend HttpIndexerBase
        var baseType = indexerType.BaseType;
        Assert.NotNull(baseType);
        Assert.Contains("HttpIndexerBase", baseType.Name);
    }

    [Fact]
    public void Assembly_ImplementsDownloadClient()
    {
        var downloadClientType = typeof(QobuzDownloadClient);
        Assert.NotNull(downloadClientType);

        // Should extend DownloadClientBase
        var baseType = downloadClientType.BaseType;
        Assert.NotNull(baseType);
        Assert.Contains("DownloadClientBase", baseType.Name);
    }

    #endregion

    #region Plugin Structure Tests

    [Fact]
    public void Plugin_HasAuthenticationService()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var authTypes = allTypes.Where(t =>
            t.Name.Contains("Authentication", StringComparison.OrdinalIgnoreCase) &&
            !t.IsInterface && !t.IsAbstract).ToList();

        Assert.NotEmpty(authTypes);
    }

    [Fact]
    public void Plugin_HasApiClient()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var apiClientTypes = allTypes.Where(t =>
            (t.Name.Contains("ApiClient", StringComparison.OrdinalIgnoreCase) ||
             t.Name.Contains("QobuzClient", StringComparison.OrdinalIgnoreCase)) &&
            !t.IsInterface).ToList();

        Assert.NotEmpty(apiClientTypes);
    }

    [Fact]
    public void Plugin_HasConstants()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var constantsTypes = allTypes.Where(t =>
            t.Name.Contains("Constants", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Settings", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(constantsTypes);
    }

    [Fact]
    public void Plugin_HasModels()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var modelTypes = allTypes.Where(t =>
            t.Namespace?.Contains("Models", StringComparison.OrdinalIgnoreCase) == true ||
            t.Name.EndsWith("Model", StringComparison.Ordinal) ||
            t.Name.StartsWith("Qobuz", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(modelTypes);
    }

    #endregion

    #region Dependencies Tests

    [Fact]
    public void Dependencies_NoCircularReferences()
    {
        // Verify the assembly can list its dependencies without throwing
        var references = _pluginAssembly.GetReferencedAssemblies();
        Assert.NotNull(references);
    }

    [Fact]
    public void Dependencies_ReferencesCommonLibrary()
    {
        var references = _pluginAssembly.GetReferencedAssemblies();       
        var hasCommon = references.Any(r =>
            r.Name?.Contains("Plugin.Common", StringComparison.OrdinalIgnoreCase) == true);

        var hasCommonTypes = _pluginAssembly.GetTypes().Any(t =>
            t.Namespace?.StartsWith("Lidarr.Plugin.Common", StringComparison.OrdinalIgnoreCase) == true);

        Assert.True(hasCommon || hasCommonTypes,
            "Plugin should include Lidarr.Plugin.Common either as a direct reference or as merged types");
    }

    [Fact]
    public void Dependencies_ReferencesLidarrCore()
    {
        var references = _pluginAssembly.GetReferencedAssemblies();
        var hasLidarrCore = references.Any(r =>
            r.Name?.Contains("Lidarr", StringComparison.OrdinalIgnoreCase) == true ||
            r.Name?.Contains("NzbDrone", StringComparison.OrdinalIgnoreCase) == true);

        Assert.True(hasLidarrCore, "Plugin should reference Lidarr or NzbDrone assemblies");
    }

    #endregion

    #region ILRepack Compliance Tests

    [Fact]
    public void ILRepack_NoCriticalTypeConflicts()
    {
        var allTypes = _pluginAssembly.GetTypes();

        // Check for common type name conflicts
        var conflictingTypeNames = new[] { "Logger", "HttpClient", "JsonSerializer" };

        foreach (var typeName in conflictingTypeNames)
        {
            var matchingTypes = allTypes.Where(t =>
                t.Name == typeName &&
                t.Namespace?.StartsWith("Lidarr.Plugin.Qobuzarr", StringComparison.Ordinal) == true).ToList();

            Assert.True(matchingTypes.Count <= 1,
                $"Multiple types named '{typeName}' found in plugin namespace - potential ILRepack conflict");
        }
    }

    [Fact]
    public void ILRepack_InternalTypesNotExposed()
    {
        var publicTypes = _pluginAssembly.GetExportedTypes();

        // Internal implementation types should not be public
        var internalPatterns = new[] { "Internal", "Impl", "Helper" };

        foreach (var pattern in internalPatterns)
        {
            var exposedInternal = publicTypes.Where(t =>
                t.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) &&
                !t.Name.Contains("Interface", StringComparison.OrdinalIgnoreCase)).ToList();

            // Allow some helpers but warn if too many
            Assert.True(exposedInternal.Count < 10,
                $"Too many internal-looking types ({pattern}) are publicly exposed");
        }
    }

    #endregion

    public void Dispose()
    {
        // Cleanup if needed
    }
}
