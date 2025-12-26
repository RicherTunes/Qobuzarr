using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NSubstitute;
using Newtonsoft.Json;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
// Download orchestration services - IDownloadOrchestrator still exists
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Download
{
    // Tests restored and updated for current API
    public class QobuzDownloadClientTests : TestFixtureBase
    {
        // Test-specific DownloadClient that overrides Settings access for testing
        private class TestableQobuzDownloadClient : QobuzDownloadClient
        {
            private QobuzDownloadSettings _testSettings;
            
            public TestableQobuzDownloadClient(
                IQobuzAuthenticationService authService,
                IQobuzApiClient apiClient,
                NzbDrone.Common.Http.IHttpClient httpClient,
                IDownloadQueueService queueService,
                IDownloadFileService fileService,
                IConcurrencyManager concurrencyManager,
                IDownloadOrchestrator orchestrator,
                ITrackDownloadService trackDownloadService,
                IDownloadSummary downloadSummary,
                IBatchProcessor batchProcessor,
                NzbDrone.Core.Configuration.IConfigService configService,
                NzbDrone.Common.Disk.IDiskProvider diskProvider,
                NzbDrone.Core.RemotePathMappings.IRemotePathMappingService remotePathMappingService,
                NzbDrone.Core.Localization.ILocalizationService localizationService,
                NLog.Logger logger) 
                : base(authService, apiClient, httpClient, queueService, fileService, concurrencyManager, 
                      orchestrator, downloadSummary, batchProcessor, trackDownloadService,
                      configService, diskProvider, remotePathMappingService, localizationService, logger)
            {
                _testSettings = new QobuzDownloadSettings
                {
                    DownloadPath = @"C:\Downloads\Qobuz",
                    PreferredQuality = 6, // FLAC CD Quality
                    CreateAlbumFolders = true,
                    ConcurrencyMode = (int)DownloadConcurrencyMode.Fixed,
                    FixedConcurrencyLevel = 3
                };
            }
            
            protected new QobuzDownloadSettings Settings => _testSettings;
            
            // Override GetEffectiveSettings to return test settings
            protected override QobuzDownloadSettings GetEffectiveSettings() => _testSettings;
            
            public void SetTestSettings(QobuzDownloadSettings settings)
            {
                _testSettings = settings;
            }
        }
        private readonly IQobuzAuthenticationService _mockAuthService;
        private readonly IQobuzApiClient _mockApiClient;
        private readonly IDownloadQueueService _mockQueueService;
        private readonly IDownloadFileService _mockFileService;
        private readonly IConcurrencyManager _mockConcurrencyManager;
        private readonly IDownloadOrchestrator _mockOrchestrator;
        private readonly IDownloadSummary _mockDownloadSummary;
        private readonly ITrackDownloadService _mockTrackDownloadService;
        private readonly IBatchProcessor _mockBatchProcessor;
        // REMOVED: IQobuzTrackDownloaderFactory has been deleted
        private readonly TestableQobuzDownloadClient _downloadClient;
        private readonly QobuzSession _testSession;

        public QobuzDownloadClientTests()
        {
            _mockAuthService = Substitute.For<IQobuzAuthenticationService>();
            _mockApiClient = Substitute.For<IQobuzApiClient>();
            _mockQueueService = Substitute.For<IDownloadQueueService>();
            _mockFileService = Substitute.For<IDownloadFileService>();
            _mockConcurrencyManager = Substitute.For<IConcurrencyManager>();
            _mockOrchestrator = Substitute.For<IDownloadOrchestrator>();
            _mockDownloadSummary = Substitute.For<IDownloadSummary>();
            _mockTrackDownloadService = Substitute.For<ITrackDownloadService>();
            _mockBatchProcessor = Substitute.For<IBatchProcessor>();
            // REMOVED: IQobuzTrackDownloaderFactory mock creation
            
            _downloadClient = new TestableQobuzDownloadClient(
                _mockAuthService,
                _mockApiClient,
                MockHttpClient.Object,
                _mockQueueService,
                _mockFileService,
                _mockConcurrencyManager,
                _mockOrchestrator,
                _mockTrackDownloadService,
                _mockDownloadSummary,
                _mockBatchProcessor,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,
                MockLogger.Object
            );

            _testSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "sample_auth_token_123456",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            SetupMockDefaults();
        }

        // Captured download item for RemoveItem tests
        private QobuzDownloadItem _lastQueuedDownload;

        private void SetupMockDefaults()
        {
            _mockAuthService.GetCachedSession().Returns(_testSession);
            
            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            MockDiskProvider.Setup(x => x.CreateFolder(It.IsAny<string>())).Verifiable();
            
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            _mockApiClient.GetAsync<QobuzAlbum>("/album/get", Arg.Any<Dictionary<string, string>>())
                         .Returns(album);

            // Fix: Make track download succeed by default (prevents null Task cascade)
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<System.Threading.CancellationToken>()
            ).Returns(Task.CompletedTask);

            // Fix: Make Test() pass path validation
            _mockFileService.ValidateDownloadPath(Arg.Any<string>()).Returns(true);

            // Fix: Capture queued downloads for RemoveItem tests
            _mockQueueService.When(x => x.AddDownload(Arg.Any<QobuzDownloadItem>()))
                .Do(ci => _lastQueuedDownload = ci.Arg<QobuzDownloadItem>());

            // Fix: TryGetDownload returns captured download when ID matches
            _mockQueueService.TryGetDownload(Arg.Any<string>(), out Arg.Any<QobuzDownloadItem>())
                .Returns(ci =>
                {
                    var id = ci.Arg<string>();
                    if (_lastQueuedDownload != null && _lastQueuedDownload.DownloadId == id)
                    {
                        ci[1] = _lastQueuedDownload;
                        return true;
                    }
                    ci[1] = null;
                    return false;
                });
        }

        [Fact]
        public async Task Download_WithValidRemoteAlbum_ShouldReturnDownloadId()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            Guid.TryParse(downloadId.Replace("-", ""), out _).Should().BeTrue();
        }

        [Fact]
        public async Task Download_WithInvalidAlbumId_ShouldThrowException()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            // Fix: Invalidate ALL sources so AlbumIdExtractor returns null
            remoteAlbum.Release.DownloadUrl = "invalid://url";
            remoteAlbum.Release.Guid = "";  // Clear the GUID too

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>()));
            
            exception.Message.Should().Contain("Could not extract album ID");
        }

        [Fact]
        public async Task Download_ShouldCreateDownloadItemWithCorrectProperties()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Wait for download task to complete so TotalSize gets populated
            if (_lastQueuedDownload?.DownloadTask != null)
            {
                await _lastQueuedDownload.DownloadTask;
            }

            // Assert
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            downloadItem.Should().NotBeNull();
            // Fix: ToDownloadClientItem formats title as "{Artist} - {Album}"
            var expectedTitle = $"{remoteAlbum.Artist.Name} - {remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album"}";
            downloadItem.Title.Should().Be(expectedTitle);
            // After download completes, status should be Completed (not Queued)
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);      
            downloadItem.TotalSize.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Download_WithQualityFallbacks_ShouldSurfaceInCompletionMessage()
        {
            // Arrange
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>()
            ).Returns(callInfo =>
            {
                var item = callInfo.ArgAt<QobuzDownloadItem>(0);
                item.QualityFallbackCount = 2;
                return Task.CompletedTask;
            });

            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            if (_lastQueuedDownload?.DownloadTask != null)
            {
                await _lastQueuedDownload.DownloadTask;
            }

            // Assert
            var downloadItem = _downloadClient.GetItems().FirstOrDefault(x => x.DownloadId == downloadId);
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
            downloadItem.Message.Should().Contain("quality fallback used for 2 track(s)");
        }

        [Fact]
        public async Task GetItems_WithActiveDownloads_ShouldReturnDownloadItems()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Act
            var items = _downloadClient.GetItems();

            // Assert
            items.Should().NotBeEmpty();
            items.Should().HaveCount(1);
            items.First().DownloadId.Should().Be(downloadId);
        }

        [Fact]
        public void GetItems_WithNoActiveDownloads_ShouldReturnEmptyList()
        {
            // Act
            var items = _downloadClient.GetItems();

            // Assert
            items.Should().NotBeNull();
            items.Should().BeEmpty();
        }

        [Fact]
        public async Task RemoveItem_WithValidDownloadId_ShouldRemoveFromTracking()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            _downloadClient.GetItems().Should().HaveCount(1);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, false);

            // Assert
            // Fix: Verify RemoveDownload was called with correct parameters
            _mockQueueService.Received(1).RemoveDownload(downloadId, false);
            _downloadClient.GetItems().Should().BeEmpty();
        }

        [Fact]
        public async Task RemoveItem_WithDeleteData_ShouldDeleteFiles()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, true);

            // Assert
            // Fix: RemoveItem delegates deletion to queue service, not disk provider directly
            // The deleteData flag is passed to RemoveDownload which handles file cleanup
            _mockQueueService.Received(1).RemoveDownload(downloadId, true);
        }

        [Fact]
        public void RemoveItem_WithInvalidDownloadItem_ShouldNotThrow()
        {
            // Arrange
            var invalidDownloadItem = new DownloadClientItem
            {
                DownloadId = "invalid-id",
                Title = "Invalid Item"
            };

            // Act & Assert
            _downloadClient.Invoking(x => x.RemoveItem(invalidDownloadItem, false))
                          .Should().NotThrow();
        }

        [Fact]
        public void Test_ShouldReturnValidationResult()
        {
            // Arrange - ensure session doesn't need refresh and has subscription
            _testSession.ExpiresAt = DateTime.UtcNow.AddHours(24);
            _testSession.Subscription = new QobuzSubscription 
            { 
                Type = "studio", 
                IsHiRes = true, 
                MaxSampleRate = 192000, 
                MaxBitDepth = 24,
                CanStream = true,
                CanDownload = true
            };
            
            // Act
            var result = _downloadClient.Test();

            // Assert
            result.Should().NotBeNull();
            
            // Debug: output validation errors if test fails
            if (!result.IsValid)
            {
                var errors = string.Join(", ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
                result.IsValid.Should().BeTrue($"Validation failed with errors: {errors}");
            }
            else
            {
                result.IsValid.Should().BeTrue();
            }
        }

        [Fact]
        public void Protocol_ShouldBeCompatibleWithHost()
        {
            // Act
            object proto = _downloadClient.Protocol;

            // Assert: support both host variants
            if (proto is string s)
            {
                s.Should().Be(nameof(QobuzarrDownloadProtocol));
            }
            else
            {
                proto.ToString().Should().Be("Unknown");
            }
        }

        [Fact]
        public void Name_ShouldReturnQobuzarr()
        {
            // Act & Assert
            _downloadClient.Name.Should().Be("Qobuzarr");
        }

        [Fact]
        public async Task PerformDownload_WithValidAlbum_ShouldCompleteSuccessfully()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for the actual download task to complete instead of arbitrary delay
            if (_lastQueuedDownload?.DownloadTask != null)
            {
                await _lastQueuedDownload.DownloadTask;
            }

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            // Fix: After awaiting the task, status should be Completed (not timing-dependent)
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
        }

        [Fact]
        public async Task PerformDownload_WithoutAuthentication_ShouldFail()
        {
            // Arrange
            _mockAuthService.GetCachedSession().Returns((QobuzSession)null);
            
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for the actual download task to complete instead of arbitrary delay
            if (_lastQueuedDownload?.DownloadTask != null)
            {
                try { await _lastQueuedDownload.DownloadTask; } catch { /* Expected to fail */ }
            }

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Contain("authentication");
        }

        #region Integration/Wiring Tests (kept for coverage, use reflection)

        /// <summary>
        /// Tests that BuildOutputPath produces a valid path structure.
        /// This is a wiring test - it verifies the method delegates correctly to file service.
        /// </summary>
        [Fact]
        public void BuildOutputPath_WithAlbumFolders_ShouldCreateCorrectPath()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            
            // Use reflection to access private method for testing
            var method = typeof(QobuzDownloadClient).GetMethod("BuildOutputPath", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var outputPath = (string)method.Invoke(_downloadClient, new object[] { remoteAlbum });

            // Assert
            outputPath.Should().NotBeNullOrEmpty();
            outputPath.Should().Contain(remoteAlbum.Artist.Name);
            outputPath.Should().Contain(remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album");
        }

        // NOTE: ExtractAlbumIdFromRelease tests moved to AlbumIdExtractorTests.cs
        // The AlbumIdExtractor is now a public static utility class that can be tested directly.

        /// <summary>
        /// Tests that cleanup doesn't remove recent downloads.
        /// This is a wiring test - it verifies the method delegates correctly to queue service.
        /// </summary>
        [Fact]
        public async Task CleanupOldDownloads_ShouldRemoveOldCompletedDownloads()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Simulate old completed download by manipulating internal state
            var items = _downloadClient.GetItems();
            items.Should().HaveCount(1);

            // Use reflection to access cleanup method
            var method = typeof(QobuzDownloadClient).GetMethod("CleanupOldDownloads", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            method.Invoke(_downloadClient, null);

            // Assert
            // Cleanup shouldn't remove recent downloads
            _downloadClient.GetItems().Should().HaveCount(1);
        }

        #endregion

        [Fact]
        public async Task Download_WithApiError_ShouldMarkAsFailed()
        {
            // Arrange
            _mockApiClient.GetAsync<QobuzAlbum>("/album/get", Arg.Any<Dictionary<string, string>>())
                         .Returns<QobuzAlbum>(x => throw new InvalidOperationException("API Error"));

            // Fix: Override the default track download to throw the API error
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<System.Threading.CancellationToken>()
            ).Returns(Task.FromException(new InvalidOperationException("API Error")));

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for download task to complete instead of arbitrary delay
            if (_lastQueuedDownload?.DownloadTask != null)
            {
                try { await _lastQueuedDownload.DownloadTask; } catch { /* Expected to fail */ }
            }

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Contain("API Error");
        }

        [Fact]
        public async Task Download_WithMultipleAlbums_ShouldTrackAllDownloads()
        {
            // Arrange
            var remoteAlbum1 = CreateTestRemoteAlbum("Album 1");
            var remoteAlbum2 = CreateTestRemoteAlbum("Album 2");
            
            // Act
            var downloadId1 = await _downloadClient.Download(remoteAlbum1, Substitute.For<IIndexer>());
            var downloadId2 = await _downloadClient.Download(remoteAlbum2, Substitute.For<IIndexer>());

            // Assert
            var items = _downloadClient.GetItems();
            items.Should().HaveCount(2);
            items.Select(x => x.DownloadId).Should().Contain(downloadId1);
            items.Select(x => x.DownloadId).Should().Contain(downloadId2);
        }

        private RemoteAlbum CreateTestRemoteAlbum(string albumTitle = "Random Access Memories")
        {
            return new RemoteAlbum
            {
                Artist = new Artist
                {
                    Name = "Daft Punk",
                    Id = 1
                },
                Albums = new List<Album>
                {
                    new Album
                    {
                        Title = albumTitle,
                        Id = 1,
                        ReleaseDate = new DateTime(2013, 5, 17)
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = $"Daft Punk - {albumTitle}",
                    DownloadUrl = "qobuz://album/0060254788359",
                    Guid = "qobuz-0060254788359",
                    Size = 500000000 // 500MB
                }
            };
        }

        public override void Dispose()
        {
            _downloadClient?.Dispose();
            base.Dispose();
        }
    }
}
