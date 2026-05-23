using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NLog;
using Xunit;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using NSubstitute;

namespace Qobuzarr.Tests.Authentication
{
    /// <summary>
    /// TDD tests for sync-over-async debt in QobuzAuthenticationService.
    ///
    /// TRACKING RED: These tests document Phase 1.1 debt — the three private persist methods
    /// (TryLoadPersistedSession, TryPersistSession, TryClearPersistedSession) call
    /// FileTokenStore.{Load,Save,Clear}Async() via .GetAwaiter().GetResult() inside lock{} blocks.
    ///
    /// Converting these to async would require making the public interface methods
    /// (GetCachedSession, StoreSession, ClearSession) async as well, which ripples into
    /// IQobuzAuthenticationService and all callers. That ripple is out of scope for Phase 1.
    ///
    /// See: docs/SYNC_ASYNC_DEBT.md for the full debt register.
    /// Removal condition: Phase 1.1 converts the persist methods to async and updates callers.
    /// Owner: RicherTunes
    /// </summary>
    public class QobuzAuthenticationServiceAsyncTests : IDisposable
    {
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Mock<IConfigService> _mockConfigService;
        private readonly Mock<ILocalizationService> _mockLocalizationService;
        private readonly ICacheManager _mockCacheManager;
        private readonly Mock<Logger> _mockLogger;
        private readonly Mock<ICredentialValidator> _mockValidator;

        public QobuzAuthenticationServiceAsyncTests()
        {
            _mockHttpClient = new Mock<IHttpClient>();
            _mockConfigService = new Mock<IConfigService>();
            _mockLocalizationService = new Mock<ILocalizationService>();
            _mockLogger = new Mock<Logger>();
            _mockValidator = new Mock<ICredentialValidator>();

            _mockCacheManager = Substitute.For<ICacheManager>();
            var mockSessionCache = Substitute.For<ICached<QobuzSession>>();
            _mockCacheManager.GetCache<QobuzSession>(Arg.Any<Type>()).Returns(mockSessionCache);

            var validResult = new CredentialValidationResult();
            _mockValidator.Setup(x => x.ValidateCredentials(It.IsAny<QobuzCredentials>()))
                .Returns(validResult);
        }

        /// <summary>
        /// Verifies that StoreSession does NOT synchronously block the calling thread when
        /// the token store is slow (200 ms delay). The current implementation WILL block
        /// because it uses .GetAwaiter().GetResult() inside a lock — so this test is
        /// intentionally RED until Phase 1.1 fixes the sync-over-async pattern.
        ///
        /// TRACKING RED — see docs/SYNC_ASYNC_DEBT.md
        /// </summary>
        [Fact(Skip = "TRACKING RED (phase-1.1): StoreSession uses sync-over-async via " +
                     ".GetAwaiter().GetResult() on FileTokenStore.SaveAsync() inside a lock{}. " +
                     "Unskip when TryPersistSession is converted to async Task. " +
                     "See docs/SYNC_ASYNC_DEBT.md for the full debt register.")]
        public async Task StoreSession_WithSlowTokenStore_ShouldNotBlockCallerThread()
        {
            // Arrange: slow fake token store with 200ms delay
            var slowStore = new SlowTokenStore(delayMs: 200);

            // We create the service using reflection to inject the slow store, since
            // the FileTokenStore is constructed internally. This test is a specification
            // of the desired non-blocking behaviour; once the async fix is applied the
            // constructor or a new overload should accept ITokenStore<QobuzSession>.
            var authService = CreateAuthServiceWithStore(slowStore);

            var session = new QobuzSession
            {
                UserId = "123",
                AuthToken = "tok",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            // Act: call the synchronous StoreSession and measure elapsed time on this thread
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // In a truly non-blocking design, StoreSession would fire-and-forget the persist
            // or schedule it on the thread-pool without blocking. We give 50ms budget here.
            await Task.Run(() => authService.StoreSession(session));

            // A Task.Yield allows this thread to observe whether it was unblocked.
            await Task.Yield();
            sw.Stop();

            // If sync-over-async is still present this will be ~200ms; after fix it will be <50ms.
            sw.ElapsedMilliseconds.Should().BeLessThan(50,
                "StoreSession should not synchronously block the calling thread for 200ms " +
                "while awaiting the slow disk persist. See docs/SYNC_ASYNC_DEBT.md.");
        }

        /// <summary>
        /// Mirrors StoreSession test but for ClearSession -> TryClearPersistedSession.
        /// TRACKING RED — see docs/SYNC_ASYNC_DEBT.md
        /// </summary>
        [Fact(Skip = "TRACKING RED (phase-1.1): ClearSession uses sync-over-async. " +
                     "See docs/SYNC_ASYNC_DEBT.md.")]
        public async Task ClearSession_WithSlowTokenStore_ShouldNotBlockCallerThread()
        {
            var slowStore = new SlowTokenStore(delayMs: 200);
            var authService = CreateAuthServiceWithStore(slowStore);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() => authService.ClearSession());
            await Task.Yield();
            sw.Stop();

            sw.ElapsedMilliseconds.Should().BeLessThan(50,
                "ClearSession should not synchronously block the calling thread. " +
                "See docs/SYNC_ASYNC_DEBT.md.");
        }

        /// <summary>
        /// Mirrors the load path: GetCachedSession -> TryLoadPersistedSession.
        /// TRACKING RED — see docs/SYNC_ASYNC_DEBT.md
        /// </summary>
        [Fact(Skip = "TRACKING RED (phase-1.1): GetCachedSession uses sync-over-async on " +
                     "LoadAsync. See docs/SYNC_ASYNC_DEBT.md.")]
        public async Task GetCachedSession_WithSlowTokenStore_ShouldNotBlockCallerThread()
        {
            var slowStore = new SlowTokenStore(delayMs: 200);
            var authService = CreateAuthServiceWithStore(slowStore);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            QobuzSession? result = null;
            await Task.Run(() => result = authService.GetCachedSession());
            await Task.Yield();
            sw.Stop();

            sw.ElapsedMilliseconds.Should().BeLessThan(50,
                "GetCachedSession should not synchronously block the calling thread. " +
                "See docs/SYNC_ASYNC_DEBT.md.");
        }

        private QobuzAuthenticationService CreateAuthServiceWithStore(ITokenStore<QobuzSession> store)
        {
            // Construct via reflection to inject a custom store, bypassing the internal
            // FileTokenStore construction in the real constructor.
            var svc = new QobuzAuthenticationService(
                _mockHttpClient.Object,
                _mockConfigService.Object,
                _mockLocalizationService.Object,
                _mockCacheManager,
                _mockLogger.Object,
                _mockValidator.Object);

            // Inject the slow store via reflection (private field _persistentStore)
            var field = typeof(QobuzAuthenticationService)
                .GetField("_persistentStore",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            field?.SetValue(svc, store);
            return svc;
        }

        public void Dispose() { }

        // ---------------------------------------------------------------------------
        // Fake token store that introduces a configurable async delay
        // ---------------------------------------------------------------------------

        private sealed class SlowTokenStore : ITokenStore<QobuzSession>
        {
            private readonly int _delayMs;

            public SlowTokenStore(int delayMs) => _delayMs = delayMs;

            public async Task<TokenEnvelope<QobuzSession>?> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
                return null;
            }

            public async Task SaveAsync(
                TokenEnvelope<QobuzSession> envelope,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            }

            public async Task ClearAsync(CancellationToken cancellationToken = default)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
