using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NSubstitute;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;
using Xunit;

namespace Qobuzarr.Tests.Integration;

/// <summary>
/// TDD coverage for <see cref="QobuzAuthHealthCheck"/> — the qobuz PILOT that surfaces a
/// latched <see cref="AuthFailureGate"/> in Lidarr's native Health banner
/// (System &gt; Status), instead of the failure being invisible (a search that just
/// silently returns 0 results with no exception, so Lidarr's native
/// <c>IndexerStatusCheck</c> never fires).
///
/// These are PURE unit tests against <see cref="IProvideHealthCheck.Check"/> — no HTTP,
/// no host container. Whether <see cref="IProvideHealthCheck"/> is actually DISCOVERED
/// and INVOKED by Lidarr's DryIoC composition root when loaded from a plugin
/// AssemblyLoadContext is a SEPARATE, unproven question that can only be answered on a
/// real Lidarr host — see the live-validation notes in qobuzarr/CLAUDE.md.
/// </summary>
public class QobuzAuthHealthCheckTests
{
    // ----------------------------------------------------------------
    // Fakes
    // ----------------------------------------------------------------

    /// <summary>
    /// Minimal fake of <see cref="IAuthFailureHandler"/> so tests can construct a REAL
    /// <see cref="AuthFailureGate"/> (the actual production type the health check reads)
    /// in a controlled status, without needing a live Qobuz session or HTTP calls.
    /// </summary>
    private sealed class FakeAuthFailureHandler : IAuthFailureHandler
    {
        public AuthStatus Status { get; set; } = AuthStatus.Unknown;

        public AuthFailure? LastFailure { get; set; }

