using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Targets;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using Xunit;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Observability
{
    /// <summary>
    /// Smoke tests verifying Common v1.10.0 observability adoption:
    /// PluginLogContext scopes are pushed/cleared, and Scrub.Secret/Scrub.Url
    /// redact sensitive values before they reach log writers.
    /// </summary>
    public class ObservabilityAdoptionTests
    {
        // ------------------------------------------------------------------ //
        // Test 1: PluginLogContext is cleared after FetchReleases scope exits
        // ------------------------------------------------------------------ //

        [Fact]
        public void PluginLogContext_Push_ClearsAfterDispose()
        {
            // Arrange — verify the pattern used in QobuzIndexer.FetchReleases
            PluginLogContext.Current.Should().BeNull("no scope should be active at test start");

            // Act
            using (var ctx = PluginLogContext.Push("Qobuzarr", "Search", provider: "qobuz:api"))
            {
                // Assert inside scope
                PluginLogContext.Current.Should().NotBeNull();
                PluginLogContext.Current!.Operation.Should().Be("Search");
                PluginLogContext.Current.PluginName.Should().Be("Qobuzarr");
                PluginLogContext.Current.Provider.Should().Be("qobuz:api");
                PluginLogContext.Current.CorrelationId.Should().NotBeNullOrWhiteSpace();
                PluginLogContext.Current.LinePrefix().Should().MatchRegex(@"^\[Search:[a-f0-9]+:qobuz:api\] $");
            }

            // Assert scope is cleaned up
            PluginLogContext.Current.Should().BeNull("scope must be popped after Dispose");
        }

        [Fact]
        public void PluginLogContext_NestedScopes_StackCorrectly()
        {
            // Verifies nesting works — outer scope restored when inner disposes
            using (var outer = PluginLogContext.Push("Qobuzarr", "Download"))
            {
                var outerCorr = PluginLogContext.Current!.CorrelationId;

                using (var inner = PluginLogContext.Push("Qobuzarr", "AuthRefresh"))
                {
                    PluginLogContext.Current!.Operation.Should().Be("AuthRefresh");
                    PluginLogContext.Current.CorrelationId.Should().NotBe(outerCorr,
                        "each Push generates its own correlation ID");
                }

                // Inner disposed — outer restored
                PluginLogContext.Current.Should().NotBeNull();
                PluginLogContext.Current!.Operation.Should().Be("Download");
                PluginLogContext.Current.CorrelationId.Should().Be(outerCorr);
            }

            PluginLogContext.Current.Should().BeNull();
        }

        [Fact]
        public async Task PluginLogContext_AsyncLocal_IsolatedAcrossAsyncPaths()
        {
            // Prove AsyncLocal semantics: sibling async paths don't bleed into each other
            var task1 = Task.Run(async () =>
            {
                using var ctx = PluginLogContext.Push("Qobuzarr", "Search");
                await Task.Delay(10);
                return PluginLogContext.Current?.Operation;
            });

            var task2 = Task.Run(async () =>
            {
                using var ctx = PluginLogContext.Push("Qobuzarr", "Download");
                await Task.Delay(10);
                return PluginLogContext.Current?.Operation;
            });

            var results = await Task.WhenAll(task1, task2);
            results.Should().Contain("Search");
            results.Should().Contain("Download");
        }

        // ------------------------------------------------------------------ //
        // Test 2: Scrub.Secret redacts API keys as expected
        // ------------------------------------------------------------------ //

        [Theory]
        [InlineData("abcdefghij", "abc***")]
        [InlineData("ab", "***")]           // shorter than leadingVisible → all redacted
        [InlineData("", "***")]             // empty → all redacted
        [InlineData(null, "***")]           // null → all redacted
        public void Scrub_Secret_RedactsCorrectly(string? value, string expected)
        {
            Scrub.Secret(value).Should().Be(expected);
        }

        [Fact]
        public void Scrub_Secret_WithCustomLeadingVisible_RedactsCorrectly()
        {
            // 5 leading chars — matches how a Qobuz App ID might be partially exposed
            Scrub.Secret("qobuz-app-id-1234567890", leadingVisible: 5).Should().Be("qobuz***");
        }

        // ------------------------------------------------------------------ //
        // Test 3: Scrub.Url redacts known sensitive query parameters
        // ------------------------------------------------------------------ //

        [Theory]
        [InlineData(
            "https://streaming.qobuz.com/file?format_id=27&user_auth_token=MYTOKEN123",
            "https://streaming.qobuz.com/file?format_id=27&user_auth_token=***")]
        [InlineData(
            // Common v1.13.0+: Scrub.Url delegates to LogRedactor.IsSensitiveParameter which
            // recognizes substrings: secret, password, token, auth, credential, key, apikey.
            // `app_id` is the public client identifier (not a secret — `app_secret` is); keep.
            // `api_key` matches "key" → redact.
            "https://api.qobuz.com/track/getFileUrl?track_id=42&app_id=abc123&api_key=SECRET",
            "https://api.qobuz.com/track/getFileUrl?track_id=42&app_id=abc123&api_key=***")]
        [InlineData(
            "https://api.qobuz.com/album/get?album_id=123",
            "https://api.qobuz.com/album/get?album_id=123")]  // no sensitive params — unchanged
        public void Scrub_Url_RedactsSensitiveQueryParams(string input, string expected)
        {
            Scrub.Url(input).Should().Be(expected);
        }

        [Fact]
        public void Scrub_Url_NullOrEmpty_ReturnsEmpty()
        {
            Scrub.Url(string.Empty).Should().BeEmpty();
        }

        // ------------------------------------------------------------------ //
        // Test 4: LinePrefix format is stable
        // ------------------------------------------------------------------ //

        [Fact]
        public void PluginLogContext_LinePrefix_ContainsOperationAndCorrelationId()
        {
            using var ctx = PluginLogContext.Push("Qobuzarr", "Test");
            var prefix = PluginLogContext.Current!.LinePrefix();
            prefix.Should().StartWith("[Test:");
            prefix.Should().EndWith("] ");
            // No provider — format should be [op:corrId]
            prefix.Should().MatchRegex(@"^\[Test:[a-f0-9]{32}\] $");
        }

        [Fact]
        public void PluginLogContext_LinePrefix_WithProvider_IncludesProvider()
        {
            using var ctx = PluginLogContext.Push("Qobuzarr", "Search", provider: "qobuz:api");
            var prefix = PluginLogContext.Current!.LinePrefix();
            prefix.Should().Contain(":qobuz:api]");
        }
    }
}
