using System;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests
{
    /// <summary>
    /// Exception thrown by framework initialization when preconditions are not met.
    /// Caught by <see cref="IntegrationTestBase"/> and converted to Assert.Skip().
    /// </summary>
    /// <remarks>
    /// This is internal to the integration test infrastructure. Test methods should
    /// call <see cref="IntegrationTestBase.SkipIfNotReady"/> rather than catching this directly.
    /// </remarks>
    public class IntegrationTestSkipException : Exception
    {
        public IntegrationTestSkipException(string message) : base(message) { }
    }

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
        /// Checks if live integration tests are enabled. Returns false if tests should be skipped.
        /// </summary>
        public static bool IsLiveIntegrationEnabled(ITestOutputHelper output)
        {
            TryLoadEnv(output);
            var flag = Environment.GetEnvironmentVariable("ENABLE_LIVE_INTEGRATION_TESTS");
            var enabled = string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
            {
                output?.WriteLine("⏭️ Skipping: Live integration tests are disabled (set ENABLE_LIVE_INTEGRATION_TESTS=true)");
            }
            return enabled;
        }

        /// <summary>
        /// Checks if Lidarr is configured. Returns false if tests should be skipped.
        /// </summary>
        public static bool IsLidarrConfigured(ITestOutputHelper output)
        {
            if (!IsLiveIntegrationEnabled(output)) return false;
            TryLoadEnv(output);

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var lidarrUrl = config["LIDARR_URL"];
            var lidarrApiKey = config["LIDARR_API_KEY"];

            var configured = !string.IsNullOrWhiteSpace(lidarrUrl) && !string.IsNullOrWhiteSpace(lidarrApiKey);
            if (!configured)
            {
                output?.WriteLine("⏭️ Skipping: Lidarr not configured (set LIDARR_URL and LIDARR_API_KEY)");
            }
            return configured;
        }

        /// <summary>
        /// Checks if Qobuz credentials are configured. Returns false if tests should be skipped.
        /// </summary>
        public static bool HasQobuzCredentials(ITestOutputHelper output)
        {
            if (!IsLiveIntegrationEnabled(output)) return false;
            TryLoadEnv(output);

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

        public static void RequireLidarrConfiguredOrSkip(ITestOutputHelper output)
        {
            if (!IsLidarrConfigured(output))
                throw new IntegrationTestSkipException("Skipping: Lidarr not configured (set LIDARR_URL and LIDARR_API_KEY)");
        }

        public static void RequireQobuzCredentialsOrSkip(ITestOutputHelper output)
        {
            if (!HasQobuzCredentials(output))
                throw new IntegrationTestSkipException("Skipping: Qobuz credentials not configured");
        }
    }
}

