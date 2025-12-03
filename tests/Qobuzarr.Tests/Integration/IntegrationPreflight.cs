using System;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Centralized preflight guard for integration tests in Qobuzarr.Tests project.
    /// This variant avoids external dependencies; relies on environment variables being set.
    /// </summary>
    public static class IntegrationPreflight
    {
        public static void RequireLiveIntegrationOrSkip(ITestOutputHelper output)
        {
            var flag = Environment.GetEnvironmentVariable("ENABLE_LIVE_INTEGRATION_TESTS");
            if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Skip("Skipping: Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true)");
            }
        }

        public static void RequireQobuzCredentialsOrSkip(ITestOutputHelper output)
        {
            RequireLiveIntegrationOrSkip(output);

            var appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
            var email = Environment.GetEnvironmentVariable("QOBUZ_EMAIL") ?? Environment.GetEnvironmentVariable("QOBUZ_USERNAME");
            var password = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");
            var userId = Environment.GetEnvironmentVariable("QOBUZ_USER_ID");
            var userToken = Environment.GetEnvironmentVariable("QOBUZ_USER_AUTH_TOKEN");

            var hasUserPass = !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password);
            var hasUserToken = !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(userToken);

            if (string.IsNullOrWhiteSpace(appId) || (!hasUserPass && !hasUserToken))
            {
                Assert.Skip("Skipping: Qobuz credentials not configured (set QOBUZ_APP_ID and either EMAIL/PASSWORD or USER_ID/USER_AUTH_TOKEN)");
            }
        }
    }
}

