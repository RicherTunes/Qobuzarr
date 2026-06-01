using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Compliance
{
    /// <summary>
    /// Validates that plugin packages contain the correct assemblies.
    /// Prevents regressions where host assemblies accidentally get bundled
    /// or required dependencies are missing.
    ///
    /// These tests require a built plugin package. When none is present
    /// (PLUGIN_PACKAGE_PATH unset AND no built package found) they SKIP
    /// gracefully via <see cref="Skip"/> rather than fail — so they're inert
    /// in broad CI sweeps (e.g. the nightly full-suite, which doesn't package)
    /// yet still execute wherever a package exists (release.yml,
    /// build.ps1 -Package, local runs). Packaging is independently gated by
    /// the dedicated packaging-gates.yml (Common reusable workflow), so this
    /// skip loses no coverage. Build a package with
    /// <c>./build.ps1 -Package</c> or set <c>PLUGIN_PACKAGE_PATH</c> to run
    /// them here.
    /// </summary>
    public class PackagingPolicyTests
    {
        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Assemblies that MUST be present in the package.
        ///
        /// Previously this list also contained Lidarr.Plugin.Abstractions.dll.
        /// As of May 2026 the Abstractions assembly is merged + internalized into
        /// Lidarr.Plugin.Qobuzarr.dll via ILRepack (see
        /// ext/Lidarr.Plugin.Common/build/PluginPackaging.targets). Shipping it as a
        /// sidecar reintroduces the COR_E_INVALIDOPERATION cross-ALC conflict that
        /// the merge was meant to eliminate, so it now belongs in the forbidden list.
        /// </summary>
        private static readonly HashSet<string> RequiredAssemblies = new(StringComparer.OrdinalIgnoreCase)
        {
            "Lidarr.Plugin.Qobuzarr.dll",           // The plugin itself (merged via ILRepack)
        };

        /// <summary>
        /// Sub-threshold DLL size means ILRepack's RepackPlugin target didn't run —
        /// Common + Abstractions weren't internalized, so the runtime will fail with
        /// "Could not load file or assembly Lidarr.Plugin.Common / Abstractions" because
        /// the forbidden-list correctly omitted the sidecars but the merge produced nothing.
        /// </summary>
        private const long MergedDllMinimumBytes = 2_000_000;

        /// <summary>
        /// Assemblies that MUST NOT be present in the package.
        /// These are provided by the Lidarr host and would cause conflicts.
        /// </summary>
        private static readonly HashSet<string> ForbiddenAssemblies = new(StringComparer.OrdinalIgnoreCase)
        {
            // Host-provided contract assemblies (shipping causes type identity conflicts)
            "FluentValidation.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Caching.Abstractions.dll",
            "Microsoft.Extensions.Caching.Memory.dll",
            "Microsoft.Extensions.Options.dll",
            "Microsoft.Extensions.Primitives.dll",

            // Plugin abstractions — merged + internalized by ILRepack, MUST NOT ship as sidecars.
            // Were in RequiredAssemblies before the May 2026 merge architecture switch.
            "Lidarr.Plugin.Abstractions.dll",
            "Lidarr.Plugin.Common.dll",

            // Lidarr host assemblies
            "Lidarr.Core.dll",
            "Lidarr.Common.dll",
            "Lidarr.Host.dll",
            "Lidarr.Api.V1.dll",
            "Lidarr.Http.dll",
            
            // .NET runtime assemblies (provided by host)
            "System.Text.Json.dll",
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Net.Http.dll",
            "System.IO.Compression.dll",
            "System.Threading.dll",
            
            // NLog (provided by host)
            "NLog.dll",
            
            // Newtonsoft.Json (provided by host)
            "Newtonsoft.Json.dll",
            
            // SignalR (provided by host)
            "Microsoft.AspNetCore.SignalR.dll",
            "Lidarr.SignalR.dll"
        };

        /// <summary>
        /// Assemblies that MAY be present (not required, not forbidden).
        /// Listed for documentation - test ignores these.
        /// </summary>
        private static readonly HashSet<string> OptionalAssemblies = new(StringComparer.OrdinalIgnoreCase)
        {
            "Lidarr.Plugin.Common.dll"  // May be ILRepack'd into main assembly
        };

        /// <summary>
        /// Skip reason when no package is available, otherwise <c>null</c>.
        /// Resolved once per test from <see cref="FindLatestPackage"/>; a non-null
        /// value drives <see cref="Skip.If(bool, string)"/> so the test is reported
        /// as Skipped (not Failed) when no package has been built.
        /// </summary>
        private static string? NoPackageSkipReason =>
            FindLatestPackage() == null
                ? "no plugin package present; run ./build.ps1 -Package or set PLUGIN_PACKAGE_PATH"
                : null;

        public PackagingPolicyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableFact]
        public void Package_Should_Contain_Required_Assemblies()
        {
            // Arrange
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            _output.WriteLine($"Testing package: {Path.GetFileName(packagePath)}");
            var assemblies = GetPackageAssemblies(packagePath);
            _output.WriteLine($"Package contains {assemblies.Count} assemblies:");
            foreach (var asm in assemblies.OrderBy(a => a))
            {
                _output.WriteLine($"  - {asm}");
            }

            // Act & Assert
            foreach (var required in RequiredAssemblies)
            {
                assemblies.Should().Contain(required,
                    $"package must contain required assembly {required}");
            }
        }

        [SkippableFact]
        public void Plugin_Dll_Should_Be_Merged_Size()
        {
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            using var archive = ZipFile.OpenRead(packagePath);
            var entry = archive.Entries.FirstOrDefault(e =>
                Path.GetFileName(e.FullName).Equals("Lidarr.Plugin.Qobuzarr.dll", StringComparison.OrdinalIgnoreCase));

            entry.Should().NotBeNull("Lidarr.Plugin.Qobuzarr.dll must be in the package");
            entry!.Length.Should().BeGreaterOrEqualTo(
                MergedDllMinimumBytes,
                "merged DLL should be at least 2MB (includes internalized Common + Abstractions). " +
                "A smaller DLL means ILRepack didn't run and runtime will fail with " +
                "'Could not load file or assembly Lidarr.Plugin.Common / Abstractions'");
        }

        [SkippableFact]
        public void Package_Should_Not_Contain_Forbidden_Assemblies()
        {
            // Arrange
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            _output.WriteLine($"Testing package: {Path.GetFileName(packagePath)}");
            var assemblies = GetPackageAssemblies(packagePath);

            // Act & Assert
            var foundForbidden = assemblies.Intersect(ForbiddenAssemblies, StringComparer.OrdinalIgnoreCase).ToList();

            if (foundForbidden.Any())
            {
                _output.WriteLine("FORBIDDEN assemblies found in package:");
                foreach (var forbidden in foundForbidden)
                {
                    _output.WriteLine($"  ❌ {forbidden}");
                }
            }

            foundForbidden.Should().BeEmpty(
                "package should not contain host assemblies that would conflict with Lidarr");
        }

        [SkippableFact]
        public void Package_Should_Not_Ship_HostContract_Assemblies()
        {
            // Arrange
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            var assemblies = GetPackageAssemblies(packagePath);
            var hostContractAssemblies = new[]
            {
                "FluentValidation.dll",
                "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                "Microsoft.Extensions.Logging.Abstractions.dll"
            };

            foreach (var forbidden in hostContractAssemblies)
            {
                assemblies.Should().NotContain(forbidden,
                    $"{forbidden} is host-provided and must not be shipped with the plugin to avoid type identity conflicts");
            }
        }

        [SkippableFact]
        public void Package_Should_Have_Reasonable_Size()
        {
            // Arrange
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            var fileInfo = new FileInfo(packagePath);
            var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
            _output.WriteLine($"Package size: {sizeMB:F2} MB");

            // Assert - package should be reasonable size (< 15 MB for a plugin)
            // Common causes of bloat: System.Text.Json, host DLLs, unused NuGet deps
            sizeMB.Should().BeLessThan(15,
                "plugin package should be under 15MB. Common bloat causes: " +
                "System.Text.Json.dll, Lidarr.*.dll host assemblies, or unused NuGet dependencies. " +
                "Check the forbidden assemblies list if this fails.");

            // And not too small (sanity check)
            sizeMB.Should().BeGreaterThan(0.1,
                "plugin package should be at least 100KB - smaller size suggests missing assemblies");
        }

        [SkippableFact]
        public void Package_Metadata_Should_Match_Contents()
        {
            // Arrange
            Skip.If(NoPackageSkipReason is not null, NoPackageSkipReason);
            var packagePath = FindLatestPackage()!;

            var metadataPath = packagePath + ".metadata.json";
            Skip.IfNot(File.Exists(metadataPath), "no .metadata.json sidecar next to the package");

            // Act
            var actualAssemblies = GetPackageAssemblies(packagePath);

            HashSet<string> metadataAssemblies;
            try
            {
                var metadataJson = File.ReadAllText(metadataPath);
                // Simple JSON parsing - treat as untrusted input
                metadataAssemblies = ParseAssembliesFromMetadata(metadataJson);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to parse metadata.json: {ex.Message}");
                _output.WriteLine("Skipping metadata validation due to parse error.");
                return;
            }

            _output.WriteLine($"Metadata lists {metadataAssemblies.Count} assemblies");
            _output.WriteLine($"Package contains {actualAssemblies.Count} assemblies");

            // Assert
            actualAssemblies.Should().BeEquivalentTo(metadataAssemblies,
                "metadata.json should accurately reflect package contents");
        }

        #region Helper Methods

        private static string? FindLatestPackage()
        {
            // Honor PLUGIN_PACKAGE_PATH first — the CI workflow sets this so tests run
            // against the exact package that will be uploaded to the release.
            var envPath = Environment.GetEnvironmentVariable("PLUGIN_PACKAGE_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            // Look for packages in standard locations. Match both the legacy
            // `qobuzarr-*.zip` (slug-cased) and the current `Lidarr.Plugin.Qobuzarr-*.zip`
            // (assembly-named) glob — release.yml uses the latter.
            var searchPaths = new[]
            {
                Path.Combine(GetRepoRoot(), "artifacts", "packages"),
                Path.Combine(GetRepoRoot(), "bin", "packages"),
                GetRepoRoot()
            };

            string[] globs = { "Lidarr.Plugin.Qobuzarr-*.zip", "qobuzarr-*.zip" };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                foreach (var glob in globs)
                {
                    var packages = Directory.GetFiles(searchPath, glob)
                        .OrderByDescending(File.GetLastWriteTime)
                        .ToList();

                    if (packages.Any())
                        return packages.First();
                }
            }

            return null;
        }

        private static string GetRepoRoot()
        {
            // Navigate up from test assembly location to find repo root
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Qobuzarr.csproj")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            return dir ?? AppContext.BaseDirectory;
        }

        private static HashSet<string> GetPackageAssemblies(string packagePath)
        {
            var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    assemblies.Add(entry.Name);
                }
            }

            return assemblies;
        }

        private static HashSet<string> ParseAssembliesFromMetadata(string json)
        {
            // Simple parsing - find "assemblies": [...] and extract strings
            var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var startMarker = "\"assemblies\"";
            var startIndex = json.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0) return assemblies;

            var arrayStart = json.IndexOf('[', startIndex);
            var arrayEnd = json.IndexOf(']', arrayStart);
            if (arrayStart < 0 || arrayEnd < 0) return assemblies;

            var arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            var parts = arrayContent.Split(',');

            foreach (var part in parts)
            {
                var trimmed = part.Trim().Trim('"', ' ', '\r', '\n');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    assemblies.Add(trimmed);
                }
            }

            return assemblies;
        }

        #endregion
    }
}
