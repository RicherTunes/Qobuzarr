using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Moq;
using Xunit;
using Xunit.Abstractions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider;
using DryIoc;
using Lidarr.Plugin.Qobuzarr;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Tests plugin compatibility with different Lidarr versions and configurations
    /// Validates assembly loading, DI registration, and interface compatibility
    /// </summary>
    [Collection("PluginCompatibility")]
    [Trait("Category", "Integration")]
    [Trait("Component", "PluginSystem")]
    public class PluginCompatibilityTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _pluginPath;
        private readonly Assembly _pluginAssembly;
        private readonly List<string> _lidarrVersions;

        // Critical Lidarr versions to test against
        private static readonly string[] TargetLidarrVersions = new[]
        {
            "2.13.0.4664", // TrevTV's proven version
            "2.13.2.4685", // Current hotio pr-plugins version
            "2.13.2.4686", // Latest known working version
        };

        public PluginCompatibilityTests(ITestOutputHelper output)
        {
            _output = output;
            _pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lidarr.Plugin.Qobuzarr.dll");
            _lidarrVersions = TargetLidarrVersions.ToList();

            // Load plugin assembly
            if (File.Exists(_pluginPath))
            {
                _pluginAssembly = Assembly.LoadFrom(_pluginPath);
            }
            else
            {
                _output.WriteLine($"Warning: Plugin assembly not found at {_pluginPath}");
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public void Plugin_ShouldHaveCorrectAssemblyVersion()
        {
            // Arrange
            if (_pluginAssembly == null)
            {
                _output.WriteLine("Skipping - plugin assembly not loaded");
                return;
            }

            // Act
            var assemblyVersion = _pluginAssembly.GetName().Version;
            var assemblyName = _pluginAssembly.GetName().Name;

            // Assert
            assemblyVersion.Should().NotBeNull();
            assemblyName.Should().Be("Lidarr.Plugin.Qobuzarr");
            
            // Version should match one of our target Lidarr versions
            var versionString = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
            TargetLidarrVersions.Should().Contain(versionString, 
                $"Assembly version should match one of the target Lidarr versions");

            _output.WriteLine($"Plugin Assembly: {assemblyName} v{versionString}");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public void QobuzIndexer_ShouldImplementRequiredInterfaces()
        {
            // Arrange
            if (_pluginAssembly == null) return;

            var indexerType = _pluginAssembly.GetType("Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexer");

            // Act & Assert
            indexerType.Should().NotBeNull("QobuzIndexer type should exist");
            
            // Check base class inheritance
            var baseType = indexerType.BaseType;
            while (baseType != null && !baseType.Name.Contains("IndexerBase"))
            {
                baseType = baseType.BaseType;
            }
            baseType.Should().NotBeNull("QobuzIndexer should inherit from IndexerBase");

            // Check required interfaces
            var interfaces = indexerType.GetInterfaces();
            interfaces.Should().Contain(i => i.Name == "IIndexer", "Should implement IIndexer");

            _output.WriteLine($"QobuzIndexer implements {interfaces.Length} interfaces");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public void QobuzDownloadClient_ShouldImplementRequiredInterfaces()
        {
            // Arrange
            if (_pluginAssembly == null) return;

            var downloadClientType = _pluginAssembly.GetType("Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadClient");

            // Act & Assert
            downloadClientType.Should().NotBeNull("QobuzDownloadClient type should exist");
            
            // Check base class inheritance
            var baseType = downloadClientType.BaseType;
            while (baseType != null && !baseType.Name.Contains("DownloadClientBase"))
            {
                baseType = baseType.BaseType;
            }
            baseType.Should().NotBeNull("QobuzDownloadClient should inherit from DownloadClientBase");

            // Check required interfaces
            var interfaces = downloadClientType.GetInterfaces();
            interfaces.Should().Contain(i => i.Name == "IDownloadClient", "Should implement IDownloadClient");

            // Check constructor parameters (critical for DI)
            var constructors = downloadClientType.GetConstructors();
            constructors.Should().NotBeEmpty("Should have at least one constructor");
            
            var mainConstructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = mainConstructor.GetParameters();
            
            // Verify ILocalizationService parameter exists (critical requirement)
            parameters.Should().Contain(p => p.ParameterType.Name == "ILocalizationService",
                "Constructor must have ILocalizationService parameter for Lidarr compatibility");

            _output.WriteLine($"QobuzDownloadClient has {constructors.Length} constructors");
            _output.WriteLine($"Main constructor has {parameters.Length} parameters");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public void Plugin_ShouldRegisterWithDryIocContainer()
        {
            // Arrange
            var container = new Container();
            
            // Mock required Lidarr services
            container.Register<ILocalizationService>(Reuse.Singleton, Made.Of(() => Mock.Of<ILocalizationService>()));
            
            // Act - Simulate plugin registration
            var registrationSuccessful = true;
            try
            {
                // Register plugin services
                container.Register<QobuzIndexer>(Reuse.Transient);
                container.Register<QobuzDownloadClient>(Reuse.Transient);
                
                // Verify resolution
                var indexer = container.Resolve<QobuzIndexer>(IfUnresolved.ReturnDefault);
                var downloadClient = container.Resolve<QobuzDownloadClient>(IfUnresolved.ReturnDefault);
                
                if (indexer == null || downloadClient == null)
                {
                    registrationSuccessful = false;
                }
            }
            catch (Exception ex)
            {
                registrationSuccessful = false;
                _output.WriteLine($"DI registration failed: {ex.Message}");
            }

            // Assert
            registrationSuccessful.Should().BeTrue("Plugin services should register with DryIoc container");
            
            _output.WriteLine("Plugin successfully registered with DryIoc container");
        }

        [Fact]
        [Trait("Priority", "High")]
        public void PluginJson_ShouldBeValidAndComplete()
        {
            // Arrange
            var pluginJsonPath = Path.Combine(Path.GetDirectoryName(_pluginPath) ?? "", "plugin.json");
            
            if (!File.Exists(pluginJsonPath))
            {
                _output.WriteLine($"plugin.json not found at {pluginJsonPath}");
                return;
            }

            // Act
            var jsonContent = File.ReadAllText(pluginJsonPath);
            dynamic pluginConfig = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);

            // Assert
            ((string)pluginConfig.name).Should().Be("Qobuzarr");
            ((string)pluginConfig.author).Should().NotBeNullOrEmpty();
            ((string)pluginConfig.version).Should().NotBeNullOrEmpty();
            ((string)pluginConfig.minimumVersion).Should().BeOneOf(TargetLidarrVersions,
                "Minimum version should match target Lidarr version");
            ((string)pluginConfig.description).Should().NotBeNullOrEmpty();
            ((string)pluginConfig.assembly).Should().Be("Lidarr.Plugin.Qobuzarr.dll");

            _output.WriteLine($"Plugin: {pluginConfig.name} v{pluginConfig.version}");
            _output.WriteLine($"Minimum Lidarr: {pluginConfig.minimumVersion}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public void AssemblyReferences_ShouldNotConflict()
        {
            // Arrange
            if (_pluginAssembly == null) return;

            var referencedAssemblies = _pluginAssembly.GetReferencedAssemblies();
            var conflicts = new List<string>();

            // Act - Check for version conflicts
            foreach (var reference in referencedAssemblies)
            {
                // Check for problematic version mismatches
                if (reference.Name.StartsWith("Lidarr") || reference.Name.StartsWith("NzbDrone"))
                {
                    var version = reference.Version;
                    if (version.Major == 10 && version.Minor == 0)
                    {
                        conflicts.Add($"{reference.Name} has development version {version} instead of release version");
                    }
                }
            }

            // Assert
            conflicts.Should().BeEmpty("Plugin should not reference development versions of Lidarr assemblies");
            
            _output.WriteLine($"Plugin references {referencedAssemblies.Length} assemblies");
            if (conflicts.Any())
            {
                conflicts.ForEach(c => _output.WriteLine($"Conflict: {c}"));
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public void Plugin_ShouldLoadInIsolatedContext()
        {
            // Arrange
            var isolatedContext = new AssemblyLoadContext("PluginTestContext", isCollectible: true);
            Assembly isolatedAssembly = null;
            var loadErrors = new List<string>();

            try
            {
                // Act - Load plugin in isolated context
                if (File.Exists(_pluginPath))
                {
                    isolatedAssembly = isolatedContext.LoadFromAssemblyPath(_pluginPath);
                    
                    // Try to instantiate main types
                    var types = isolatedAssembly.GetExportedTypes();
                    foreach (var type in types.Where(t => t.Name.Contains("Qobuz")))
                    {
                        try
                        {
                            if (!type.IsAbstract && !type.IsInterface)
                            {
                                // Check if type can be instantiated
                                var constructors = type.GetConstructors();
                                if (constructors.Any(c => c.GetParameters().Length == 0))
                                {
                                    Activator.CreateInstance(type);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            loadErrors.Add($"{type.Name}: {ex.Message}");
                        }
                    }
                }

                // Assert
                isolatedAssembly.Should().NotBeNull("Plugin should load in isolated context");
                loadErrors.Should().BeEmpty("Plugin types should be instantiable");
                
                _output.WriteLine($"Successfully loaded plugin in isolated context");
                _output.WriteLine($"Found {isolatedAssembly?.GetExportedTypes().Length ?? 0} exported types");
            }
            finally
            {
                isolatedContext.Unload();
            }
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public void Plugin_ShouldHandleMultipleVersionsGracefully()
        {
            // Arrange
            var versionCompatibility = new Dictionary<string, bool>();

            // Act - Test compatibility with different versions
            foreach (var version in TargetLidarrVersions)
            {
                try
                {
                    // Simulate version check
                    var targetVersion = Version.Parse(version);
                    var pluginVersion = _pluginAssembly?.GetName().Version;
                    
                    if (pluginVersion != null)
                    {
                        // Check if versions are compatible (same major.minor)
                        var compatible = pluginVersion.Major == targetVersion.Major &&
                                       pluginVersion.Minor == targetVersion.Minor;
                        versionCompatibility[version] = compatible;
                    }
                }
                catch (Exception ex)
                {
                    versionCompatibility[version] = false;
                    _output.WriteLine($"Version {version} check failed: {ex.Message}");
                }
            }

            // Assert
            versionCompatibility.Values.Should().Contain(true, "Plugin should be compatible with at least one target version");
            
            _output.WriteLine("Version Compatibility:");
            foreach (var kvp in versionCompatibility)
            {
                _output.WriteLine($"  {kvp.Key}: {(kvp.Value ? "Compatible" : "Incompatible")}");
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public void RequiredDependencies_ShouldBePresent()
        {
            // Arrange
            var requiredDlls = new[]
            {
                "Newtonsoft.Json.dll",
                "NLog.dll",
                "FluentValidation.dll"
            };

            var pluginDirectory = Path.GetDirectoryName(_pluginPath) ?? "";
            var missingDependencies = new List<string>();

            // Act
            foreach (var dll in requiredDlls)
            {
                var dllPath = Path.Combine(pluginDirectory, dll);
                if (!File.Exists(dllPath))
                {
                    // Check if it's merged via ILRepack
                    if (_pluginAssembly != null)
                    {
                        var merged = _pluginAssembly.GetReferencedAssemblies()
                            .Any(a => a.Name == Path.GetFileNameWithoutExtension(dll));
                        if (!merged)
                        {
                            missingDependencies.Add(dll);
                        }
                    }
                    else
                    {
                        missingDependencies.Add(dll);
                    }
                }
            }

            // Assert
            missingDependencies.Should().BeEmpty("All required dependencies should be present or merged");
            
            if (missingDependencies.Any())
            {
                _output.WriteLine("Missing dependencies:");
                missingDependencies.ForEach(d => _output.WriteLine($"  - {d}"));
            }
            else
            {
                _output.WriteLine("All required dependencies present");
            }
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public void Plugin_ShouldNotHaveSecurityVulnerabilities()
        {
            // Arrange
            if (_pluginAssembly == null) return;

            var vulnerabilities = new List<string>();
            var types = _pluginAssembly.GetExportedTypes();

            // Act - Check for common security issues
            foreach (var type in types)
            {
                // Check for SQL injection vulnerabilities
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    try
                    {
                        var methodBody = method.GetMethodBody();
                        if (methodBody != null)
                        {
                            // Check for string concatenation in SQL-like contexts
                            var localVars = methodBody.LocalVariables;
                            if (localVars.Any(v => v.LocalType == typeof(string)) &&
                                method.Name.ToLower().Contains("query"))
                            {
                                vulnerabilities.Add($"Potential SQL injection in {type.Name}.{method.Name}");
                            }
                        }
                    }
                    catch
                    {
                        // Skip methods we can't analyze
                    }
                }

                // Check for hardcoded credentials
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(string))
                    {
                        var fieldName = field.Name.ToLower();
                        if (fieldName.Contains("password") || fieldName.Contains("secret") || 
                            fieldName.Contains("key") || fieldName.Contains("token"))
                        {
                            if (field.IsLiteral || (field.IsStatic && field.IsInitOnly))
                            {
                                vulnerabilities.Add($"Potential hardcoded credential in {type.Name}.{field.Name}");
                            }
                        }
                    }
                }
            }

            // Assert
            vulnerabilities.Should().BeEmpty("Plugin should not have security vulnerabilities");
            
            if (vulnerabilities.Any())
            {
                _output.WriteLine("Potential security issues:");
                vulnerabilities.ForEach(v => _output.WriteLine($"  - {v}"));
            }
            else
            {
                _output.WriteLine("No security vulnerabilities detected");
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}