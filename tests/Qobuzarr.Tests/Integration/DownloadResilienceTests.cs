using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NzbDrone.Core.Download;
using Qobuzarr.Download.Clients;
using Qobuzarr.Download.Models;
using Qobuzarr.Download.Services;
using Qobuzarr.Tests.Fixtures;
using Xunit;

namespace Qobuzarr.Tests.Integration
{
    [Collection("Integration")]
    public class DownloadResilienceTests : IntegrationTestBase
    {
        private readonly QobuzDownloadClient _downloadClient;
        private readonly IDownloadService _downloadService;
        private readonly Mock<ILogger<QobuzDownloadClient>> _loggerMock;
        private readonly string _testDownloadPath;

        public DownloadResilienceTests()
        {
            _loggerMock = new Mock<ILogger<QobuzDownloadClient>>();
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            _downloadClient = provider.GetRequiredService<QobuzDownloadClient>();
            _downloadService = provider.GetRequiredService<IDownloadService>();
            _testDownloadPath = Path.Combine(Path.GetTempPath(), "QobuzarrTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDownloadPath);
        }

        [Theory]
        [InlineData(0.25)] // 25% complete
        [InlineData(0.50)] // 50% complete
        [InlineData(0.90)] // 90% complete
        public async Task Should_ResumeDownload_AfterInterruption(double progressBeforeInterruption)
        {
            // Arrange
            var trackId = "test_track_123";
            var totalSize = 50_000_000; // 50MB file
            var downloadedBytes = (long)(totalSize * progressBeforeInterruption);
            var partialFilePath = Path.Combine(_testDownloadPath, $"{trackId}.flac.part");
            
            // Create partial file simulating interrupted download
            await CreatePartialFile(partialFilePath, downloadedBytes);

            // Act
            var downloadRequest = new DownloadRequest
            {
                TrackId = trackId,
                OutputPath = _testDownloadPath,
                ResumeIfExists = true
            };

            var result = await _downloadService.DownloadTrackAsync(downloadRequest);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.BytesDownloaded.Should().BeApproximately(totalSize - downloadedBytes, 1000);
            result.TotalBytes.Should().Be(totalSize);
            
            // Verify file integrity
            var finalFilePath = Path.Combine(_testDownloadPath, $"{trackId}.flac");
            File.Exists(finalFilePath).Should().BeTrue();
            new FileInfo(finalFilePath).Length.Should().Be(totalSize);
            
            // Verify partial file was cleaned up
            File.Exists(partialFilePath).Should().BeFalse();
        }

        [Fact]
        public async Task Should_HandleNetworkTimeout_GracefullyDuringDownload()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var callCount = 0;

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount <= 2)
                    {
                        // First two attempts timeout
                        throw new TaskCanceledException("Network timeout");
                    }
                    
