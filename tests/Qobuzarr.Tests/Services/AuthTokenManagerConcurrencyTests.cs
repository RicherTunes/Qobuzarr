using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Services;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Services;

/// <summary>
/// Concurrency contract for AuthTokenManager.RefreshTokenInternalAsync.
/// Wave 37 hoisted the check-and-set on _isRefreshing into _tokenLock so the
/// inner refresh method is correct under concurrent entry, even if callers
/// bypass the outer _refreshSemaphore. These tests pin that contract.
/// </summary>
public sealed class AuthTokenManagerConcurrencyTests
{
    [Fact]
    public async Task GetValidTokenAsync_ConcurrentCallers_AreSerializedByRefreshSemaphore()
    {
        // Cold-start scenario: 16 threads all need a token simultaneously, none
        // forcing refresh. Only ONE should actually trigger AuthenticateAsync —
        // everyone else acquires the semaphore after, sees the token is now valid
        // via the double-check, and returns it.
        var auth = new CountingAuthService(delayBeforeReturn: TimeSpan.FromMilliseconds(150));
        var mgr = new AuthTokenManager(auth, LogManager.CreateNullLogger());

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => mgr.GetValidTokenAsync(forceRefresh: false)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Every caller got a non-null token.
        Assert.All(results, t => Assert.False(string.IsNullOrEmpty(t)));
        // All callers got the SAME token (no double-refresh fan-out).
        Assert.Single(results.Distinct());
        // AuthenticateAsync was hit exactly once.
        Assert.Equal(1, auth.CallCount);
    }

    [Fact]
    public async Task RefreshSlot_IsReleasedAfterFailure_AndAllowsLaterRefresh()
    {
        // After a failure we must clear _isRefreshing so subsequent callers can refresh.
        // If the cleanup were inside the catch-only path, a refresh failure would lock
        // the manager into a "refresh-in-progress" state forever.
        var auth = new ScriptedAuthService();
        auth.NextResultIs(throwException: true);
        var mgr = new AuthTokenManager(auth, LogManager.CreateNullLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() => mgr.GetValidTokenAsync(forceRefresh: true));

        // Slot must be released — next call gets a fresh chance.
        auth.NextResultIs(throwException: false, token: "recovered");
        var ok = await mgr.GetValidTokenAsync(forceRefresh: true);
        Assert.Equal("recovered", ok);
    }

    private sealed class CountingAuthService : IQobuzAuthService
    {
        private readonly TimeSpan _delay;
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);

        public CountingAuthService(TimeSpan delayBeforeReturn) => _delay = delayBeforeReturn;

        public async Task<AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new AuthResult
            {
                Token = "token-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                ExpiryTime = DateTime.UtcNow.AddHours(1),
                UserId = "u-1",
                UserType = "premium",
            };
        }
    }

    private sealed class ScriptedAuthService : IQobuzAuthService
    {
        private bool _shouldThrow;
        private string _nextToken = "default";

        public void NextResultIs(bool throwException, string token = "")
        {
            _shouldThrow = throwException;
            if (!string.IsNullOrEmpty(token)) _nextToken = token;
        }

        public Task<AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            if (_shouldThrow)
                throw new InvalidOperationException("scripted failure");
            return Task.FromResult(new AuthResult
            {
                Token = _nextToken,
                ExpiryTime = DateTime.UtcNow.AddHours(1),
                UserId = "u-1",
                UserType = "premium",
            });
        }
    }
}