        public ValueTask HandleFailureAsync(AuthFailure failure, CancellationToken cancellationToken = default)
        {
            LastFailure = failure;
            Status = AuthStatus.Failed;
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleSuccessAsync(CancellationToken cancellationToken = default)
        {
            LastFailure = null;
            Status = AuthStatus.Authenticated;
            return ValueTask.CompletedTask;
        }

        public ValueTask RequestReauthenticationAsync(string reason, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Minimal fake of <see cref="IQobuzApiClient"/> exposing only the members the health
    /// check touches (<see cref="IQobuzApiClient.Gate"/>). Every other member throws if hit,
    /// so an accidental dependency on API/session behavior fails loudly instead of silently
    /// returning a default.
    /// </summary>
    private sealed class FakeQobuzApiClient : IQobuzApiClient
    {
        public AuthFailureGate? Gate { get; set; }

        public Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
            => throw new NotSupportedException("Health check must not touch the API client's network surface.");

        public Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
            => throw new NotSupportedException("Health check must not touch the API client's network surface.");

        public void SetSession(QobuzSession session)
            => throw new NotSupportedException();

        public void ClearSession()
            => throw new NotSupportedException();

        public bool HasValidSession()
            => throw new NotSupportedException();

        public Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static AuthFailureGate CreateGate(AuthStatus status)
    {
        var handler = new FakeAuthFailureHandler { Status = status };
        return new AuthFailureGate(handler);
    }

    private static ILocalizationService CreateLocalizationService()
    {
        // Real Lidarr's ILocalizationService.GetLocalizedString falls back to returning the
        // phrase unchanged when the key isn't in the translation dictionary (see
        // NzbDrone.Core.Localization.LocalizationService.GetLocalizedString) — mirror that
        // fallback behavior here rather than asserting a specific translation key exists.
        var localization = Substitute.For<ILocalizationService>();
        localization.GetLocalizedString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return localization;
    }

    // ----------------------------------------------------------------
    // Latched-bad gate -> Warning
    // ----------------------------------------------------------------

    [Fact]
    public void Check_GateLatchedBad_ReturnsWarningWithActionableMessage()
    {
        var apiClient = new FakeQobuzApiClient { Gate = CreateGate(AuthStatus.Failed) };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        Assert.Equal(HealthCheckResult.Warning, result.Type);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        // Actionable: must tell the user WHERE to go and WHAT to do, not just that something
        // is wrong.
        Assert.Contains("Settings", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Indexer", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_GateExpired_ReturnsWarning()
    {
        var apiClient = new FakeQobuzApiClient { Gate = CreateGate(AuthStatus.Expired) };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        Assert.Equal(HealthCheckResult.Warning, result.Type);
    }

    [Fact]
    public void Check_GateLatchedBad_MessageDoesNotLeakCredentials()
    {
        var handler = new FakeAuthFailureHandler
        {
            Status = AuthStatus.Failed,
            LastFailure = new AuthFailure
            {
                ErrorCode = "401",
                Message = "Bearer eyJhbGciOiJIUzI1NiJ9.super-secret-token-value rejected",
            },
        };
        var gate = new AuthFailureGate(handler);
        var apiClient = new FakeQobuzApiClient { Gate = gate };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        // The health check's own message is a fixed, generic, actionable string — it must
        // NEVER interpolate the raw upstream failure message/token into the banner text
        // Lidarr renders to the user.
        Assert.DoesNotContain("super-secret-token-value", result.Message);
        Assert.DoesNotContain("Bearer", result.Message);
    }

    [Fact]
    public void Check_GateLatchedBad_SourceIsThisCheckType()
    {
        var apiClient = new FakeQobuzApiClient { Gate = CreateGate(AuthStatus.Failed) };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        // HealthCheckService groups/dedupes results by Source.Name — must be this concrete
        // type, not a base type or null.
        Assert.Equal(typeof(QobuzAuthHealthCheck), result.Source);
    }

    // ----------------------------------------------------------------
    // Healthy gate -> Ok
    // ----------------------------------------------------------------

    [Fact]
    public void Check_GateHealthy_ReturnsOk()
    {
        var apiClient = new FakeQobuzApiClient { Gate = CreateGate(AuthStatus.Authenticated) };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        Assert.Equal(HealthCheckResult.Ok, result.Type);
    }

    [Fact]
    public void Check_GateUnknownStatus_ReturnsOk()
    {
        // AuthStatus.Unknown (never latched, e.g. fresh process / no request made yet) must
        // not be treated as a failure.
        var apiClient = new FakeQobuzApiClient { Gate = CreateGate(AuthStatus.Unknown) };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        Assert.Equal(HealthCheckResult.Ok, result.Type);
    }

    // ----------------------------------------------------------------
    // Absent/uninitialized gate -> Ok (safe default; matches the ecosystem convention
    // documented on QobuzIndexer.IsAuthShortCircuited: "a null gate is always healthy")
    // ----------------------------------------------------------------

    [Fact]
    public void Check_GateAbsent_ReturnsOk_DoesNotThrow()
    {
        var apiClient = new FakeQobuzApiClient { Gate = null };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        var result = sut.Check();

        Assert.Equal(HealthCheckResult.Ok, result.Type);
    }

    // ----------------------------------------------------------------
    // Constructor guards
    // ----------------------------------------------------------------

    [Fact]
    public void Constructor_NullApiClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new QobuzAuthHealthCheck(null!, CreateLocalizationService()));
    }

    // ----------------------------------------------------------------
    // CheckOnStartup / CheckOnSchedule defaults
    // ----------------------------------------------------------------

    [Fact]
    public void CheckOnStartup_And_CheckOnSchedule_AreEnabled()
    {
        // Latched auth is exactly the kind of condition that can develop WHILE Lidarr is
        // running (a session expiring mid-uptime) - it must be re-evaluated on the periodic
        // schedule, not just once at startup.
        var apiClient = new FakeQobuzApiClient { Gate = null };
        var sut = new QobuzAuthHealthCheck(apiClient, CreateLocalizationService());

        Assert.True(sut.CheckOnStartup);
        Assert.True(sut.CheckOnSchedule);
    }
}
