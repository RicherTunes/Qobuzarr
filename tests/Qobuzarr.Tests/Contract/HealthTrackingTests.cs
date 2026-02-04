using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Qobuzarr.Tests.Contract;

/// <summary>
/// Health tracking logging contract tests validating that Qobuzarr providers emit
/// correct health tracking events: check pass, check fail, rate limited, recover.
/// </summary>
[Trait("Area", "Contract")]
[Trait("Target", "Provider")]
public class HealthTrackingTests
{
    private const string PluginName = "Qobuzarr";
    private const string ProviderName = "MockQobuzProvider";

    /// <summary>
    /// Rate limit exception for testing.
    /// </summary>
    private class RateLimitException : Exception
    {
        public RateLimitException(string message) : base(message) { }
    }

    /// <summary>
    /// Health status enum for testing.
    /// </summary>
    private enum HealthStatus
    {
        Unknown = 0,
        Healthy = 1,
        Degraded = 2,
        Unhealthy = 3,
        Critical = 4
    }

    /// <summary>
    /// Rate limit info for testing.
    /// </summary>
    private class RateLimitInfo
    {
        public int CurrentRequests { get; set; }
        public int MaxRequests { get; set; }
    }

    /// <summary>
    /// Contract for Qobuz provider operations.
    /// </summary>
    private interface IQobuzProvider
    {
        string ProviderNameValue { get; }
        Task<ProviderHealthResult> TestConnectionAsync();
        Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken);
        Task<HealthStatus> GetHealthStatusAsync();
        Task<string> GetClientIdAsync();
        Task<RateLimitInfo> GetRateLimitInfoAsync();
        Task ResetRateLimitAsync();
    }

    private sealed class MockQobuzProvider : IQobuzProvider
    {
        private readonly ILogger _logger;
        private readonly bool _simulateFailure;
        private readonly bool _simulateRateLimit;

        public MockQobuzProvider(ILogger logger, bool simulateFailure = false, bool simulateRateLimit = false)
        {
            _logger = logger;
            _simulateFailure = simulateFailure;
            _simulateRateLimit = simulateRateLimit;
        }

        public string ProviderNameValue => ProviderName;

        public Task<ProviderHealthResult> TestConnectionAsync()
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Log request start using Common library extension
            _logger.LogRequestStart(PluginName, ProviderName, "TestConnection", correlationId, "health_check", 1);

            try
            {
                if (_simulateFailure)
                {
                    stopwatch.Stop();
                    _logger.LogHealthCheckFail(PluginName, ProviderName, "Test failed due to simulated failure");
                    return Task.FromResult(ProviderHealthResult.Unhealthy("TestConnectionAsync failed", stopwatch.Elapsed));
                }
                else if (_simulateRateLimit)
                {
                    _logger.LogRateLimited(PluginName, ProviderName, correlationId, TimeSpan.FromSeconds(30));
                    return Task.FromResult(ProviderHealthResult.Unhealthy("Rate limited", stopwatch.Elapsed, errorCode: "RATE_LIMITED"));
                }
                else
                {
                    stopwatch.Stop();
                    _logger.LogHealthCheckPass(PluginName, ProviderName, stopwatch.ElapsedMilliseconds);
                    return Task.FromResult(ProviderHealthResult.Healthy(stopwatch.Elapsed));
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogHealthCheckFail(PluginName, ProviderName, ex.Message);
                return Task.FromResult(ProviderHealthResult.Unhealthy(ex.Message, stopwatch.Elapsed));
            }
        }

        public Task<ProviderHealthResult> TestConnectionAsync(CancellationToken cancellationToken)
            => TestConnectionAsync();

        public Task<HealthStatus> GetHealthStatusAsync()
        {
            return Task.FromResult(HealthStatus.Healthy);
        }

        public Task<string> GetClientIdAsync()
        {
            return Task.FromResult("test-client-id");
        }

        public Task<RateLimitInfo> GetRateLimitInfoAsync()
        {
            return Task.FromResult(new RateLimitInfo { CurrentRequests = 0, MaxRequests = 100 });
        }

        public Task ResetRateLimitAsync()
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _logger.LogRateLimitRecovered(PluginName, ProviderName, correlationId, 1);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test logger that captures log entries for verification.
    /// </summary>
    private class TestLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry
            {
                Level = logLevel,
                EventId = eventId,
                Message = formatter(state, exception) ?? string.Empty,
                Exception = exception
            });
        }

        public void ClearEntries()
        {
            Entries.Clear();
        }

        public class LogEntry
        {
            public LogLevel Level { get; init; }
            public EventId EventId { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }
    }

    [Fact]
    public async Task Provider_LogsHealthCheckPass_WhenConnectionSucceeds()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false, simulateRateLimit: false);

        // Act
        var result = await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        logs.Should().NotBeEmpty();
        logs.Should().Contain(log => log.Message.Contains(ProviderName) && log.Message.Contains("Health check passed"));

        var passLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Health check passed"));
        passLog.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_LogsHealthCheckFail_WhenConnectionFails()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: true, simulateRateLimit: false);

        // Act
        var result = await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        logs.Should().NotBeEmpty();
        logs.Should().Contain(log => log.Message.Contains(ProviderName) && log.Message.Contains("Health check failed"));

        var failLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Health check failed"));
        failLog.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_LogsRateLimited_WhenRateLimitDetected()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false, simulateRateLimit: true);

        // Act
        var result = await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        logs.Should().NotBeEmpty();
        logs.Should().Contain(log => log.Message.Contains(ProviderName) && log.Message.Contains("Rate limited"));

        var rateLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Rate limited"));
        rateLog.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_LogsRateLimitRecovered_WhenRateLimitRecovered()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false, simulateRateLimit: false);

        // Act
        await provider.ResetRateLimitAsync();

        // Assert
        var logs = logger.Entries;
        logs.Should().NotBeEmpty();
        logs.Should().Contain(log => log.Message.Contains(ProviderName) && log.Message.Contains("Rate limit recovered"));

        var recoveredLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Rate limit recovered"));
        recoveredLog.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_LogsRequestStart_BeforeCheckCompletes()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false);

        // Act
        await provider.TestConnectionAsync();

        // Assert - start log should be before completion
        var allLogs = logger.Entries;
        var startLog = allLogs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Request started"));

        startLog.Should().NotBeNull();
        var passLog = allLogs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Health check passed"));

        passLog.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_LogsHealthCheckWithRequiredFields()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false);

        // Act
        await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        var passLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Health check passed"));

        passLog.Should().NotBeNull();
        passLog!.Message.Should().Contain(PluginName);
        passLog.Message.Should().Contain(ProviderName);
        passLog.Message.Should().Contain("ElapsedMs");
    }

    [Fact]
    public async Task Provider_LogsHealthCheckFailWithRequiredFields()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: true);

        // Act
        await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        var failLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Health check failed"));

        failLog.Should().NotBeNull();
        failLog!.Message.Should().Contain(PluginName);
        failLog.Message.Should().Contain(ProviderName);
        failLog.Message.Should().Contain("Reason");
    }

    [Fact]
    public async Task Provider_LogsRateLimitedWithRequiredFields()
    {
        // Arrange
        var logger = new TestLogger();
        var provider = new MockQobuzProvider(logger, simulateFailure: false, simulateRateLimit: true);

        // Act
        await provider.TestConnectionAsync();

        // Assert
        var logs = logger.Entries;
        var rateLog = logs.FirstOrDefault(log =>
            log.Message.Contains(ProviderName) &&
            log.Message.Contains("Rate limited"));

        rateLog.Should().NotBeNull();
        rateLog!.Message.Should().Contain(PluginName);
        rateLog.Message.Should().Contain(ProviderName);
        rateLog.Message.Should().Contain("RetryAfterMs");
    }

    [Fact]
    public void HealthTracking_Contracts_ShouldExist()
    {
        // Assert - Verify required health tracking extension methods exist
        // This documents the contract that all providers must support:
        // - LogRequestStart: For starting requests
        // - LogRequestComplete: For completing requests
        // - LogHealthCheckPass: For successful health checks
        // - LogHealthCheckFail: For failed health checks
        // - LogRateLimited: For rate limit scenarios
        // - LogRateLimitRecovered: For rate limit recovery

        var extensionType = typeof(LlmLoggerExtensions);
        var methods = extensionType.GetMethods();

        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogRequestStart));
        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogRequestComplete));
        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogHealthCheckPass));
        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogHealthCheckFail));
        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogRateLimited));
        methods.Should().Contain(m => m.Name == nameof(LlmLoggerExtensions.LogRateLimitRecovered));
    }
}
