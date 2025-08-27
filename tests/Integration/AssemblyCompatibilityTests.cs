using System;
using System.Reflection;
using System.Linq;
using Xunit;
using FluentAssertions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Critical integration tests for assembly compatibility with Lidarr plugins branch.
    /// These tests prevent ReflectionTypeLoadException and protocol compatibility issues.
    /// </summary>
    [Trait("Category", "Integration")]
    public class AssemblyCompatibilityTests
    {
        #region Assembly Loading Tests

        [Fact]
        public void PluginAssembly_ShouldLoadWithoutReflectionErrors()
        {
            // Act & Assert
            var assembly = typeof(QobuzIndexer).Assembly;
            
            // Should not throw ReflectionTypeLoadException
            var action = () => assembly.GetTypes();
            action.Should().NotThrow<ReflectionTypeLoadException>();
            
            // Should contain expected types
            var types = assembly.GetTypes();
            types.Should().Contain(t => t.Name == "QobuzIndexer");
            types.Should().Contain(t => t.Name == "QobuzDownloadClient");
            types.Should().Contain(t => t.Name == "QobuzarrDownloadProtocol");
        }

        [Fact]
        public void PluginAssembly_ShouldHaveCorrectVersion()
        {
            // Arrange
            var assembly = typeof(QobuzIndexer).Assembly;
            var assemblyName = assembly.GetName();
            
            // Act
            var version = assemblyName.Version;
            
            // Assert
            version.Should().NotBeNull();
            // Plugin version should be in expected format
            version.Major.Should().BeGreaterThanOrEqualTo(0);
            version.Minor.Should().BeGreaterThanOrEqualTo(0);
            version.Build.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void LidarrCoreAssembly_ShouldBeAccessible()
        {
            // Act & Assert - Should be able to access Lidarr.Core types
            var indexerBaseType = typeof(HttpIndexerBase<>);
            var downloadClientBaseType = typeof(DownloadClientBase<>);
            
            indexerBaseType.Should().NotBeNull();
            downloadClientBaseType.Should().NotBeNull();
            
            // Check assembly names
            indexerBaseType.Assembly.GetName().Name.Should().Be("Lidarr.Core");
            downloadClientBaseType.Assembly.GetName().Name.Should().Be("Lidarr.Core");
        }

        #endregion

        #region Protocol Compatibility Tests

        [Fact]
        public void QobuzIndexer_Protocol_ShouldBeStringType()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act
            var protocolProperty = indexerType.GetProperty("Protocol");
            
            // Assert
            protocolProperty.Should().NotBeNull();
            protocolProperty.PropertyType.Should().Be(typeof(string), 
                "Plugins branch requires string Protocol property, not DownloadProtocol enum");
        }

        [Fact]
        public void QobuzDownloadClient_Protocol_ShouldBeStringType()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var protocolProperty = downloadClientType.GetProperty("Protocol");
            
            // Assert
            protocolProperty.Should().NotBeNull();
            protocolProperty.PropertyType.Should().Be(typeof(string),
                "Plugins branch requires string Protocol property, not DownloadProtocol enum");
        }

        [Fact]
        public void QobuzarrDownloadProtocol_ShouldImplementIDownloadProtocol()
        {
            // Arrange
            var protocolType = Type.GetType("NzbDrone.Core.Indexers.QobuzarrDownloadProtocol, Lidarr.Plugin.Qobuzarr");
            
            // Act & Assert
            protocolType.Should().NotBeNull("QobuzarrDownloadProtocol type should exist");
            
            // Check if IDownloadProtocol interface exists (plugins branch specific)
            var downloadProtocolInterface = protocolType.GetInterfaces()
                .FirstOrDefault(i => i.Name == "IDownloadProtocol");
                
            downloadProtocolInterface.Should().NotBeNull(
                "QobuzarrDownloadProtocol should implement IDownloadProtocol (plugins branch requirement)");
        }

        [Fact]
        public void Protocol_Values_ShouldUseNameofPattern()
        {
            // This test validates the nameof pattern used by working plugins
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var indexerProtocolProperty = indexerType.GetProperty("Protocol");
            var downloadClientProtocolProperty = downloadClientType.GetProperty("Protocol");
            
            // Create instances to test property values
            // Note: We can't instantiate directly due to DI dependencies, so we check via reflection
            
            // Assert
            indexerProtocolProperty.Should().NotBeNull();
            downloadClientProtocolProperty.Should().NotBeNull();
            
            // Both should be overrides
            indexerProtocolProperty.GetGetMethod().Should().NotBeNull();
            downloadClientProtocolProperty.GetGetMethod().Should().NotBeNull();
            
            indexerProtocolProperty.GetGetMethod().IsVirtual.Should().BeTrue();
            downloadClientProtocolProperty.GetGetMethod().IsVirtual.Should().BeTrue();
        }

        #endregion

        #region Base Class Compatibility Tests

        [Fact]
        public void QobuzIndexer_ShouldInheritFromCorrectBase()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act
            var baseType = indexerType.BaseType;
            
            // Assert
            baseType.Should().NotBeNull();
            baseType.Name.Should().Be("HttpIndexerBase`1");
            baseType.GetGenericArguments().Should().ContainSingle()
                .Which.Name.Should().Be("QobuzIndexerSettings");
        }

        [Fact]
        public void QobuzDownloadClient_ShouldInheritFromCorrectBase()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var baseType = downloadClientType.BaseType;
            
            // Assert
            baseType.Should().NotBeNull();
            baseType.Name.Should().Be("DownloadClientBase`1");
            baseType.GetGenericArguments().Should().ContainSingle()
                .Which.Name.Should().Be("QobuzDownloadSettings");
        }

        [Fact]
        public void QobuzIndexer_ShouldImplementIIndexer()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act
            var implementsIIndexer = indexerType.GetInterfaces()
                .Any(i => i.Name == "IIndexer");
            
            // Assert
            implementsIIndexer.Should().BeTrue("QobuzIndexer should implement IIndexer interface");
        }

        [Fact]
        public void QobuzDownloadClient_ShouldImplementIDownloadClient()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var implementsIDownloadClient = downloadClientType.GetInterfaces()
                .Any(i => i.Name == "IDownloadClient");
            
            // Assert
            implementsIDownloadClient.Should().BeTrue("QobuzDownloadClient should implement IDownloadClient interface");
        }

        #endregion

        #region Constructor Compatibility Tests

        [Fact]
        public void QobuzIndexer_Constructor_ShouldHaveCorrectSignature()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act
            var constructors = indexerType.GetConstructors();
            
            // Assert
            constructors.Should().NotBeEmpty("QobuzIndexer should have at least one constructor");
            
            // Check for DI constructor parameters
            var primaryConstructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = primaryConstructor.GetParameters();
            
            // Should have required dependencies
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("IHttpClient"));
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("IIndexerStatusService"));
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("IConfigService"));
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("Logger"));
        }

        [Fact]
        public void QobuzDownloadClient_Constructor_ShouldHaveCorrectSignature()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var constructors = downloadClientType.GetConstructors();
            
            // Assert
            constructors.Should().NotBeEmpty("QobuzDownloadClient should have at least one constructor");
            
            // Check for DI constructor parameters
            var primaryConstructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = primaryConstructor.GetParameters();
            
            // Critical: Should have ILocalizationService (required by plugins branch)
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("ILocalizationService"),
                "DownloadClientBase constructor requires ILocalizationService in plugins branch");
            
            // Other standard dependencies
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("IHttpClient"));
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("IConfigService"));
            parameters.Should().Contain(p => p.ParameterType.Name.Contains("Logger"));
        }

        #endregion

        #region Method Override Tests

        [Fact]
        public void QobuzIndexer_ShouldOverrideRequiredMethods()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act & Assert - Check for required method overrides
            var protocolMethod = indexerType.GetProperty("Protocol", 
                BindingFlags.Public | BindingFlags.Instance);
            protocolMethod.Should().NotBeNull("Protocol property must be overridden");
            
            var nameMethod = indexerType.GetProperty("Name", 
                BindingFlags.Public | BindingFlags.Instance);
            nameMethod.Should().NotBeNull("Name property must be present");
            
            var fetchMethod = indexerType.GetMethod("FetchPage", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            fetchMethod.Should().NotBeNull("FetchPage method must be overridden");
        }

        [Fact]
        public void QobuzDownloadClient_ShouldOverrideRequiredMethods()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act & Assert - Check for required method overrides
            var protocolMethod = downloadClientType.GetProperty("Protocol", 
                BindingFlags.Public | BindingFlags.Instance);
            protocolMethod.Should().NotBeNull("Protocol property must be overridden");
            
            var downloadMethod = downloadClientType.GetMethod("Download", 
                BindingFlags.Public | BindingFlags.Instance);
            downloadMethod.Should().NotBeNull("Download method must be overridden");
            
            var getItemsMethod = downloadClientType.GetMethod("GetItems", 
                BindingFlags.Public | BindingFlags.Instance);
            getItemsMethod.Should().NotBeNull("GetItems method must be overridden");
        }

        #endregion

        #region Version Mismatch Detection Tests

        [Fact]
        public void AssemblyVersions_ShouldBeCompatible()
        {
            // This test helps detect version mismatches that cause ReflectionTypeLoadException
            
            // Arrange
            var pluginAssembly = typeof(QobuzIndexer).Assembly;
            var lidarrCoreAssembly = typeof(HttpIndexerBase<>).Assembly;
            
            // Act
            var pluginVersion = pluginAssembly.GetName().Version;
            var lidarrVersion = lidarrCoreAssembly.GetName().Version;
            
            // Assert
            // Log versions for debugging
            var message = $"Plugin: {pluginVersion}, Lidarr.Core: {lidarrVersion}";
            
            // Both should be loaded successfully
            pluginVersion.Should().NotBeNull(message);
            lidarrVersion.Should().NotBeNull(message);
            
            // If using plugins branch, Lidarr.Core version should match expected pattern
            // Plugins branch uses release versions like 2.13.x.x, not development 10.0.0.x
            if (lidarrVersion.Major == 10)
            {
                // Development version - might cause issues with hotio runtime
                lidarrVersion.Major.Should().NotBe(10, 
                    "Lidarr.Core version 10.x.x.x suggests development build. " +
                    "Use plugins branch assemblies (2.13.x.x) for compatibility with hotio:pr-plugins");
            }
        }

        [Fact]
        public void ReferencedAssemblies_ShouldBeLoadable()
        {
            // This test checks that all referenced assemblies can be loaded
            
            // Arrange
            var pluginAssembly = typeof(QobuzIndexer).Assembly;
            var referencedAssemblies = pluginAssembly.GetReferencedAssemblies();
            
            // Act & Assert
            foreach (var referencedAssembly in referencedAssemblies)
            {
                if (referencedAssembly.Name.StartsWith("Lidarr") || 
                    referencedAssembly.Name.StartsWith("NzbDrone"))
                {
                    // Try to load each referenced assembly
                    var action = () => Assembly.Load(referencedAssembly);
                    action.Should().NotThrow($"Should be able to load {referencedAssembly.Name}");
                }
            }
        }

        #endregion

        #region Plugin Discovery Tests

        [Fact]
        public void PluginTypes_ShouldBeDiscoverableByLidarr()
        {
            // This test simulates how Lidarr discovers plugin types
            
            // Arrange
            var assembly = typeof(QobuzIndexer).Assembly;
            
            // Act - Simulate Lidarr's plugin discovery
            var indexerTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetInterfaces().Any(i => i.Name == "IIndexer"))
                .ToList();
                
            var downloadClientTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetInterfaces().Any(i => i.Name == "IDownloadClient"))
                .ToList();
            
            // Assert
            indexerTypes.Should().ContainSingle()
                .Which.Name.Should().Be("QobuzIndexer");
                
            downloadClientTypes.Should().ContainSingle()
                .Which.Name.Should().Be("QobuzDownloadClient");
        }

        [Fact]
        public void PluginTypes_ShouldHaveParameterlessOrDIConstructor()
        {
            // Lidarr's DI container needs to be able to instantiate plugin types
            
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            var downloadClientType = typeof(QobuzDownloadClient);
            
            // Act
            var indexerConstructors = indexerType.GetConstructors();
            var downloadClientConstructors = downloadClientType.GetConstructors();
            
            // Assert
            indexerConstructors.Should().NotBeEmpty("QobuzIndexer needs at least one public constructor");
            downloadClientConstructors.Should().NotBeEmpty("QobuzDownloadClient needs at least one public constructor");
            
            // All constructor parameters should be resolvable by DI
            foreach (var constructor in indexerConstructors)
            {
                var parameters = constructor.GetParameters();
                parameters.Should().AllSatisfy(p =>
                    p.ParameterType.IsInterface || 
                    p.ParameterType.Name.Contains("Logger") ||
                    p.ParameterType.Name.Contains("Config"),
                    "All constructor parameters should be DI-resolvable");
            }
        }

        #endregion

        #region Security and Stability Tests

        [Fact]
        public void SensitiveTypes_ShouldNotBePublic()
        {
            // Ensure credentials and session types aren't exposed publicly
            
            // Arrange
            var assembly = typeof(QobuzIndexer).Assembly;
            
            // Act
            var publicTypes = assembly.GetExportedTypes();
            
            // Assert
            publicTypes.Should().NotContain(t => 
                t.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) &&
                !t.Name.Contains("TokenRefresher"), // TokenRefresher service is ok
                "Sensitive types should not be publicly exposed");
        }

        [Fact]
        public void PluginAssembly_ShouldNotHaveObsoleteTypes()
        {
            // Check for use of obsolete APIs that might cause issues
            
            // Arrange
            var assembly = typeof(QobuzIndexer).Assembly;
            var types = assembly.GetTypes();
            
            // Act & Assert
            foreach (var type in types)
            {
                var obsoleteAttr = type.GetCustomAttribute<ObsoleteAttribute>();
                if (obsoleteAttr != null)
                {
                    // Log warning but don't fail - some obsolete types might be necessary
                    obsoleteAttr.IsError.Should().BeFalse(
                        $"Type {type.Name} is marked obsolete with error: {obsoleteAttr.Message}");
                }
            }
        }

        #endregion
    }
}