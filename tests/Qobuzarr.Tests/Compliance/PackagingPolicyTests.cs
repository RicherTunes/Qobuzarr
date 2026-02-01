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
    /// </summary>
    public class PackagingPolicyTests
    {
        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Assemblies that MUST be present in the package.
        /// These are plugin dependencies that Lidarr doesn't provide.
        /// </summary>
        private static readonly HashSet<string> RequiredAssemblies = new(StringComparer.OrdinalIgnoreCase)
        {
            "Lidarr.Plugin.Qobuzarr.dll",           // The plugin itself
            "Lidarr.Plugin.Abstractions.dll"        // Plugin contract (host does NOT provide this)
        };

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
        /// When true, missing package is a test failure instead of skip.
        /// Set via REQUIRE_PACKAGE_TESTS=true or CI=true environment variable.
        /// </summary>
        private static bool RequirePackageExists =>
            Environment.GetEnvironmentVariable("REQUIRE_PACKAGE_TESTS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
            Environment.GetEnvironmentVariable("CI")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        public PackagingPolicyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Package_Should_Contain_Required_Assemblies()
        {
            // Arrange
            var packagePath = FindLatestPackage();
            if (packagePath == null)
            {
                if (RequirePackageExists)
                {
                    throw new InvalidOperationException(
                        "No package found but REQUIRE_PACKAGE_TESTS/CI is set. " +
                        "Run 'build.ps1 Release -Package' before running tests.");
                }
                _output.WriteLine("No package found - skipping test. Run 'build.ps1 Release -Package' first.");
                return;
            }

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

        [Fact]
        public void Package_Should_Not_Contain_Forbidden_Assemblies()
        {
            // Arrange
            var packagePath = FindLatestPackage();
            if (packagePath == null)
            {
                if (RequirePackageExists)
                {
                    throw new InvalidOperationException(
                        "No package found but REQUIRE_PACKAGE_TESTS/CI is set.");
                }
                _output.WriteLine("No package found - skipping test.");
                return;
            }

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

        [Fact]
        public void Package_Should_Not_Ship_HostContract_Assemblies()
        {
            // Arrange
            var packagePath = FindLatestPackage();
            if (packagePath == null)
            {
                if (RequirePackageExists)
                {
                    throw new InvalidOperationException(
                        "No package found but REQUIRE_PACKAGE_TESTS/CI is set.");
                }
                _output.WriteLine("No package found - skipping test.");
                return;
            }

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

        [Fact]
        public void Package_Should_Have_Reasonable_Size()
        {
            // Arrange
            var packagePath = FindLatestPackage();
            if (packagePath == null)
            {
                if (RequirePackageExists)
                {
                    throw new InvalidOperationException(
                        "No package found but REQUIRE_PACKAGE_TESTS/CI is set.");
                }
                _output.WriteLine("No package found - skipping test.");
                return;
            }

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

        [Fact]
        public void Package_Metadata_Should_Match_Contents()
        {
            // Arrange
            var packagePath = FindLatestPackage();
            if (packagePath == null)
            {
                if (RequirePackageExists)
                {
                    throw new InvalidOperationException(
                        "No package found but REQUIRE_PACKAGE_TESTS/CI is set.");
                }
                _output.WriteLine("No package found - skipping test.");
                return;
            }

            var metadataPath = packagePath + ".metadata.json";
            if (!File.Exists(metadataPath))
            {
                _output.WriteLine("No metadata file found - skipping validation.");
                return;
            }

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
            // Look for packages in standard locations
            var searchPaths = new[]
            {
                Path.Combine(GetRepoRoot(), "artifacts", "packages"),
                Path.Combine(GetRepoRoot(), "bin", "packages"),
                GetRepoRoot()
            };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                var packages = Directory.GetFiles(searchPath, "qobuzarr-*.zip")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();

                if (packages.Any())
                    return packages.First();
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
