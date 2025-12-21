using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    public class DownloadFileServiceTests : TestFixtureBase
    {
        private readonly DownloadFileService _sut;

        public DownloadFileServiceTests()
        {
            _sut = new DownloadFileService(
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullDiskProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadFileService(
                null,
                MockRemotePathMappingService.Object,
                MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullRemotePathMappingService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadFileService(
                MockDiskProvider.Object,
                null,
                MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadFileService(
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                null));
        }

        [Fact]
        public void BuildOutputPath_WithValidRemoteAlbum_ReturnsCorrectPath()
        {
            // Arrange
            var artist = new Artist { Name = "Test Artist" };
            var album = new Album 
            { 
                Title = "Test Album",
                Artist = new NzbDrone.Core.Datastore.LazyLoaded<Artist>(artist)
            };
            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { album },
                Artist = artist
            };
            var settings = new QobuzDownloadSettings
            {
                DownloadPath = @"C:\Music",
                CreateAlbumFolders = true
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert
            result.Should().Be(@"C:\Music\Test Artist\Test Album");
        }

        [Fact]
        public void BuildOutputPath_WithSpecialCharacters_SanitizesFileName()
        {
            // Arrange
            var artist = new Artist { Name = "Test<Artist>?" };
            var album = new Album 
            { 
                Title = "Test\"Album\"|File",
                Artist = new NzbDrone.Core.Datastore.LazyLoaded<Artist>(artist)
            };
            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { album },
                Artist = artist
            };
            var settings = new QobuzDownloadSettings
            {
                DownloadPath = @"C:\Music"
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert
            result.Should().NotContain("<");
            result.Should().NotContain(">");
            result.Should().NotContain("?");
            result.Should().NotContain("\"");
            result.Should().NotContain("|");
            result.Should().Contain("TestArtist");
            result.Should().Contain("TestAlbumFile");
        }

        [Fact]
        public void BuildOutputPath_WithEmptyAlbums_ThrowsArgumentException()
        {
            // Arrange
            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album>()
            };
            var settings = new QobuzDownloadSettings();

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => _sut.BuildOutputPath(remoteAlbum, settings));
            ex.Message.Should().Contain("Remote album must contain at least one album");
        }

        [Fact]
        public void BuildOutputPath_WithLongNames_TruncatesCorrectly()
        {
            // Arrange
            var longName = new string('A', 200); // Very long name
            var artist = new Artist { Name = longName };
            var album = new Album 
            { 
                Title = longName,
                Artist = new NzbDrone.Core.Datastore.LazyLoaded<Artist>(artist)
            };
            var remoteAlbum = new RemoteAlbum
            {
                Albums = new List<Album> { album },
                Artist = artist
            };
            var settings = new QobuzDownloadSettings
            {
                DownloadPath = @"C:\Music"
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert
            result.Length.Should().BeLessThan(300); // Should be truncated
            result.Should().StartWith(@"C:\Music");
        }

        [Fact]
        public void EnsureOutputDirectory_WithValidPath_CreatesDirectory()
        {
            // Arrange
            var path = @"C:\TestOutput";
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(false);
            MockDiskProvider.Setup(x => x.FolderWritable(path)).Returns(true);

            // Act
            _sut.EnsureOutputDirectory(path);

            // Assert
            MockDiskProvider.Verify(x => x.CreateFolder(path), Times.Once);
        }

        [Fact]
        public void EnsureOutputDirectory_WithExistingDirectory_DoesNotCreateDirectory()
        {
            // Arrange
            var path = @"C:\TestOutput";
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(true);
            MockDiskProvider.Setup(x => x.FolderWritable(path)).Returns(true);

            // Act
            _sut.EnsureOutputDirectory(path);

            // Assert
            MockDiskProvider.Verify(x => x.CreateFolder(path), Times.Never);
        }

        [Fact]
        public void EnsureOutputDirectory_WithNonWritableDirectory_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var path = @"C:\TestOutput";
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(true);
            MockDiskProvider.Setup(x => x.FolderWritable(path)).Returns(false);

            // Act & Assert
            var ex = Assert.Throws<UnauthorizedAccessException>(() => _sut.EnsureOutputDirectory(path));
            ex.Message.Should().Contain("not writable");
        }

        [Fact]
        public void EnsureOutputDirectory_WithNullOrWhitespacePath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _sut.EnsureOutputDirectory(null));
            Assert.Throws<ArgumentException>(() => _sut.EnsureOutputDirectory(""));
            Assert.Throws<ArgumentException>(() => _sut.EnsureOutputDirectory("   "));
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_WithValidPath_DeletesFiles()
        {
            // Arrange
            var path = @"C:\FailedDownload";
            var files = new[] { @"C:\FailedDownload\file1.mp3", @"C:\FailedDownload\file2.mp3" };
            
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(true);
            MockDiskProvider.Setup(x => x.GetFiles(path, true)).Returns(files);

            // Act
            await _sut.CleanupFailedDownloadAsync(path);

            // Assert
            MockDiskProvider.Verify(x => x.DeleteFile(files[0]), Times.Once);
            MockDiskProvider.Verify(x => x.DeleteFile(files[1]), Times.Once);
            MockDiskProvider.Verify(x => x.DeleteFolder(path, true), Times.Once);
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_WithNonExistentPath_DoesNothing()
        {
            // Arrange
            var path = @"C:\NonExistent";
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(false);

            // Act
            await _sut.CleanupFailedDownloadAsync(path);

            // Assert
            MockDiskProvider.Verify(x => x.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            MockDiskProvider.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_WithFileDeleteError_ContinuesWithFolderCleanup()
        {
            // Arrange
            var path = @"C:\FailedDownload";
            var files = new[] { @"C:\FailedDownload\file1.mp3" };
            
            MockDiskProvider.Setup(x => x.FolderExists(path)).Returns(true);
            MockDiskProvider.Setup(x => x.GetFiles(path, true)).Returns(files);
            MockDiskProvider.Setup(x => x.DeleteFile(files[0])).Throws<IOException>();

            // Act
            await _sut.CleanupFailedDownloadAsync(path);

            // Assert
            MockDiskProvider.Verify(x => x.DeleteFile(files[0]), Times.Once);
            MockDiskProvider.Verify(x => x.DeleteFolder(path, true), Times.Once);
        }

        [Fact]
        public void ValidateDownloadPath_WithValidPath_ReturnsTrue()
        {
            // Arrange
            var path = @"C:\ValidPath\album";
            var parentDir = @"C:\ValidPath";
            
            MockDiskProvider.Setup(x => x.FolderExists(parentDir)).Returns(true);
            MockDiskProvider.Setup(x => x.FolderWritable(parentDir)).Returns(true);
            MockDiskProvider.Setup(x => x.GetAvailableSpace(parentDir)).Returns(2L * 1024 * 1024 * 1024); // 2GB

            // Act
            var result = _sut.ValidateDownloadPath(path);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ValidateDownloadPath_WithInsufficientSpace_ReturnsFalse()
        {
            // Arrange
            var path = @"C:\ValidPath\album";
            var parentDir = @"C:\ValidPath";
            
            MockDiskProvider.Setup(x => x.FolderExists(parentDir)).Returns(true);
            MockDiskProvider.Setup(x => x.FolderWritable(parentDir)).Returns(true);
            MockDiskProvider.Setup(x => x.GetAvailableSpace(parentDir)).Returns(100L * 1024 * 1024); // 100MB (< 1GB minimum)

            // Act
            var result = _sut.ValidateDownloadPath(path);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ValidateDownloadPath_WithNonExistentParent_ReturnsFalse()
        {
            // Arrange
            var path = @"C:\NonExistent\album";
            var parentDir = @"C:\NonExistent";
            
            MockDiskProvider.Setup(x => x.FolderExists(parentDir)).Returns(false);

            // Act
            var result = _sut.ValidateDownloadPath(path);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ValidateDownloadPath_WithNullPath_ReturnsFalse()
        {
            // Act
            var result = _sut.ValidateDownloadPath(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void GetAvailableDiskSpace_WithValidPath_ReturnsSpace()
        {
            // Arrange
            var path = @"C:\ValidPath";
            var expectedSpace = 1024L * 1024 * 1024; // 1GB
            
            MockDiskProvider.Setup(x => x.GetAvailableSpace(path)).Returns(expectedSpace);

            // Act
            var result = _sut.GetAvailableDiskSpace(path);

            // Assert
            result.Should().Be(expectedSpace);
        }

        [Fact]
        public void GetAvailableDiskSpace_WithException_ReturnsNull()
        {
            // Arrange
            var path = @"C:\InvalidPath";
            MockDiskProvider.Setup(x => x.GetAvailableSpace(path)).Throws<IOException>();

            // Act
            var result = _sut.GetAvailableDiskSpace(path);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void CreateUniqueDownloadDirectory_WithNonExistentDirectory_ReturnsOriginalPath()
        {
            // Arrange
            var basePath = @"C:\Downloads";
            var albumName = "Test Album";
            var expectedPath = @"C:\Downloads\Test Album";
            
            MockDiskProvider.Setup(x => x.FolderExists(expectedPath)).Returns(false);

            // Act
            var result = _sut.CreateUniqueDownloadDirectory(basePath, albumName);

            // Assert
            result.Should().Be(expectedPath);
        }

        [Fact]
        public void CreateUniqueDownloadDirectory_WithExistingDirectory_ReturnsUniquePathWithTimestamp()
        {
            // Arrange
            var basePath = @"C:\Downloads";
            var albumName = "Test Album";
            var originalPath = @"C:\Downloads\Test Album";
            
            MockDiskProvider.Setup(x => x.FolderExists(originalPath)).Returns(true);

            // Act
            var result = _sut.CreateUniqueDownloadDirectory(basePath, albumName);

            // Assert
            result.Should().StartWith(@"C:\Downloads\Test Album_");
            result.Should().NotBe(originalPath);
        }

        [Fact]
        public void CreateUniqueDownloadDirectory_WithSpecialCharacters_SanitizesAndCreatesUnique()
        {
            // Arrange
            var basePath = @"C:\Downloads";
            var albumName = "Test<>Album?";
            
            // Act
            var result = _sut.CreateUniqueDownloadDirectory(basePath, albumName);

            // Assert
            result.Should().NotContain("<");
            result.Should().NotContain(">");
            result.Should().NotContain("?");
            result.Should().StartWith(@"C:\Downloads\Test");
        }
    }
}
