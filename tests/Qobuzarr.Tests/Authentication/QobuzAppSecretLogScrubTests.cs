using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Moq;
using NSubstitute;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Localization;
using NzbDrone.Common.Cache;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Authentication
{
    /// <summary>
    /// Characterization tests defending the Wave-23 security fix (commit 45e240b).
    ///
    /// Before Wave-23, <c>ExtractAppSecretFromBundle</c> logged raw <c>seed</c>,
    /// <c>info</c>, and <c>extras</c> strings at Debug level. Those three values
    /// concatenate (with trim + base64 decode) into the Qobuz appSecret, meaning
    /// any process that captures Debug logs can reconstruct the secret offline.
    ///
    /// The fix replaced raw-value logging with length-only logging
    /// (e.g., <c>Found seed (len=25), timezone: America</c>).
    ///
    /// These tests sentinel-inject known strings into the bundle, capture all NLog
    /// output, and assert the sentinels never appear in any log line.
    /// If a future refactor accidentally reintroduces <c>_logger.Debug($"...{seed}...")</c>
    /// patterns, these tests will catch it immediately.
    ///
    /// <b>Approach chosen: Option A (reflection).</b>
    /// <c>ExtractAppSecretFromBundle</c> is private. The codebase already uses
    /// reflection for private method testing (see <c>CloneSession</c> tests in
    /// <c>QobuzAuthenticationServiceCovTests</c>), and it keeps production code
    /// untouched. Reflection is also more direct than driving through a public
    /// entry-point (Option B), which would require HTTP mocking for the full
    /// <c>GetDynamicCredentialsAsync</c> stack.
    ///
    /// <b>Bundle construction note:</b>
    /// The seed regex is: <c>\):[a-z]\.initialSeed\("(?&lt;seed&gt;.*?)",window\.utimezone\.(?&lt;timezone&gt;[a-z]+)\)</c>
    /// The infoExtras regex is: <c>name:"[^"]*/&lt;Timezone&gt;",info:"(?&lt;info&gt;[^"]*)",extras:"(?&lt;extras&gt;[^"]*)"</c>
    /// (timezone title-cased by production code before building the pattern).
    /// The test bundles are crafted to satisfy both regexes so both logging
    /// statements fire before any base64-decode failure terminates the method.
    /// </summary>
    [Collection(Qobuzarr.Tests.Collections.AuthenticationTestCollection.Name)]
    public sealed class QobuzAppSecretLogScrubTests : IDisposable
    {
        // -----------------------------------------------------------------------
        // Sentinel values — unique strings that MUST NOT appear in any log line.
        // They are chosen to be non-base64 (contain underscores and digits) so
        // that even if they slip into a log message the base64 decode step would
        // fail, rather than accidentally producing a valid secret.
        // -----------------------------------------------------------------------
        private const string SeedSentinel   = "SEED_SENTINEL_DEADBEEF_42";
        private const string InfoSentinel   = "INFO_SENTINEL_CAFEBABE";
        private const string ExtrasSentinel = "EXTRAS_SENTINEL_F00BA22";

        // -----------------------------------------------------------------------
        // Bundle fragments — each constant satisfies the relevant regex capture
        // group while embedding the sentinel as the captured value.
        //
        // Seed pattern:  ):x.initialSeed("<seed>",window.utimezone.<timezone>)
        //   - x = single lowercase letter (here 'x')
        //   - timezone = lowercase letters (here 'america' -> TitleCase -> 'America')
        //
        // InfoExtras pattern: name:"<prefix>/America",info:"<info>",extras:"<extras>"
        // -----------------------------------------------------------------------

        /// <summary>
        /// A minimal bundle that satisfies BOTH the seed+timezone regex AND the
        /// info+extras regex for the 'america' timezone.  Both logging statements
        /// on lines 749 and 768 of QobuzAuthenticationService fire before the
        /// base64 decode attempt.  The sentinel values are embedded as the exact
        /// captured group values.
        /// </summary>
        private const string BothSentinelBundle =
            // seed match: ):x.initialSeed("SEED_SENTINEL_DEADBEEF_42",window.utimezone.america)
            "):x.initialSeed(\"" + SeedSentinel + "\",window.utimezone.america)" +
            // info+extras match (timezone='America' after title-casing 'america')
            " name:\"config/America\",info:\"" + InfoSentinel + "\",extras:\"" + ExtrasSentinel + "\"";

        /// <summary>
        /// A bundle where the seed regex matches (triggering the seed-length log)
        /// but the info/extras pattern deliberately fails (wrong timezone literal so
        /// no match for 'America').  This drives the failure path through
        /// <c>InvalidOperationException("Failed to find info and extras...")</c>.
        /// </summary>
        private const string SeedOnlyBundle =
            // seed match — 'chicago' title-cases to 'Chicago', not 'America'
            "):x.initialSeed(\"" + SeedSentinel + "\",window.utimezone.chicago)" +
            // info+extras for a DIFFERENT timezone — intentionally won't match 'Chicago'
            // (the pattern looks for /Chicago but this has /America, so no match)
            " name:\"config/America\",info:\"" + InfoSentinel + "\",extras:\"" + ExtrasSentinel + "\"";

        // -----------------------------------------------------------------------
        // Infrastructure — a real NLog Logger wired to TestLogger's MemoryTarget,
        // plus a QobuzAuthenticationService instance using the internal test seam.
        // -----------------------------------------------------------------------
        private readonly Logger _logger;
        private readonly QobuzAuthenticationService _service;
        private readonly MethodInfo _extractMethod;
        private readonly string _sessionFilePath;

        public QobuzAppSecretLogScrubTests()
        {
            // Wire up a real NLog Logger backed by the in-memory MemoryTarget so
            // that TestLogger.GetLoggedMessages() captures every log event
            // produced by _service during the test.
            _logger = TestLogger.Create("QobuzAppSecretLogScrubTests");
            TestLogger.ClearLoggedMessages();

            // Per-instance session file path (same pattern as the other auth test
            // classes) prevents cross-test file-system races.
            _sessionFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"qobuzarr-scrub-test-{Guid.NewGuid():N}.session.json");

            // Minimal mocks: only what the constructor strictly requires.
            var mockHttpClient   = new Mock<IHttpClient>();
            var mockConfig       = new Mock<NzbDrone.Core.Configuration.IConfigService>();
            var mockLocalization = new Mock<ILocalizationService>();

            var cacheManager = Substitute.For<ICacheManager>();
            var sessionCache = Substitute.For<ICached<QobuzSession>>();
            cacheManager.GetCache<QobuzSession>(Arg.Any<Type>()).Returns(sessionCache);

            _service = new QobuzAuthenticationService(
                mockHttpClient.Object,
                mockConfig.Object,
                mockLocalization.Object,
                cacheManager,
                _logger,
                credentialValidator: null,
                sessionFilePath: _sessionFilePath);

            // Grab the private method once so all three tests share the lookup.
            _extractMethod = typeof(QobuzAuthenticationService)
                .GetMethod(
                    "ExtractAppSecretFromBundle",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    "ExtractAppSecretFromBundle not found via reflection — " +
                    "was it renamed or made public?");
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.File.Exists(_sessionFilePath))
                    System.IO.File.Delete(_sessionFilePath);
            }
            catch
            {
                // test cleanup is best-effort
            }
        }

        // -----------------------------------------------------------------------
        // Helper
        // -----------------------------------------------------------------------

        /// <summary>
        /// Invokes ExtractAppSecretFromBundle via reflection, ignoring any
        /// TargetInvocationException (which wraps exceptions thrown by the method
        /// itself).  The test only cares about what was logged, not about whether
        /// the method succeeded or threw internally.
        /// </summary>
        private void InvokeExtract(string bundle)
        {
            try
            {
                _extractMethod.Invoke(_service, new object[] { bundle });
            }
            catch (TargetInvocationException)
            {
                // swallow — the method's internal catch-all already logged the
                // exception; we're only auditing log output
            }
        }

        // -----------------------------------------------------------------------
        // Test 1 — seed sentinel must never appear in any log line
        // -----------------------------------------------------------------------

        [Fact]
        public void ExtractAppSecret_OnSuccess_NeverLogsRawSeed()
        {
            // Arrange
            TestLogger.ClearLoggedMessages();

            // Act — drive through the full bundle; both regex matches fire so
            // both Debug statements (lines 749 and 768) execute before any
            // base64 decode exception.
            InvokeExtract(BothSentinelBundle);

            // Assert
            var logLines = TestLogger.GetLoggedMessages();

            // The sentinel value MUST NOT appear in any log line.
            logLines.Should().NotContain(
                line => line.Contains(SeedSentinel, StringComparison.Ordinal),
                because: "Wave-23 fix replaced raw seed logging with length-only; " +
                         "raw seed value must never reach the log sink");

            // The safe length-only format SHOULD be present (positive contract).
            logLines.Should().Contain(
                line => line.Contains("len=", StringComparison.Ordinal),
                because: "the fixed code logs 'len=<n>' instead of the raw seed value");
        }

        // -----------------------------------------------------------------------
        // Test 2 — info and extras sentinels must never appear in any log line
        // -----------------------------------------------------------------------

        [Fact]
        public void ExtractAppSecret_OnSuccess_NeverLogsRawInfoOrExtras()
        {
            // Arrange
            TestLogger.ClearLoggedMessages();

            // Act — same bundle, drives both seed+timezone AND info+extras
            // regex matches so the Debug statement at line 768 fires.
            InvokeExtract(BothSentinelBundle);

            // Assert — neither sentinel may appear in any log line
            var logLines = TestLogger.GetLoggedMessages();

            logLines.Should().NotContain(
                line => line.Contains(InfoSentinel, StringComparison.Ordinal),
                because: "Wave-23 fix replaced raw info logging with length-only; " +
                         "raw info value must never reach the log sink");

            logLines.Should().NotContain(
                line => line.Contains(ExtrasSentinel, StringComparison.Ordinal),
                because: "Wave-23 fix replaced raw extras logging with length-only; " +
                         "raw extras value must never reach the log sink");

            // Positive contract: both length markers must appear in the same line
            // or across two Debug lines.
            var joinedLog = string.Join("\n", logLines);
            joinedLog.Should().Contain("len=",
                because: "safe length-only logging must be present");
        }

        // -----------------------------------------------------------------------
        // Test 3 — partial-match failure path must not leak the seed sentinel
        // -----------------------------------------------------------------------

        [Fact]
        public void ExtractAppSecret_OnFailure_DoesNotLeakPartialMatchValues()
        {
            // Arrange — SeedOnlyBundle: the seed regex matches (triggering the
            // seed-length log at line 749) but the info/extras regex fails
            // because the timezone is 'Chicago' and no name:"..."/Chicago"
            // entry is present.  The method then throws
            // InvalidOperationException("Failed to find info and extras for
            // timezone Chicago in bundle.js") and the catch-all at line 790
            // fires: _logger.Error(ex, "Failed to extract app secret…").
            TestLogger.ClearLoggedMessages();

            InvokeExtract(SeedOnlyBundle);

            var logLines = TestLogger.GetLoggedMessages();

            // The seed sentinel captured before the failure must NOT appear.
            logLines.Should().NotContain(
                line => line.Contains(SeedSentinel, StringComparison.Ordinal),
                because: "even on the partial-match failure path, the already-captured " +
                         "seed value must not be logged; only its length is safe to emit");

            // The error log line must not contain the sentinel either — the
            // exception message embeds the timezone name, not the seed value.
            logLines.Should().NotContain(
                line => line.Contains(SeedSentinel, StringComparison.Ordinal) &&
                        line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase),
                because: "the catch-all Error log must not expose the seed sentinel");
        }
    }
}