                    // Third attempt succeeds
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(GenerateTestAudioData(1000000))
                    };
                });

            var httpClient = new HttpClient(mockHandler.Object)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var downloadService = new DownloadService(httpClient, _loggerMock.Object);

            // Act
            var result = await downloadService.DownloadWithRetryAsync(
                "https://test.qobuz.com/track.flac",
                _testDownloadPath,
                maxRetries: 3);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            callCount.Should().Be(3, "should retry twice after timeouts");
            
            // Verify appropriate backoff was applied
            VerifyExponentialBackoff();
        }

        [Fact]
        public async Task Should_ValidatePartialFile_BeforeResume()
        {
            // Arrange
            var trackId = "test_track_456";
            var partialFilePath = Path.Combine(_testDownloadPath, $"{trackId}.flac.part");
            var metadataPath = $"{partialFilePath}.metadata";
            
            // Create corrupted partial file
            await CreateCorruptedPartialFile(partialFilePath, 10_000_000);
            
            // Create metadata file with checksum
            var metadata = new DownloadMetadata
            {
                TrackId = trackId,
                BytesDownloaded = 10_000_000,
                LastChecksum = "invalid_checksum",
                LastModified = DateTime.UtcNow.AddMinutes(-5)
            };
            await SaveMetadata(metadataPath, metadata);

            // Act
            var downloadRequest = new DownloadRequest
            {
                TrackId = trackId,
                OutputPath = _testDownloadPath,
                ResumeIfExists = true,
                ValidateBeforeResume = true
            };

            var result = await _downloadService.DownloadTrackAsync(downloadRequest);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.ValidationFailed.Should().BeTrue("corrupted file should fail validation");
            result.StartedFromBeginning.Should().BeTrue("should restart download after validation failure");
            
            // Verify corrupted partial was deleted and download restarted
            File.Exists(partialFilePath).Should().BeFalse();
            File.Exists(metadataPath).Should().BeFalse();
        }

        [Fact]
        public async Task Should_CleanupCorruptedDownloads()
        {
            // Arrange
            var corruptedFiles = new[]
            {
                Path.Combine(_testDownloadPath, "track1.flac.part"),
                Path.Combine(_testDownloadPath, "track2.flac.part"),
                Path.Combine(_testDownloadPath, "track3.flac.corrupt")
            };

            foreach (var file in corruptedFiles)
            {
                await CreateCorruptedPartialFile(file, Random.Shared.Next(1000, 100000));
            }

            // Act
            var cleanupResult = await _downloadService.CleanupCorruptedDownloadsAsync(_testDownloadPath);

            // Assert
            cleanupResult.Should().NotBeNull();
            cleanupResult.FilesRemoved.Should().Be(3);
            cleanupResult.BytesRecovered.Should().BeGreaterThan(0);
            
            // Verify files were actually deleted
            foreach (var file in corruptedFiles)
            {
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public async Task Should_HandleStreamUrlExpiration_DuringDownload()
        {
            // Arrange
            var trackId = "test_track_789";
            var initialStreamUrl = "https://stream.qobuz.com/track.flac?token=initial";
            var refreshedStreamUrl = "https://stream.qobuz.com/track.flac?token=refreshed";
            
            var mockHandler = new Mock<HttpMessageHandler>();
            var urlUsed = initialStreamUrl;
            
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri.ToString() == initialStreamUrl && 
                        DateTime.UtcNow > TestStartTime.AddSeconds(30))
                    {
                        // URL expired after 30 seconds
                        return new HttpResponseMessage(HttpStatusCode.Forbidden);
                    }
                    
                    if (request.RequestUri.ToString() == refreshedStreamUrl)
                    {
                        // New URL works
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(GenerateTestAudioData(5000000))
                        };
                    }
                    
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(GenerateTestAudioData(1000000))
                    };
                });

            // Act - Start download that will span URL expiration
            var downloadTask = _downloadService.DownloadTrackAsync(new DownloadRequest
            {
                TrackId = trackId,
                StreamUrl = initialStreamUrl,
                OutputPath = _testDownloadPath,
                HandleUrlExpiration = true
            });

            // Simulate URL expiration during download
            await Task.Delay(31000);

            var result = await downloadTask;

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.StreamUrlRefreshed.Should().BeTrue("should detect and handle URL expiration");
            
            // Verify new URL was obtained and used
            VerifyStreamUrlRefresh(trackId);
        }

        [Theory]
        [InlineData(1, 1)]     // Single chunk failure
        [InlineData(3, 10)]    // Multiple chunk failures
        [InlineData(5, 100)]   // Many chunk failures
        public async Task Should_RetryFailedChunks_InChunkedDownload(int failuresPerChunk, int totalChunks)
        {
            // Arrange
            var trackId = "test_track_chunked";
            var chunkSize = 1_000_000; // 1MB chunks
            var chunkFailures = new Dictionary<int, int>();

            var mockHandler = CreateChunkedDownloadMockHandler(
                totalChunks, 
                chunkSize, 
                failuresPerChunk, 
                chunkFailures);

            // Act
            var result = await _downloadService.DownloadTrackChunkedAsync(new ChunkedDownloadRequest
            {
                TrackId = trackId,
                OutputPath = _testDownloadPath,
                ChunkSize = chunkSize,
                MaxRetries = failuresPerChunk + 1,
                ParallelChunks = 4
            });

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.TotalChunks.Should().Be(totalChunks);
            result.FailedChunks.Should().Be(0, "all chunks should eventually succeed");
            result.RetryCount.Should().BeGreaterThan(0);
            
            // Verify all chunks were eventually downloaded
            var finalFile = Path.Combine(_testDownloadPath, $"{trackId}.flac");
            new FileInfo(finalFile).Length.Should().Be(totalChunks * chunkSize);
        }

        [Fact]
        public async Task Should_RecoverFrom_PartialChunkCorruption()
        {
            // Arrange
            var trackId = "test_track_corruption";
            var chunkPath = Path.Combine(_testDownloadPath, $"{trackId}.chunk.2");
            
            // Simulate partial chunk corruption
            await CreateCorruptedPartialFile(chunkPath, 500_000);

            // Act
            var result = await _downloadService.RecoverCorruptedChunkAsync(new ChunkRecoveryRequest
            {
                TrackId = trackId,
                ChunkIndex = 2,
                ChunkPath = chunkPath,
                ExpectedSize = 1_000_000,
                ExpectedChecksum = "valid_checksum"
            });

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Redownloaded.Should().BeTrue();
            result.BytesRecovered.Should().Be(1_000_000);
            
            // Verify chunk is now valid
            File.Exists(chunkPath).Should().BeTrue();
            new FileInfo(chunkPath).Length.Should().Be(1_000_000);
        }

        [Fact]
        public async Task Should_MaintainDownloadProgress_AcrossRestarts()
        {
            // Arrange
            var trackId = "test_track_restart";
            var progressFile = Path.Combine(_testDownloadPath, $"{trackId}.progress");
            
            // Simulate previous download progress
            var previousProgress = new DownloadProgress
            {
                TrackId = trackId,
                BytesDownloaded = 25_000_000,
                TotalBytes = 50_000_000,
                ChunksCompleted = new[] { 0, 1, 2, 3, 4 },
                LastUpdate = DateTime.UtcNow.AddMinutes(-10)
            };
            await SaveProgress(progressFile, previousProgress);

            // Act - Resume download
            var result = await _downloadService.ResumeDownloadAsync(new ResumeRequest
            {
                TrackId = trackId,
                OutputPath = _testDownloadPath,
                ProgressFile = progressFile
            });

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.ResumedFrom.Should().Be(25_000_000);
            result.TotalBytes.Should().Be(50_000_000);
            result.ChunksSkipped.Should().Be(5, "should skip already completed chunks");
        }

        [Fact]
        public async Task Should_HandleConcurrentChunkDownloads_WithFailures()
        {
            // Arrange
            var trackId = "test_track_concurrent";
            var totalChunks = 20;
            var concurrentLimit = 5;
            var randomFailures = GenerateRandomChunkFailures(totalChunks, failureRate: 0.2);

            // Act
            var result = await _downloadService.DownloadTrackChunkedAsync(new ChunkedDownloadRequest
            {
                TrackId = trackId,
                OutputPath = _testDownloadPath,
                TotalChunks = totalChunks,
                ParallelChunks = concurrentLimit,
                ChunkFailureSimulation = randomFailures,
                MaxRetries = 3
            });

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.TotalChunks.Should().Be(totalChunks);
            result.MaxConcurrentChunks.Should().BeLessOrEqualTo(concurrentLimit);
            
            // Verify all chunks downloaded despite failures
            for (int i = 0; i < totalChunks; i++)
            {
                var chunkFile = Path.Combine(_testDownloadPath, $"{trackId}.chunk.{i}");
                File.Exists(chunkFile).Should().BeTrue($"chunk {i} should exist");
            }
        }

        [Fact]
        public async Task Should_ApplyBandwidthThrottling_WhenConfigured()
        {
            // Arrange
            var maxBandwidthMBps = 5; // 5 MB/s limit
            var fileSize = 50_000_000; // 50MB file
            var expectedMinDuration = TimeSpan.FromSeconds(fileSize / (maxBandwidthMBps * 1_000_000));

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _downloadService.DownloadTrackAsync(new DownloadRequest
            {
                TrackId = "test_track_throttled",
                OutputPath = _testDownloadPath,
                MaxBandwidthMBps = maxBandwidthMBps,
                FileSize = fileSize
            });
            stopwatch.Stop();

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            stopwatch.Elapsed.Should().BeGreaterThan(expectedMinDuration.Subtract(TimeSpan.FromSeconds(1)),
                "download should be throttled to configured bandwidth");
            result.AverageSpeedMBps.Should().BeLessOrEqualTo(maxBandwidthMBps * 1.1, 
                "average speed should not significantly exceed limit");
        }

        private async Task CreatePartialFile(string path, long size)
        {
            var data = new byte[size];
            Random.Shared.NextBytes(data);
            await File.WriteAllBytesAsync(path, data);
        }

        private async Task CreateCorruptedPartialFile(string path, long size)
        {
            var data = new byte[size];
            Random.Shared.NextBytes(data);
            // Corrupt the data by zeroing out random sections
            for (int i = 0; i < 10; i++)
            {
                var offset = Random.Shared.Next(0, (int)size - 1000);
                Array.Clear(data, offset, Math.Min(1000, (int)size - offset));
            }
            await File.WriteAllBytesAsync(path, data);
        }

        private byte[] GenerateTestAudioData(int size)
        {
            var data = new byte[size];
            Random.Shared.NextBytes(data);
            // Add FLAC header signature
            if (size >= 4)
            {
                data[0] = 0x66; // 'f'
                data[1] = 0x4C; // 'L'
                data[2] = 0x61; // 'a'
                data[3] = 0x43; // 'C'
            }
            return data;
        }

        private Mock<HttpMessageHandler> CreateChunkedDownloadMockHandler(
            int totalChunks, 
            int chunkSize, 
            int failuresPerChunk,
            Dictionary<int, int> chunkFailures)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    // Parse chunk index from request
                    var chunkIndex = ExtractChunkIndex(request);
                    
                    if (!chunkFailures.ContainsKey(chunkIndex))
                        chunkFailures[chunkIndex] = 0;
                    
                    if (chunkFailures[chunkIndex] < failuresPerChunk)
                    {
                        chunkFailures[chunkIndex]++;
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    }
                    
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(GenerateTestAudioData(chunkSize))
                    };
                });
            
            return mockHandler;
        }

        private int ExtractChunkIndex(HttpRequestMessage request)
        {
            // Extract chunk index from request headers or URL
            if (request.Headers.TryGetValues("X-Chunk-Index", out var values))
            {
                return int.Parse(values.First());
            }
            return 0;
        }

        private Dictionary<int, bool> GenerateRandomChunkFailures(int totalChunks, double failureRate)
        {
            var failures = new Dictionary<int, bool>();
            for (int i = 0; i < totalChunks; i++)
            {
                failures[i] = Random.Shared.NextDouble() < failureRate;
            }
            return failures;
        }

        private async Task SaveMetadata(string path, DownloadMetadata metadata)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(metadata);
            await File.WriteAllTextAsync(path, json);
        }

        private async Task SaveProgress(string path, DownloadProgress progress)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(progress);
            await File.WriteAllTextAsync(path, json);
        }

        private void VerifyExponentialBackoff()
        {
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retry attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeast(2));
        }

        private void VerifyStreamUrlRefresh(string trackId)
        {
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Refreshing stream URL for {trackId}")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }

        public override void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDownloadPath))
                {
                    Directory.Delete(_testDownloadPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
            base.Dispose();
        }
    }

    public class DownloadMetadata
    {
        public string TrackId { get; set; }
        public long BytesDownloaded { get; set; }
        public string LastChecksum { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class DownloadProgress
    {
        public string TrackId { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public int[] ChunksCompleted { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class ChunkedDownloadRequest : DownloadRequest
    {
        public int TotalChunks { get; set; }
        public int ChunkSize { get; set; }
        public int ParallelChunks { get; set; }
        public Dictionary<int, bool> ChunkFailureSimulation { get; set; }
    }

    public class ChunkRecoveryRequest
    {
        public string TrackId { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkPath { get; set; }
        public long ExpectedSize { get; set; }
        public string ExpectedChecksum { get; set; }
    }

    public class ResumeRequest
    {
        public string TrackId { get; set; }
        public string OutputPath { get; set; }
        public string ProgressFile { get; set; }
    }
}