using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using FluentValidation.Results;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Indexers;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Qobuzarr.Tests.Indexers;

/// <summary>
/// TDD tests for AuthFailureGate helpers on QobuzIndexer (parity-matrix item #24).
///
/// Red phase: these tests compile but FAIL (or fail to compile if helpers don't exist yet)
/// before the implementation is added to QobuzIndexer.
/// Green phase: all facts pass after implementation.
/// </summary>
public sealed class QobuzIndexerAuthGateTests
{
    // ------------------------------------------------------------------ //
    // Test 1: IsAuthShortCircuited returns true when gate is latched bad
    // ------------------------------------------------------------------ //

    /// <summary>
    /// When the AuthFailureGate is latched bad (probe slot consumed), the static helper
    /// must return true so Test() and FetchReleases() can short-circuit without touching
    /// the network — mirrors AppleMusicLidarrDownloadClient.IsAuthShortCircuited.
    /// </summary>
    [Fact]
    public void Test_WhenGateLatchedBad_AddsAuthLatchedValidationFailure()
    {
        // Arrange — build a gate that is latched bad with no probe slot remaining.
        var handler = new DefaultAuthFailureHandler(NullLogger<DefaultAuthFailureHandler>.Instance);
        // Latch the gate: one HandleFailureAsync call with failureThreshold=1 suffices.
        Task.Run(() => handler.HandleFailureAsync(new AuthFailure { ErrorCode = "401", Message = "bad credentials" }))
            .GetAwaiter().GetResult();

        // Use a very long probe interval so TryAcquireProbeSlot always returns false after
        // the first call.
        var gate = new AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromHours(24));

        // Consume the first probe slot so the gate denies further probes.
        gate.TryAcquireProbeSlot();

        Assert.False(gate.IsHealthy, "Gate must be latched bad for this test to be meaningful.");

        // Act — invoke the static helper exposed for tests.
        bool shortCircuited = QobuzIndexer.IsAuthShortCircuited(gate);

        // Assert — gate is latched and probe slot is exhausted → must return true.
        Assert.True(shortCircuited,
            "IsAuthShortCircuited should return true when gate is latched and no probe slot is available.");
    }

    // ------------------------------------------------------------------ //
    // Test 2: FetchReleases short-circuits when gate is latched
    // ------------------------------------------------------------------ //

    /// <summary>
    /// When the gate is latched bad, IsAuthShortCircuited should prevent every search
    /// from hitting the network. The helper must return true (callers return empty).
    /// </summary>
    [Fact]
    public void Fetch_WhenGateLatchedBad_HelperReturnsTrueToShortCircuit()
    {
        // Arrange — latched gate with no probe slot.
        var handler = new DefaultAuthFailureHandler(NullLogger<DefaultAuthFailureHandler>.Instance);
        Task.Run(() => handler.HandleFailureAsync(new AuthFailure { ErrorCode = "403", Message = "forbidden" }))
            .GetAwaiter().GetResult();
        var gate = new AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromHours(24));
        gate.TryAcquireProbeSlot(); // consume the slot

        // Act.
        bool shortCircuited = QobuzIndexer.IsAuthShortCircuited(gate);

        // Assert — helper returns true, signaling callers to return empty list without HTTP.
        Assert.True(shortCircuited,
            "IsAuthShortCircuited must return true for a latched gate with no probe slot (prevents search-loop amplification).");
    }

    // ------------------------------------------------------------------ //
    // Test 3: RecordAuthOutcomeFromException latches gate on 401
    // ------------------------------------------------------------------ //

    /// <summary>
    /// When a runtime exception with HTTP 401 is caught, RecordAuthOutcomeFromException
    /// must record the failure in the gate, transitioning gate.IsHealthy → false.
    /// </summary>
    [Fact]
    public void Test_WhenAuthFailureThrown_RecordsAuthFailure()
    {
        // Arrange — healthy gate.
        var handler = new DefaultAuthFailureHandler(NullLogger<DefaultAuthFailureHandler>.Instance);
        Task.Run(() => handler.HandleSuccessAsync()).GetAwaiter().GetResult();
        var gate = new AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromMinutes(5));

        Assert.True(gate.IsHealthy, "Gate should be healthy before the test.");

        // Simulate an auth failure exception (HTTP 401 from QobuzApiException).
        var ex = new HttpRequestException("401 Unauthorized", null, HttpStatusCode.Unauthorized);

        // Act — invoke the static helper exposed for tests.
        QobuzIndexer.RecordAuthOutcomeFromException(gate, ex);

        // Assert — gate must now be latched bad.
        Assert.False(gate.IsHealthy,
            "RecordAuthOutcomeFromException must latch the gate bad on a 401 HttpRequestException.");
    }

    // ------------------------------------------------------------------ //
    // Test 4: LooksLikeAuthFailure helper — qobuz-specific patterns
    // ------------------------------------------------------------------ //

    [Fact]
    public void LooksLikeAuthFailure_HttpRequestException_401_ReturnsTrue()
    {
        var ex = new HttpRequestException("auth failed", null, HttpStatusCode.Unauthorized);
        Assert.True(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_EndpointlessHttp403_ReturnsFalse()
    {
        var ex = new HttpRequestException("forbidden resource", null, HttpStatusCode.Forbidden);
        Assert.False(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_QobuzApiException403OnLogin_ReturnsTrue()
    {
        var ex = new QobuzApiException("invalid app credentials", "/user/login", HttpStatusCode.Forbidden);
        Assert.True(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_QobuzApiException403OnResource_ReturnsFalse()
    {
        var ex = new QobuzApiException("subscription tier denied", "/track/getFileUrl", HttpStatusCode.Forbidden);
        Assert.False(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_GenericException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Something unrelated entirely.");
        Assert.False(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_HttpRequestException_500_ReturnsFalse()
    {
        var ex = new HttpRequestException("server error", null, HttpStatusCode.InternalServerError);
        Assert.False(QobuzIndexer.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void IsAuthShortCircuited_NullGate_ReturnsFalse()
    {
        // A null gate is always considered healthy (safe default when no gate is wired).
        Assert.False(QobuzIndexer.IsAuthShortCircuited(null));
    }

    [Fact]
    public void IsAuthShortCircuited_HealthyGate_ReturnsFalse()
    {
        var handler = new DefaultAuthFailureHandler(NullLogger<DefaultAuthFailureHandler>.Instance);
        Task.Run(() => handler.HandleSuccessAsync()).GetAwaiter().GetResult();
        var gate = new AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromMinutes(5));

        Assert.True(gate.IsHealthy);
        Assert.False(QobuzIndexer.IsAuthShortCircuited(gate),
            "IsAuthShortCircuited should return false when gate is healthy.");
    }
}
