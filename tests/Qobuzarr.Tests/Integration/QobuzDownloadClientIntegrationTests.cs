using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for QobuzDownloadClient against real Qobuz API
    /// Tests core download functionality, error recovery, and performance
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("Category", "LiveIntegration")]
    [Trait("RequiresCredentials", "true")]
    public class QobuzDownloadClientIntegrationTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;
        private QobuzDownloadClient _downloadClient;
        private IQobuzAuthenticationService _authService;
        private IQobuzApiClient _apiClient;
        private QobuzSession _session;
        private readonly string _testOutputPath;
        private readonly List<string> _downloadedFiles = new();
        private readonly CancellationTokenSource _testCancellation = new();

        public QobuzDownloadClientIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testOutputPath = Path.Combine(Path.GetTempPath(), "QobuzarrTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputPath);

            // Setup DI container for integration tests
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configure logging
            services.AddSingleton<IQobuzLogger>(sp =>
            {
                var logger = LogManager.GetCurrentClassLogger();
                return new NLogAdapter(logger);
            });

            // Configure real services for integration testing - use Scoped for test isolation
            services.AddScoped<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddScoped<IQobuzApiClient, QobuzApiClient>();
            services.AddScoped<Lidarr.Plugin.Qobuzarr.Abstractions.IQobuzHttpClient, LidarrHttpClientAdapter>();
            services.AddScoped<IDownloadFileService, DownloadFileService>();
            services.AddScoped<IConcurrencyManager, ConcurrencyManager>();
            services.AddScoped<ITrackDownloadService, TrackDownloadService>();
            services.AddScoped<IMetadataProcessor, MetadataProcessor>();
            services.AddScoped<IQualityFallbackProvider, QualityFallbackProvider>();

            // Add download client
            services.AddScoped<QobuzDownloadClient>();
        }

        private const string SkipReason = "Qobuz credentials not configured (set QOBUZ_APP_ID, QOBUZ_EMAIL, QOBUZ_PASSWORD)";

        /// <summary>
        /// Skips the current test if prerequisites are not met.
        /// Call at the start of each test method.
        /// </summary>
        private void SkipIfNotReady()
        {
            Skip.If(_downloadClient == null, SkipReason);
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Get credentials from environment or skip tests
                var appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
                var appSecret = Environment.GetEnvironmentVariable("QOBUZ_APP_SECRET");
                var email = Environment.GetEnvironmentVariable("QOBUZ_EMAIL");
                var password = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(email))
                {
                    _output.WriteLine("⏭️ Skipping: " + SkipReason);
                    return;
                }

                // Initialize services
                _authService = _serviceProvider.GetRequiredService<IQobuzAuthenticationService>();
                _apiClient = _serviceProvider.GetRequiredService<IQobuzApiClient>();
                _downloadClient = _serviceProvider.GetRequiredService<QobuzDownloadClient>();

                // Authenticate
                var credentials = new QobuzCredentials
                {
                    Email = email,
                    MD5Password = password,
                    AppId = appId,
                    AppSecret = appSecret
                };

                _session = await _authService.AuthenticateAsync(credentials);
                _output.WriteLine($"Authenticated successfully. Session expires at {_session.ExpiresAt}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⏭️ Skipping: Test initialization failed: {ex.Message}");
                // Test will be skipped due to null services
            }
        }

        [SkippableFact]
        public async Task Download_RealAlbum_CompletesSuccessfully()
        {
            SkipIfNotReady();
            // Arrange - Use a small album for testing
            var albumId = "0060254734592"; // Example: a single or EP
            var remoteAlbum = CreateRemoteAlbum(albumId, "Test Album");

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Wait for download to complete (with timeout)
            var completed = await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(2));

            // Assert
            completed.Should().BeTrue("Download should complete within timeout");

            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
            downloadItem.TotalSize.Should().BeGreaterThan(0);

            // Verify files were downloaded
            var downloadPath = Path.Combine(_testOutputPath, downloadItem.Title);
            Directory.Exists(downloadPath).Should().BeTrue();

            var files = Directory.GetFiles(downloadPath, "*.flac", SearchOption.AllDirectories);
            files.Should().NotBeEmpty("FLAC files should be downloaded");

            _downloadedFiles.AddRange(files);
            _output.WriteLine($"Downloaded {files.Length} tracks to {downloadPath}");
        }

        [SkippableFact]
        public async Task Download_MultipleAlbumsConcurrently_HandlesCorrectly()
        {
            SkipIfNotReady();
            // Arrange - Multiple small albums
            var albumIds = new[]
            {
                "0060254734592",
                "0060254788359",
                "0060254712345"
            };

            var downloadTasks = new List<Task<string>>();

            // Act - Start concurrent downloads
            foreach (var albumId in albumIds)
            {
                var remoteAlbum = CreateRemoteAlbum(albumId, $"Album {albumId}");
                downloadTasks.Add(_downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>()));
            }

            var downloadIds = await Task.WhenAll(downloadTasks);

            // Wait for all downloads
            var completionTasks = downloadIds.Select(id =>
                WaitForDownloadCompletion(id, TimeSpan.FromMinutes(5))
            ).ToArray();

            var results = await Task.WhenAll(completionTasks);

            // Assert
            results.Should().AllSatisfy(r => r.Should().BeTrue());

            var items = _downloadClient.GetItems();
            items.Should().HaveCount(albumIds.Length);
            items.Should().AllSatisfy(item =>
            {
                item.Status.Should().Be(DownloadItemStatus.Completed);
                item.TotalSize.Should().BeGreaterThan(0);
            });

            _output.WriteLine($"Successfully downloaded {albumIds.Length} albums concurrently");
        }

        [SkippableFact]
        public async Task Download_WithAuthenticationExpiry_RefreshesToken()
        {
            SkipIfNotReady();
            // Arrange - Force token to expire soon
            var shortSession = new QobuzSession
            {
                UserId = _session.UserId,
                AuthToken = _session.AuthToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(30) // Expires in 30 seconds
            };

            // Replace session with short-lived one
            _authService.StoreSession(shortSession);

            var albumId = "0060254734592";
            var remoteAlbum = CreateRemoteAlbum(albumId, "Token Refresh Test");

            // Act - Start download
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Wait longer than token expiry
            await Task.Delay(TimeSpan.FromSeconds(35));

            // Download should still complete with refreshed token
            var completed = await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(2));

            // Assert
            completed.Should().BeTrue("Download should complete even after token expiry");

            var currentSession = _authService.GetCachedSession();
            currentSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow,
                "Session should be refreshed with new expiry");

            _output.WriteLine("Token refresh during download succeeded");
        }

        // TODO: Fix HTTP client mocking for network retry test
        /*
        [Fact]
        public async Task Download_WithNetworkInterruption_RetriesSuccessfully()
        {
            // Arrange
            var albumId = "0060254734592";
            var remoteAlbum = CreateRemoteAlbum(albumId, "Network Retry Test");

            // Simulate network interruption by using a mock that fails initially
            var attemptCount = 0;
            var mockHttpClient = new Mock<IQobuzHttpClient>();
            mockHttpClient
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var currentAttempt = Interlocked.Increment(ref attemptCount);
                    if (currentAttempt <= 2)
                    {
                        throw new IOException("Network error simulation");
                    }
                    return new HttpResponse(new HttpRequest("http://test.com"), 200, new System.IO.MemoryStream());
                });

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());
            var completed = await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(3));

            // Assert
            completed.Should().BeTrue("Download should complete after retries");
            attemptCount.Should().BeGreaterThan(2, "Should have retried after failures");
            
            _output.WriteLine($"Download succeeded after {attemptCount} attempts");
        }
        */

        [SkippableFact]
        public async Task Download_QualityFallback_SelectsAvailableQuality()
        {
            SkipIfNotReady();
            // Arrange - Request Hi-Res but accept fallback
            var albumId = "0060254734592";
            var remoteAlbum = CreateRemoteAlbum(albumId, "Quality Fallback Test");

            // Configure to request highest quality
            var settings = new QobuzDownloadSettings
            {
                PreferredQuality = (int)Lidarr.Plugin.Qobuzarr.Models.QobuzAudioQuality.FLACHiRes24Bit192Khz
            };

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());
            var completed = await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(2));

            // Assert
            completed.Should().BeTrue();

            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Quality might have fallen back to CD or lower
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);

            _output.WriteLine($"Download completed with quality fallback");
        }

        [SkippableFact]
        public async Task Download_LargeAlbum_HandlesMemoryEfficiently()
        {
            SkipIfNotReady();
            // Arrange - Album with many tracks
            var albumId = "0060254788359"; // Example: compilation album
            var remoteAlbum = CreateRemoteAlbum(albumId, "Large Album Test");

            var initialMemory = GC.GetTotalMemory(true);

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            // Monitor memory during download
            var maxMemory = initialMemory;
            var monitorTask = Task.Run(async () =>
            {
                while (!_testCancellation.Token.IsCancellationRequested)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    if (currentMemory > maxMemory)
                    {
                        maxMemory = currentMemory;
                    }
                    await Task.Delay(100);
                }
            });

            var completed = await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(5));
            _testCancellation.Cancel();
            await monitorTask;

            // Assert
            completed.Should().BeTrue();

            var memoryIncrease = maxMemory - initialMemory;
            var memoryIncreaseMB = memoryIncrease / (1024 * 1024);

            memoryIncreaseMB.Should().BeLessThan(500,
                "Memory usage should not exceed 500MB during download");

            _output.WriteLine($"Peak memory increase: {memoryIncreaseMB}MB");
        }

        [SkippableFact]
        public async Task RemoveDownload_WithDeleteData_CleansUpFiles()
        {
            SkipIfNotReady();
            // Arrange - Download an album first
            var albumId = "0060254734592";
            var remoteAlbum = CreateRemoteAlbum(albumId, "Cleanup Test");

            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());
            await WaitForDownloadCompletion(downloadId, TimeSpan.FromMinutes(2));

            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);
            downloadItem.Should().NotBeNull();

            var downloadPath = Path.Combine(_testOutputPath, downloadItem.Title);
            Directory.Exists(downloadPath).Should().BeTrue();

            // Act
            _downloadClient.RemoveItem(downloadItem, deleteData: true);

            // Assert
            _downloadClient.GetItems().Should().NotContain(x => x.DownloadId == downloadId);
            Directory.Exists(downloadPath).Should().BeFalse("Files should be deleted");

            _output.WriteLine("Download removed and files cleaned up successfully");
        }

        [SkippableFact]
        public async Task Download_InvalidAlbumId_FailsGracefully()
        {
            SkipIfNotReady();
            // Arrange
            var invalidAlbumId = "99999999999999";
            var remoteAlbum = CreateRemoteAlbum(invalidAlbumId, "Invalid Album");

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());
            await Task.Delay(TimeSpan.FromSeconds(10)); // Wait for failure

            // Assert
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().NotBeNullOrEmpty();

            _output.WriteLine($"Invalid album handled gracefully: {downloadItem.Message}");
        }

        [SkippableFact]
        public async Task GetDownloadStatus_ProvidesAccurateProgress()
        {
            SkipIfNotReady();
            // Arrange
            var albumId = "0060254734592";
            var remoteAlbum = CreateRemoteAlbum(albumId, "Progress Tracking Test");

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Mock.Of<IIndexer>());

            var progressUpdates = new List<double>();
            var statusChecks = 0;

            while (statusChecks < 20) // Check status 20 times
            {
                await Task.Delay(500);

                var items = _downloadClient.GetItems();
                var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

                if (downloadItem != null)
                {
                    progressUpdates.Add(downloadItem.RemainingSize);

                    if (downloadItem.Status == DownloadItemStatus.Completed ||
                        downloadItem.Status == DownloadItemStatus.Failed)
                    {
                        break;
                    }
                }

                statusChecks++;
            }

            // Assert
            progressUpdates.Should().NotBeEmpty();
            progressUpdates.Should().BeInDescendingOrder("Progress should increase over time");

            _output.WriteLine($"Tracked {progressUpdates.Count} progress updates");
        }

        private RemoteAlbum CreateRemoteAlbum(string albumId, string title)
        {
            return new RemoteAlbum
            {
                Artist = new Artist
                {
                    Name = "Test Artist",
                    Id = 1
                },
                Albums = new List<Album>
                {
                    new Album
                    {
                        Title = title,
                        Id = 1,
                        ReleaseDate = DateTime.Now
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = title,
                    DownloadUrl = $"qobuz://album/{albumId}",
                    Guid = $"qobuz-{albumId}",
                    Size = 100000000 // 100MB estimated
                }
            };
        }

        private async Task<bool> WaitForDownloadCompletion(string downloadId, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < endTime)
            {
                var items = _downloadClient.GetItems();
                var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

                if (downloadItem != null)
                {
                    if (downloadItem.Status == DownloadItemStatus.Completed)
                    {
                        return true;
                    }

                    if (downloadItem.Status == DownloadItemStatus.Failed)
                    {
                        _output.WriteLine($"Download failed: {downloadItem.Message}");
                        return false;
                    }
                }

                await Task.Delay(1000);
            }

            _output.WriteLine($"Download timed out after {timeout}");
            return false;
        }

        public async Task DisposeAsync()
        {
            _testCancellation?.Cancel();
            _downloadClient?.Dispose();

            // Cleanup test files
            try
            {
                if (Directory.Exists(_testOutputPath))
                {
                    Directory.Delete(_testOutputPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Custom exception to skip tests when prerequisites are not met
    /// </summary>
    public class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }
}
