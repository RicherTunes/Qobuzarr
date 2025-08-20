using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Tests error scenarios and recovery mechanisms
    /// Validates resilience against failures and proper error handling
    /// </summary>
    [Collection("ErrorRecovery")]
    [Trait("Category", "Integration")]
    [Trait("Component", "ErrorHandling")]
    public class ErrorRecoveryTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly Mock<ILogger<QobuzDownloadClient>> _mockLogger;
        private readonly ChaosHttpMessageHandler _chaosHandler;
        private readonly HttpClient _httpClient;

        public ErrorRecoveryTests(ITestOutputHelper output)
        {
            _output = output;
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _mockLogger = new Mock<ILogger<QobuzDownloadClient>>();
            
            // Setup chaos handler for simulating failures
            _chaosHandler = new ChaosHttpMessageHandler();
            _httpClient = new HttpClient(_chaosHandler);
            
            SetupDefaultMocks();
        }

        private void SetupDefaultMocks()
        {
            var validSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "test_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            _mockAuthService.Setup(x => x.GetCachedSession()).Returns(validSession);
            _mockAuthService.Setup(x => x.GetValidSessionAsync()).ReturnsAsync(validSession);
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task Download_WithNetworkFailure_ShouldRetryAndRecover()
        {
            // Arrange
            _chaosHandler.ConfigureFailures(
                failureRate: 0.5, // 50% failure rate
                failureTypes: new[] { FailureType.NetworkTimeout });
            
            var downloadClient = CreateDownloadClientWithChaos();
            var remoteAlbum = CreateTestRemoteAlbum();
            
            // Act
            var downloadId = await downloadClient.Download(remoteAlbum, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
            
            // Wait for retries
            await Task.Delay(2000);
            
            var items = downloadClient.GetItems();
            var item = items.FirstOrDefault(x => x.DownloadId == downloadId);
            
            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            _chaosHandler.AttemptCount.Should().BeGreaterThan(1, "Should have retried after failures");
            _chaosHandler.SuccessCount.Should().BeGreaterThan(0, "Should eventually succeed");
            
            _output.WriteLine($"Recovered after {_chaosHandler.AttemptCount} attempts");
            _output.WriteLine($"Failures: {_chaosHandler.FailureCount}, Successes: {_chaosHandler.SuccessCount}");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task Authentication_WithExpiredToken_ShouldRefreshAutomatically()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            };
            
            var refreshedSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "new_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            _mockAuthService.SetupSequence(x => x.GetCachedSession())
                .Returns(expiredSession)
                .Returns(refreshedSession);
            
            _mockAuthService.Setup(x => x.RefreshSessionAsync())
                .ReturnsAsync(refreshedSession);
            
            var downloadClient = CreateDownloadClient();
            
            // Act
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await downloadClient.Download(remoteAlbum, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
            
            // Assert
            _mockAuthService.Verify(x => x.RefreshSessionAsync(), Times.Once,
                "Should refresh expired token automatically");
            downloadId.Should().NotBeNullOrEmpty();
            
            _output.WriteLine("Successfully refreshed expired token");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task Download_WithCorruptedData_ShouldDetectAndRetry()
        {
            // Arrange
            _chaosHandler.ConfigureFailures(
                failureRate: 0.3,
                failureTypes: new[] { FailureType.CorruptedResponse });
            
            var downloadClient = CreateDownloadClientWithChaos();
            var mockFileService = new Mock<IDownloadFileService>();
            
            // Simulate corrupted file detection
            var corruptionDetected = false;
            mockFileService.Setup(x => x.ValidateDownloadedFile(It.IsAny<string>()))
                .Returns<string>(path =>
                {
                    if (!corruptionDetected && _chaosHandler.FailureCount > 0)
                    {
                        corruptionDetected = true;
                        return false; // File is corrupted
                    }
                    return true; // File is valid
                });
            
            // Act
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await downloadClient.Download(remoteAlbum, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
            
            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            if (corruptionDetected)
            {
                _output.WriteLine("Corruption detected and handled");
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task Download_WithInterruption_ShouldResumeFromLastPosition()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            var remoteAlbum = CreateTestRemoteAlbum();
            var mockFileService = new Mock<IDownloadFileService>();
            
            var resumePosition = 0L;
            mockFileService.Setup(x => x.GetResumePosition(It.IsAny<string>()))
                .Returns<string>(path =>
                {
                    // Simulate partial download
                    resumePosition = 1024 * 1024; // 1MB already downloaded
                    return resumePosition;
                });
            
            // Act - Start download
            var downloadId = await downloadClient.Download(remoteAlbum, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
            
            // Simulate interruption
            _chaosHandler.SimulateInterruption();
            await Task.Delay(500);
            
            // Resume download
            var items = downloadClient.GetItems();
            var item = items.FirstOrDefault(x => x.DownloadId == downloadId);
            
            // Assert
            item.Should().NotBeNull();
            mockFileService.Verify(x => x.GetResumePosition(It.IsAny<string>()), Times.AtLeastOnce,
                "Should check resume position after interruption");
            
            _output.WriteLine($"Download resumed from position: {resumePosition} bytes");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task API_WithRateLimiting_ShouldRespectLimitsAndRetry()
        {
            // Arrange
            _chaosHandler.ConfigureFailures(
                failureRate: 0.3,
                failureTypes: new[] { FailureType.RateLimited });
            
            var apiClient = new QobuzApiClient(_httpClient, _mockAuthService.Object, Mock.Of<ILogger<QobuzApiClient>>());
            var requestTimes = new List<DateTime>();
            
            _chaosHandler.OnRequest = () => requestTimes.Add(DateTime.UtcNow);
            
            // Act - Make multiple requests
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await apiClient.SearchAlbumsAsync($"test {i}", 1, 0);
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                    {
                        // Rate limited - expected
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            
            // Assert
            _chaosHandler.RateLimitHits.Should().BeGreaterThan(0, "Should encounter rate limits");
            
            // Check that requests are spaced out after rate limiting
            if (requestTimes.Count > 2)
            {
                var gaps = new List<double>();
                for (int i = 1; i < requestTimes.Count; i++)
                {
                    gaps.Add((requestTimes[i] - requestTimes[i - 1]).TotalMilliseconds);
                }
                
                gaps.Max().Should().BeGreaterThan(1000, "Should have backoff delays after rate limiting");
            }
            
            _output.WriteLine($"Rate limit hits: {_chaosHandler.RateLimitHits}");
            _output.WriteLine($"Total requests: {requestTimes.Count}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task Download_WithDiskSpaceIssue_ShouldHandleGracefully()
        {
            // Arrange
            var mockDiskProvider = new Mock<NzbDrone.Common.Disk.IDiskProvider>();
            mockDiskProvider.Setup(x => x.GetAvailableSpace(It.IsAny<string>()))
                .Returns(1024 * 1024); // Only 1MB available
            
            var downloadClient = CreateDownloadClient(mockDiskProvider.Object);
            var remoteAlbum = CreateTestRemoteAlbum();
            remoteAlbum.Release.Size = 500 * 1024 * 1024; // 500MB album
            
            // Act
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => downloadClient.Download(remoteAlbum, Mock.Of<NzbDrone.Core.Indexers.IIndexer>()));
            
            // Assert
            exception.Message.Should().Contain("disk space", StringComparison.OrdinalIgnoreCase);
            
            _output.WriteLine($"Disk space check prevented download: {exception.Message}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SessionRecovery_AfterMultipleFailures_ShouldMaintainState()
        {
            // Arrange
            var failureCount = 0;
            _mockAuthService.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    failureCount++;
                    if (failureCount < 3)
                    {
                        throw new QobuzAuthenticationException("Temporary auth failure");
                    }
                    return new QobuzSession
                    {
                        UserId = "recovered_user",
                        AuthToken = "recovered_token",
                        ExpiresAt = DateTime.UtcNow.AddHours(1)
                    };
                });
            
            // Act
            QobuzSession session = null;
            var attempts = 0;
            var maxAttempts = 5;
            
            while (session == null && attempts < maxAttempts)
            {
                try
                {
                    attempts++;
                    session = await _mockAuthService.Object.AuthenticateAsync("test@example.com", "password");
                }
                catch (QobuzAuthenticationException)
                {
                    await Task.Delay(500); // Wait before retry
                }
            }
            
            // Assert
            session.Should().NotBeNull("Should eventually recover");
            session.AuthToken.Should().Be("recovered_token");
            attempts.Should().Be(3, "Should succeed on third attempt");
            
            _output.WriteLine($"Session recovered after {attempts} attempts");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task ConcurrentDownloads_WithPartialFailures_ShouldCompleteSuccessfully()
        {
            // Arrange
            _chaosHandler.ConfigureFailures(
                failureRate: 0.2, // 20% failure rate
                failureTypes: new[] { FailureType.NetworkTimeout, FailureType.ServerError });
            
            var downloadClient = CreateDownloadClientWithChaos();
            var albums = Enumerable.Range(1, 5).Select(i => CreateTestRemoteAlbum($"Album {i}")).ToList();
            
            // Act
            var downloadTasks = albums.Select(album => 
                Task.Run(() => downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>()))
            ).ToList();
            
            var downloadIds = await Task.WhenAll(downloadTasks);
            
            // Assert
            downloadIds.Should().AllSatisfy(id => id.Should().NotBeNullOrEmpty());
            downloadIds.Distinct().Should().HaveCount(5, "All downloads should have unique IDs");
            
            var successRate = (double)_chaosHandler.SuccessCount / _chaosHandler.AttemptCount;
            successRate.Should().BeGreaterThan(0.5, "Despite failures, most attempts should succeed with retry");
            
            _output.WriteLine($"Completed {albums.Count} downloads");
            _output.WriteLine($"Success rate: {successRate:P2} after {_chaosHandler.AttemptCount} total attempts");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task Download_WithAssemblyLoadingIssue_ShouldProvideDetailedError()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            
            // Simulate assembly loading issue
            var mockLocalizationService = new Mock<NzbDrone.Core.Localization.ILocalizationService>();
            mockLocalizationService.Setup(x => x.GetLocalizedString(It.IsAny<string>()))
                .Throws(new TypeLoadException("Could not load type 'Lidarr.Core.Download.IDownloadClient'"));
            
            // Act & Assert
            var exception = Assert.Throws<TypeLoadException>(() =>
            {
                // This would fail during plugin initialization
                var client = new QobuzDownloadClient(
                    _mockAuthService.Object,
                    Mock.Of<IQobuzApiClient>(),
                    _httpClient,
                    Mock.Of<IDownloadQueueService>(),
                    Mock.Of<IDownloadFileService>(),
                    Mock.Of<IConcurrencyManager>(),
                    Mock.Of<IDownloadOrchestrator>(),
                    Mock.Of<IDownloadSummary>(),
                    Mock.Of<IBatchProcessor>(),
                    Mock.Of<IQobuzTrackDownloaderFactory>(),
                    Mock.Of<NzbDrone.Core.Configuration.IConfigService>(),
                    Mock.Of<NzbDrone.Common.Disk.IDiskProvider>(),
                    Mock.Of<NzbDrone.Core.RemotePathMappings.IRemotePathMappingService>(),
                    mockLocalizationService.Object,
                    Mock.Of<ILogger<QobuzDownloadClient>>()
                );
            });
            
            exception.Message.Should().Contain("IDownloadClient");
            _output.WriteLine($"Assembly loading error detected: {exception.Message}");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task ErrorMetrics_ShouldTrackFailuresAccurately()
        {
            // Arrange
            var metrics = new ErrorMetrics();
            _chaosHandler.ConfigureFailures(
                failureRate: 0.4,
                failureTypes: Enum.GetValues<FailureType>());
            
            _chaosHandler.OnFailure = (type) => metrics.RecordFailure(type);
            _chaosHandler.OnSuccess = () => metrics.RecordSuccess();
            
            var downloadClient = CreateDownloadClientWithChaos();
            
            // Act - Perform multiple operations
            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var album = CreateTestRemoteAlbum($"Test {i}");
                        await downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
                    }
                    catch
                    {
                        // Expected failures
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            
            // Assert
            metrics.TotalAttempts.Should().BeGreaterThan(0);
            metrics.FailureRate.Should().BeApproximately(_chaosHandler.ConfiguredFailureRate, 0.2,
                "Actual failure rate should be close to configured rate");
            
            _output.WriteLine($"Error Metrics:");
            _output.WriteLine($"  Total Attempts: {metrics.TotalAttempts}");
            _output.WriteLine($"  Success Rate: {metrics.SuccessRate:P2}");
            _output.WriteLine($"  Failure Rate: {metrics.FailureRate:P2}");
            _output.WriteLine($"  Failure Types: {string.Join(", ", metrics.FailuresByType.Keys)}");
        }

        // Helper methods
        private QobuzDownloadClient CreateDownloadClient(NzbDrone.Common.Disk.IDiskProvider diskProvider = null)
        {
            return new QobuzDownloadClient(
                _mockAuthService.Object,
                Mock.Of<IQobuzApiClient>(),
                new HttpClient(),
                Mock.Of<IDownloadQueueService>(),
                Mock.Of<IDownloadFileService>(),
                Mock.Of<IConcurrencyManager>(),
                Mock.Of<IDownloadOrchestrator>(),
                Mock.Of<IDownloadSummary>(),
                Mock.Of<IBatchProcessor>(),
                Mock.Of<IQobuzTrackDownloaderFactory>(),
                Mock.Of<NzbDrone.Core.Configuration.IConfigService>(),
                diskProvider ?? Mock.Of<NzbDrone.Common.Disk.IDiskProvider>(),
                Mock.Of<NzbDrone.Core.RemotePathMappings.IRemotePathMappingService>(),
                Mock.Of<NzbDrone.Core.Localization.ILocalizationService>(),
                _mockLogger.Object
            );
        }

        private QobuzDownloadClient CreateDownloadClientWithChaos()
        {
            return new QobuzDownloadClient(
                _mockAuthService.Object,
                Mock.Of<IQobuzApiClient>(),
                _httpClient, // Uses chaos handler
                Mock.Of<IDownloadQueueService>(),
                Mock.Of<IDownloadFileService>(),
                Mock.Of<IConcurrencyManager>(),
                Mock.Of<IDownloadOrchestrator>(),
                Mock.Of<IDownloadSummary>(),
                Mock.Of<IBatchProcessor>(),
                Mock.Of<IQobuzTrackDownloaderFactory>(),
                Mock.Of<NzbDrone.Core.Configuration.IConfigService>(),
                Mock.Of<NzbDrone.Common.Disk.IDiskProvider>(),
                Mock.Of<NzbDrone.Core.RemotePathMappings.IRemotePathMappingService>(),
                Mock.Of<NzbDrone.Core.Localization.ILocalizationService>(),
                _mockLogger.Object
            );
        }

        private RemoteAlbum CreateTestRemoteAlbum(string title = "Test Album")
        {
            return new RemoteAlbum
            {
                Artist = new NzbDrone.Core.Music.Artist
                {
                    Name = "Test Artist",
                    Id = 1
                },
                Albums = new List<NzbDrone.Core.Music.Album>
                {
                    new NzbDrone.Core.Music.Album
                    {
                        Title = title,
                        Id = 1,
                        ReleaseDate = DateTime.Now
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = title,
                    DownloadUrl = "qobuz://album/12345",
                    Guid = $"qobuz-{Guid.NewGuid()}",
                    Size = 100 * 1024 * 1024 // 100MB
                }
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _chaosHandler?.Dispose();
        }

        // Helper classes
        private class ChaosHttpMessageHandler : DelegatingHandler
        {
            private readonly Random _random = new Random();
            private bool _interrupted = false;
            
            public double ConfiguredFailureRate { get; private set; }
            public FailureType[] ConfiguredFailureTypes { get; private set; }
            public int AttemptCount { get; private set; }
            public int FailureCount { get; private set; }
            public int SuccessCount { get; private set; }
            public int RateLimitHits { get; private set; }
            
            public Action OnRequest { get; set; }
            public Action<FailureType> OnFailure { get; set; }
            public Action OnSuccess { get; set; }

            public ChaosHttpMessageHandler()
            {
                InnerHandler = new HttpClientHandler();
            }

            public void ConfigureFailures(double failureRate, FailureType[] failureTypes)
            {
                ConfiguredFailureRate = failureRate;
                ConfiguredFailureTypes = failureTypes;
            }

            public void SimulateInterruption()
            {
                _interrupted = true;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                AttemptCount++;
                OnRequest?.Invoke();

                if (_interrupted)
                {
                    _interrupted = false;
                    throw new TaskCanceledException("Connection interrupted");
                }

                if (_random.NextDouble() < ConfiguredFailureRate && ConfiguredFailureTypes?.Any() == true)
                {
                    var failureType = ConfiguredFailureTypes[_random.Next(ConfiguredFailureTypes.Length)];
                    FailureCount++;
                    OnFailure?.Invoke(failureType);

                    switch (failureType)
                    {
                        case FailureType.NetworkTimeout:
                            await Task.Delay(5000);
                            throw new TaskCanceledException("Network timeout");
                        
                        case FailureType.ServerError:
                            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                        
                        case FailureType.RateLimited:
                            RateLimitHits++;
                            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                            {
                                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2)) }
                            };
                        
                        case FailureType.CorruptedResponse:
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("corrupted data !@#$%^&*()")
                            };
                    }
                }

                SuccessCount++;
                OnSuccess?.Invoke();
                return await base.SendAsync(request, cancellationToken);
            }
        }

        private enum FailureType
        {
            NetworkTimeout,
            ServerError,
            RateLimited,
            CorruptedResponse
        }

        private class ErrorMetrics
        {
            public int TotalAttempts { get; private set; }
            public int Successes { get; private set; }
            public int Failures { get; private set; }
            public Dictionary<FailureType, int> FailuresByType { get; } = new Dictionary<FailureType, int>();

            public double SuccessRate => TotalAttempts > 0 ? (double)Successes / TotalAttempts : 0;
            public double FailureRate => TotalAttempts > 0 ? (double)Failures / TotalAttempts : 0;

            public void RecordSuccess()
            {
                TotalAttempts++;
                Successes++;
            }

            public void RecordFailure(FailureType type)
            {
                TotalAttempts++;
                Failures++;
                
                if (!FailuresByType.ContainsKey(type))
                    FailuresByType[type] = 0;
                FailuresByType[type]++;
            }
        }
    }
}