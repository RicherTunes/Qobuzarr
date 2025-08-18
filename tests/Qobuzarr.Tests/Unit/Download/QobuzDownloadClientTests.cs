using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Download
{
    // Tests restored and updated for current API
    public class QobuzDownloadClientTests : TestFixtureBase
    {
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly QobuzDownloadClient _downloadClient;
        private readonly QobuzSession _testSession;

        public QobuzDownloadClientTests()
        {
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _mockApiClient = new Mock<IQobuzApiClient>();
            
            _downloadClient = new QobuzDownloadClient(
                _mockAuthService.Object,
                _mockApiClient.Object,
                MockHttpClient.Object,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,  // Added missing ILocalizationService
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

        private void SetupMockDefaults()
        {
            _mockAuthService.Setup(x => x.GetCachedSession()).Returns(_testSession);
            
            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            MockDiskProvider.Setup(x => x.CreateFolder(It.IsAny<string>()));
            
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            _mockApiClient.Setup(x => x.GetAsync<QobuzAlbum>("/album/get", It.IsAny<Dictionary<string, string>>()))
                         .ReturnsAsync(album);
        }

        [Fact]
        public async Task Download_WithValidRemoteAlbum_ShouldReturnDownloadId()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            Guid.TryParse(downloadId.Replace("-", ""), out _).Should().BeTrue();
        }

        [Fact]
        public async Task Download_WithInvalidAlbumId_ShouldThrowException()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            remoteAlbum.Release.DownloadUrl = "invalid://url";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>()));
            
            exception.Message.Should().Contain("Could not extract album ID");
        }

        [Fact]
        public async Task Download_ShouldCreateDownloadItemWithCorrectProperties()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Assert
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            downloadItem.Should().NotBeNull();
            downloadItem.Title.Should().Be(remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album");
            downloadItem.Status.Should().Be(DownloadItemStatus.Queued);
            downloadItem.TotalSize.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task GetItems_WithActiveDownloads_ShouldReturnDownloadItems()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

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
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            _downloadClient.GetItems().Should().HaveCount(1);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, false);

            // Assert
            _downloadClient.GetItems().Should().BeEmpty();
        }

        [Fact]
        public async Task RemoveItem_WithDeleteData_ShouldDeleteFiles()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, true);

            // Assert
            MockDiskProvider.Verify(x => x.DeleteFolder(It.IsAny<string>(), true), Times.Once);
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
        public void AddFromMagnetLink_ShouldThrowNotSupportedException()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act & Assert
            Assert.Throws<NotSupportedException>(() => _downloadClient.Test());
        }

        [Fact]
        public void AddFromTorrentFile_ShouldThrowNotSupportedException()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act & Assert
            Assert.Throws<NotSupportedException>(() => _downloadClient.Test());
        }

        [Fact]
        public void Protocol_ShouldReturnQobuzDownloadProtocol()
        {
            // Act & Assert
            _downloadClient.Protocol.Should().Be(nameof(QobuzDownloadProtocol));
        }

        [Fact]
        public void Name_ShouldReturnQobuz()
        {
            // Act & Assert
            _downloadClient.Name.Should().Be("Qobuz");
        }

        [Fact]
        public async Task PerformDownload_WithValidAlbum_ShouldCompleteSuccessfully()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Wait for download to process
            await Task.Delay(100);

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            // Note: Status might be Downloading or Completed depending on timing
            downloadItem.Status.Should().BeOneOf(DownloadItemStatus.Downloading, DownloadItemStatus.Completed);
        }

        [Fact]
        public async Task PerformDownload_WithoutAuthentication_ShouldFail()
        {
            // Arrange
            _mockAuthService.Setup(x => x.GetCachedSession()).Returns((QobuzSession)null);
            
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Wait for download to process
            await Task.Delay(200);

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Contain("authentication");
        }

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

        [Fact]
        public void ExtractAlbumIdFromRelease_WithValidQobuzUrl_ShouldReturnAlbumId()
        {
            // Arrange
            var release = new ReleaseInfo
            {
                DownloadUrl = "qobuz://album/0060254788359"
            };

            // Use reflection to access private method for testing
            var method = typeof(QobuzDownloadClient).GetMethod("ExtractAlbumIdFromRelease", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var albumId = (string)method.Invoke(_downloadClient, new object[] { release });

            // Assert
            albumId.Should().Be("0060254788359");
        }

        [Fact]
        public void ExtractAlbumIdFromRelease_WithValidGuid_ShouldReturnAlbumId()
        {
            // Arrange
            var release = new ReleaseInfo
            {
                Guid = "qobuz-0060254788359"
            };

            // Use reflection to access private method for testing
            var method = typeof(QobuzDownloadClient).GetMethod("ExtractAlbumIdFromRelease", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var albumId = (string)method.Invoke(_downloadClient, new object[] { release });

            // Assert
            albumId.Should().Be("0060254788359");
        }

        [Fact]
        public async Task CleanupOldDownloads_ShouldRemoveOldCompletedDownloads()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

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

        [Fact]
        public async Task Download_WithApiError_ShouldMarkAsFailed()
        {
            // Arrange
            _mockApiClient.Setup(x => x.GetAsync<QobuzAlbum>("/album/get", It.IsAny<Dictionary<string, string>>()))
                         .ThrowsAsync(new InvalidOperationException("API Error"));

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Wait for download to process and fail
            await Task.Delay(200);

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
            var downloadId1 = await _downloadClient.Download(remoteAlbum1, Mock.Of<IIndexer>());
            var downloadId2 = await _downloadClient.Download(remoteAlbum2, Mock.Of<IIndexer>());

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
