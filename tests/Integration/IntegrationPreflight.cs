using System;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests
{
    /// <summary>
    /// Centralized preflight guard for live/integration tests.
    /// Ensures tests only run when explicitly enabled and configured.
    /// </summary>
    public static class IntegrationPreflight
    {
        private static bool _envLoaded;

        private static void TryLoadEnv(ITestOutputHelper output)
        {
            if (_envLoaded) return;
            try
            {
                // Load nearest .env if available (tests/Integration has DotNetEnv)
                DotNetEnv.Env.TraversePath().Load();
                _envLoaded = true;
                output?.WriteLine("[Preflight] Loaded environment from .env if present.");
            }
            catch (Exception ex)
            {
                output?.WriteLine($"[Preflight] Unable to load .env: {ex.Message}");
            }
        }

        /// <summary>
        /// Require explicit opt-in via ENABLE_LIVE_INTEGRATION_TESTS=true
        /// </summary>
        public static void RequireLiveIntegrationOrSkip(ITestOutputHelper output)
        {
            TryLoadEnv(output);
            var flag = Environment.GetEnvironmentVariable("ENABLE_LIVE_INTEGRATION_TESTS");
            if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Skip("Skipping: Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true)");
            }
        }

        /// <summary>
        /// Require Lidarr target configuration for tests that call Lidarr HTTP endpoints
        /// </summary>
        public static void RequireLidarrConfiguredOrSkip(ITestOutputHelper output)
        {
            RequireLiveIntegrationOrSkip(output);
            TryLoadEnv(output);

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var lidarrUrl = config["LIDARR_URL"];
            var lidarrApiKey = config["LIDARR_API_KEY"];

            if (string.IsNullOrWhiteSpace(lidarrUrl) || string.IsNullOrWhiteSpace(lidarrApiKey))
            {
                Assert.Skip("Skipping: Lidarr not configured (set LIDARR_URL and LIDARR_API_KEY)");
            }
        }

        /// <summary>
        /// Require Qobuz credentials for tests that call Qobuz endpoints
        /// </summary>
        public static void RequireQobuzCredentialsOrSkip(ITestOutputHelper output)
        {
            RequireLiveIntegrationOrSkip(output);
            TryLoadEnv(output);

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

