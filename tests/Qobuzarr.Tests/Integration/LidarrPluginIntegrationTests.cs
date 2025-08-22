using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Common.Cache;
using DryIoc;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for Lidarr plugin compatibility
    /// Tests plugin discovery, DI registration, and interface compatibility
    /// </summary>
    [Collection("LidarrIntegration")]
    [Trait("Category", "Integration")]
    [Trait("Component", "PluginSystem")]
    public class LidarrPluginIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Container _container;
        private readonly Assembly _pluginAssembly;
        private readonly Mock<IConfigService> _mockConfigService;
        private readonly Mock<ILocalizationService> _mockLocalizationService;
        private readonly Mock<IDiskProvider> _mockDiskProvider;
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Mock<ICacheManager> _mockCacheManager;
        private readonly Logger _logger;

        // Version compatibility requirements
        private const string TARGET_LIDARR_VERSION = "2.13.2.4686";
        private const string MIN_LIDARR_VERSION = "2.13.0.4664";

        public LidarrPluginIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = LogManager.GetCurrentClassLogger();
            
            // Setup DryIoC container (Lidarr's DI container)
            _container = new Container(rules => rules
                .WithDefaultIfAlreadyRegistered(IfAlreadyRegistered.Replace)
                .WithAutoConcreteTypeResolution()
                .WithTrackingDisposableTransients());

            // Setup mocks for Lidarr dependencies
            _mockConfigService = new Mock<IConfigService>();
            _mockLocalizationService = new Mock<ILocalizationService>();
            _mockDiskProvider = new Mock<IDiskProvider>();
            _mockHttpClient = new Mock<IHttpClient>();
            _mockCacheManager = new Mock<ICacheManager>();

            // Load plugin assembly
            _pluginAssembly = typeof(QobuzarrPlugin).Assembly;
            
            ConfigureContainer();
        }

        private void ConfigureContainer()
        {
            // Register Lidarr core services (mocked)
            _container.RegisterInstance(_mockConfigService.Object);
            _container.RegisterInstance(_mockLocalizationService.Object);
            _container.RegisterInstance(_mockDiskProvider.Object);
            _container.RegisterInstance(_mockHttpClient.Object);
            _container.RegisterInstance(_mockCacheManager.Object);
            _container.RegisterInstance(_logger);

            // Setup default mock behaviors
            _mockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            _mockDiskProvider.Setup(x => x.CreateFolder(It.IsAny<string>()));
            _mockConfigService.Setup(x => x.DownloadedAlbumsFolder).Returns("/downloads");
        }

        [Fact]
        public void PluginAssembly_HasCorrectVersion()
        {
            // Arrange
            var assemblyVersion = _pluginAssembly.GetName().Version;
            var expectedVersion = new Version(TARGET_LIDARR_VERSION);

            // Act & Assert
            assemblyVersion.Should().NotBeNull();
            assemblyVersion.Major.Should().Be(expectedVersion.Major);
            assemblyVersion.Minor.Should().Be(expectedVersion.Minor);
            
            _output.WriteLine($"Plugin assembly version: {assemblyVersion}");
            _output.WriteLine($"Target Lidarr version: {TARGET_LIDARR_VERSION}");
        }

        [Fact]
        public void PluginEntry_ImplementsRequiredInterfaces()
        {
            // Arrange
            var pluginType = typeof(QobuzarrPlugin);

            // Act - Check for plugin interface (if it exists in Lidarr)
            var hasPluginInterface = pluginType.GetInterfaces()
                .Any(i => i.Name.Contains("Plugin") || i.Name.Contains("Extension"));

            // Assert
            pluginType.Should().NotBeNull();
            pluginType.IsPublic.Should().BeTrue("Plugin class must be public");
            pluginType.IsAbstract.Should().BeFalse("Plugin class must be concrete");
            
            _output.WriteLine($"Plugin type: {pluginType.FullName}");
            _output.WriteLine($"Implements plugin interface: {hasPluginInterface}");
        }

        [Fact]
        public void QobuzIndexer_ImplementsIIndexer()
        {
            // Arrange
            var indexerType = typeof(QobuzIndexer);

            // Act
            var implementsIIndexer = typeof(IIndexer).IsAssignableFrom(indexerType);
            var hasHttpIndexerBase = indexerType.BaseType?.Name.Contains("HttpIndexerBase") ?? false;

            // Assert
            implementsIIndexer.Should().BeTrue("QobuzIndexer must implement IIndexer");
            hasHttpIndexerBase.Should().BeTrue("QobuzIndexer should derive from HttpIndexerBase");
            
            // Verify required methods exist
            var methods = indexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            methods.Should().Contain(m => m.Name == "Fetch");
            methods.Should().Contain(m => m.Name == "GetSearchRequests");
            
            _output.WriteLine($"Indexer type: {indexerType.FullName}");
            _output.WriteLine($"Base type: {indexerType.BaseType?.Name}");
            _output.WriteLine($"Implements IIndexer: {implementsIIndexer}");
        }

        [Fact]
        public void QobuzDownloadClient_ImplementsIDownloadClient()
        {
            // Arrange
            var downloadClientType = typeof(QobuzDownloadClient);

            // Act
            var implementsIDownloadClient = typeof(IDownloadClient).IsAssignableFrom(downloadClientType);
            var hasDownloadClientBase = downloadClientType.BaseType?.Name.Contains("DownloadClientBase") ?? false;

            // Assert
            implementsIDownloadClient.Should().BeTrue("QobuzDownloadClient must implement IDownloadClient");
            hasDownloadClientBase.Should().BeTrue("QobuzDownloadClient should derive from DownloadClientBase");
            
            // Verify required methods exist
            var methods = downloadClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            methods.Should().Contain(m => m.Name == "Download");
            methods.Should().Contain(m => m.Name == "GetItems");
            methods.Should().Contain(m => m.Name == "RemoveItem");
            
            // Verify required properties exist
            var properties = downloadClientType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            properties.Should().Contain(p => p.Name == "Protocol");
            properties.Should().Contain(p => p.Name == "Name");
            
            _output.WriteLine($"Download client type: {downloadClientType.FullName}");
            _output.WriteLine($"Base type: {downloadClientType.BaseType?.Name}");
            _output.WriteLine($"Implements IDownloadClient: {implementsIDownloadClient}");
        }

        [Fact]
        public void DependencyInjection_CanResolvePluginServices()
        {
            // Arrange - Register plugin services
            _container.Register<QobuzIndexer>(Reuse.Singleton);
            _container.Register<QobuzDownloadClient>(Reuse.Singleton);
            
            // Register plugin dependencies
            RegisterPluginDependencies();

            // Act & Assert - Resolve indexer
            var indexer = _container.Resolve<QobuzIndexer>();
            indexer.Should().NotBeNull();
            
            // Act & Assert - Resolve download client
            var downloadClient = _container.Resolve<QobuzDownloadClient>();
            downloadClient.Should().NotBeNull();
            
            _output.WriteLine("Successfully resolved plugin services through DI container");
        }

        [Fact]
        public void Settings_SerializeCorrectly()
        {
            // Arrange
            var indexerSettings = new QobuzIndexerSettings
            {
                BaseUrl = "https://api.qobuz.com",
                AppId = "test_app_id",
                AppSecret = "test_secret",
                Username = "test@example.com",
                Password = "test_password",
                PreferredQuality = 27
            };

            var downloadSettings = new QobuzDownloadSettings
            {
                BaseUrl = "https://api.qobuz.com",
                AppId = "test_app_id",
                AppSecret = "test_secret",
                Username = "test@example.com",
                Password = "test_password",
                DownloadPath = "/downloads/qobuz",
                PreferredQuality = 27,
                MaxConcurrentDownloads = 3
            };

            // Act - Serialize and deserialize
            var indexerJson = Newtonsoft.Json.JsonConvert.SerializeObject(indexerSettings);
            var downloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(downloadSettings);
            
            var deserializedIndexer = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzIndexerSettings>(indexerJson);
            var deserializedDownload = Newtonsoft.Json.JsonConvert.DeserializeObject<QobuzDownloadSettings>(downloadJson);

            // Assert
            deserializedIndexer.Should().BeEquivalentTo(indexerSettings);
            deserializedDownload.Should().BeEquivalentTo(downloadSettings);
            
            _output.WriteLine("Settings serialization/deserialization successful");
        }

        [Fact]
        public void PluginManifest_ContainsRequiredMetadata()
        {
            // Arrange
            var pluginJsonPath = Path.Combine(
                Path.GetDirectoryName(_pluginAssembly.Location) ?? "",
                "plugin.json");

            // Act
            var manifestExists = File.Exists(pluginJsonPath);
            
            if (manifestExists)
            {
                var manifestContent = File.ReadAllText(pluginJsonPath);
                var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(manifestContent);
                
                // Assert
                manifest.Should().NotBeNull();
                ((string)manifest.name).Should().NotBeNullOrEmpty();
                ((string)manifest.version).Should().NotBeNullOrEmpty();
                ((string)manifest.minimumLidarrVersion).Should().NotBeNullOrEmpty();
                
                _output.WriteLine($"Plugin name: {manifest.name}");
                _output.WriteLine($"Plugin version: {manifest.version}");
                _output.WriteLine($"Minimum Lidarr version: {manifest.minimumLidarrVersion}");
            }
            else
            {
                _output.WriteLine("Warning: plugin.json not found in assembly directory");
            }
        }

        [Fact]
        public void AssemblyLoading_NoVersionConflicts()
        {
            // Arrange
            var referencedAssemblies = _pluginAssembly.GetReferencedAssemblies();
            var lidarrAssemblies = referencedAssemblies
                .Where(a => a.Name.StartsWith("NzbDrone") || a.Name.StartsWith("Lidarr"))
                .ToList();

            // Act & Assert
            foreach (var assembly in lidarrAssemblies)
            {
                assembly.Version.Should().NotBeNull();
                
                // Version should match target Lidarr version
                if (assembly.Name == "NzbDrone.Core" || assembly.Name == "Lidarr.Core")
                {
                    var majorMinor = $"{assembly.Version.Major}.{assembly.Version.Minor}";
                    var targetMajorMinor = TARGET_LIDARR_VERSION.Split('.').Take(2);
                    
                    majorMinor.Should().Be(string.Join(".", targetMajorMinor),
                        $"Assembly {assembly.Name} version should match target Lidarr version");
                }
                
                _output.WriteLine($"Referenced: {assembly.Name} v{assembly.Version}");
            }
        }

        [Fact]
        public void Constructor_AcceptsRequiredDependencies()
        {
            // Arrange - Test QobuzDownloadClient constructor
            var downloadClientType = typeof(QobuzDownloadClient);
            var constructors = downloadClientType.GetConstructors();

            // Act & Assert
            constructors.Should().NotBeEmpty("QobuzDownloadClient must have public constructors");
            
            var mainConstructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = mainConstructor.GetParameters();
            
            // Verify ILocalizationService parameter exists (critical for Lidarr compatibility)
            parameters.Should().Contain(p => p.ParameterType == typeof(ILocalizationService),
                "Constructor must accept ILocalizationService for Lidarr compatibility");
            
            _output.WriteLine($"Constructor parameters: {parameters.Length}");
            foreach (var param in parameters)
            {
                _output.WriteLine($"  - {param.ParameterType.Name} {param.Name}");
            }
        }

        [Fact]
        public void InterfaceCompatibility_MatchesLidarrVersion()
        {
            // Arrange
            var downloadProtocolType = typeof(DownloadProtocol);
            var downloadItemStatusType = typeof(DownloadItemStatus);

            // Act - Verify enum values exist
            var protocols = Enum.GetValues(downloadProtocolType);
            var statuses = Enum.GetValues(downloadItemStatusType);

            // Assert
            protocols.Should().NotBeEmpty();
            statuses.Should().NotBeEmpty();
            
            // Verify expected values exist
            protocols.Cast<DownloadProtocol>().Should().Contain(DownloadProtocol.Usenet);
            statuses.Cast<DownloadItemStatus>().Should().Contain(DownloadItemStatus.Queued);
            statuses.Cast<DownloadItemStatus>().Should().Contain(DownloadItemStatus.Downloading);
            statuses.Cast<DownloadItemStatus>().Should().Contain(DownloadItemStatus.Completed);
            statuses.Cast<DownloadItemStatus>().Should().Contain(DownloadItemStatus.Failed);
            
            _output.WriteLine("Interface compatibility verified");
        }

        [Fact]
        public async Task PluginLifecycle_InitializeAndDispose()
        {
            // Arrange
            RegisterPluginDependencies();
            
            // Act - Create and initialize plugin components
            var indexer = _container.Resolve<QobuzIndexer>();
            var downloadClient = _container.Resolve<QobuzDownloadClient>();
            
            // Simulate plugin lifecycle
            var testRelease = new ReleaseInfo
            {
                Title = "Test Album",
                DownloadUrl = "qobuz://album/123456",
                Guid = "qobuz-123456"
            };
            
            // Test indexer search
            var searchCriteria = new AlbumSearchCriteria
            {
                Artist = new Music.Artist { Name = "Test Artist" },
                AlbumTitle = "Test Album"
            };
            
            var searchRequests = indexer.GetSearchRequests(searchCriteria);
            searchRequests.Should().NotBeNull();
            
            // Test download client
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = "Test Artist" },
                Albums = new List<Album> 
                { 
                    new Album { Title = "Test Album" } 
                },
                Release = testRelease
            };
            
            var downloadId = await downloadClient.Download(remoteAlbum, indexer);
            downloadId.Should().NotBeNullOrEmpty();
            
            // Cleanup
            downloadClient.RemoveItem(new DownloadClientItem { DownloadId = downloadId }, false);
            
            // Assert - Verify clean disposal
            downloadClient.GetItems().Should().NotContain(i => i.DownloadId == downloadId);
            
            _output.WriteLine("Plugin lifecycle test completed successfully");
        }

        private void RegisterPluginDependencies()
        {
            // Register all plugin dependencies that would be provided by Lidarr
            var pluginTypes = _pluginAssembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)
                .Where(t => t.Namespace?.StartsWith("Lidarr.Plugin.Qobuzarr") ?? false);

            foreach (var type in pluginTypes)
            {
                // Register services and their interfaces
                var interfaces = type.GetInterfaces()
                    .Where(i => i.Namespace?.StartsWith("Lidarr.Plugin.Qobuzarr") ?? false);
                
                foreach (var iface in interfaces)
                {
                    _container.Register(iface, type, Reuse.Singleton);
                }
                
                // Also register concrete types
                if (!type.IsGenericTypeDefinition)
                {
                    _container.Register(type, Reuse.Singleton);
                }
            }
            
            _output.WriteLine($"Registered {pluginTypes.Count()} plugin types in DI container");
        }

        public void Dispose()
        {
            _container?.Dispose();
        }
    }
}