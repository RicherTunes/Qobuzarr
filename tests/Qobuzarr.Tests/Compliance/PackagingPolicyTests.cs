using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Compliance
{
    /// <summary>
    /// Qobuzarr package compliance. The four contract assertions (required files,
    /// forbidden DLLs, merged-DLL size) delegate to the shared
    /// <see cref="PluginPackagingContract"/> in Common.TestKit so the rules don't
    /// drift across the four-plugin family. Qobuzarr-specific extras (reasonable
    /// size envelope, optional metadata-manifest cross-check) stay inline.
    /// </summary>
    public class PackagingPolicyTests
    {
        private readonly ITestOutputHelper _output;

        private static readonly PluginPackagePolicy Policy = PluginPackagingContract.MergedDllPolicy(
            mainAssemblyName: "Lidarr.Plugin.Qobuzarr");

        private static bool RequirePackageExists =>
            Environment.GetEnvironmentVariable("CI")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
            Environment.GetEnvironmentVariable("REQUIRE_PACKAGE_TESTS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        public PackagingPolicyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Package_Matches_Cross_Plugin_Policy()
        {
            var packagePath = FindLatestPackage();
            if (packagePath == null) { SkipIfAllowed(); return; }

            _output.WriteLine($"Testing package: {Path.GetFileName(packagePath)}");
            PluginPackagingContract.AssertZipMatchesPolicy(packagePath, Policy);
        }

        // ----- Qobuzarr-specific extras (not duplicated across plugins) -----

        [Fact]
        public void Package_Should_Have_Reasonable_Size()
        {
            var packagePath = FindLatestPackage();
            if (packagePath == null) { SkipIfAllowed(); return; }

            var fileInfo = new FileInfo(packagePath);
            var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
            _output.WriteLine($"Package size: {sizeMB:F2} MB");

            sizeMB.Should().BeLessThan(15,
                "plugin package should be under 15MB. Common bloat causes: " +
                "System.Text.Json.dll, Lidarr.*.dll host assemblies, or unused NuGet dependencies.");
            sizeMB.Should().BeGreaterThan(0.1,
                "plugin package should be at least 100KB — smaller suggests missing assemblies");
        }

        [Fact]
        public void Package_Metadata_Should_Match_Contents()
        {
            var packagePath = FindLatestPackage();
            if (packagePath == null) { SkipIfAllowed(); return; }

            var metadataPath = packagePath + ".metadata.json";
            if (!File.Exists(metadataPath))
            {
                _output.WriteLine("No metadata file found - skipping validation.");
                return;
            }

            var actualAssemblies = GetPackageAssemblies(packagePath);
            HashSet<string> metadataAssemblies;
            try
            {
                var metadataJson = File.ReadAllText(metadataPath);
                metadataAssemblies = ParseAssembliesFromMetadata(metadataJson);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to parse metadata.json: {ex.Message}");
                _output.WriteLine("Skipping metadata validation due to parse error.");
                return;
            }

            _output.WriteLine($"Metadata lists {metadataAssemblies.Count} assemblies; package contains {actualAssemblies.Count}");
            actualAssemblies.Should().BeEquivalentTo(metadataAssemblies,
                "metadata.json should accurately reflect package contents");
        }

        // ----- helpers -----

        private void SkipIfAllowed()
        {
            if (RequirePackageExists)
            {
                throw new InvalidOperationException(
                    "No package found but REQUIRE_PACKAGE_TESTS/CI is set. " +
                    "Run 'build.ps1 Release -Package' before running tests.");
            }
            _output.WriteLine("No package found - skipping test.");
        }

        private static string? FindLatestPackage()
        {
            // Honor cross-plugin PLUGIN_PACKAGE_PATH first (release.yml sets this).
            var envPath = Environment.GetEnvironmentVariable("PLUGIN_PACKAGE_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            var searchPaths = new[]
            {
                Path.Combine(GetRepoRoot(), "artifacts", "packages"),
                Path.Combine(GetRepoRoot(), "bin", "packages"),
                GetRepoRoot()
            };

            string[] globs = { "Lidarr.Plugin.Qobuzarr-*.zip", "qobuzarr-*.zip" };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath)) continue;
                foreach (var glob in globs)
                {
                    var packages = Directory.GetFiles(searchPath, glob)
                        .OrderByDescending(File.GetLastWriteTime)
                        .ToList();
                    if (packages.Any()) return packages.First();
                }
            }
            return null;
        }

        private static string GetRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Qobuzarr.csproj")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            return dir ?? AppContext.BaseDirectory;
        }

        private static HashSet<string> GetPackageAssemblies(string packagePath)
        {
            using var archive = ZipFile.OpenRead(packagePath);
            return archive.Entries
                .Where(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(e => Path.GetFileName(e.FullName))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> ParseAssembliesFromMetadata(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("assemblies", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                }
            }
            return result;
        }
    }
}
