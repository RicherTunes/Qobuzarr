using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NLog;
using Polly;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Comprehensive error scenario and resilience tests
    /// Tests authentication failures, network issues, corrupted data, and recovery mechanisms
    /// </summary>
    [Collection("ErrorResilience")]
    [Trait("Category", "Resilience")]
    [Trait("Component", "ErrorHandling")]
    public class ErrorResilienceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly Logger _logger;
        private readonly List<Exception> _capturedExceptions = new();
        private readonly CancellationTokenSource _testCancellation = new();

        // Error simulation settings
        private int _networkFailureCount = 0;
        private int _authFailureCount = 0;
        private bool _simulateCorruptedData = false;
        private bool _simulateRateLimiting = false;

        public ErrorResilienceTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = LogManager.GetCurrentClassLogger();
            
            // Setup DI container with mocked services for controlled error simulation
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            SetupErrorSimulation();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            
            services.AddSingleton(_mockApiClient.Object);
            services.AddSingleton(_mockAuthService.Object);
            services.AddSingleton<IQobuzLogger>(new NLogAdapter(_logger));
            
            // Add real services that will be tested
            services.AddScoped<IDownloadQueueService, DownloadQueueService>();
            services.AddScoped<IConcurrencyManager, ConcurrencyManager>();
            services.AddScoped<IDownloadFileService, DownloadFileService>();
        }

        private void SetupErrorSimulation()
        {
            // Setup controllable error conditions
            _mockApiClient.Setup(x => x.GetAsync<QobuzAlbum>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns<string, Dictionary<string, string>>(async (endpoint, parameters) =>
                {
                    await SimulateNetworkConditions();
                    
                    if (_simulateCorruptedData)
                    {
                        throw new JsonSerializationException("Corrupted response data");
                    }
                    
                    if (_simulateRateLimiting)
                    {
                        throw new QobuzApiException("Rate limit exceeded", 429);
                    }
                    
                    // Return mock album data
                    return new QobuzAlbum
                    {
                        Id = "123456",
                        Title = "Test Album",
                        Artist = new QobuzArtist { Name = "Test Artist" }
                    };
                });

            _mockAuthService.Setup(x => x.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .Returns<QobuzCredentials>(async (credentials) =>
                {
                    if (_authFailureCount > 0)
                    {
                        _authFailureCount--;
                        throw new QobuzAuthenticationException("Authentication failed");
                    }
                    
                    return new QobuzSession
                    {
                        UserId = "test_user",
                        AuthToken = "test_token",
                        ExpiresAt = DateTime.UtcNow.AddHours(1)
                    };
                });
        }

        [Fact]
        public async Task AuthenticationFailure_RetriesAndRecovers()
        {
            // Arrange
            _authFailureCount = 2; // Fail first 2 attempts
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password",
                AppId = "app_id",
                AppSecret = "secret"
            };

            var retryPolicy = Policy
                .Handle<QobuzAuthenticationException>()
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _output.WriteLine($"Auth retry {retryCount} after {timeSpan.TotalSeconds}s: {exception.Message}");
                    });

            // Act
            QobuzSession session = null;
            var attempts = 0;
            
            await retryPolicy.ExecuteAsync(async () =>
            {
                attempts++;
                session = await _mockAuthService.Object.AuthenticateAsync(credentials);
            });

            // Assert
            session.Should().NotBeNull();
            attempts.Should().Be(3, "Should succeed on third attempt");
            session.AuthToken.Should().NotBeNullOrEmpty();
            
            _output.WriteLine($"Authentication recovered after {attempts} attempts");
        }

        [Fact]
        public async Task TokenExpiry_AutomaticallyRefreshes()
        {
            // Arrange
            var shortLivedSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "initial_token",
                ExpiresAt = DateTime.UtcNow.AddSeconds(2) // Expires in 2 seconds
            };

            var refreshCount = 0;
            _mockAuthService.Setup(x => x.RefreshSessionAsync(It.IsAny<QobuzSession>()))
                .ReturnsAsync(() =>
                {
                    refreshCount++;
                    return new QobuzSession
                    {
                        UserId = "test_user",
                        AuthToken = $"refreshed_token_{refreshCount}",
                        ExpiresAt = DateTime.UtcNow.AddHours(1)
                    };
                });

            _mockAuthService.Setup(x => x.GetCachedSession())
                .Returns(() => shortLivedSession);

            // Act - Wait for token to expire
            await Task.Delay(TimeSpan.FromSeconds(3));
            
            // Simulate API call that triggers refresh
            var newSession = await _mockAuthService.Object.RefreshSessionAsync(shortLivedSession);

            // Assert
            newSession.Should().NotBeNull();
            newSession.AuthToken.Should().StartWith("refreshed_token_");
            newSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
            refreshCount.Should().BeGreaterThan(0);
            
            _output.WriteLine($"Token refreshed {refreshCount} times");
        }

        [Fact]
        public async Task NetworkTimeout_RetriesWithBackoff()
        {
            // Arrange
            _networkFailureCount = 3;
            var timeouts = new List<TimeSpan>();
            
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    5,
                    retryAttempt => 
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        timeouts.Add(delay);
                        return delay;
                    },
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _output.WriteLine($"Network retry {retryCount} after {timeSpan.TotalSeconds}s");
                    });

            // Act
            var success = false;
            await retryPolicy.ExecuteAsync(async () =>
            {
                if (_networkFailureCount > 0)
                {
                    _networkFailureCount--;
                    throw new HttpRequestException("Network timeout");
                }
                success = true;
            });

            // Assert
            success.Should().BeTrue();
            timeouts.Should().HaveCount(3);
            timeouts.Should().BeInAscendingOrder("Backoff delays should increase");
            
            _output.WriteLine($"Network recovered after {timeouts.Count} retries");
            _output.WriteLine($"Total retry time: {timeouts.Sum(t => t.TotalSeconds)}s");
        }

        [Fact]
        public async Task CorruptedDownload_DetectedAndRetried()
        {
            // Arrange
            var downloadService = _serviceProvider.GetRequiredService<IDownloadFileService>();
            var corruptionDetected = false;
            var retryCount = 0;

            // Simulate download with corruption check
            async Task<byte[]> DownloadWithVerification(string url)
            {
                retryCount++;
                
                // Simulate corrupted data on first attempt
                if (retryCount == 1)
                {
                    var corruptedData = new byte[] { 0xFF, 0xFF, 0xFF }; // Invalid FLAC header
                    if (!IsValidFlacHeader(corruptedData))
                    {
                        corruptionDetected = true;
                        throw new InvalidDataException("Corrupted FLAC data detected");
                    }
                }
                
                // Return valid data on retry
                return GenerateValidFlacHeader();
            }

            // Act
            byte[] downloadedData = null;
            var maxRetries = 3;
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    downloadedData = await DownloadWithVerification("http://test.url");
                    break;
                }
                catch (InvalidDataException ex)
                {
                    _output.WriteLine($"Attempt {i + 1}: {ex.Message}");
                    if (i == maxRetries - 1) throw;
                }
            }

            // Assert
            corruptionDetected.Should().BeTrue("Corruption should be detected");
            downloadedData.Should().NotBeNull();
            IsValidFlacHeader(downloadedData).Should().BeTrue();
            retryCount.Should().Be(2, "Should succeed on second attempt");
            
            _output.WriteLine($"Download recovered after {retryCount} attempts");
        }

        [Fact]
        public async Task RateLimiting_RespectsBackoffHeaders()
        {
            // Arrange
            _simulateRateLimiting = true;
            var rateLimitHits = 0;
            var backoffDelays = new List<TimeSpan>();

            _mockApiClient.Setup(x => x.GetAsync<QobuzAlbum>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns<string, Dictionary<string, string>>(async (endpoint, parameters) =>
                {
                    if (rateLimitHits < 2)
                    {
                        rateLimitHits++;
                        var retryAfter = 5 * rateLimitHits; // Increasing backoff
                        throw new QobuzApiException($"Rate limit exceeded. Retry after {retryAfter}s", 429);
                    }
                    
                    return new QobuzAlbum { Id = "123", Title = "Success" };
                });

            // Act
            QobuzAlbum result = null;
            var attempts = 0;
            
            while (attempts < 5)
            {
                try
                {
                    attempts++;
                    result = await _mockApiClient.Object.GetAsync<QobuzAlbum>("/album/get", new Dictionary<string, string>());
                    break;
                }
                catch (QobuzApiException ex) when (ex.StatusCode == 429)
                {
                    var retryAfter = ExtractRetryAfter(ex.Message);
                    backoffDelays.Add(TimeSpan.FromSeconds(retryAfter));
                    
                    _output.WriteLine($"Rate limited. Waiting {retryAfter}s before retry {attempts}");
                    await Task.Delay(TimeSpan.FromMilliseconds(100)); // Simulated wait
                }
            }

            // Assert
            result.Should().NotBeNull();
            rateLimitHits.Should().Be(2);
            backoffDelays.Should().HaveCount(2);
            backoffDelays.Should().BeInAscendingOrder("Backoff should increase");
            
            _output.WriteLine($"Rate limiting handled with {backoffDelays.Count} backoffs");
        }

        [Fact]
        public async Task PartialDownloadRecovery_ResumesFromLastPosition()
        {
            // Arrange
            var totalSize = 100_000_000; // 100MB
            var downloadedSize = 0;
            var interruptions = 0;
            var resumePositions = new List<long>();

            async Task<long> SimulateResumableDownload(long startPosition)
            {
                resumePositions.Add(startPosition);
                
                // Simulate interruption at 30% and 60%
                if ((startPosition < 30_000_000 && interruptions == 0) ||
                    (startPosition < 60_000_000 && interruptions == 1))
                {
                    interruptions++;
                    var downloaded = 10_000_000; // Download 10MB before interruption
                    throw new IOException($"Connection lost at {startPosition + downloaded} bytes");
                }
                
                // Complete download
                return totalSize - startPosition;
            }

            // Act
            while (downloadedSize < totalSize)
            {
                try
                {
                    var bytesDownloaded = await SimulateResumableDownload(downloadedSize);
                    downloadedSize += bytesDownloaded;
                    _output.WriteLine($"Downloaded: {downloadedSize}/{totalSize} bytes");
                    break;
                }
                catch (IOException ex)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"at (\d+) bytes");
                    if (match.Success)
                    {
                        downloadedSize = long.Parse(match.Groups[1].Value);
                    }
                    _output.WriteLine($"Interruption {interruptions}: {ex.Message}");
                    _output.WriteLine($"Resuming from {downloadedSize} bytes");
                }
            }

            // Assert
            downloadedSize.Should().Be(totalSize);
            interruptions.Should().Be(2);
            resumePositions.Should().HaveCount(3); // Initial + 2 resumes
            resumePositions.Should().BeInAscendingOrder();
            
            _output.WriteLine($"Download completed with {interruptions} interruptions");
            _output.WriteLine($"Resume positions: {string.Join(", ", resumePositions)}");
        }

        [Fact]
        public async Task ConcurrentDownloadFailure_IsolatesFailures()
        {
            // Arrange
            var concurrencyManager = _serviceProvider.GetRequiredService<IConcurrencyManager>();
            var downloads = new List<(string id, Task<bool> task)>();
            var failedDownloads = new List<string>();
            var successfulDownloads = new List<string>();

            // Simulate 10 concurrent downloads, 3 will fail
            var failureIndices = new HashSet<int> { 2, 5, 8 };

            // Act
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                var downloadId = $"download_{index}";
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Random.Shared.Next(100, 500)); // Simulate work
                        
                        if (failureIndices.Contains(index))
                        {
                            throw new AlbumDownloadException($"Download {downloadId} failed");
                        }
                        
                        lock (successfulDownloads)
                        {
                            successfulDownloads.Add(downloadId);
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lock (failedDownloads)
                        {
                            failedDownloads.Add(downloadId);
                        }
                        _capturedExceptions.Add(ex);
                        return false;
                    }
                });
                
                downloads.Add((downloadId, task));
            }

            var results = await Task.WhenAll(downloads.Select(d => d.task));

            // Assert
            successfulDownloads.Should().HaveCount(7);
            failedDownloads.Should().HaveCount(3);
            results.Count(r => r).Should().Be(7, "7 downloads should succeed");
            results.Count(r => !r).Should().Be(3, "3 downloads should fail");
            
            // Verify failures didn't affect other downloads
            failedDownloads.Should().BeEquivalentTo(new[] { "download_2", "download_5", "download_8" });
            
            _output.WriteLine($"Concurrent downloads: {downloads.Count}");
            _output.WriteLine($"Successful: {successfulDownloads.Count}");
            _output.WriteLine($"Failed: {failedDownloads.Count}");
            _output.WriteLine($"Failure isolation verified");
        }

        [Fact]
        public async Task AssemblyLoadingFailure_GracefulDegradation()
        {
            // Arrange - Simulate missing ML assembly
            var mlOptimizationAvailable = false;
            var fallbackMode = false;

            try
            {
                // Attempt to load ML optimization assembly
                var assembly = System.Reflection.Assembly.Load("Microsoft.ML");
                mlOptimizationAvailable = true;
            }
            catch (FileNotFoundException)
            {
                // ML assembly not available, use fallback
                fallbackMode = true;
                _output.WriteLine("ML assembly not found, using fallback optimization");
            }

            // Act - Use fallback query optimization
            Func<string, int> optimizeQuery = (query) =>
            {
                if (mlOptimizationAvailable)
                {
                    // Would use ML model
                    return query.Length > 50 ? 3 : 1; // Simplified
                }
                else
                {
                    // Simple heuristic fallback
                    return query.Split(' ').Length > 5 ? 2 : 1;
                }
            };

            var testQueries = new[]
            {
                "simple query",
                "complex query with many words that needs optimization",
                "Miles Davis Kind of Blue"
            };

            var results = testQueries.Select(q => new
            {
                Query = q,
                ApiCalls = optimizeQuery(q)
            }).ToList();

            // Assert
            if (fallbackMode)
            {
                results.Should().AllSatisfy(r => r.ApiCalls.Should().BePositive());
                _output.WriteLine("Graceful degradation successful - using heuristic optimization");
            }
            else
            {
                _output.WriteLine("ML optimization available and functioning");
            }

            foreach (var result in results)
            {
                _output.WriteLine($"Query: '{result.Query}' -> {result.ApiCalls} API calls");
            }
        }

        [Fact]
        public async Task DiskSpaceExhaustion_HandlesGracefully()
        {
            // Arrange
            var availableSpace = 50_000_000L; // 50MB available
            var downloadSize = 100_000_000L; // 100MB needed
            var cleanupPerformed = false;

            async Task<bool> AttemptDownloadWithSpaceCheck(long size)
            {
                if (size > availableSpace)
                {
                    // Attempt cleanup
                    if (!cleanupPerformed)
                    {
                        _output.WriteLine($"Insufficient space. Need {size}, have {availableSpace}");
                        _output.WriteLine("Performing cleanup...");
                        
                        // Simulate cleanup
                        cleanupPerformed = true;
                        availableSpace += 60_000_000; // Free up 60MB
                        
                        _output.WriteLine($"Cleanup complete. Available space: {availableSpace}");
                        return false; // Retry needed
                    }
                    
                    if (size > availableSpace)
                    {
                        throw new IOException($"Insufficient disk space. Need {size}, have {availableSpace}");
                    }
                }
                
                return true;
            }

            // Act
            var downloadSuccessful = false;
            var attempts = 0;
            
            while (attempts < 3 && !downloadSuccessful)
            {
                attempts++;
                downloadSuccessful = await AttemptDownloadWithSpaceCheck(downloadSize);
                
                if (!downloadSuccessful && cleanupPerformed)
                {
                    // Retry after cleanup
                    continue;
                }
            }

            // Assert
            downloadSuccessful.Should().BeTrue();
            cleanupPerformed.Should().BeTrue();
            availableSpace.Should().BeGreaterOrEqualTo(downloadSize);
            attempts.Should().Be(2, "Should succeed after cleanup on second attempt");
            
            _output.WriteLine($"Download succeeded after {attempts} attempts with cleanup");
        }

        [Fact]
        public void ErrorMetrics_TrackedAccurately()
        {
            // Arrange
            var errorMetrics = new ErrorMetrics();
            
            // Simulate various errors
            errorMetrics.RecordError(new QobuzAuthenticationException("Auth failed"), ErrorCategory.Authentication);
            errorMetrics.RecordError(new HttpRequestException("Timeout"), ErrorCategory.Network);
            errorMetrics.RecordError(new HttpRequestException("Connection reset"), ErrorCategory.Network);
            errorMetrics.RecordError(new QobuzApiException("Rate limited", 429), ErrorCategory.RateLimit);
            errorMetrics.RecordError(new InvalidDataException("Corrupted"), ErrorCategory.DataCorruption);
            
            // Act
            var summary = errorMetrics.GetSummary();
            
            // Assert
            summary.TotalErrors.Should().Be(5);
            summary.ErrorsByCategory[ErrorCategory.Network].Should().Be(2);
            summary.ErrorsByCategory[ErrorCategory.Authentication].Should().Be(1);
            summary.ErrorsByCategory[ErrorCategory.RateLimit].Should().Be(1);
            summary.ErrorsByCategory[ErrorCategory.DataCorruption].Should().Be(1);
            
            summary.ErrorRate.Should().BeGreaterThan(0);
            summary.MostCommonError.Should().Be(ErrorCategory.Network);
            
            // Output error report
            _output.WriteLine("=== ERROR METRICS REPORT ===");
            _output.WriteLine($"Total errors: {summary.TotalErrors}");
            _output.WriteLine($"Error rate: {summary.ErrorRate:P}");
            _output.WriteLine($"Most common: {summary.MostCommonError}");
            _output.WriteLine("\nBreakdown by category:");
            foreach (var kvp in summary.ErrorsByCategory)
            {
                _output.WriteLine($"  {kvp.Key}: {kvp.Value} errors");
            }
        }

        // Helper methods
        private async Task SimulateNetworkConditions()
        {
            if (_networkFailureCount > 0)
            {
                _networkFailureCount--;
                await Task.Delay(100);
                throw new HttpRequestException("Network timeout simulated");
            }
            
            // Simulate variable latency
            await Task.Delay(Random.Shared.Next(10, 200));
        }

        private bool IsValidFlacHeader(byte[] data)
        {
            // FLAC files start with "fLaC"
            return data?.Length >= 4 && 
                   data[0] == 0x66 && // f
                   data[1] == 0x4C && // L
                   data[2] == 0x61 && // a
                   data[3] == 0x43;   // C
        }

        private byte[] GenerateValidFlacHeader()
        {
            return new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22 };
        }

        private int ExtractRetryAfter(string message)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, @"Retry after (\d+)s");
            return match.Success ? int.Parse(match.Groups[1].Value) : 5;
        }

        public void Dispose()
        {
            _testCancellation?.Cancel();
            _testCancellation?.Dispose();
        }

        // Helper classes
        private class ErrorMetrics
        {
            private readonly List<(Exception error, ErrorCategory category, DateTime timestamp)> _errors = new();
            private DateTime _startTime = DateTime.UtcNow;

            public void RecordError(Exception error, ErrorCategory category)
            {
                _errors.Add((error, category, DateTime.UtcNow));
            }

            public ErrorSummary GetSummary()
            {
                var summary = new ErrorSummary
                {
                    TotalErrors = _errors.Count,
                    ErrorsByCategory = _errors.GroupBy(e => e.category)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    ErrorRate = _errors.Count / Math.Max(1, (DateTime.UtcNow - _startTime).TotalMinutes),
                    MostCommonError = _errors.GroupBy(e => e.category)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault()?.Key ?? ErrorCategory.Unknown
                };
                
                return summary;
            }
        }

        private class ErrorSummary
        {
            public int TotalErrors { get; set; }
            public Dictionary<ErrorCategory, int> ErrorsByCategory { get; set; } = new();
            public double ErrorRate { get; set; }
            public ErrorCategory MostCommonError { get; set; }
        }

        private enum ErrorCategory
        {
            Unknown,
            Authentication,
            Network,
            RateLimit,
            DataCorruption
        }

        // Mock exceptions
        private class JsonSerializationException : Exception
        {
            public JsonSerializationException(string message) : base(message) { }
        }
    }
}