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

        // Cross-platform test paths
        private static string TestBasePath => OperatingSystem.IsWindows() ? @"C:\Music" : "/tmp/music";
        private static string TestOutputPath => OperatingSystem.IsWindows() ? @"C:\TestOutput" : "/tmp/testoutput";

        /// <summary>
        /// Normalizes path separators for cross-platform comparison.
        /// </summary>
        private static string NormalizePath(string path) =>
            path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

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
                DownloadPath = TestBasePath,
                CreateAlbumFolders = true
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert
            var expectedPath = Path.Combine(TestBasePath, "Test Artist", "Test Album");
            NormalizePath(result).Should().Be(NormalizePath(expectedPath));
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
                DownloadPath = TestBasePath
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert - on Windows these chars are sanitized; on Linux they may be preserved
            if (OperatingSystem.IsWindows())
            {
                result.Should().NotContain("<");
                result.Should().NotContain(">");
                result.Should().NotContain("?");
                result.Should().NotContain("\"");
                result.Should().NotContain("|");
            }
            // On all platforms, result should contain sanitized forms of the names
            result.Should().Contain("Test");
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
                DownloadPath = TestBasePath
            };

            // Act
            var result = _sut.BuildOutputPath(remoteAlbum, settings);

            // Assert
            result.Length.Should().BeLessThan(300); // Should be truncated
            NormalizePath(result).Should().StartWith(NormalizePath(TestBasePath));
        }

        [Fact]
        public void EnsureOutputDirectory_WithValidPath_CreatesDirectory()
        {
            // Arrange
            var path = TestOutputPath;
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
            var path = TestOutputPath;
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
            var path = TestOutputPath;
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

        // F-10: cleanup is root-contained via Common's SafeDirectoryCleanup — it deletes a partial download
        // tree under the configured root, but refuses to delete the root itself or anything outside it. These
        // exercise the real filesystem (SafeDirectoryCleanup uses System.IO, not the IDiskProvider mock).

        [Fact]
        public async Task CleanupFailedDownloadAsync_PathInsideRoot_DeletesTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "qz-cleanup-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "Artist", "Album");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "01.flac"), "x");
            try
            {
                await _sut.CleanupFailedDownloadAsync(target, root);

                Directory.Exists(target).Should().BeFalse("the partial download tree must be removed");
                Directory.Exists(root).Should().BeTrue("the download root must survive");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_PathOutsideRoot_RefusesDelete()
        {
            var root = Path.Combine(Path.GetTempPath(), "qz-root-" + Guid.NewGuid().ToString("N"));
            var outside = Path.Combine(Path.GetTempPath(), "qz-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            try
            {
                await _sut.CleanupFailedDownloadAsync(outside, root);

                Directory.Exists(outside).Should().BeTrue("a path outside the download root must never be deleted");
            }
            finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(outside, true); } catch { } }
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_PathEqualsRoot_RefusesDelete()
        {
            var root = Path.Combine(Path.GetTempPath(), "qz-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await _sut.CleanupFailedDownloadAsync(root, root);

                Directory.Exists(root).Should().BeTrue("the download root itself must never be deleted");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task CleanupFailedDownloadAsync_NonExistentPathInsideRoot_DoesNotThrow()
        {
            var root = Path.Combine(Path.GetTempPath(), "qz-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var missing = Path.Combine(root, "never-created");

                Func<Task> act = async () => await _sut.CleanupFailedDownloadAsync(missing, root);

                await act.Should().NotThrowAsync();
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void ValidateDownloadPath_WithValidPath_ReturnsTrue()
        {
            // Arrange
            var parentDir = Path.Combine(TestOutputPath, "ValidPath");
            var path = Path.Combine(parentDir, "album");

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
            var parentDir = Path.Combine(TestOutputPath, "ValidPath");
            var path = Path.Combine(parentDir, "album");

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
            var parentDir = Path.Combine(TestOutputPath, "NonExistent");
            var path = Path.Combine(parentDir, "album");

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
            var path = Path.Combine(TestOutputPath, "ValidPath");
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
            var path = Path.Combine(TestOutputPath, "InvalidPath");
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
            var basePath = Path.Combine(Path.GetTempPath(), "Downloads");
            var albumName = "Test Album";
            var expectedPath = Path.Combine(basePath, albumName);

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
            var basePath = Path.Combine(Path.GetTempPath(), "Downloads");
            var albumName = "Test Album";
            var originalPath = Path.Combine(basePath, albumName);

            MockDiskProvider.Setup(x => x.FolderExists(originalPath)).Returns(true);

            // Act
            var result = _sut.CreateUniqueDownloadDirectory(basePath, albumName);

            // Assert
            result.Should().StartWith(originalPath + "_");
            result.Should().NotBe(originalPath);
        }

        [Fact]
        public void CreateUniqueDownloadDirectory_WithSpecialCharacters_SanitizesAndCreatesUnique()
        {
            // Arrange
            var basePath = Path.Combine(Path.GetTempPath(), "Downloads");
            var albumName = "Test<>Album?";

            // Act
            var result = _sut.CreateUniqueDownloadDirectory(basePath, albumName);

            // Assert - on Windows, special chars are sanitized; on Linux they may be valid
            if (OperatingSystem.IsWindows())
            {
                result.Should().NotContain("<");
                result.Should().NotContain(">");
                result.Should().NotContain("?");
            }
            result.Should().StartWith(Path.Combine(basePath, "Test"));
        }
    }
}
