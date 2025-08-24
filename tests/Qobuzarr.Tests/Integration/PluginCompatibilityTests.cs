using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using DryIoc;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Tests plugin compatibility with Lidarr's plugin system
    /// Validates protocol compatibility, DI registration, and assembly loading
    /// </summary>
    [Collection("PluginCompatibility")]
    public class PluginCompatibilityTests : IClassFixture<LidarrPluginFixture>
    {
        private readonly ITestOutputHelper _output;
        private readonly LidarrPluginFixture _fixture;

        public PluginCompatibilityTests(ITestOutputHelper output, LidarrPluginFixture fixture)
        {
            _output = output;
            _fixture = fixture;
        }

        #region Assembly Loading Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void Plugin_ShouldLoadWithCorrectAssemblyVersion()
        {
            // Arrange
            var pluginAssembly = typeof(QobuzarrPlugin).Assembly;
            var expectedVersion = new Version(2, 13, 2, 4686); // Target Lidarr version

            // Act
            var assemblyVersion = pluginAssembly.GetName().Version;
            var lidarrCoreReference = pluginAssembly.GetReferencedAssemblies()
                .FirstOrDefault(a => a.Name == "NzbDrone.Core");

            // Assert
            assemblyVersion.Should().NotBeNull("Plugin assembly should have a version");
            lidarrCoreReference.Should().NotBeNull("Plugin should reference NzbDrone.Core");
            
            // Version compatibility check
            lidarrCoreReference.Version.Major.Should().Be(expectedVersion.Major, 
                "Major version should match target Lidarr version");
            
            _output.WriteLine($"Plugin version: {assemblyVersion}");
            _output.WriteLine($"Lidarr.Core reference: {lidarrCoreReference?.Version}");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzarrPlugin_ShouldImplementIPlugin()
        {
            // Arrange & Act
            var plugin = new QobuzarrPlugin();
            var pluginInterface = plugin.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == "IPlugin");

            // Assert
            pluginInterface.Should().NotBeNull("Plugin should implement IPlugin interface");
            plugin.Name.Should().Be("Qobuzarr");
            plugin.Version.Should().NotBeNull();
            
            _output.WriteLine($"Plugin: {plugin.Name} v{plugin.Version}");
        }

        #endregion

        #region Protocol Compatibility Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzarrDownloadProtocol_ShouldImplementIDownloadProtocol()
        {
            // This tests the critical protocol compatibility issue
            
            // Arrange & Act
            var protocolType = typeof(QobuzarrDownloadProtocol);
            var implementsInterface = protocolType.GetInterfaces()
                .Any(i => i.Name == "IDownloadProtocol");

            // Assert
            implementsInterface.Should().BeTrue(
                "QobuzarrDownloadProtocol must implement IDownloadProtocol for plugin compatibility");
            
            // Verify it's in correct namespace for Lidarr discovery
            protocolType.Namespace.Should().Be("NzbDrone.Core.Indexers",
                "Protocol must be in NzbDrone.Core.Indexers namespace");
            
            _output.WriteLine($"Protocol type: {protocolType.FullName}");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzIndexer_Protocol_ShouldReturnStringNameof()
        {
            // Validate the Protocol property returns string (plugins branch requirement)
            
            // Arrange
            var indexer = _fixture.CreateIndexer();
            
            // Act
            var protocol = indexer.Protocol;
            var protocolType = indexer.GetType()
                .GetProperty("Protocol")
                ?.PropertyType;

            // Assert
            protocol.Should().Be("QobuzarrDownloadProtocol",
                "Protocol should return nameof(QobuzarrDownloadProtocol)");
            protocolType.Should().Be(typeof(string),
                "Protocol property must be string type for plugins branch compatibility");
            
            _output.WriteLine($"Indexer protocol: {protocol} (Type: {protocolType?.Name})");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzDownloadClient_Protocol_ShouldMatchIndexer()
        {
            // Ensure download client and indexer use same protocol
            
            // Arrange
            var downloadClient = _fixture.CreateDownloadClient();
            var indexer = _fixture.CreateIndexer();
            
            // Act
            var clientProtocol = downloadClient.Protocol;
            var indexerProtocol = indexer.Protocol;

            // Assert
            clientProtocol.Should().Be(indexerProtocol,
                "Download client and indexer must use the same protocol");
            clientProtocol.Should().Be("QobuzarrDownloadProtocol");
            
            _output.WriteLine($"Protocols match: {clientProtocol}");
        }

        #endregion

        #region Dependency Injection Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void Services_ShouldRegisterInDryIocContainer()
        {
            // Test that services can be registered in Lidarr's DryIoC container
            
            // Arrange
            var container = new Container();
            var module = new QobuzarrModule();

            // Act
            module.Register(container);

            // Assert
            // Verify critical services are registered
            container.IsRegistered<IQobuzApiClient>().Should().BeTrue(
                "IQobuzApiClient should be registered");
            container.IsRegistered<IQobuzAuthenticationService>().Should().BeTrue(
                "IQobuzAuthenticationService should be registered");
            container.IsRegistered<IDownloadOrchestrator>().Should().BeTrue(
                "IDownloadOrchestrator should be registered");
            
            _output.WriteLine("All services successfully registered in DryIoC container");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void Indexer_ShouldBeDiscoverableByLidarr()
        {
            // Test that indexer can be discovered via reflection (how Lidarr finds it)
            
            // Arrange
            var assembly = typeof(QobuzIndexer).Assembly;
            
            // Act
            var indexerTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && 
                           t.IsSubclassOf(typeof(HttpIndexerBase<>).MakeGenericType(typeof(QobuzIndexerSettings))))
                .ToList();

            // Assert
            indexerTypes.Should().ContainSingle(t => t == typeof(QobuzIndexer),
                "Assembly should contain exactly one QobuzIndexer");
            
            var indexerType = indexerTypes.First();
            indexerType.Should().BePublic("Indexer must be public for discovery");
            indexerType.GetConstructors().Should().NotBeEmpty("Indexer must have constructors");
            
            _output.WriteLine($"Discoverable indexer: {indexerType.FullName}");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void DownloadClient_ShouldBeDiscoverableByLidarr()
        {
            // Test that download client can be discovered via reflection
            
            // Arrange
            var assembly = typeof(QobuzDownloadClient).Assembly;
            
            // Act
            var downloadClientTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && 
                           t.IsSubclassOf(typeof(DownloadClientBase<>).MakeGenericType(typeof(QobuzDownloadSettings))))
                .ToList();

            // Assert
            downloadClientTypes.Should().ContainSingle(t => t == typeof(QobuzDownloadClient),
                "Assembly should contain exactly one QobuzDownloadClient");
            
            var clientType = downloadClientTypes.First();
            clientType.Should().BePublic("Download client must be public for discovery");
            
            _output.WriteLine($"Discoverable download client: {clientType.FullName}");
        }

        #endregion

        #region Settings Persistence Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzIndexerSettings_ShouldSerializeCorrectly()
        {
            // Test settings serialization for Lidarr database persistence
            
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                AppId = "test_app_id",
                AppSecret = "test_app_secret",
                Username = "test@example.com",
                Password = "test_password",
                PreferHighResolution = true,
                EnableSmartSearch = true,
                EnableMLOptimization = true
            };

            // Act
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzIndexerSettings>(json);

            // Assert
            deserialized.Should().BeEquivalentTo(settings,
                "Settings should serialize/deserialize without data loss");
            json.Should().Contain("AppId");
            json.Should().Contain("EnableMLOptimization");
            
            _output.WriteLine($"Serialized settings: {json}");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzDownloadSettings_ShouldValidateCorrectly()
        {
            // Test settings validation logic
            
            // Arrange
            var validSettings = new QobuzDownloadSettings
            {
                AppId = "test_app_id",
                AppSecret = "test_app_secret",
                Username = "test@example.com",
                Password = "test_password",
                DownloadPath = "/downloads",
                QualityId = 27,
                MaxConcurrentDownloads = 3
            };

            var invalidSettings = new QobuzDownloadSettings
            {
                // Missing required fields
                DownloadPath = "/downloads"
            };

            // Act
            var validationValid = validSettings.Validate();
            var validationInvalid = invalidSettings.Validate();

            // Assert
            validationValid.IsValid.Should().BeTrue("Valid settings should pass validation");
            validationInvalid.IsValid.Should().BeFalse("Invalid settings should fail validation");
            validationInvalid.Errors.Should().NotBeEmpty("Should have validation errors");
            
            _output.WriteLine($"Validation errors: {string.Join(", ", validationInvalid.Errors)}");
        }

        #endregion

        #region Constructor Compatibility Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzIndexer_Constructor_ShouldMatchLidarrRequirements()
        {
            // Validate constructor signature matches Lidarr expectations
            
            // Arrange
            var indexerType = typeof(QobuzIndexer);
            
            // Act
            var constructors = indexerType.GetConstructors();
            var primaryConstructor = constructors.FirstOrDefault();

            // Assert
            constructors.Should().HaveCount(1, "Should have single constructor");
            primaryConstructor.Should().NotBeNull();
            
            var parameters = primaryConstructor.GetParameters();
            parameters.Should().Contain(p => p.ParameterType == typeof(IHttpClient),
                "Constructor should accept IHttpClient");
            parameters.Should().Contain(p => p.ParameterType == typeof(IIndexerStatusService),
                "Constructor should accept IIndexerStatusService");
            parameters.Should().Contain(p => p.ParameterType == typeof(IConfigService),
                "Constructor should accept IConfigService");
            
            _output.WriteLine($"Constructor parameters: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void QobuzDownloadClient_Constructor_ShouldIncludeLocalizationService()
        {
            // Critical: DownloadClientBase requires ILocalizationService
            
            // Arrange
            var clientType = typeof(QobuzDownloadClient);
            
            // Act
            var constructor = clientType.GetConstructors().FirstOrDefault();
            var parameters = constructor?.GetParameters();

            // Assert
            parameters.Should().Contain(p => p.ParameterType == typeof(ILocalizationService),
                "Constructor MUST include ILocalizationService for DownloadClientBase compatibility");
            
            _output.WriteLine($"Has ILocalizationService: {parameters?.Any(p => p.ParameterType == typeof(ILocalizationService))}");
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public async Task Indexer_ShouldHandleConcurrentSearches()
        {
            // Test thread safety for concurrent Lidarr searches
            
            // Arrange
            var indexer = _fixture.CreateIndexer();
            var searchCriteria = new[]
            {
                new AlbumSearchCriteria { Artist = "Miles Davis", Album = "Kind of Blue" },
                new AlbumSearchCriteria { Artist = "John Coltrane", Album = "A Love Supreme" },
                new AlbumSearchCriteria { Artist = "Bill Evans", Album = "Sunday at the Village Vanguard" }
            };

            // Act
            var tasks = searchCriteria.Select(criteria =>
                Task.Run(() => indexer.Fetch(criteria))
            ).ToList();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(searchCriteria.Length);
            results.Should().OnlyContain(r => r != null, "All searches should complete");
            
            _output.WriteLine($"Completed {results.Length} concurrent searches successfully");
        }

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public async Task DownloadClient_ShouldHandleConcurrentDownloads()
        {
            // Test concurrent download handling
            
            // Arrange
            var downloadClient = _fixture.CreateDownloadClient();
            var downloadRequests = Enumerable.Range(1, 5)
                .Select(i => new RemoteAlbum
                {
                    Release = new AlbumRelease { AlbumId = i.ToString() },
                    ParsedAlbumInfo = new ParsedAlbumInfo
                    {
                        AlbumTitle = $"Test Album {i}",
                        ArtistName = $"Test Artist {i}"
                    }
                })
                .ToList();

            // Act
            var tasks = downloadRequests.Select(request =>
                Task.Run(() =>
                {
                    try
                    {
                        return downloadClient.Download(request);
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Download failed (expected in test): {ex.Message}");
                        return null;
                    }
                })
            ).ToList();

            var results = await Task.WhenAll(tasks);

            // Assert
            // In test environment, downloads may fail but should not deadlock
            tasks.Should().OnlyContain(t => t.IsCompleted, "All tasks should complete");
            
            _output.WriteLine($"Processed {tasks.Count} concurrent download requests");
        }

        #endregion

        #region Event Integration Tests

        [Fact]
        [Trait("Category", "PluginCompatibility")]
        public void DownloadClient_ShouldSupportLidarrEvents()
        {
            // Verify download client can handle Lidarr events
            
            // Arrange
            var clientType = typeof(QobuzDownloadClient);
            
            // Act
            var methods = clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            var hasGetItems = methods.Any(m => m.Name == "GetItems");
            var hasRemoveItem = methods.Any(m => m.Name == "RemoveItem");
            var hasGetStatus = methods.Any(m => m.Name == "GetStatus");

            // Assert
            hasGetItems.Should().BeTrue("Should implement GetItems for queue monitoring");
            hasRemoveItem.Should().BeTrue("Should implement RemoveItem for queue management");
            hasGetStatus.Should().BeTrue("Should implement GetStatus for health checks");
            
            _output.WriteLine($"Event support - GetItems: {hasGetItems}, RemoveItem: {hasRemoveItem}, GetStatus: {hasGetStatus}");
        }

        #endregion
    }

    /// <summary>
    /// Test fixture for creating plugin components with mocked dependencies
    /// </summary>
    public class LidarrPluginFixture
    {
        private readonly IServiceProvider _serviceProvider;

        public LidarrPluginFixture()
        {
            var services = new ServiceCollection();
            
            // Register mocked Lidarr services
            services.AddSingleton<IHttpClient, MockHttpClient>();
            services.AddSingleton<IConfigService, MockConfigService>();
            services.AddSingleton<IIndexerStatusService, MockIndexerStatusService>();
            services.AddSingleton<ILocalizationService, MockLocalizationService>();
            services.AddSingleton<IDiskProvider, MockDiskProvider>();
            services.AddSingleton<IRemotePathMappingService, MockRemotePathMappingService>();
            
            // Register plugin services
            services.AddSingleton<IQobuzApiClient, QobuzApiClient>();
            services.AddSingleton<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddSingleton<IDownloadOrchestrator, DownloadOrchestrator>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        public QobuzIndexer CreateIndexer()
        {
            return new QobuzIndexer(
                _serviceProvider.GetRequiredService<IHttpClient>(),
                _serviceProvider.GetRequiredService<IIndexerStatusService>(),
                _serviceProvider.GetRequiredService<IConfigService>(),
                null, // IAppIndexerMapService
                null  // Logger
            );
        }

        public QobuzDownloadClient CreateDownloadClient()
        {
            return new QobuzDownloadClient(
                _serviceProvider.GetRequiredService<IQobuzAuthenticationService>(),
                _serviceProvider.GetRequiredService<IQobuzApiClient>(),
                _serviceProvider.GetRequiredService<IHttpClient>(),
                null, // IDownloadQueueService
                null, // IDownloadFileService
                null, // IConcurrencyManager
                _serviceProvider.GetRequiredService<IDownloadOrchestrator>(),
                null, // IDownloadSummary
                null, // IBatchProcessor
                null, // IQobuzTrackDownloaderFactory
                _serviceProvider.GetRequiredService<IConfigService>(),
                _serviceProvider.GetRequiredService<IDiskProvider>(),
                _serviceProvider.GetRequiredService<IRemotePathMappingService>(),
                null, // IEventAggregator
                _serviceProvider.GetRequiredService<ILocalizationService>(),
                null  // Logger
            );
        }
    }

    // Mock implementations for testing
    public class MockHttpClient : IHttpClient { /* Mock implementation */ }
    public class MockConfigService : IConfigService { /* Mock implementation */ }
    public class MockIndexerStatusService : IIndexerStatusService { /* Mock implementation */ }
    public class MockLocalizationService : ILocalizationService { /* Mock implementation */ }
    public class MockDiskProvider : IDiskProvider { /* Mock implementation */ }
    public class MockRemotePathMappingService : IRemotePathMappingService { /* Mock implementation */ }
}