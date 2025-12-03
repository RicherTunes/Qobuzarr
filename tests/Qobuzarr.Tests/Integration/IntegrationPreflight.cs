using System;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Exception thrown to skip integration tests when preconditions are not met.
    /// xUnit 2.x doesn't have Assert.Skip, so we use a custom exception that tests can catch.
    /// </summary>
    public class IntegrationTestSkipException : Exception
    {
        public IntegrationTestSkipException(string message) : base(message) { }
    }

    /// <summary>
    /// Centralized preflight guard for integration tests in Qobuzarr.Tests project.
    /// This variant avoids external dependencies; relies on environment variables being set.
    /// </summary>
    public static class IntegrationPreflight
    {
        /// <summary>
        /// Checks if live integration tests are enabled. Returns false if tests should be skipped.
        /// </summary>
        public static bool IsLiveIntegrationEnabled(ITestOutputHelper output)
        {
            var flag = Environment.GetEnvironmentVariable("ENABLE_LIVE_INTEGRATION_TESTS");
            var enabled = string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
            {
                output?.WriteLine("⏭️ Skipping: Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true)");
            }
            return enabled;
        }

        /// <summary>
        /// Checks if Qobuz credentials are configured. Returns false if tests should be skipped.
        /// </summary>
        public static bool HasQobuzCredentials(ITestOutputHelper output)
        {
            if (!IsLiveIntegrationEnabled(output)) return false;

            var appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
            var email = Environment.GetEnvironmentVariable("QOBUZ_EMAIL") ?? Environment.GetEnvironmentVariable("QOBUZ_USERNAME");
            var password = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");
            var userId = Environment.GetEnvironmentVariable("QOBUZ_USER_ID");
            var userToken = Environment.GetEnvironmentVariable("QOBUZ_USER_AUTH_TOKEN");

            var hasUserPass = !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password);
            var hasUserToken = !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(userToken);

            var hasCredentials = !string.IsNullOrWhiteSpace(appId) && (hasUserPass || hasUserToken);
            if (!hasCredentials)
            {
                output?.WriteLine("⏭️ Skipping: Qobuz credentials not configured (set QOBUZ_APP_ID and either EMAIL/PASSWORD or USER_ID/USER_AUTH_TOKEN)");
            }
            return hasCredentials;
        }

        // Legacy methods for backward compatibility - these throw for tests that expect exceptions
        public static void RequireLiveIntegrationOrSkip(ITestOutputHelper output)
        {
            if (!IsLiveIntegrationEnabled(output))
                throw new IntegrationTestSkipException("Skipping: Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true)");
        }

        public static void RequireQobuzCredentialsOrSkip(ITestOutputHelper output)
        {
            if (!HasQobuzCredentials(output))
                throw new IntegrationTestSkipException("Skipping: Qobuz credentials not configured");
        }
    }
}

